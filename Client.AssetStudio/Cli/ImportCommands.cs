using Client.AssetStudio.Catalog;
using Client.AssetStudio.Export;
using Client.AssetStudio.Import;

namespace Client.AssetStudio.Cli;

/// <summary>匯入外部模型，以及匯出／匯入這一對的往返驗收。</summary>
public static class ImportCommands
{
    /// <summary>讀一個外部的 glTF / GLB，把驗證報告印出來。</summary>
    public static int Inspect(string path, float? scale)
    {
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"找不到 {path}");
            return 2;
        }

        var options = scale is float value
            ? new GltfImporter.Options(Scale: value, AutoScale: false)
            : new GltfImporter.Options();

        var imported = GltfImporter.Import(path, options);
        PrintReport(Path.GetFileName(path), imported);

        return imported.Report.HasErrors ? 1 : 0;
    }

    public static void PrintReport(string title, ImportedModel imported)
    {
        var report = imported.Report;

        Console.WriteLine();
        Console.WriteLine($"── {title} ──");
        Console.WriteLine(report.Summary);

        if (report.Height > 0f)
        {
            Console.WriteLine($"高度 {report.Height:F0} 世界單位"
                            + $"（MU 角色約 175；建議縮放 ×{report.SuggestedScale:F3}）");
        }

        if (imported.Clips.Length > 0)
        {
            Console.WriteLine();
            Console.WriteLine("動作：");

            foreach (var (clip, index) in imported.Clips.Select((c, i) => (c, i)))
                Console.WriteLine($"  {index,3}  {clip}");
        }

        if (report.Issues.Count == 0)
            return;

        Console.WriteLine();

        foreach (var issue in report.Issues)
        {
            string mark = issue.Severity switch
            {
                ImportSeverity.Error => "錯誤",
                ImportSeverity.Warning => "注意",
                _ => "說明",
            };

            Console.WriteLine($"[{mark}] {issue.Title}");

            if (!string.IsNullOrEmpty(issue.Detail))
                Console.WriteLine($"       {issue.Detail}");
        }
    }

    /// <summary>
    /// 往返驗收：遊戲裡的模型 → glTF → 讀回來，比對幾何。
    /// </summary>
    /// <remarks>
    /// 匯出與匯入各自「看起來對」是不夠的。座標系、骨骼順序、頂點空間這三件事
    /// 只要有一個弄反，模型仍然畫得出來、只是不對，而用眼睛判斷不了程度。
    /// 走一趟往返再量點雲距離，就把「看起來差不多」變成一個數字。
    /// </remarks>
    public static int RoundTrip(EntityCatalog catalog, string target, string? workDirectory, string dataPath)
    {
        var entry = catalog.Entries.FirstOrDefault(e =>
                        e.FullPath is not null
                     && (e.ModelPath.Equals(target, StringComparison.OrdinalIgnoreCase)
                      || e.Name.Equals(target, StringComparison.OrdinalIgnoreCase)
                      || e.ClassName?.Equals(target, StringComparison.OrdinalIgnoreCase) == true))
                 ?? catalog.Entries.FirstOrDefault(e =>
                        e.FullPath is not null && e.Search.Contains(target, StringComparison.OrdinalIgnoreCase));

        if (entry?.FullPath is null)
        {
            Console.Error.WriteLine($"找不到「{target}」");
            return 2;
        }

        string work = workDirectory ?? Path.Combine(Path.GetTempPath(), "mu-roundtrip");
        Directory.CreateDirectory(work);

        var exported = GltfExporter.Export(entry.FullPath, work,
            new GltfExporter.Options(ExportTextures: false, Kind: entry.Kind,
                                     BodyParts: entry.BodyParts, DataPath: dataPath));

        // 匯入時不自動縮放：往返比對要的是「原封不動」，不是「縮到參考身高」。
        var imported = GltfImporter.Import(exported.GltfPath, new GltfImporter.Options(AutoScale: false));

        if (imported.Report.HasErrors)
        {
            PrintReport(entry.Name, imported);
            return 1;
        }

        var reader = new Client.Data.BMD.BMDReader();
        var original = reader.Load(entry.FullPath).GetAwaiter().GetResult();

        // 匯出時把身體部位併進去了，比對的另一側也要併，否則角色與 NPC 的
        // 「應該長什麼樣」會是空的（主模型是純骨架）。
        var parts = entry.BodyParts
            .Select(part => Path.Combine(dataPath, part))
            .Where(File.Exists)
            .Select(full => reader.Load(full).GetAwaiter().GetResult())
            .ToArray();

        var result = ModelComparer.Compare(original, parts, imported.Model);

        Console.WriteLine();
        Console.WriteLine($"── 往返：{entry.Name}（{entry.ModelPath}）──");
        Console.WriteLine($"頂點    {result.VerticesA,7} → {result.VerticesB,7}");
        Console.WriteLine($"三角形  {result.TrianglesA,7} → {result.TrianglesB,7}");
        Console.WriteLine($"骨骼    {result.BonesA,7} → {result.BonesB,7}");
        Console.WriteLine($"尺寸    {result.SizeA.X:F1} × {result.SizeA.Y:F1} × {result.SizeA.Z:F1}"
                        + $"  →  {result.SizeB.X:F1} × {result.SizeB.Y:F1} × {result.SizeB.Z:F1}");
        if (result.Comparable)
        {
            Console.WriteLine($"點雲誤差 平均 {result.MeanDistance:F4}　最大 {result.MaxDistance:F4} 世界單位"
                            + $"（相對 {result.RelativeError * 100f:F3}%）");
        }
        else
        {
            Console.WriteLine("其中一側沒有幾何，無法比對。");
        }

        foreach (var issue in imported.Report.Issues.Where(i => i.Severity != ImportSeverity.Info))
            Console.WriteLine($"[注意] {issue.Title}");

        // 0.1% 是憑經驗訂的門檻：浮點與四元數正規化的累積誤差遠小於它，
        // 而座標系或骨骼順序弄錯的話會差好幾個數量級，不會落在中間。
        bool ok = result.Comparable && result.RelativeError < 0.001f;
        Console.WriteLine(ok ? "往返一致。" : "往返不一致 —— 匯出或匯入至少有一邊弄錯了。");

        return ok ? 0 : 1;
    }
}
