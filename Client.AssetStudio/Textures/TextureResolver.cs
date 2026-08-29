namespace Client.AssetStudio.Textures;

/// <summary>
/// 把 <c>.bmd</c> 網格裡寫的貼圖名找成磁碟上的實際檔案。
/// </summary>
/// <remarks>
/// 規則與 <c>tools/AssetCheck</c>、<c>Client.Main.Content.TextureLoader</c> 一致：
/// 模型裡寫的副檔名（多半是 <c>.jpg</c> / <c>.tga</c>）不一定是磁碟上的，
/// 要逐一換成 reader 支援的格式再找，而且 macOS 的檔案系統是區分大小寫的，
/// 資源包裡的大小寫又不統一（<c>MONSTER158.bmd</c> 與 <c>Monster01.bmd</c> 並存）。
///
/// 找不到不是例外狀況 —— 沒有貼圖的網格在遊戲裡會被<b>安靜地跳過不畫</b>，
/// 這正是「戰士看不到腿、NPC 只剩人頭」那類問題的成因，所以要把它當成一等資訊回報。
/// </remarks>
public static class TextureResolver
{
    /// <summary>客戶端 reader 支援的副檔名，順序與 <c>TextureLoader</c> 一致。</summary>
    public static readonly string[] Extensions = ["ozj", "ozt", "ozd", "ozp", "jpg", "tga", "png", "bmp"];

    private static readonly Dictionary<string, string[]> DirectoryCache = new(StringComparer.OrdinalIgnoreCase);

    public sealed record Resolution(string Requested, string? FullPath)
    {
        public bool Found => FullPath is not null;
        public string FileName => Path.GetFileName(FullPath ?? Requested);
    }

    public static Resolution Resolve(string modelDirectory, string texturePath)
    {
        if (string.IsNullOrWhiteSpace(texturePath))
            return new Resolution(texturePath ?? string.Empty, null);

        string baseName = Path.GetFileNameWithoutExtension(texturePath);

        foreach (var extension in Extensions)
        {
            string? hit = Find(modelDirectory, $"{baseName}.{extension}")
                       ?? Find(Path.Combine(modelDirectory, "texture"), $"{baseName}.{extension}");

            if (hit is not null)
                return new Resolution(texturePath, hit);
        }

        return new Resolution(texturePath, null);
    }

    /// <summary>
    /// 大小寫不敏感的檔案查找。一次列舉整個目錄再快取 ——
    /// 一個模型可能問十幾張貼圖，而 <c>Data/Item</c> 有三千多個檔案。
    /// </summary>
    private static string? Find(string directory, string fileName)
    {
        if (!Directory.Exists(directory))
            return null;

        if (!DirectoryCache.TryGetValue(directory, out var files))
        {
            files = Directory.GetFiles(directory);
            DirectoryCache[directory] = files;
        }

        foreach (var file in files)
        {
            if (string.Equals(Path.GetFileName(file), fileName, StringComparison.OrdinalIgnoreCase))
                return file;
        }

        return null;
    }

    /// <summary>寫回貼圖之後要讓快取失效，否則新檔案在這一次執行裡永遠找不到。</summary>
    public static void Invalidate(string directory) => DirectoryCache.Remove(directory);

    public static void InvalidateAll() => DirectoryCache.Clear();
}
