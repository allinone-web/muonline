#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Controls.UI.Game.Skills
{
    /// <summary>
    /// 把技能圖示做成圓形貼圖，供手機的圓形技能鈕使用。
    ///
    /// 圖集裡的圖示是 20x28 的直式滿版插畫（不是有留白的符號），直接畫進圓形按鈕
    /// 會有上下兩截凸出圓外，看起來像貼歪的貼紙。這裡取中央的正方形區域，
    /// 套上圓形 alpha 遮罩後產生一張小貼圖 —— 圖示就會完整填滿圓形按鈕。
    ///
    /// 每個技能只做一次，之後快取重用；失敗（例如後端不允許 GetData）會記錄下來
    /// 不再重試，呼叫端改用退而求其次的方形畫法。
    /// </summary>
    internal static class SkillIconCircleCache
    {
        /// <summary>輸出解析度。來源只有 20x20，放大是必然的；這裡只是讓圓形邊緣平滑。</summary>
        private const int Size = 96;

        private static readonly Dictionary<(string Path, Rectangle Source), Texture2D> _cache = new();
        private static readonly HashSet<(string Path, Rectangle Source)> _failed = new();

        public static Texture2D? TryGet(GraphicsDevice device, Texture2D atlas, string texturePath, Rectangle source)
        {
            if (device == null || atlas == null || atlas.IsDisposed || source.Width <= 0 || source.Height <= 0)
                return null;

            var key = (texturePath, source);
            if (_cache.TryGetValue(key, out var cached))
            {
                if (!cached.IsDisposed)
                    return cached;

                _cache.Remove(key);
            }

            if (_failed.Contains(key))
                return null;

            try
            {
                var texture = Build(device, atlas, source);
                _cache[key] = texture;
                return texture;
            }
            catch (Exception)
            {
                // 讀不到圖集像素就退回方形畫法，不要每幀重試
                _failed.Add(key);
                return null;
            }
        }

        private static Texture2D Build(GraphicsDevice device, Texture2D atlas, Rectangle source)
        {
            // 取中央的正方形：圖示是 20x28，直接用整格會變成長方形
            int side = Math.Min(source.Width, source.Height);
            var square = new Rectangle(
                source.X + (source.Width - side) / 2,
                source.Y + (source.Height - side) / 2,
                side, side);

            var pixels = new Color[side * side];
            atlas.GetData(0, square, pixels, 0, pixels.Length);

            var output = new Color[Size * Size];
            const float center = (Size - 1) / 2f;
            const float radius = Size / 2f;
            // 邊緣做約 1.5 像素的過渡，避免圓周出現鋸齒
            const float edge = 1.5f / radius;

            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float dx = (x - center) / radius;
                    float dy = (y - center) / radius;
                    float distance = MathF.Sqrt(dx * dx + dy * dy);
                    float alpha = MathHelper.Clamp((1f - distance) / edge, 0f, 1f);

                    // 雙線性取樣。來源只有 20x20，先前用最近鄰放大到 64x64 之後
                    // 再交給 GPU 縮放到按鈕大小 —— 等於重採樣兩次，馬賽克感特別重。
                    var src = SampleBilinear(pixels, side, (x + 0.5f) * side / Size - 0.5f, (y + 0.5f) * side / Size - 0.5f);

                    // 預乘 alpha —— 場景以 BlendState.AlphaBlend（預乘）繪製
                    output[y * Size + x] = new Color(
                        (byte)(src.X * alpha),
                        (byte)(src.Y * alpha),
                        (byte)(src.Z * alpha),
                        (byte)(255 * alpha));
                }
            }

            var texture = new Texture2D(device, Size, Size, false, SurfaceFormat.Color);
            texture.SetData(output);
            return texture;
        }

        private static Vector3 SampleBilinear(Color[] pixels, int side, float u, float v)
        {
            int x0 = Math.Clamp((int)MathF.Floor(u), 0, side - 1);
            int y0 = Math.Clamp((int)MathF.Floor(v), 0, side - 1);
            int x1 = Math.Min(x0 + 1, side - 1);
            int y1 = Math.Min(y0 + 1, side - 1);
            float fx = MathHelper.Clamp(u - x0, 0f, 1f);
            float fy = MathHelper.Clamp(v - y0, 0f, 1f);

            Vector3 P(int x, int y)
            {
                var c = pixels[y * side + x];
                return new Vector3(c.R, c.G, c.B);
            }

            var top = Vector3.Lerp(P(x0, y0), P(x1, y0), fx);
            var bottom = Vector3.Lerp(P(x0, y1), P(x1, y1), fx);
            return Vector3.Lerp(top, bottom, fy);
        }
    }
}
