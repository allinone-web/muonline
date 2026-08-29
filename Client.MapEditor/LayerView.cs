using Client.Data.ATT;
using Client.Data.MAP;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MuAssets.Core;

namespace Client.MapEditor;

public enum MapLayer
{
    Layer1,
    Layer2,
    Alpha,
    Attribute,
    Height,
    Light,
}

/// <summary>
/// 把 <see cref="MapDocument"/> 的逐格資料畫成一張 256×256 的圖，供「圖層」面板顯示。
/// </summary>
/// <remarks>
/// 這同時是編輯器的俯視導覽圖：一眼看出地形分佈、不可行走區、貼圖用在哪。
/// 只在圖層或文件變動時重建，不是每幀。
/// </remarks>
public sealed class LayerView : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly ImGuiRenderer _imgui;

    private Texture2D? _texture;
    private IntPtr? _textureId;

    public LayerView(GraphicsDevice device, ImGuiRenderer imgui)
    {
        _device = device;
        _imgui = imgui;
    }

    public IntPtr? TextureId => _textureId;

    public void Rebuild(MapDocument document, MapLayer layer)
    {
        var pixels = new Color[MapDocument.CellCount];

        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = Sample(document, layer, i);

        // 圖層尺寸固定 256×256，貼圖只需要建一次，之後重複用 SetData。
        if (_texture is null || _texture.IsDisposed)
        {
            _texture = new Texture2D(_device, MapDocument.Size, MapDocument.Size);
            _textureId = _imgui.BindTexture(_texture);
        }

        _texture.SetData(pixels);
    }

    private static Color Sample(MapDocument document, MapLayer layer, int index) => layer switch
    {
        MapLayer.Layer1 => TileIndexColor(document.Layer1[index]),
        MapLayer.Layer2 => document.Layer2[index] == TerrainTextureMapping.NoLayerIndex
            ? new Color(24, 26, 30)
            : TileIndexColor(document.Layer2[index]),
        MapLayer.Alpha => Grayscale(document.Alpha[index]),
        MapLayer.Attribute => AttributeColor(document.Attributes[index]),
        MapLayer.Height => Grayscale(document.HeightAt(index)),
        MapLayer.Light => ToXnaColor(document.LightAt(index)),
        _ => Color.Magenta,
    };

    private static Color Grayscale(byte value) => new(value, value, value);

    private static Color ToXnaColor(System.Drawing.Color color) => new(color.R, color.G, color.B);

    /// <summary>
    /// 貼圖索引沒有天然顏色，用黃金角在色相環上取樣，相鄰索引的顏色差距最大。
    /// </summary>
    private static Color TileIndexColor(byte index)
    {
        const float GoldenAngle = 137.507f;
        return FromHsv((index * GoldenAngle) % 360f, 0.55f, 0.85f);
    }

    /// <summary>
    /// 屬性圖用「問題優先」的配色：擋路的紅、安全區綠、水藍，一眼掃得出來。
    /// </summary>
    private static Color AttributeColor(TWFlags flags)
    {
        if (flags == TWFlags.None)
            return new Color(40, 44, 50);

        if (flags.HasFlag(TWFlags.NoGround))
            return new Color(18, 18, 22);

        if (flags.HasFlag(TWFlags.NoMove))
            return new Color(200, 70, 70);

        if (flags.HasFlag(TWFlags.SafeZone))
            return new Color(90, 190, 110);

        if (flags.HasFlag(TWFlags.Water))
            return new Color(70, 130, 210);

        if (flags.HasFlag(TWFlags.NoAttackZone))
            return new Color(210, 180, 80);

        return new Color(140, 110, 190);
    }

    private static Color FromHsv(float hue, float saturation, float value)
    {
        float c = value * saturation;
        float x = c * (1f - MathF.Abs(((hue / 60f) % 2f) - 1f));
        float m = value - c;

        (float r, float g, float b) = hue switch
        {
            < 60f => (c, x, 0f),
            < 120f => (x, c, 0f),
            < 180f => (0f, c, x),
            < 240f => (0f, x, c),
            < 300f => (x, 0f, c),
            _ => (c, 0f, x),
        };

        return new Color(r + m, g + m, b + m);
    }

    public void Dispose()
    {
        if (_textureId.HasValue)
            _imgui.UnbindTexture(_textureId.Value);

        _texture?.Dispose();
        _texture = null;
        _textureId = null;
    }
}
