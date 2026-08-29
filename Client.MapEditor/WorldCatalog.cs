using System.Reflection;
using Client.Main.Controls;
using Client.Main.Core.Utilities;
using Client.Main.Scenes;

namespace Client.MapEditor;

/// <summary>
/// Data 目錄下的一張地圖，外加客戶端那邊登記的世界類別。
/// </summary>
/// <param name="Index">客戶端的 world index（目錄名 <c>WorldN</c> 的 N）。</param>
/// <param name="MapNumber">OpenMU 的 map number，等於 <paramref name="Index"/> - 1；沒有登記類別時為 null。</param>
/// <param name="WorldType">`Client.Main.Worlds` 底下對應的類別，沒有就是 null。</param>
public sealed record WorldEntry(
    int Index,
    int? MapNumber,
    string Name,
    Type? WorldType,
    string Directory,
    bool HasAtt,
    bool HasMap,
    bool HasObj,
    string[] TileFiles)
{
    public bool IsPlayable => HasAtt && HasMap;
}

/// <summary>
/// 掃 Data 目錄並和 <see cref="GameScene.MapWorldRegistry"/> 對起來。
/// </summary>
public static class WorldCatalog
{
    /// <summary>
    /// 每個 world 的「物件 type → 語意類別」對應表快取。
    /// 取得方式是實際 new 一個該 world 類別再呼叫它的 <c>CreateMapTileObjects()</c> ——
    /// 這些覆寫方法只是填一個 <c>Type[256]</c>，不碰圖形資源。
    /// </summary>
    private static readonly Dictionary<int, Type[]?> TileObjectTypeCache = new();

    public static WorldEntry[] Discover(string dataDir)
    {
        if (!Directory.Exists(dataDir))
            return [];

        // MapWorldRegistry 的 key 是 OpenMU 的 map number，client world index 要減一。
        var byMapNumber = GameScene.MapWorldRegistry;

        var entries = new List<WorldEntry>();

        foreach (var dir in Directory.EnumerateDirectories(dataDir, "World*"))
        {
            var dirName = Path.GetFileName(dir);
            if (!int.TryParse(dirName.AsSpan("World".Length), out int index))
                continue;

            var files = Directory.EnumerateFiles(dir).Select(Path.GetFileName).OfType<string>().ToArray();

            Type? worldType = null;
            int? mapNumber = null;

            if (index >= 1 && index <= 256 && byMapNumber.TryGetValue((byte)(index - 1), out var registered))
            {
                worldType = registered;
                mapNumber = index - 1;
            }

            entries.Add(new WorldEntry(
                Index: index,
                MapNumber: mapNumber,
                Name: ResolveName(worldType, index),
                WorldType: worldType,
                Directory: dir,
                HasAtt: Has(files, $"EncTerrain{index}.att"),
                HasMap: Has(files, $"EncTerrain{index}.map"),
                HasObj: Has(files, $"EncTerrain{index}.obj"),
                TileFiles: files.Where(IsTileTexture).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray()));
        }

        return entries.OrderBy(e => e.Index).ToArray();
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

        if (entry.WorldType is not null)
        {
            try
            {
                var instance = (WorldControl)Activator.CreateInstance(entry.WorldType)!;

                // CreateMapTileObjects 是 protected virtual，用反射叫它填 MapTileObjects。
                entry.WorldType
                    .GetMethod("CreateMapTileObjects", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.Invoke(instance, null);

                result = (Type[])instance.MapTileObjects.Clone();
                instance.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WorldCatalog] 取 World{entry.Index}（{entry.WorldType.Name}）的物件類別表失敗：{ex.InnerException?.Message ?? ex.Message}");
            }
        }

        TileObjectTypeCache[entry.Index] = result;
        return result;
    }

    private static string ResolveName(Type? worldType, int index)
    {
        var info = worldType?.GetCustomAttribute<WorldInfoAttribute>();
        return info?.DisplayName ?? $"World{index}";
    }

    private static bool Has(string[] files, string fileName)
        => files.Any(f => string.Equals(f, fileName, StringComparison.OrdinalIgnoreCase));

    private static bool IsTileTexture(string fileName)
        => fileName.StartsWith("Tile", StringComparison.OrdinalIgnoreCase)
        || fileName.StartsWith("ExtTile", StringComparison.OrdinalIgnoreCase);
}
