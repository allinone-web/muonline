using Client.AssetStudio.Catalog;
using Client.AssetStudio.Textures;
using Client.MapEditor;
using ImGuiNET;
using NVector2 = System.Numerics.Vector2;

namespace Client.AssetStudio.Ui;

public sealed partial class StudioUi
{
    private string _importSourcePath = string.Empty;
    private int _importTargetMesh = -1;
    private int _jpegQuality = 92;
    private string _noteDraft = string.Empty;
    private string _noteDraftFor = string.Empty;

    /// <summary>模型資訊：網格、骨骼、動作、貼圖，以及貼圖的匯出與匯入。</summary>
    private void DrawModelPanel()
    {
        PlaceWindow("模型");
        ImGui.Begin("模型");

        var entry = _session.Selected;
        var model = _session.Model;

        if (entry is null)
        {
            ImGui.TextColored(Muted, "從左邊的目錄選一個資源");
            ImGui.End();
            return;
        }

        ImGui.Text(entry.Name);
        ImGui.TextColored(Muted, entry.ModelPath);

        if (entry.ClassName is not null)
        {
            ImGui.TextColored(Muted, $"類別 {entry.ClassName}　伺服器編號 {entry.Number}");
            HelpMarker("伺服器編號 = [NpcInfo] 的 typeId = OpenMU 的 MonsterDefinition.Number。\n"
                     + "模型檔名裡的數字（Monster33.bmd）是另一套編號，兩者沒有關係。");
        }

        if (entry.Group.Length > 0)
        {
            ImGui.TextColored(Muted, entry.Detail.Length > 0
                ? $"分類 {entry.Group}　{entry.Detail}"
                : $"分類 {entry.Group}");
        }

        DrawTagEditor(entry);

        if (model is null)
        {
            ImGui.TextColored(Danger, "模型未載入");
            ImGui.End();
            return;
        }

        ImGui.Separator();

        if (ImGui.BeginTabBar("modelTabs"))
        {
            if (ImGui.BeginTabItem("概觀"))
            {
                DrawModelOverview();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("網格與貼圖"))
            {
                DrawMeshTable();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("骨骼"))
            {
                DrawBoneTable();
                ImGui.EndTabItem();
            }

            if (entry.Kind == EntityKind.Item && _session.Items.For(entry.ModelPath).Count > 0
                && ImGui.BeginTabItem("道具定義"))
            {
                DrawItemBindings(entry);
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        ImGui.End();
    }

    /// <summary>
    /// 標註這個資源要不要換。
    /// </summary>
    /// <remarks>
    /// 備註欄用「草稿 + 失焦才寫檔」：每敲一個字就寫一次 JSON，
    /// 四千多筆的檔案會被寫爆，而且打字會頓。
    /// </remarks>
    private void DrawTagEditor(EntityEntry entry)
    {
        var tags = _session.Tags;
        var current = tags.TagOf(entry.ModelPath);

        ImGui.SetNextItemWidth(140f);

        if (ImGui.BeginCombo("##tag", AssetTagNames.Of(current)))
        {
            foreach (var tag in AssetTagNames.All)
            {
                if (ImGui.Selectable(AssetTagNames.Of(tag), current == tag))
                {
                    tags.SetTag(entry.ModelPath, tag);

                    // 目錄的可見清單是有快取的；標註篩選開著時不清掉就不會更新。
                    _visibleKey = string.Empty;
                }
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();

        if (_noteDraftFor != entry.ModelPath)
        {
            _noteDraft = tags.NoteOf(entry.ModelPath);
            _noteDraftFor = entry.ModelPath;
        }

        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##note", "備註（誰畫的、換成什麼、還缺什麼）", ref _noteDraft, 256);

        if (ImGui.IsItemDeactivatedAfterEdit())
            tags.SetNote(entry.ModelPath, _noteDraft);
    }

    /// <summary>這個模型被哪幾件道具使用，以及那些道具的數值。</summary>
    private void DrawItemBindings(EntityEntry entry)
    {
        var bindings = _session.Items.For(entry.ModelPath);

        ImGui.TextColored(Muted, $"{bindings.Count} 件道具使用這個模型");
        HelpMarker("同一個模型被多件道具共用是常態 —— 同一把劍的不同等級、不同名字，"
                 + "在 item.bmd 裡是不同的編號但指向同一個 .bmd。\n"
                 + "換掉這個模型會同時影響下面全部。");

        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY
                                    | ImGuiTableFlags.Borders | ImGuiTableFlags.SizingStretchProp;

        if (!ImGui.BeginTable("itemBindings", 4, flags))
            return;

        ImGui.TableSetupColumn("編號", ImGuiTableColumnFlags.WidthFixed, 46f);
        ImGui.TableSetupColumn("名稱");
        ImGui.TableSetupColumn("需求", ImGuiTableColumnFlags.WidthFixed, 130f);
        ImGui.TableSetupColumn("職業", ImGuiTableColumnFlags.WidthFixed, 120f);
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableHeadersRow();

        foreach (var binding in bindings)
        {
            var definition = binding.Definition;

            ImGui.TableNextRow();

            ImGui.TableSetColumnIndex(0);
            ImGui.Text(binding.Number.ToString());

            ImGui.TableSetColumnIndex(1);
            ImGui.Text(binding.Name);

            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.Text($"{binding.GroupName}　群組 {binding.Group} 編號 {binding.Number}");
                ImGui.TextColored(Muted, $"傷害 {definition.DamageMin}-{definition.DamageMax}　"
                                       + $"防禦 {definition.Defense}　魔力 {definition.MagicPower}");
                ImGui.TextColored(Muted, $"掉落等級 {definition.DropLevel}　"
                                       + (definition.TwoHanded ? "雙手" : "單手"));
                ImGui.EndTooltip();
            }

            ImGui.TableSetColumnIndex(2);
            ImGui.TextColored(Muted,
                $"Lv{definition.RequiredLevel} 力{definition.RequiredStrength} 敏{definition.RequiredDexterity}");

            ImGui.TableSetColumnIndex(3);
            ImGui.TextColored(Muted, definition.AllowedClasses.Count == 0
                ? "－"
                : string.Join("/", definition.AllowedClasses));
        }

        ImGui.EndTable();
    }

    private void DrawModelOverview()
    {
        var model = _session.Model!;

        ImGui.Text($"網格　　{model.AllMeshes.Count()}"
            + (model.Parts.Count > 0 ? $"（主模型 {model.Meshes.Length} + 身體部位 {model.Parts.Count} 個）" : string.Empty));
        ImGui.Text($"三角形　{model.TriangleCount:N0}");
        ImGui.Text($"骨骼　　{model.BoneCount}");
        ImGui.Text($"動作　　{model.ActionCount}");
        ImGui.Text($"檔案　　{FormatBytes(model.FileSize)}　BMD 版本 {model.Bmd.Version}");

        var size = model.Bounds.Max - model.Bounds.Min;
        ImGui.Text($"尺寸　　{size.X:F0} × {size.Y:F0} × {size.Z:F0} 世界單位");
        HelpMarker("一個地形格是 100 世界單位。這個尺寸是模型檔的原始大小，"
                 + "遊戲裡還會再乘上該類別的 Scale（例如 Bali 是 0.12）。");

        if (model.AllMeshes.Count() == 0 && model.BoneCount > 0)
        {
            ImGui.Separator();
            ImGui.TextColored(Warning, "這個 .bmd 只有骨架、沒有網格");
            ImGui.TextColored(Muted, "看得到的身體是另外幾個模型組起來的（NPCObject.SetBodyPartsAsync）。"
                                   + "下面「這個類別另外載入的模型」就是那幾個。");
        }

        int missing = model.AllMeshes.Count(m => !m.Texture.Found);
        if (missing > 0)
        {
            ImGui.Separator();
            ImGui.TextColored(Danger, $"{missing} 個網格缺貼圖");
            ImGui.TextColored(Muted, "缺貼圖的網格在遊戲裡會被安靜地跳過不畫。");

            foreach (var mesh in model.AllMeshes.Where(m => !m.Texture.Found))
                ImGui.TextColored(Warning, $"　網格 {mesh.Index}：{mesh.TexturePath}");
        }

        if (model.Parts.Count > 0)
        {
            ImGui.Separator();
            ImGui.TextColored(Muted, "身體部位（共用主模型的骨架，已一起顯示）：");

            foreach (var part in model.Parts)
                ImGui.TextColored(Muted, $"　{part.FileName}　{part.Meshes.Length} 網格");
        }

        if (_session.Selected?.Attachments.Length > 0)
        {
            ImGui.Separator();
            ImGui.TextColored(Muted, "這個類別另外載入的模型（各自綁在特定骨頭上，沒有一起顯示）：");

            foreach (var attachment in _session.Selected.Attachments)
            {
                if (ImGui.SmallButton($"開啟##{attachment}"))
                    OpenAttachment(attachment);

                ImGui.SameLine();
                ImGui.TextColored(Muted, attachment);
            }
        }
    }

    private void OpenAttachment(string relativePath)
    {
        var entry = _session.Catalog.Entries.FirstOrDefault(e =>
            e.ModelPath.Equals(relativePath, StringComparison.OrdinalIgnoreCase));

        if (entry is not null)
            _session.Select(entry);
        else
            _session.Report($"目錄裡沒有 {relativePath}（可能不在掃描的資料夾內）", failed: true);
    }

    private void DrawMeshTable()
    {
        var model = _session.Model!;

        ImGui.TextColored(Muted, "取消勾選可以單獨看某一塊，換素材時很有用。");

        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY
                                    | ImGuiTableFlags.Borders | ImGuiTableFlags.SizingStretchProp;

        if (ImGui.BeginTable("meshes", 5, flags, new NVector2(0f, 220f)))
        {
            ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed, 28f);
            ImGui.TableSetupColumn("顯示", ImGuiTableColumnFlags.WidthFixed, 40f);
            ImGui.TableSetupColumn("貼圖");
            ImGui.TableSetupColumn("三角形", ImGuiTableColumnFlags.WidthFixed, 58f);
            ImGui.TableSetupColumn("半透明", ImGuiTableColumnFlags.WidthFixed, 54f);
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableHeadersRow();

            foreach (var mesh in model.AllMeshes)
            {
                ImGui.TableNextRow();
                ImGui.PushID(mesh.Index);

                ImGui.TableSetColumnIndex(0);
                bool selected = _importTargetMesh == mesh.Index;
                if (ImGui.Selectable($"{mesh.Index}", selected, ImGuiSelectableFlags.SpanAllColumns))
                    _importTargetMesh = mesh.Index;

                ImGui.TableSetColumnIndex(1);
                bool visible = mesh.Visible;
                if (ImGui.Checkbox("##visible", ref visible))
                    mesh.Visible = visible;

                ImGui.TableSetColumnIndex(2);
                if (mesh.Texture.Found)
                    ImGui.Text(mesh.Texture.FileName);
                else
                    ImGui.TextColored(Danger, mesh.TexturePath + "（缺）");

                ImGui.TableSetColumnIndex(3);
                ImGui.Text(mesh.TriangleCount.ToString());

                ImGui.TableSetColumnIndex(4);
                bool transparent = mesh.IsTransparent;
                if (ImGui.Checkbox("##transparent", ref transparent))
                    mesh.IsTransparent = transparent;

                ImGui.PopID();
            }

            ImGui.EndTable();
        }

        ImGui.Separator();
        DrawTextureTools();
    }

    /// <summary>選中網格的貼圖：預覽、匯出 PNG、從 PNG 匯入。</summary>
    private void DrawTextureTools()
    {
        var model = _session.Model!;

        var mesh = model.AllMeshes.FirstOrDefault(m => m.Index == _importTargetMesh)
                ?? model.AllMeshes.FirstOrDefault();

        if (mesh is null)
            return;

        _importTargetMesh = mesh.Index;

        ImGui.Text($"網格 {mesh.Index} 的貼圖");

        if (!mesh.Texture.Found)
        {
            ImGui.TextColored(Danger, $"找不到 {mesh.TexturePath}");
            ImGui.TextColored(Muted, $"可接受的格式：{string.Join(" / ", TextureResolver.Extensions)}"
                                   + "（網格寫 .tga 時 ozt 優先，寫 .jpg 時 ozj 優先）；"
                                   + "找的位置是模型所在目錄與其下的 texture/ 子目錄。");
        }
        else
        {
            var id = _previews.Get(mesh.Texture.FullPath!);
            if (id.HasValue)
                ImGui.Image(id.Value, new NVector2(128f, 128f));

            ImGui.SameLine();
            ImGui.BeginGroup();
            ImGui.TextColored(Muted, mesh.Texture.FullPath!);

            if (ImGui.Button("匯出 PNG"))
                ExportTexture(mesh.Texture.FullPath!);

            ImGui.SameLine();
            if (ImGui.Button("在 Finder 顯示"))
                RevealInFinder(mesh.Texture.FullPath!);

            ImGui.EndGroup();
        }

        ImGui.SetNextItemWidth(-120f);
        ImGui.InputTextWithHint("##importPath", "要匯入的 PNG 檔完整路徑", ref _importSourcePath, 512);

        ImGui.SameLine();
        ImGui.SetNextItemWidth(110f);
        ImGui.SliderInt("##quality", ref _jpegQuality, 60, 100, "JPEG %d");

        if (ImGui.Button("匯入並取代這張貼圖"))
            ImportTexture(mesh);

        HelpMarker(
            "會依照目標檔的副檔名寫回：\n"
          + "· .OZJ —— 24 byte 標頭 + JPEG（不透明貼圖）\n"
          + "· .OZT —— 帶 alpha，寬高必須是 2 的冪\n"
          + "· .OZD —— 加密的 DXT，無法寫入；請改存成同名的 .OZT，\n"
          + "  載入時副檔名的搜尋順序會先找到它。\n\n"
          + "原檔會先備份成 .bak（同目錄）。");
    }

    private void ExportTexture(string path)
    {
        try
        {
            Directory.CreateDirectory(_exportDirectory);
            string destination = Path.Combine(_exportDirectory, Path.GetFileNameWithoutExtension(path) + ".png");

            TextureIO.ExportPng(path, destination);
            _session.Report($"已匯出 {destination}");
        }
        catch (Exception ex)
        {
            _session.Report($"匯出失敗：{ex.Message}", failed: true);
        }
    }

    private void ImportTexture(Rendering.MeshView mesh)
    {
        if (string.IsNullOrWhiteSpace(_importSourcePath) || !File.Exists(_importSourcePath))
        {
            _session.Report("請先填入要匯入的 PNG 檔完整路徑", failed: true);
            return;
        }

        var model = _session.Model!;

        // 沒有現成檔案時（缺貼圖的網格），就在**這個網格自己的**資料夾新建一個 .OZT ——
        // 身體部位是另外載入的模型，貼圖跟著它走，寫到主模型的資料夾遊戲會找不到。
        // OZT 帶 alpha 而且是這裡唯一能無損寫入的格式。
        string destination = mesh.Texture.FullPath
            ?? Path.Combine(mesh.Directory, Path.GetFileNameWithoutExtension(mesh.TexturePath) + ".OZT");

        // OZD 寫不了，但同名的 OZT 會被優先找到，所以直接改寫成 OZT。
        if (Path.GetExtension(destination).Equals(".ozd", StringComparison.OrdinalIgnoreCase))
            destination = Path.ChangeExtension(destination, ".OZT");

        try
        {
            if (File.Exists(destination))
                File.Copy(destination, destination + ".bak", overwrite: true);
        }
        catch (Exception ex)
        {
            _session.Report($"備份原檔失敗，已中止：{ex.Message}", failed: true);
            return;
        }

        var result = TextureIO.Import(_importSourcePath, destination, _jpegQuality);
        _session.Report(result.Message, failed: !result.Success);

        if (result.Success)
        {
            model.ReloadTextures();
            Catalog.ModelInspector.Invalidate(model.Path);

            // 預覽快取是以路徑為鍵的，寫回同一個路徑之後它還是舊圖。
            // 整個換掉最省事 —— 匯入是低頻操作。
            _previews = new TexturePreviewCache(_game.GraphicsDevice, _imgui);
        }
    }

    private void DrawBoneTable()
    {
        var model = _session.Model!;
        var bones = model.Bmd.Bones ?? [];

        ImGui.TextColored(Muted, $"{bones.Length} 根骨骼。父欄的 −1 是根骨。");

        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY
                                    | ImGuiTableFlags.Borders | ImGuiTableFlags.SizingStretchProp;

        if (!ImGui.BeginTable("bones", 3, flags))
            return;

        ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed, 36f);
        ImGui.TableSetupColumn("名稱");
        ImGui.TableSetupColumn("父", ImGuiTableColumnFlags.WidthFixed, 40f);
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableHeadersRow();

        for (int i = 0; i < bones.Length; i++)
        {
            var bone = bones[i];
            ImGui.TableNextRow();

            ImGui.TableSetColumnIndex(0);
            ImGui.Text(i.ToString());

            ImGui.TableSetColumnIndex(1);
            if (bone is null || bone == Client.Data.BMD.BMDTextureBone.Dummy)
                ImGui.TextColored(Muted, "（Dummy）");
            else
                ImGui.Text(string.IsNullOrWhiteSpace(bone.Name) ? "（無名）" : bone.Name);

            ImGui.TableSetColumnIndex(2);
            ImGui.TextColored(Muted, bone is null || bone == Client.Data.BMD.BMDTextureBone.Dummy
                ? "－"
                : bone.Parent.ToString());
        }

        ImGui.EndTable();
    }
}
