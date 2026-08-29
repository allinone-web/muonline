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
