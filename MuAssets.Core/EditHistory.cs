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
public sealed class EditStroke
{
    private readonly Dictionary<int, (int Before, int After)> _cells = [];

    public EditStroke(EditTarget target, string description)
    {
        Target = target;
        Description = description;
    }

    public EditTarget Target { get; }
    public string Description { get; }
    public int CellCount => _cells.Count;
    public bool IsEmpty => _cells.Count == 0;

    /// <summary>
    /// 記錄一格的變動。同一格在一次筆劃裡被重複塗到時，保留最早的 Before 與最新的 After。
    /// </summary>
    public void Record(int index, int before, int after)
    {
        if (_cells.TryGetValue(index, out var existing))
            _cells[index] = (existing.Before, after);
        else
            _cells[index] = (before, after);
    }

    /// <summary>把沒有實際變化的格子丟掉，避免留下空筆劃。</summary>
    public void Trim()
    {
        foreach (var key in _cells.Where(kv => kv.Value.Before == kv.Value.After).Select(kv => kv.Key).ToArray())
            _cells.Remove(key);
    }

    public void Apply(MapDocument document, bool undo)
    {
        foreach (var (index, (before, after)) in _cells)
            Write(document, index, undo ? before : after);
    }

    private void Write(MapDocument document, int index, int value)
    {
        switch (Target)
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
