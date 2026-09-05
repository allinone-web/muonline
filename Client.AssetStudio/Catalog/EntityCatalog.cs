using System.Reflection;

namespace Client.AssetStudio.Catalog;

/// <summary>目錄裡的一筆資源。可能是「有類別的怪物」，也可能是「沒人引用的孤兒模型」。</summary>
public sealed record EntityEntry
{
    public required EntityKind Kind { get; init; }

    /// <summary>
    /// <c>[NpcInfo]</c> 的 TypeId，也就是 OpenMU <c>MonsterDefinition.Number</c>。
    /// 沒有對應類別的模型是 -1。
    /// </summary>
    public int Number { get; init; } = -1;

    public required string Name { get; init; }

    /// <summary><c>Client.Main</c> 裡的類別名稱，沒有就是純檔案。</summary>
    public string? ClassName { get; init; }

    /// <summary>相對於 <c>Data/</c> 的模型路徑，例如 <c>Monster/Monster33.bmd</c>。</summary>
    public required string ModelPath { get; init; }

    /// <summary>檔案系統上的絕對路徑；模型檔不存在時為 null。</summary>
    public string? FullPath { get; init; }

    /// <summary>同一個類別另外直接載入的模型（座騎、法杖、分件的 Boss…）。</summary>
    public string[] Attachments { get; init; } = [];

    /// <summary>
    /// <c>SetBodyPartsAsync</c> 組出來的身體部位。
    /// </summary>
    /// <remarks>
    /// 與 <see cref="Attachments"/> 分開，因為兩者在渲染上完全不同：
    /// 身體部位<b>共用主模型的骨架</b>（遊戲端是 <c>LinkParentAnimation</c>），
    /// 可以直接掛上去一起畫；武器與座騎則各自綁在特定骨頭上、有自己的變換，
    /// 用主骨架去畫會擺在完全錯誤的位置。
    /// </remarks>
    public string[] BodyParts { get; init; } = [];

    public bool ModelMissing => FullPath is null;

    /// <summary>
    /// 這一筆在<b>語意上</b>屬於哪一類。<see cref="Kind"/> 說的是「怎麼載入」。
    /// </summary>
    /// <remarks>
    /// 兩者必須分開。<see cref="EntityKind.Library"/> 是<b>載入機制</b>
    /// （走 glTF 而不是 BMD），不是「這是什麼東西」——
    /// 天堂那 1,514 個角色語意上就是怪物，卻因為 Kind 是 Library
    /// 全部擠在「資源庫」那一格裡，跟 MU 的怪物完全看不到彼此。
    ///
    /// 分開之後：分類看 <see cref="SemanticKind"/>（怪物歸怪物），
    /// 載入看 <see cref="Kind"/>（Library 走 GltfImporter）。
    /// 來源用 <see cref="Group"/> 區分，所以在同一個「怪物」底下
    /// 仍然分得出哪些是 MU 原生、哪些是匯入的。
    /// </remarks>
    /// <remarks>
    /// ★ 後備值一定要用 <c>null</c>，不能用「等於 default 就代表沒設」——
    /// <c>EntityKind.Monster</c> 的值就是 0，也就是 default。
    /// 用 default 當哨兵的話，<b>所有語意上是怪物的匯入資產都會被判成「沒設」</b>
    /// 然後被覆寫回 Library，而且看起來完全正常。
    /// </remarks>
    public EntityKind SemanticKind
    {
        get => _semanticKind ?? Kind;
        init => _semanticKind = value;
    }

    private readonly EntityKind? _semanticKind;

    /// <summary>
    /// 這個檔案有沒有被任何類別引用（當主模型、身體部位或附掛模型都算）。
    /// </summary>
    /// <remarks>
    /// 只對「沒有類別的純檔案」有意義。被引用卻沒有自己的類別，代表它是
    /// 某個類別的零件（<c>Npc/ManUpper02.bmd</c>）；完全沒被引用的才是孤兒。
    /// </remarks>
    public bool IsReferenced { get; init; } = true;

    /// <summary>
    /// 語意上的子分類（劍／頭盔／職業預設身體…）。空字串代表這一類沒有子分類。
    /// </summary>
    /// <remarks>
    /// 目錄的第一層是「檔案在哪個資料夾」，那是結構不是語意。
    /// 道具有 2715 個模型，檔名幾乎不帶語意；沒有這一層，
    /// 「把所有的弓列出來」這種最基本的問題就只能靠猜檔名。
    /// </remarks>
    public string Group { get; init; } = string.Empty;

    /// <summary>子分類之外的補充（道具名稱、部位變體、編號…）。</summary>
    public string Detail { get; init; } = string.Empty;

    /// <summary>清單裡的唯一鍵，也是 ImGui 的 ID。</summary>
    /// <summary>
    /// 這一筆在介面上的識別碼。<b>必須唯一。</b>
    /// </summary>
    /// <remarks>
    /// 加上 <see cref="Detail"/> 是必要的，不是保險：職業角色那 56 筆共用
    /// 同一個主模型（<c>Player/Player.bmd</c>），只靠路徑的話 56 筆會撞成同一個 id ——
    /// ImGui 會抱怨「conflicting ID」，而且選取會同時命中好幾筆。
    /// </remarks>
    public string Id => ClassName is not null
        ? $"{Kind}:{ClassName}"
        : $"{Kind}:{ModelPath}:{Detail}";

    public string Search => $"{Name} {ClassName} {ModelPath} {Number} {Group} {Detail}";
}

public sealed record CatalogStats(
    int ClassBound,
    int OrphanModels,
    int MissingModels,
    int UnresolvedClasses);

/// <summary>
/// 把「程式碼裡的類別」與「磁碟上的模型檔」併成一份可瀏覽的目錄。
/// </summary>
/// <remarks>
/// 兩邊都不是完整的：
/// <list type="bullet">
/// <item>Data/Monster 有 552 個 .bmd，Client.Main 只有 401 個怪物類別 —— 多出來的是
/// 沒被任何類別引用的模型（改版遺留、Boss 的分件、未啟用的內容）。</item>
/// <item>反過來也有類別找不到模型檔，那是資源包版本與程式碼不同步。</item>
/// </list>
/// 兩種都要顯示出來，而不是安靜地少一筆 —— 「這個模型沒人用」與「這個怪缺模型」
/// 都是替換素材時必須知道的事實。
/// </remarks>
public sealed class EntityCatalog
{
    private static readonly (EntityKind Kind, string Directory)[] ModelDirectories =
    [
        (EntityKind.Monster, "Monster"),
        (EntityKind.Npc, "NPC"),
        (EntityKind.Player, "Player"),
        (EntityKind.SkillModel, "Skill"),
        (EntityKind.Item, "Item"),
        (EntityKind.Effect, "Effect"),
    ];

    public EntityEntry[] Entries { get; private set; } = [];

    /// <summary>
    /// 每個大分類底下的子分類，依數量排序。<b>在 Build 時算好。</b>
    /// </summary>
    /// <remarks>
    /// UI 的工具列每幀都會問一次「這一類有哪些子分類」。
    /// 每幀對 4739 筆做 GroupBy 會持續配置記憶體、給 GC 找事做，
    /// 而這份資料在 <see cref="Build"/> 之後就不會變了。
    /// </remarks>
    private Dictionary<EntityKind, string[]> _groups = [];

    public CatalogStats Stats { get; private set; } = new(0, 0, 0, 0);

    /// <summary>掃描過程中的問題，UI 會顯示出來（例如 Client.Main 有型別載入不了）。</summary>
    public List<string> Warnings { get; } = [];

    public void Build(string dataPath, ItemCatalog? items = null, Project.AssetLibrary? library = null)
    {
        Warnings.Clear();

        var files = IndexFiles(dataPath);

        // primary = 已經有一筆類別項目代表它的檔案，不必再列一次。
        // referenced = 任何被程式碼提到過的檔案，用來區分「零件」與「真孤兒」。
        var primary = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<EntityEntry>();

        int unresolvedClasses = 0;
        int missing = 0;

        foreach (var (type, attribute) in DiscoverEntityTypes())
        {
            var kind = KindOf(type);
            var references = ModelPathScanner.Scan(type);
            var paths = references.Select(r => r.Path).ToArray();

            // 主模型 = 第一個「直接指定」而且落在這個類別自己的資料夾裡的路徑。
            // 身體部位不能當主模型 —— NPC 的主模型是骨架（Man01.bmd，零網格），
            // 部位才是看得到的那些，但代表這個 NPC 的仍然是骨架那一個。
            string? primaryPath = references.FirstOrDefault(r => r.Source == "直接指定" && MatchesKind(r.Path, kind)).Path
                           ?? references.FirstOrDefault(r => r.Source == "直接指定").Path
                           ?? paths.FirstOrDefault();

            if (primaryPath is null)
            {
                unresolvedClasses++;
                continue;
            }

            string? full = Resolve(files, primaryPath);
            if (full is null)
                missing++;
            else
                primary.Add(primaryPath);

            foreach (var attachment in paths)
            {
                if (Resolve(files, attachment) is not null)
                    referenced.Add(attachment);
            }

            var bodyParts = references
                .Where(r => r.Source == "身體部位" && Resolve(files, r.Path) is not null)
                .Select(r => r.Path)
                .ToArray();

            entries.Add(new EntityEntry
            {
                Kind = kind,
                // 沒有 [NpcInfo] 的類別對不到伺服器，用 -1 表示；名稱就用類別名。
                Number = attribute?.TypeId ?? -1,
                Name = attribute?.DisplayName ?? type.Name,
                ClassName = type.Name,
                ModelPath = primaryPath,
                FullPath = full,
                BodyParts = bodyParts,
                Attachments = references
                    .Where(r => r.Source != "身體部位")
                    .Select(r => r.Path)
                    .Where(p => !p.Equals(primaryPath, StringComparison.OrdinalIgnoreCase))
                    .ToArray(),
            });
        }

        // 職業角色：把 Player/ 底下散落的部位組成「一個看得到的角色」。
        // 沒有這一步的話目錄裡只有一件一件的上衣與褲子，
        // 沒辦法回答「這個職業長什麼樣、動起來如何」。
        foreach (var player in PlayerClassCatalog.Build(path => Resolve(files, path)))
        {
            entries.Add(player);
            primary.Add(player.ModelPath);

            foreach (var part in player.BodyParts)
                referenced.Add(part);
        }

        int classBound = entries.Count;
        int orphans = 0;

        foreach (var (kind, directory) in ModelDirectories)
        {
            foreach (var (relative, full) in files.Where(f => f.Key.StartsWith(directory + "/", StringComparison.OrdinalIgnoreCase)))
            {
                // 已經有類別項目代表它的就不重複列；
                // 被引用但沒有自己的類別的零件仍然要列出來 —— 「換掉這件上衣的貼圖」
                // 是很自然的需求，而零件不會有自己的 [NpcInfo]。
                if (primary.Contains(relative))
                    continue;

                bool used = referenced.Contains(relative);
                if (!used)
                    orphans++;

                entries.Add(new EntityEntry
                {
                    Kind = kind,
                    Name = Path.GetFileNameWithoutExtension(relative),
                    ModelPath = relative,
                    FullPath = full,
                    IsReferenced = used,
                });
            }
        }

        // 語意分類。放在最後統一做，因為它與「這一筆是從類別來的還是從檔案來的」無關。
        entries = entries.Select(e => Classify(e, items)).ToList();

        if (library is not null)
            entries.AddRange(LibraryEntries(library));

        Entries = entries
            .OrderBy(e => e.SemanticKind)
            .ThenBy(e => e.Group, StringComparer.Ordinal)
            .ThenBy(e => e.ClassName is null)          // 有類別的排前面
            .ThenBy(e => e.Number < 0 ? int.MaxValue : e.Number)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _groups = Entries
            .Where(e => e.Group.Length > 0)
            .GroupBy(e => e.SemanticKind)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(e => e.Group)
                      .OrderByDescending(x => x.Count())
                      .Select(x => x.Key)
                      .ToArray());

        Stats = new CatalogStats(classBound, orphans, missing, unresolvedClasses);
    }

    /// <summary>把資源庫裡的自有資產轉成目錄項目。</summary>
    /// <remarks>
    /// 刻意<b>不</b>在這裡解析 glTF。目錄要能在幾十毫秒內建好，
    /// 而每個 glb 都跑一次 <c>GltfImporter</c> 會讓啟動時間跟資產數量成正比。
    /// 解析延後到「使用者真的點下去」那一刻（<c>StudioGame.ProcessLoadRequest</c>）。
    /// </remarks>
    private static IEnumerable<EntityEntry> LibraryEntries(Project.AssetLibrary library)
    {
        foreach (var asset in library.Assets)
        {
            string source = library.SourcePathOf(asset);

            yield return new EntityEntry
            {
                Kind = EntityKind.Library,
                // 語意上它是一隻怪物／一件道具，只是載入方式不同（見 SemanticKind 的說明）
                SemanticKind = asset.Kind,
                Number = asset.BindNumber,
                Name = asset.Name,
                ClassName = null,
                // 同一個「怪物」分類底下要分得出來源，否則 MU 原生的 591 隻
                // 會被匯入的 1,514 個淹掉，而且看不出差別。
                Group = "資源庫（匯入）",
                Detail = asset.BindNumber >= 0 ? $"綁定 #{asset.BindNumber}" : string.Empty,
                ModelPath = asset.Source,
                FullPath = File.Exists(source) ? source : null,
                IsReferenced = true,
            };
        }
    }

    /// <summary>某一類底下的全部資源。用<b>語意分類</b>，所以匯入的怪物也在「怪物」裡。</summary>
    public EntityEntry[] OfKind(EntityKind kind) => Entries.Where(e => e.SemanticKind == kind).ToArray();

    /// <summary>某一類底下實際出現過的子分類，依數量排序。</summary>
    public string[] GroupsOf(EntityKind kind) => _groups.GetValueOrDefault(kind, []);

    /// <summary>
    /// 補上語意分類。
    /// </summary>
    /// <remarks>
    /// 三類各有各的真相來源，不能用同一套規則：
    /// <list type="bullet">
    /// <item><b>道具</b> —— <c>item.bmd</c>（模型路徑 → 群組 + 名稱）。</item>
    /// <item><b>角色</b> —— 檔名規則，因為根本沒有資料檔描述那個資料夾。</item>
    /// <item><b>怪物 / NPC</b> —— 有沒有伺服器編號就是最有用的分類，
    /// 那決定了它能不能真的出現在遊戲裡。</item>
    /// </list>
    /// </remarks>
    private static EntityEntry Classify(EntityEntry entry, ItemCatalog? items)
    {
        // 已經自己標好分類的不要再動 —— 職業角色是組合出來的，
        // 它的 ModelPath 是共用骨架，照檔名分類會把 15 個職業全歸成「角色骨架」。
        if (entry.Group.Length > 0)
            return entry;

        switch (entry.Kind)
        {
            case EntityKind.Item when items is not null:
            {
                string? group = items.GroupOf(entry.ModelPath);
                string? name = items.NameOf(entry.ModelPath);

                return entry with
                {
                    Group = group ?? "未對應",
                    Detail = name ?? string.Empty,
                    // 道具名稱比檔名有用得多，但檔名要留著才搜尋得到。
                    Name = name is null ? entry.Name : $"{name}（{entry.Name}）",
                };
            }

            case EntityKind.Player:
            {
                var classification = PlayerPartClassifier.Classify(entry.ModelPath);
                return entry with { Group = classification.Group, Detail = classification.Detail };
            }

            case EntityKind.Monster or EntityKind.Npc:
            {
                string group = entry.ClassName is null
                    ? (entry.IsReferenced ? "零件（被其他類別引用）" : "沒有類別")
                    : entry.Number >= 0 ? "有伺服器編號" : "有類別、無伺服器編號";

                return entry with { Group = group };
            }

            default:
                return entry;
        }
    }

    /// <summary>哪些類別用到這個模型檔。孤兒模型的判斷與「誰在用」是同一份資料。</summary>
    public string[] UsersOf(string modelPath) => Entries
        .Where(e => e.ClassName is not null
                 && (e.ModelPath.Equals(modelPath, StringComparison.OrdinalIgnoreCase)
                  || e.Attachments.Contains(modelPath, StringComparer.OrdinalIgnoreCase)
                  || e.BodyParts.Contains(modelPath, StringComparer.OrdinalIgnoreCase)))
        .Select(e => e.ClassName!)
        .ToArray();

    // ── 掃描 ────────────────────────────────────────────────────────

    /// <summary>
    /// <c>Data/</c> 底下所有 <c>.bmd</c>，鍵是「相對路徑、正斜線、原始大小寫」。
    /// 查找時忽略大小寫 —— 資源包裡 <c>MONSTER158.bmd</c> 與 <c>Monster01.bmd</c> 並存。
    /// </summary>
    private static Dictionary<string, string> IndexFiles(string dataPath)
    {
        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (_, directory) in ModelDirectories)
        {
            string root = Path.Combine(dataPath, directory);
            if (!Directory.Exists(root))
                continue;

            foreach (var file in Directory.EnumerateFiles(root, "*.bmd", SearchOption.AllDirectories))
            {
                if (!IsModel(file))
                    continue;

                string relative = Path.GetRelativePath(dataPath, file).Replace('\\', '/');
                index[relative] = file;
            }
        }

        return index;
    }

    /// <summary>
    /// 真的是模型嗎？<c>Data/Skill/skill.bmd</c> 與 <c>Data/Item/item.bmd</c> 用的是同一個副檔名，
    /// 但它們是<b>定義表</b>不是模型 —— 丟給 <c>BMDReader</c> 只會得到
    /// 「Invalid file type. Expected BMD and Received ???」。
    /// </summary>
    /// <remarks>
    /// 用魔數而不是檔名黑名單：<c>BMDReader</c> 也是先讀前三個位元組再決定要不要解密，
    /// 所以就算是加密的 v12/v15，<c>"BMD"</c> 這三個字仍然是明文。
    /// </remarks>
    private static bool IsModel(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> magic = stackalloc byte[3];

            return stream.ReadAtLeast(magic, 3, throwOnEndOfStream: false) == 3
                && magic[0] == (byte)'B' && magic[1] == (byte)'M' && magic[2] == (byte)'D';
        }
        catch
        {
            return false;
        }
    }

    private static string? Resolve(Dictionary<string, string> files, string relative)
        => files.TryGetValue(relative.Replace('\\', '/'), out var full) ? full : null;

    /// <summary>
    /// <c>Client.Main</c> 裡所有的怪物與 NPC 類別，<c>[NpcInfo]</c> 有沒有掛都算。
    /// </summary>
    /// <remarks>
    /// <b>不能只收有 <c>[NpcInfo]</c> 的。</b>399 個怪物類別裡只有 137 個掛了那個屬性；
    /// 其餘 262 個（<c>Archer</c>、<c>Balgass1</c>、<c>BloodyOrc</c>…）照樣指定了模型路徑，
    /// 只是沒有伺服器編號。漏掉它們的話，那 262 個模型會被誤判成「沒人引用的孤兒」——
    /// 而「這個模型還有沒有人在用」正是替換素材前最需要問對的問題。
    ///
    /// 不走 <c>NpcDatabase</c>：它只收有屬性的，而且遇到型別載入失敗會整個炸掉；
    /// 這裡要能在殘缺的組件上仍列出其餘的。
    /// </remarks>
    private IEnumerable<(Type Type, NpcInfoAttribute? Attribute)> DiscoverEntityTypes()
    {
        Type[] types;

        try
        {
            types = typeof(Client.Main.MuGame).Assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            Warnings.Add($"Client.Main 有 {ex.LoaderExceptions.Length} 個型別載入失敗，目錄可能不完整");
            types = ex.Types.Where(t => t is not null).Cast<Type>().ToArray();
        }

        foreach (var type in types)
        {
            // async 方法會被編譯成巢狀的狀態機型別（<Load>d__1），它們與外層類別
            // 同命名空間，掃 IL 時當然也「看得到」同一組模型路徑 ——
            // 不排掉的話 399 個怪物類別會膨脹成 789 筆重複。
            if (type.IsAbstract || type.IsNested || type.Name.StartsWith('<'))
                continue;

            var attribute = type.GetCustomAttribute<NpcInfoAttribute>();
            bool inEntityNamespace = type.Namespace is string ns
                && (ns.EndsWith(".Monsters", StringComparison.Ordinal)
                 || ns.EndsWith(".NPCS", StringComparison.Ordinal));

            if (attribute is not null || inEntityNamespace)
                yield return (type, attribute);
        }
    }

    private static EntityKind KindOf(Type type)
    {
        string ns = type.Namespace ?? string.Empty;

        if (ns.EndsWith(".Monsters", StringComparison.Ordinal))
            return EntityKind.Monster;
        if (ns.EndsWith(".NPCS", StringComparison.Ordinal))
            return EntityKind.Npc;

        return EntityKind.Npc;
    }

    private static bool MatchesKind(string path, EntityKind kind) => kind switch
    {
        EntityKind.Monster => path.StartsWith("Monster/", StringComparison.OrdinalIgnoreCase),
        EntityKind.Npc => path.StartsWith("NPC/", StringComparison.OrdinalIgnoreCase),
        EntityKind.Player => path.StartsWith("Player/", StringComparison.OrdinalIgnoreCase),
        EntityKind.SkillModel => path.StartsWith("Skill/", StringComparison.OrdinalIgnoreCase),
        EntityKind.Item => path.StartsWith("Item/", StringComparison.OrdinalIgnoreCase),
        EntityKind.Effect => path.StartsWith("Effect/", StringComparison.OrdinalIgnoreCase),
        _ => false,
    };
}

public static class EntityKindNames
{
    private static readonly Dictionary<EntityKind, string> Names = new()
    {
        [EntityKind.Monster] = "怪物",
        [EntityKind.Npc] = "NPC",
        [EntityKind.Player] = "角色",
        [EntityKind.Pet] = "寵物",
        [EntityKind.SkillModel] = "技能模型",
        [EntityKind.Item] = "道具",
        [EntityKind.Effect] = "特效",
        [EntityKind.Library] = "資源庫",
    };

    public static string Of(EntityKind kind) => Names.GetValueOrDefault(kind, kind.ToString());

    public static EntityKind[] All { get; } = Enum.GetValues<EntityKind>();
}
