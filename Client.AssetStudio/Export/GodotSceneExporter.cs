using System.Text.Json;
using System.Text.Json.Serialization;
using Client.AssetStudio.Catalog;
using Client.AssetStudio.Textures;
using Client.Data.MAP;
using MuAssets.Core;

namespace Client.AssetStudio.Export;

/// <summary>
/// 把一張 MU 地圖整包匯出成引擎中立的形式，給 Godot 原型用。
/// </summary>
/// <remarks>
/// 這是 <c>STRATEGY.md</c> §6.8.7「一天的技術驗證」的資料端：
/// <b>MU 的 3D 地圖 ＋ Lineage 的 2D 角色</b>（RO 式）好不好看，
/// 是整個美術方案成立與否的關鍵，而它只能靠看畫面回答。
///
/// 輸出刻意只有三種格式，與 <c>docs/引擎轉換方案-工具與客戶端遷移到Godot.md</c> 的鐵律一致：
/// <code>
/// map.json + 六張 PNG   地形（沿用地圖編輯器的專案格式，一個欄位都沒改）
/// tiles/&lt;索引&gt;.png      地形貼圖
/// models/&lt;type&gt;/*.gltf  物件模型
/// scene.json            物件擺放與世界常數
/// </code>
/// 沒有 <c>.tscn</c>、沒有 <c>.tres</c>。Godot 端從這些來源建場景，
/// 隨時刪掉整個 <c>.godot/</c> 都能重建。
///
/// <b>物件模型的路徑規則有一個例外</b>：
/// 通則是 <c>Object{world}/Object{type+1:00}.bmd</c>，但 World1（勒瑞西亞）
/// 用的是具名檔（<c>Tree01.bmd</c>、<c>Bonfire01.bmd</c>…），對不上通則。
/// 這裡不猜 —— 對不上就照實回報，讓使用者換一張圖或補對應表。
/// </remarks>
public static class GodotSceneExporter
{
    public sealed record Options(
        int MaxObjectTypes = 64,
        bool ExportObjects = true,
        float SampleFps = GltfImporterDefaults.SampleFps);

    public sealed record Result(
        int WorldIndex,
        string Directory,
        int TileTextures,
        int ObjectTypes,
        int ObjectTypesExported,
        int ObjectInstances,
        string[] Warnings);

    public static Result Export(string dataPath, int worldIndex, string outputDirectory, Options? options = null)
    {
        options ??= new Options();
        var warnings = new List<string>();

        var world = WorldDirectory.Discover(dataPath).FirstOrDefault(w => w.Index == worldIndex)
            ?? throw new DirectoryNotFoundException($"找不到 World{worldIndex}");

        var document = MapDocument.LoadAsync(world).GetAwaiter().GetResult();
        warnings.AddRange(document.Warnings);

        Directory.CreateDirectory(outputDirectory);
        MapProjectIo.SaveAsync(document, outputDirectory).GetAwaiter().GetResult();

        int tiles = ExportTiles(document, world, outputDirectory, warnings);

        var placements = document.Objects
            .Select(o => new ObjectPlacement(
                o.Type,
                [o.Position.X, o.Position.Y, o.Position.Z],
                [o.Angle.X, o.Angle.Y, o.Angle.Z],
                o.Scale))
            .ToList();

        var types = placements.Select(p => p.Type).Distinct().OrderBy(t => t).ToArray();
        var models = new Dictionary<short, string>();

        if (options.ExportObjects)
            models = ExportObjects(dataPath, worldIndex, types, placements, outputDirectory, options, warnings);

        var scene = new SceneFile
        {
            WorldIndex = worldIndex,
            WorldName = world.Name,
            TerrainSize = MapDocument.Size,
            TerrainScale = MuConstants.TerrainScale,

            // 遊戲把高度圖的灰階值乘 1.5 當世界高度（TerrainLoader）。
            // Godot 端要用同一個係數，不然物件會浮在地形上或陷進去。
            HeightScale = 1.5f,

            Models = models.ToDictionary(m => m.Key.ToString(), m => m.Value),
            Objects = placements,
        };

        File.WriteAllText(
            Path.Combine(outputDirectory, "scene.json"),
            JsonSerializer.Serialize(scene, SceneJsonOptions));

        return new Result(worldIndex, outputDirectory, tiles, types.Length, models.Count,
                          placements.Count, warnings.ToArray());
    }

    // ── 地形貼圖 ─────────────────────────────────────────────────

    private static int ExportTiles(MapDocument document, WorldEntry world, string outputDirectory, List<string> warnings)
    {
        string tileDirectory = Path.Combine(outputDirectory, "tiles");
        Directory.CreateDirectory(tileDirectory);

        var used = document.Layer1.Concat(document.Layer2)
            .Where(index => index != TerrainTextureMapping.NoLayerIndex)
            .Distinct()
            .OrderBy(index => index)
            .ToArray();

        int exported = 0;

        foreach (byte index in used)
        {
            if (!TerrainTextureMapping.Default.TryGetValue(index, out var fileName))
            {
                warnings.Add($"貼圖索引 {index} 沒有對應的檔名（TerrainTextureMapping 缺這一筆）");
                continue;
            }

            var resolution = TextureResolver.Resolve(world.Directory, fileName);

            if (!resolution.Found)
            {
                warnings.Add($"貼圖索引 {index} 的檔案不存在：{fileName}");
                continue;
            }

            try
            {
                TextureIO.ExportPng(resolution.FullPath!, Path.Combine(tileDirectory, $"{index}.png"));
                exported++;
            }
            catch (Exception ex)
            {
                warnings.Add($"貼圖索引 {index} 轉檔失敗：{ex.Message}");
            }
        }

        return exported;
    }

    // ── 物件模型 ─────────────────────────────────────────────────

    private static Dictionary<short, string> ExportObjects(
        string dataPath, int worldIndex, short[] types, List<ObjectPlacement> placements,
        string outputDirectory, Options options, List<string> warnings)
    {
        var models = new Dictionary<short, string>();

        // 先export最常出現的型別。原型只是要看畫面，全部匯出既慢又沒有必要 ——
        // 遮罩測試需要的是「有幾棟房子擋在角色前面」，不是 107 種樹都到齊。
        var byFrequency = placements
            .GroupBy(p => p.Type)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .Take(options.MaxObjectTypes)
            .ToArray();

        foreach (short type in byFrequency)
        {
            string relative = $"Object{worldIndex}/Object{type + 1:00}.bmd";
            string full = Path.Combine(dataPath, relative);

            if (!File.Exists(full))
            {
                warnings.Add($"物件 type {type} 找不到模型：{relative}"
                           + (worldIndex == 1 ? "（World1 用具名檔，對不上通則）" : string.Empty));
                continue;
            }

            try
            {
                string directory = Path.Combine(outputDirectory, "models", type.ToString());

                var exported = GltfExporter.Export(full, directory,
                    new GltfExporter.Options(options.SampleFps, ExportTextures: true, EntityKind.Effect));

                models[type] = Path.GetRelativePath(outputDirectory, exported.GltfPath).Replace('\\', '/');

                foreach (var warning in exported.Warnings)
                    warnings.Add($"物件 {type}：{warning}");
            }
            catch (Exception ex)
            {
                warnings.Add($"物件 type {type} 匯出失敗：{ex.GetType().Name} {ex.Message}");
            }
        }

        return models;
    }

    // ── scene.json ───────────────────────────────────────────────

    private static readonly JsonSerializerOptions SceneJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,

        // 官方資源裡有 .obj 物件帶 NaN / Infinity 座標（World92），與 map.json 同樣要放行。
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    private sealed class SceneFile
    {
        public int WorldIndex { get; set; }
        public string WorldName { get; set; } = string.Empty;
        public int TerrainSize { get; set; }
        public float TerrainScale { get; set; }
        public float HeightScale { get; set; }

        /// <summary>物件 type → 模型檔（相對於這個資料夾）。</summary>
        public Dictionary<string, string> Models { get; set; } = [];

        public List<ObjectPlacement> Objects { get; set; } = [];
    }

    /// <param name="Angle">尤拉角，單位是度。</param>
    public sealed record ObjectPlacement(short Type, float[] Position, float[] Angle, float Scale);
}

/// <summary>與匯入器共用的預設值，避免兩邊各寫一個魔術數字。</summary>
internal static class GltfImporterDefaults
{
    public const float SampleFps = 4f;
}
