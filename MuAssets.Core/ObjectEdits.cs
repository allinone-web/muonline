namespace MuAssets.Core;

/// <summary>
/// 物件的一次可還原改動：新增、刪除、變換，或以上的一整批。
/// </summary>
/// <remarks>
/// 批次是必要的而不是方便：框選之後刪掉 30 個物件、散佈筆刷一次撒 20 棵樹，
/// 那在使用者眼中是「一個動作」，撤銷就該一次還原。
/// 分成 30 筆的話要按 30 次撤銷，而且中途停手會留下半毀的狀態。
/// </remarks>
public sealed class ObjectEdit
{
    private ObjectEdit(string description, MapObjectInstance instance)
    {
        Description = description;
        Instance = instance;
    }

    public string Description { get; }

    /// <summary>主要對象；批次時是第一個，給介面聚焦用。</summary>
    public MapObjectInstance Instance { get; }

    private List<ObjectEdit>? _children;

    private bool _isAdd;
    private bool _isRemove;
    private MapObjectInstance? _before;
    private MapObjectInstance? _after;
    private int _index = -1;

    public static ObjectEdit Add(MapObjectInstance instance)
        => new("放置物件", instance) { _isAdd = true };

    public static ObjectEdit Remove(MapObjectInstance instance, int index)
        => new("刪除物件", instance) { _isRemove = true, _index = index };

    /// <summary>變換：記下改動前後的完整狀態，還原時整個換回去。</summary>
    public static ObjectEdit Transform(MapObjectInstance instance, MapObjectInstance before)
        => new("調整物件", instance) { _before = before, _after = instance.Clone() };

    /// <summary>
    /// 把好幾筆併成一筆，一次撤銷全部還原。
    /// </summary>
    /// <remarks>
    /// 撤銷時是<b>反序</b>執行的。刪除的還原是「插回原本的索引」，
    /// 正序還原會讓後面那幾筆插到錯的位置。
    /// </remarks>
    public static ObjectEdit Batch(string description, IReadOnlyList<ObjectEdit> edits)
    {
        if (edits.Count == 0)
            throw new ArgumentException("批次不能是空的", nameof(edits));

        return new(description, edits[0].Instance) { _children = [.. edits] };
    }

    /// <summary>這一筆包含幾個物件。</summary>
    public int Count => _children?.Count ?? 1;

    public void Apply(MapDocument document, bool undo)
    {
        if (_children is not null)
        {
            if (undo)
            {
                for (int i = _children.Count - 1; i >= 0; i--)
                    _children[i].Apply(document, undo: true);
            }
            else
            {
                foreach (var child in _children)
                    child.Apply(document, undo: false);
            }

            return;
        }

        if (_isAdd)
        {
            if (undo)
                document.Objects.Remove(Instance);
            else if (!document.Objects.Contains(Instance))
                document.Objects.Add(Instance);

            return;
        }

        if (_isRemove)
        {
            if (undo)
                document.Objects.Insert(Math.Clamp(_index, 0, document.Objects.Count), Instance);
            else
                document.Objects.Remove(Instance);

            return;
        }

        var source = undo ? _before : _after;
        if (source is null)
            return;

        Instance.Position = source.Position;
        Instance.Angle = source.Angle;
        Instance.Scale = source.Scale;
        Instance.Type = source.Type;
    }
}

/// <summary>物件改動的撤銷／重做堆疊，與格子類的 <see cref="EditHistory"/> 分開。</summary>
public sealed class ObjectHistory
{
    private const int MaxDepth = 200;

    private readonly List<ObjectEdit> _undo = [];
    private readonly List<ObjectEdit> _redo = [];

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public int Depth => _undo.Count;
    public string? NextUndoDescription => _undo.Count > 0 ? _undo[^1].Description : null;

    public void Push(ObjectEdit edit)
    {
        _undo.Add(edit);
        _redo.Clear();

        if (_undo.Count > MaxDepth)
            _undo.RemoveAt(0);
    }

    public ObjectEdit? Undo(MapDocument document)
    {
        if (_undo.Count == 0)
            return null;

        var edit = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        edit.Apply(document, undo: true);
        _redo.Add(edit);
        return edit;
    }

    public ObjectEdit? Redo(MapDocument document)
    {
        if (_redo.Count == 0)
            return null;

        var edit = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        edit.Apply(document, undo: false);
        _undo.Add(edit);
        return edit;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}
