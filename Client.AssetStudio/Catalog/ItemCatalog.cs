using Client.Main.Controls.UI.Game.Inventory;
using Client.Main.Core.Utilities;

namespace Client.AssetStudio.Catalog;

/// <summary>一個道具模型對應到的道具定義。</summary>
public sealed record ItemBinding(byte Group, int Number, string Name, ItemDefinition Definition)
{
    public string GroupName => ItemCatalog.GroupName(Group);
}

/// <summary>
/// <c>Data/Item/</c> 底下 2715 個模型的語意分類。
/// </summary>
/// <remarks>
/// <b>為什麼需要這一層：</b>道具是資源包裡最大的一堆，而檔名（<c>Sword01.bmd</c>、
/// <c>Wing01.bmd</c>、<c>bufflitem01.bmd</c>）幾乎不帶語意 ——
/// 「這是劍還是斧」「哪個職業能用」「幾級掉」全部不在檔名裡。
/// 要換掉一整套美術資源時，「把所有的弓列出來」是最基本的問題，
/// 沒有這一層就只能靠猜檔名。
///
/// 真相在 <c>item.bmd</c>：每一筆道具都帶 <c>szModelFolder</c> + <c>szModelName</c>
/// （組出來就是 <c>Item/Sword01.bmd</c>）、<c>ItemSubGroup</c>（0=劍、1=斧…）、
/// 名稱、需求與職業限制。<c>Client.Main.ItemDatabase</c> 已經把它讀成
/// <c>(Group, Id) → ItemDefinition</c>，這裡只是反過來建「模型路徑 → 道具」的索引。
///
/// 不自己解 <c>item.bmd</c>：<c>ItemDatabase</c> 讀的是<b>內嵌的</b>那一份，
/// 與遊戲執行期用的是同一份。<c>Data/Item/item.bmd</c> 是另一個版本，
/// 與 <c>Data/Local/skill.bmd</c> 同樣的理由不要碰（見 <see cref="SkillCatalog"/>）。
/// </remarks>
public sealed class ItemCatalog
{
    /// <summary>MU 的道具群組。名稱對照 <c>ItemDatabase.GetWeapons/GetArmors/GetWings/GetPets</c> 的分組。</summary>
    private static readonly Dictionary<byte, string> GroupNames = new()
    {
        [0] = "劍",
        [1] = "斧",
        [2] = "釘錘 / 短杖",
        [3] = "矛 / 長柄",
        [4] = "弓 / 弩",
        [5] = "法杖",
        [6] = "盾",
        [7] = "頭盔",
        [8] = "盔甲",
        [9] = "褲子",
        [10] = "手套",
        [11] = "鞋子",
        [12] = "翅膀 / 輔助",
        [13] = "寵物 / 飾品",
        [14] = "消耗品 / 寶石",
        [15] = "卷軸 / 其他",
    };

    /// <summary>模型相對路徑（正斜線）→ 道具。一個模型可能被多筆道具共用。</summary>
    private readonly Dictionary<string, List<ItemBinding>> _byModel = new(StringComparer.OrdinalIgnoreCase);

    public string? Error { get; private set; }

    public int BoundItems { get; private set; }

    public static string GroupName(byte group) => GroupNames.GetValueOrDefault(group, $"群組 {group}");

    public static IEnumerable<string> AllGroupNames => GroupNames.Values;

    public void Build()
    {
        _byModel.Clear();
        BoundItems = 0;
        Error = null;

        try
        {
            foreach (byte group in GroupNames.Keys)
            {
                foreach (var definition in ItemDatabase.GetItemDefinitions(group))
                {
                    if (definition is null || string.IsNullOrWhiteSpace(definition.TexturePath))
                        continue;

                    string path = Normalize(definition.TexturePath);

                    if (!_byModel.TryGetValue(path, out var list))
                        _byModel[path] = list = [];

                    list.Add(new ItemBinding(group, definition.Id, definition.Name ?? string.Empty, definition));
                    BoundItems++;
                }
            }
        }
        catch (Exception ex)
        {
            Error = $"道具定義載入失敗：{ex.Message}";
        }
    }

    public IReadOnlyList<ItemBinding> For(string modelPath)
        => _byModel.TryGetValue(Normalize(modelPath), out var list) ? list : [];

    /// <summary>
    /// 把 <c>item.bmd</c> 的路徑正規化成目錄用的形式。
    /// </summary>
    /// <remarks>
    /// <c>szModelFolder</c> 本身就以反斜線結尾（<c>"Item\\"</c>），
    /// <c>ItemDatabase</c> 再用 <c>Path.Combine</c> 接上檔名，結果是 <c>Item//sword01.bmd</c>
    /// —— <b>兩條斜線</b>。不處理的話 3347 筆道具一個也對不上，
    /// 而且症狀是「分類全部是空的」，看起來像資料沒讀到。
    /// 大小寫也不一致（<c>sword01</c> vs <c>Sword01</c>），所以字典用 OrdinalIgnoreCase。
    /// </remarks>
    private static string Normalize(string path)
    {
        var normalized = path.Replace('\\', '/');

        while (normalized.Contains("//", StringComparison.Ordinal))
            normalized = normalized.Replace("//", "/", StringComparison.Ordinal);

        return normalized.Trim('/');
    }

    /// <summary>這個模型的群組名稱。同一個模型被多個群組共用時取第一個。</summary>
    public string? GroupOf(string modelPath)
    {
        var bindings = For(modelPath);
        return bindings.Count == 0 ? null : bindings[0].GroupName;
    }

    /// <summary>清單裡要顯示的名字：道具名稱比檔名有用得多。</summary>
    public string? NameOf(string modelPath)
    {
        var bindings = For(modelPath);

        if (bindings.Count == 0)
            return null;

        // 同一個模型被多筆道具共用是常態（同一把劍的不同等級）。
        // 顯示第一個名字加上數量，比顯示一串名字好讀。
        return bindings.Count == 1
            ? bindings[0].Name
            : $"{bindings[0].Name}（共 {bindings.Count} 種）";
    }
}
