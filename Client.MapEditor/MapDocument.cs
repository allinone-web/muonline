using System.Drawing;
using Client.Data;
using Client.Data.ATT;
using Client.Data.MAP;
using Client.Data.OBJS;
using Client.Data.OZB;

namespace Client.MapEditor;

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
    public const int Size = Constants.TERRAIN_SIZE;
    public const int CellCount = Size * Size;

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
