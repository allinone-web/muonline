using Client.MapEditor;
using Microsoft.Xna.Framework.Graphics;

namespace Client.AssetStudio.Rendering;

/// <summary>
/// 模型縮圖的快取，**有上限**。超過就丟掉最久沒用到的。
/// </summary>
/// <remarks>
/// 沒有沿用 <c>Client.MapEditor.ThumbnailCache</c>，因為兩邊的需求不同而不是實作不同：
/// 地圖編輯器一次只看一張圖的 <c>Object{N}/</c>（大約兩百個模型），畫完就到頂；
/// 這個工具的目錄有 <b>4739 個模型</b>，光「道具」一類就 2715 個。
/// 一張 128×128 RGBA 是 64 KB，全部畫完是 <b>約 300 MB 的 GPU 記憶體，而且永遠不會釋放</b>——
/// 捲一遍道具就吃掉 175 MB。渲染器本身仍然是共用的那一份。
///
/// 逐出策略是「最久沒被取用」。捲動時看得到的那一頁一定是最近取用的，
/// 所以實際上不會抖動（來回捲同一區塊不會反覆重畫）。
/// </remarks>
public sealed class BoundedThumbnailCache : IDisposable
{
    /// <summary>每幀最多畫幾張。縮圖要切 render target，只能在主執行緒畫。</summary>
    private const int DefaultBudgetPerFrame = 3;

    private readonly BmdThumbnailRenderer _renderer;
    private readonly ImGuiRenderer _imgui;
    private readonly int _capacity;

    /// <summary>路徑 → 貼圖 id（畫失敗是 null）。</summary>
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>最近使用順序，最舊的在最前面。</summary>
    private readonly LinkedList<string> _recent = new();

    private int _budgetLeft;

    public BoundedThumbnailCache(GraphicsDevice device, ImGuiRenderer imgui, int size = 128, int capacity = 600)
    {
        _renderer = new BmdThumbnailRenderer(device, size);
        _imgui = imgui;
        _capacity = Math.Max(capacity, 32);
    }

    public int Count => _entries.Count;

    public int Capacity => _capacity;

    /// <summary>
    /// 每幀開頭呼叫：重置這一幀的繪製預算，並在<b>這時候</b>才逐出超出上限的縮圖。
    /// </summary>
    /// <remarks>
    /// 逐出一定要在幀的開頭，不能在 <see cref="Get"/> 裡順手做。
    /// ImGui 的繪製指令只記貼圖 id，真正的貼圖是在 <c>EndLayout</c> 才查表用的；
    /// 在同一幀中途 <c>UnbindTexture</c> 一個前面已經送出繪製指令的 id，
    /// <c>ImGuiRenderer</c> 查不到就會丟
    /// <c>InvalidOperationException: ImGui 要求了未登記的貼圖 id</c>。
    /// 上一幀的繪製在這裡已經結束，所以此刻逐出是安全的。
    /// </remarks>
    public void BeginFrame(int budget = DefaultBudgetPerFrame)
    {
        _budgetLeft = budget;
        Trim();
    }

    /// <summary>
    /// 取得縮圖。還沒畫、而且這一幀的預算用完時回傳 null ——
    /// 呼叫端畫個佔位方塊，下一幀再來。
    /// </summary>
    public IntPtr? Get(string bmdPath)
    {
        if (_entries.TryGetValue(bmdPath, out var cached))
        {
            Touch(cached);
            return cached.TextureId;
        }

        if (_budgetLeft <= 0)
            return null;

        _budgetLeft--;

        var texture = _renderer.Render(bmdPath);
        IntPtr? id = null;

        if (texture is not null)
            id = _imgui.BindTexture(texture);

        // 畫失敗的也記進去（值為 null），才不會每幀重試同一個壞檔。
        var entry = new Entry(bmdPath, texture, id, _recent.AddLast(bmdPath));
        _entries[bmdPath] = entry;

        // 這裡不 Trim：見 BeginFrame 的說明。這一幀最多超出上限 budget 張。
        return id;
    }

    private void Touch(Entry entry)
    {
        _recent.Remove(entry.Node);
        _recent.AddLast(entry.Node);
    }

    private void Trim()
    {
        while (_entries.Count > _capacity && _recent.First is { } oldest)
        {
            _recent.RemoveFirst();

            if (_entries.Remove(oldest.Value, out var evicted))
                Release(evicted);
        }
    }

    private void Release(Entry entry)
    {
        if (entry.TextureId is IntPtr id)
            _imgui.UnbindTexture(id);

        entry.Texture?.Dispose();
    }

    public void Dispose()
    {
        foreach (var entry in _entries.Values)
            Release(entry);

        _entries.Clear();
        _recent.Clear();
        _renderer.Dispose();
    }

    private sealed record Entry(string Path, Texture2D? Texture, IntPtr? TextureId, LinkedListNode<string> Node);
}
