namespace Client.AssetStudio.Cli;

using System.Text.Json;
using Client.AssetStudio.Catalog;
using Client.AssetStudio.Export;
using Client.AssetStudio.Import;
using Client.Data.BMD;

/// <summary>
/// BMD → glTF → BMD 這條管線的無視窗回歸測試。
/// </summary>
/// <remarks>
/// <c>--roundtrip</c> 是給人看的：挑一隻模型、印出誤差、自己判斷。
/// 這支是給 CI 看的：固定幾個案例、自己判斷、用離開碼講話。
///
/// 兩個案例分別擋兩種不同的壞法：
/// <list type="bullet">
/// <item><b>幾何往返</b>擋「數字對不上」—— 座標系、骨骼順序、頂點空間弄錯了，
///       症狀都是「模型看起來怪怪的」，眼睛判斷不了程度，只有點雲誤差說得準。</item>
/// <item><b>純骨架</b>擋「檔案根本打不開」。player.bmd 自己 0 網格、60 骨、380 個動作，
///       幾何全在 ArmorClass 那些部位檔裡。沒有部位可併的時候如果照樣寫出空的
///       <c>primitives</c>，glTF 規格不允許空陣列，Blender 與驗證器會整份拒絕 ——
///       骨架和 380 個動作一起賠掉，而匯出當下不會有任何錯誤訊息。
///       這個 bug 真的發生過，所以它有一個案例。</item>
/// </list>
/// 離開碼：0 全過、1 有案例失敗、2 沒東西可測（資源目錄不對）。
/// </remarks>
public static class AssetPipelineSelfTest
{
    /// <summary>幾何往返允許的相對誤差。匯出入都是 float32，不該有可見的漂移。</summary>
    private const float ErrorTolerance = 0.0001f;

    public static int Run(EntityCatalog catalog, string dataPath)
    {
        string work = Path.Combine(Path.GetTempPath(), "mu-asset-selftest");
        if (Directory.Exists(work))
            Directory.Delete(work, recursive: true);
        Directory.CreateDirectory(work);

        var cases = new List<(string Name, bool Passed, string Detail)>();

        try
        {
            if (GeometryRoundTrip(catalog, dataPath, work) is { } geometry)
                cases.Add(geometry);
            else
                return Missing("找不到有幾何的模型可測");

            if (SkeletonOnly(dataPath, work) is { } skeleton)
                cases.Add(skeleton);
            else
                return Missing("找不到 Player/player.bmd");
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch { }
        }

        Console.WriteLine();
        foreach (var (name, passed, detail) in cases)
            Console.WriteLine($"[{(passed ? " ok " : "FAIL")}] {name,-14} {detail}");

        bool allPassed = cases.All(c => c.Passed);
        Console.WriteLine();
        Console.WriteLine(allPassed ? "全部通過。          離開碼 0" : "有案例失敗。        離開碼 1");
        return allPassed ? 0 : 1;
    }

    /// <summary>匯出再匯回來，蒙皮後的點雲應該回到原處。</summary>
    private static (string, bool, string)? GeometryRoundTrip(EntityCatalog catalog, string dataPath, string work)
    {
        // 挑第一個「檔案在、而且自己就有網格」的資源，不寫死名字 ——
        // 不同版本的客戶端資源檔名不一樣，寫死會變成「換一份資源就紅」。
        var reader = new BMDReader();
        EntityEntry? picked = null;
        BMD? original = null;

        foreach (var entry in catalog.Entries.Where(e => e.FullPath is not null))
        {
            try
            {
                var model = reader.Load(entry.FullPath!).GetAwaiter().GetResult();
                if (model.Meshes is { Length: > 0 } && model.Bones is { Length: > 0 })
                {
                    picked = entry;
                    original = model;
                    break;
                }
            }
            catch
            {
                // 解不開的模型是 --verify 的事，不是這裡的事。
            }
        }

        if (picked is null || original is null)
            return null;

        string gltf = GltfExporter.Export(picked.FullPath!, Path.Combine(work, "geometry"),
            new GltfExporter.Options(ExportTextures: false, Kind: picked.Kind,
                                     BodyParts: picked.BodyParts, DataPath: dataPath)).GltfPath;

        var imported = GltfImporter.Import(gltf, new GltfImporter.Options(Scale: 1f, AutoScale: false));
        if (imported.Report.HasErrors)
            return ($"幾何往返", false, $"{picked.Name}　匯入失敗：{imported.Report.Issues[0].Title}");

        var parts = picked.BodyParts
            .Select(part => Path.Combine(dataPath, part))
            .Where(File.Exists)
            .Select(full => reader.Load(full).GetAwaiter().GetResult())
            .ToArray();

        var result = ModelComparer.Compare(original, parts, imported.Model);

        bool passed = result.Comparable
                   && result.BonesA == result.BonesB
                   && result.TrianglesA == result.TrianglesB
                   && result.RelativeError <= ErrorTolerance;

        return ("幾何往返", passed,
            $"{picked.Name}　骨 {result.BonesA}→{result.BonesB}、"
          + $"三角 {result.TrianglesA}→{result.TrianglesB}、相對誤差 {result.RelativeError:P3}");
    }

    /// <summary>沒有幾何的模型也要匯出成打得開的檔案，而且動作不能掉。</summary>
    private static (string, bool, string)? SkeletonOnly(string dataPath, string work)
    {
        string bmd = Path.Combine(dataPath, "Player", "player.bmd");
        if (!File.Exists(bmd))
            return null;

        // 刻意不給 BodyParts：這正是「主模型自己沒有幾何」的情境。
        var export = GltfExporter.Export(bmd, Path.Combine(work, "skeleton"),
            new GltfExporter.Options(ExportTextures: false, Kind: EntityKind.Player, DataPath: dataPath));

        var problems = new List<string>();

        // 規格：任何陣列都不能是空的。這是當初讓整份檔案作廢的那一條。
        using (var doc = JsonDocument.Parse(File.ReadAllText(export.GltfPath)))
        {
            foreach (string path in EmptyArrays(doc.RootElement, string.Empty))
                problems.Add($"空陣列 {path}");
        }

        // 規格：skin 只能掛在有 mesh 的節點上。
        using (var doc = JsonDocument.Parse(File.ReadAllText(export.GltfPath)))
        {
            if (doc.RootElement.TryGetProperty("nodes", out var nodes))
            {
                foreach (var node in nodes.EnumerateArray())
                {
                    if (node.TryGetProperty("skin", out _) && !node.TryGetProperty("mesh", out _))
                        problems.Add("skin 掛在沒有 mesh 的節點");
                }
            }
        }

        // 真的讀得開嗎 —— 用 SharpGLTF 走一遍，schema 錯誤會在這裡炸出來。
        // 匯入器接著會說「沒有任何網格」，那是它的本分（它要的是幾何），不算失敗。
        try
        {
            GltfImporter.Import(export.GltfPath, new GltfImporter.Options(Scale: 1f, AutoScale: false));
        }
        catch (Exception ex)
        {
            problems.Add($"讀不開：{ex.Message}");
        }

        if (export.Bones == 0)
            problems.Add("骨架掉了");

        if (export.Animations == 0)
            problems.Add("動作掉了");

        return ("純骨架", problems.Count == 0,
            problems.Count == 0
                ? $"骨 {export.Bones}、動作 {export.Animations}、無網格但檔案合法"
                : string.Join("；", problems.Take(3)));
    }

    private static IEnumerable<string> EmptyArrays(JsonElement element, string path)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Array:
                int index = 0;
                bool empty = true;
                foreach (var item in element.EnumerateArray())
                {
                    empty = false;
                    foreach (string found in EmptyArrays(item, $"{path}[{index}]"))
                        yield return found;
                    index++;
                }

                if (empty)
                    yield return path;

                break;

            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    foreach (string found in EmptyArrays(property.Value, $"{path}.{property.Name}"))
                        yield return found;
                }

                break;
        }
    }

    private static int Missing(string reason)
    {
        Console.WriteLine($"跳過自測：{reason}。          離開碼 2");
        return 2;
    }
}
