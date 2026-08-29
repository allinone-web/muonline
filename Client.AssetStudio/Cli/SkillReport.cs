using Client.AssetStudio.Catalog;
using Client.AssetStudio.Server;

namespace Client.AssetStudio.Cli;

/// <summary>
/// 技能盤點，命令列版。
/// </summary>
/// <remarks>
/// 這份報告要回答的是三個「靜默失敗」的問題（HANDOFF 第 5 節）：
/// <list type="number">
/// <item><b>型別對不對</b>——Area / Target / Self 走三種完全不同的封包，
/// 送錯型別 → 收到的是另一種封包 → 特效註冊表根本不會被呼叫。不崩潰、不報錯、就是沒特效。</item>
/// <item><b>有沒有專屬動作</b>——查不到就退回 <c>PlayerSkillHand1/2</c>，
/// 戰士拿著劍在原地畫圈，玩家會以為技能沒放出去。</item>
/// <item><b>客戶端與伺服器對不對得上</b>——技能編號在兩邊各有一份定義。</item>
/// </list>
/// </remarks>
public static class SkillReport
{
    public static async Task<int> PrintAsync(SkillCatalog catalog, string? filter, string? connectionString, bool includeServer)
    {
        if (catalog.Error is string error)
        {
            Console.Error.WriteLine(error);
            return 2;
        }

        Dictionary<short, SkillRow> server = [];

        if (includeServer)
        {
            var repository = new OpenMuRepository();

            if (!string.IsNullOrWhiteSpace(connectionString))
                repository.ConnectionString = connectionString;

            try
            {
                server = await repository.LoadSkillsAsync();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"連不上資料庫（只印客戶端這一側）：{ex.Message}");
            }
        }

        var entries = catalog.Entries
            .Where(s => filter is null || s.Search.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Console.WriteLine();
        Console.WriteLine($"技能定義來源：{catalog.Source}");
        Console.WriteLine($"客戶端 {catalog.Entries.Length} 個，伺服器 {server.Count} 個");

        int noAnimation = catalog.Entries.Count(s => !s.IsMaster && s.Animation < 0);
        int noEffect = catalog.Entries.Count(s => s.VisualEffectClass is null);
        int notInServer = catalog.Entries.Count(s => !server.ContainsKey((short)s.Number));

        Console.WriteLine($"沒有專屬動作　　{noAnimation}　（退回 PlayerSkillHand1/2 的施法動作。法系技能本來就該是這樣，戰士系的才是問題）");
        Console.WriteLine($"沒有註冊視覺效果{noEffect}　（不一定是問題，很多技能本來就只有動作與音效）");

        if (server.Count > 0)
            Console.WriteLine($"伺服器沒有　　　{notInServer}　（學不到，因為技能是伺服器發的）");

        Console.WriteLine();
        Console.WriteLine("編號  名稱                      型別    動作   視覺效果                  射程(端/服)  傷害(端/服)");

        foreach (var skill in entries)
        {
            server.TryGetValue((short)skill.Number, out var row);

            string animation = skill.Animation >= 0 ? skill.Animation.ToString() : "－";
            string effect = skill.VisualEffectClass ?? "－";
            string range = $"{skill.Definition.Distance}/{(row is null ? "－" : row.Range.ToString())}";
            string damage = $"{skill.Definition.Damage}/{(row is null ? "－" : row.AttackDamage.ToString())}";

            Console.WriteLine(
                $"{skill.Number,-5} {Trim(skill.Name, 24),-24} {skill.Type,-7} {animation,-6} "
              + $"{Trim(effect, 24),-24} {range,-12} {damage}");
        }

        return 0;
    }

    private static string Trim(string value, int length)
        => value.Length <= length ? value : value[..(length - 1)] + "…";
}
