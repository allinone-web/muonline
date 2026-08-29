namespace Client.MapEditor;

/// <summary>物件的一次可還原改動：新增、刪除，或變換。</summary>
public sealed class ObjectEdit
{
    private ObjectEdit(string description, MapObjectInstance instance)
    {
        Description = description;
        Instance = instance;
    }

    public string Description { get; }
    public MapObjectInstance Instance { get; }

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

    public void Apply(MapDocument document, bool undo)
    {
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
