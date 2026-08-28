using System;
using Microsoft.Xna.Framework;

namespace Client.Main.Controllers
{
    public enum ScaleMode
    {
        /// <summary>
        /// Uniform scaling with letterboxing/pillarboxing.
        /// Maintains aspect ratio, adds black bars if needed.
        /// </summary>
        Uniform,

        /// <summary>
        /// Stretches to fill entire screen.
        /// May distort aspect ratio but no black bars.
        /// </summary>
        Stretch
    }

    /// <summary>
    /// Centralizes UI scaling between the virtual layout resolution and the physical back buffer.
    /// </summary>
    public static class UiScaler
    {
        private const float MinScale = 0.0001f;

        public static Point VirtualSize { get; private set; } = new(1280, 720);
        public static Point ActualSize { get; private set; } = new(1280, 720);

        /// <summary>
        /// Scale factor X (virtual to actual). In Uniform mode, ScaleX == ScaleY.
        /// </summary>
        public static float ScaleX { get; private set; } = 1f;

        /// <summary>
        /// Scale factor Y (virtual to actual). In Uniform mode, ScaleX == ScaleY.
        /// </summary>
        public static float ScaleY { get; private set; } = 1f;

        /// <summary>
        /// Uniform scale (min of ScaleX, ScaleY). For backward compatibility.
        /// </summary>
        public static float Scale { get; private set; } = 1f;

        /// <summary>
        /// Inverse scale X (actual to virtual).
        /// </summary>
        public static float InverseScaleX { get; private set; } = 1f;

        /// <summary>
        /// Inverse scale Y (actual to virtual).
        /// </summary>
        public static float InverseScaleY { get; private set; } = 1f;

        /// <summary>
        /// Inverse uniform scale. For backward compatibility.
        /// </summary>
        public static float InverseScale { get; private set; } = 1f;

        public static Vector2 Offset { get; private set; } = Vector2.Zero;
        public static Matrix SpriteTransform { get; private set; } = Matrix.Identity;
        public static ScaleMode Mode { get; private set; } = ScaleMode.Uniform;

        /// <summary>
        /// Indicates if UiScaler has been properly configured with valid screen dimensions.
        /// </summary>
        public static bool IsConfigured { get; private set; } = false;

        /// <summary>
        /// iPhone 的圓角與動態島會裁掉畫面邊緣。UI 若鋪滿整個 back buffer，
        /// 四角的元素會被切掉且點不到。這裡以像素為單位內縮可用區域。
        /// 由平台端（MuIos）在取得 UIKit 的 SafeAreaInsets 後設定。
        /// </summary>
        public static Vector4 SafeAreaInsets { get; set; } = Vector4.Zero; // Left, Top, Right, Bottom

        public static void Configure(int actualWidth, int actualHeight, int virtualWidth, int virtualHeight,
            ScaleMode mode = ScaleMode.Uniform)
        {
            // Ignore invalid dimensions (can happen in constructor before window is ready)
            if (actualWidth <= 0 || actualHeight <= 0 || virtualWidth <= 0 || virtualHeight <= 0)
            {
                return;
            }

            ActualSize = new Point(actualWidth, actualHeight);
            VirtualSize = new Point(virtualWidth, virtualHeight);
            Mode = mode;

            switch (mode)
            {
                case ScaleMode.Uniform:
                    ConfigureUniform();
                    break;
                case ScaleMode.Stretch:
                    ConfigureStretch();
                    break;
            }

            IsConfigured = true;
        }

        private static void ConfigureUniform()
        {
            // 扣掉安全區域後才是 UI 真正可用的範圍（iPhone 圓角、動態島、home indicator）。
            // 桌面上 SafeAreaInsets 為 0，行為與原本完全相同。
            float usableWidth = MathF.Max(ActualSize.X - SafeAreaInsets.X - SafeAreaInsets.Z, 1f);
            float usableHeight = MathF.Max(ActualSize.Y - SafeAreaInsets.Y - SafeAreaInsets.W, 1f);

            float scaleX = usableWidth / VirtualSize.X;
            float scaleY = usableHeight / VirtualSize.Y;

            Scale = MathF.Max(Math.Min(scaleX, scaleY), MinScale);
            ScaleX = Scale;
            ScaleY = Scale;

            InverseScale = 1f / Scale;
            InverseScaleX = InverseScale;
            InverseScaleY = InverseScale;

            float scaledWidth = VirtualSize.X * Scale;
            float scaledHeight = VirtualSize.Y * Scale;

            // 先移到安全區域的原點，再把剩餘空間平均分到兩側。
            Offset = new Vector2(
                SafeAreaInsets.X + MathF.Max(0f, (usableWidth - scaledWidth) * 0.5f),
                SafeAreaInsets.Y + MathF.Max(0f, (usableHeight - scaledHeight) * 0.5f));

            // Apply render scale to the transform
            float finalScale = Scale * Constants.RENDER_SCALE;
            var transform = Matrix.CreateScale(finalScale, finalScale, 1f);
            transform.Translation = new Vector3(Offset * Constants.RENDER_SCALE, 0f);
            SpriteTransform = transform;
        }

        private static void ConfigureStretch()
        {
            // 扣掉安全區域後才是 UI 真正可用的範圍（iPhone 圓角、動態島、home indicator）
            float usableWidth = MathF.Max(ActualSize.X - SafeAreaInsets.X - SafeAreaInsets.Z, 1f);
            float usableHeight = MathF.Max(ActualSize.Y - SafeAreaInsets.Y - SafeAreaInsets.W, 1f);

            ScaleX = MathF.Max(usableWidth / VirtualSize.X, MinScale);
            ScaleY = MathF.Max(usableHeight / VirtualSize.Y, MinScale);
            Scale = Math.Min(ScaleX, ScaleY); // For backward compatibility

            InverseScaleX = 1f / ScaleX;
            InverseScaleY = 1f / ScaleY;
            InverseScale = 1f / Scale;

            Offset = new Vector2(SafeAreaInsets.X, SafeAreaInsets.Y);

            // Create non-uniform scale transform
            float finalScaleX = ScaleX * Constants.RENDER_SCALE;
            float finalScaleY = ScaleY * Constants.RENDER_SCALE;
            // 縮放後還要平移到安全區域起點，否則 UI 只是變小、仍然貼在螢幕左上角
            SpriteTransform = Matrix.CreateScale(finalScaleX, finalScaleY, 1f)
                            * Matrix.CreateTranslation(Offset.X, Offset.Y, 0f);
        }

        /// <summary>
        /// Converts actual screen coordinates to virtual coordinates.
        /// </summary>
        public static Point ToVirtual(Point actual)
        {
            float x = (actual.X - Offset.X) * InverseScaleX;
            float y = (actual.Y - Offset.Y) * InverseScaleY;
            return new Point((int)MathF.Round(x), (int)MathF.Round(y));
        }

        /// <summary>
        /// Converts virtual coordinates to actual screen coordinates.
        /// </summary>
        public static Point ToActual(Point virtualPoint)
        {
            float x = virtualPoint.X * ScaleX + Offset.X;
            float y = virtualPoint.Y * ScaleY + Offset.Y;
            return new Point((int)MathF.Round(x), (int)MathF.Round(y));
        }

        /// <summary>
        /// Converts a virtual rectangle to actual screen rectangle.
        /// </summary>
        public static Rectangle ToActual(Rectangle virtualRect)
        {
            Point topLeft = ToActual(virtualRect.Location);
            int width = (int)MathF.Round(virtualRect.Width * ScaleX);
            int height = (int)MathF.Round(virtualRect.Height * ScaleY);
            return new Rectangle(topLeft.X, topLeft.Y, width, height);
        }

        /// <summary>
        /// Converts a virtual Vector2 to actual screen Vector2.
        /// </summary>
        public static Vector2 ToActual(Vector2 virtualPos)
        {
            return new Vector2(
                virtualPos.X * ScaleX + Offset.X,
                virtualPos.Y * ScaleY + Offset.Y);
        }

        /// <summary>
        /// Converts actual screen Vector2 to virtual Vector2.
        /// </summary>
        public static Vector2 ToVirtual(Vector2 actual)
        {
            return new Vector2(
                (actual.X - Offset.X) * InverseScaleX,
                (actual.Y - Offset.Y) * InverseScaleY);
        }
    }
}
