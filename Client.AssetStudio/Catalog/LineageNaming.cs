namespace Client.AssetStudio.Catalog;

/// <summary>
/// 從天堂（梦想与征程）資產的<b>包名前綴</b>判斷它是什麼。
/// </summary>
/// <remarks>
/// 那批 1,543 個包全部用同一套命名，而且前綴本身就帶了類型：
/// <code>
/// SK_Mon_*     914   怪物
/// SK_Npc_*     356   NPC（大小寫混用，還有 SK_NPC_ 116 個、Sk_Npc_ 幾個）
/// SK_Mdoll_*   129   怪物（Mdoll = monster doll）
/// SK_PC_*       16   玩家角色
/// </code>
///
/// <b>不加這一層的話 1,514 個全部當怪物收</b>，裡面混著 NPC、寶箱、裝飾物 ——
/// 「找一隻怪來替換」時要從 1,514 個裡面自己挑掉一半。
///
/// <b>大小寫一定要不敏感。</b>同一種東西在這批資料裡有 <c>SK_Npc_</c>、
/// <c>SK_NPC_</c>、<c>Sk_Npc_</c> 三種寫法 —— 區分大小寫的比對會漏掉兩種。
///
/// Python 端有一份對照實作（<c>tools/assets/asset_index.py</c> 的
/// <c>classify_lineage_pack</c>），兩邊要一起改。分岔的話
/// 瀏覽器與離線索引會對同一個包給出不同分類。
/// </remarks>
public static class LineageNaming
{
    /// <summary>前綴 → 類型。順序有意義：先比對長的前綴。</summary>
    private static readonly (string Prefix, EntityKind Kind, string Group)[] Rules =
    [
        ("SK_Mon_", EntityKind.Monster, "天堂怪物"),
        ("SK_Mdoll_", EntityKind.Monster, "天堂怪物"),
        ("SK_PC_", EntityKind.Player, "天堂玩家角色"),
        ("SK_Npc_", EntityKind.Npc, "天堂 NPC"),
        ("ST_Wpn_", EntityKind.Item, "天堂武器"),
    ];

    /// <summary>NPC 裡面其實不是角色的那幾種。判斷用的是名字中段，不是前綴。</summary>
    private static readonly (string Fragment, string Group)[] NpcRefinements =
    [
        ("_Box", "天堂容器"),
        ("_Deco_", "天堂裝飾物"),
        ("_Evt_", "天堂活動物件"),
    ];

    /// <summary>
    /// 判斷一個包名。認不出來時回傳 <paramref name="fallback"/>，
    /// 不猜 —— 猜錯會讓 NPC 混進怪物堆裡，而那正是要修掉的問題。
    /// </summary>
    public static (EntityKind Kind, string Group) Classify(string packName, EntityKind fallback)
    {
        foreach (var (prefix, kind, group) in Rules)
        {
            if (!packName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            if (kind != EntityKind.Npc)
                return (kind, group);

            foreach (var (fragment, refined) in NpcRefinements)
            {
                if (packName.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                    return (kind, refined);
            }

            return (kind, group);
        }

        return (fallback, "資源庫（匯入）");
    }

    /// <summary>認不認得這個包名。用來報告「有幾個沒分類到」。</summary>
    public static bool IsKnown(string packName) =>
        Rules.Any(rule => packName.StartsWith(rule.Prefix, StringComparison.OrdinalIgnoreCase));
}
