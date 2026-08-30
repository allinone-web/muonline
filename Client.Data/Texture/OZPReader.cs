using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp;

namespace Client.Data.Texture
{
    public class OZPReader : BaseReader<TextureData>
    {
        public const int MAX_WIDTH = 1024;
        public const int MAX_HEIGHT = 1024;

        /// <summary>PNG 的簽章是 8 個位元組：89 50 4E 47 0D 0A 1A 0A。</summary>
        private static readonly byte[] PngSignatureTail = [0x0D, 0x0A, 0x1A, 0x0A];

        protected override TextureData Read(byte[] buffer)
        {
            if (buffer.Length < 8 || buffer[0] != 137 || buffer[1] != 'P' || buffer[2] != 'N' || buffer[3] != 'G')
                throw new ApplicationException("Invalid file format");

            // OZP 是「4 個位元組前綴（89 50 4E 47）＋ 一份完整的 PNG」，
            // 所以它的第 5~8 個位元組又是一次 89 50 4E 47。
            // 純 PNG 的第 5~8 個位元組則是簽章的後半 0D 0A 1A 0A。
            //
            // 原本無條件砍掉前 4 個位元組，於是<b>純 PNG 一定壞</b>：
            // 剩下 0D 0A 1A 0A IHDR… 不是合法 PNG，ImageSharp 直接拋例外。
            // 遊戲自己的資源全是 OZP 所以從來沒踩到；資源庫匯入的外部貼圖是純 PNG，
            // 每一張都載不進去 —— 模型有網格、有骨骼，就是畫不出來。
            bool isPlainPng =
                buffer[4] == PngSignatureTail[0] && buffer[5] == PngSignatureTail[1] &&
                buffer[6] == PngSignatureTail[2] && buffer[7] == PngSignatureTail[3];

            return ReadPNG(isPlainPng ? buffer : buffer[4..]);
        }

        private TextureData ReadPNG(byte[] buffer)
        {
            using var image = Image.Load<Rgba32>(buffer);

            int width = image.Width;
            int height = image.Height;

            var data = new byte[width * height * 4];
            image.CopyPixelDataTo(data);

            return new TextureData
            {
                Width = width,
                Height = height,
                Components = 4,
                Data = data,
                IsCompressed = false,
                Format = TextureSurfaceFormat.Color
            };
        }
    }
}
