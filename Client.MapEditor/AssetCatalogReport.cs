namespace Client.MapEditor;

/// <summary>
/// 掃過所有 <c>Object{N}</c> 目錄，回報自動分類的覆蓋率。
/// </summary>
/// <remarks>
/// 用 <c>--catalog-report</c> 觸發。刻意不傳語意型別表 —— 那需要 MuGame 才能建 world 類別，
/// 而這份報告要量的是「檔名 + BMD 貼圖名」這兩個自動來源本身有多少覆蓋率。
/// </remarks>
public static class AssetCatalogReport
{
    public static void Print(string dataPath)
    {
        var catalog = new AssetCatalog(Path.Combine(Path.GetTempPath(), "maptool-catalog-report.json"));

        var folders = Directory.EnumerateDirectories(dataPath, "Object*")
            .Select(dir => (dir, ok: int.TryParse(Path.GetFileName(dir).AsSpan("Object".Length), out int n), n: Index(dir)))
            .Where(x => x.ok)
            .OrderBy(x => x.n)
            .ToArray();

        int total = 0;
        int unclassified = 0;
        int fromPlacement = 0;
        var byCategory = new Dictionary<AssetCategory, int>();

        Console.WriteLine($"{"目錄",-12} {"模型",6} {"未分類",7}  主要類別");
        Console.WriteLine(new string('-', 78));

        foreach (var (_, _, worldIndex) in folders)
        {
            var placement = PlacementStats.Build(dataPath, worldIndex);
            var assets = catalog.Scan(dataPath, worldIndex, semanticTypes: null, placement);
            if (assets.Length == 0)
                continue;

            int missing = assets.Count(a => a.Category == AssetCategory.Unclassified);
            total += assets.Length;
            unclassified += missing;

            foreach (var asset in assets)
            {
                byCategory[asset.Category] = byCategory.GetValueOrDefault(asset.Category) + 1;

                if (asset.CategorySource.StartsWith("擺放位置", StringComparison.Ordinal))
                    fromPlacement++;
            }

            var top = assets
                .Where(a => a.Category != AssetCategory.Unclassified)
                .GroupBy(a => a.Category)
                .OrderByDescending(g => g.Count())
                .Take(3)
                .Select(g => $"{AssetCategoryNames.Of(g.Key)} {g.Count()}");

            Console.WriteLine($"Object{worldIndex,-6} {assets.Length,6} {missing,7}  {string.Join("、", top)}");
        }

        Console.WriteLine();
        Console.WriteLine($"合計 {total} 個模型，已分類 {total - unclassified}（{(total == 0 ? 0 : (total - unclassified) * 100.0 / total):F1}%），未分類 {unclassified}");
        Console.WriteLine($"其中 {fromPlacement} 個是靠「擺放位置」推測出來的（標示為推測，可人工覆蓋）");
        Console.WriteLine();

        foreach (var (category, count) in byCategory.OrderByDescending(kv => kv.Value))
            Console.WriteLine($"  {AssetCategoryNames.Of(category),-10} {count,6}");
    }

    /// <summary>
    /// 列出未分類模型最常引用的貼圖名。看這份就知道還缺哪些關鍵字。
    /// </summary>
    public static void PrintUnknownTextures(string dataPath, int take = 60)
    {
        var catalog = new AssetCatalog(Path.Combine(Path.GetTempPath(), "maptool-catalog-report.json"));
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var directory in Directory.EnumerateDirectories(dataPath, "Object*"))
        {
            if (!int.TryParse(Path.GetFileName(directory).AsSpan("Object".Length), out int worldIndex))
                continue;

            foreach (var asset in catalog.Scan(dataPath, worldIndex, semanticTypes: null))
            {
                if (asset.Category != AssetCategory.Unclassified)
                    continue;

                foreach (var name in AssetCatalog.TextureNames(asset.Path))
                    counts[name] = counts.GetValueOrDefault(name) + 1;
            }
        }

        Console.WriteLine($"未分類模型引用的貼圖名（前 {take} 名）：");
        foreach (var (name, count) in counts.OrderByDescending(kv => kv.Value).Take(take))
            Console.WriteLine($"  {count,5}  {name}");
    }

    private static int Index(string directory)
        => int.TryParse(Path.GetFileName(directory).AsSpan("Object".Length), out int n) ? n : -1;
}
