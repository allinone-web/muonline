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
}
