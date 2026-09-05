using System.Text.Json;
using Client.AssetStudio.Catalog;
using Client.AssetStudio.Textures;

namespace Client.AssetStudio.Cli;

/// <summary>
/// 把整個資源目錄連同**每個模型的可量事實**倒成一份 JSON。
/// </summary>
/// <remarks>
/// <b>為什麼要有這個。</b>目錄面板是給人看的，一次只看得到一筆；
/// 而「幫我找出所有超過 3000 面的怪物」「哪些模型缺貼圖」「這個動作幾幀」
/// 這種問題，人跟 AI 都需要**整份可查詢的資料**，不是一個一個點開。
///
/// 這份 JSON 是 <c>tools/mu catalog</c> 與 Python 端統一索引的資料來源 ——
/// BMD 的解析留在這裡（C# 有現成的 <c>BMDReader</c> 與貼圖解析），
/// Python 端只負責合併與分類，不重寫一份解析器。
///
/// 輸出是<b>決定性</b>的：同樣的 <c>Data/</c> 一定得到同樣的 JSON，
/// 所以可以進 git、可以做 diff、可以當回歸測試的基準。
/// </remarks>
public static class CatalogJsonCommand
{
    public static int Run(EntityCatalog catalog, string dataPath, string outputPath, bool inspect)
    {
        var entries = new List<object>();
        int inspected = 0;
        int failed = 0;

        foreach (var entry in catalog.Entries.OrderBy(e => e.Kind).ThenBy(e => e.Group).ThenBy(e => e.Name))
        {
            object? model = null;

            if (inspect && entry.FullPath is not null &&
                entry.ModelPath.EndsWith(".bmd", StringComparison.OrdinalIgnoreCase))
            {
                var summary = ModelInspector.Inspect(entry.FullPath);
                inspected++;
                if (summary.Error is not null)
                    failed++;

                model = new
                {
                    meshes = summary.Meshes,
                    bones = summary.Bones,
                    actions = summary.Actions,
                    triangles = summary.Triangles,
                    vertices = summary.Vertices,
                    meshTriangles = summary.MeshTriangles,
                    textures = summary.Textures,
                    missingTextures = summary.MissingTextures,
                    actionDetails = summary.ActionDetails.Select(a => new
                    {
                        index = a.Index,
                        frames = a.Frames,
                        playSpeed = a.PlaySpeed,
                        lockPositions = a.LockPositions,
                    }),
                    error = summary.Error,
                };
            }

            entries.Add(new
            {
                id = entry.Id,
                kind = entry.Kind.ToString(),
                group = entry.Group,
                detail = entry.Detail,
                number = entry.Number,
                name = entry.Name,
                className = entry.ClassName,
                modelPath = entry.ModelPath,
                exists = entry.FullPath is not null,
                bytes = entry.FullPath is not null && File.Exists(entry.FullPath)
                    ? new FileInfo(entry.FullPath).Length
                    : 0L,
                referenced = entry.IsReferenced,
                bodyParts = entry.BodyParts,
                attachments = entry.Attachments,
                model,
            });
        }

        var payload = new
        {
            schema = "mu-asset-catalog/1",
            source = "mu",
            dataPath = Path.GetFullPath(dataPath),
            generated = "MuAssetStudio --catalog-json",
            counts = new
            {
                entries = entries.Count,
                inspected,
                failed,
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
                // 中文的分類名不要被跳脫成 \uXXXX —— 這份檔案是要給人跟 AI 讀的。
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            });
        }

        Console.WriteLine();
        Console.WriteLine($"已寫出 {entries.Count} 筆 → {outputPath}");
        if (inspect)
            Console.WriteLine($"其中解析了 {inspected} 個模型，{failed} 個解不開");
        else
            Console.WriteLine("（沒有加 --inspect，所以沒有模型細節）");

        return 0;
    }
}
