using Client.AssetStudio.Catalog;

namespace Client.AssetStudio.Cli;

/// <summary>
/// 把目錄裡的每一個模型都解析一遍，回報解不開的與缺貼圖的。
/// </summary>
/// <remarks>
/// 對應 <c>tools/MapTool verify</c> 在地圖那一側做的事。
/// 「隨便挑十隻怪看起來沒問題」不是驗收，這個才是：四千多個模型全部走一遍，
/// 而且把三種結果分開 ——
/// <list type="bullet">
/// <item><b>FAIL</b>：解析器解不開。這是工具的問題，要修。</item>
/// <item><b>TEX</b>：解得開但少貼圖。多半是原始資源本身的缺漏，
/// 但也可能是副檔名搜尋順序沒涵蓋到，值得逐一看。</item>
/// <item><b>ok</b>：網格、骨骼、動作、貼圖都齊。</item>
/// </list>
/// </remarks>
public static class VerifyCommand
{
    public static int Run(EntityCatalog catalog, string? kindFilter)
    {
        EntityKind? kind = kindFilter is null
            ? null
            : EntityKindNames.All.FirstOrDefault(k =>
                  EntityKindNames.Of(k).Equals(kindFilter, StringComparison.OrdinalIgnoreCase)
               || k.ToString().Equals(kindFilter, StringComparison.OrdinalIgnoreCase));

        var entries = catalog.Entries
            .Where(e => e.FullPath is not null)
            .Where(e => kind is null || e.Kind == kind)
            // 同一個模型可能被多個類別引用，只驗一次。
            .GroupBy(e => e.FullPath!, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToArray();

        Console.WriteLine();
        Console.WriteLine($"驗證 {entries.Length} 個模型…");

        int failed = 0;
        int missingTextures = 0;
        int ok = 0;
        long triangles = 0;

        foreach (var entry in entries)
        {
            var summary = ModelInspector.Inspect(entry);

            if (summary.Error is string error)
            {
                failed++;
                Console.WriteLine($"FAIL {entry.ModelPath}　{error}");
                continue;
            }

            triangles += summary.Triangles;

            if (summary.MissingTextures.Length > 0)
            {
                missingTextures++;
                Console.WriteLine($"TEX  {entry.ModelPath}　缺 {string.Join("、", summary.MissingTextures)}");
                continue;
            }

            ok++;
        }

        Console.WriteLine();
        Console.WriteLine($"ok {ok}　缺貼圖 {missingTextures}　解析失敗 {failed}　"
                        + $"（共 {triangles:N0} 個三角形）");

        return failed == 0 ? 0 : 1;
    }
}
