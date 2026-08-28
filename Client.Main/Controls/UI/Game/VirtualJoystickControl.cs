using System;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Helpers;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Client.Main.Controls.UI.Game
{
    /// <summary>
    /// 手機用的虛擬搖桿，取代點擊移動。
    ///
    /// MU 的移動協議是「送一條路徑給伺服器」而不是「送方向」，因此這裡不改協議：
    /// 把搖桿方向換算成前方數格的目標格子，再走既有的 <see cref="Objects.WalkerObject.MoveTo"/>。
    /// 為避免每幀灌爆封包，只有在方向明顯改變或角色接近目標時才重新下指令。
    ///
    /// 採「浮動搖桿」設計：手指按在啟用區域的任何位置即以該點為圓心，
    /// 比固定位置更適合單手操作，也不必精準對準。
    /// </summary>
    public class VirtualJoystickControl : UIControl
    {
        /// <summary>搖桿可被喚起的區域（畫面左半、下半），以虛擬座標比例表示。</summary>
        private const float ActivationWidthRatio = 0.45f;
        private const float ActivationTopRatio = 0.35f;

        private const float BaseRadius = 78f;
        private const float KnobRadius = 34f;

        /// <summary>超過這個比例才視為有效輸入，避免手指微抖就走動。</summary>
        private const float DeadZone = 0.18f;

        /// <summary>目標格子距離角色幾格。太短會頻繁重下指令，太長轉向會遲鈍。</summary>
        private const float TargetTileDistance = 4f;

        /// <summary>方向改變超過這個弧度才重新下指令（約 20 度）。</summary>
        private const float DirectionChangeThreshold = 0.35f;

        /// <summary>重新下指令的最短間隔，避免灌爆封包。</summary>
        private const double MinResendSeconds = 0.25;

        private bool _active;
        private Vector2 _center;
        private Vector2 _knob;
        private Vector2 _direction;      // 已正規化；長度 0 表示無輸入
        private float _magnitude;

        private Vector2 _lastSentDirection;
        private double _lastSendTime;

        private Texture2D _pixel;

        /// <summary>搖桿目前是否有有效輸入。</summary>
        public bool IsActive => _active && _magnitude > DeadZone;

        /// <summary>正規化後的方向（螢幕座標系，Y 向下）。</summary>
        public Vector2 Direction => _direction;

        public VirtualJoystickControl()
        {
            Interactive = false;   // 自行處理觸控，不走 UI 的點擊路由
            AutoViewSize = false;
            ViewSize = new Point(1, 1);
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (!Visible)
            {
                _active = false;
                return;
            }

            var mouse = MuGame.Instance.UiMouseState;
            bool pressed = mouse.LeftButton == ButtonState.Pressed;
            var position = new Vector2(mouse.X, mouse.Y);

            if (!pressed)
            {
                _active = false;
                _magnitude = 0f;
                _direction = Vector2.Zero;
                return;
            }

            if (!_active)
            {
                if (!IsInActivationArea(position))
                    return;

                _active = true;
                _center = position;
                _knob = position;
                _lastSentDirection = Vector2.Zero;
            }

            var offset = position - _center;
            float length = offset.Length();

            if (length > BaseRadius)
            {
                offset = offset / length * BaseRadius;
                length = BaseRadius;
            }

            _knob = _center + offset;
            _magnitude = length / BaseRadius;
            _direction = length > 0.0001f ? offset / length : Vector2.Zero;
        }

        /// <summary>
        /// 搖桿只在畫面左下角一帶生效，右側留給鏡頭手勢與技能按鈕。
        /// </summary>
        private static bool IsInActivationArea(Vector2 virtualPosition)
        {
            var size = UiScaler.VirtualSize;
            return virtualPosition.X <= size.X * ActivationWidthRatio
                && virtualPosition.Y >= size.Y * ActivationTopRatio;
        }

        /// <summary>
        /// 是否應該重新對伺服器下移動指令。
        /// 方向明顯改變、或距上次下指令已超過最短間隔時才回傳 true。
        /// </summary>
        public bool ShouldIssueMove(GameTime gameTime, out Vector2 direction)
        {
            direction = _direction;

            if (!IsActive)
                return false;

            double now = gameTime.TotalGameTime.TotalSeconds;
            bool directionChanged = _lastSentDirection == Vector2.Zero
                || Vector2.Dot(_lastSentDirection, _direction) < MathF.Cos(DirectionChangeThreshold);

            if (!directionChanged && now - _lastSendTime < MinResendSeconds)
                return false;

            _lastSentDirection = _direction;
            _lastSendTime = now;
            return true;
        }

        /// <summary>目標格子與角色的距離（格）。</summary>
        public static float TileDistance => TargetTileDistance;

        public override void Draw(GameTime gameTime)
        {
            if (Status != GameControlStatus.Ready || !Visible || !_active)
                return;

            _pixel ??= GraphicsManager.Instance.Pixel;
            if (_pixel == null)
                return;

            var sb = GraphicsManager.Instance.Sprite;
            if (sb == null)
                return;

            // 場景可能已經開好批次；重複 Begin 會失敗，畫面上就什麼都看不到。
            SpriteBatchScope? scope = null;
            if (!SpriteBatchScope.BatchIsBegun)
            {
                scope = new SpriteBatchScope(
                    sb, SpriteSortMode.Deferred, BlendState.AlphaBlend,
                    SamplerState.LinearClamp, transform: UiScaler.SpriteTransform);
            }

            try
            {

            DrawRing(sb, _center, BaseRadius, new Color(255, 255, 255, 46), 3f);
            DrawDisc(sb, _knob, KnobRadius, new Color(255, 255, 255, 92));

            }
            finally
            {
                scope?.Dispose();
            }

            base.Draw(gameTime);
        }

        // 沒有現成的圓形貼圖，用細短線段拼出圓環／圓面。
        // 搖桿只有一個，每幀幾十個 quad 的成本可以忽略。
        private void DrawRing(SpriteBatch sb, Vector2 center, float radius, Color color, float thickness)
        {
            const int Segments = 36;
            for (int i = 0; i < Segments; i++)
            {
                float a0 = MathHelper.TwoPi * i / Segments;
                float a1 = MathHelper.TwoPi * (i + 1) / Segments;
                var p0 = center + new Vector2(MathF.Cos(a0), MathF.Sin(a0)) * radius;
                var p1 = center + new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * radius;
                DrawLine(sb, p0, p1, color, thickness);
            }
        }

        private void DrawDisc(SpriteBatch sb, Vector2 center, float radius, Color color)
        {
            const int Rings = 8;
            for (int r = 0; r < Rings; r++)
            {
                float rr = radius * (r + 1) / Rings;
                DrawRing(sb, center, rr, color, radius / Rings + 1f);
            }
        }

        private void DrawLine(SpriteBatch sb, Vector2 from, Vector2 to, Color color, float thickness)
        {
            var delta = to - from;
            float length = delta.Length();
            if (length < 0.001f)
                return;

            float angle = MathF.Atan2(delta.Y, delta.X);
            sb.Draw(_pixel, from, null, color, angle, Vector2.Zero,
                    new Vector2(length, thickness), SpriteEffects.None, 0f);
        }
    }
}
