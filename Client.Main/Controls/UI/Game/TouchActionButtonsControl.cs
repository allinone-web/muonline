using System;
using System.Collections.Generic;
using Client.Main.Controllers;
using Client.Main.Core.Client;
using Client.Main.Graphics;
using Client.Main.Helpers;
using Client.Main.Models;
using Client.Main.Scenes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Client.Main.Controls.UI.Game
{
    /// <summary>
    /// 手機用的攻擊／技能按鈕，配置在畫面右下角。
    ///
    /// 桌面的流程是「先按 1-0 選技能、再點目標」，手機上要點兩次而且得點準怪物。
    /// 這裡改成手遊 MMO 的標準做法：一顆大的普通攻擊鈕，外圈是已指派的技能鈕，
    /// 按下去就自動鎖定最近的敵人出手，不需要先選再點。
    ///
    /// 技能來源沿用底部快捷列已指派的內容，玩家仍在原本的介面指派，不另建一套。
    /// </summary>
    public class TouchActionButtonsControl : UIControl
    {
        private const float MainButtonRadius = 62f;
        private const float SkillButtonRadius = 38f;

        /// <summary>主按鈕圓心與畫面右下角的距離（虛擬座標）。</summary>
        private const float MarginRight = 120f;
        private const float MarginBottom = 190f;

        /// <summary>技能鈕排在主按鈕左上方的弧線上。</summary>
        private const float SkillArcRadius = 118f;
        private const float SkillArcStartDegrees = 150f;
        private const float SkillArcStepDegrees = 42f;
        private const int MaxSkillButtons = 4;

        /// <summary>按下後的視覺回饋時間。</summary>
        private const double PressFeedbackSeconds = 0.12;

        private readonly Func<IReadOnlyList<SkillEntryState>> _skillProvider;

        private Vector2 _mainCenter;
        private readonly Vector2[] _skillCenters = new Vector2[MaxSkillButtons];
        private readonly double[] _skillPressedAt = new double[MaxSkillButtons];
        private double _mainPressedAt;

        private bool _wasPressed;
        private Texture2D _pixel;

        public TouchActionButtonsControl(Func<IReadOnlyList<SkillEntryState>> skillProvider)
        {
            _skillProvider = skillProvider;
            Interactive = false;   // 自行處理觸控，避免與 UI 點擊路由互搶
            AutoViewSize = false;
            ViewSize = new Point(1, 1);
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (Status != GameControlStatus.Ready || !Visible)
                return;

            RefreshLayout();

            var mouse = MuGame.Instance.UiMouseState;
            bool pressed = mouse.LeftButton == ButtonState.Pressed;

            // 只在按下的那一幀觸發，避免按住不放連發
            if (pressed && !_wasPressed)
            {
                HandlePress(new Vector2(mouse.X, mouse.Y), gameTime);
            }

            _wasPressed = pressed;
        }

        private void RefreshLayout()
        {
            var size = UiScaler.VirtualSize;
            _mainCenter = new Vector2(size.X - MarginRight, size.Y - MarginBottom);

            for (int i = 0; i < MaxSkillButtons; i++)
            {
                float degrees = SkillArcStartDegrees + SkillArcStepDegrees * i;
                float radians = MathHelper.ToRadians(degrees);
                _skillCenters[i] = _mainCenter + new Vector2(
                    MathF.Cos(radians) * SkillArcRadius,
                    MathF.Sin(radians) * SkillArcRadius);
            }
        }

        private void HandlePress(Vector2 position, GameTime gameTime)
        {
            if (MuGame.Instance.ActiveScene is not GameScene scene)
                return;

            double now = gameTime.TotalGameTime.TotalSeconds;

            if (Vector2.Distance(position, _mainCenter) <= MainButtonRadius)
            {
                _mainPressedAt = now;
                scene.AttackNearestEnemy(null);   // null = 普通攻擊
                return;
            }

            var skills = _skillProvider?.Invoke();
            for (int i = 0; i < MaxSkillButtons; i++)
            {
                if (Vector2.Distance(position, _skillCenters[i]) > SkillButtonRadius)
                    continue;

                var skill = skills != null && i < skills.Count ? skills[i] : null;
                if (skill == null)
                    return;   // 空的技能鈕不做事，也不要穿透到底下的世界

                _skillPressedAt[i] = now;
                scene.AttackNearestEnemy(skill);
                return;
            }
        }

        /// <summary>按鈕是否吃掉了這個座標的觸控 —— 供外部避免同時觸發世界點擊。</summary>
        public bool ContainsPoint(Vector2 position)
        {
            if (!Visible)
                return false;

            if (Vector2.Distance(position, _mainCenter) <= MainButtonRadius)
                return true;

            for (int i = 0; i < MaxSkillButtons; i++)
            {
                if (Vector2.Distance(position, _skillCenters[i]) <= SkillButtonRadius)
                    return true;
            }

            return false;
        }

        public override void Draw(GameTime gameTime)
        {
            if (!Visible)
                return;

            _pixel ??= GraphicsManager.Instance.Pixel;
            if (_pixel == null)
                return;

            double now = gameTime.TotalGameTime.TotalSeconds;
            var sb = GraphicsManager.Instance.Sprite;
            var font = GraphicsManager.Instance.Font;
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

            // 主攻擊鈕
            bool mainPressed = now - _mainPressedAt < PressFeedbackSeconds;
            DrawButton(sb, font, _mainCenter, MainButtonRadius,
                       mainPressed ? new Color(255, 210, 120, 220) : new Color(220, 90, 60, 190),
                       "ATK");

            // 技能鈕
            var skills = _skillProvider?.Invoke();
            for (int i = 0; i < MaxSkillButtons; i++)
            {
                var skill = skills != null && i < skills.Count ? skills[i] : null;
                bool pressed = now - _skillPressedAt[i] < PressFeedbackSeconds;

                Color color = skill == null
                    ? new Color(90, 90, 100, 110)                       // 未指派
                    : pressed
                        ? new Color(190, 220, 255, 225)
                        : new Color(70, 120, 200, 185);

                DrawButton(sb, font, _skillCenters[i], SkillButtonRadius, color, (i + 1).ToString());
            }

            }
            finally
            {
                scope?.Dispose();
            }

            base.Draw(gameTime);
        }

        private void DrawButton(SpriteBatch sb, SpriteFont font, Vector2 center, float radius, Color color, string label)
        {
            DrawDisc(sb, center, radius, color);
            DrawRing(sb, center, radius, new Color(255, 255, 255, 120), 2.5f);

            if (font == null || string.IsNullOrEmpty(label))
                return;

            var measured = font.MeasureString(label);
            float scale = radius / 46f;
            sb.DrawString(font, label,
                center - measured * scale * 0.5f,
                Color.White, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }

        // 沒有現成的圓形貼圖，用細短線段拼出圓環／圓面。
        // 按鈕數量固定且很少，每幀成本可以忽略。
        private void DrawRing(SpriteBatch sb, Vector2 center, float radius, Color color, float thickness)
        {
            const int Segments = 32;
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
            const int Rings = 10;
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
