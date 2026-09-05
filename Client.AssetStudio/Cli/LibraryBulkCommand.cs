using Client.AssetStudio.Catalog;
using Client.AssetStudio.Project;

namespace Client.AssetStudio.Cli;

/// <summary>
/// 把一整個資料夾的 glTF/GLB 一次收進資源庫，讓它們出現在瀏覽器的目錄裡。
/// </summary>
/// <remarks>
/// <b>為什麼要有這個。</b>天堂（梦想与征程）那批解析成果有 1,537 個角色與 36 把武器，
/// 每個都是「一個資料夾 ＋ 一個 .glb ＋ 三張 PNG」。一個一個 <c>--library-add</c>
/// 要跑 1,573 次，每次都重掃一遍資源目錄 —— 實務上不可能。
///
/// <b>預設是 link 不是 copy。</b>那批平均 2.7 MB，複製下來是 4 GB，
/// 而它們本來就在另一個 repo 裡受版本管理。複製只會多出一份會過期的副本。
/// 代價是來源移動就壞掉，所以 <c>--copy</c> 留著給要落地的情況。
///
/// <b>已經收過的會跳過</b>，可以重跑。來源那邊新增了資產，再跑一次就補進來。
/// </remarks>
public static class LibraryBulkCommand
{
    public static int Run(AssetLibrary library, string directory, EntityKind kind,
                          bool copy, int limit, string? filter, bool autoKind = false)
    {
        if (!Directory.Exists(directory))
        {
            Console.Error.WriteLine($"找不到資料夾：{directory}");
            return 2;
        }

        // 一個資產 = 一個子資料夾，裡面剛好一個 .glb 或 .gltf。
        // 直接躺在根目錄的模型也收，那是另一種常見的擺法。
        var candidates = new List<(string Name, string Path)>();

        foreach (string sub in Directory.EnumerateDirectories(directory).OrderBy(p => p))
        {
            string? model = FindModel(sub);
            if (model is not null)
                candidates.Add((Path.GetFileName(sub), model));
        }

        foreach (string file in Directory.EnumerateFiles(directory).OrderBy(p => p))
        {
            if (IsModel(file))
                candidates.Add((Path.GetFileNameWithoutExtension(file), file));
        }

        if (filter is not null)
        {
            candidates = candidates
                .Where(c => c.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (candidates.Count == 0)
        {
            Console.Error.WriteLine($"{directory} 底下找不到任何 .glb / .gltf");
            return 2;
        }

        var existing = library.Assets.Select(a => a.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        int added = 0, skipped = 0, failed = 0;
        var failures = new List<string>();

        Console.WriteLine();
        Console.WriteLine($"找到 {candidates.Count} 個模型，{(copy ? "複製" : "只記路徑")}模式");

        foreach (var (name, path) in candidates)
        {
            if (limit > 0 && added >= limit)
                break;

            if (existing.Contains(name))
            {
                skipped++;
                continue;
            }

            // --kind auto：從包名前綴判斷。不加這一層的話 1,514 個全部當怪物收，
            // 裡面混著 NPC、寶箱、裝飾物。
            var resolved = autoKind ? LineageNaming.Classify(name, kind).Kind : kind;
            var asset = library.Add(path, name, resolved, out _, link: !copy);
            if (asset is null)
            {
                failed++;
                if (failures.Count < 10)
                    failures.Add($"{name}：{library.LastError}");
                continue;
            }

            added++;
            if (added % 100 == 0)
                Console.WriteLine($"  …已收 {added} 個");
        }

        Console.WriteLine();
        Console.WriteLine($"新增 {added}　已存在跳過 {skipped}　失敗 {failed}");

        if (autoKind)
        {
            int unknown = candidates.Count(c => !LineageNaming.IsKnown(c.Name));
            if (unknown > 0)
                Console.WriteLine($"其中 {unknown} 個包名認不出類型，用了預設的 {EntityKindNames.Of(kind)}");
        }
        foreach (string failure in failures)
            Console.WriteLine($"  [失敗] {failure}");

        Console.WriteLine();
        Console.WriteLine($"資源庫現在有 {library.Assets.Count} 筆 → {library.Root}");
        Console.WriteLine("在瀏覽器裡看：分類選「資源庫」");

        return failed > 0 && added == 0 ? 1 : 0;
    }

    private static string? FindModel(string directory)
    {
        // .glb 優先。同一個資料夾裡兩者都有時，.glb 是單檔、不會漏掉相依檔。
        return Directory.EnumerateFiles(directory)
            .Where(IsModel)
            .OrderBy(p => Path.GetExtension(p).Equals(".glb", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .FirstOrDefault();
    }

    private static bool IsModel(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".glb" or ".gltf";
}
