#nullable enable
using Client.Main.Controllers;
using Client.Main.Controls.UI.Common;
using Client.Main.Controls.UI.Game.Common;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Controls.UI.Game
{
    /// <summary>
    /// Doppelganger event enter dialog. Shows event info and enter button.
    /// Matches SourceMain NewUIDoppelGangerWindow.
    /// </summary>
    public class DoppelgangerEnterControl : UIControl
    {
        private SpriteFont? _font;
        public event Action? EnterRequested;
        public event Action? CloseRequested;

        public DoppelgangerEnterControl()
        {
            AutoViewSize = false;
            Interactive = true;
            BackgroundColor = new Color(18, 16, 28, 245);
            BorderColor = ModernHudTheme.BorderOuter;
            BorderThickness = 2;
            ControlSize = new Point(340, 240);
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
            sb.Draw(pixel, new Rectangle(rect.X, rect.Y, rect.Width, 2), ModernHudTheme.Accent * Alpha);
            sb.Draw(pixel, new Rectangle(rect.X, rect.Bottom - 2, rect.Width, 2), ModernHudTheme.Accent * Alpha);

            DrawTitle(sb, "Doppelganger Event");
            DrawLine(sb, rect.X + 20, ref _lineY, "Level: 150+", ModernHudTheme.TextWhite);
            DrawLine(sb, rect.X + 20, ref _lineY, "Tickets required: 1 Blood Bone + 1 Scroll of Blood", ModernHudTheme.TextGray);
            DrawLine(sb, rect.X + 20, ref _lineY, "Reward: Experience, Zen, Rare Items", ModernHudTheme.TextGold);

            _lineY += 12;
            var btnRect = new Rectangle(rect.X + 100, _lineY, 140, 32);
            bool hovered = btnRect.Contains(MuGame.Instance.UiMouseState.Position);
            sb.Draw(pixel, btnRect, hovered ? ModernHudTheme.Accent * Alpha : ModernHudTheme.Secondary * Alpha);
            var enterText = "Enter Event";
            var enterSize = _font.MeasureString(enterText) * 0.6f;
            sb.DrawString(_font, enterText, new Vector2(btnRect.Center.X - enterSize.X / 2, btnRect.Y + 6),
                ModernHudTheme.TextWhite * Alpha, 0f, Vector2.Zero, 0.6f, SpriteEffects.None, 0f);
        }

        private int _lineY;
        private void DrawTitle(SpriteBatch sb, string text)
        {
            _lineY = DisplayRectangle.Y + 30;
            var sz = _font!.MeasureString(text) * 0.8f;
            sb.DrawString(_font, text, new Vector2(DisplayRectangle.Center.X - sz.X / 2, _lineY),
                ModernHudTheme.TextGold * Alpha, 0f, Vector2.Zero, 0.8f, SpriteEffects.None, 0f);
            _lineY += 40;
        }

        private void DrawLine(SpriteBatch sb, int x, ref int y, string text, Color color)
        {
            sb.DrawString(_font!, text, new Vector2(x, y), color * Alpha, 0f, Vector2.Zero, 0.5f, SpriteEffects.None, 0f);
            y += 22;
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
