using System.Text.Json;
using System.Text.Json.Serialization;
using Client.Data.ATT;
using Client.Data.OZB;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using DrawingColor = System.Drawing.Color;

namespace MuAssets.Core;

/// <summary>
/// 專案格式的讀寫：`map.json` + 六張 PNG。
/// </summary>
/// <remarks>
/// Schema 與讀寫都只定義在 MuAssets.Core；MapTool 與編輯器共同引用，避免兩份格式漂移。
/// 設計成 git 友善：純量與物件在 JSON 裡可以 diff，逐格資料是 PNG（可以直接用影像工具看與改）。
/// </remarks>
public static class MapProjectIo
{
    public const string ProjectFileName = "map.json";

    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,

        // 官方資源裡有 .obj 物件帶著 NaN / Infinity 座標（例如 World92）。
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public static async Task SaveAsync(MapDocument document, string projectDirectory)
    {
        Directory.CreateDirectory(projectDirectory);

        var project = new MapProject
        {
            WorldIndex = document.WorldIndex,
            MapVersion = document.MapVersion,
            MapNumber = document.MapNumber,
            AttVersion = document.AttVersion,
            AttIndex = document.AttIndex,
            ObjVersion = document.ObjVersion,
            ObjMapNumber = document.WorldIndex,
            Objects = document.Objects.Select(MapProjectObject.From).ToList(),
            Spawns = document.Spawns.Select(s => s.Clone()).ToList(),
            HeightVersion = document.Height?.Version ?? 0,
            HeightFileType = document.Height?.FileType ?? OZBFileType.BM8,
            HeightHeaderBase64 = Encode(document.Height?.RawHeader),
            LightVersion = document.Light?.Version ?? 0,
            LightFileType = document.Light?.FileType ?? OZBFileType.BM6,
            LightHeaderBase64 = Encode(document.Light?.RawHeader),
        };

        SaveGrayscale(Path.Combine(projectDirectory, "layer1.png"), document.Layer1);
        SaveGrayscale(Path.Combine(projectDirectory, "layer2.png"), document.Layer2);
        SaveGrayscale(Path.Combine(projectDirectory, "alpha.png"), document.Alpha);

        var attributes = new byte[document.Attributes.Length];
        for (int i = 0; i < attributes.Length; i++)
            attributes[i] = (byte)((ushort)document.Attributes[i] & 0xFF);

        SaveGrayscale(Path.Combine(projectDirectory, "attribute.png"), attributes);

        if (document.Height is not null)
            SaveOzb(Path.Combine(projectDirectory, "height.png"), document.Height);

        if (document.Light is not null)
            SaveOzb(Path.Combine(projectDirectory, "light.png"), document.Light);

        await File.WriteAllTextAsync(
            Path.Combine(projectDirectory, ProjectFileName),
            JsonSerializer.Serialize(project, JsonOptions));
    }

    public static async Task<MapDocument> LoadAsync(string projectDirectory)
    {
        var project = await ReadAsync(projectDirectory);

        var document = new MapDocument
        {
            WorldIndex = project.WorldIndex,
            MapVersion = project.MapVersion,
            MapNumber = project.MapNumber,
            AttVersion = project.AttVersion,
            AttIndex = project.AttIndex,
            ObjVersion = project.ObjVersion,
            Objects = project.Objects.Select(o => o.ToDocumentObject()).ToList(),
            Spawns = project.Spawns,
        };

        document.Layer1 = LoadRequiredGrayscale(projectDirectory, "layer1.png");
        document.Layer2 = LoadRequiredGrayscale(projectDirectory, "layer2.png");
        document.Alpha = LoadRequiredGrayscale(projectDirectory, "alpha.png");

        byte[] attributes = LoadRequiredGrayscale(projectDirectory, "attribute.png");
        for (int i = 0; i < attributes.Length; i++)
            document.Attributes[i] = (TWFlags)attributes[i];

        document.Height = LoadOzb(
            Path.Combine(projectDirectory, "height.png"),
            project.HeightVersion, project.HeightFileType, project.HeightHeaderBase64);

        document.Light = LoadOzb(
            Path.Combine(projectDirectory, "light.png"),
            project.LightVersion, project.LightFileType, project.LightHeaderBase64);

        ValidateDocument(project, document);

        return document;
    }

    public static async Task<MapProject> ReadAsync(string projectDirectory)
    {
        string jsonPath = Path.Combine(projectDirectory, ProjectFileName);
        if (!File.Exists(jsonPath))
            throw new FileNotFoundException($"找不到 {jsonPath}", jsonPath);

        return JsonSerializer.Deserialize<MapProject>(await File.ReadAllTextAsync(jsonPath), JsonOptions)
               ?? throw new InvalidDataException($"無法解析 {jsonPath}");
    }

    public static byte[] LoadRequiredGrayscale(string projectDirectory, string fileName)
    {
        string path = Path.Combine(projectDirectory, fileName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"專案缺少必要 PNG：{path}", path);

        using var image = Image.Load<L8>(path);
        RequireTerrainSize(path, image.Width, image.Height);
        var values = new byte[MapDocument.CellCount];

        for (int y = 0; y < MapDocument.Size; y++)
        for (int x = 0; x < MapDocument.Size; x++)
            values[(y * MapDocument.Size) + x] = image[x, y].PackedValue;

        return values;
    }

    private static string? Encode(byte[]? data) => data is null ? null : Convert.ToBase64String(data);

    private static void SaveGrayscale(string path, byte[] values)
    {
        using var image = new Image<L8>(MapDocument.Size, MapDocument.Size);

        for (int y = 0; y < MapDocument.Size; y++)
        {
            for (int x = 0; x < MapDocument.Size; x++)
                image[x, y] = new L8(values[(y * MapDocument.Size) + x]);
        }

        image.SaveAsPng(path);
    }

    /// <summary>BM8 是 8-bit 灰階（值在 R 通道）、BM6 是 24-bit RGB，各存成對應的 PNG。</summary>
    private static void SaveOzb(string path, OZB ozb)
    {
        if (ozb.FileType == OZBFileType.BM8)
        {
            using var gray = new Image<L8>(ozb.Width, ozb.Height);
            for (int y = 0; y < ozb.Height; y++)
            {
                for (int x = 0; x < ozb.Width; x++)
                    gray[x, y] = new L8(ozb.Data[(y * ozb.Width) + x].R);
            }

            gray.SaveAsPng(path);
            return;
        }

        using var rgb = new Image<Rgb24>(ozb.Width, ozb.Height);
        for (int y = 0; y < ozb.Height; y++)
        {
            for (int x = 0; x < ozb.Width; x++)
            {
                var c = ozb.Data[(y * ozb.Width) + x];
                rgb[x, y] = new Rgb24(c.R, c.G, c.B);
            }
        }

        rgb.SaveAsPng(path);
    }

    private static OZB LoadOzb(string path, byte version, string fileType, string? headerBase64)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"專案缺少必要 PNG：{path}", path);

        byte[]? header = string.IsNullOrEmpty(headerBase64) ? null : Convert.FromBase64String(headerBase64);

        if (fileType == OZBFileType.BM8)
        {
            using var gray = Image.Load<L8>(path);
            RequireTerrainSize(path, gray.Width, gray.Height);
            var data = new DrawingColor[gray.Width * gray.Height];

            for (int y = 0; y < gray.Height; y++)
            {
                for (int x = 0; x < gray.Width; x++)
                {
                    byte value = gray[x, y].PackedValue;
                    data[(y * gray.Width) + x] = DrawingColor.FromArgb(255, value, 0, 0);
                }
            }

            return new OZB
            {
                Version = version,
                Width = gray.Width,
                Height = gray.Height,
                FileType = OZBFileType.BM8,
                RawHeader = header,
                Data = data,
            };
        }

        if (fileType != OZBFileType.BM6)
            throw new InvalidDataException($"{path} 的 OZB file type '{fileType}' 非法；只允許 BM8 或 BM6。");

        using var image = Image.Load<Rgb24>(path);
        RequireTerrainSize(path, image.Width, image.Height);
        var pixels = new DrawingColor[image.Width * image.Height];

        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                var p = image[x, y];
                pixels[(y * image.Width) + x] = DrawingColor.FromArgb(255, p.R, p.G, p.B);
            }
        }

        return new OZB
        {
            Version = version,
            Width = image.Width,
            Height = image.Height,
            FileType = OZBFileType.BM6,
            RawHeader = header,
            Data = pixels,
        };
    }

    private static void RequireTerrainSize(string path, int width, int height)
    {
        if (width != MapDocument.Size || height != MapDocument.Size)
            throw new InvalidDataException($"{path} 尺寸必須是 {MapDocument.Size}x{MapDocument.Size}，實際為 {width}x{height}。");
    }

    private static void ValidateDocument(MapProject project, MapDocument document)
    {
        if (project.WorldIndex <= 0)
            throw new InvalidDataException($"WorldIndex={project.WorldIndex} 非法；必須大於 0。");
        if (project.MapNumber < 0 || project.AttIndex < 0 || project.ObjMapNumber < 0)
            throw new InvalidDataException("MapNumber、AttIndex 與 ObjMapNumber 不得為負數。");
        if (project.ObjVersion > 5)
            throw new InvalidDataException($"ObjVersion={project.ObjVersion} 非法；只允許 0..5。");

        foreach (var value in document.Layer1.Concat(document.Layer2.Where(v => v != Client.Data.MAP.TerrainTextureMapping.NoLayerIndex)))
        {
            if (!Client.Data.MAP.TerrainTextureMapping.Default.ContainsKey(value))
                throw new InvalidDataException($"地形貼圖索引 {value} 沒有合法映射；禁止忽略或替換成預設貼圖。");
        }

        foreach (var item in project.Objects)
        {
            if (item.Type < 0)
                throw new InvalidDataException($"物件 Type={item.Type} 是非法引用。");
            if (!float.IsFinite(item.Scale) || item.Scale <= 0f)
                throw new InvalidDataException($"物件 Type={item.Type} 的 Scale={item.Scale} 非法。");
        }
    }
}
