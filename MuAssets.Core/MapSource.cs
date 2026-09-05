using System.Text.Json;

namespace MuAssets.Core;

/// <summary>一個地圖來源：某個專案的某個目錄。</summary>
/// <param name="Name">顯示用的名字，通常就是專案名。</param>
/// <param name="Root">根目錄。</param>
/// <param name="Kind">這個根底下的東西長什麼樣。</param>
public sealed record MapSource(string Name, string Root, MapSourceKind Kind)
{
    public bool Exists => Directory.Exists(Root);
}

public enum MapSourceKind
{
    /// <summary>MU 的 Data 目錄：底下是 <c>World{N}/</c>，裡面是客戶端原生檔。可讀可寫。</summary>
    MuData,

    /// <summary>
    /// 一堆 authoring 專案：底下每個子目錄各有 <c>map.json</c> ＋ 六張 PNG。
    /// </summary>
    /// <remarks>
    /// <c>godot-export</c> 的中立包與 <c>lineage-asset-extract</c> 的中繼產物都是這個形狀。
    /// **唯讀** —— 檔案留在原專案，這裡只是開來看。
    /// </remarks>
    ProjectRoot,
}

/// <summary>來源底下的一張圖。</summary>
public sealed record MapSourceEntry(
    MapSource Source,
    string Directory,
    string Name,
    int? WorldIndex,
    bool HasObjects)
{
    /// <summary>顯示用：「來源／名字」。</summary>
    public string Label => $"{Source.Name}／{Name}";
}

/// <summary>
/// 跨專案的地圖瀏覽：把好幾個專案的地圖列在一起，**檔案留在各自專案裡不複製**。
/// </summary>
/// <remarks>
/// 為什麼不複製：三個專案各自都有天堂地圖的窗格
/// （本專案 Data 5 個、RealmForge 39 個、lineage-asset-extract 130 個），
/// 而且是同一條管線的不同快照。複製進來只會多出好幾份會各自漂移的副本 ——
/// 已經因為這樣出過事（同一個 world 編號兩邊內容不同）。
///
/// 所以這裡只記「哪個專案的哪個目錄」，開的時候走唯讀外部專案那條路。
/// </remarks>
public static class MapSourceCatalog
{
    /// <summary>
    /// 自動偵測預設的三個來源。找不到的直接不列 —— 列一個開不起來的項目只會讓人以為壞了。
    /// </summary>
    /// <param name="dataDirectory">本專案的 MU Data 目錄。</param>
    public static MapSource[] Defaults(string dataDirectory)
    {
        var sources = new List<MapSource>();

        if (Directory.Exists(dataDirectory))
            sources.Add(new MapSource("本專案（MU Data）", dataDirectory, MapSourceKind.MuData));

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var candidates = new (string Name, string Path)[]
        {
            ("RealmForge", Path.Combine(home, "Documents/GitHub/realmforge/Assets/skin_mu3d/mapmu")),
            ("天堂抽取（lineage-asset-extract）", Path.Combine(home, "Documents/GitHub/lineage-asset-extract/out/mu")),
        };

        foreach (var (name, path) in candidates)
        {
            if (Directory.Exists(path))
                sources.Add(new MapSource(name, path, MapSourceKind.ProjectRoot));
        }

        return [.. sources];
    }

    /// <summary>列出一個來源底下的地圖。不解析內容，只看檔案在不在，所以很快。</summary>
    public static MapSourceEntry[] Enumerate(MapSource source)
    {
        if (!source.Exists)
            return [];

        return source.Kind switch
        {
            MapSourceKind.MuData => EnumerateMuData(source),
            MapSourceKind.ProjectRoot => EnumerateProjects(source),
            _ => [],
        };
    }

    private static MapSourceEntry[] EnumerateMuData(MapSource source)
        => [.. WorldDirectory.Discover(source.Root)
            .OrderBy(w => w.Index)
            .Select(w => new MapSourceEntry(source, w.Directory, w.Name, w.Index, w.HasObj))];

    private static MapSourceEntry[] EnumerateProjects(MapSource source)
    {
        var entries = new List<MapSourceEntry>();

        foreach (string directory in Directory.EnumerateDirectories(source.Root).Order(StringComparer.Ordinal))
        {
            string mapJson = Path.Combine(directory, "map.json");
            if (!File.Exists(mapJson))
                continue;

            int? index = null;
            bool hasObjects = false;

            // 只讀 map.json 最外層的兩個欄位。整份解析要吃六張 PNG，
            // 列清單時沒必要 —— 130 個目錄會變成好幾秒。
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(mapJson));
                if (doc.RootElement.TryGetProperty("WorldIndex", out var w) && w.TryGetInt32(out int value))
                    index = value;
                hasObjects = doc.RootElement.TryGetProperty("Objects", out var objects)
                             && objects.ValueKind == JsonValueKind.Array
                             && objects.GetArrayLength() > 0;
            }
            catch (JsonException)
            {
                // 壞掉的 map.json 就不列。不要在瀏覽清單上丟例外。
                continue;
            }

            entries.Add(new MapSourceEntry(
                source,
                directory,
                Path.GetFileName(directory),
                index,
                hasObjects));
        }

        return [.. entries];
    }
}
