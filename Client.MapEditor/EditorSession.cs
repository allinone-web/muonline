using Client.Data.ATT;

namespace Client.MapEditor;

/// <summary>
/// 編輯器的共用狀態。UI（在 <see cref="MapEditorGame"/> 裡畫）與場景
/// （<see cref="MapEditorScene"/> 裡載入世界）都讀寫這裡。
/// </summary>
/// <remarks>
/// UI 只設 <see cref="RequestedWorldIndex"/>，實際載入由場景在 Update 裡處理 ——
/// 世界的載入會碰圖形資源，必須留在主執行緒的遊戲迴圈上。
/// </remarks>
public sealed class EditorSession
{
    public static EditorSession Current { get; } = new();

    public string DataPath { get; set; } = string.Empty;

    public WorldEntry[] Worlds { get; set; } = [];

    /// <summary>目前載入的 world index，尚未載入任何世界時為 null。</summary>
    public int? LoadedWorldIndex { get; set; }

    /// <summary>UI 想切到的 world index。場景處理完會清成 null。</summary>
    public int? RequestedWorldIndex { get; set; }

    public bool IsLoading { get; set; }

    public string StatusMessage { get; set; } = string.Empty;

    public EditorCamera Camera { get; } = new();

    /// <summary>目前這張圖的可編輯資料。與畫面上的世界分開，見 <see cref="MapDocument"/>。</summary>
    public MapDocument? Document { get; set; }

    /// <summary>「圖層」面板正在看哪一層。</summary>
    public MapLayer VisibleLayer { get; set; } = MapLayer.Layer1;

    /// <summary>圖層貼圖需要重建（切圖或切層之後）。</summary>
    public bool LayerViewDirty { get; set; } = true;

    // ── 編輯工具 ──────────────────────────────────────────────

    public EditorToolKind Tool { get; set; } = EditorToolKind.None;

    /// <summary>放置與選取時，隨機化用的亂數來源。</summary>
    public Random Random { get; } = new(20260829);
    public Brush Brush { get; } = new();
    public EditHistory History { get; } = new();

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

    // ── 物件工具 ──────────────────────────────────────────────

    public ObjectHistory ObjectHistory { get; } = new();

    /// <summary>放置工具要放哪一種物件（.obj 的 type）。</summary>
    public short PlaceObjectType { get; set; }

    /// <summary>目前選中的物件，沒有選取時為 null。</summary>
    public MapObjectInstance? SelectedObject { get; set; }

    /// <summary>物件清單有變動、還沒同步到畫面上的世界。</summary>
    public bool ObjectsDirty { get; set; }

    /// <summary>放置時自動貼齊格子中心。</summary>
    public bool SnapToTile { get; set; } = true;

    /// <summary>放置時的隨機旋轉範圍（度），0 = 不隨機。</summary>
    public float PlaceRandomYaw { get; set; }

    /// <summary>放置時的縮放隨機比例，0 = 不隨機。</summary>
    public float PlaceRandomScale { get; set; }

    /// <summary>目前這一筆還沒結束的筆劃（滑鼠按著拖曳的期間）。</summary>
    public EditStroke? ActiveStroke { get; set; }

    /// <summary>文件被改過、還沒推進渲染端。</summary>
    public bool TerrainDirty { get; set; }

    /// <summary>存檔後清空；用來提示有未儲存的變更。</summary>
    public bool HasUnsavedChanges { get; set; }

    // ── 生怪與 NPC ────────────────────────────────────────────

    public MonsterCatalog NpcCatalog { get; } = MonsterCatalog.Load();

    /// <summary>目前選中的生怪區。</summary>
    public SpawnArea? SelectedSpawn { get; set; }

    /// <summary>放置生怪區時要用哪一種怪物／NPC。</summary>
    public ushort SpawnTypeId { get; set; }

    /// <summary>在圖層俯視圖上拖曳生怪區時的起點；null 表示沒在拖。</summary>
    public (int X, int Y)? SpawnDragStart { get; set; }

    // ── 校驗 ──────────────────────────────────────────────────

    public List<ValidationIssue> Issues { get; set; } = [];

    /// <summary>校驗結果過期了（地圖或編輯有變動）。</summary>
    public bool IssuesStale { get; set; } = true;

    // ── 存檔與部署 ────────────────────────────────────────────

    public EditorSettings Settings { get; } = EditorSettings.Load();

    /// <summary>每張圖自訂的貼圖索引對應。見 <see cref="TextureMappingStore"/>。</summary>
    public TextureMappingStore TextureMappings { get; } = new(
        Path.Combine(EditorSettings.ConfigDirectory, "texture-mappings.json"));

    /// <summary>最近一次存檔／匯出／部署的結果，顯示在檔案面板上。</summary>
    public string FileMessage { get; set; } = string.Empty;

    /// <summary>檔案操作正在進行中，避免重複觸發。</summary>
    public bool FileBusy { get; set; }

    /// <summary>--selftest：地圖載入後跑一次編輯管線的自我測試。</summary>
    public bool RunSelfTest { get; set; }

    /// <summary>自我測試是否全數通過；null 表示還沒跑。</summary>
    public bool? SelfTestPassed { get; set; }

    /// <summary>--export-to：地圖（含自我測試改動）載入後匯出到這個目錄。</summary>
    public string? ExportOnStartPath { get; set; }

    /// <summary>--export-openmu-to：把伺服器端資料匯出到這個目錄。</summary>
    public string? ExportOpenMuOnStartPath { get; set; }

    public WorldEntry? LoadedWorld
        => LoadedWorldIndex is int index ? Worlds.FirstOrDefault(w => w.Index == index) : null;

    public void RequestWorld(int index)
    {
        if (IsLoading || LoadedWorldIndex == index)
            return;

        RequestedWorldIndex = index;
    }
}
