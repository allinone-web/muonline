namespace MuAssets.Core;

/// <summary>
/// Data 目錄下的一張地圖。
/// </summary>
/// <remarks>
/// <b>刻意不含客戶端的 world 類別</b> —— 那是 <c>Client.Main</c> 的概念。
/// 需要它的地方（編輯器的物件語意分類）自己用 <see cref="Index"/> 去查。
/// </remarks>
/// <param name="Index">客戶端的 world index（目錄名 <c>WorldN</c> 的 N）。</param>
/// <param name="MapNumber">OpenMU 的 map number，等於 <paramref name="Index"/> - 1。</param>
public sealed record WorldEntry(
    int Index,
    int? MapNumber,
    string Name,
    string Directory,
    bool HasAtt,
    bool HasMap,
    bool HasObj,
    string[] TileFiles)
{
    public bool IsPlayable => HasAtt && HasMap;
}

/// <summary>
/// 掃 Data 目錄找出所有 <c>WorldN</c>。純檔案系統操作，不碰引擎。
/// </summary>
public static class WorldDirectory
{
    /// <param name="resolveName">
    /// 依 world index 取正式名稱。傳 null 就用 <c>WorldN</c>。
    /// 編輯器會傳一個查 <c>[WorldInfo]</c> 的函式進來。
    /// </param>
    public static WorldEntry[] Discover(string dataDirectory, Func<int, string?>? resolveName = null)
    {
        if (!Directory.Exists(dataDirectory))
            return [];

        var entries = new List<WorldEntry>();

        foreach (var directory in Directory.EnumerateDirectories(dataDirectory, "World*"))
        {
            var directoryName = Path.GetFileName(directory);
            if (!int.TryParse(directoryName.AsSpan("World".Length), out int index))
                continue;

            var files = Directory.EnumerateFiles(directory).Select(Path.GetFileName).OfType<string>().ToArray();

            entries.Add(new WorldEntry(
                Index: index,
                // 客戶端 worldIndex = OpenMU map number + 1。
                MapNumber: index >= 1 ? index - 1 : null,
                Name: resolveName?.Invoke(index) ?? $"World{index}",
                Directory: directory,
                HasAtt: Has(files, $"EncTerrain{index}.att"),
                HasMap: Has(files, $"EncTerrain{index}.map"),
                HasObj: Has(files, $"EncTerrain{index}.obj"),
                TileFiles: files.Where(IsTileTexture).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray()));
        }

        return entries.OrderBy(e => e.Index).ToArray();
    }

    private static bool Has(string[] files, string fileName)
        => files.Any(f => string.Equals(f, fileName, StringComparison.OrdinalIgnoreCase));

    private static bool IsTileTexture(string fileName)
        => fileName.StartsWith("Tile", StringComparison.OrdinalIgnoreCase)
        || fileName.StartsWith("ExtTile", StringComparison.OrdinalIgnoreCase);
}
