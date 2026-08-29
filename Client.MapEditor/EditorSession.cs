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

    public WorldEntry[] Worlds { get; set; } = [];

    /// <summary>目前載入的 world index，尚未載入任何世界時為 null。</summary>
    public int? LoadedWorldIndex { get; set; }

    /// <summary>UI 想切到的 world index。場景處理完會清成 null。</summary>
    public int? RequestedWorldIndex { get; set; }

    public bool IsLoading { get; set; }

    public EditorCamera Camera { get; } = new();

    /// <summary>「圖層」面板正在看哪一層。</summary>
    public MapLayer VisibleLayer { get; set; } = MapLayer.Layer1;

    /// <summary>在圖層俯視圖上拖曳生怪區時的起點；null 表示沒在拖。</summary>
    public (int X, int Y)? SpawnDragStart { get; set; }

    // ── 存檔與部署 ────────────────────────────────────────────

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
