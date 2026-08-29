using Client.AssetStudio.Catalog;
using Client.AssetStudio.Server;
using ImGuiNET;
using NVector2 = System.Numerics.Vector2;

namespace Client.AssetStudio.Ui;

public sealed partial class StudioUi
{
    private string _connectionString = OpenMuRepository.DefaultConnectionString;

    /// <summary>
    /// 伺服器數值。這個面板的存在理由就是那句話：<b>外觀在客戶端，行為在伺服器。</b>
    /// </summary>
    /// <remarks>
    /// 左邊看到的模型與動畫來自 <c>.bmd</c>；這裡的移動速度、攻擊速度、射程、視野、HP、傷害
    /// 全部來自 OpenMU 的 PostgreSQL，改 <c>.bmd</c> 對它們沒有任何影響。
    /// 兩者靠 <c>[NpcInfo] typeId</c> ↔ <c>MonsterDefinition.Number</c> 對上。
    /// </remarks>
    private void DrawServerPanel()
    {
        PlaceWindow("伺服器數值");
        ImGui.Begin("伺服器數值");

        DrawConnectionBar();
        ImGui.Separator();

        var entry = _session.Selected;

        if (entry is null || entry.ClassName is null || entry.Number < 0)
        {
            ImGui.TextColored(Muted, entry is null
                ? "從左邊的目錄選一個資源"
                : "這是孤兒模型，沒有 [NpcInfo] 編號，因此對不到資料庫的任何一筆");

            if (entry is not null && entry.ClassName is null)
            {
                HelpMarker("孤兒模型 = 檔案在資源包裡但沒有 C# 類別引用它。\n"
                         + "要讓它在遊戲裡出現，得先在 Client.Main 寫一個帶 [NpcInfo] 的類別，"
                         + "並在 OpenMU 建一筆對應編號的 MonsterDefinition。");
            }

            ImGui.End();
            return;
        }

        if (!_session.ServerMonsters.TryGetValue((short)entry.Number, out var original))
        {
            ImGui.TextColored(Warning, _session.Server.IsConnected
                ? $"資料庫裡沒有 Number = {entry.Number} 的 MonsterDefinition"
                : "尚未連上資料庫");

            if (_session.Server.IsConnected)
            {
                HelpMarker("客戶端有這個類別但伺服器沒有對應的定義 —— 這隻怪不會出現在遊戲裡。\n"
                         + "反過來也可能：資料庫有定義但客戶端沒有類別，那樣會出現一隻沒有模型的怪。");
            }

            ImGui.End();
            return;
        }

        var draft = _session.DraftFor(entry.Number);
        if (draft is null)
        {
            ImGui.End();
            return;
        }

        DrawMonsterEditor(original, draft);

        ImGui.End();
    }

    private void DrawConnectionBar()
    {
        var server = _session.Server;

        if (server.IsConnected)
            ImGui.TextColored(Good, $"已連線　怪物 {_session.ServerMonsters.Count} 筆、技能 {_session.ServerSkills.Count} 筆");
        else
            ImGui.TextColored(Warning, "未連線");

        ImGui.SetNextItemWidth(-110f);
        if (ImGui.InputText("##connection", ref _connectionString, 512))
            server.ConnectionString = _connectionString;

        ImGui.SameLine();
        if (ImGui.Button(_session.ServerBusy ? "連線中…" : "連線 / 重讀") && !_session.ServerBusy)
        {
            _session.Server.ConnectionString = _connectionString;
            _ = _session.ReloadServerAsync();
        }

        bool write = server.WriteEnabled;
        if (ImGui.Checkbox("允許寫入資料庫", ref write))
            server.WriteEnabled = write;

        HelpMarker("這是一個活著的遊戲資料庫，預設唯讀。\n\n"
                 + "另外：OpenMU 在啟動時把整份 GameConfiguration 讀進記憶體，\n"
                 + "所以寫回之後必須重啟 openmu-startup 容器，遊戲裡才看得到變化。");

        if (server.LastError is string error)
            ImGui.TextColored(Danger, error);
    }

    private void DrawMonsterEditor(MonsterRow original, MonsterRow draft)
    {
        bool dirty = _session.HasPendingServerEdits(draft.Number);

        ImGui.Text($"MonsterDefinition #{draft.Number}");
        ImGui.SameLine();
        if (dirty)
            ImGui.TextColored(Warning, "（有未寫回的修改）");

        string designation = draft.Designation;
        if (ImGui.InputText("名稱", ref designation, 128))
            draft.Designation = designation;

        if (!string.Equals(original.Designation, _session.Selected?.Name, StringComparison.OrdinalIgnoreCase))
        {
            ImGui.TextColored(Muted, $"客戶端顯示名稱：{_session.Selected?.Name}");
            HelpMarker("兩邊的名字不一致不是錯誤 —— 客戶端的名字來自 [NpcInfo] 的第二個參數，"
                     + "伺服器的來自資料庫。玩家看到的是伺服器送來的那個。");
        }

        ImGui.Separator();

        DragShort("移動範圍", draft.MoveRange, v => draft.MoveRange = v, 0, 30,
            "怪物閒晃時離出生點最遠幾格。");

        DragShort("攻擊射程", draft.AttackRange, v => draft.AttackRange = v, 0, 30,
            "≥ 3 的怪會被當成遠程：太近時會後退保持距離（風箏），見 docs/怪物AI-實作與維護.md。");

        DragShort("視野範圍", draft.ViewRange, v => draft.ViewRange = v, 0, 40,
            "多遠會發現玩家。");

        DragMilliseconds("移動間隔", draft.MoveDelay, v => draft.MoveDelay = v, 0, 5000,
            "走一格要多久。玩家大約每 400ms 一格。");

        DragMilliseconds("攻擊間隔", draft.AttackDelay, v => draft.AttackDelay = v, 0, 10000,
            "兩次出手的最短間隔。這是「怪物 DPS」最直接的旋鈕。\n"
          + "注意它已經不再是思考頻率 —— 思考已改為 100ms 的排程器（HANDOFF 4.5 節）。");

        DragMilliseconds("重生間隔", draft.RespawnDelay, v => draft.RespawnDelay = v, 0, 600000,
            "死亡到再次出現的時間。");

        int drops = draft.NumberOfMaximumItemDrops;
        if (ImGui.DragInt("最多掉落件數", ref drops, 0.1f, 0, 10))
            draft.NumberOfMaximumItemDrops = drops;

        ImGui.TextColored(Muted, $"AI 類別　{draft.IntelligenceTypeName ?? "（預設 BasicMonsterIntelligence）"}");

        ImGui.Separator();
        ImGui.Text("屬性");
        HelpMarker("HP、等級、傷害、防禦存在 MonsterAttribute 這張表，一筆一個 AttributeDefinition。");

        if (ImGui.BeginTable("attributes", 2,
                ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.SizingStretchProp,
                new NVector2(0f, 150f)))
        {
            ImGui.TableSetupColumn("屬性");
            ImGui.TableSetupColumn("值", ImGuiTableColumnFlags.WidthFixed, 120f);

            foreach (var attribute in draft.Attributes)
            {
                ImGui.TableNextRow();

                ImGui.TableSetColumnIndex(0);
                ImGui.Text(attribute.Designation);

                ImGui.TableSetColumnIndex(1);
                ImGui.PushID(attribute.Id.ToString());
                ImGui.SetNextItemWidth(-1f);

                float value = attribute.Value;
                if (ImGui.DragFloat("##value", ref value, MathF.Max(1f, MathF.Abs(value) * 0.01f)))
                    attribute.Value = value;

                ImGui.PopID();
            }

            ImGui.EndTable();
        }

        if (_session.ServerSpawns.TryGetValue(draft.Number, out var spawns))
        {
            ImGui.Separator();
            ImGui.TextColored(Muted, "出現於：");
            foreach (var line in spawns)
                ImGui.TextColored(Muted, "　" + line);
        }

        ImGui.Separator();

        if (!_session.Server.WriteEnabled)
            ImGui.BeginDisabled();

        if (ImGui.Button("寫回資料庫") && dirty)
            _ = SaveMonsterAsync(draft);

        if (!_session.Server.WriteEnabled)
            ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button("還原"))
            _session.DiscardDraft(draft.Number);

        ImGui.SameLine();
        ImGui.TextColored(Muted, "寫回後需重啟 openmu-startup 容器");
    }

    private async Task SaveMonsterAsync(MonsterRow draft)
    {
        try
        {
            await _session.Server.SaveMonsterAsync(draft);
            _session.ServerMonsters[draft.Number] = draft.Clone();
            _session.DiscardDraft(draft.Number);

            _session.Report($"#{draft.Number} {draft.Designation} 已寫回。重啟 openmu-startup 後生效。");
        }
        catch (Exception ex)
        {
            _session.Report($"寫回失敗：{ex.Message}", failed: true);
        }
    }

    // ImGui 的 DragInt 只吃 int，而資料庫欄位是 smallint 與 interval。
    // 用 getter + setter 而不是 ref：MonsterRow 是自動屬性，取不到位址。
    private static void DragShort(string label, short value, Action<short> setter, int min, int max, string help)
    {
        int temporary = value;
        if (ImGui.DragInt(label, ref temporary, 0.1f, min, max))
            setter((short)Math.Clamp(temporary, min, max));

        HelpMarker(help);
    }

    private static void DragMilliseconds(string label, TimeSpan value, Action<TimeSpan> setter, int min, int max, string help)
    {
        int milliseconds = (int)value.TotalMilliseconds;
        if (ImGui.DragInt(label, ref milliseconds, 5f, min, max, "%d ms"))
            setter(TimeSpan.FromMilliseconds(Math.Clamp(milliseconds, min, max)));

        HelpMarker(help);
    }
}
