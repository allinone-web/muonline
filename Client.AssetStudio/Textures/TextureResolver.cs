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
    /// <summary>客戶端 reader 支援的副檔名。</summary>
    public static readonly string[] Extensions = ["ozt", "ozd", "tga", "ozj", "ozp", "jpg", "png", "bmp"];

    /// <summary>
    /// 網格要求 <c>.tga</c> / <c>.ozt</c> / <c>.ozd</c> 時的搜尋順序 —— 帶 alpha 的格式優先。
    /// </summary>
    private static readonly string[] AlphaFirst = ["ozt", "ozd", "tga", "ozj", "ozp", "jpg", "png", "bmp"];

    /// <summary>網格要求 <c>.jpg</c> / <c>.ozj</c>（或沒寫）時的搜尋順序。</summary>
    private static readonly string[] OpaqueFirst = ["ozj", "ozp", "jpg", "ozt", "ozd", "tga", "png", "bmp"];

    /// <summary>
    /// 依網格寫的副檔名挑搜尋順序。
    /// </summary>
    /// <remarks>
    /// <b>這一段不能簡化成一張固定的清單。</b>資源包裡同名檔案會兩種格式並存 ——
    /// <c>Object3/stree.OZJ</c>（JPEG，不透明）與 <c>Object3/stree.OZT</c>（TGA，帶 alpha）——
    /// 固定先找 <c>ozj</c> 的話，樹葉會拿到不透明那一張，整棵樹變成一塊灰板子。
    ///
    /// 客戶端的作法是<b>先看網格要求什麼副檔名</b>再選 reader
    /// （<c>TextureLoader</c> 的 <c>.tga → OZTReader</c>、<c>.jpg → OZJReader</c>），
    /// 然後把磁碟上的檔名換成該 reader 的副檔名。這裡照抄那個語意，
    /// 再保留原本的全格式後援，讓資源包缺檔時仍然找得到東西。
    /// </remarks>
    private static string[] CandidateExtensions(string texturePath)
        => Path.GetExtension(texturePath).ToLowerInvariant() switch
        {
            ".tga" or ".ozt" or ".ozd" => AlphaFirst,
            _ => OpaqueFirst,
        };

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

        foreach (var extension in CandidateExtensions(texturePath))
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
