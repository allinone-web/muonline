using Client.AssetStudio.Catalog;
using Client.AssetStudio.Textures;

namespace Client.AssetStudio.Cli;

/// <summary>
/// 貼圖的匯出與匯入，命令列版。
/// </summary>
/// <remarks>
/// 圖形介面一次改一張；真正要換掉一整套美術資源時需要的是能寫進腳本的東西
/// （<c>for f in *.png; do … done</c>）。這兩個子命令與面板上的按鈕走完全相同的程式碼路徑，
/// 所以介面上驗證過的行為，批次跑也一樣。
/// </remarks>
public static class TextureCommands
{
    public static int Export(string source, string? destination)
    {
        if (!File.Exists(source))
        {
            Console.Error.WriteLine($"找不到 {source}");
            return 2;
        }

        destination ??= Path.ChangeExtension(source, ".png");

        try
        {
            TextureIO.ExportPng(source, destination);

            using var image = TextureIO.Decode(source);
            Console.WriteLine($"{Path.GetFileName(source)} -> {destination}　（{image.Width}x{image.Height}）");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"匯出失敗：{ex.GetType().Name} {ex.Message}");
            return 1;
        }
    }

    /// <summary>把一個模型（含身體部位）用到的整套貼圖匯出成 PNG。</summary>
    public static int ExportAll(EntityCatalog catalog, string target, string destination, string dataPath)
    {
        var entry = FindModel(catalog, target);
        if (entry is null)
            return 2;

        var result = TextureBatch.Export(entry, dataPath, destination);

        Console.WriteLine($"{entry.Name} → {destination}");
        Console.WriteLine(result.Summary);

        foreach (var message in result.Messages)
            Console.WriteLine("  " + message);

        return result.Failed == 0 ? 0 : 1;
    }

    /// <summary>把資料夾裡改過的 PNG 依主檔名寫回這個模型的貼圖。</summary>
    public static int ImportAll(
        EntityCatalog catalog, string target, string source, string dataPath, int quality, bool backup)
    {
        var entry = FindModel(catalog, target);
        if (entry is null)
            return 2;

        var result = TextureBatch.Import(entry, dataPath, source, quality, backup);

        Console.WriteLine($"{source} → {entry.Name}");
        Console.WriteLine(result.Summary);

        foreach (var message in result.Messages)
            Console.WriteLine("  " + message);

        return result.Failed == 0 ? 0 : 1;
    }

    private static EntityEntry? FindModel(EntityCatalog catalog, string target)
    {
        var entry = catalog.Entries.FirstOrDefault(e =>
                        e.FullPath is not null
                     && (e.ModelPath.Equals(target, StringComparison.OrdinalIgnoreCase)
                      || e.Name.Equals(target, StringComparison.OrdinalIgnoreCase)
                      || e.ClassName?.Equals(target, StringComparison.OrdinalIgnoreCase) == true))
                 ?? catalog.Entries.FirstOrDefault(e =>
                        e.FullPath is not null && e.Search.Contains(target, StringComparison.OrdinalIgnoreCase));

        if (entry is null)
            Console.Error.WriteLine($"找不到「{target}」");

        return entry;
    }

    public static int Import(string source, string destination, int quality, bool backup)
    {
        if (!File.Exists(source))
        {
            Console.Error.WriteLine($"找不到 {source}");
            return 2;
        }

        // 預設會備份。這個命令會直接覆寫遊戲資源，而原始資源包有 1.8 GB，
        // 弄壞一張圖之後從壓縮檔裡撈回來比留一份 .bak 麻煩得多。
        if (backup && File.Exists(destination))
        {
            try
            {
                File.Copy(destination, destination + ".bak", overwrite: true);
                Console.WriteLine($"已備份 {destination}.bak");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"備份失敗，已中止：{ex.Message}");
                return 1;
            }
        }

        var result = TextureIO.Import(source, destination, quality);
        Console.WriteLine(result.Message);
        return result.Success ? 0 : 1;
    }
}
