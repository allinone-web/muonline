using System.Text.Json;
using System.Text.Json.Serialization;
using Client.Data.BMD;

namespace Client.MapEditor;

/// <summary>素材分類。刻意做成通用的類別，未來匯入非 MU 的素材也套得上。</summary>
public enum AssetCategory
{
    Unclassified,
    Ground,
    Vegetation,
    Rock,
    Water,
    Building,
    Wall,
    Furniture,
    Light,
    Portal,
    Creature,
    Effect,
    Decoration,
}

public sealed record AssetEntry(
    string Id,
    string FileName,
    string Path,
    int WorldIndex,
    short? ObjectType,
    AssetCategory Category,
    string CategorySource);

/// <summary>
/// 掃描 <c>Object{N}</c> 目錄，把每個 <c>.bmd</c> 歸類。
/// </summary>
/// <remarks>
/// 分類來源依優先序疊加，先命中的贏：
/// <list type="number">
/// <item>人工標註（<c>object-catalog.json</c>），永遠最高</item>
/// <item>該 world 類別的 <c>CreateMapTileObjects()</c> 語意型別（<c>TreeObject</c> / <c>HouseObject</c>…）</item>
/// <item>檔名關鍵字 —— 只有 Object1（Lorencia）是具名檔案（Tree01.bmd、Bonfire01.bmd…），
///       其餘資料夾都是 ObjectNN.bmd，這條在那裡用不上</item>
/// <item>BMD 內部的貼圖檔名關鍵字（<c>BMDTextureMesh.TexturePath</c>）</item>
/// </list>
/// </remarks>
public sealed class AssetCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>關鍵字 → 類別。順序有意義：先命中的贏，所以特定的要排在通用的前面。</summary>
    private static readonly (string Keyword, AssetCategory Category)[] Keywords =
    [
        ("grass", AssetCategory.Vegetation),
        ("tree", AssetCategory.Vegetation),
        ("leaf", AssetCategory.Vegetation),
        ("bush", AssetCategory.Vegetation),
        ("flower", AssetCategory.Vegetation),
        ("plant", AssetCategory.Vegetation),
        ("straw", AssetCategory.Vegetation),
        ("mushroom", AssetCategory.Vegetation),

        ("bridgestone", AssetCategory.Building),
        ("stonewall", AssetCategory.Wall),
        ("stonestatue", AssetCategory.Decoration),
        ("stone", AssetCategory.Rock),
        ("rock", AssetCategory.Rock),
        ("cliff", AssetCategory.Rock),
        ("ore", AssetCategory.Rock),

        ("water", AssetCategory.Water),
        ("waterspout", AssetCategory.Water),
        ("wave", AssetCategory.Water),
        ("pond", AssetCategory.Water),

        ("house", AssetCategory.Building),
        ("bridge", AssetCategory.Building),
        ("tower", AssetCategory.Building),
        ("castle", AssetCategory.Building),
        ("temple", AssetCategory.Building),
        ("roof", AssetCategory.Building),
        ("stair", AssetCategory.Building),
        ("tent", AssetCategory.Building),
        ("ship", AssetCategory.Building),
        ("well", AssetCategory.Building),

        ("fence", AssetCategory.Wall),
        ("wall", AssetCategory.Wall),
        ("door", AssetCategory.Wall),
        ("gate", AssetCategory.Portal),
        ("curtain", AssetCategory.Wall),

        ("portal", AssetCategory.Portal),
        ("warp", AssetCategory.Portal),
        ("teleport", AssetCategory.Portal),

        ("light", AssetCategory.Light),
        ("fire", AssetCategory.Light),
        ("bonfire", AssetCategory.Light),
        ("candle", AssetCategory.Light),
        ("torch", AssetCategory.Light),
        ("lamp", AssetCategory.Light),

        ("bird", AssetCategory.Creature),
        ("butterfly", AssetCategory.Creature),
        ("fish", AssetCategory.Creature),
        ("animal", AssetCategory.Creature),
        ("horse", AssetCategory.Creature),

        ("effect", AssetCategory.Effect),
        ("smoke", AssetCategory.Effect),
        ("spark", AssetCategory.Effect),
        ("rain", AssetCategory.Effect),
        ("snow", AssetCategory.Effect),

        ("beer", AssetCategory.Furniture),
        ("furniture", AssetCategory.Furniture),
        ("chair", AssetCategory.Furniture),
        ("table", AssetCategory.Furniture),
        ("barrel", AssetCategory.Furniture),
        ("box", AssetCategory.Furniture),
        ("chest", AssetCategory.Furniture),
        ("drum", AssetCategory.Furniture),
        ("carriage", AssetCategory.Furniture),
        ("cannon", AssetCategory.Furniture),
        ("sign", AssetCategory.Decoration),
        ("tomb", AssetCategory.Decoration),
        ("statue", AssetCategory.Decoration),
        ("flag", AssetCategory.Decoration),

        ("ground", AssetCategory.Ground),
        ("floor", AssetCategory.Ground),
        ("terrain", AssetCategory.Ground),
        ("road", AssetCategory.Ground),
        ("tile", AssetCategory.Ground),

        // 以下這批是從 --catalog-unknown 的統計裡挑出來、語意明確的貼圖名。
        // 大部分未分類模型的貼圖名是無語意代碼（BosBB、br001、choarms_02），
        // 那些只能靠人工標註，不再硬猜。
        ("mansion", AssetCategory.Building),
        ("building", AssetCategory.Building),
        ("bilding", AssetCategory.Building),   // 官方資源裡的拼字錯誤
        ("pillar", AssetCategory.Building),
        ("column", AssetCategory.Building),
        ("arch", AssetCategory.Building),
        ("support", AssetCategory.Building),
        ("prison", AssetCategory.Building),
        ("rampart", AssetCategory.Wall),
        ("brick", AssetCategory.Wall),
        ("obelisk", AssetCategory.Decoration),
        ("statye", AssetCategory.Decoration),  // statue 的拼字錯誤
        ("cobweb", AssetCategory.Decoration),
        ("scarp", AssetCategory.Rock),
        ("branch", AssetCategory.Vegetation),
        ("squid", AssetCategory.Creature),
        ("monster", AssetCategory.Creature),
        ("lava", AssetCategory.Effect),
        ("ice", AssetCategory.Rock),
    ];

    /// <summary>語意類別名（去掉 Object 後綴）→ 分類。來自各 world 的 CreateMapTileObjects()。</summary>
    private static readonly Dictionary<string, AssetCategory> SemanticClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Tree"] = AssetCategory.Vegetation,
        ["Grass"] = AssetCategory.Vegetation,
        ["Stone"] = AssetCategory.Rock,
        ["StoneStatue"] = AssetCategory.Decoration,
        ["SteelStatue"] = AssetCategory.Decoration,
        ["Tomb"] = AssetCategory.Decoration,
        ["Sign"] = AssetCategory.Decoration,
        ["Flag"] = AssetCategory.Decoration,
        ["FireLight"] = AssetCategory.Light,
        ["Bonfire"] = AssetCategory.Light,
        ["Light"] = AssetCategory.Light,
        ["StreetLight"] = AssetCategory.Light,
        ["Candle"] = AssetCategory.Light,
        ["DungeonGate"] = AssetCategory.Portal,
        ["Gate"] = AssetCategory.Portal,
        ["Portal"] = AssetCategory.Portal,
        ["WaterPortal"] = AssetCategory.Portal,
        ["MerchantAnimal"] = AssetCategory.Creature,
        ["Bird"] = AssetCategory.Creature,
        ["Fish"] = AssetCategory.Creature,
        ["Bug"] = AssetCategory.Creature,
        ["TreasureDrum"] = AssetCategory.Furniture,
        ["TreasureChest"] = AssetCategory.Furniture,
        ["Beer"] = AssetCategory.Furniture,
        ["Furniture"] = AssetCategory.Furniture,
        ["Carriage"] = AssetCategory.Furniture,
        ["Cannon"] = AssetCategory.Furniture,
        ["Straw"] = AssetCategory.Vegetation,
        ["WaterPlant"] = AssetCategory.Vegetation,
        ["Ship"] = AssetCategory.Building,
        ["SteelWall"] = AssetCategory.Wall,
        ["SteelDoor"] = AssetCategory.Wall,
        ["StoneWall"] = AssetCategory.Wall,
        ["MuWall"] = AssetCategory.Wall,
        ["HouseWall"] = AssetCategory.Wall,
        ["Fence"] = AssetCategory.Wall,
        ["Curtain"] = AssetCategory.Wall,
        ["Bridge"] = AssetCategory.Building,
        ["BridgeStone"] = AssetCategory.Building,
        ["House"] = AssetCategory.Building,
        ["HouseEtc"] = AssetCategory.Building,
        ["Tent"] = AssetCategory.Building,
        ["Stair"] = AssetCategory.Building,
        ["Well"] = AssetCategory.Building,
        ["RestPlace"] = AssetCategory.Building,
        ["WaterSpout"] = AssetCategory.Water,
        ["Bubbles"] = AssetCategory.Water,
        ["Hanging"] = AssetCategory.Decoration,
        ["Aurora"] = AssetCategory.Effect,
        ["LightBeam"] = AssetCategory.Effect,
    };

    private readonly Dictionary<string, AssetCategory> _manual = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _catalogPath;

    public AssetCatalog(string catalogPath)
    {
        _catalogPath = catalogPath;
        Load();
    }

    /// <summary>掃一個 <c>Object{N}</c> 目錄，把裡面每個 <c>.bmd</c> 歸類。</summary>
    /// <param name="placement">
    /// 該圖的擺放統計。傳入時會多一道「從擺放位置推測」的分類 —— 見 <see cref="PlacementStats"/>。
    /// </param>
    public AssetEntry[] Scan(
        string dataPath,
        int worldIndex,
        Type[]? semanticTypes,
        Dictionary<short, PlacementProfile>? placement = null)
    {
        string directory = Path.Combine(dataPath, $"Object{worldIndex}");
        if (!Directory.Exists(directory))
            return [];

        return Directory.EnumerateFiles(directory, "*.bmd", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(directory, "*.BMD", SearchOption.TopDirectoryOnly))
            .DistinctBy(p => p, StringComparer.OrdinalIgnoreCase)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Select(path => Classify(path, worldIndex, semanticTypes, placement))
            .ToArray();
    }

    public void SetCategory(AssetEntry entry, AssetCategory category)
    {
        _manual[entry.Id] = category;
        Save();
    }

    public void ClearCategory(AssetEntry entry)
    {
        if (_manual.Remove(entry.Id))
            Save();
    }

    public int ManualCount => _manual.Count;

    private AssetEntry Classify(
        string path,
        int worldIndex,
        Type[]? semanticTypes,
        Dictionary<short, PlacementProfile>? placement)
    {
        string fileName = Path.GetFileName(path);
        string id = $"World{worldIndex}/{fileName}";
        short? objectType = ParseObjectType(fileName);

        if (_manual.TryGetValue(id, out var manual))
            return new AssetEntry(id, fileName, path, worldIndex, objectType, manual, "人工");

        if (objectType is short type && semanticTypes is not null &&
            type >= 0 && type < semanticTypes.Length &&
            semanticTypes[type] is Type semantic &&
            TryFromSemanticClass(semantic.Name, out var fromClass))
        {
            return new AssetEntry(id, fileName, path, worldIndex, objectType, fromClass, semantic.Name);
        }

        if (TryFromKeyword(Path.GetFileNameWithoutExtension(fileName), out var fromName))
            return new AssetEntry(id, fileName, path, worldIndex, objectType, fromName, "檔名");

        if (TryFromTextures(path, out var fromTexture, out var matchedTexture))
            return new AssetEntry(id, fileName, path, worldIndex, objectType, fromTexture, $"貼圖 {matchedTexture}");

        // 最後一招：看它實際擺在什麼地形上。這是推測，來源會標清楚。
        if (objectType is short placedType &&
            placement is not null &&
            placement.TryGetValue(placedType, out var profile) &&
            PlacementStats.TryClassify(profile, out var fromPlacement))
        {
            return new AssetEntry(id, fileName, path, worldIndex, objectType, fromPlacement, $"擺放位置 ×{profile.Count}");
        }

        return new AssetEntry(id, fileName, path, worldIndex, objectType, AssetCategory.Unclassified, "－");
    }

    /// <summary>
    /// <c>ObjectNN.bmd</c> → type NN-1（見 <c>MapTileObject.Load</c> 的 <c>Type + 1</c>）。
    /// Object1（Lorencia）用具名檔案，這裡會回 null。
    /// </summary>
    private static short? ParseObjectType(string fileName)
    {
        string name = Path.GetFileNameWithoutExtension(fileName);

        if (!name.StartsWith("Object", StringComparison.OrdinalIgnoreCase))
            return null;

        return short.TryParse(name.AsSpan("Object".Length), out short number) && number > 0
            ? (short)(number - 1)
            : null;
    }

    private static bool TryFromSemanticClass(string className, out AssetCategory category)
    {
        // 型別名一律是 XxxObject，去掉後綴再查。
        string key = className.EndsWith("Object", StringComparison.Ordinal)
            ? className[..^"Object".Length]
            : className;

        return SemanticClasses.TryGetValue(key, out category);
    }

    private static bool TryFromKeyword(string text, out AssetCategory category)
    {
        foreach (var (keyword, mapped) in Keywords)
        {
            if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                category = mapped;
                return true;
            }
        }

        category = AssetCategory.Unclassified;
        return false;
    }

    /// <summary>
    /// 最後一道：看模型內部引用的貼圖檔名。ObjectNN.bmd 這種無意義檔名只剩這個線索。
    /// </summary>
    private static bool TryFromTextures(string bmdPath, out AssetCategory category, out string matched)
    {
        category = AssetCategory.Unclassified;
        matched = string.Empty;

        try
        {
            var model = new BMDReader().Load(bmdPath).GetAwaiter().GetResult();

            foreach (var mesh in model.Meshes ?? [])
            {
                if (string.IsNullOrWhiteSpace(mesh.TexturePath))
                    continue;

                if (TryFromKeyword(Path.GetFileNameWithoutExtension(mesh.TexturePath), out category))
                {
                    matched = Path.GetFileName(mesh.TexturePath);
                    return true;
                }
            }
        }
        catch
        {
            // 讀不了的模型就是未分類，不是錯誤。
        }

        return false;
    }

    /// <summary>列出一個模型引用的所有貼圖檔名（不含副檔名），供分類分析用。</summary>
    public static IEnumerable<string> TextureNames(string bmdPath)
    {
        BMD model;
        try
        {
            model = new BMDReader().Load(bmdPath).GetAwaiter().GetResult();
        }
        catch
        {
            yield break;
        }

        foreach (var mesh in model.Meshes ?? [])
        {
            if (!string.IsNullOrWhiteSpace(mesh.TexturePath))
                yield return Path.GetFileNameWithoutExtension(mesh.TexturePath);
        }
    }

    private void Load()
    {
        if (!File.Exists(_catalogPath))
            return;

        try
        {
            var stored = JsonSerializer.Deserialize<Dictionary<string, AssetCategory>>(
                File.ReadAllText(_catalogPath), JsonOptions);

            if (stored is null)
                return;

            foreach (var (key, value) in stored)
                _manual[key] = value;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AssetCatalog] 讀取 {_catalogPath} 失敗：{ex.Message}");
        }
    }

    private void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(_catalogPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(_catalogPath, JsonSerializer.Serialize(_manual, JsonOptions));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AssetCatalog] 寫入 {_catalogPath} 失敗：{ex.Message}");
        }
    }
}

public static class AssetCategoryNames
{
    private static readonly Dictionary<AssetCategory, string> Names = new()
    {
        [AssetCategory.Unclassified] = "未分類",
        [AssetCategory.Ground] = "地面",
        [AssetCategory.Vegetation] = "草木",
        [AssetCategory.Rock] = "岩石",
        [AssetCategory.Water] = "水體",
        [AssetCategory.Building] = "建築",
        [AssetCategory.Wall] = "牆與圍籬",
        [AssetCategory.Furniture] = "家具道具",
        [AssetCategory.Light] = "燈光火源",
        [AssetCategory.Portal] = "傳送門機關",
        [AssetCategory.Creature] = "生物",
        [AssetCategory.Effect] = "特效",
        [AssetCategory.Decoration] = "裝飾",
    };

    public static string Of(AssetCategory category) => Names.GetValueOrDefault(category, category.ToString());

    public static AssetCategory[] All { get; } = Enum.GetValues<AssetCategory>();
}
