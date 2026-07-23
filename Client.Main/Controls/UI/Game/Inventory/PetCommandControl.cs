#nullable enable
using Client.Main.Controllers;
using Client.Main.Controls.UI.Common;
using Client.Main.Controls.UI.Game.Common;
using Client.Main.Models;
using Client.Main.Objects.Pets;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Controls.UI.Game.Inventory
{
    /// <summary>
    /// Popup menu for pet commands: Attack, Defense, Collect, Wait.
    /// Attaches to the pet inventory dialog.
    /// </summary>
    public class PetCommandControl : UIControl
    {
        private SpriteFont? _font;
        private PetCommand _selectedCommand = PetCommand.AttackRandom;

        private static readonly (string Label, PetCommand Command, Color Color)[] CommandButtons =
        [
            ("Attack (Random)", PetCommand.AttackRandom, new Color(220, 50, 40)),
            ("Attack (Same Target)", PetCommand.AttackSameTarget, new Color(200, 80, 40)),
            ("Attack (With Owner)", PetCommand.AttackWithOwner, new Color(180, 120, 40)),
            ("Defense", PetCommand.Defense, new Color(40, 140, 220)),
            ("Collect Items", PetCommand.Collect, new Color(180, 160, 40)),
            ("Wait (Stay)", PetCommand.Wait, new Color(140, 140, 140)),
        ];

        private const int BtnHeight = 26;
        private const int BtnPadding = 6;

        public event Action<PetCommand>? CommandSelected;

        public PetCommandControl()
        {
            AutoViewSize = false;
            Interactive = true;
            BackgroundColor = new Color(20, 24, 32, 235);
            BorderColor = ModernHudTheme.BorderOuter;
            BorderThickness = 1;
            ControlSize = new Point(190, CommandButtons.Length * (BtnHeight + BtnPadding) + BtnPadding * 2);
            ViewSize = ControlSize;
            Visible = false;
        }

        public void SetSelectedCommand(PetCommand cmd)
        {
            _selectedCommand = cmd;
        }

        public override void Draw(GameTime gameTime)
        {
            if (!Visible || Status != GameControlStatus.Ready) return;

            var spriteBatch = GraphicsManager.Instance.Sprite;
            var pixel = GraphicsManager.Instance.Pixel;
            _font ??= GraphicsManager.Instance.Font;
            if (spriteBatch == null || pixel == null || _font == null) return;

            Rectangle rect = DisplayRectangle;

            // Background
            spriteBatch.Draw(pixel, rect, BackgroundColor * Alpha);
            spriteBatch.Draw(pixel, new Rectangle(rect.X, rect.Y, rect.Width, 1), ModernHudTheme.BorderOuter * Alpha);
            spriteBatch.Draw(pixel, new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1), ModernHudTheme.BorderOuter * Alpha);
            spriteBatch.Draw(pixel, new Rectangle(rect.X, rect.Y, 1, rect.Height), ModernHudTheme.BorderOuter * Alpha);
            spriteBatch.Draw(pixel, new Rectangle(rect.Right - 1, rect.Y, 1, rect.Height), ModernHudTheme.BorderOuter * Alpha);

            // Command buttons
            for (int i = 0; i < CommandButtons.Length; i++)
            {
                var (label, cmd, color) = CommandButtons[i];
                var btnRect = new Rectangle(rect.X + BtnPadding, rect.Y + BtnPadding + i * (BtnHeight + BtnPadding), rect.Width - BtnPadding * 2, BtnHeight);

                bool isHovered = btnRect.Contains(MuGame.Instance.UiMouseState.Position);
                bool isSelected = cmd == _selectedCommand;

                Color bgColor = isSelected ? new Color(60, 60, 40, 220)
                    : isHovered ? new Color(40, 40, 50, 200)
                    : new Color(28, 30, 38, 180);

                spriteBatch.Draw(pixel, btnRect, bgColor * Alpha);
                spriteBatch.Draw(pixel, new Rectangle(btnRect.X, btnRect.Y, btnRect.Width, 1), ModernHudTheme.BorderInner * 0.4f * Alpha);

                string text = isSelected ? $"> {label}" : $"  {label}";
                float scale = 0.5f;
                var textSize = _font.MeasureString(text) * scale;
                float ty = btnRect.Y + (btnRect.Height - textSize.Y) / 2f;

                spriteBatch.DrawString(_font, text, new Vector2(btnRect.X + 8 + 1, ty + 1), Color.Black * 0.6f, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
                spriteBatch.DrawString(_font, text, new Vector2(btnRect.X + 8, ty), color * Alpha, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            }
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            if (!Visible) return;

            var mouse = MuGame.Instance.UiMouseState;
            var prev = MuGame.Instance.PrevUiMouseState;
            if (mouse.LeftButton == Microsoft.Xna.Framework.Input.ButtonState.Pressed
                && prev.LeftButton == Microsoft.Xna.Framework.Input.ButtonState.Released)
            {
                Rectangle rect = DisplayRectangle;
                for (int i = 0; i < CommandButtons.Length; i++)
                {
                    var btnRect = new Rectangle(rect.X + BtnPadding, rect.Y + BtnPadding + i * (BtnHeight + BtnPadding), rect.Width - BtnPadding * 2, BtnHeight);
                    if (btnRect.Contains(mouse.Position))
                    {
                        var cmd = CommandButtons[i].Command;
                        _selectedCommand = cmd;
                        CommandSelected?.Invoke(cmd);
                        break;
                    }
                }
            }
        }
    }
}
