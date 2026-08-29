using Client.AssetStudio.Catalog;
using ImGuiNET;
using NVector2 = System.Numerics.Vector2;

namespace Client.AssetStudio.Ui;

public sealed partial class StudioUi
{
    private string _catalogFilter = string.Empty;
    private EntityKind _kindFilter = EntityKind.Monster;
    private bool _onlyClassBound;
    private bool _onlyOrphans;
    private bool _onlyMissingTextures;
    private bool _showThumbnails = true;
    private EntityEntry[] _visible = [];
    private string _visibleKey = string.Empty;

    /// <summary>
    /// 資源目錄。左欄，工具的入口。
    /// </summary>
    /// <remarks>
    /// 這裡刻意同時列出「有類別的怪物」與「沒人引用的孤兒模型」：
    /// <c>Data/Monster</c> 有 552 個 <c>.bmd</c>，<c>Client.Main</c> 只有 401 個怪物類別。
    /// 只列前者會讓人以為資源就這麼多；只列後者會失去「這隻怪的伺服器編號是幾號」這個關鍵資訊。
    /// </remarks>
    private void DrawCatalogPanel()
    {
        PlaceWindow("資源目錄");
        ImGui.Begin("資源目錄");

        DrawCatalogToolbar();
        ImGui.Separator();

        var entries = ResolveVisible();
        ImGui.TextColored(Muted, $"{entries.Length} / {_session.Catalog.Entries.Length} 筆");

        if (ImGui.BeginChild("catalogList", new NVector2(0f, 0f)))
        {
            if (_showThumbnails)
                DrawThumbnailGrid(entries);
            else
                DrawList(entries);
        }

        ImGui.EndChild();
        ImGui.End();
    }

    private void DrawCatalogToolbar()
    {
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##catalogFilter", "搜尋名稱、類別、檔名或編號", ref _catalogFilter, 96);

        ImGui.SetNextItemWidth(150f);
        if (ImGui.BeginCombo("##kind", EntityKindNames.Of(_kindFilter)))
        {
            foreach (var kind in EntityKindNames.All)
            {
                int count = _session.Catalog.Entries.Count(e => e.Kind == kind);
                if (count == 0)
                    continue;

                if (ImGui.Selectable($"{EntityKindNames.Of(kind)} ({count})", _kindFilter == kind))
                    _kindFilter = kind;
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        ImGui.Checkbox("縮圖", ref _showThumbnails);

        ImGui.Checkbox("只顯示有類別的", ref _onlyClassBound);
        HelpMarker("有類別 = Client.Main 裡有對應的 C# 類別，因此有 [NpcInfo] 編號，"
                 + "可以對上 OpenMU 的 MonsterDefinition.Number。\n"
                 + "沒有類別的是孤兒模型：檔案在資源包裡，但遊戲程式沒有引用它。");

        ImGui.SameLine();
        ImGui.Checkbox("只顯示沒人用的", ref _onlyOrphans);
        HelpMarker("沒人用 = 整份 Client.Main 都沒有提到這個檔案。\n"
                 + "被引用但沒有自己的類別的（例如 Npc/ManUpper02.bmd 這種身體部位）不算沒人用，\n"
                 + "滑鼠移上去可以看到是誰在用它。\n\n"
                 + "要換掉一整套美術資源時，這個清單是「可以先不管」的那一堆。");

        ImGui.Checkbox("只顯示缺貼圖的", ref _onlyMissingTextures);
        HelpMarker("缺貼圖的網格在遊戲裡會被安靜地跳過不畫 —— 這正是「戰士看不到腿」那類問題的成因。\n"
                 + "這個篩選要解析每個模型的 BMD，第一次切換會停頓一下。");

        var stats = _session.Catalog.Stats;
        if (stats.MissingModels > 0 || stats.UnresolvedClasses > 0)
        {
            ImGui.TextColored(Warning,
                $"{stats.MissingModels} 個類別找不到模型檔、{stats.UnresolvedClasses} 個類別看不出模型路徑");
            HelpMarker("看不出模型路徑：那個類別的模型是執行期才決定的（例如 Prepare(item.TexturePath) 的武器），"
                     + "不是掃描失敗。");
        }

        foreach (var warning in _session.Catalog.Warnings)
            ImGui.TextColored(Warning, warning);
    }

    /// <summary>
    /// 把目錄跳到某一筆上。<c>--open</c> 與「在目錄裡找相關模型」都用它。
    /// </summary>
    /// <remarks>
    /// 選取而不捲到它等於沒選 —— 一個分類有五百多格縮圖，使用者看不出哪一格是選中的。
    /// </remarks>
    public void RevealInCatalog(EntityEntry entry)
    {
        _kindFilter = entry.Kind;
        _catalogFilter = entry.ClassName ?? entry.Name;
        _onlyClassBound = false;
        _onlyOrphans = false;
        _onlyMissingTextures = false;
        _visibleKey = string.Empty;
    }

    private EntityEntry[] ResolveVisible()
    {
        string key = $"{_kindFilter}|{_catalogFilter}|{_onlyClassBound}|{_onlyMissingTextures}|{_onlyOrphans}";
        if (key == _visibleKey)
            return _visible;

        var query = _session.Catalog.Entries.Where(e => e.Kind == _kindFilter);

        if (_onlyClassBound)
            query = query.Where(e => e.ClassName is not null);

        if (_onlyOrphans)
            query = query.Where(e => e.ClassName is null && !e.IsReferenced);

        if (!string.IsNullOrWhiteSpace(_catalogFilter))
            query = query.Where(e => e.Search.Contains(_catalogFilter, StringComparison.OrdinalIgnoreCase));

        if (_onlyMissingTextures)
            query = query.Where(e => ModelInspector.MissingTextureCount(e) > 0);

        _visible = query.ToArray();
        _visibleKey = key;
        return _visible;
    }

    private void DrawList(EntityEntry[] entries)
    {
        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY
                                    | ImGuiTableFlags.Borders | ImGuiTableFlags.SizingStretchProp;

        if (!ImGui.BeginTable("catalog", 3, flags))
            return;

        ImGui.TableSetupColumn("編號", ImGuiTableColumnFlags.WidthFixed, 48f);
        ImGui.TableSetupColumn("名稱");
        ImGui.TableSetupColumn("模型", ImGuiTableColumnFlags.WidthFixed, 110f);
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableHeadersRow();

        // 沒有用 ImGuiListClipper：類別篩選之後最多幾百到兩千列，逐列送進 ImGui
        // 的成本遠低於 clipper 那套需要 unsafe 指標的 API 帶來的出錯機會。
        foreach (var entry in entries)
            DrawRow(entry);

        ImGui.EndTable();
    }

    private void DrawRow(EntityEntry entry)
    {
        ImGui.TableNextRow();

        ImGui.TableSetColumnIndex(0);
        bool selected = _session.Selected?.Id == entry.Id;

        string label = entry.Number >= 0 ? entry.Number.ToString() : "－";
        if (ImGui.Selectable($"{label}##{entry.Id}", selected, ImGuiSelectableFlags.SpanAllColumns))
            _session.Select(entry);

        DrawEntryTooltip(entry);

        ImGui.TableSetColumnIndex(1);
        if (entry.ModelMissing)
            ImGui.TextColored(Danger, entry.Name);
        else if (entry.ClassName is null)
            ImGui.TextColored(Muted, entry.Name);
        else
            ImGui.Text(entry.Name);

        ImGui.TableSetColumnIndex(2);
        ImGui.TextColored(Muted, Path.GetFileName(entry.ModelPath));
    }

    private void DrawThumbnailGrid(EntityEntry[] entries)
    {
        const float cell = 96f;
        int perRow = Math.Max(1, (int)(ImGui.GetContentRegionAvail().X / (cell + 14f)));

        // 縮圖有每幀預算（見 ThumbnailCache），所以整份走一遍不會卡；
        // 沒畫到的格子這一幀顯示佔位方塊，下一幀補上。
        for (int i = 0; i < entries.Length; i++)
        {
            DrawThumbnailCell(entries[i], cell);

            if ((i + 1) % perRow != 0)
                ImGui.SameLine();
        }
    }

    private void DrawThumbnailCell(EntityEntry entry, float size)
    {
        ImGui.BeginGroup();
        ImGui.PushID(entry.Id);

        var id = entry.FullPath is null ? null : _thumbnails.Get(entry.FullPath);
        bool selected = _session.Selected?.Id == entry.Id;

        if (selected)
            ImGui.PushStyleColor(ImGuiCol.Button, new System.Numerics.Vector4(0.28f, 0.42f, 0.62f, 1f));

        if (id.HasValue)
        {
            if (ImGui.ImageButton("##thumb", id.Value, new NVector2(size, size)))
                _session.Select(entry);
        }
        else if (ImGui.Button(entry.ModelMissing ? "缺檔" : "…", new NVector2(size, size)))
        {
            _session.Select(entry);
        }

        if (selected)
            ImGui.PopStyleColor();

        DrawEntryTooltip(entry);

        string caption = entry.Number >= 0 ? $"{entry.Number} {entry.Name}" : entry.Name;
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + size);
        ImGui.TextColored(entry.ClassName is null ? Muted : new System.Numerics.Vector4(0.88f, 0.9f, 0.92f, 1f), caption);
        ImGui.PopTextWrapPos();

        ImGui.PopID();
        ImGui.EndGroup();
    }

    private void DrawEntryTooltip(EntityEntry entry)
    {
        if (!ImGui.IsItemHovered())
            return;

        ImGui.BeginTooltip();
        ImGui.Text(entry.Name);
        ImGui.TextColored(Muted, entry.ModelPath);

        if (entry.ClassName is not null)
        {
            ImGui.TextColored(Muted, $"類別 {entry.ClassName}　伺服器編號 {entry.Number}");

            if (_session.ServerMonsters.TryGetValue((short)entry.Number, out var row))
            {
                ImGui.Separator();
                ImGui.TextColored(Muted,
                    $"DB：{row.Designation}　移動 {row.MoveDelay.TotalMilliseconds:F0}ms　"
                  + $"攻擊 {row.AttackDelay.TotalMilliseconds:F0}ms　射程 {row.AttackRange}");
            }
        }
        else if (entry.IsReferenced)
        {
            var users = _session.Catalog.UsersOf(entry.ModelPath);
            ImGui.TextColored(Muted, users.Length == 0
                ? "被程式碼引用，但找不到是哪個類別"
                : $"被 {string.Join("、", users.Take(4))}{(users.Length > 4 ? "…" : string.Empty)} 引用");
        }
        else
        {
            ImGui.TextColored(Warning, "沒有任何類別引用這個模型");
        }

        if (entry.Attachments.Length > 0)
        {
            ImGui.Separator();
            foreach (var attachment in entry.Attachments.Take(8))
                ImGui.TextColored(Muted, "＋ " + attachment);
        }

        if (entry.ModelMissing)
            ImGui.TextColored(Danger, "模型檔不存在");

        ImGui.EndTooltip();
    }
}
