using Client.AssetStudio.Catalog;
using ImGuiNET;
using NVector2 = System.Numerics.Vector2;
using NVector4 = System.Numerics.Vector4;

namespace Client.AssetStudio.Ui;

public sealed partial class StudioUi
{
    private string _catalogFilter = string.Empty;
    private EntityKind _kindFilter = EntityKind.Monster;
    private string _groupFilter = string.Empty;
    private AssetTag _tagFilter = AssetTag.None;
    private bool _filterByTag;
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

    /// <summary>
    /// 目錄的工具列。
    /// </summary>
    /// <remarks>
    /// 面板預設只有 330 寬，而這裡有兩層分類、四個布林篩選、標註篩選與進度。
    /// 全部攤平會互相擠到看不見文字，所以：<b>每天都會用到的放最上面且佔滿寬度</b>
    /// （搜尋、大分類、子分類），其餘收進兩個可摺疊的區塊。
    /// </remarks>
    private void DrawCatalogToolbar()
    {
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##catalogFilter", "搜尋名稱、類別、檔名或編號", ref _catalogFilter, 96);

        float half = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) * 0.5f;

        ImGui.SetNextItemWidth(half);
        if (ImGui.BeginCombo("##kind", EntityKindNames.Of(_kindFilter)))
        {
            foreach (var kind in EntityKindNames.All)
            {
                int count = _session.Catalog.Entries.Count(e => e.Kind == kind);
                if (count == 0)
                    continue;

                if (ImGui.Selectable($"{EntityKindNames.Of(kind)} ({count})", _kindFilter == kind))
                {
                    _kindFilter = kind;

                    // 子分類是跟著大分類走的。不清掉的話切過去會篩出零筆，
                    // 看起來像「這一類是空的」。
                    _groupFilter = string.Empty;
                    _visibleKey = string.Empty;
                }
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(half);

        // 第二層：語意分類。第一層是「檔案在哪個資料夾」，那是結構不是語意 ——
        // 道具全部擠在一個資料夾裡，沒有這一層等於沒有分類。
        var groups = _session.Catalog.GroupsOf(_kindFilter);

        if (groups.Length > 0)
        {
            if (ImGui.BeginCombo("##group", _groupFilter.Length == 0 ? "全部子分類" : _groupFilter))
            {
                if (ImGui.Selectable("全部子分類", _groupFilter.Length == 0))
                {
                    _groupFilter = string.Empty;
                    _visibleKey = string.Empty;
                }

                foreach (var group in groups)
                {
                    int count = _session.Catalog.OfKind(_kindFilter).Count(e => e.Group == group);

                    if (ImGui.Selectable($"{group} ({count})", _groupFilter == group))
                    {
                        _groupFilter = group;
                        _visibleKey = string.Empty;
                    }
                }

                ImGui.EndCombo();
            }
        }
        else
        {
            ImGui.TextDisabled("（無子分類）");
        }

        if (ImGui.CollapsingHeader("篩選"))
            DrawAdvancedFilters();

        if (ImGui.CollapsingHeader("替換進度"))
            DrawTagToolbar();

        ImGui.Checkbox("縮圖", ref _showThumbnails);

        var stats = _session.Catalog.Stats;
        if (stats.MissingModels > 0 || stats.UnresolvedClasses > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(Warning, $"{stats.MissingModels} 個類別缺模型");
            HelpMarker($"{stats.MissingModels} 個類別找不到模型檔、"
                     + $"{stats.UnresolvedClasses} 個類別看不出模型路徑。\n"
                     + "後者是那個類別的模型在執行期才決定（例如 Prepare(item.TexturePath) 的武器），"
                     + "不是掃描失敗。\n"
                     + "完整清單：MuAssetStudio --report");
        }

        foreach (var warning in _session.Catalog.Warnings)
            ImGui.TextColored(Warning, warning);
    }

    private void DrawAdvancedFilters()
    {
        if (ImGui.Checkbox("只顯示有類別的", ref _onlyClassBound))
            _visibleKey = string.Empty;

        HelpMarker("有類別 = Client.Main 裡有對應的 C# 類別，因此有 [NpcInfo] 編號，"
                 + "可以對上 OpenMU 的 MonsterDefinition.Number。");

        if (ImGui.Checkbox("只顯示沒人用的", ref _onlyOrphans))
            _visibleKey = string.Empty;

        HelpMarker("沒人用 = 整份 Client.Main 都沒有提到這個檔案。\n"
                 + "被引用但沒有自己的類別的（例如 Npc/ManUpper02.bmd 這種身體部位）不算沒人用，\n"
                 + "滑鼠移上去可以看到是誰在用它。\n\n"
                 + "要換掉一整套美術資源時，這個清單是「可以先不管」的那一堆。");

        if (ImGui.Checkbox("只顯示缺貼圖的", ref _onlyMissingTextures))
            _visibleKey = string.Empty;

        HelpMarker("缺貼圖的網格在遊戲裡會被安靜地跳過不畫 —— 這正是「戰士看不到腿」那類問題的成因。\n"
                 + "這個篩選要解析每個模型的 BMD，第一次切換會停頓一下。");
    }

    /// <summary>切到某個大分類。給 <c>--kind</c> 用（自動化截圖要能驗到縮圖牆）。</summary>
    public bool SelectKind(string name)
    {
        foreach (var kind in EntityKindNames.All)
        {
            if (!EntityKindNames.Of(kind).Equals(name, StringComparison.OrdinalIgnoreCase)
                && !kind.ToString().Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            _kindFilter = kind;
            _catalogFilter = string.Empty;
            _groupFilter = string.Empty;
            _visibleKey = string.Empty;
            return true;
        }

        return false;
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
        _catalogFilter = entry.ClassName ?? Path.GetFileNameWithoutExtension(entry.ModelPath);
        _groupFilter = string.Empty;
        _filterByTag = false;
        _onlyClassBound = false;
        _onlyOrphans = false;
        _onlyMissingTextures = false;
        _visibleKey = string.Empty;
    }

    /// <summary>
    /// 替換進度。這一列是「完全成為自己的遊戲」這件事唯一看得到的度量。
    /// </summary>
    private void DrawTagToolbar()
    {
        var tags = _session.Tags;

        int toReplace = tags.CountOf(AssetTag.ToReplace);
        int replaced = tags.CountOf(AssetTag.Replaced);
        int keep = tags.CountOf(AssetTag.Keep);
        int unused = tags.CountOf(AssetTag.Unused);

        ImGui.TextColored(Muted, $"標註：待替換 {toReplace}　已替換 {replaced}　保留 {keep}　不使用 {unused}");
        HelpMarker("在清單或縮圖上按右鍵可以標註。標註存在 " + tags.Path + "，\n"
                 + "與遊戲資源分開 —— 資源包重灌不會把標註洗掉。");

        if (toReplace + replaced > 0)
        {
            float progress = replaced / (float)(toReplace + replaced);
            ImGui.ProgressBar(progress, new NVector2(-1f, 14f), $"{replaced} / {toReplace + replaced}");
        }

        if (tags.LastError is string error)
            ImGui.TextColored(Danger, error);

        ImGui.Checkbox("只顯示這個標註", ref _filterByTag);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(130f);

        if (ImGui.BeginCombo("##tagFilter", AssetTagNames.Of(_tagFilter)))
        {
            foreach (var tag in AssetTagNames.All)
            {
                if (ImGui.Selectable(AssetTagNames.Of(tag), _tagFilter == tag))
                {
                    _tagFilter = tag;
                    _visibleKey = string.Empty;
                }
            }

            ImGui.EndCombo();
        }
    }

    /// <summary>右鍵選單：標註這個資源。</summary>
    private void DrawTagContextMenu(EntityEntry entry)
    {
        if (!ImGui.BeginPopupContextItem("tag"))
            return;

        ImGui.TextColored(Muted, entry.ModelPath);
        ImGui.Separator();

        var current = _session.Tags.TagOf(entry.ModelPath);

        foreach (var tag in AssetTagNames.All)
        {
            if (ImGui.MenuItem(AssetTagNames.Of(tag), string.Empty, current == tag))
            {
                _session.Tags.SetTag(entry.ModelPath, tag);
                _visibleKey = string.Empty;
            }
        }

        ImGui.EndPopup();
    }

    private static NVector4 TagColor(AssetTag tag) => tag switch
    {
        AssetTag.ToReplace => new NVector4(1f, 0.65f, 0.2f, 1f),
        AssetTag.Replaced => new NVector4(0.5f, 0.82f, 0.55f, 1f),
        AssetTag.Keep => new NVector4(0.55f, 0.7f, 0.95f, 1f),
        AssetTag.Unused => new NVector4(0.55f, 0.55f, 0.58f, 1f),
        _ => new NVector4(0.88f, 0.9f, 0.92f, 1f),
    };

    private EntityEntry[] ResolveVisible()
    {
        string key = $"{_kindFilter}|{_groupFilter}|{_catalogFilter}|{_onlyClassBound}"
                   + $"|{_onlyMissingTextures}|{_onlyOrphans}|{_filterByTag}|{_tagFilter}";
        if (key == _visibleKey)
            return _visible;

        var query = _session.Catalog.Entries.Where(e => e.Kind == _kindFilter);

        if (_groupFilter.Length > 0)
            query = query.Where(e => e.Group == _groupFilter);

        if (_filterByTag)
            query = query.Where(e => _session.Tags.TagOf(e.ModelPath) == _tagFilter);

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

        if (!ImGui.BeginTable("catalog", 4, flags))
            return;

        ImGui.TableSetupColumn("編號", ImGuiTableColumnFlags.WidthFixed, 48f);
        ImGui.TableSetupColumn("名稱");
        ImGui.TableSetupColumn("模型", ImGuiTableColumnFlags.WidthFixed, 110f);
        ImGui.TableSetupColumn("標註", ImGuiTableColumnFlags.WidthFixed, 62f);
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
        DrawTagContextMenu(entry);

        ImGui.TableSetColumnIndex(1);
        if (entry.ModelMissing)
            ImGui.TextColored(Danger, entry.Name);
        else if (entry.ClassName is null)
            ImGui.TextColored(Muted, entry.Name);
        else
            ImGui.Text(entry.Name);

        ImGui.TableSetColumnIndex(2);
        ImGui.TextColored(Muted, Path.GetFileName(entry.ModelPath));

        ImGui.TableSetColumnIndex(3);
        var tag = _session.Tags.TagOf(entry.ModelPath);
        if (tag != AssetTag.None)
            ImGui.TextColored(TagColor(tag), AssetTagNames.Of(tag));
    }

    /// <summary>
    /// 縮圖牆。<b>只畫看得到的那幾列。</b>
    /// </summary>
    /// <remarks>
    /// 沒有這一層的話，「道具」那一類每幀要跑 2715 次
    /// <c>BeginGroup / PushID / ImageButton / PopID</c>，而且每一格都會去問一次縮圖快取
    /// —— 快取因此被整份掃過，LRU 的「最久沒用到」永遠是這一幀剛用過的東西，
    /// 逐出策略等於失效。ImGui 會裁掉畫面外的東西，但那是在<b>我們的程式跑完之後</b>。
    ///
    /// 每一格的高度必須是固定的，否則捲動位置算不出來；所以說明文字截成一行，
    /// 完整名稱在滑鼠提示裡。
    /// </remarks>
    /// <summary>縮圖底下固定保留幾行說明文字。</summary>
    private const int CaptionLines = 2;

    private void DrawThumbnailGrid(EntityEntry[] entries)
    {
        const float cell = 96f;

        var spacing = ImGui.GetStyle().ItemSpacing;
        float columnWidth = cell + spacing.X;
        int perRow = Math.Max(1, (int)(ImGui.GetContentRegionAvail().X / columnWidth));
        // 說明文字固定兩行：一行放不下「Apocalypse Sword（apocalypse_sword）」這種名字，
        // 而行數不固定的話捲動位置就算不出來。
        float rowHeight = cell + (ImGui.GetTextLineHeightWithSpacing() * CaptionLines) + spacing.Y;

        int rows = (entries.Length + perRow - 1) / perRow;
        float scroll = ImGui.GetScrollY();
        float viewHeight = ImGui.GetWindowHeight();

        // 上下各多畫一列，捲動時才不會看到空白閃一下。
        int firstRow = Math.Max(0, (int)(scroll / rowHeight) - 1);
        int lastRow = Math.Min(rows, (int)((scroll + viewHeight) / rowHeight) + 2);

        if (firstRow > 0)
            ImGui.Dummy(new NVector2(0f, firstRow * rowHeight));

        for (int row = firstRow; row < lastRow; row++)
        {
            for (int column = 0; column < perRow; column++)
            {
                int index = (row * perRow) + column;
                if (index >= entries.Length)
                    break;

                if (column > 0)
                    ImGui.SameLine();

                DrawThumbnailCell(entries[index], cell);
            }
        }

        if (lastRow < rows)
            ImGui.Dummy(new NVector2(0f, (rows - lastRow) * rowHeight));
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
        DrawTagContextMenu(entry);

        string caption = entry.Number >= 0 ? $"{entry.Number} {entry.Name}" : entry.Name;
        var tag = _session.Tags.TagOf(entry.ModelPath);

        // 固定兩行、自己斷行：格子高度必須固定，捲動的可見範圍才算得出來
        // （見 DrawThumbnailGrid）。放不下的完整名稱在滑鼠提示裡。
        var color = tag != AssetTag.None ? TagColor(tag)
                  : entry.ClassName is null ? Muted
                  : new NVector4(0.88f, 0.9f, 0.92f, 1f);

        foreach (var line in Wrap(caption, size, CaptionLines))
            ImGui.TextColored(color, line);

        ImGui.PopID();
        ImGui.EndGroup();
    }

    /// <summary>
    /// 把文字斷成固定行數，最後一行放不下的部分用刪節號。
    /// </summary>
    /// <remarks>
    /// 不用 <c>PushTextWrapPos</c>：那會讓行數隨字串長度變動，
    /// 而縮圖牆需要每一格<b>等高</b>才算得出捲動的可見範圍。
    /// 一律回傳 <paramref name="lines"/> 行（不足的補空字串）。
    /// </remarks>
    private static string[] Wrap(string text, float width, int lines)
    {
        var result = new string[lines];
        var rest = text.AsSpan();

        for (int line = 0; line < lines; line++)
        {
            if (rest.IsEmpty)
            {
                result[line] = string.Empty;
                continue;
            }

            bool last = line == lines - 1;

            if (ImGui.CalcTextSize(rest.ToString()).X <= width)
            {
                result[line] = rest.ToString();
                rest = default;
                continue;
            }

            int fit = LongestFit(rest, width, last);

            result[line] = last ? rest[..fit].ToString() + "…" : rest[..fit].ToString();
            rest = rest[fit..];
        }

        return result;
    }

    /// <summary>最多幾個字元放得進 <paramref name="width"/>（最後一行要留刪節號的位置）。</summary>
    private static int LongestFit(ReadOnlySpan<char> text, float width, bool reserveEllipsis)
    {
        for (int length = text.Length; length > 1; length--)
        {
            string candidate = reserveEllipsis ? text[..length].ToString() + "…" : text[..length].ToString();

            if (ImGui.CalcTextSize(candidate).X <= width)
                return length;
        }

        return 1;
    }

    private void DrawEntryTooltip(EntityEntry entry)
    {
        if (!ImGui.IsItemHovered())
            return;

        ImGui.BeginTooltip();
        ImGui.Text(entry.Name);
        ImGui.TextColored(Muted, entry.ModelPath);

        if (entry.Group.Length > 0)
        {
            ImGui.TextColored(Muted, entry.Detail.Length > 0
                ? $"{entry.Group}　{entry.Detail}"
                : entry.Group);
        }

        var hoveredTag = _session.Tags.TagOf(entry.ModelPath);
        if (hoveredTag != AssetTag.None)
            ImGui.TextColored(TagColor(hoveredTag), "標註：" + AssetTagNames.Of(hoveredTag));

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

        ImGui.TextColored(Muted, "右鍵：標註");
        ImGui.EndTooltip();
    }
}
