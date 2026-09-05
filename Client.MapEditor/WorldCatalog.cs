using System.Reflection;
using Client.Main.Controls;
using Client.Main.Core.Utilities;
using Client.Main.Scenes;
using MuAssets.Core;

namespace Client.MapEditor;

/// <summary>
/// 把 <see cref="WorldDirectory"/> 掃到的地圖對上客戶端登記的 world 類別。
/// </summary>
/// <remarks>
/// 這一層刻意留在編輯器：它依賴 <c>Client.Main</c> 的
/// <see cref="GameScene.MapWorldRegistry"/> 與 <see cref="WorldInfoAttribute"/>，
/// 而 Core 不准依賴引擎那一側。
/// </remarks>
public static class WorldCatalog
{
    /// <summary>
    /// 每個 world 的「物件 type → 語意類別」對應表快取。
    /// 取得方式是實際 new 一個該 world 類別再呼叫它的 <c>CreateMapTileObjects()</c> ——
    /// 這些覆寫方法只是填一個 <c>Type[256]</c>，不碰圖形資源。
    /// </summary>
    private static readonly Dictionary<int, Type[]?> TileObjectTypeCache = new();

    public static WorldEntry[] Discover(string dataDirectory)
        => WorldDirectory.Discover(dataDirectory, ResolveName);

    /// <summary>依 world index 找出客戶端登記的世界類別。</summary>
    public static Type? WorldTypeFor(int worldIndex)
    {
        // MapWorldRegistry 的 key 是 OpenMU 的 map number，client world index 要減一。
        if (worldIndex < 1 || worldIndex > 256)
            return null;

        return GameScene.MapWorldRegistry.TryGetValue((byte)(worldIndex - 1), out var type) ? type : null;
    }

    /// <summary>
    /// 取得該 world 的「物件 type → 語意類別」表（例如 Lorencia 的 0–12 是 <c>TreeObject</c>）。
    /// 沒有登記類別、或建構失敗時回傳 null，呼叫端就退回泛用的 <c>MapTileObject</c>。
    /// </summary>
    public static Type[]? GetTileObjectTypes(WorldEntry entry)
    {
        if (TileObjectTypeCache.TryGetValue(entry.Index, out var cached))
            return cached;

        Type[]? result = null;
        var worldType = WorldTypeFor(entry.Index);

        if (worldType is not null)
        {
            try
            {
                var instance = (WorldControl)Activator.CreateInstance(worldType)!;

                // CreateMapTileObjects 是 protected virtual，用反射叫它填 MapTileObjects。
                worldType
                    .GetMethod("CreateMapTileObjects", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.Invoke(instance, null);

                result = (Type[])instance.MapTileObjects.Clone();
                instance.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WorldCatalog] 取 World{entry.Index}（{worldType.Name}）的物件類別表失敗：{ex.InnerException?.Message ?? ex.Message}");
            }
        }

        TileObjectTypeCache[entry.Index] = result;
        return result;
    }

    private static string? ResolveName(int worldIndex)
        => WorldTypeFor(worldIndex)?.GetCustomAttribute<WorldInfoAttribute>()?.DisplayName;

    /// <summary>
    /// 把每張圖的語意型別表匯出成 JSON，給無頭的工具用。
    /// </summary>
    /// <remarks>
    /// 這是分類線索裡最準的一條 —— 有它的圖未分類約 2%，沒有的高到 79%。
    /// 但它要實例化 <c>Client.Main</c> 的 <c>WorldControl</c> 並反射叫
    /// <c>CreateMapTileObjects()</c>，所以只有帶著 MonoGame 的行程拿得到。
    /// MapTool 刻意無頭（CI 跑得動），於是拿不到。
    ///
    /// 解法不是讓 MapTool 相依 MonoGame，也不是用 regex 去解析 <c>World*.cs</c>
    /// （那些檔案有迴圈、有陣列、有繼承，正則會在看不見的地方解錯），
    /// 而是**由唯一解得對的地方導出一次**，其他人讀同一份。
    ///
    /// 輸出：<c>{ "1": { "0": "TreeObject", "5": "HouseObject" }, ... }</c>
    /// —— 外層是 world 編號，內層是 type → 類別名（只收非 null 的）。
    /// </remarks>
    public static void ExportSemanticTypes(string dataPath, string outputPath)
    {
        var payload = new SortedDictionary<int, SortedDictionary<int, string>>();
        int worlds = 0, entries = 0;

        foreach (var entry in WorldDirectory.Discover(dataPath).OrderBy(w => w.Index))
        {
            var types = GetTileObjectTypes(entry);
            if (types is null)
                continue;

            var map = new SortedDictionary<int, string>();
            for (int type = 0; type < types.Length; type++)
            {
                // MapTileObject 是所有格子的預設基底，21558/21760 筆都是它 ——
                // 它不帶任何語意，收進去只會讓檔案大 100 倍而查不到東西。
                if (types[type] is Type t && t.Name != "MapTileObject")
                    map[type] = t.Name;
            }

            if (map.Count == 0)
                continue;

            payload[entry.Index] = map;
            worlds++;
            entries += map.Count;
        }

        string json = System.Text.Json.JsonSerializer.Serialize(payload, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
        });

        string? directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(outputPath, json);
        Console.WriteLine($"語意型別：{worlds} 張圖、{entries} 筆 → {outputPath}");
    }
}
