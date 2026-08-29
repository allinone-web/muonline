using Client.AssetStudio.Textures;
using Client.Data.BMD;

namespace Client.AssetStudio.Catalog;

/// <summary>一個模型不用載進 GPU 就能問到的事實。</summary>
public sealed record ModelSummary(
    int Meshes,
    int Bones,
    int Actions,
    int Triangles,
    string[] Textures,
    string[] MissingTextures)
{
    /// <summary>解析失敗的原因。成功時是 null。</summary>
    public string? Error { get; init; }

    public static readonly ModelSummary Empty = new(0, 0, 0, 0, [], []);

    public static ModelSummary Failed(string error) => Empty with { Error = error };
}

/// <summary>
/// 離線檢查模型：網格數、骨骼數、動作數，以及<b>缺哪幾張貼圖</b>。
/// </summary>
/// <remarks>
/// 判斷規則與 <c>tools/AssetCheck</c> 相同（那支工具已經證明過這條路徑）。
/// 缺貼圖是這裡最重要的輸出：沒有貼圖的網格在遊戲裡<b>不會報錯，只會不畫</b>，
/// 所以「哪個模型缺哪張圖」必須是一個能被搜尋、能被篩選的一等資訊，
/// 而不是要人一隻一隻點開看。
///
/// 結果快取在記憶體 —— 目錄面板的「只顯示缺貼圖的」篩選會對整個類別做一遍。
/// </remarks>
public static class ModelInspector
{
    private static readonly Dictionary<string, ModelSummary> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ModelSummary Inspect(EntityEntry entry)
    {
        if (entry.FullPath is null)
            return ModelSummary.Empty;

        return Inspect(entry.FullPath);
    }

    public static ModelSummary Inspect(string bmdPath)
    {
        if (Cache.TryGetValue(bmdPath, out var cached))
            return cached;

        ModelSummary summary;

        try
        {
            var bmd = new BMDReader().Load(bmdPath).GetAwaiter().GetResult();
            string directory = Path.GetDirectoryName(bmdPath) ?? string.Empty;

            var textures = new List<string>();
            var missing = new List<string>();
            int triangles = 0;

            foreach (var mesh in bmd.Meshes ?? [])
            {
                triangles += mesh.Triangles?.Length ?? 0;

                string name = mesh.TexturePath ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name))
                {
                    missing.Add("（網格沒有貼圖名稱）");
                    continue;
                }

                if (!textures.Contains(name, StringComparer.OrdinalIgnoreCase))
                    textures.Add(name);

                if (!TextureResolver.Resolve(directory, name).Found && !missing.Contains(name, StringComparer.OrdinalIgnoreCase))
                    missing.Add(name);
            }

            summary = new ModelSummary(
                bmd.Meshes?.Length ?? 0,
                bmd.Bones?.Length ?? 0,
                bmd.Actions?.Length ?? 0,
                triangles,
                textures.ToArray(),
                missing.ToArray());
        }
        catch (Exception ex)
        {
            // 解析失敗的也記進快取，否則篩選會每幀重試同一個壞檔。
            // 失敗與「這個模型真的是空的」必須分得出來 —— 前者是 bug 或壞檔，後者不是。
            summary = ModelSummary.Failed($"{ex.GetType().Name}: {ex.Message}");
        }

        Cache[bmdPath] = summary;
        return summary;
    }

    public static int MissingTextureCount(EntityEntry entry) => Inspect(entry).MissingTextures.Length;

    public static void Invalidate(string bmdPath) => Cache.Remove(bmdPath);
}
