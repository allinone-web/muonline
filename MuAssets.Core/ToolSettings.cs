using Client.Data.ATT;

namespace MuAssets.Core;

/// <summary>
/// 筆刷工具需要的全部設定。
/// </summary>
/// <remarks>
/// 刻意只放「工具要用的資料」，不是整個編輯器狀態 ——
/// <see cref="EditorTools"/> 原本吃整個 <c>EditorSession</c>，那讓它綁死在 MonoGame 編輯器上。
/// 拆出這個純資料物件之後，Godot 那邊的編輯器可以直接重用同一套筆刷邏輯。
/// </remarks>
public sealed class ToolSettings
{
    public EditorToolKind Tool { get; set; } = EditorToolKind.None;

    public Brush Brush { get; } = new();

    /// <summary>貼圖筆刷要畫哪個索引。</summary>
    public byte PaintTileIndex { get; set; }

    /// <summary>第二層筆刷改成塗「無第二層」（哨兵值 255）。</summary>
    public bool PaintLayer2AsEmpty { get; set; }

    /// <summary>混合筆刷要逼近的目標值。</summary>
    public float PaintAlphaValue { get; set; } = 255f;

    public HeightMode HeightMode { get; set; } = HeightMode.Raise;

    /// <summary>升降每次施加的高度單位（0–255 的高度圖刻度）。</summary>
    public float HeightStep { get; set; } = 12f;

    /// <summary>壓平模式要壓到的高度。</summary>
    public float FlattenTarget { get; set; } = 100f;

    public TWFlags AttributeFlag { get; set; } = TWFlags.NoMove;

    /// <summary>屬性筆刷改成清除該旗標而不是設定。</summary>
    public bool AttributeErase { get; set; }
}
