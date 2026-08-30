namespace Client.AssetStudio.Catalog;

/// <summary>
/// 把 <c>Data/Player/</c> 底下散落的部位檔，組成「一個看得到的職業角色」。
/// </summary>
/// <remarks>
/// <b>為什麼需要這個。</b>目錄裡本來就有 <c>ArmorClass14.bmd</c> 這些檔案，
/// 但一個一個點開看到的是「一件浮在空中的上衣」——
/// 因為角色的幾何分在五個部位檔裡，而<b>骨架與 380 個動作全部在
/// <c>Player/Player.bmd</c></b>（它自己 0 網格）。
///
/// 所以「看一個職業長什麼樣、動起來如何」需要把六個檔案合起來看，
/// 而那正是遊戲執行期做的事。這個類別就是把那件事變成目錄裡的一筆。
///
/// <b>動作是全職業共用的。</b>380 個動作在 <c>Player.bmd</c> 裡，
/// 每個部位檔自己只有 1 個動作（等於沒有）。
/// 換句話說<b>新職業帶來的是更細的模型，不是更好的動作</b> ——
/// 職業之間的動作差異來自「拿什麼武器」（<c>PlayerActionMapper</c> 依武器類型選動作），
/// 不是來自身體。挑職業時這一點要先知道，否則會期待錯東西。
/// </remarks>
public static class PlayerClassCatalog
{
    /// <summary>五個部位的檔名前綴。順序決定合併順序，與遊戲一致。</summary>
    private static readonly string[] Slots = ["Helm", "Armor", "Pant", "Glove", "Boot"];

    private const string Skeleton = "Player/Player.bmd";

    /// <summary>
    /// 職業編號 → 名稱。取自 <c>Client.Main.Models.PlayerClass</c>。
    /// </summary>
    /// <remarks>
    /// 刻意在這裡重寫一份而不是引用那個列舉：這一層是引擎中立的目錄，
    /// 而那個列舉在 <c>Client.Main</c>（相依 MonoGame）。
    /// 一份 15 筆的對照表比一個引擎相依划算。
    /// </remarks>
    private static readonly (int Number, string Name, bool IsNew)[] BaseClasses =
    [
        (1,  "法師 Dark Wizard",          false),
        (2,  "戰士 Dark Knight",          false),
        (3,  "弓箭手 Fairy Elf",          false),
        (4,  "魔劍士 Magic Gladiator",    false),
        (5,  "魔王 Dark Lord",            false),
        (6,  "召喚術士 Summoner",         false),
        (7,  "鬥士 Rage Fighter",         false),
        (8,  "槍騎士 Glow Lancer",        true),
        (9,  "符文法師 Rune Mage",        true),
        (10, "斬殺者 Slayer",             true),
        (11, "火槍手 Gun Crusher",        true),
        (12, "白巫師 White Wizard",       true),
        (13, "魔導士 Mage",               true),
        (14, "幻影騎士 Illusion Knight",  true),
        (15, "煉金術士 Alchemist",        true),
    ];

    /// <summary>轉職階段的字尾。基礎階是 0。</summary>
    private static readonly (int Offset, string Suffix)[] Tiers =
    [
        (0,   ""),
        (200, "（二轉）"),
        (300, "（三轉）"),
        (400, "（四轉）"),
    ];

    /// <summary>
    /// 產生職業角色的目錄項目。
    /// </summary>
    /// <param name="resolve">
    /// 相對路徑 → 絕對路徑，找不到回 null。由 <see cref="EntityCatalog"/> 傳它自己的索引進來，
    /// 這樣大小寫與副檔名的處理只有一份。
    /// </param>
    public static IEnumerable<EntityEntry> Build(Func<string, string?> resolve)
    {
        string? skeleton = resolve(Skeleton);
        if (skeleton is null)
            yield break;

        foreach (var (number, name, isNew) in BaseClasses)
        {
            foreach (var (offset, suffix) in Tiers)
            {
                int id = offset + number;

                var parts = Slots
                    .Select(slot => $"Player/{slot}Class{id:D2}.bmd")
                    .Where(path => resolve(path) is not null)
                    .ToArray();

                // 五個部位缺任何一個就不列 —— 半個角色比沒有角色更難判斷。
                if (parts.Length != Slots.Length)
                    continue;

                yield return new EntityEntry
                {
                    Kind = EntityKind.Player,
                    Name = $"{name}{suffix}",
                    Number = id,
                    ModelPath = Skeleton,
                    FullPath = skeleton,
                    BodyParts = parts,
                    IsReferenced = true,
                    Group = isNew ? "職業角色（新職業，S20 之後）" : "職業角色（舊職業）",
                    Detail = $"Class{id:D2}",
                };
            }
        }
    }
}
