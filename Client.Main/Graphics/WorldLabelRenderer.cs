using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Graphics
{
    /// <summary>
    /// Shared label/nameplate rendering for world objects.
    /// Avoids duplicated background+border+text drawing across WorldObject, DroppedItemObject, etc.
    /// </summary>
    public static class WorldLabelRenderer
    {
        private static Texture2D _whitePixel;

        private static Texture2D GetWhitePixel(GraphicsDevice gd)
        {
            if (_whitePixel == null || _whitePixel.IsDisposed)
            {
                _whitePixel = new Texture2D(gd, 1, 1);
                _whitePixel.SetData(new[] { Color.White });
            }
            return _whitePixel;
        }

        /// <summary>
        /// Draw a background rectangle with a subtle border using the active SpriteBatch.
        /// Caller must have already called SpriteBatch.Begin().
        /// </summary>
        public static void DrawLabelBackground(SpriteBatch sb, GraphicsDevice gd, Rectangle rect, Color bgColor, float layer = 0f)
        {
            var pixel = GetWhitePixel(gd);
            // Border
            var borderColor = Color.White * 0.3f;
            var borderRect = new Rectangle(rect.X - 1, rect.Y - 1, rect.Width + 2, rect.Height + 2);
            sb.Draw(pixel, borderRect, null, borderColor, 0f, Vector2.Zero, SpriteEffects.None, layer + 0.0001f);
            // Background
            sb.Draw(pixel, rect, null, bgColor, 0f, Vector2.Zero, SpriteEffects.None, layer);
        }

        /// <summary>
        /// Draw a simple background rectangle (no border) for hover name overlays.
        /// </summary>
        public static void DrawSimpleBackground(SpriteBatch sb, GraphicsDevice gd, Rectangle rect, Color bgColor, float layer = 0f)
        {
            var pixel = GetWhitePixel(gd);
            sb.Draw(pixel, rect, null, bgColor, 0f, Vector2.Zero, SpriteEffects.None, layer);
        }

        /// <summary>
        /// Draw a text label with background, centered above a world position.
        /// Caller must ensure SpriteBatch.Begin() has been called.
        /// </summary>
        public static void DrawWorldLabel(
            SpriteBatch sb,
            GraphicsDevice gd,
            SpriteFont font,
            string text,
            Vector3 worldPos,
            Color textColor,
            Color bgColor,
            Matrix projection,
            Matrix view,
            float scale = 0.4f,   // 呼叫端可覆寫；手機的放大在下面套用
            float layer = 0f)
        {
            Vector3 screen = gd.Viewport.Project(worldPos, projection, view, Matrix.Identity);
            if (screen.Z < 0f || screen.Z > 1f)
                return;

            // 掉落物的名稱。手機放大，見 MobileUi.WorldTextScale ——
            // 0.4 是桌面尺寸，在手上的手機幾乎讀不出撿到的是什麼。
            scale *= Client.Main.Controls.UI.MobileUi.WorldTextScale;

            var size = font.MeasureString(text) * scale;
            int padX = (int)(4 * Client.Main.Controls.UI.MobileUi.WorldTextScale);
            int padY = (int)(2 * Client.Main.Controls.UI.MobileUi.WorldTextScale);
            var rect = new Rectangle(
                (int)(screen.X - size.X * 0.5f) - padX,
                (int)(screen.Y - size.Y) - padY,
                (int)(size.X) + padX * 2,
                (int)(size.Y) + padY * 2);

            DrawSimpleBackground(sb, gd, rect, bgColor, layer);
            sb.DrawString(font, text, new Vector2(rect.X + padX, rect.Y + padY), textColor, 0f, Vector2.Zero, scale, SpriteEffects.None, layer);
        }
    }
}
