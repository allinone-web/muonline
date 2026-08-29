using Client.AssetStudio.Catalog;
using Client.AssetStudio.Rendering;
using Client.AssetStudio.Server;

namespace Client.AssetStudio;

/// <summary>
/// 工具的共用狀態。UI 面板寫入意圖，<see cref="StudioGame"/> 在主執行緒實現。
/// </summary>
/// <remarks>
/// 分成「請求」與「完成」兩個欄位，是因為載入模型會建立 <c>Texture2D</c> 與 render target，
/// 那些只能在主執行緒做。UI 是在 <c>Draw</c> 裡跑的，直接載入會在切換選取時卡住整個畫面，
/// 而且例外會從 ImGui 的繪製迴圈裡冒出來，堆疊看不出真正的原因。
/// </remarks>
public sealed class StudioSession
{
    public static StudioSession Current { get; } = new();

    public string DataPath { get; set; } = string.Empty;

    public EntityCatalog Catalog { get; } = new();

    public SkillCatalog Skills { get; } = new();

    /// <summary>道具模型 → 道具定義。把 2715 個沒有語意的檔名變成可分類的清單。</summary>
    public ItemCatalog Items { get; } = new();

    /// <summary>人工標註：哪些素材要換、換了沒有。存在 ~/.mu-studio/asset-tags.json。</summary>
    public AssetTagStore Tags { get; } = new();

    public OpenMuRepository Server { get; } = new();

    /// <summary>資料庫裡的怪物，鍵是 <c>MonsterDefinition.Number</c>。連不上時是空的。</summary>
    public Dictionary<short, MonsterRow> ServerMonsters { get; set; } = [];

    /// <summary>編輯中的複本。按「寫回」才會送出，按「還原」就丟掉。</summary>
    public Dictionary<short, MonsterRow> ServerMonsterDrafts { get; } = [];

    public Dictionary<short, SkillRow> ServerSkills { get; set; } = [];

    public Dictionary<short, List<string>> ServerSpawns { get; set; } = [];

    // ── 選取與載入 ────────────────────────────────────────────

    public EntityEntry? Selected { get; set; }

    /// <summary>UI 想開的資源。載入完成後由 <see cref="StudioGame"/> 清成 null。</summary>
    public EntityEntry? Requested { get; set; }

    public AnimatedModel? Model { get; set; }

    public bool IsLoading { get; set; }

    public string StatusMessage { get; set; } = string.Empty;

    /// <summary>最近一次操作的結果訊息（匯出、寫回、匯入…）。</summary>
    public string? ActionMessage { get; set; }

    public bool ActionFailed { get; set; }

    // ── 動畫播放 ──────────────────────────────────────────────

    public int CurrentAction { get; set; }

    public double AnimTime { get; set; }

    public bool Playing { get; set; } = true;

    /// <summary>
    /// 對應 <c>ModelObject.AnimationSpeed</c>。預設 4 是遊戲的預設值 ——
    /// 但每隻怪的實際速度是類別在 <c>Load()</c> 裡用 <c>SetActionSpeed()</c> 覆寫的，
    /// <b>那個值不在 <c>.bmd</c> 裡</b>，所以這裡只能是一個可調的基準。
    /// </summary>
    public float AnimationSpeed { get; set; } = AnimatedModel.DefaultAnimationSpeed;

    public int Frame0 { get; set; }

    public int Frame1 { get; set; }

    public float FrameBlend { get; set; }

    /// <summary>資料庫正在讀取中。UI 用它把按鈕變成「連線中…」。</summary>
    public bool ServerBusy { get; private set; }

    /// <summary>讀好但還沒套用的資料庫快照。由 <see cref="StudioGame"/> 在主執行緒套用。</summary>
    private ServerSnapshot? _pendingServer;

    private sealed record ServerSnapshot(
        Dictionary<short, MonsterRow> Monsters,
        Dictionary<short, SkillRow> Skills,
        Dictionary<short, List<string>> Spawns,
        string Message,
        bool Failed);

    /// <summary>
    /// 連上 OpenMU 的資料庫並讀進怪物、技能與生怪區。
    /// </summary>
    /// <remarks>
    /// 啟動時會自動跑一次（可用 <c>--no-db</c> 關掉）。
    /// 這個工具的核心主張是「外觀與行為要並排看」，每次開啟都要手動按一次連線
    /// 等於把那件事變成選配。連不上不是錯誤 —— 伺服器沒開著也應該能瀏覽模型。
    ///
    /// <b>讀完不直接指派。</b>這個方法跑在執行緒集區，而 UI 正在同一時間走訪
    /// <c>ServerMonsterDrafts</c> 與每一筆的 <c>Attributes</c>。
    /// 在背景 <c>Clear()</c> 一個正在被列舉的字典會丟
    /// <c>InvalidOperationException</c>，而且是在 ImGui 的繪製迴圈裡冒出來、
    /// 堆疊看不出真正的原因。所以結果先放進 <see cref="_pendingServer"/>，
    /// 由主執行緒的 <see cref="ApplyPendingServerData"/> 套用。
    /// </remarks>
    public async Task ReloadServerAsync()
    {
        if (ServerBusy)
            return;

        ServerBusy = true;

        try
        {
            var monsters = await Server.LoadMonstersAsync();
            var skills = await Server.LoadSkillsAsync();
            var spawns = await Server.LoadSpawnSummaryAsync();

            _pendingServer = new ServerSnapshot(
                monsters, skills, spawns,
                $"資料庫已讀取：怪物 {monsters.Count}、技能 {skills.Count}",
                Failed: false);
        }
        catch (Exception ex)
        {
            _pendingServer = new ServerSnapshot([], [], [], $"資料庫連線失敗：{ex.Message}", Failed: true);
        }
        finally
        {
            ServerBusy = false;
        }
    }

    /// <summary>由主執行緒每幀呼叫一次，把背景讀好的資料庫快照換上去。</summary>
    public void ApplyPendingServerData()
    {
        if (Interlocked.Exchange(ref _pendingServer, null) is not { } snapshot)
            return;

        if (!snapshot.Failed)
        {
            ServerMonsters = snapshot.Monsters;
            ServerSkills = snapshot.Skills;
            ServerSpawns = snapshot.Spawns;
            ServerMonsterDrafts.Clear();
        }

        Report(snapshot.Message, snapshot.Failed);
    }

    public void Report(string message, bool failed = false)
    {
        ActionMessage = message;
        ActionFailed = failed;
    }

    public void Select(EntityEntry entry)
    {
        if (Selected?.Id == entry.Id)
            return;

        Requested = entry;
    }

    /// <summary>取得（必要時建立）某隻怪的伺服器參數編輯複本。</summary>
    public MonsterRow? DraftFor(int number)
    {
        if (number < 0 || number > short.MaxValue)
            return null;

        short key = (short)number;

        if (ServerMonsterDrafts.TryGetValue(key, out var draft))
            return draft;

        if (!ServerMonsters.TryGetValue(key, out var original))
            return null;

        draft = original.Clone();
        ServerMonsterDrafts[key] = draft;
        return draft;
    }

    public bool HasPendingServerEdits(int number)
    {
        if (number < 0 || number > short.MaxValue)
            return false;

        short key = (short)number;

        return ServerMonsterDrafts.TryGetValue(key, out var draft)
            && ServerMonsters.TryGetValue(key, out var original)
            && Differs(original, draft);
    }

    public void DiscardDraft(int number)
    {
        if (number is >= 0 and <= short.MaxValue)
            ServerMonsterDrafts.Remove((short)number);
    }

    private static bool Differs(MonsterRow a, MonsterRow b)
    {
        if (a.Designation != b.Designation
            || a.MoveRange != b.MoveRange
            || a.AttackRange != b.AttackRange
            || a.ViewRange != b.ViewRange
            || a.MoveDelay != b.MoveDelay
            || a.AttackDelay != b.AttackDelay
            || a.RespawnDelay != b.RespawnDelay
            || a.Attribute != b.Attribute
            || a.NumberOfMaximumItemDrops != b.NumberOfMaximumItemDrops
            || a.IntelligenceTypeName != b.IntelligenceTypeName)
        {
            return true;
        }

        for (int i = 0; i < a.Attributes.Count && i < b.Attributes.Count; i++)
        {
            if (Math.Abs(a.Attributes[i].Value - b.Attributes[i].Value) > 0.0001f)
                return true;
        }

        return false;
    }
}
