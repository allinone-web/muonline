using Client.AssetStudio.Catalog;
using Client.AssetStudio.Import;
using Client.AssetStudio.Project;
using ImGuiNET;
using NVector2 = System.Numerics.Vector2;

namespace Client.AssetStudio.Ui;

public sealed partial class StudioUi
{
    private string _libraryRoot = string.Empty;
    private string _importPath = string.Empty;
    private string _importName = string.Empty;
    private EntityKind _importKind = EntityKind.Monster;
    private LibraryAsset? _selectedAsset;
    private ImportedModel? _selectedImport;
    private string _bindDraft = string.Empty;

    /// <summary>
    /// 自有資產的資源庫。
    /// </summary>
    /// <remarks>
    /// 目錄面板管的是<b>遊戲原本的</b>資產（Webzen 的），這個面板管的是<b>你自己的</b>。
    /// 兩者刻意分開：一個是要被替換掉的，一個是拿來替換的。
    ///
    /// 匯入的流程是「先看報告，再決定」——
    /// 相容性報告會說清楚 MU 表達不出你模型的哪些東西（多骨權重、縮放、morph target），
    /// 那些不是錯誤，是<b>取捨</b>，該由人看過再決定，不該由工具靜默處理掉。
    /// </remarks>
    private void DrawLibraryPanel()
    {
        PlaceWindow("資源庫");
        ImGui.Begin("資源庫（自有資產）", ref _showLibrary);

        var library = _session.Library;

        if (_libraryRoot.Length == 0)
            _libraryRoot = library.Root;

        ImGui.SetNextItemWidth(-150f);
        ImGui.InputText("##libraryRoot", ref _libraryRoot, 512);

        ImGui.SameLine();
        if (ImGui.Button("開啟"))
        {
            library.Open(_libraryRoot);
            _selectedAsset = null;
            _selectedImport = null;
        }

        ImGui.SameLine();
        if (ImGui.Button("在 Finder 顯示"))
        {
            Directory.CreateDirectory(library.Root);
            RevealInFinder(library.Root);
        }

        if (library.LastError is string error)
            ImGui.TextColored(Danger, error);

        ImGui.TextColored(Muted, "存的是 glTF + PNG + 一份 JSON 清單 —— 引擎中立，換引擎不用動。");

        ImGui.Separator();
        DrawImportBar(library);
        ImGui.Separator();

        if (library.Assets.Count == 0)
        {
            ImGui.TextColored(Muted, "還沒有任何自有資產。");
            ImGui.End();
            return;
        }

        float listHeight = MathF.Max(120f, ImGui.GetContentRegionAvail().Y * 0.35f);

        if (ImGui.BeginChild("libraryList", new NVector2(0f, listHeight)))
            DrawAssetList(library);

        ImGui.EndChild();

        if (_selectedAsset is not null)
        {
            ImGui.Separator();
            DrawAssetDetail(library, _selectedAsset);
        }

        ImGui.End();
    }

    private void DrawImportBar(AssetLibrary library)
    {
        ImGui.SetNextItemWidth(-150f);
        ImGui.InputTextWithHint("##importPath", "要匯入的 .gltf / .glb 完整路徑", ref _importPath, 512);

        ImGui.SameLine();
        if (ImGui.Button("匯入") && _importPath.Length > 0)
            ImportIntoLibrary(library);

        ImGui.SetNextItemWidth(180f);
        ImGui.InputTextWithHint("##importName", "名稱（留空用檔名）", ref _importName, 96);

        ImGui.SameLine();
        ImGui.SetNextItemWidth(120f);

        if (ImGui.BeginCombo("##importKind", EntityKindNames.Of(_importKind)))
        {
            foreach (var kind in EntityKindNames.All)
            {
                if (ImGui.Selectable(EntityKindNames.Of(kind), _importKind == kind))
                    _importKind = kind;
            }

            ImGui.EndCombo();
        }

        HelpMarker("匯入時原始檔會原封不動複製進資源庫，貼圖另外抽成 PNG。\n"
                 + "不轉成 .bmd —— 資源庫存的是來源，衍生物都能從來源重建。");
    }

    private void ImportIntoLibrary(AssetLibrary library)
    {
        var asset = library.Add(_importPath, _importName.Length > 0 ? _importName : null, _importKind, out var imported);

        if (asset is null)
        {
            _session.Report(library.LastError ?? "匯入失敗", failed: true);

            // 失敗的報告也要看得到 —— 「為什麼匯不進來」比「匯不進來」有用得多。
            _selectedImport = imported;
            _selectedAsset = null;
            return;
        }

        _importPath = string.Empty;
        _importName = string.Empty;
        Select(library, asset);

        _session.Report($"已加入資源庫：{asset.Name}"
                      + (imported?.Report.WarningCount > 0 ? $"（{imported.Report.WarningCount} 項要注意）" : string.Empty),
                        failed: imported?.Report.WarningCount > 0);
    }

    private void DrawAssetList(AssetLibrary library)
    {
        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY
                                    | ImGuiTableFlags.Borders | ImGuiTableFlags.SizingStretchProp;

        if (!ImGui.BeginTable("libraryAssets", 4, flags))
            return;

        ImGui.TableSetupColumn("名稱");
        ImGui.TableSetupColumn("分類", ImGuiTableColumnFlags.WidthFixed, 60f);
        ImGui.TableSetupColumn("綁定", ImGuiTableColumnFlags.WidthFixed, 80f);
        ImGui.TableSetupColumn("動作", ImGuiTableColumnFlags.WidthFixed, 48f);
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableHeadersRow();

        foreach (var asset in library.Assets)
        {
            ImGui.TableNextRow();

            ImGui.TableSetColumnIndex(0);
            if (ImGui.Selectable(asset.Name + "##" + asset.Id, _selectedAsset?.Id == asset.Id,
                    ImGuiSelectableFlags.SpanAllColumns))
            {
                Select(library, asset);
            }

            ImGui.TableSetColumnIndex(1);
            ImGui.TextColored(Muted, EntityKindNames.Of(asset.Kind));

            ImGui.TableSetColumnIndex(2);
            if (asset.BindNumber >= 0)
                ImGui.TextColored(Good, "#" + asset.BindNumber);
            else
                ImGui.TextColored(Muted, "－");

            ImGui.TableSetColumnIndex(3);
            ImGui.TextColored(asset.Actions.Count > 0 ? Good : Muted, asset.Actions.Count.ToString());
        }

        ImGui.EndTable();
    }

    /// <summary>依名稱或 id 選一筆自有資產。給 <c>--open-library</c> 用。</summary>
    public bool SelectLibraryAsset(string idOrName) => SelectLibraryAsset(idOrName, openPanel: true);

    /// <param name="openPanel">headless 截圖時給 false：只把模型載進檢視器，不要讓資源庫面板蓋住畫面。</param>
    public bool SelectLibraryAsset(string idOrName, bool openPanel)
    {
        var asset = _session.Library.Find(idOrName);

        if (asset is null)
            return false;

        _showLibrary = openPanel;
        Select(_session.Library, asset);
        return true;
    }

    /// <summary>選中一筆資產：讀進來、顯示在檢視器裡。</summary>
    private void Select(AssetLibrary library, LibraryAsset asset)
    {
        _selectedAsset = asset;
        _bindDraft = asset.BindNumber >= 0 ? asset.BindNumber.ToString() : string.Empty;

        try
        {
            _selectedImport = GltfImporter.Import(
                library.SourcePathOf(asset),
                new GltfImporter.Options(Scale: asset.Scale, AutoScale: false));

            // 交給主執行緒在下一幀掛上檢視器。與目錄選取走同一條路徑，
            // 因為建 Texture2D 只能在主執行緒做。
            _session.RequestedLibraryAsset = (asset, _selectedImport, library.TextureDirectoryOf(asset));
        }
        catch (Exception ex)
        {
            _session.Report($"讀取 {asset.Name} 失敗：{ex.Message}", failed: true);
        }
    }

    private void DrawAssetDetail(AssetLibrary library, LibraryAsset asset)
    {
        ImGui.Text(asset.Name);
        ImGui.TextColored(Muted, asset.Source);

        if (_selectedImport is { } imported)
        {
            ImGui.TextColored(imported.Report.WarningCount > 0 ? Warning : Muted, imported.Report.Summary);

            foreach (var issue in imported.Report.Issues.Where(i => i.Severity != ImportSeverity.Info))
            {
                ImGui.TextColored(issue.Severity == ImportSeverity.Error ? Danger : Warning, "・" + issue.Title);

                if (ImGui.IsItemHovered() && issue.Detail.Length > 0)
                    ImGui.SetTooltip(issue.Detail);
            }
        }

        ImGui.SetNextItemWidth(120f);
        float scale = asset.Scale;
        if (ImGui.DragFloat("縮放", ref scale, MathF.Max(scale * 0.01f, 0.01f), 0.001f, 10000f, "×%.3f"))
        {
            asset.Scale = scale;
            library.Update();
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(100f);

        if (ImGui.InputTextWithHint("綁定編號", "如 150", ref _bindDraft, 8, ImGuiInputTextFlags.CharsDecimal)
            && ImGui.IsItemDeactivatedAfterEdit())
        {
            asset.BindNumber = int.TryParse(_bindDraft, out int number) ? number : -1;
            library.Update();
        }

        HelpMarker("要接管遊戲裡的哪一個怪物／NPC 編號（[NpcInfo] 的 typeId，"
                 + "也就是 OpenMU 的 MonsterDefinition.Number）。\n"
                 + "目前只是記錄下來 —— 真正把它裝進執行期要等客戶端能讀 glTF。");

        ImGui.Separator();
        DrawActionMapping(library, asset);
    }

    /// <summary>
    /// 動作對映：遊戲的動作編號 ← 外部檔案裡的動作名稱。
    /// </summary>
    /// <remarks>
    /// 這張表是匯入外部角色最無法自動化的一步，也是最容易被忽略的一步。
    /// MU 用<b>編號</b>認動作（<c>MonsterActionType.Die</c> 就是 6），
    /// 外部模型用<b>名字</b>（"Death"、"die_01"、"Armature|Die"）。
    /// 沒有這張表，角色會用錯的動作播放，而且不會有任何錯誤訊息。
    /// </remarks>
    private void DrawActionMapping(AssetLibrary library, LibraryAsset asset)
    {
        ImGui.Text("動作對映");
        HelpMarker("左邊是遊戲的動作編號（改不了，那是遊戲的語彙），"
                 + "右邊是你的檔案裡的動作名稱。");

        var clips = _selectedImport?.Clips ?? [];

        if (clips.Length == 0)
        {
            ImGui.TextColored(Muted, "這個檔案沒有動畫。");
            return;
        }

        // 怪物只有 11 個具名動作；角色那一套有 380 個，全列出來沒有意義。
        int count = asset.Kind == EntityKind.Monster ? 11 : 16;

        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders
                                    | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY;

        if (!ImGui.BeginTable("actionMap", 2, flags, new NVector2(0f, 0f)))
            return;

        ImGui.TableSetupColumn("遊戲的動作");
        ImGui.TableSetupColumn("你的動作", ImGuiTableColumnFlags.WidthFixed, 180f);
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableHeadersRow();

        for (int action = 0; action < count; action++)
        {
            ImGui.TableNextRow();
            ImGui.PushID(action);

            ImGui.TableSetColumnIndex(0);
            ImGui.Text(ActionNames.Of(asset.Kind, action));

            ImGui.TableSetColumnIndex(1);
            string current = library.ClipFor(asset, action) ?? "－";
            ImGui.SetNextItemWidth(-1f);

            if (ImGui.BeginCombo("##clip", current))
            {
                if (ImGui.Selectable("－（不對映）", current == "－"))
                    library.MapAction(asset, action, null);

                foreach (var clip in clips)
                {
                    if (ImGui.Selectable(clip, current == clip))
                        library.MapAction(asset, action, clip);
                }

                ImGui.EndCombo();
            }

            ImGui.PopID();
        }

        ImGui.EndTable();
    }
}
