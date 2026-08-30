using System.Security.Cryptography;
using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MuAssets.Core;

/// <summary>這個貼圖被哪一張圖、用什麼檔名使用。</summary>
public sealed record TextureUsage(int WorldIndex, string FileName);

/// <summary>庫裡的一個貼圖：內容唯一，可能被很多張圖共用。</summary>
public sealed record TextureLibraryEntry(
    string Id,
    string Name,
    long Bytes,
    List<TextureUsage> Usages)
{
    /// <summary>檔名說的角色（草地槽位、岩石槽位…）。</summary>
    public TextureSlot Slot { get; set; }

    /// <summary>影像說的外觀（雪白、青綠、土黃…）。與 Slot 不一定一致，見分類器的說明。</summary>
    public TextureLook Look { get; set; }

    /// <summary>量測結果。還沒分類過就是 null。</summary>
    public TextureProfile? Profile { get; set; }

    /// <summary>被幾張地圖用到。</summary>
    public int WorldCount => Usages.Select(u => u.WorldIndex).Distinct().Count();

    /// <summary>
    /// 被幾張<b>會長草</b>的地圖用到。只對搖動的草有意義 ——
    /// 客戶端寫死了一份不長草的地圖清單，那些圖裡的草貼圖是躺著不動的。
    /// </summary>
    public int GrassWorldCount => Usages
        .Select(u => u.WorldIndex)
        .Distinct()
        .Count(TerrainTextureClassifier.WorldHasGrass);
}

public sealed record TextureLibraryIndex(string DataDirectory, List<TextureLibraryEntry> Entries);

public sealed record ReplaceResult(bool Success, int FilesWritten, string[] BackedUp, string[] Skipped, string? Error);

public sealed record BatchImportResult(int Matched, string[] Applied, string[] Unmatched, string[] Warnings);

/// <summary>
/// 跨地圖的共用貼圖庫。
/// </summary>
/// <remarks>
/// <b>MU 沒有共用素材目錄</b>：客戶端是按 <c>World{N}/&lt;檔名&gt;</c> 去找貼圖的，
/// 所以每張圖各自帶一整套。實測 81 張圖有 1612 個地形貼圖檔、125 MB，
/// 但**內容唯一的只有 707 個** —— 905 個是位元組完全相同的副本，約 69 MB。
/// <c>TileGrass01.ozj</c> 在 78 張圖裡各有一份一模一樣的。
///
/// 這對「用替換貼圖逐步改進地圖外觀」是最大的阻力：想換掉草地，要改 78 個地方。
///
/// 所以這裡把「內容」與「擺放位置」分開：
/// 庫記的是內容（依雜湊去重），每一筆記得它被哪些圖用什麼檔名使用。
/// 換一次內容，所有用到的地方一起換掉 —— 而客戶端完全不知道有這回事，
/// 它看到的還是每張圖各自的檔案。
///
/// 這是刻意不動客戶端的設計：改客戶端的貼圖查找路徑是可以，
/// 但那會讓資源包與原版不相容，代價遠大於收益。
/// </remarks>
public static class TextureLibrary
{
    public const string IndexFileName = "texture-library.json";

    private static readonly string[] TextureExtensions = [".ozj", ".ozt", ".ozd", ".ozp"];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>掃過所有 <c>World*/</c>，依內容雜湊把貼圖去重。</summary>
    public static TextureLibraryIndex Build(string dataDirectory)
    {
        var byHash = new Dictionary<string, TextureLibraryEntry>(StringComparer.Ordinal);

        foreach (var world in WorldDirectory.Discover(dataDirectory).OrderBy(w => w.Index))
        {
            foreach (string path in Directory.EnumerateFiles(world.Directory))
            {
                if (!TextureExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                    continue;

                string hash = HashOf(path);
                string fileName = Path.GetFileName(path);

                if (byHash.TryGetValue(hash, out var entry))
                {
                    entry.Usages.Add(new TextureUsage(world.Index, fileName));
                    continue;
                }

                byHash[hash] = new TextureLibraryEntry(
                    hash[..12],
                    fileName,
                    new FileInfo(path).Length,
                    [new TextureUsage(world.Index, fileName)]);
            }
        }

        // 用得最廣的排前面：那些才是「換一次影響最大」的。
        var entries = byHash.Values
            .OrderByDescending(e => e.WorldCount)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new TextureLibraryIndex(dataDirectory, entries);
    }

    /// <summary>
    /// 把索引與唯一的貼圖檔存進庫目錄。
    /// </summary>
    /// <remarks>
    /// 檔名用 <c>{Id}-{原檔名}</c>：Id 保證唯一，原檔名讓人看得懂。
    /// 同名但內容不同的貼圖（不同地圖各有一版 TileGrass01）因此不會互相蓋掉。
    /// </remarks>
    public static async Task SaveAsync(TextureLibraryIndex index, string libraryDirectory)
    {
        Directory.CreateDirectory(libraryDirectory);

        foreach (var entry in index.Entries)
        {
            var first = entry.Usages[0];
            string source = Path.Combine(index.DataDirectory, $"World{first.WorldIndex}", first.FileName);
            string target = Path.Combine(libraryDirectory, FileNameFor(entry));

            if (File.Exists(source) && !File.Exists(target))
                File.Copy(source, target);
        }

        await File.WriteAllTextAsync(
            Path.Combine(libraryDirectory, IndexFileName),
            JsonSerializer.Serialize(index, JsonOptions));
    }

    public static TextureLibraryIndex? Load(string libraryDirectory)
    {
        string path = Path.Combine(libraryDirectory, IndexFileName);

        return File.Exists(path)
            ? JsonSerializer.Deserialize<TextureLibraryIndex>(File.ReadAllText(path), JsonOptions)
            : null;
    }

    public static string FileNameFor(TextureLibraryEntry entry) => $"{entry.Id}-{entry.Name}";

    /// <summary>匯出／匯入時用的 PNG 檔名。Id 在前，重繪完才對得回來。</summary>
    public static string PngNameFor(TextureLibraryEntry entry)
        => $"{entry.Id}-{Path.GetFileNameWithoutExtension(entry.Name)}.png";

    /// <summary>
    /// 量測並分類庫裡的每一筆。
    /// </summary>
    public static int Classify(TextureLibraryIndex index, string libraryDirectory)
    {
        int measured = 0;

        foreach (var entry in index.Entries)
        {
            string path = Path.Combine(libraryDirectory, FileNameFor(entry));

            if (!File.Exists(path))
                continue;

            // 槽位要看**所有**用到它的檔名，不是只看第一個。
            // 同一份內容在不同地圖裡可能叫不同名字，而只要有一處是以
            // 搖動草的檔名被引用，引擎就會把它當草載進去。
            // 實測：只看第一個名字會漏掉 6 張草貼圖（34 而不是 40）。
            entry.Slot = entry.Usages
                .Select(u => TerrainTextureClassifier.SlotOf(u.FileName))
                .OrderBy(slot => slot == TextureSlot.GrassBillboard ? 0 : slot == TextureSlot.Unknown ? 2 : 1)
                .First();

            if (TerrainTextureClassifier.Measure(path) is not { } profile)
                continue;

            entry.Profile = profile;
            entry.Look = TerrainTextureClassifier.LookOf(profile);
            measured++;
        }

        return measured;
    }

    /// <summary>
    /// 把庫裡的貼圖解成 PNG 匯出，給美術重繪。
    /// </summary>
    /// <remarks>
    /// 檔名是 <c>{Id}-{原名}.png</c> —— Id 在前，重繪完 <see cref="ImportAsync"/> 才對得回來。
    /// 改檔名會對不回來，這一點要跟美術講清楚。
    /// </remarks>
    public static int Export(
        TextureLibraryIndex index, string libraryDirectory, string outputDirectory,
        Func<TextureLibraryEntry, bool>? filter = null)
    {
        Directory.CreateDirectory(outputDirectory);
        int exported = 0;

        foreach (var entry in index.Entries)
        {
            if (filter is not null && !filter(entry))
                continue;

            string source = Path.Combine(libraryDirectory, FileNameFor(entry));
            if (!File.Exists(source))
                continue;

            using var image = TextureExporter.DecodeFile(source);
            if (image is null)
                continue;

            image.SaveAsPng(Path.Combine(outputDirectory, PngNameFor(entry)));
            exported++;
        }

        return exported;
    }

    /// <summary>
    /// 從一個目錄批次匯入重繪過的貼圖，依 Id 對回庫裡的項目。
    /// </summary>
    /// <remarks>
    /// 每一筆都會寫進所有用到它的地圖 —— 一個檔案改一次，78 張圖一起變。
    /// 對不到 Id 的檔案會照實回報，不會安靜跳過。
    /// </remarks>
    public static async Task<BatchImportResult> ImportAsync(
        TextureLibraryIndex index, string sourceDirectory, string libraryDirectory,
        bool dryRun = true, int quality = 92)
    {
        var byId = index.Entries.ToDictionary(e => e.Id, StringComparer.OrdinalIgnoreCase);
        var applied = new List<string>();
        var unmatched = new List<string>();
        var warnings = new List<string>();
        int files = 0;

        foreach (string path in Directory.EnumerateFiles(sourceDirectory)
            .Where(p => Path.GetExtension(p).Equals(".png", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal))
        {
            string name = Path.GetFileNameWithoutExtension(path);
            int dash = name.IndexOf('-');
            string id = dash > 0 ? name[..dash] : name;

            if (!byId.TryGetValue(id, out var entry))
            {
                unmatched.Add(Path.GetFileName(path));
                continue;
            }

            files++;

            // 搖動的草有一個會坑人的相依：它在世界裡的高度是從**貼圖的像素高度**
            // 算出來的（GrassRenderer：height = 貼圖高度 × 2）。
            // 所以把 64px 高的草重繪成 256px，草會變成四倍高 ——
            // 比角色還高三倍，而且不會有任何錯誤訊息。
            if (entry.Slot == TextureSlot.GrassBillboard && entry.Profile is { } original)
            {
                var replacement = TerrainTextureClassifier.Measure(path);

                if (replacement is not null && replacement.Height != original.Height)
                {
                    warnings.Add(
                        $"{entry.Name}（{entry.Id}）高度從 {original.Height} 變成 {replacement.Height} px —— " +
                        $"搖動的草在世界裡的高度 = 貼圖像素高度 × 2，" +
                        $"這會讓草變成 {(float)replacement.Height / original.Height:F1} 倍高。" +
                        "要提升解析度就只加寬、不加高，或同時改 GrassRenderer 的 GrassHeightMultiplier。");
                }
            }

            if (dryRun)
            {
                applied.Add($"{entry.Name}（{entry.Id}）→ {entry.Usages.Count} 個檔案、{entry.WorldCount} 張圖");
                continue;
            }

            var result = await ReplaceAsync(index, entry, path, libraryDirectory, quality);

            applied.Add(result.Success
                ? $"{entry.Name}（{entry.Id}）→ 改寫 {result.FilesWritten} 個檔案"
                : $"{entry.Name}（{entry.Id}）→ 失敗：{result.Error}");
        }

        return new BatchImportResult(files, [.. applied], [.. unmatched], [.. warnings]);
    }

    /// <summary>
    /// 用一張新圖取代庫裡的某一筆，並寫回所有用到它的地方。
    /// </summary>
    /// <remarks>
    /// 覆蓋前一律備份成 <c>.bak</c>：原始資源包是官方檔案，沒有版本控制。
    /// 每個目標檔案都沿用它自己的標頭（見 <see cref="TextureWriter"/>）。
    /// </remarks>
    public static async Task<ReplaceResult> ReplaceAsync(
        TextureLibraryIndex index,
        TextureLibraryEntry entry,
        string sourceImagePath,
        string? libraryDirectory = null,
        int quality = 92)
    {
        var written = new List<string>();
        var backedUp = new List<string>();
        var skipped = new List<string>();

        try
        {
            using var image = await Image.LoadAsync<Rgba32>(sourceImagePath);

            foreach (var usage in entry.Usages)
            {
                string target = Path.Combine(index.DataDirectory, $"World{usage.WorldIndex}", usage.FileName);

                if (!File.Exists(target))
                {
                    skipped.Add($"{target}（不存在）");
                    continue;
                }

                if (!TextureWriter.IsSupported(target))
                {
                    skipped.Add($"{target}（格式寫不回去）");
                    continue;
                }

                var original = await File.ReadAllBytesAsync(target);

                string backup = target + ".bak";
                if (!File.Exists(backup))
                {
                    File.Copy(target, backup);
                    backedUp.Add(backup);
                }

                await File.WriteAllBytesAsync(target, TextureWriter.Build(image, target, original, quality));
                written.Add(target);
            }

            // 庫裡那一份也要換，不然下次從庫部署又變回舊的。
            if (libraryDirectory is not null)
            {
                string libraryFile = Path.Combine(libraryDirectory, FileNameFor(entry));

                if (File.Exists(libraryFile))
                {
                    var original = await File.ReadAllBytesAsync(libraryFile);
                    await File.WriteAllBytesAsync(
                        libraryFile, TextureWriter.Build(image, libraryFile, original, quality));
                }
            }

            return new ReplaceResult(true, written.Count, [.. backedUp], [.. skipped], null);
        }
        catch (Exception ex)
        {
            return new ReplaceResult(false, written.Count, [.. backedUp], [.. skipped], ex.Message);
        }
    }

    private static string HashOf(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(MD5.HashData(stream));
    }
}
