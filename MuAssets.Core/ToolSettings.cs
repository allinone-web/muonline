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

    /// <summary>
    /// 第一層筆刷開啟自動過渡：中心塗實，邊緣用第二層 + 混合值做漸層。
    /// </summary>
    /// <remarks>
    /// MU 的地形本來就是「兩層貼圖 + 逐格混合值」，過渡是設計的一部分，
    /// 不是後來加的技巧 —— 實測 World1 有 35% 的格子帶第二層，
    /// 而它們的混合值是連續分布的（25–254，中位數 127），不是二元的。
    ///
    /// 沒有這個開關的時候，要畫出那種效果得手動切到混合筆刷、
    /// 沿著邊界一格一格塗，畫一條路要花幾十分鐘。
    /// </remarks>
    public bool AutoTransition { get; set; } = true;

    // ── 散佈筆刷 ──────────────────────────────────────────────

    /// <summary>一筆撒幾個。</summary>
    public int ScatterCount { get; set; } = 8;

    /// <summary>物件之間的最小間距（格）。0 = 不限制，但那會撒出結塊。</summary>
    public float ScatterSpacing { get; set; } = 1.5f;

    /// <summary>避開不可走／無地面／水的格子。</summary>
    public bool ScatterAvoidBlocked { get; set; } = true;

    /// <summary>隨機朝向的範圍（度）。</summary>
    public float PlaceRandomYaw { get; set; } = 360f;

    /// <summary>隨機大小的比例，0.2 表示 0.8–1.2 倍。</summary>
    public float PlaceRandomScale { get; set; } = 0.15f;

    /// <summary>放置與散佈要用哪一種物件（.obj 的 type）。</summary>
    public short PlaceObjectType { get; set; }

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
