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

        /// <summary>未觸控時的提示圈位置（距畫面左緣／下緣，虛擬座標）。</summary>
        private const float IdleHintX = 172f;
        private const float IdleHintBottom = 150f;

        private bool _active;
        private Vector2 _center;
        private Vector2 _knob;
        private Vector2 _direction;      // 已正規化；長度 0 表示無輸入
        private float _magnitude;

        private Vector2 _lastSentDirection;
        private double _lastSendTime;

        /// <summary>
        /// 判斷某個座標是否被 UI 佔用。搖桿直接讀觸控狀態、不走 UI 的點擊路由，
        /// 少了這道判斷，按底部的 HUD 按鈕會同時把角色往那個方向指令出去。
        /// </summary>
        public Func<Point, bool> IsBlocked { get; set; }

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

                if (IsBlocked != null && IsBlocked(new Point(mouse.X, mouse.Y)))
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
            if (Status != GameControlStatus.Ready || !Visible)
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
                if (_active)
                    DrawActiveStick(sb);
                else
                    DrawIdleHint(sb);
            }
            finally
            {
                scope?.Dispose();
            }

            base.Draw(gameTime);
        }

        private void DrawActiveStick(SpriteBatch sb)
        {
            // 外圈底座
            MobileUi.DrawDisc(sb, _center, BaseRadius, new Color(12, 14, 20) * 0.28f);
            MobileUi.DrawRing(sb, _center, BaseRadius, Color.White * 0.28f, 3.5f);

            // 推的方向上加一段較亮的提示，讓玩家確認方向有吃到
            if (_magnitude > DeadZone)
            {
                var tip = _center + _direction * BaseRadius;
                MobileUi.DrawGlow(sb, tip, KnobRadius * 0.9f, new Color(120, 190, 255) * (0.22f + 0.25f * _magnitude));
            }

            // 搖桿頭
            MobileUi.DrawGlow(sb, _knob, KnobRadius * 1.5f, Color.Black * 0.35f);
            MobileUi.DrawDisc(sb, _knob, KnobRadius, Color.White * 0.55f);
            MobileUi.DrawRing(sb, _knob, KnobRadius, Color.White * 0.8f, 2.5f);
        }

        /// <summary>
        /// 沒有觸控時在左下角畫一個很淡的提示圈。
        /// 浮動搖桿的缺點是新玩家不知道它在哪，一個常駐的淡圈就解決了，
        /// 又不會擋住畫面。
        /// </summary>
        private static void DrawIdleHint(SpriteBatch sb)
        {
            var size = UiScaler.VirtualSize;
            var center = new Vector2(IdleHintX, size.Y - IdleHintBottom);

            MobileUi.DrawRing(sb, center, BaseRadius * 0.85f, Color.White * 0.10f, 2.5f);
            MobileUi.DrawDisc(sb, center, KnobRadius * 0.62f, Color.White * 0.10f);
        }
    }
}
