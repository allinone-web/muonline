using System.Drawing;
using Client.Data.ATT;
using Client.Data.MAP;
using Client.Data.OBJS;
using Client.Data.OZB;

namespace MuAssets.Core;

/// <summary>
/// 編輯器持有的地圖資料 —— 一份可改的複本，與畫面上那個世界分開。
/// </summary>
/// <remarks>
/// 為什麼不直接讀 <c>TerrainControl</c> 的資料：那份是渲染用的，欄位是 private，
/// 而且它的生命週期跟著世界走。編輯器需要一份自己的、可以隨便改、能存回檔案的資料 ——
/// 筆刷改這裡，再把變動推進渲染端（Phase 3），存檔時直接交給 Client.Data 的 Writer。
/// </remarks>
public sealed class MapDocument
{
    public const int Size = MuConstants.TerrainSize;
    public const int CellCount = MuConstants.CellCount;

    public int WorldIndex { get; init; }

    public byte MapVersion { get; set; }
    public byte MapNumber { get; set; }
    public byte[] Layer1 { get; set; } = new byte[CellCount];
    public byte[] Layer2 { get; set; } = new byte[CellCount];
    public byte[] Alpha { get; set; } = new byte[CellCount];

    public byte AttVersion { get; set; }
    public byte AttIndex { get; set; }
    public TWFlags[] Attributes { get; set; } = new TWFlags[CellCount];

    public byte ObjVersion { get; set; }
    public List<MapObjectInstance> Objects { get; set; } = [];

    /// <summary>生怪與 NPC 區域。客戶端的 .obj 不含這些，它們只進伺服器。</summary>
    public List<SpawnArea> Spawns { get; set; } = [];

    public OZB? Height { get; set; }
    public OZB? Light { get; set; }

    /// <summary>載入過程中沒能讀到的部分，UI 直接顯示出來而不是靜靜地少資料。</summary>
    public List<string> Warnings { get; } = [];

    /// <summary>
    /// 一張全新的空白地圖：整片平地、全部可走、沒有物件。
    /// </summary>
    /// <remarks>
    /// 幾個值不是隨便挑的：
    ///
    /// <list type="bullet">
    ///   <item><b>Layer2 全填 255</b> —— 那是「這格沒有第二層」的哨兵值。
    ///         填 0 的話整張圖會被第二層的 0 號貼圖蓋掉。</item>
    ///   <item><b>高度全 0</b> —— 平地。高度是 0–255 的刻度，渲染時乘 1.5。</item>
    ///   <item><b>光照全 128</b> —— 渲染端會把烘焙光乘 2，128 對應 1.0，
    ///         也就是不打亮也不壓暗。填 0 會得到一張全黑的地圖。</item>
    /// </list>
    ///
    /// 版本欄位跟著 World1：舊版 XOR 格式是編輯器唯一寫得出來的格式
    /// （Season 20 的 ModulusCryptor 只有解密），而讀取端沒有魔數時就走舊格式路徑。
    /// </remarks>
    /// <param name="worldIndex">客戶端的 world 編號（OpenMU 的地圖編號 + 1）。</param>
    /// <param name="groundTile">整張圖的底層貼圖索引，預設 0（TileGrass01）。</param>
    public static MapDocument CreateBlank(int worldIndex, byte groundTile = 0)
    {
        var document = new MapDocument
        {
            WorldIndex = worldIndex,
            MapVersion = 0,
            MapNumber = (byte)worldIndex,
            AttVersion = 0,
            AttIndex = (byte)worldIndex,
            ObjVersion = 0,
        };

        Array.Fill(document.Layer1, groundTile);
        Array.Fill(document.Layer2, TerrainTextureMapping.NoLayerIndex);
        Array.Fill(document.Alpha, (byte)0);
        Array.Fill(document.Attributes, (TWFlags)0);

        document.Height = new OZB
        {
            Version = 0,
            Width = Size,
            Height = Size,
            FileType = OZBFileType.BM8,
            Data = CreateSurface(Color.FromArgb(0, 0, 0)),
        };

        document.Light = new OZB
        {
            Version = 0,
            Width = Size,
            Height = Size,
            FileType = OZBFileType.BM6,
            Data = CreateSurface(Color.FromArgb(128, 128, 128)),
        };

        return document;
    }

    private static Color[] CreateSurface(Color value)
    {
        var data = new Color[CellCount];
        Array.Fill(data, value);
        return data;
    }

    public static async Task<MapDocument> LoadAsync(WorldEntry entry)
    {
        var document = new MapDocument { WorldIndex = entry.Index };

        string Path(string name) => System.IO.Path.Combine(entry.Directory, name);

        await Try(document, "map", async () =>
        {
            var map = await new MapReader().Load(Path($"EncTerrain{entry.Index}.map"));
            document.MapVersion = map.Version;
            document.MapNumber = map.MapNumber;
            document.Layer1 = map.Layer1;
            document.Layer2 = map.Layer2;
            document.Alpha = map.Alpha;
        });

        await Try(document, "att", async () =>
        {
            var att = await new ATTReader().Load(Path($"EncTerrain{entry.Index}.att"));
            document.AttVersion = att.Version;
            document.AttIndex = att.Index;
            document.Attributes = att.TerrainWall;
        });

        if (entry.HasObj)
        {
            await Try(document, "obj", async () =>
            {
                var obj = await new OBJReader().Load(Path($"EncTerrain{entry.Index}.obj"));
                document.ObjVersion = obj.Version;
                document.Objects = obj.Objects.Select(MapObjectInstance.From).ToList();
            });
        }

        await Try(document, "TerrainHeight.OZB", async () =>
            document.Height = await new OZBReader().Load(Path("TerrainHeight.OZB")));

        await Try(document, "TerrainLight.OZB", async () =>
            document.Light = await new OZBReader().Load(Path("TerrainLight.OZB")));

        return document;
    }

    /// <summary>取某一格的高度（0–255）。高度圖缺失或損毀時回傳 0。</summary>
    public byte HeightAt(int index)
    {
        var data = Height?.Data;
        return data is not null && index < data.Length ? data[index].R : (byte)0;
    }

    public Color LightAt(int index)
    {
        var data = Light?.Data;
        return data is not null && index < data.Length ? data[index] : Color.Black;
    }

    /// <summary>統計每個貼圖索引用了幾格，供資產面板標出「這張圖實際在用哪些貼圖」。</summary>
    public Dictionary<byte, int> TileUsage(bool layer2)
    {
        var source = layer2 ? Layer2 : Layer1;
        var counts = new Dictionary<byte, int>();

        foreach (var value in source)
        {
            // Layer2 的 255 是「這格沒有第二層」的哨兵值，不是貼圖索引。
            if (layer2 && value == TerrainTextureMapping.NoLayerIndex)
                continue;

            counts[value] = counts.GetValueOrDefault(value) + 1;
        }

        return counts;
    }

    private static async Task Try(MapDocument document, string label, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            document.Warnings.Add($"{label}：{ex.Message}");
        }
    }
}
