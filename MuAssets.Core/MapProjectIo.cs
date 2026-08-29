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
/// <b>格式刻意與 <c>tools/MapTool</c> 的 dump/build 完全相同</b>，
/// 所以編輯器存出來的專案可以直接 <c>maptool build</c>，
/// CLI dump 出來的也可以在編輯器裡打開。欄位名稱改動要兩邊一起改。
///
/// 設計成 git 友善：純量與物件在 JSON 裡可以 diff，逐格資料是 PNG（可以直接用影像工具看與改）。
/// </remarks>
public static class MapProjectIo
{
    public const string ProjectFileName = "map.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,

        // 官方資源裡有 .obj 物件帶著 NaN / Infinity 座標（例如 World92）。
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public static async Task SaveAsync(MapDocument document, string projectDirectory)
    {
        Directory.CreateDirectory(projectDirectory);

        var project = new ProjectFile
        {
            WorldIndex = document.WorldIndex,
            MapVersion = document.MapVersion,
            MapNumber = document.MapNumber,
            AttVersion = document.AttVersion,
            AttIndex = document.AttIndex,
            ObjVersion = document.ObjVersion,
            ObjMapNumber = document.WorldIndex,
            Objects = document.Objects.Select(ObjectRecord.From).ToList(),
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
        string jsonPath = Path.Combine(projectDirectory, ProjectFileName);
        if (!File.Exists(jsonPath))
            throw new FileNotFoundException($"找不到 {jsonPath}", jsonPath);

        var project = JsonSerializer.Deserialize<ProjectFile>(await File.ReadAllTextAsync(jsonPath), JsonOptions)
                      ?? throw new InvalidDataException($"無法解析 {jsonPath}");

        var document = new MapDocument
        {
            WorldIndex = project.WorldIndex,
            MapVersion = project.MapVersion,
            MapNumber = project.MapNumber,
            AttVersion = project.AttVersion,
            AttIndex = project.AttIndex,
            ObjVersion = project.ObjVersion,
            Objects = project.Objects.Select(o => o.To()).ToList(),
            Spawns = project.Spawns,
        };

        document.Layer1 = LoadGrayscale(Path.Combine(projectDirectory, "layer1.png")) ?? document.Layer1;
        document.Layer2 = LoadGrayscale(Path.Combine(projectDirectory, "layer2.png")) ?? document.Layer2;
        document.Alpha = LoadGrayscale(Path.Combine(projectDirectory, "alpha.png")) ?? document.Alpha;

        if (LoadGrayscale(Path.Combine(projectDirectory, "attribute.png")) is byte[] attributes)
        {
            for (int i = 0; i < Math.Min(attributes.Length, document.Attributes.Length); i++)
                document.Attributes[i] = (TWFlags)attributes[i];
        }

        document.Height = LoadOzb(
            Path.Combine(projectDirectory, "height.png"),
            project.HeightVersion, project.HeightFileType, project.HeightHeaderBase64);

        document.Light = LoadOzb(
            Path.Combine(projectDirectory, "light.png"),
            project.LightVersion, project.LightFileType, project.LightHeaderBase64);

        return document;
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

    private static byte[]? LoadGrayscale(string path)
    {
        if (!File.Exists(path))
            return null;

        using var image = Image.Load<L8>(path);
        var values = new byte[image.Width * image.Height];

        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
                values[(y * image.Width) + x] = image[x, y].PackedValue;
        }

        return values;
    }

    private static OZB? LoadOzb(string path, byte version, string fileType, string? headerBase64)
    {
        if (!File.Exists(path))
            return null;

        byte[]? header = string.IsNullOrEmpty(headerBase64) ? null : Convert.FromBase64String(headerBase64);

        if (fileType == OZBFileType.BM8)
        {
            using var gray = Image.Load<L8>(path);
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

        using var image = Image.Load<Rgb24>(path);
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

    /// <summary>與 <c>tools/MapTool</c> 的 <c>MapProject</c> 逐欄對應，兩邊要一起改。</summary>
    private sealed class ProjectFile
    {
        public int WorldIndex { get; set; }
        public byte MapVersion { get; set; }
        public byte MapNumber { get; set; }
        public byte AttVersion { get; set; }
        public byte AttIndex { get; set; }
        public byte ObjVersion { get; set; }
        public int ObjMapNumber { get; set; }
        public List<ObjectRecord> Objects { get; set; } = [];

        /// <summary>生怪區。MapTool 目前不處理這個欄位，會原樣忽略。</summary>
        public List<SpawnArea> Spawns { get; set; } = [];
        public byte HeightVersion { get; set; }
        public string HeightFileType { get; set; } = OZBFileType.BM8;
        public string? HeightHeaderBase64 { get; set; }
        public byte LightVersion { get; set; }
        public string LightFileType { get; set; } = OZBFileType.BM6;
        public string? LightHeaderBase64 { get; set; }
    }

    private sealed class ObjectRecord
    {
        public short Type { get; set; }
        public float[] Position { get; set; } = [0, 0, 0];
        public float[] Angle { get; set; } = [0, 0, 0];
        public float Scale { get; set; } = 1f;

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public byte? UnknownX { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public byte? UnknownY { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public byte? UnknownZ { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public float[]? Lightning { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public byte? UnknownByte { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public float? UnknownFloat1 { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public float? UnknownFloat2 { get; set; }

        // 語義標註。沒有標註的物件（絕大多數）不寫進 JSON，
        // 這樣既省檔案大小，舊專案讀進來也自然是預設值。
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Role { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? RoleId { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string[]? Tags { get; set; }

        public static ObjectRecord From(MapObjectInstance instance) => new()
        {
            Type = instance.Type,
            Position = [instance.Position.X, instance.Position.Y, instance.Position.Z],
            Angle = [instance.Angle.X, instance.Angle.Y, instance.Angle.Z],
            Scale = instance.Scale,
            UnknownX = instance.UnknownX,
            UnknownY = instance.UnknownY,
            UnknownZ = instance.UnknownZ,
            Lightning = [instance.Lightning.X, instance.Lightning.Y, instance.Lightning.Z],
            UnknownByte = instance.UnknownByte,
            UnknownFloat1 = instance.UnknownFloat1,
            UnknownFloat2 = instance.UnknownFloat2,
            Role = instance.HasRole ? instance.Role : null,
            RoleId = instance.RoleId == 0 ? null : instance.RoleId,
            Tags = instance.Tags.Length == 0 ? null : instance.Tags,
        };

        public MapObjectInstance To() => new()
        {
            Type = Type,
            Position = ToVector(Position),
            Angle = ToVector(Angle),
            Scale = Scale,
            UnknownX = UnknownX ?? 0,
            UnknownY = UnknownY ?? 0,
            UnknownZ = UnknownZ ?? 0,
            Lightning = Lightning is null ? default : ToVector(Lightning),
            UnknownByte = UnknownByte ?? 0,
            UnknownFloat1 = UnknownFloat1 ?? 0f,
            UnknownFloat2 = UnknownFloat2 ?? 0f,
            Role = Role ?? string.Empty,
            RoleId = RoleId ?? 0,
            Tags = Tags ?? [],
        };

        private static System.Numerics.Vector3 ToVector(float[] values)
            => values.Length >= 3 ? new(values[0], values[1], values[2]) : default;
    }
}
