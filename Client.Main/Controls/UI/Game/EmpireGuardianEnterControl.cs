#nullable enable
using Client.Main.Controllers;
using Client.Main.Controls.UI.Game.Common;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Controls.UI.Game
{
    /// <summary>
    /// Imperial Guardian (Empire Guardian) event enter dialog.
    /// </summary>
    public class EmpireGuardianEnterControl : UIControl
    {
        private SpriteFont? _font;

        public event Action? EnterRequested;
        public event Action? CloseRequested;

        public EmpireGuardianEnterControl()
        {
            AutoViewSize = false;
            Interactive = true;
            BackgroundColor = new Color(18, 16, 28, 245);
            BorderColor = ModernHudTheme.BorderOuter;
            BorderThickness = 2;
            ControlSize = new Point(340, 260);
            ViewSize = ControlSize;
            Visible = false;
        }

        public override void Draw(GameTime gameTime)
        {
            if (!Visible || Status != GameControlStatus.Ready) return;
            var sb = GraphicsManager.Instance.Sprite;
            var pixel = GraphicsManager.Instance.Pixel;
            _font ??= GraphicsManager.Instance.Font;
            if (sb == null || pixel == null || _font == null) return;

            Rectangle rect = DisplayRectangle;
            sb.Draw(pixel, rect, BackgroundColor * Alpha);
            sb.Draw(pixel, new Rectangle(rect.X, rect.Y, rect.Width, 2), new Color(60, 140, 60) * Alpha);

            int y = rect.Y + 24;
            var title = "Imperial Guardian Event";
            var tsz = _font.MeasureString(title) * 0.8f;
            sb.DrawString(_font, title, new Vector2(rect.Center.X - tsz.X / 2, y), ModernHudTheme.TextGold * Alpha, 0f, Vector2.Zero, 0.8f, SpriteEffects.None, 0f);
            y += 38;

            string[] lines = ["Level: 300+", "Tickets: 1 Armor of Guardman", "Reward: Experience, Master Points, Rare Items", "", "[Press ESC to close]"];
            foreach (var line in lines)
            {
                var sz = _font.MeasureString(line) * 0.5f;
                sb.DrawString(_font, line, new Vector2(rect.X + 24, y), line.StartsWith("[") ? ModernHudTheme.TextGray : ModernHudTheme.TextWhite, 0f, Vector2.Zero, 0.5f, SpriteEffects.None, 0f);
                y += 24;
            }
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            if (!Visible) return;
            var kb = MuGame.Instance.Keyboard;
            var prev = MuGame.Instance.PrevKeyboard;
            if (kb.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Enter) && prev.IsKeyUp(Microsoft.Xna.Framework.Input.Keys.Enter))
                EnterRequested?.Invoke();
            if (kb.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Escape) && prev.IsKeyUp(Microsoft.Xna.Framework.Input.Keys.Escape))
                CloseRequested?.Invoke();
        }
    }
}
