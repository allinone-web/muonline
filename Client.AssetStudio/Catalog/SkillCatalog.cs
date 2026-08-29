using System.Reflection;
using Client.Data.BMD;
using Client.Main.Core.Utilities;

namespace Client.AssetStudio.Catalog;

/// <summary>一個技能在客戶端這一側的全部資訊。伺服器那一側由 <c>OpenMuRepository</c> 提供。</summary>
public sealed record SkillEntry
{
    public required int Number { get; init; }

    public required string Name { get; init; }

    public required SkillBMD Definition { get; init; }

    /// <summary>Area / Target / Self。送錯型別的封包會靜默失敗（見 HANDOFF 第 5 節）。</summary>
    public SkillType Type { get; init; }

    /// <summary>對應的角色動作編號，-1 代表沒有專屬動作。</summary>
    public int Animation { get; init; }

    /// <summary>編號 300 以上的大師技換算成的基礎技；本身就是基礎技時等於 <see cref="Number"/>。</summary>
    public int BaseSkill { get; init; }

    public bool IsMaster => Number != BaseSkill;

    /// <summary><c>[SkillVisualEffect(id)]</c> 註冊的視覺效果類別，沒有註冊就是 null。</summary>
    public string? VisualEffectClass { get; init; }

    public string? Sound { get; init; }

    public string Search => $"{Number} {Name} {VisualEffectClass}";
}

/// <summary>
/// 技能清單。三份資料併在一起看，因為它們經常不一致。
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><c>skill.bmd</c> —— 客戶端的技能屬性表（名稱、圖示、耗魔、射程、需求）。</item>
/// <item><c>SkillDefinitions</c> —— <b>手工維護</b>的型別／動作／音效對照表。
/// 加新技能時要對照 OpenMU 的 <c>SkillsInitializer</c>，漏了會沒有特效而且不會報錯。</item>
/// <item><c>[SkillVisualEffect]</c> —— 視覺效果的註冊表。</item>
/// </list>
/// 資料庫裡的傷害與冷卻是<b>另一個真相</b>，由伺服器面板並排顯示 ——
/// 客戶端的 <c>skill_eng.bmd</c> 需求值與 OpenMU 對不上是已知事實
/// （能量有 64 個技能對不上，最誇張的算出要 138245 點而伺服器只要 118）。
/// </remarks>
public sealed class SkillCatalog
{
    public SkillEntry[] Entries { get; private set; } = [];

    public string Source { get; private set; } = string.Empty;

    public string? Error { get; private set; }

    public async Task BuildAsync(string dataPath)
    {
        Error = null;

        Dictionary<int, SkillBMD> definitions;

        // 用 Client.Main 內嵌的 skill_eng.bmd —— 遊戲執行期讀的就是這一份
        // （SkillDatabase.Initialize），所以工具顯示的必須是同一份。
        //
        // ⚠ 不要改用 Data/Local/skill.bmd：Season 20 的那個檔案佈局與
        // SkillBMDReader 預期的 88 byte／筆不同，硬解出來會得到 772 筆
        // 名稱是亂碼（W3dW3dW3e…）的「技能」，而且不會丟例外 —— 是靜默的錯誤資料。
        // 需要解 S20 的 skill.bmd 的話要先把那個格式逆向出來，那是另一件事。
        try
        {
            await SkillDatabase.Initialize();
            definitions = new Dictionary<int, SkillBMD>(SkillDatabase.GetAllSkills());
            Source = "Client.Main 內嵌的 skill_eng.bmd（遊戲實際讀的那一份）";
        }
        catch (Exception ex)
        {
            Error = $"技能定義載入失敗：{ex.Message}";
            Entries = [];
            return;
        }

        var effects = DiscoverVisualEffects();

        Entries = definitions
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value.Name))
            .OrderBy(pair => pair.Key)
            .Select(pair => new SkillEntry
            {
                Number = pair.Key,
                Name = pair.Value.Name,
                Definition = pair.Value,
                Type = SkillDefinitions.GetSkillType(pair.Key),
                Animation = SkillDefinitions.GetSkillAnimation(pair.Key),
                BaseSkill = SkillDefinitions.ResolveBaseSkill(pair.Key),
                VisualEffectClass = effects.GetValueOrDefault((ushort)pair.Key),
                Sound = SkillDefinitions.GetSkillSound(pair.Key),
            })
            .ToArray();
    }

    /// <summary>技能編號 → 視覺效果類別名稱。一個類別可以掛多個技能編號。</summary>
    private static Dictionary<ushort, string> DiscoverVisualEffects()
    {
        var map = new Dictionary<ushort, string>();

        Type[] types;
        try
        {
            types = typeof(Client.Main.MuGame).Assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types.Where(t => t is not null).Cast<Type>().ToArray();
        }

        foreach (var type in types)
        {
            foreach (var attribute in type.GetCustomAttributes<SkillVisualEffectAttribute>())
                map[attribute.SkillId] = type.Name;
        }

        return map;
    }
}
