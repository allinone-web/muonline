using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.MapEditor;

/// <summary>
/// 把 MU 的貼圖檔解出來、上到 GPU、登記給 ImGui，並依完整路徑快取。
/// </summary>
/// <remarks>
/// 不走 <c>Client.Main.Content.TextureLoader</c>：那一套是為遊戲的資產路徑與非同步預載設計的，
/// 編輯器要的是「給我這個檔案的縮圖」這種直接的存取。
/// </remarks>
public sealed class TexturePreviewCache
{
    private readonly GraphicsDevice _device;
    private readonly ImGuiRenderer _imgui;
    private readonly Dictionary<string, IntPtr?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public TexturePreviewCache(GraphicsDevice device, ImGuiRenderer imgui)
    {
        _device = device;
        _imgui = imgui;
    }

    /// <summary>最近一次載入失敗的訊息，讓 UI 可以顯示出來而不是靜靜地少一張圖。</summary>
    public string? LastError { get; private set; }

    public IntPtr? Get(string path)
    {
        if (_cache.TryGetValue(path, out var cached))
            return cached;

        IntPtr? result = null;

        try
        {
            var texture = Load(path);
            if (texture is not null)
                result = _imgui.BindTexture(texture);
        }
        catch (Exception ex)
        {
            LastError = $"{Path.GetFileName(path)}: {ex.Message}";
        }

        _cache[path] = result;
        return result;
    }

    private Texture2D? Load(string path) => TextureDecoder.Decode(_device, path);
}
