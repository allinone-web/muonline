namespace MuAssets.Core;

/// <summary>
/// 掃過所有 <c>Object{N}</c> 目錄，回報自動分類的覆蓋率。
/// </summary>
/// <remarks>
/// 用 <c>--catalog-report</c> 觸發。刻意不傳語意型別表 —— 那需要 MuGame 才能建 world 類別，
/// 而這份報告要量的是「檔名 + BMD 貼圖名」這兩個自動來源本身有多少覆蓋率。
/// </remarks>
public static class AssetCatalogReport
{
    public static void Print(string dataPath) => Print(dataPath, useShape: true);

    public static void Print(string dataPath, bool useShape)
    {
        var catalog = new AssetCatalog(Path.Combine(Path.GetTempPath(), "maptool-catalog-report.json"))
        {
            UseShapeFallback = useShape,
        };

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
        // 這份要量的是「前面幾條線索」剩下什麼，所以不跑形狀那一步。
        var catalog = new AssetCatalog(Path.Combine(Path.GetTempPath(), "maptool-catalog-report.json"))
        {
            UseShapeFallback = false,
        };
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

    /// <summary>
    /// 量已知類別的模型，看「貼圖外觀 + 透明度」這條線索分不分得開。
    /// </summary>
    /// <remarks>
    /// 先驗證再實作。檔名與貼圖名這兩條線索對剩下的 1307 個已經沒用了
    /// （它們引用的是 BosBB、angeflo_R 這種毫無語意的名字），
    /// 所以要換一條線索 —— 但換之前得先確認它在**已知答案**上分得開，
    /// 不然只是把猜測換一種形式。
    /// </remarks>
    public static void PrintSignalStudy(string dataPath, int samplePerCategory = 60)
    {
        var catalog = new AssetCatalog(Path.Combine(Path.GetTempPath(), "maptool-signal-study.json"))
        {
            UseShapeFallback = false,
        };
        var buckets = new Dictionary<AssetCategory, List<(float Green, float Alpha, float Value, float Sat)>>();

        foreach (var directory in Directory.EnumerateDirectories(dataPath, "Object*").OrderBy(d => d))
        {
            if (!int.TryParse(Path.GetFileName(directory).AsSpan("Object".Length), out int worldIndex))
                continue;

            foreach (var asset in catalog.Scan(dataPath, worldIndex, semanticTypes: null))
            {
                var list = buckets.TryGetValue(asset.Category, out var existing) ? existing : buckets[asset.Category] = [];

                if (list.Count >= samplePerCategory)
                    continue;

                if (MeasureTextures(directory, asset.Path) is { } measured)
                    list.Add(measured);
            }
        }

        Console.WriteLine($"{"類別",-12}{"樣本",5}{"綠色調%",9}{"透明%",8}{"明度",7}{"飽和",7}");
        Console.WriteLine(new string('-', 50));

        foreach (var (category, samples) in buckets.OrderByDescending(kv => kv.Value.Count))
        {
            if (samples.Count == 0)
                continue;

            Console.WriteLine(
                $"{AssetCategoryNames.Of(category),-12}{samples.Count,5}" +
                $"{samples.Average(s => s.Green) * 100,9:F0}{samples.Average(s => s.Alpha) * 100,8:F0}" +
                $"{samples.Average(s => s.Value),7:F2}{samples.Average(s => s.Sat),7:F2}");
        }
    }

    /// <summary>
    /// 量已知類別的模型幾何，看尺寸與形狀分不分得開。
    /// </summary>
    public static void PrintGeometryStudy(string dataPath, int samplePerCategory = 60)
    {
        var catalog = new AssetCatalog(Path.Combine(Path.GetTempPath(), "maptool-geometry-study.json"))
        {
            UseShapeFallback = false,
        };
        var buckets = new Dictionary<AssetCategory, List<(float Width, float Height, float Flatness, int Triangles, int Actions)>>();

        foreach (var directory in Directory.EnumerateDirectories(dataPath, "Object*").OrderBy(d => d))
        {
            if (!int.TryParse(Path.GetFileName(directory).AsSpan("Object".Length), out int worldIndex))
                continue;

            foreach (var asset in catalog.Scan(dataPath, worldIndex, semanticTypes: null))
            {
                var list = buckets.TryGetValue(asset.Category, out var existing) ? existing : buckets[asset.Category] = [];

                if (list.Count >= samplePerCategory)
                    continue;

                if (MeasureGeometry(asset.Path) is { } measured)
                    list.Add(measured);
            }
        }

        Console.WriteLine($"{"類別",-12}{"樣本",5}{"寬",9}{"高",9}{"高寬比",9}{"三角形",9}{"動作",6}");
        Console.WriteLine(new string('-', 60));

        foreach (var (category, samples) in buckets.OrderByDescending(kv => kv.Value.Count))
        {
            if (samples.Count == 0)
                continue;

            Console.WriteLine(
                $"{AssetCategoryNames.Of(category),-12}{samples.Count,5}" +
                $"{samples.Average(s => s.Width),9:F0}{samples.Average(s => s.Height),9:F0}" +
                $"{samples.Average(s => s.Flatness),9:F2}{samples.Average(s => s.Triangles),9:F0}" +
                $"{samples.Average(s => s.Actions),6:F1}");
        }
    }

    /// <summary>
    /// 量形狀規則的精確度：拿「已經有把握的分類」當答案，看規則猜得對不對。
    /// </summary>
    /// <remarks>
    /// 有把握的來源是語意類別與檔名關鍵字 —— 那兩條不是推測。
    /// 擺放位置與形狀本身都是推測，不能拿來當答案（會自我印證）。
    ///
    /// 這一步是必要的：規則是從各類別的平均值訂出來的，
    /// 平均值分得開不代表個體分得開。不量精確度就等於在猜。
    /// </remarks>
    public static void PrintShapePrecision(string dataPath)
    {
        var catalog = new AssetCatalog(Path.Combine(Path.GetTempPath(), "maptool-precision.json"))
        {
            UseShapeFallback = false,
        };

        // 預測 → （總次數, 猜對次數）
        var stats = new Dictionary<AssetCategory, (int Predicted, int Correct)>();
        var confusion = new Dictionary<(AssetCategory Predicted, AssetCategory Truth), int>();

        foreach (var directory in Directory.EnumerateDirectories(dataPath, "Object*").OrderBy(d => d))
        {
            if (!int.TryParse(Path.GetFileName(directory).AsSpan("Object".Length), out int worldIndex))
                continue;

            foreach (var asset in catalog.Scan(dataPath, worldIndex, semanticTypes: null))
            {
                // 只拿有把握的當答案。
                if (asset.Category == AssetCategory.Unclassified
                    || asset.CategorySource.StartsWith("擺放位置", StringComparison.Ordinal))
                {
                    continue;
                }

                if (ModelShapeClassifier.Measure(asset.Path, directory) is not { } shape)
                    continue;

                if (!ModelShapeClassifier.TryClassify(shape, out var predicted))
                    continue;

                var (count, correct) = stats.GetValueOrDefault(predicted);
                bool hit = predicted == asset.Category;
                stats[predicted] = (count + 1, correct + (hit ? 1 : 0));

                if (!hit)
                {
                    var key = (predicted, asset.Category);
                    confusion[key] = confusion.GetValueOrDefault(key) + 1;
                }
            }
        }

        Console.WriteLine($"{"規則猜的",-12}{"次數",6}{"猜對",6}{"精確度",9}");
        Console.WriteLine(new string('-', 36));

        foreach (var (category, (count, correct)) in stats.OrderByDescending(kv => kv.Value.Predicted))
        {
            Console.WriteLine(
                $"{AssetCategoryNames.Of(category),-12}{count,6}{correct,6}{(count == 0 ? 0 : correct * 100.0 / count),8:F0}%");
        }

        Console.WriteLine();
        Console.WriteLine("猜錯時最常錯成什麼：");

        foreach (var ((predicted, truth), count) in confusion.OrderByDescending(kv => kv.Value).Take(10))
        {
            Console.WriteLine(
                $"  猜 {AssetCategoryNames.Of(predicted),-8} 其實是 {AssetCategoryNames.Of(truth),-8} ×{count}");
        }
    }

    private static (float Width, float Height, float Flatness, int Triangles, int Actions)? MeasureGeometry(string bmdPath)
    {
        try
        {
            var model = new Client.Data.BMD.BMDReader().Load(bmdPath).GetAwaiter().GetResult();

            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;
            int triangles = 0;

            foreach (var mesh in model.Meshes)
            {
                triangles += mesh.Triangles.Length;

                foreach (var vertex in mesh.Vertices)
                {
                    minX = MathF.Min(minX, vertex.Position.X); maxX = MathF.Max(maxX, vertex.Position.X);
                    minY = MathF.Min(minY, vertex.Position.Y); maxY = MathF.Max(maxY, vertex.Position.Y);
                    minZ = MathF.Min(minZ, vertex.Position.Z); maxZ = MathF.Max(maxZ, vertex.Position.Z);
                }
            }

            if (minX > maxX)
                return null;

            float width = MathF.Max(maxX - minX, maxY - minY);
            float height = maxZ - minZ;

            return (width, height, width <= 0.01f ? 0f : height / width, triangles, model.Actions.Length);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 量一個模型引用的所有貼圖，取平均。
    /// </summary>
    /// <returns>（綠色調的比例、透明像素比例、明度、飽和度）；一張都讀不到時回 null。</returns>
    private static (float Green, float Alpha, float Value, float Sat)? MeasureTextures(string directory, string bmdPath)
    {
        float green = 0, alpha = 0, value = 0, saturation = 0;
        int count = 0;

        foreach (string name in AssetCatalog.TextureNames(bmdPath))
        {
            string? found = FindTexture(directory, name);
            if (found is null)
                continue;

            if (TerrainTextureClassifier.Measure(found) is not { } profile)
                continue;

            // 「綠色調」不是看平均色（樹的平均色偏暗黃），而是看色相落不落在綠色區間。
            green += profile.Hue is >= 60f and < 170f ? 1f : 0f;
            alpha += profile.TransparentRatio;
            value += profile.Value;
            saturation += profile.Saturation;
            count++;
        }

        return count == 0 ? null : (green / count, alpha / count, value / count, saturation / count);
    }

    private static string? FindTexture(string directory, string name)
    {
        foreach (string extension in new[] { ".ozj", ".ozt", ".ozd", ".ozp" })
        {
            string candidate = Path.Combine(directory, Path.GetFileNameWithoutExtension(name) + extension);

            if (File.Exists(candidate))
                return candidate;

            foreach (string existing in Directory.EnumerateFiles(directory,
                Path.GetFileNameWithoutExtension(name) + ".*"))
            {
                if (existing.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                    return existing;
            }
        }

        return null;
    }

    private static int Index(string directory)
        => int.TryParse(Path.GetFileName(directory).AsSpan("Object".Length), out int n) ? n : -1;
}
