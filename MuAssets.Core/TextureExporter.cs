using Client.Data.Texture;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MuAssets.Core;

/// <summary>
/// 把 MU 的貼圖檔轉成 PNG。**零引擎相依** —— 只用 Client.Data 的 reader 與 ImageSharp。
/// </summary>
/// <remarks>
/// 這份刻意不依賴 MonoGame，因為它要能在無頭 CLI 裡跑，
/// 編輯器的 <c>TextureDecoder</c> 則是「解碼 + 上傳 GPU」混在一起的引擎版本。
///
/// 每個 reader 的通道順序不同：
/// <c>.ozj</c> 是 RGB、<c>.ozt</c> 是 BGRA、<c>.ozp</c> 是 RGBA、<c>.ozd</c> 是壓縮的 DXT。
/// </remarks>
public static class TextureExporter
{
    /// <summary>客戶端會把要求的副檔名換成 reader 支援的再找，順序與 TextureLoader 一致。</summary>
    private static readonly string[] Extensions = ["ozj", "ozt", "ozd", "ozp", "jpg", "tga", "png", "bmp"];

    /// <summary>
    /// 找到並轉出貼圖。回傳寫出的 PNG 檔名（相對於 <paramref name="outputDirectory"/>），
    /// 找不到或轉不了回傳 null。
    /// </summary>
    public static string? Export(string modelDirectory, string texturePath, string outputDirectory)
    {
        string? source = Find(modelDirectory, texturePath);
        if (source is null)
            return null;

        string name = Path.GetFileNameWithoutExtension(source) + ".png";
        string destination = Path.Combine(outputDirectory, name);

        if (File.Exists(destination))
            return name;

        try
        {
            using var image = Decode(source);
            if (image is null)
                return null;

            Directory.CreateDirectory(outputDirectory);
            image.SaveAsPng(destination);
            return name;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>這張貼圖有沒有真的用到 alpha —— 決定 glTF 的 alphaMode。</summary>
    public static bool HasTransparency(string modelDirectory, string texturePath)
    {
        string? source = Find(modelDirectory, texturePath);
        if (source is null)
            return false;

        try
        {
            using var image = Decode(source);
            if (image is null)
                return false;

            for (int y = 0; y < image.Height; y++)
            {
                for (int x = 0; x < image.Width; x++)
                {
                    if (image[x, y].A < 250)
                        return true;
                }
            }
        }
        catch
        {
            // 讀不了就當作不透明。
        }

        return false;
    }

    private static Image<Rgba32>? Decode(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();

        return extension switch
        {
            ".ozj" => FromTextureData(new OZJReader().Load(path).GetAwaiter().GetResult(), ChannelOrder.Rgb),
            ".ozt" => FromTextureData(new OZTReader().Load(path).GetAwaiter().GetResult(), ChannelOrder.Bgra),
            ".ozp" => FromTextureData(new OZPReader().Load(path).GetAwaiter().GetResult(), ChannelOrder.Rgba),

            // .ozd 是壓縮的 DXT，CPU 端解壓不在這支工具的範圍，先跳過。
            ".ozd" => null,

            ".jpg" or ".jpeg" or ".png" or ".bmp" or ".tga" => Image.Load<Rgba32>(path),
            _ => null,
        };
    }

    private enum ChannelOrder
    {
        Rgb,
        Rgba,
        Bgra,
    }

    private static Image<Rgba32> FromTextureData(TextureData data, ChannelOrder order)
    {
        var image = new Image<Rgba32>(data.Width, data.Height);

        for (int y = 0; y < data.Height; y++)
        {
            for (int x = 0; x < data.Width; x++)
            {
                int src = ((y * data.Width) + x) * data.Components;

                image[x, y] = order switch
                {
                    ChannelOrder.Rgb => new Rgba32(data.Data[src], data.Data[src + 1], data.Data[src + 2], 255),
                    ChannelOrder.Bgra => new Rgba32(data.Data[src + 2], data.Data[src + 1], data.Data[src], data.Data[src + 3]),
                    _ => new Rgba32(data.Data[src], data.Data[src + 1], data.Data[src + 2], data.Data[src + 3]),
                };
            }
        }

        return image;
    }

    /// <summary>換副檔名 + 大小寫容錯 + 也找 texture/ 子目錄，與客戶端的找法一致。</summary>
    private static string? Find(string directory, string texturePath)
    {
        if (string.IsNullOrWhiteSpace(texturePath))
            return null;

        string baseName = Path.GetFileNameWithoutExtension(texturePath);

        foreach (var extension in Extensions)
        {
            string candidate = Resolve(Path.Combine(directory, $"{baseName}.{extension}"));
            if (candidate is not null)
                return candidate;

            string nested = Resolve(Path.Combine(directory, "texture", $"{baseName}.{extension}"));
            if (nested is not null)
                return nested;
        }

        return null;
    }

    private static string? Resolve(string path)
    {
        if (File.Exists(path))
            return path;

        string? directory = Path.GetDirectoryName(path);
        string name = Path.GetFileName(path);

        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            return null;

        return Directory.EnumerateFiles(directory)
            .FirstOrDefault(f => string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase));
    }
}
