using Client.Data.ATT;

namespace MuAssets.Core;

/// <summary>編輯器改動的哪一份逐格資料。</summary>
public enum EditTarget
{
    Layer1,
    Layer2,
    Alpha,
    Attribute,
    Height,
    Light,
}

/// <summary>
/// 一筆可還原的改動：只記「這一筆真的動到的格子」的前後值。
/// </summary>
/// <remarks>
/// 不存整張快照。一張圖一層是 65536 格，畫幾百筆就會吃掉幾百 MB；
/// 一次筆劃通常只碰幾十到幾百格，記 diff 便宜得多。
/// </remarks>
/// <summary>
/// 一次筆劃。
/// </summary>
/// <remarks>
/// 一筆可以同時動好幾種資料 —— 自動過渡的第一層筆刷就會一次改
/// 第一層、第二層與混合值三樣。所以變動是「每種目標各一份」，
/// 而不是整筆只有一個目標；不然撤銷只會還原其中一樣，
/// 畫面看起來復原了、資料其實是壞的。
///
/// <see cref="Target"/> 仍然保留，那是給介面顯示與歸類用的主要目標。
/// </remarks>
public sealed class EditStroke
{
    private readonly Dictionary<EditTarget, Dictionary<int, (int Before, int After)>> _changes = [];

    public EditStroke(EditTarget target, string description)
    {
        Target = target;
        Description = description;
    }

    /// <summary>主要目標，給介面顯示用。實際變動可能不只這一種。</summary>
    public EditTarget Target { get; }

    public string Description { get; }

    public int CellCount => _changes.Values.Sum(cells => cells.Count);

    public bool IsEmpty => CellCount == 0;

    /// <summary>
    /// 記錄一格的變動。同一格在一次筆劃裡被重複塗到時，保留最早的 Before 與最新的 After。
    /// </summary>
    public void Record(int index, int before, int after) => Record(Target, index, before, after);

    /// <summary>記錄某一種目標上一格的變動。</summary>
    public void Record(EditTarget target, int index, int before, int after)
    {
        if (!_changes.TryGetValue(target, out var cells))
            _changes[target] = cells = [];

        if (cells.TryGetValue(index, out var existing))
            cells[index] = (existing.Before, after);
        else
            cells[index] = (before, after);
    }

    /// <summary>把沒有實際變化的格子丟掉，避免留下空筆劃。</summary>
    public void Trim()
    {
        foreach (var (_, cells) in _changes)
        {
            foreach (int key in cells.Where(kv => kv.Value.Before == kv.Value.After).Select(kv => kv.Key).ToArray())
                cells.Remove(key);
        }
    }

    public void Apply(MapDocument document, bool undo)
    {
        foreach (var (target, cells) in _changes)
        {
            foreach (var (index, (before, after)) in cells)
                Write(document, target, index, undo ? before : after);
        }
    }

    private static void Write(MapDocument document, EditTarget target, int index, int value)
    {
        switch (target)
        {
            case EditTarget.Layer1:
                document.Layer1[index] = (byte)value;
                break;
            case EditTarget.Layer2:
                document.Layer2[index] = (byte)value;
                break;
            case EditTarget.Alpha:
                document.Alpha[index] = (byte)value;
                break;
            case EditTarget.Attribute:
                document.Attributes[index] = (TWFlags)value;
                break;
            case EditTarget.Height:
                SetHeight(document, index, (byte)value);
                break;
            case EditTarget.Light:
                SetLight(document, index, value);
                break;
        }
    }

    private static void SetHeight(MapDocument document, int index, byte value)
    {
        var data = document.Height?.Data;
        if (data is not null && index < data.Length)
            data[index] = System.Drawing.Color.FromArgb(255, value, 0, 0);
    }

    private static void SetLight(MapDocument document, int index, int packed)
    {
        var data = document.Light?.Data;
        if (data is not null && index < data.Length)
            data[index] = System.Drawing.Color.FromArgb(255, (packed >> 16) & 0xFF, (packed >> 8) & 0xFF, packed & 0xFF);
    }

    /// <summary>把光照顏色打包成一個 int，供歷史記錄用（歷史只存整數值）。</summary>
    public static int PackLight(byte r, byte g, byte b) => (r << 16) | (g << 8) | b;
}

/// <summary>撤銷／重做堆疊。</summary>
public sealed class EditHistory
{
    private const int MaxDepth = 200;

    private readonly List<EditStroke> _undo = [];
    private readonly List<EditStroke> _redo = [];

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public int UndoDepth => _undo.Count;

    public string? NextUndoDescription => _undo.Count > 0 ? _undo[^1].Description : null;
    public string? NextRedoDescription => _redo.Count > 0 ? _redo[^1].Description : null;

    public void Push(EditStroke stroke)
    {
        stroke.Trim();
        if (stroke.IsEmpty)
            return;

        _undo.Add(stroke);
        _redo.Clear();

        if (_undo.Count > MaxDepth)
            _undo.RemoveAt(0);
    }

    /// <returns>被還原的那一筆，沒有可還原時為 null。</returns>
    public EditStroke? Undo(MapDocument document)
    {
        if (_undo.Count == 0)
            return null;

        var stroke = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        stroke.Apply(document, undo: true);
        _redo.Add(stroke);
        return stroke;
    }

    public EditStroke? Redo(MapDocument document)
    {
        if (_redo.Count == 0)
            return null;

        var stroke = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        stroke.Apply(document, undo: false);
        _undo.Add(stroke);
        return stroke;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}
