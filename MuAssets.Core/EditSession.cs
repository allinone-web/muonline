using Client.Data.ATT;

namespace MuAssets.Core;

/// <summary>
/// 一次編輯工作階段的狀態與操作。**零引擎相依**。
/// </summary>
/// <remarks>
/// 這裡放的是「改地圖」這件事本身：文件、筆刷設定、兩條歷史、選取、生怪區、髒標記。
/// 不含相機、視窗、載入進度那些跟宿主綁定的東西 —— 那些在
/// <c>Client.MapEditor.EditorSession</c>（MonoGame）或未來的 Godot 版本裡。
///
/// 這樣切的理由是 T1 的分層規則：同一套編輯語意要能被 MonoGame 編輯器、
/// Godot 編輯器與無頭 CLI 共用。<see cref="EditPipelineSelfTest"/> 就是靠這一點
/// 才能不開視窗就跑完整條管線。
///
/// 這裡的方法只改資料、設髒標記；把改動推進渲染端是宿主的事
/// （MonoGame 版看 <c>TerrainDirty</c> / <c>ObjectsDirty</c>）。
/// </remarks>
public class EditSession
{
    /// <summary>目前這張圖的可編輯資料。</summary>
    public MapDocument? Document { get; set; }

    /// <summary>最近一次操作的結果描述，宿主拿去顯示在狀態列。</summary>
    public string StatusMessage { get; set; } = string.Empty;

    // ── 編輯工具 ──────────────────────────────────────────────

    /// <summary>筆刷工具的設定。</summary>
    public ToolSettings Tools { get; } = new();

    public EditorToolKind Tool { get => Tools.Tool; set => Tools.Tool = value; }
    public Brush Brush => Tools.Brush;
    public byte PaintTileIndex { get => Tools.PaintTileIndex; set => Tools.PaintTileIndex = value; }
    public bool PaintLayer2AsEmpty { get => Tools.PaintLayer2AsEmpty; set => Tools.PaintLayer2AsEmpty = value; }
    public bool AutoTransition { get => Tools.AutoTransition; set => Tools.AutoTransition = value; }
    public float PaintAlphaValue { get => Tools.PaintAlphaValue; set => Tools.PaintAlphaValue = value; }
    public HeightMode HeightMode { get => Tools.HeightMode; set => Tools.HeightMode = value; }
    public float HeightStep { get => Tools.HeightStep; set => Tools.HeightStep = value; }
    public float FlattenTarget { get => Tools.FlattenTarget; set => Tools.FlattenTarget = value; }
    public TWFlags AttributeFlag { get => Tools.AttributeFlag; set => Tools.AttributeFlag = value; }
    public bool AttributeErase { get => Tools.AttributeErase; set => Tools.AttributeErase = value; }
    public LightMode LightMode { get => Tools.LightMode; set => Tools.LightMode = value; }

    /// <summary>放置與選取時，隨機化用的亂數來源。固定種子，測試才可重現。</summary>
    public Random Random { get; } = new(20260829);

    public EditHistory History { get; } = new();

    /// <summary>目前這一筆還沒結束的筆劃（滑鼠按著拖曳的期間）。</summary>
    public EditStroke? ActiveStroke { get; set; }

    // ── 物件工具 ──────────────────────────────────────────────

    public ObjectHistory ObjectHistory { get; } = new();

    /// <summary>放置工具要放哪一種物件（.obj 的 type）。實際存在 <see cref="Tools"/>。</summary>
    public short PlaceObjectType { get => Tools.PlaceObjectType; set => Tools.PlaceObjectType = value; }

    /// <summary>目前選中的物件們。單選就是只有一個。</summary>
    public List<MapObjectInstance> SelectedObjects { get; } = [];

    /// <summary>
    /// 主要選取對象 —— 手柄畫在它身上，屬性面板顯示它。沒有選取時為 null。
    /// </summary>
    /// <remarks>
    /// 設定它等於「只選這一個」。多選是後來才加的，這樣既有的呼叫端不必全改，
    /// 而且「主要對象」本來就是多選介面需要的概念（手柄總得畫在某一個身上）。
    /// </remarks>
    public MapObjectInstance? SelectedObject
    {
        get => SelectedObjects.Count > 0 ? SelectedObjects[0] : null;
        set
        {
            SelectedObjects.Clear();

            if (value is not null)
                SelectedObjects.Add(value);
        }
    }

    /// <summary>放置時自動貼齊格子中心。</summary>
    public bool SnapToTile { get; set; } = true;

    /// <summary>放置時的隨機旋轉範圍（度），0 = 不隨機。</summary>
    public float PlaceRandomYaw { get => Tools.PlaceRandomYaw; set => Tools.PlaceRandomYaw = value; }

    /// <summary>放置時的縮放隨機比例，0 = 不隨機。</summary>
    public float PlaceRandomScale { get => Tools.PlaceRandomScale; set => Tools.PlaceRandomScale = value; }

    /// <summary>散佈筆刷一筆撒幾個。</summary>
    public int ScatterCount { get => Tools.ScatterCount; set => Tools.ScatterCount = value; }

    /// <summary>散佈的最小間距（格）。</summary>
    public float ScatterSpacing { get => Tools.ScatterSpacing; set => Tools.ScatterSpacing = value; }

    /// <summary>散佈時避開不可走／水的格子。</summary>
    public bool ScatterAvoidBlocked { get => Tools.ScatterAvoidBlocked; set => Tools.ScatterAvoidBlocked = value; }

    // ── 生怪與 NPC ────────────────────────────────────────────

    public MonsterCatalog NpcCatalog { get; } = MonsterCatalog.Load();

    /// <summary>目前選中的生怪區。</summary>
    public SpawnArea? SelectedSpawn { get; set; }

    /// <summary>放置生怪區時要用哪一種怪物／NPC。</summary>
    public ushort SpawnTypeId { get; set; }

    // ── 校驗 ──────────────────────────────────────────────────

    public List<ValidationIssue> Issues { get; set; } = [];

    /// <summary>校驗結果過期了（地圖或編輯有變動）。</summary>
    public bool IssuesStale { get; set; } = true;

    // ── 髒標記（宿主每幀讀，讀完自己清） ──────────────────────

    /// <summary>地形資料被改過、還沒推進渲染端。</summary>
    public bool TerrainDirty { get; set; }

    /// <summary>光照被改過。與地形分開，因為推進渲染端的成本不同。</summary>
    public bool LightDirty { get; set; }

    /// <summary>圖層貼圖需要重建（切圖、切層或編輯之後）。</summary>
    public bool LayerViewDirty { get; set; } = true;

    /// <summary>物件清單有變動、還沒同步到畫面上的世界。</summary>
    public bool ObjectsDirty { get; set; }

    /// <summary>存檔後清空；用來提示有未儲存的變更。</summary>
    public bool HasUnsavedChanges { get; set; }

    // ── 設定 ──────────────────────────────────────────────────

    public EditorSettings Settings { get; } = EditorSettings.Load();

    /// <summary>每張圖自訂的貼圖索引對應。見 <see cref="TextureMappingStore"/>。</summary>
    public TextureMappingStore TextureMappings { get; } = new(
        Path.Combine(EditorSettings.ConfigDirectory, "texture-mappings.json"));

    // ── 地形編輯 ──────────────────────────────────────────────

    /// <summary>套一次完整筆劃：下筆、施加、放開（放開才進歷史）。程式化編輯與測試用。</summary>
    public void ApplyStroke(int tileX, int tileY)
    {
        if (Document is null)
            return;

        var stroke = new EditStroke(EditorTools.TargetOf(Tool), EditorTools.DescriptionOf(Tool));
        EditorTools.Apply(Tools, Document, stroke, tileX, tileY);

        History.Push(stroke);
        TerrainDirty = true;

        if (Tool == EditorToolKind.PaintLight)
            LightDirty = true;
        LayerViewDirty = true;
        HasUnsavedChanges = true;
    }

    public void Undo()
    {
        if (Document is null)
            return;

        var stroke = History.Undo(Document);
        if (stroke is null)
            return;

        TerrainDirty = true;
        LayerViewDirty = true;
        StatusMessage = $"已撤銷：{stroke.Description}（{stroke.CellCount} 格）";
    }

    public void Redo()
    {
        if (Document is null)
            return;

        var stroke = History.Redo(Document);
        if (stroke is null)
            return;

        TerrainDirty = true;
        LayerViewDirty = true;
        StatusMessage = $"已重做：{stroke.Description}（{stroke.CellCount} 格）";
    }

    // ── 物件編輯 ──────────────────────────────────────────────

    /// <summary>刪掉所有選中的物件，算一次撤銷。</summary>
    public void DeleteSelectedObject()
    {
        var document = Document;

        if (document is null || SelectedObjects.Count == 0)
            return;

        var edits = new List<ObjectEdit>();

        // 由後往前刪：先刪前面的會讓後面那些的索引全部往前移，
        // 記下來的索引就對不上了，撤銷會插回錯的位置。
        foreach (var instance in SelectedObjects
            .Select(o => (Instance: o, Index: document.Objects.IndexOf(o)))
            .Where(x => x.Index >= 0)
            .OrderByDescending(x => x.Index))
        {
            document.Objects.RemoveAt(instance.Index);
            edits.Add(ObjectEdit.Remove(instance.Instance, instance.Index));
        }

        if (edits.Count == 0)
            return;

        ObjectHistory.Push(edits.Count == 1
            ? edits[0]
            : ObjectEdit.Batch($"刪除 {edits.Count} 個物件", edits));

        StatusMessage = edits.Count == 1
            ? $"刪除 type {edits[0].Instance.Type}"
            : $"刪除 {edits.Count} 個物件";

        SelectedObjects.Clear();
        ObjectsDirty = true;
        HasUnsavedChanges = true;
    }

    /// <summary>
    /// 選取一個矩形範圍內的物件（格子座標，兩個角落任意順序）。
    /// </summary>
    /// <param name="additive">true 表示加進現有選取，而不是取代。</param>
    public int SelectInRectangle(int ax, int ay, int bx, int by, bool additive = false)
    {
        var document = Document;
        if (document is null)
            return 0;

        int minX = Math.Min(ax, bx);
        int maxX = Math.Max(ax, bx);
        int minY = Math.Min(ay, by);
        int maxY = Math.Max(ay, by);

        if (!additive)
            SelectedObjects.Clear();

        int added = 0;

        foreach (var instance in document.Objects)
        {
            if (instance.TileX < minX || instance.TileX > maxX
                || instance.TileY < minY || instance.TileY > maxY)
            {
                continue;
            }

            if (SelectedObjects.Contains(instance))
                continue;

            SelectedObjects.Add(instance);
            added++;
        }

        StatusMessage = SelectedObjects.Count == 0
            ? "框選範圍內沒有物件"
            : $"選取 {SelectedObjects.Count} 個物件";

        return added;
    }

    public void CommitObjectTransform(MapObjectInstance instance, MapObjectInstance before)
    {
        ObjectHistory.Push(ObjectEdit.Transform(instance, before));
        ObjectsDirty = true;
        HasUnsavedChanges = true;
    }

    public void UndoObject()
    {
        if (Document is null)
            return;

        var edit = ObjectHistory.Undo(Document);
        if (edit is null)
            return;

        SelectedObject = null;
        ObjectsDirty = true;
        StatusMessage = $"已撤銷：{edit.Description}";
    }

    public void RedoObject()
    {
        if (Document is null)
            return;

        var edit = ObjectHistory.Redo(Document);
        if (edit is null)
            return;

        ObjectsDirty = true;
        StatusMessage = $"已重做：{edit.Description}";
    }

    // ── 生怪區 ────────────────────────────────────────────────

    /// <summary>
    /// 在一格附近撒一批物件，整批算一次撤銷。
    /// </summary>
    public int ScatterAt(int tileX, int tileY)
    {
        var document = Document;
        if (document is null)
            return 0;

        var placed = ScatterBrush.Scatter(Tools, document, tileX, tileY, Random, document.Objects);

        if (placed.Count == 0)
        {
            StatusMessage = "這一帶撒不下去（間距太大或都是不可走的格子）";
            return 0;
        }

        var edits = new List<ObjectEdit>(placed.Count);

        foreach (var instance in placed)
        {
            document.Objects.Add(instance);
            edits.Add(ObjectEdit.Add(instance));
        }

        ObjectHistory.Push(edits.Count == 1
            ? edits[0]
            : ObjectEdit.Batch($"散佈 {edits.Count} 個物件", edits));

        ObjectsDirty = true;
        HasUnsavedChanges = true;
        StatusMessage = $"撒了 {placed.Count} 個 type {PlaceObjectType}";

        return placed.Count;
    }

    // ── 區塊複製貼上 ──────────────────────────────────────────

    /// <summary>剪貼簿裡的區塊；還沒複製過就是 null。</summary>
    public MapBlock? Clipboard { get; private set; }

    /// <summary>複製時要不要連物件一起。</summary>
    public bool ClipboardIncludesObjects { get; set; } = true;

    public void CopyRegion(int ax, int ay, int bx, int by)
    {
        var document = Document;
        if (document is null)
            return;

        Clipboard = MapClipboard.Copy(document, ax, ay, bx, by, ClipboardIncludesObjects);

        StatusMessage =
            $"複製 {Clipboard.Width}×{Clipboard.Height} 格" +
            (Clipboard.Objects.Count > 0 ? $"、{Clipboard.Objects.Count} 個物件" : string.Empty);
    }

    /// <summary>
    /// 把剪貼簿貼到某一格（左上角對齊）。
    /// </summary>
    /// <remarks>
    /// 地形與物件的歷史是分開的兩條，所以一次貼上會是兩次撤銷。
    /// 這件事寫在狀態列上 —— 不然使用者按一次撤銷會看到地形回去了、物件還在。
    /// </remarks>
    public void PasteAt(int tileX, int tileY)
    {
        var document = Document;

        if (document is null || Clipboard is null)
            return;

        var stroke = new EditStroke(EditTarget.Layer1, $"貼上 {Clipboard.Width}×{Clipboard.Height}");
        var pasted = MapClipboard.Paste(document, Clipboard, tileX, tileY, stroke, ClipboardIncludesObjects);

        if (!stroke.IsEmpty)
        {
            History.Push(stroke);
            TerrainDirty = true;
            LightDirty = true;
            LayerViewDirty = true;
        }

        if (pasted.Count > 0)
        {
            var edits = new List<ObjectEdit>(pasted.Count);

            foreach (var instance in pasted)
            {
                document.Objects.Add(instance);
                edits.Add(ObjectEdit.Add(instance));
            }

            ObjectHistory.Push(edits.Count == 1
                ? edits[0]
                : ObjectEdit.Batch($"貼上 {edits.Count} 個物件", edits));

            ObjectsDirty = true;
        }

        HasUnsavedChanges = true;
        IssuesStale = true;

        StatusMessage = pasted.Count > 0
            ? $"貼上 {stroke.CellCount} 格與 {pasted.Count} 個物件（地形與物件各算一次撤銷）"
            : $"貼上 {stroke.CellCount} 格";
    }

    /// <summary>用兩個角落建一個生怪區，種類取自 <see cref="SpawnTypeId"/>。</summary>
    public SpawnArea? AddSpawnArea(int startX, int startY, int endX, int endY)
    {
        var document = Document;
        if (document is null)
            return null;

        var area = SpawnArea.FromCorners(startX, startY, endX, endY);
        area.TypeId = SpawnTypeId;
        area.Name = NpcCatalog.Entries.FirstOrDefault(e => e.TypeId == area.TypeId)?.Name ?? string.Empty;

        // 面積越大預設放越多隻，但至少一隻。
        area.Quantity = (short)Math.Clamp(area.Width * area.Height / 40, 1, 60);

        document.Spawns.Add(area);

        SelectedSpawn = area;
        IssuesStale = true;
        HasUnsavedChanges = true;
        StatusMessage =
            $"新增生怪區 {area.Name}（{area.X1},{area.Y1}）-（{area.X2},{area.Y2}）× {area.Quantity}";

        return area;
    }
}
