// GameScene.cs
using Client.Main.Controllers;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace Client.Main.Controls.UI
{
    /// <summary>
    /// Represents a single on-screen floating text notification.
    /// </summary>
    public class FloatingText : UIControl
    {
        // ──────────────────────────── Fields ────────────────────────────
        public string Text { get; }
        public Color TextColor { get; }

        private readonly SpriteFont _font;
        private readonly Vector2 _rawSize;
        private Vector2 _center;
        private float _alpha = 1f;

        /// <summary>
        /// Timestamp when this instance was created (seconds since game start).
        /// </summary>
        public float CreationTime { get; }

        // ────────────────────────── Tuning Constants ──────────────────────────
        private const float FONT_SCALE = 0.6f;
        private const float ORIGINAL_FPS = 25f;
        private const float FLASH_STEPS = 10f;
        private const float FLASH_ON_STEPS = 5f;
        private const int BACKGROUND_PADDING_X = 2;
        private const int BACKGROUND_PADDING_Y = 1;

        private static readonly Color GoldenNoticeColor = new(255, 200, 80);
        private static readonly Color GuildNoticeColor = new(100, 255, 200);
        private static readonly Color NoticeBackgroundColor = new(0, 0, 0, 128);

        // ─────────────────────────── Constructors ───────────────────────────
        public FloatingText(string text, Color color, Vector2 spawnCenter, float creationTime)
        {
            Text = text ?? string.Empty;
            TextColor = color;
            _font = GraphicsManager.Instance.Font
                           ?? throw new InvalidOperationException("SpriteFont is missing.");
            CreationTime = creationTime;

            _rawSize = _font.MeasureString(Text);
            ControlSize = (_rawSize * FONT_SCALE).ToPoint();
            ViewSize = ControlSize;

            _center = spawnCenter;
            UpdatePosition();

            Interactive = false;
            Visible = true;
        }

        // ────────────────────────── Public API ───────────────────────────
        /// <summary>
        /// Updates the vertical center for layout in NotificationManager.
        /// </summary>
        internal void SetCenterY(float newCenterY)
        {
            _center.Y = newCenterY;
            UpdatePosition();
        }

        /// <summary>
        /// Moves the text up or down by the given delta.
        /// </summary>
        public void MoveUp(float deltaY)
        {
            _center.Y += deltaY;
            UpdatePosition();
        }

        private void UpdatePosition()
        {
            X = (int)(_center.X - ControlSize.X * 0.5f);
            Y = (int)(_center.Y - ControlSize.Y * 0.5f);
        }

        public float ScaledHeight => _rawSize.Y * FONT_SCALE;

        // ─────────────────────────── Overrides ───────────────────────────
        public override void Update(GameTime gameTime)
        {
            if (!Visible) return;

            // The original client flashes normal notices in sync with its 25 FPS animation clock.
            if (IsGoldenNotice)
            {
                float flashStep = (float)Math.Floor(
                    gameTime.TotalGameTime.TotalSeconds * ORIGINAL_FPS);
                _alpha = flashStep % FLASH_STEPS < FLASH_ON_STEPS
                    ? 128f / 255f
                    : 1f;
            }
            else
            {
                _alpha = 1f;
            }
        }

        public override void Draw(GameTime gameTime)
        {
            if (!Visible || _alpha <= 0.01f || string.IsNullOrEmpty(Text)) return;

            var spriteBatch = GraphicsManager.Instance.Sprite;
            var pixel = GraphicsManager.Instance.Pixel;

            Vector2 scaledSize = _rawSize * FONT_SCALE;
            Vector2 drawPos = _center - scaledSize * 0.5f;

            if (pixel != null)
            {
                var background = new Rectangle(
                    (int)MathF.Floor(drawPos.X) - BACKGROUND_PADDING_X,
                    (int)MathF.Floor(drawPos.Y) - BACKGROUND_PADDING_Y,
                    (int)MathF.Ceiling(scaledSize.X) + BACKGROUND_PADDING_X * 2,
                    (int)MathF.Ceiling(scaledSize.Y) + BACKGROUND_PADDING_Y * 2);

                spriteBatch.Draw(pixel, background, NoticeBackgroundColor);
            }

            Color textColor = (IsGoldenNotice ? GoldenNoticeColor : GuildNoticeColor) * _alpha;

            spriteBatch.DrawString(
                _font,
                Text,
                drawPos,
                textColor,
                0f,
                Vector2.Zero,
                FONT_SCALE,
                SpriteEffects.None,
                0f);
        }

        private bool IsGoldenNotice =>
            TextColor.R == Color.Goldenrod.R &&
            TextColor.G == Color.Goldenrod.G &&
            TextColor.B == Color.Goldenrod.B;
    }
}
