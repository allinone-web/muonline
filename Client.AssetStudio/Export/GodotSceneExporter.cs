using System.Text.Json;
using SixLabors.ImageSharp;
using System.Text.Json.Serialization;
using Client.AssetStudio.Catalog;
using Client.AssetStudio.Textures;
using Client.Data;
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
        // 預設要蓋滿：一張圖大約 110 種物件，全部匯出只要 8 秒、11 MB。
        // 之前預設 64，結果地圖上有 92 個物件因為型別沒匯出而缺席 ——
        // 而那在畫面上看起來就是「這張圖跟遊戲裡不太一樣」，很難查。
        int MaxObjectTypes = 512,
        bool ExportObjects = true,
        float SampleFps = GltfImporterDefaults.SampleFps);

    public sealed record Result(
        int WorldIndex,
        string Directory,
        int TileTextures,
        int GrassTextures,
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
        int grass = ExportGrass(world, outputDirectory, warnings);
        ExportCamera(world, outputDirectory, warnings);
        double heightScale16 = ExportHeight16(document, outputDirectory, warnings);

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
            HeightScale16 = heightScale16,

            Models = models.ToDictionary(m => m.Key.ToString(), m => m.Value),
            Objects = placements,
        };

        File.WriteAllText(
            Path.Combine(outputDirectory, "scene.json"),
            JsonSerializer.Serialize(scene, SceneJsonOptions));

        InjectHeightScale16IntoMapJson(outputDirectory, heightScale16, warnings);

        return new Result(worldIndex, outputDirectory, tiles, grass, types.Length, models.Count,
                          placements.Count, warnings.ToArray());
    }

    // ── 草貼圖 ───────────────────────────────────────────────────

    /// <summary>
    /// <c>TileGrass0{1,2,3}.OZT → grass/{0,1,2}.png</c>。
    /// </summary>
    /// <remarks>
    /// 草不是地形貼圖：MU 用獨立的 OZT（帶 alpha），由客戶端的 GrassBuilder
    /// 在 layer1∈{0,1,2} 的格子上程序化長出來。這條鏈原本只在 RealmForge 的
    /// 舊 <c>import_mu_map.sh</c>（ozt2png.py）裡，中立包自己不帶 —— 於是
    /// <c>rf sync-mu-map</c> 同步出來的地圖沒有草。檔名固定三張、輸出編號
    /// 對齊客戶端 MuGrassBuilder 的 <c>grass/0..2.png</c> 約定；缺檔跳過
    /// （World4 本來就沒有 TileGrass02，與貼圖索引 1 缺檔同一件事）。
    /// </remarks>
    private static int ExportGrass(WorldEntry world, string outputDirectory, List<string> warnings)
    {
        int exported = 0;

        for (int i = 1; i <= 3; i++)
        {
            // 只認 .OZT，不走 TextureResolver —— 它會後援到同名的 .OZJ（不透明），
            // 而 World1 正好有 TileGrass02.OZJ 沒有 .OZT：草拿到不透明貼圖
            // 就是一片灰板。舊鏈（ozt2png.py）同樣只認 OZT、缺檔跳過。
            string? source = new[] { $"TileGrass0{i}.OZT", $"TileGrass0{i}.ozt" }
                .Select(name => Path.Combine(world.Directory, name))
                .FirstOrDefault(File.Exists);

            if (source is null)
                continue;

            try
            {
                string grassDirectory = Path.Combine(outputDirectory, "grass");
                Directory.CreateDirectory(grassDirectory);

                // 不走 TextureIO.ExportPng：它的 .ozt 分支會把 R/B 再換一次序。
                // 那是 tex-export ↔ tex-import 之間「PNG＝檔案位元組序」的內部約定
                // （成對抵銷，往返精確），但這裡的 PNG 是給 Godot 直接吃的——
                // 用那條路草會反色（實測 TGA 位元組裁決過）。OZTReader 本身
                // 已把 TGA 的 BGRA 換成 RGBA（它檔內的 Red/Blue 註解標反了），
                // 直接存就是正色。
                var data = new Client.Data.Texture.OZTReader().Load(source).GetAwaiter().GetResult();
                using var image = SixLabors.ImageSharp.Image.LoadPixelData<SixLabors.ImageSharp.PixelFormats.Rgba32>(
                    data.Data, data.Width, data.Height);
                SixLabors.ImageSharp.ImageExtensions.SaveAsPng(image, Path.Combine(grassDirectory, $"{i - 1}.png"));
                exported++;
            }
            catch (Exception ex)
            {
                warnings.Add($"草貼圖 TileGrass0{i} 轉檔失敗：{ex.Message}");
            }
        }

        return exported;
    }

    // ── 場景鏡頭 ─────────────────────────────────────────────────

    /// <summary>
    /// <c>Camera_Angle_Position.bmd → camera.json</c>（有才出，一般野外圖沒有）。
    /// </summary>
    /// <remarks>
    /// 登入（World95 海上帆船）與選角這類特殊場景的取景不在代碼裡，
    /// 在世界目錄的 CAP 檔（加密 INI，遊戲由 <c>WorldControl</c> 載入：
    /// <c>FOV×FOV_SCALE</c>、position、target=HeroPosition）。中立包不帶原始
    /// 加密檔——解好值出成 JSON，Godot 端直接讀。單位＝MU 世界單位（÷100=格）。
    /// </remarks>
    private static void ExportCamera(WorldEntry world, string outputDirectory, List<string> warnings)
    {
        string cap = Path.Combine(world.Directory, "Camera_Angle_Position.bmd");

        if (!File.Exists(cap))
            return;

        try
        {
            var data = new Client.Data.CAP.CAPReader().Load(cap).GetAwaiter().GetResult();
            var json = new
            {
                說明 = "來源 Camera_Angle_Position.bmd（加密 INI，已解值）。target=HeroPosition；FOV 再乘客戶端 FOV_SCALE。",
                cameraPosition = new[] { data.CameraPosition.X, data.CameraPosition.Y, data.CameraPosition.Z },
                target = new[] { data.HeroPosition.X, data.HeroPosition.Y, data.HeroPosition.Z },
                cameraAngle = new[] { data.CameraAngle.X, data.CameraAngle.Y, data.CameraAngle.Z },
                fov = data.CameraFOV,
                distance = data.CameraDistance,
                zDistance = data.CameraZDistance,
                ratio = data.CameraRatio,
            };
            File.WriteAllText(
                Path.Combine(outputDirectory, "camera.json"),
                JsonSerializer.Serialize(json, SceneJsonOptions));
        }
        catch (Exception ex)
        {
            warnings.Add($"Camera_Angle_Position 解析失敗：{ex.Message}");
        }
    }

    // ── 16-bit 高度（docs/23 跨線契約）───────────────────────────

    /// <summary>
    /// <c>height16.png</c>（16-bit 灰階）＋回傳 <c>HeightScale16</c>。
    /// </summary>
    /// <remarks>
    /// 跨線契約（docs/21 卡 8）：世界高 = 像素值 × HeightScale16，height16.png 與
    /// HeightScale16 <b>成套出現缺一不可</b>；讀取端有就讀、沒有退回 height.png。
    /// MU 原生高度只有 8-bit（OZB byte×1.5）——這裡是<b>升容器不升精度</b>
    /// （值 = byte×256、Scale16 = 1.5/256，與 byte×1.5 位元等值），
    /// 出它是讓客戶端讀取器對 MU/Lineage 兩線走同一條路；
    /// 真正吃到 16-bit 精度的是 Lineage 線（u16 原生）。
    /// </remarks>
    private static double ExportHeight16(MapDocument document, string outputDirectory, List<string> warnings)
    {
        const double scale16 = 1.5 / 256.0;

        try
        {
            var ozb = document.Height;
            if (ozb == null)
            {
                warnings.Add("height16：文件沒有高度圖，略過（height.png 同樣不會有）");
                return scale16;
            }

            using var gray = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.L16>(ozb.Width, ozb.Height);
            for (int y = 0; y < ozb.Height; y++)
            {
                for (int x = 0; x < ozb.Width; x++)
                    gray[x, y] = new SixLabors.ImageSharp.PixelFormats.L16((ushort)(ozb.Data[(y * ozb.Width) + x].R << 8));
            }

            SixLabors.ImageSharp.ImageExtensions.SaveAsPng(gray, Path.Combine(outputDirectory, "height16.png"));
        }
        catch (Exception ex)
        {
            warnings.Add($"height16 寫出失敗：{ex.Message}");
        }

        return scale16;
    }

    /// <summary>契約要求 map.json 也帶 HeightScale16——不動 MuAssets.Core 的格式代碼，
    /// 寫完後以 JSON 後處理注入欄位（R2：可以加欄位）。</summary>
    private static void InjectHeightScale16IntoMapJson(string outputDirectory, double heightScale16, List<string> warnings)
    {
        try
        {
            string path = Path.Combine(outputDirectory, "map.json");
            if (!File.Exists(path))
                return;

            var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path));
            if (node is null)
                return;

            node["HeightScale16"] = heightScale16;
            File.WriteAllText(path, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            warnings.Add($"map.json 注入 HeightScale16 失敗：{ex.Message}");
        }
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

        // 依出現次數排序後取前 N 種。預設的 N 大到蓋滿整張圖；
        // 調小是「我只想快速看一眼」時的選項，而不是常態。
        var byFrequency = placements
            .GroupBy(p => p.Type)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .Take(options.MaxObjectTypes)
            .ToArray();

        // 截斷不准靜默：被 Take 丟掉的型別不會進迴圈、連警告都不會有，
        // 畫面上就是「某些物件消失」而查不到原因（RealmForge M1 缺屋頂即此）。
        int distinctTypes = placements.Select(p => p.Type).Distinct().Count();
        if (byFrequency.Length < distinctTypes)
            warnings.Add($"物件型別 {distinctTypes} 種超過上限 {options.MaxObjectTypes}，"
                       + $"砍掉了 {distinctTypes - byFrequency.Length} 種低頻型別——用 --max-types 提高上限");

        // World1 用具名檔，型別編號要透過 ModelType 這個列舉才對得上檔名。
        var namedFiles = worldIndex == 1 ? IndexObjectFiles(dataPath, worldIndex) : null;

        foreach (short type in byFrequency)
        {
            string? relative = ResolveModelPath(dataPath, worldIndex, type, namedFiles);

            if (relative is null)
            {
                warnings.Add($"物件 type {type} 找不到模型"
                           + (worldIndex == 1 ? $"（ModelType 是 {(ModelType)type}，Object1 裡沒有對應檔案）" : string.Empty));
                continue;
            }

            string full = Path.Combine(dataPath, relative);

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

    /// <summary>
    /// 型別編號 → 模型檔（相對於 Data）。
    /// </summary>
    /// <remarks>
    /// 通則是 <c>Object{world}/Object{type+1:00}.bmd</c>，除了 World1（勒瑞西亞）——
    /// 它用的是具名檔（<c>Tree01.bmd</c>、<c>Stone03.bmd</c>…）。
    ///
    /// 那個對應表其實一直都在：<c>Client.Data.ModelType</c> 這個列舉，
    /// 而**列舉成員的名字就是檔名**（客戶端的 <c>TreeObject</c> 等類別就是這樣組路徑的）。
    /// 少數對不上的靠三段後援補：加 <c>01</c> 後綴、去掉數字後綴加 <c>01</c>、
    /// 以及大小寫不敏感的比對（資源包裡有 <c>DoungeonGate01.bmd</c> 這種拼錯的檔名）。
    ///
    /// 對不上就照實回報，不猜。
    /// </remarks>
    private static string? ResolveModelPath(
        string dataPath, int worldIndex, short type, Dictionary<string, string>? namedFiles)
    {
        if (namedFiles is null)
        {
            string generic = $"Object{worldIndex}/Object{type + 1:00}.bmd";
            return File.Exists(Path.Combine(dataPath, generic)) ? generic : null;
        }

        if (!ModelTypeNames.TryGetValue((ushort)type, out string? name))
            return null;

        foreach (var candidate in Candidates(name))
        {
            if (namedFiles.TryGetValue(candidate, out var file))
                return $"Object{worldIndex}/{file}";
        }

        // 最後才用後綴比對：MuWall02 的實際檔名是 StoneMuWall02。
        // 放最後是因為它最寬鬆，前面精確對得上就不該走到這裡。
        foreach (var candidate in Candidates(name))
        {
            var hit = namedFiles.FirstOrDefault(
                pair => pair.Key.EndsWith(candidate, StringComparison.OrdinalIgnoreCase));

            if (hit.Value is not null)
                return $"Object{worldIndex}/{hit.Value}";
        }

        return null;

        static IEnumerable<string> Candidates(string name)
        {
            yield return name;                                  // Tree01
            yield return name + "01";                           // Bonfire → Bonfire01
            var trimmed = name.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
            if (trimmed.Length > 0 && trimmed != name)
                yield return trimmed + "01";

            // Season 20 的檔名多一個 o：DungeonGate 的實際檔案是 DoungeonGate01.bmd。
            // 不是大小寫問題，後綴比對也橋不過去。原版客戶端就是寫死這個拼法
            //（Client.Main/Objects/Worlds/Lorencia/DungeonGateObject.cs），那才是真相來源。
            if (name == "DungeonGate")
                yield return "DoungeonGate01";
        }
    }

    /// <summary>
    /// 型別編號 → <see cref="ModelType"/> 的成員名，取<b>宣告順序在前</b>的那一個。
    /// </summary>
    /// <remarks>
    /// 不能直接用 <c>((ModelType)type).ToString()</c>：這個列舉有重複值 ——
    /// <c>Tree01 = 0</c> 與 <c>ITEM_GROUP_SWORD = 0 * 512</c> 撞在一起，
    /// 而 <c>ToString()</c> 挑哪一個是未定義行為（實測會挑到 ITEM_GROUP_SWORD）。
    ///
    /// <c>ITEM_GROUP_*</c> 是道具分類，跟地圖物件不是同一組編號，整批排除。
    /// </remarks>
    private static readonly Dictionary<ushort, string> ModelTypeNames = BuildModelTypeNames();

    private static Dictionary<ushort, string> BuildModelTypeNames()
    {
        var map = new Dictionary<ushort, string>();
        var names = Enum.GetNames(typeof(ModelType));
        var values = (ModelType[])Enum.GetValues(typeof(ModelType));

        for (int i = 0; i < names.Length; i++)
        {
            if (names[i].StartsWith("ITEM_GROUP_", StringComparison.Ordinal))
                continue;

            map.TryAdd((ushort)values[i], names[i]);
        }

        return map;
    }

    /// <summary>Object{N} 目錄裡的 <c>.bmd</c>，鍵是不含副檔名的檔名（大小寫不敏感）。</summary>
    private static Dictionary<string, string> IndexObjectFiles(string dataPath, int worldIndex)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string directory = Path.Combine(dataPath, $"Object{worldIndex}");

        if (!Directory.Exists(directory))
            return map;

        foreach (var file in Directory.EnumerateFiles(directory, "*.bmd"))
            map[Path.GetFileNameWithoutExtension(file)] = Path.GetFileName(file);

        return map;
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
        public double HeightScale16 { get; set; }

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
