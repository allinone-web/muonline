using MuAssets.Core;

namespace Client.MapEditor;

/// <summary>
/// MonoGame 編輯器的共用狀態。UI（在 <see cref="MapEditorGame"/> 裡畫）與場景
/// （<see cref="MapEditorScene"/> 裡載入世界）都讀寫這裡。
/// </summary>
/// <remarks>
/// 編輯語意本身在 <see cref="EditSession"/>（Core，零引擎相依）。這一層只加宿主的東西：
/// 相機、世界載入排程、檔案面板狀態、命令列旗標。Godot 版的編輯器會有自己的這一層，
/// 但共用同一個 <see cref="EditSession"/>。
///
/// UI 只設 <see cref="RequestedWorldIndex"/>，實際載入由場景在 Update 裡處理 ——
/// 世界的載入會碰圖形資源，必須留在主執行緒的遊戲迴圈上。
/// </remarks>
public sealed class EditorSession : EditSession
{
    public static EditorSession Current { get; } = new();

    public string DataPath { get; set; } = string.Empty;

    /// <summary>外部 authoring project；存在時只讀，任何存檔／匯出／部署都被拒絕。</summary>
    public string? ExternalProjectDirectory { get; set; }

    public bool IsExternalProjectReadOnly => ExternalProjectDirectory is not null;

    public WorldEntry[] Worlds { get; set; } = [];

    /// <summary>目前載入的 world index，尚未載入任何世界時為 null。</summary>
    public int? LoadedWorldIndex { get; set; }

    /// <summary>--world：啟動時要開哪一張圖；null 表示用預設。</summary>
    public int? StartupWorldIndex { get; set; }

    /// <summary>
    /// <c>--tile X,Y</c>：載完地圖後把相機對到這一格，而不是看全圖。
    /// </summary>
    public (int X, int Y)? StartupTile { get; set; }

    /// <summary>UI 想切到的 world index。場景處理完會清成 null。</summary>
    public int? RequestedWorldIndex { get; set; }

    public bool IsLoading { get; set; }

    public EditorCamera Camera { get; } = new();

    /// <summary>「圖層」面板正在看哪一層。</summary>
    public MapLayer VisibleLayer { get; set; } = MapLayer.Layer1;

    /// <summary>框選的起點（螢幕座標）；null 表示沒在框。</summary>
    public Microsoft.Xna.Framework.Vector2? BoxSelectStart { get; set; }

    /// <summary>框選的目前位置（螢幕座標）。</summary>
    public Microsoft.Xna.Framework.Vector2? BoxSelectCurrent { get; set; }

    /// <summary>在圖層俯視圖上拖曳生怪區時的起點；null 表示沒在拖。</summary>
    public (int X, int Y)? SpawnDragStart { get; set; }

    // ── 存檔與部署 ────────────────────────────────────────────

    /// <summary>最近一次存檔／匯出／部署的結果，顯示在檔案面板上。</summary>
    public string FileMessage { get; set; } = string.Empty;

    /// <summary>檔案操作正在進行中，避免重複觸發。</summary>
    public bool FileBusy { get; set; }

    /// <summary>--audit-objects：把每張圖都載一次，對帳物件有沒有全部活下來。</summary>
    public bool AuditObjects { get; set; }

    /// <summary>--selftest：地圖載入後跑一次編輯管線的自我測試。</summary>
    /// <summary>
    /// 正在拍黃金影像的鏡位。非 null 時相機由它獨佔（每幀套用），介面也不畫 ——
    /// 基準圖裡有一個面板寬度變了就整張紅，那種比對沒有人會留著。
    /// </summary>
    public GoldenShot? GoldenShot { get; set; }

    public bool RunSelfTest { get; set; }

    /// <summary>
    /// <c>--grass</c>：啟動時把會搖動的草打開。
    /// </summary>
    /// <remarks>
    /// 預設不開，跟遊戲一樣交給畫質預設決定（Auto 在 Apple GPU 上會解析成 Medium，而 Medium 關草）。
    /// 這個旗標只是省下「開起來再去『檢視』面板勾一次」，換草貼圖做前後對比時會用到。
    /// </remarks>
    public bool ForceGrass { get; set; }

    /// <summary><c>--grass-density N</c>：一格長幾叢草。1 = 原版。</summary>
    public int GrassDensity { get; set; } = 1;

    /// <summary><c>--grass-planes N</c>：一叢草由幾片交叉組成。1 = 平板，2 = 十字，3 = 三角。</summary>
    public int GrassPlanes { get; set; } = 1;

    /// <summary><c>--grass-distance N</c>：草的繪製距離（世界單位）。0 = 不限制。</summary>
    public float GrassDistance { get; set; }

    /// <summary><c>--grass-dense N</c>：超過這個距離退回每格一片。0 = 不分層。</summary>
    public float GrassDenseDistance { get; set; }

    /// <summary>自我測試是否全數通過；null 表示還沒跑。</summary>
    public bool? SelfTestPassed { get; set; }

    /// <summary>--export-to：地圖（含自我測試改動）載入後匯出到這個目錄。</summary>
    public string? ExportOnStartPath { get; set; }

    /// <summary>--export-openmu-to：把伺服器端資料匯出到這個目錄。</summary>
    public string? ExportOpenMuOnStartPath { get; set; }

    /// <summary>
    /// 啟動後把全部地圖的語意型別表導出到這個路徑，然後直接結束。
    /// </summary>
    /// <remarks>
    /// 為什麼要等遊戲起來才做：語意型別要實例化 <c>WorldControl</c>，
    /// 而那需要 MuGame 已經初始化 —— 在 <c>Program.cs</c> 的 CLI 階段呼叫會全部
    /// 拿到 NullReferenceException（實測 86 張圖全失敗）。
    /// </remarks>
    public string? ExportSemanticTypesOnStartPath { get; set; }

    public WorldEntry? LoadedWorld
        => LoadedWorldIndex is int index ? Worlds.FirstOrDefault(w => w.Index == index) : null;

    public void RequestWorld(int index)
    {
        if (IsLoading || LoadedWorldIndex == index)
            return;

        RequestedWorldIndex = index;
    }
}
