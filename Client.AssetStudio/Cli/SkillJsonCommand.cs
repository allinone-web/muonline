using System.Text.Json;
using Client.AssetStudio.Catalog;

namespace Client.AssetStudio.Cli;

/// <summary>
/// 把 399 個技能連同<b>名稱與全部屬性</b>倒成 JSON。
/// </summary>
/// <remarks>
/// <b>為什麼需要這一支。</b>綜合索引原本的魔法資料是從 <c>tools/mu magic</c> 來的，
/// 而那支只認得「有音效或有視覺特效」的技能 —— 90 個。剩下的 300 多個在索引裡
/// 根本不存在，存在的那 76 個也只叫「技能 1」「技能 2」，沒有名字。
///
/// 但名字一直都在：客戶端內嵌的 <c>skill_eng.bmd</c>（1024 筆、88 byte 一筆、
/// 每個 byte 與 <c>FC CF AB</c> 循環 XOR）早就被 <see cref="SkillCatalog"/> 解開了，
/// 連耗魔、射程、需求、圖示編號都有。**缺的只是把它倒出來。**
///
/// 輸出是決定性的：同樣的 <c>skill_eng.bmd</c> 一定得到同樣的 JSON。
/// </remarks>
public static class SkillJsonCommand
{
    public static int Run(SkillCatalog skills, string outputPath)
    {
        if (skills.Entries.Length == 0)
        {
            Console.Error.WriteLine($"技能表是空的：{skills.Error ?? "沒有載入"}");
            return 2;
        }

        var entries = skills.Entries.OrderBy(e => e.Number).Select(entry =>
        {
            var definition = entry.Definition;

            return new
            {
                number = entry.Number,
                name = entry.Name,
                type = entry.Type.ToString(),
                animation = entry.Animation,
                baseSkill = entry.BaseSkill,
                isMaster = entry.IsMaster,
                visualEffectClass = entry.VisualEffectClass,
                sound = entry.Sound,

                // skill_eng.bmd 的屬性。伺服器那一側是另一個真相，這裡只報客戶端看到的。
                requiredLevel = definition.RequiredLevel,
                damage = definition.Damage,
                manaCost = definition.ManaCost,
                abilityGaugeCost = definition.AbilityGaugeCost,
                distance = definition.Distance,
                delay = definition.Delay,
                requiredEnergy = definition.RequiredEnergy,
                requiredLeadership = definition.RequiredLeadership,
                requiredStrength = definition.RequiredStrength,
                requiredDexterity = definition.RequiredDexterity,
                masteryType = definition.MasteryType,
                skillRank = definition.SkillRank,
                magicIcon = definition.MagicIcon,
                isDamage = definition.IsDamage,
                itemSkill = definition.ItemSkill,
                killCount = definition.KillCount,

                // 哪些職業能用。索引 = PlayerClass 的基礎職業編號。
                // ★ 一定要轉成 int[]：System.Text.Json 會把 byte[] 寫成 base64
                //   （"AQAAAQAAAA=="），下游看不出那是七個職業旗標。
                requireClass = definition.RequireClass.Select(b => (int)b).ToArray(),
                requireDutyClass = definition.RequireDutyClass.Select(b => (int)b).ToArray(),
            };
        }).ToArray();

        var payload = new
        {
            schema = "mu-skill-catalog/1",
            source = skills.Source,
            說明 = "客戶端 skill_eng.bmd 解出來的全部技能。"
                 + "visualEffectClass 為 null 只代表沒註冊視覺特效 —— "
                 + "很多技能本來就只有動作與音效，不是缺資料。",
            counts = new
            {
                total = entries.Length,
                withVisualEffect = entries.Count(e => e.visualEffectClass is not null),
                withSound = entries.Count(e => e.sound is not null),
                withAnimation = entries.Count(e => e.animation >= 0),
                master = entries.Count(e => e.isMaster),
            },
            entries,
        };

        string directory = Path.GetDirectoryName(Path.GetFullPath(outputPath)) ?? ".";
        Directory.CreateDirectory(directory);

        using (var stream = File.Create(outputPath))
        {
            JsonSerializer.Serialize(stream, payload, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            });
        }

        Console.WriteLine();
        Console.WriteLine($"已寫出 {entries.Length} 個技能 → {outputPath}");
        Console.WriteLine($"  有視覺特效 {payload.counts.withVisualEffect}"
                        + $"　有音效 {payload.counts.withSound}"
                        + $"　有專屬動作 {payload.counts.withAnimation}"
                        + $"　大師技 {payload.counts.master}");
        return 0;
    }
}
