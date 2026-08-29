using Client.AssetStudio.Catalog;
using Client.Data.BMD;

namespace Client.AssetStudio.Textures;

public sealed record BatchResult(int Succeeded, int Skipped, int Failed, string[] Messages)
{
    public string Summary => $"成功 {Succeeded}、略過 {Skipped}、失敗 {Failed}";
}

/// <summary>
/// 一個模型的<b>整套</b>貼圖匯出與匯回。
/// </summary>
/// <remarks>
/// 這是「逐步替換貼圖」的日常迴圈，也是這個專案最先能真正產出自有美術的一條路：
/// <code>
/// 匯出整套 → 在 Photoshop / Krita 裡逐張重畫 → 匯回同一個資料夾 → 遊戲裡直接看到
/// </code>
/// 一次一張的按鈕不夠用 —— 一隻怪 2 到 14 張貼圖，一套角色裝備更多。
///
/// <b>檔名就是對應關係。</b>匯出時用來源檔的主檔名（<c>rt03.OZJ</c> → <c>rt03.png</c>），
/// 匯回時照同一個名字找。不另外寫對應表：多一份需要同步的狀態，
/// 就多一種「改了圖但沒生效」的失敗方式。
///
/// 匯回時<b>格式沿用原檔</b>（OZJ 進 OZJ、OZT 進 OZT），因為那兩者的差別是有沒有 alpha，
/// 而遊戲用它決定要不要走半透明路徑。OZD 沒有加密端，會改寫成同名的 <c>.OZT</c> ——
/// 貼圖搜尋順序會先找到它。
/// </remarks>
public static class TextureBatch
{
    /// <summary>把這個模型（含身體部位）用到的每一張貼圖匯出成 PNG。</summary>
    public static BatchResult Export(EntityEntry entry, string dataPath, string destination)
    {
        Directory.CreateDirectory(destination);

        int ok = 0;
        int skipped = 0;
        int failed = 0;
        var messages = new List<string>();

        foreach (var (texture, _) in CollectTextures(entry, dataPath, messages))
        {
            if (!texture.Found)
            {
                skipped++;
                messages.Add($"缺貼圖，略過：{texture.Requested}");
                continue;
            }

            string target = Path.Combine(destination,
                Path.GetFileNameWithoutExtension(texture.FullPath!) + ".png");

            try
            {
                TextureIO.ExportPng(texture.FullPath!, target);
                ok++;
            }
            catch (Exception ex)
            {
                failed++;
                messages.Add($"{Path.GetFileName(texture.FullPath)}：{ex.Message}");
            }
        }

        return new BatchResult(ok, skipped, failed, messages.ToArray());
    }

    /// <summary>
    /// 從資料夾把改過的 PNG 寫回遊戲資源。依主檔名對應，找不到同名的就略過。
    /// </summary>
    public static BatchResult Import(EntityEntry entry, string dataPath, string source, int jpegQuality, bool backup)
    {
        int ok = 0;
        int skipped = 0;
        int failed = 0;
        var messages = new List<string>();

        if (!Directory.Exists(source))
            return new BatchResult(0, 0, 1, [$"找不到資料夾 {source}"]);

        foreach (var (texture, directory) in CollectTextures(entry, dataPath, messages))
        {
            string stem = Path.GetFileNameWithoutExtension(texture.FullPath ?? texture.Requested);
            string candidate = Path.Combine(source, stem + ".png");

            if (!File.Exists(candidate))
            {
                skipped++;
                continue;
            }

            // 沒有原檔的（缺貼圖的網格）就新建一個 OZT —— 那是唯一能無損寫入而且帶 alpha 的格式。
            string target = texture.FullPath ?? Path.Combine(directory, stem + ".OZT");

            if (Path.GetExtension(target).Equals(".ozd", StringComparison.OrdinalIgnoreCase))
                target = Path.ChangeExtension(target, ".OZT");

            if (backup && File.Exists(target) && !File.Exists(target + ".bak"))
            {
                try
                {
                    File.Copy(target, target + ".bak");
                }
                catch (Exception ex)
                {
                    failed++;
                    messages.Add($"備份失敗，略過 {Path.GetFileName(target)}：{ex.Message}");
                    continue;
                }
            }

            var result = TextureIO.Import(candidate, target, jpegQuality);

            if (result.Success)
            {
                ok++;
            }
            else
            {
                failed++;
                messages.Add($"{stem}：{result.Message}");
            }
        }

        TextureResolver.InvalidateAll();
        return new BatchResult(ok, skipped, failed, messages.ToArray());
    }

    /// <summary>
    /// 這個模型會用到的每一張貼圖，連同「該去哪個資料夾找」。
    /// </summary>
    /// <remarks>
    /// 身體部位是另外幾個模型，貼圖跟著它們自己的資料夾走 ——
    /// 少了這一段，換角色裝備的貼圖時會全部寫到主模型的資料夾，而遊戲永遠找不到。
    /// </remarks>
    private static IEnumerable<(TextureResolver.Resolution Texture, string Directory)> CollectTextures(
        EntityEntry entry, string dataPath, List<string> messages)
    {
        var sources = new List<string>();

        if (entry.FullPath is not null)
            sources.Add(entry.FullPath);

        foreach (var part in entry.BodyParts)
        {
            string full = Path.Combine(dataPath, part);
            if (File.Exists(full))
                sources.Add(full);
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var reader = new BMDReader();

        foreach (var source in sources)
        {
            BMD model;

            try
            {
                model = reader.Load(source).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                messages.Add($"讀不開 {Path.GetFileName(source)}：{ex.Message}");
                continue;
            }

            string directory = Path.GetDirectoryName(source) ?? dataPath;

            foreach (var mesh in model.Meshes ?? [])
            {
                if (string.IsNullOrWhiteSpace(mesh.TexturePath))
                    continue;

                var resolution = TextureResolver.Resolve(directory, mesh.TexturePath);
                string key = resolution.FullPath ?? directory + "|" + mesh.TexturePath;

                if (seen.Add(key))
                    yield return (resolution, directory);
            }
        }
    }
}
