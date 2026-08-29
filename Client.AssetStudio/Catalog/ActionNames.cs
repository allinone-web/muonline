using Client.Main.Models;

namespace Client.AssetStudio.Catalog;

/// <summary>
/// 動作編號 → 名稱。<c>.bmd</c> 只存「第幾個動作」，語意在程式碼的列舉裡。
/// </summary>
/// <remarks>
/// 兩套列舉互不相容，用錯會讓整份動作清單的名稱全部錯開：
/// 怪物是 <see cref="MonsterActionType"/>（11 個），角色與 NPC 是
/// <see cref="PlayerAction"/>（約 380 個）。同一個索引 3 在怪物是「攻擊一」，
/// 在角色是「持劍待機」。
/// </remarks>
public static class ActionNames
{
    private static readonly Dictionary<int, string> MonsterNames = new()
    {
        [0] = "待機 1",
        [1] = "待機 2",
        [2] = "走",
        [3] = "攻擊 1",
        [4] = "攻擊 2",
        [5] = "受擊",
        [6] = "死亡",
        [7] = "出場",
        [8] = "攻擊 3",
        [9] = "攻擊 4",
        [10] = "跑",
    };

    public static string Of(EntityKind kind, int index) => kind switch
    {
        EntityKind.Monster => MonsterNames.TryGetValue(index, out var name)
            ? $"{index}　{name}"
            : $"{index}　動作 {index}",

        EntityKind.Npc or EntityKind.Player => Enum.IsDefined(typeof(PlayerAction), index)
            ? $"{index}　{Humanize(((PlayerAction)index).ToString())}"
            : $"{index}　動作 {index}",

        _ => $"{index}　動作 {index}",
    };

    /// <summary>怪物只有 11 個具名動作，超過的是資源自帶的額外動作，不是錯誤。</summary>
    public static bool IsNamed(EntityKind kind, int index) => kind switch
    {
        EntityKind.Monster => MonsterNames.ContainsKey(index),
        EntityKind.Npc or EntityKind.Player => Enum.IsDefined(typeof(PlayerAction), index),
        _ => false,
    };

    /// <summary><c>PlayerAttackTwoHandSword1</c> → <c>Player Attack Two Hand Sword 1</c>。</summary>
    private static string Humanize(string name)
    {
        var builder = new System.Text.StringBuilder(name.Length + 8);

        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];

            if (i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1]))
                builder.Append(' ');
            else if (i > 0 && char.IsDigit(c) && !char.IsDigit(name[i - 1]))
                builder.Append(' ');

            builder.Append(c);
        }

        return builder.ToString();
    }
}
