using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace MuAssets.Core;

/// <summary>
/// 把一張圖寫成 MU 的貼圖格式。<see cref="TextureExporter"/> 的反向。
/// </summary>
/// <remarks>
/// 兩種格式都是「標頭 + 常見格式的內容」：
///
/// <list type="bullet">
///   <item><c>.ozj</c>　24 byte 標頭 + 原始 JPEG</item>
///   <item><c>.ozt</c>　16 byte 標頭 + 寬高與位元深度 + 由下而上的 RGBA</item>
/// </list>
///
/// <b>一律沿用被取代那個檔案自己的標頭</b>，不自己合成。
/// 標頭裡有些欄位的意義還沒完全查清楚（例如 byte 17 的 top-down 旗標
/// 只有 muonline 的讀取端在看，原版客戶端怎麼用還不確定），
/// 原樣保留就不必賭 —— 換的是內容，不是格式。
///
/// 檔案裡的 707 個唯一貼圖全部是這兩種格式，所以覆蓋率是 100%。
/// </remarks>
public static class TextureWriter
{
    private const int OzjHeaderLength = 24;
    private const int OzjTopDownFlagOffset = 17;
    private const int OztHeaderLength = 16;

    /// <summary>這個副檔名寫得回去嗎。</summary>
    public static bool IsSupported(string path)
    {
        string extension = Path.GetExtension(path);
        return extension.Equals(".ozj", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".ozt", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 用 <paramref name="image"/> 的內容取代 <paramref name="targetPath"/>，格式與標頭沿用原檔。
    /// </summary>
    /// <param name="quality">JPEG 品質（只對 .ozj 有意義）。</param>
    public static byte[] Build(Image<Rgba32> image, string targetPath, byte[] originalBytes, int quality = 92)
    {
        string extension = Path.GetExtension(targetPath);

        if (extension.Equals(".ozj", StringComparison.OrdinalIgnoreCase))
            return BuildOzj(image, originalBytes, quality);

        if (extension.Equals(".ozt", StringComparison.OrdinalIgnoreCase))
            return BuildOzt(image, originalBytes);

        throw new NotSupportedException($"寫不了 {extension}（只支援 .ozj 與 .ozt）");
    }

    private static byte[] BuildOzj(Image<Rgba32> source, byte[] originalBytes, int quality)
    {
        if (originalBytes.Length < OzjHeaderLength)
            throw new InvalidDataException("原檔太短，不是有效的 .ozj");

        // 標頭的 byte 17 是 top-down 旗標：為 0 時讀取端會把每一列上下翻。
        // 所以旗標為 0 就先翻一次，讀回來才會是我們要的方向。
        bool topDown = originalBytes[OzjTopDownFlagOffset] != 0;

        using var image = source.Clone();
        if (!topDown)
            image.Mutate(x => x.Flip(FlipMode.Vertical));

        using var jpeg = new MemoryStream();
        image.Save(jpeg, new JpegEncoder { Quality = quality });

        var buffer = new byte[OzjHeaderLength + jpeg.Length];
        originalBytes.AsSpan(0, OzjHeaderLength).CopyTo(buffer);
        jpeg.GetBuffer().AsSpan(0, (int)jpeg.Length).CopyTo(buffer.AsSpan(OzjHeaderLength));

        return buffer;
    }

    private static byte[] BuildOzt(Image<Rgba32> image, byte[] originalBytes)
    {
        if (originalBytes.Length < OztHeaderLength + 6)
            throw new InvalidDataException("原檔太短，不是有效的 .ozt");

        int width = image.Width;
        int height = image.Height;

        var buffer = new byte[OztHeaderLength + 6 + (width * height * 4)];
        originalBytes.AsSpan(0, OztHeaderLength).CopyTo(buffer);

        var span = buffer.AsSpan(OztHeaderLength);
        BitConverter.TryWriteBytes(span, (short)width);
        BitConverter.TryWriteBytes(span[2..], (short)height);
        span[4] = 32;                       // 位元深度，讀取端只接受 32
        span[5] = originalBytes[OztHeaderLength + 5];   // 用途未明的那個位元組，原樣保留

        // 像素由下而上、每格 R G B A —— 與 OZTReader 的讀法對稱。
        const int offset = OztHeaderLength + 6;

        int cursor = offset;

        image.ProcessPixelRows(accessor =>
        {
            for (int y = accessor.Height - 1; y >= 0; y--)
            {
                var row = accessor.GetRowSpan(y);

                for (int x = 0; x < row.Length; x++)
                {
                    var pixel = row[x];
                    buffer[cursor++] = pixel.R;
                    buffer[cursor++] = pixel.G;
                    buffer[cursor++] = pixel.B;
                    buffer[cursor++] = pixel.A;
                }
            }
        });

        return buffer;
    }
}
