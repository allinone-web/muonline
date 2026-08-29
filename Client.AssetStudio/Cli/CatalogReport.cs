using Client.AssetStudio.Catalog;
using Client.AssetStudio.Export;
using Client.AssetStudio.Server;

namespace Client.AssetStudio.Cli;

/// <summary>
/// 不開視窗的模式。
/// </summary>
/// <remarks>
/// 有 CLI 模式的理由與 <c>tools/AssetCheck</c> 一樣：<b>要能在終端機裡驗證</b>。
/// 「這個模型缺哪張貼圖」「有多少孤兒模型」這種問題不該需要開一個視窗、
/// 用滑鼠點過去看 —— 而且沒有視窗的工作階段（ssh、CI）根本開不起來。
/// </remarks>
public static class CatalogReport
{
    public static void Print(EntityCatalog catalog)
    {
        var stats = catalog.Stats;

        Console.WriteLine();
        Console.WriteLine("── 資源目錄 ──────────────────────────────");
        Console.WriteLine($"有類別的資源　{stats.ClassBound}");
        Console.WriteLine($"孤兒模型　　　{stats.OrphanModels}　（檔案存在，但沒有任何類別引用）");
        Console.WriteLine($"缺模型的類別　{stats.MissingModels}");
        Console.WriteLine($"看不出模型的類別　{stats.UnresolvedClasses}　（模型路徑是執行期決定的）");

        foreach (var warning in catalog.Warnings)
            Console.WriteLine($"⚠ {warning}");

        Console.WriteLine();
        Console.WriteLine("分類　　　　有類別　零件　孤兒　缺模型");

        foreach (var kind in EntityKindNames.All)
        {
            var entries = catalog.OfKind(kind);
            if (entries.Length == 0)
                continue;

            int bound = entries.Count(e => e.ClassName is not null);

            // 零件 = 被程式碼引用但沒有自己的類別（身體部位、武器、Boss 的分件）。
            // 孤兒 = 整份 Client.Main 都沒提到。兩者在「要不要重做這個素材」上意義完全不同。
            int parts = entries.Count(e => e.ClassName is null && e.IsReferenced);
            int orphan = entries.Count(e => e.ClassName is null && !e.IsReferenced);
            int missing = entries.Count(e => e.ModelMissing);

            Console.WriteLine($"{EntityKindNames.Of(kind),-10}{bound,7}{parts,7}{orphan,7}{missing,8}");
        }

        var brokenClasses = catalog.Entries.Where(e => e.ClassName is not null && e.ModelMissing).ToArray();

        if (brokenClasses.Length > 0)
        {
            Console.WriteLine();
            Console.WriteLine("── 缺模型檔的類別 ────────────────────────");

            foreach (var entry in brokenClasses)
                Console.WriteLine($"  #{entry.Number,-4} {entry.ClassName,-28} {entry.ModelPath}");
        }
    }

    /// <summary>
    /// 兩個模型的骨架能不能共用一套矩陣。
    /// </summary>
    /// <remarks>
    /// NPC 與角色的可見身體是「主模型的骨架 + 部位的網格」組起來的
    /// （遊戲端的 <c>LinkParentAnimation</c>），前提是<b>兩邊的骨頭索引指的是同一根骨頭</b>。
    /// 骨頭數不同不一定有問題（部位可以只用到前面幾根），但同一個索引對到不同名字就一定錯 ——
    /// 症狀是模型看起來「零件都在、但揉成一團」。
    /// </remarks>
    public static int CompareSkeletons(EntityCatalog catalog, string baseTarget, string partTarget)
    {
        var baseEntry = Find(catalog, baseTarget).FirstOrDefault(e => e.FullPath is not null);
        var partEntry = Find(catalog, partTarget).FirstOrDefault(e => e.FullPath is not null);

        if (baseEntry is null || partEntry is null)
        {
            Console.Error.WriteLine("兩個模型都要找得到");
            return 2;
        }

        var baseBones = ReadBoneNames(baseEntry.FullPath!);
        var partBones = ReadBoneNames(partEntry.FullPath!);

        Console.WriteLine();
        Console.WriteLine($"主模型 {baseEntry.ModelPath}　{baseBones.Length} 根骨頭");
        Console.WriteLine($"部位　 {partEntry.ModelPath}　{partBones.Length} 根骨頭");
        Console.WriteLine();

        int mismatched = 0;
        int firstMismatch = -1;
        int lastBiped = -1;

        for (int i = 0; i < Math.Min(baseBones.Length, partBones.Length); i++)
        {
            bool same = string.Equals(baseBones[i], partBones[i], StringComparison.OrdinalIgnoreCase);

            if (!same)
            {
                mismatched++;

                if (firstMismatch < 0)
                    firstMismatch = i;
            }

            // 3ds Max 的 Biped 骨頭都叫 "Bip01 …"，那些才是真正參與變形的。
            if (baseBones[i].StartsWith("Bip01", StringComparison.OrdinalIgnoreCase))
                lastBiped = i;

            Console.WriteLine($"{i,3} {(same ? " " : "x")} {baseBones[i],-24} {partBones[i]}");
        }

        Console.WriteLine();

        if (mismatched == 0)
        {
            Console.WriteLine("骨頭名稱完全一致，可以共用同一套骨骼矩陣。");
            return 0;
        }

        Console.WriteLine($"有 {mismatched} 根對不上，第一根在索引 {firstMismatch}。");

        if (firstMismatch > lastBiped)
        {
            // 尾端那幾根多半是美術自己加的輔助骨（名字像亂打的），沒有頂點綁在上面。
            Console.WriteLine($"最後一根 Biped 骨頭是索引 {lastBiped}，對不上的全在它後面 —— "
                            + "那些是美術自己加的輔助骨，共用矩陣仍然是安全的。");
            return 0;
        }

        Console.WriteLine("對不上的落在 Biped 骨頭範圍內 —— 共用主模型的骨骼矩陣會把模型揉成一團。");
        return 1;
    }

    private static string[] ReadBoneNames(string bmdPath)
    {
        var bmd = new Client.Data.BMD.BMDReader().Load(bmdPath).GetAwaiter().GetResult();

        return (bmd.Bones ?? []).Select((b, i) =>
            b is null || b == Client.Data.BMD.BMDTextureBone.Dummy
                ? "(dummy)"
                : string.IsNullOrWhiteSpace(b.Name) ? $"(bone{i})" : b.Name).ToArray();
    }

    /// <summary>某個模型的貼圖是否齊全。回傳 0 = 齊全，1 = 有缺。</summary>
    public static int Check(EntityCatalog catalog, string target)
    {
        var matches = Find(catalog, target);

        if (matches.Length == 0)
        {
            Console.Error.WriteLine($"找不到「{target}」");
            return 2;
        }

        int missingTotal = 0;

        foreach (var entry in matches.Take(20))
        {
            var summary = ModelInspector.Inspect(entry);

            Console.WriteLine();
            Console.WriteLine($"{entry.Name}　（{entry.ModelPath}）");

            if (entry.ClassName is not null)
                Console.WriteLine($"  類別 {entry.ClassName}　伺服器編號 {entry.Number}");

            if (entry.ModelMissing)
            {
                Console.WriteLine("  ❌ 模型檔不存在");
                missingTotal++;
                continue;
            }

            Console.WriteLine($"  網格 {summary.Meshes}　骨骼 {summary.Bones}　"
                            + $"動作 {summary.Actions}　三角形 {summary.Triangles:N0}");

            if (summary.Meshes == 0 && summary.Bones > 0)
            {
                Console.WriteLine("  這個 .bmd 只有骨架、沒有網格 —— 看得到的身體是另外幾個模型組起來的"
                                + "（NPCObject.SetBodyPartsAsync）。");
            }

            foreach (var attachment in entry.Attachments)
                Console.WriteLine($"  ＋ {attachment}");

            foreach (var texture in summary.Textures)
            {
                bool ok = !summary.MissingTextures.Contains(texture, StringComparer.OrdinalIgnoreCase);
                Console.WriteLine($"  {(ok ? "✓" : "❌")} {texture}");
            }

            missingTotal += summary.MissingTextures.Length;
        }

        if (matches.Length > 20)
            Console.WriteLine($"\n（另有 {matches.Length - 20} 筆符合，未列出）");

        Console.WriteLine();
        Console.WriteLine(missingTotal == 0 ? "貼圖齊全。" : $"共 {missingTotal} 項缺漏。");

        return missingTotal == 0 ? 0 : 1;
    }

    public static int Export(EntityCatalog catalog, string target, string outputDirectory, float fps)
    {
        var matches = Find(catalog, target).Where(e => e.FullPath is not null).ToArray();

        if (matches.Length == 0)
        {
            Console.Error.WriteLine($"找不到「{target}」，或它沒有模型檔");
            return 2;
        }

        int failed = 0;

        foreach (var entry in matches)
        {
            string directory = Path.Combine(outputDirectory, SafeName(entry));

            try
            {
                var result = GltfExporter.Export(entry.FullPath!, directory,
                    new GltfExporter.Options(fps, ExportTextures: true, entry.Kind));

                Console.WriteLine($"✓ {result.GltfPath}");
                Console.WriteLine($"   {result.Meshes} 網格、{result.Bones} 骨骼、"
                                + $"{result.Animations} 動畫、{result.Textures} 貼圖");

                foreach (var warning in result.Warnings)
                    Console.WriteLine($"   ⚠ {warning}");
            }
            catch (Exception ex)
            {
                failed++;
                Console.Error.WriteLine($"❌ {entry.Name}：{ex.GetType().Name} {ex.Message}");
            }
        }

        return failed == 0 ? 0 : 1;
    }

    /// <summary>
    /// 連上 OpenMU 的資料庫並把「客戶端類別」與「伺服器定義」對照印出來。
    /// </summary>
    /// <remarks>
    /// 這是整個工具最核心的一張表：外觀在客戶端、行為在伺服器，兩邊靠
    /// <c>[NpcInfo] typeId</c> ↔ <c>MonsterDefinition.Number</c> 對上。
    /// 對不上的兩種情況都要看得到 ——
    /// 「客戶端有類別、伺服器沒定義」= 這隻怪不會出現；
    /// 「伺服器有定義、客戶端沒類別」= 出現一隻沒有模型的怪。
    /// </remarks>
    public static async Task<int> PrintServerAsync(EntityCatalog catalog, string? connectionString, string? filter)
    {
        var repository = new OpenMuRepository();

        if (!string.IsNullOrWhiteSpace(connectionString))
            repository.ConnectionString = connectionString;

        Dictionary<short, MonsterRow> monsters;

        try
        {
            monsters = await repository.LoadMonstersAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"連不上資料庫：{ex.Message}");
            Console.Error.WriteLine($"連線字串：{repository.ConnectionString}");
            return 2;
        }

        var byNumber = catalog.Entries
            .Where(e => e.ClassName is not null && e.Number >= 0)
            .GroupBy(e => (short)e.Number)
            .ToDictionary(g => g.Key, g => g.ToArray());

        Console.WriteLine();
        Console.WriteLine($"資料庫有 {monsters.Count} 筆 MonsterDefinition，客戶端有 {byNumber.Count} 個編號");

        int both = monsters.Keys.Count(byNumber.ContainsKey);
        Console.WriteLine($"兩邊都有　　　　{both}");
        Console.WriteLine($"只有伺服器有　　{monsters.Count - both}　（會出現一隻沒有模型的怪）");
        Console.WriteLine($"只有客戶端有　　{byNumber.Count - both}　（這個類別不會被生出來）");

        Console.WriteLine();
        Console.WriteLine("編號  伺服器名稱              客戶端類別            模型                  移動  攻擊   射程 視野   HP");

        foreach (var (number, row) in monsters.OrderBy(p => p.Key))
        {
            var entries = byNumber.GetValueOrDefault(number);

            if (filter is not null
                && !row.Designation.Contains(filter, StringComparison.OrdinalIgnoreCase)
                && !number.ToString().Equals(filter, StringComparison.Ordinal)
                && entries?.Any(e => e.Search.Contains(filter, StringComparison.OrdinalIgnoreCase)) != true)
            {
                continue;
            }

            string className = entries is null ? "（無）" : string.Join("/", entries.Select(e => e.ClassName));
            string model = entries?.FirstOrDefault()?.ModelPath ?? "－";
            float health = row.Attributes.FirstOrDefault(a => a.Designation == "Maximum Health")?.Value ?? 0f;

            Console.WriteLine(
                $"{number,-5} {Trim(row.Designation, 22),-22} {Trim(className, 20),-20} "
              + $"{Trim(Path.GetFileName(model), 20),-20} "
              + $"{row.MoveDelay.TotalMilliseconds,5:F0} {row.AttackDelay.TotalMilliseconds,5:F0} "
              + $"{row.AttackRange,5} {row.ViewRange,4} {health,7:F0}");
        }

        return 0;
    }

    private static string Trim(string value, int length)
        => value.Length <= length ? value : value[..(length - 1)] + "…";

    /// <summary>名稱、類別名、相對路徑、檔名都可以拿來找。</summary>
    private static EntityEntry[] Find(EntityCatalog catalog, string target)
    {
        var exact = catalog.Entries.Where(e =>
                e.ModelPath.Equals(target, StringComparison.OrdinalIgnoreCase)
             || e.Name.Equals(target, StringComparison.OrdinalIgnoreCase)
             || e.ClassName?.Equals(target, StringComparison.OrdinalIgnoreCase) == true)
            .ToArray();

        if (exact.Length > 0)
            return exact;

        return catalog.Entries
            .Where(e => e.Search.Contains(target, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private static string SafeName(EntityEntry entry)
    {
        string name = entry.Number >= 0 ? $"{entry.Number:000}_{entry.Name}" : entry.Name;

        foreach (char invalid in Path.GetInvalidFileNameChars())
            name = name.Replace(invalid, '_');

        return name.Replace(' ', '_');
    }
}
