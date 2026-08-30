using Client.AssetStudio.Catalog;
using Client.Data.BMD;

namespace Client.AssetStudio.Cli;

/// <summary>
/// 15 個職業的身體精細度對照，命令列版。
/// </summary>
/// <remarks>
/// 「哪個職業的模型比較細」這個問題，用眼睛比 15 次很慢而且不準。
/// 這裡把五個部位的三角形加起來一次列出來，挑之前先看數字，
/// 再拿 <c>tools/mu classes &lt;名稱&gt;</c> 去 3D 裡確認造型。
///
/// <b>要先知道的一件事</b>：動作是全職業共用的（380 個都在
/// <c>Player/Player.bmd</c> 裡），所以換職業身體換到的是「模型比較細」，
/// <b>不是「動作比較好」</b>。
/// </remarks>
public static class ClassReport
{
    public static int Print(EntityCatalog catalog, string dataPath)
    {
        var reader = new BMDReader();

        var classes = catalog.Entries
            .Where(e => e.Kind == EntityKind.Player && e.Group.StartsWith("職業角色", StringComparison.Ordinal))
            .Where(e => e.Number < 200)                       // 只看基礎階，轉職階段外觀通常一起換
            .OrderBy(e => e.Number)
            .ToArray();

        if (classes.Length == 0)
        {
            Console.Error.WriteLine("目錄裡沒有職業角色");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine($"{"編號",-4} {"職業",-28} {"三角形",8}  {"部位網格",-18} 新舊");
        Console.WriteLine(new string('─', 78));

        foreach (var entry in classes)
        {
            int triangles = 0;
            var meshes = new List<int>();

            foreach (var part in entry.BodyParts)
            {
                string full = Path.Combine(dataPath, part);
                if (!File.Exists(full))
                    continue;

                try
                {
                    var bmd = reader.Load(full).GetAwaiter().GetResult();
                    meshes.Add(bmd.Meshes.Length);
                    triangles += bmd.Meshes.Sum(m => m.Triangles.Length);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"  {part} 讀不開：{ex.Message}");
                }
            }

            bool isNew = entry.Group.Contains("新職業", StringComparison.Ordinal);

            Console.WriteLine($"{entry.Number,-4} {entry.Name,-28} {triangles,8:N0}  "
                            + $"{string.Join("／", meshes),-18} {(isNew ? "新" : "舊")}");
        }

        Console.WriteLine();
        Console.WriteLine("動作全職業共用（380 個都在 Player/Player.bmd）——");
        Console.WriteLine("換身體換到的是模型比較細，不是動作比較好。");
        Console.WriteLine();
        Console.WriteLine("看造型： tools/mu classes <名稱或編號>");

        return 0;
    }
}
