using System.Text.RegularExpressions;

namespace Client.AssetStudio.Catalog;

/// <summary>
/// <c>Data/Player/</c> 底下 612 個模型的分類。
/// </summary>
/// <remarks>
/// 這個資料夾與道具不同，<b>沒有任何資料檔描述它</b>：
/// 角色的可見外觀是遊戲執行期用「部位 + 編號」組路徑組出來的
/// （<c>Player/ArmorMale05.bmd</c>），編號來自玩家身上的裝備。
/// 好消息是檔名的規則非常一致，所以直接從檔名分類就夠準：
/// <code>
/// [前綴_]{部位}{變體}{編號}.bmd
///   部位  MaskHelm | Helm | Armor | Pant | Glove | Boot | Wing
///   變體  Class（職業預設的身體）| Male | Female | Elf | ElfC | Monk
///   前綴  HDK_ / CW_ / t_（改版加的職業或測試用）
/// </code>
/// <b>Class 與 Male 的差別很重要</b>：<c>ArmorClass02.bmd</c> 是「戰士沒穿裝備時的身體」，
/// <c>ArmorMale05.bmd</c> 是「第 5 號盔甲」。換素材時這兩類的工作量完全不同 ——
/// Class 只有 56 個而且一定要有，Male 有 43 個而且可以逐件換。
/// </remarks>
public static class PlayerPartClassifier
{
    private static readonly (string Token, string Name)[] Slots =
    [
        ("MaskHelm", "面具頭盔"),
        ("Helm", "頭盔"),
        ("Armor", "盔甲"),
        ("Pant", "褲子"),
        ("Glove", "手套"),
        ("Boot", "鞋子"),
        ("Wing", "翅膀"),
    ];

    private static readonly (string Token, string Name)[] Variants =
    [
        ("Class", "職業預設身體"),
        ("Female", "女性裝備"),
        ("Male", "裝備"),
        ("ElfC", "精靈（C）"),
        ("Elf", "精靈"),
        ("Monk", "武僧"),
    ];

    private static readonly Regex Trailing = new(@"(\d+)([A-Za-z_]*)$", RegexOptions.Compiled);

    public sealed record Classification(string Group, string Detail);

    public static Classification Classify(string modelPath)
    {
        string name = Path.GetFileNameWithoutExtension(modelPath);

        // 少數幾個不照規則的，直接點名。
        switch (name.ToLowerInvariant())
        {
            case "player": return new Classification("角色骨架", "所有角色動作的來源（約 380 個動作）");
            case "angel": return new Classification("其他", "守護天使");
            case "shadow": return new Classification("其他", "影子");
            case "helper": return new Classification("其他", "輔助物件");
        }

        foreach (var (token, slotName) in Slots)
        {
            int index = name.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                continue;

            string prefix = name[..index].TrimEnd('_');
            string rest = name[(index + token.Length)..];

            string variant = Variants
                .FirstOrDefault(v => rest.StartsWith(v.Token, StringComparison.OrdinalIgnoreCase))
                .Name ?? string.Empty;

            var number = Trailing.Match(rest);

            var detail = string.Join("　", new[]
            {
                variant,
                number.Success ? $"編號 {number.Groups[1].Value}" : null,
                string.IsNullOrEmpty(prefix) ? null : $"前綴 {prefix}",
                rest.Contains("inven", StringComparison.OrdinalIgnoreCase) ? "背包用" : null,
                rest.Contains("Test", StringComparison.OrdinalIgnoreCase) ? "測試用" : null,
            }.Where(part => !string.IsNullOrEmpty(part)));

            return new Classification(slotName, detail);
        }

        return new Classification("未分類", string.Empty);
    }

    public static IEnumerable<string> AllGroupNames =>
        Slots.Select(s => s.Name).Concat(["角色骨架", "其他", "未分類"]);
}
