#nullable enable
using Client.Main.Controllers;
using Client.Main.Controls.UI.Game.Common;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Controls.UI.Game
{
    /// <summary>
    /// Cursed Temple (Illusion Temple) event enter dialog.
    /// </summary>
    public class CursedTempleEnterControl : UIControl
    {
        private SpriteFont? _font;

        public event Action? EnterRequested;
        public event Action? CloseRequested;

        public CursedTempleEnterControl()
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
            sb.Draw(pixel, new Rectangle(rect.X, rect.Y, rect.Width, 2), new Color(140, 80, 200) * Alpha);

            int y = rect.Y + 20;
            var title = "Illusion Temple (Cursed Temple)";
            var tsz = _font.MeasureString(title) * 0.7f;
            sb.DrawString(_font, title, new Vector2(rect.Center.X - tsz.X / 2, y), ModernHudTheme.TextGold * Alpha, 0f, Vector2.Zero, 0.7f, SpriteEffects.None, 0f);
            y += 42;

            string[] lines =
            [
                "Level: 220+",
                "Tickets: 1 Old Scroll",
                "Reward: Illusion Points, Rank Rewards",
                "",
                "6 vs 6 team battle for the holy artifact!",
                "",
                "[Press ESC to close]"
            ];
            foreach (var line in lines)
            {
                var sz = _font.MeasureString(line) * 0.5f;
                Color c = line.StartsWith("[") ? ModernHudTheme.TextGray :
                          line.StartsWith("6 vs") ? new Color(255, 200, 60) :
                          ModernHudTheme.TextWhite;
                sb.DrawString(_font, line, new Vector2(rect.X + 24, y), c * Alpha, 0f, Vector2.Zero, 0.5f, SpriteEffects.None, 0f);
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
