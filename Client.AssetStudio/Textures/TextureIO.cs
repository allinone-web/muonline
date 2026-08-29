using Client.Data.Texture;
using Client.Main.Content;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace Client.AssetStudio.Textures;

/// <summary>
/// MU 貼圖格式 ↔ PNG 的雙向轉換。
/// </summary>
/// <remarks>
/// 這是「替換美術資源」最直接的一段：把 <c>.OZJ</c> 匯出成 PNG、在任何繪圖軟體裡改、再匯回去。
///
/// 三種格式的實情（都踩過）：
/// <list type="bullet">
/// <item><b>OZJ</b>：<b>不是</b>純 JPEG。前 24 個位元組是自訂標頭，而且內容剛好是後面
/// JPEG 起始 24 個位元組的複本。<c>OZJReader</c> 讀第 17 個位元組當「是否由上而下」的旗標
/// ——它落在 JFIF 的密度欄位裡，官方資源一律非零，所以實際上從不翻轉。
/// 寫入時照抄同一個慣例（複製 JPEG 前 24 byte、確保第 17 個非零），讀回來就是同一張圖。</item>
/// <item><b>OZT</b>：16 byte 標頭 + <c>width/height/depth/descriptor</c> + <b>由下而上</b>的 RGBA。
/// 官方檔案的那 16 個位元組是固定樣式，照抄。</item>
/// <item><b>OZD</b>：ModulusCryptor 加密的 DDS（DXT 壓縮）。<c>Client.Data</c> 只有解密沒有加密，
/// 所以<b>只能匯出、不能匯入</b>。要換 OZD 的貼圖就存成同名的 <c>.OZT</c> ——
/// <c>TextureResolver</c> 的副檔名順序會先找到 OZT。這條路比自己寫一個加密器可靠得多。</item>
/// </list>
/// </remarks>
public static class TextureIO
{
    /// <summary>OZJ 的自訂標頭長度。</summary>
    private const int OzjHeaderSize = 24;

    /// <summary>OZJReader 讀這個位置當「由上而下」旗標。</summary>
    private const int OzjTopDownFlagOffset = 17;

    /// <summary>OZT 在 width 欄位之前的固定前置位元組（取自官方資源，兩個樣本一致）。</summary>
    private static readonly byte[] OztHeader =
        [0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

    public sealed record ImportResult(bool Success, string Message);

    // ── 匯出 ─────────────────────────────────────────────────────

    /// <summary>把任何支援的貼圖解成 RGBA 影像。壓縮的 OZD 會在 CPU 上解開。</summary>
    public static Image<Rgba32> Decode(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();

        if (extension is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tga")
            return Image.Load<Rgba32>(path);

        var data = extension switch
        {
            ".ozj" => new OZJReader().Load(path).GetAwaiter().GetResult(),
            ".ozt" => new OZTReader().Load(path).GetAwaiter().GetResult(),
            ".ozp" => new OZPReader().Load(path).GetAwaiter().GetResult(),
            ".ozd" => new OZDReader().Load(path).GetAwaiter().GetResult(),
            _ => throw new NotSupportedException($"不支援的貼圖格式：{extension}"),
        };

        return ToImage(data, extension);
    }

    public static void ExportPng(string sourcePath, string destinationPath)
    {
        using var image = Decode(sourcePath);

        string? directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        image.SaveAsPng(destinationPath);
    }

    private static Image<Rgba32> ToImage(TextureData data, string extension)
    {
        byte[] pixels;

        if (data.IsCompressed)
        {
            // 桌面 GPU 讀得懂 DXT，但要存成 PNG 就得自己解開。
            // 沿用 Client.Main 既有的軟體解壓（Android/iOS 也是用它），不另寫一份。
            pixels = data.Format switch
            {
                TextureSurfaceFormat.Dxt1 => DxtDecoder.DecompressDXT1(data.Data, data.Width, data.Height),
                TextureSurfaceFormat.Dxt3 => DxtDecoder.DecompressDXT3(data.Data, data.Width, data.Height),
                TextureSurfaceFormat.Dxt5 => DxtDecoder.DecompressDXT5(data.Data, data.Width, data.Height),
                _ => throw new NotSupportedException($"不支援的壓縮格式：{data.Format}"),
            };

            return Image.LoadPixelData<Rgba32>(pixels, data.Width, data.Height);
        }

        pixels = new byte[data.Width * data.Height * 4];

        for (int i = 0; i < data.Width * data.Height; i++)
        {
            int source = i * data.Components;
            int destination = i * 4;

            // 每個 reader 吐出來的通道順序不同，這裡與 TextureDecoder 的對應表一致。
            if (extension == ".ozt")
            {
                pixels[destination] = data.Data[source + 2];
                pixels[destination + 1] = data.Data[source + 1];
                pixels[destination + 2] = data.Data[source];
                pixels[destination + 3] = data.Data[source + 3];
            }
            else if (data.Components >= 4)
            {
                pixels[destination] = data.Data[source];
                pixels[destination + 1] = data.Data[source + 1];
                pixels[destination + 2] = data.Data[source + 2];
                pixels[destination + 3] = data.Data[source + 3];
            }
            else
            {
                pixels[destination] = data.Data[source];
                pixels[destination + 1] = data.Data[source + 1];
                pixels[destination + 2] = data.Data[source + 2];
                pixels[destination + 3] = 255;
            }
        }

        return Image.LoadPixelData<Rgba32>(pixels, data.Width, data.Height);
    }

    // ── 匯入 ─────────────────────────────────────────────────────

    /// <summary>
    /// 把一張圖寫成 MU 的貼圖格式。目標格式由 <paramref name="destinationPath"/> 的副檔名決定。
    /// </summary>
    public static ImportResult Import(string imagePath, string destinationPath, int jpegQuality = 92)
    {
        try
        {
            using var image = Image.Load<Rgba32>(imagePath);
            string extension = Path.GetExtension(destinationPath).ToLowerInvariant();

            switch (extension)
            {
                case ".ozj":
                    WriteOzj(image, destinationPath, jpegQuality);
                    return new ImportResult(true, $"已寫入 {Path.GetFileName(destinationPath)}（{image.Width}×{image.Height}，JPEG 品質 {jpegQuality}）");

                case ".ozt":
                    if (!IsPowerOfTwo(image.Width) || !IsPowerOfTwo(image.Height))
                    {
                        // OZTReader 會把尺寸進位到 2 的冪，非 2 的冪會在右下角留下未初始化的區塊。
                        return new ImportResult(false, $"OZT 的寬高必須是 2 的冪，目前是 {image.Width}×{image.Height}");
                    }

                    WriteOzt(image, destinationPath);
                    return new ImportResult(true, $"已寫入 {Path.GetFileName(destinationPath)}（{image.Width}×{image.Height}，RGBA）");

                case ".png":
                    image.SaveAsPng(destinationPath);
                    return new ImportResult(true, $"已寫入 {Path.GetFileName(destinationPath)}");

                case ".ozd":
                    return new ImportResult(false,
                        "OZD 是加密的 DXT，Client.Data 只有解密沒有加密。請改存成同名的 .OZT —— 載入時副檔名的搜尋順序會先找到它。");

                default:
                    return new ImportResult(false, $"不支援寫入 {extension}");
            }
        }
        catch (Exception ex)
        {
            return new ImportResult(false, $"{ex.GetType().Name}：{ex.Message}");
        }
        finally
        {
            TextureResolver.InvalidateAll();
        }
    }

    /// <summary>
    /// OZJ = 24 byte 標頭 + JPEG。標頭是 JPEG 前 24 個位元組的複本 ——
    /// 這正是官方檔案的樣子（見兩個樣本的 hexdump），照做就能被 <c>OZJReader</c> 正確讀回。
    /// </summary>
    private static void WriteOzj(Image<Rgba32> image, string path, int quality)
    {
        using var jpeg = new MemoryStream();
        image.SaveAsJpeg(jpeg, new JpegEncoder { Quality = quality });

        var payload = jpeg.ToArray();
        if (payload.Length < OzjHeaderSize)
            throw new InvalidOperationException("JPEG 編碼結果太短，無法組出 OZJ 標頭");

        var header = payload.AsSpan(0, OzjHeaderSize).ToArray();

        // 這個位元組是 OZJReader 的「由上而下」旗標。JFIF 的密度欄位一定非零，
        // 但保險起見強制設好 —— 為零的話讀回來會上下顛倒，而且不會有任何錯誤訊息。
        if (header[OzjTopDownFlagOffset] == 0)
            header[OzjTopDownFlagOffset] = 1;

        using var output = File.Create(path);
        output.Write(header);
        output.Write(payload);
    }

    /// <summary>OZT = 固定 16 byte 前置 + width/height/depth/descriptor + 由下而上的 RGBA。</summary>
    private static void WriteOzt(Image<Rgba32> image, string path)
    {
        using var output = File.Create(path);
        using var writer = new BinaryWriter(output);

        writer.Write(OztHeader);
        writer.Write((short)image.Width);
        writer.Write((short)image.Height);
        writer.Write((byte)32);
        writer.Write((byte)0x08);

        // 一次抓出整張的 RGBA，再逐列寫 —— ImageSharp 的逐列 API 在不同版本之間換過位置，
        // CopyPixelDataTo 是穩定的那一個。
        var pixels = new byte[image.Width * image.Height * 4];
        image.CopyPixelDataTo(pixels);

        int stride = image.Width * 4;

        // 由下而上：OZTReader 把檔案的第 y 列寫進輸出的第 (height-1-y) 列。
        for (int y = image.Height - 1; y >= 0; y--)
            writer.Write(pixels, y * stride, stride);
    }

    private static bool IsPowerOfTwo(int value) => value > 0 && (value & (value - 1)) == 0;
}
