using Microsoft.Xna.Framework.Graphics;

namespace Client.MapEditor;

/// <summary>
/// 模型縮圖的記憶體快取。只畫「這一幀真的要顯示」的那幾張，而且有每幀預算。
/// </summary>
/// <remarks>
/// 資源包裡有 6863 個 .bmd，一次全畫會卡住主執行緒好幾分鐘，全部留在記憶體
/// （128×128 RGBA ≈ 64KB 一張）也要 450MB。所以：捲到哪畫到哪，每幀最多畫幾張。
///
/// 縮圖必須在主執行緒畫（要切 render target），所以不能丟到背景執行緒。
/// </remarks>
public sealed class ThumbnailCache : IDisposable
{
    private const int DefaultBudgetPerFrame = 3;

    private readonly BmdThumbnailRenderer _renderer;
    private readonly ImGuiRenderer _imgui;
    private readonly Dictionary<string, IntPtr?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Texture2D> _textures = [];

    private int _budgetLeft;

    public ThumbnailCache(GraphicsDevice device, ImGuiRenderer imgui, int size = 128)
    {
        _renderer = new BmdThumbnailRenderer(device, size);
        _imgui = imgui;
    }

    public int RenderedCount => _textures.Count;

    /// <summary>每幀開頭呼叫，重置這一幀能畫幾張。</summary>
    public void BeginFrame(int budget = DefaultBudgetPerFrame) => _budgetLeft = budget;

    /// <summary>
    /// 取得縮圖。還沒畫且這一幀預算用完時回傳 null —— 呼叫端畫個佔位方塊，下一幀再來。
    /// </summary>
    public IntPtr? Get(string bmdPath)
    {
        if (_cache.TryGetValue(bmdPath, out var cached))
            return cached;

        if (_budgetLeft <= 0)
            return null;

        _budgetLeft--;

        var texture = _renderer.Render(bmdPath);
        IntPtr? id = null;

        if (texture is not null)
        {
            _textures.Add(texture);
            id = _imgui.BindTexture(texture);
        }

        // 畫失敗的也記進去（值為 null），才不會每幀重試。
        _cache[bmdPath] = id;
        return id;
    }

    public void Dispose()
    {
        foreach (var texture in _textures)
            texture.Dispose();

        _textures.Clear();
        _cache.Clear();
        _renderer.Dispose();
    }
}
