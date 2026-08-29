using Client.AssetStudio.Catalog;
using Client.AssetStudio.Textures;
using ImGuiNET;
using NVector2 = System.Numerics.Vector2;

namespace Client.AssetStudio.Ui;

public sealed partial class StudioUi
{
    private string _skillFilter = string.Empty;
    private int _selectedSkill = -1;
    private bool _skillsOnlyWithEffect;
    private bool _skillsHideMaster;

    /// <summary>
    /// 魔法工具。技能在<b>三個地方</b>各有一份定義，這個面板把它們並排。
    /// </summary>
    /// <remarks>
    /// <list type="number">
    /// <item><c>skill.bmd</c> —— 客戶端的屬性表（名稱、圖示、耗魔、射程、需求）。</item>
    /// <item><c>SkillDefinitions</c> —— 手工維護的型別／動作／音效對照表。
    /// <b>型別送錯是靜默失敗</b>：Area 與 Target 走完全不同的封包，
    /// 特效註冊表掛在伺服器回來的封包上，送錯就永遠不會被呼叫（HANDOFF 第 5 節）。</item>
    /// <item>OpenMU 的 <c>config.Skill</c> —— <b>傷害與判定的真相</b>。</item>
    /// </list>
    /// 客戶端的需求值與伺服器對不上是已知事實，而且方向是「客戶端比較嚴格」，
    /// 所以這裡只呈現差異，不試圖判定誰對。
    /// </remarks>
    private void DrawSkillPanel()
    {
        PlaceWindow("魔法");
        ImGui.Begin("魔法", ref _showSkills);

        var skills = _session.Skills;

        if (skills.Error is string error)
        {
            ImGui.TextColored(Danger, error);
            ImGui.End();
            return;
        }

        if (skills.Entries.Length == 0)
        {
            ImGui.TextColored(Muted, "尚未載入技能定義");
            ImGui.End();
            return;
        }

        ImGui.TextColored(Muted, $"{skills.Entries.Length} 個技能　來源：{skills.Source}");

        if (SkillIconResolver.Unavailable is string unavailable)
            ImGui.TextColored(Warning, $"圖示不可用：{unavailable}");

        ImGui.SetNextItemWidth(220f);
        ImGui.InputTextWithHint("##skillFilter", "搜尋名稱或編號", ref _skillFilter, 64);

        ImGui.SameLine();
        ImGui.Checkbox("只看有視覺效果的", ref _skillsOnlyWithEffect);
        HelpMarker("有 [SkillVisualEffect] 註冊的技能才會在遊戲裡播特效。"
                 + "沒有註冊不代表壞掉 —— 很多技能本來就只有動作與音效。");

        ImGui.SameLine();
        ImGui.Checkbox("隱藏大師技", ref _skillsHideMaster);
        HelpMarker("編號 300 以上是大師技，它們沒有自己的動作、音效與特效，全部沿用基礎技。");

        var visible = skills.Entries
            .Where(s => !_skillsOnlyWithEffect || s.VisualEffectClass is not null)
            .Where(s => !_skillsHideMaster || !s.IsMaster)
            .Where(s => string.IsNullOrWhiteSpace(_skillFilter)
                     || s.Search.Contains(_skillFilter, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        ImGui.Separator();

        float listWidth = MathF.Max(280f, ImGui.GetContentRegionAvail().X * 0.42f);

        if (ImGui.BeginChild("skillList", new NVector2(listWidth, 0f)))
            DrawSkillList(visible);
        ImGui.EndChild();

        ImGui.SameLine();

        if (ImGui.BeginChild("skillDetail", new NVector2(0f, 0f)))
        {
            var selected = skills.Entries.FirstOrDefault(s => s.Number == _selectedSkill);
            if (selected is null)
                ImGui.TextColored(Muted, "選一個技能");
            else
                DrawSkillDetail(selected);
        }

        ImGui.EndChild();
        ImGui.End();
    }

    private void DrawSkillList(SkillEntry[] entries)
    {
        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY
                                    | ImGuiTableFlags.Borders | ImGuiTableFlags.SizingStretchProp;

        if (!ImGui.BeginTable("skills", 4, flags))
            return;

        ImGui.TableSetupColumn("圖示", ImGuiTableColumnFlags.WidthFixed, 30f);
        ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed, 42f);
        ImGui.TableSetupColumn("名稱");
        ImGui.TableSetupColumn("型別", ImGuiTableColumnFlags.WidthFixed, 54f);
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableHeadersRow();

        foreach (var skill in entries)
        {
            ImGui.TableNextRow();

            ImGui.TableSetColumnIndex(0);
            DrawSkillIcon(skill, 20f, 28f);

            ImGui.TableSetColumnIndex(1);
            if (ImGui.Selectable($"{skill.Number}##s{skill.Number}", _selectedSkill == skill.Number,
                    ImGuiSelectableFlags.SpanAllColumns))
            {
                _selectedSkill = skill.Number;
            }

            ImGui.TableSetColumnIndex(2);
            if (skill.IsMaster)
                ImGui.TextColored(Muted, skill.Name);
            else
                ImGui.Text(skill.Name);

            ImGui.TableSetColumnIndex(3);
            ImGui.TextColored(Muted, skill.Type.ToString());
        }

        ImGui.EndTable();
    }

    /// <summary>
    /// 技能圖示。位置由 <c>Client.Main</c> 的 <c>SkillIconAtlas</c> 算（見 <see cref="SkillIconResolver"/>），
    /// 這裡只負責把圖集切出那一格。
    /// </summary>
    private void DrawSkillIcon(SkillEntry skill, float width, float height)
    {
        var frame = SkillIconResolver.Resolve(skill.Number, skill.Definition);

        if (frame is null)
        {
            ImGui.Dummy(new NVector2(width, height));
            return;
        }

        string? file = SkillIconResolver.ResolveAtlasFile(_session.DataPath, frame.TexturePath);
        var id = file is null ? null : _previews.Get(file);

        if (id is null)
        {
            ImGui.Dummy(new NVector2(width, height));
            return;
        }

        // SkillIconAtlas 把來源矩形算成像素，所以 UV 要除以圖集邊長。
        // 大師技用的是另一張 512×512、一列 25 格的圖集，其餘四張都是 256×256
        // （見 SkillIconAtlas 對 mumain NewUIMuHelper.cpp 的註解）。
        float atlasSize = Path.GetFileNameWithoutExtension(frame.TexturePath)
            .Contains("Master", StringComparison.OrdinalIgnoreCase) ? 512f : 256f;

        var uv0 = new NVector2(frame.Source.X / atlasSize, frame.Source.Y / atlasSize);
        var uv1 = new NVector2((frame.Source.X + frame.Source.Width) / atlasSize,
                               (frame.Source.Y + frame.Source.Height) / atlasSize);

        ImGui.Image(id.Value, new NVector2(width, height), uv0, uv1);
    }

    private void DrawSkillDetail(SkillEntry skill)
    {
        DrawSkillIcon(skill, 40f, 56f);
        ImGui.SameLine();

        ImGui.BeginGroup();
        ImGui.Text($"{skill.Number}　{skill.Name}");
        ImGui.TextColored(Muted, $"型別 {skill.Type}　"
            + (skill.IsMaster ? $"大師技，基礎技 {skill.BaseSkill}" : "基礎技"));
        ImGui.EndGroup();

        ImGui.Separator();

        if (ImGui.BeginTable("skillCompare", 3,
                ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("欄位");
            ImGui.TableSetupColumn("客戶端 skill.bmd", ImGuiTableColumnFlags.WidthFixed, 130f);
            ImGui.TableSetupColumn("OpenMU 資料庫", ImGuiTableColumnFlags.WidthFixed, 130f);
            ImGui.TableHeadersRow();

            _session.ServerSkills.TryGetValue((short)skill.Number, out var server);

            var definition = skill.Definition;

            Row("射程", definition.Distance.ToString(), server?.Range.ToString());
            Row("傷害", definition.Damage.ToString(), server?.AttackDamage.ToString());
            Row("耗魔", definition.ManaCost.ToString(), "—（伺服器用 SkillEntry/公式）");
            Row("耗 AG", definition.AbilityGaugeCost.ToString(), null);
            Row("冷卻", $"{definition.Delay} ms", null);
            Row("需求等級", definition.RequiredLevel.ToString(), null);
            Row("需求能量", definition.RequiredEnergy.ToString(), null);
            Row("需求力量", definition.RequiredStrength.ToString(), null);
            Row("需求敏捷", definition.RequiredDexterity.ToString(), null);
            Row("每次攻擊命中數", "—", server?.NumberOfHitsPerAttack.ToString());
            Row("移動到目標", "—", server?.MovesToTarget.ToString());

            ImGui.EndTable();

            static void Row(string label, string? client, string? server)
            {
                ImGui.TableNextRow();

                ImGui.TableSetColumnIndex(0);
                ImGui.Text(label);

                ImGui.TableSetColumnIndex(1);
                ImGui.Text(client ?? "—");

                ImGui.TableSetColumnIndex(2);
                if (server is null)
                    ImGui.TextColored(Muted, "—");
                else if (client is not null && client != server && server.Length < 12)
                    ImGui.TextColored(Warning, server);
                else
                    ImGui.Text(server);
            }
        }

        if (!_session.Server.IsConnected)
        {
            ImGui.TextColored(Muted, "尚未連上資料庫，右欄是空的。");
            HelpMarker("伺服器才是傷害與判定的權威。客戶端的 skill_eng.bmd 需求值與 OpenMU 對不上"
                     + "是已知事實，而且方向是客戶端比較嚴格 —— 所以不要照客戶端的數字調平衡。");
        }

        ImGui.Separator();

        ImGui.Text("客戶端行為");
        ImGui.TextColored(Muted, $"角色動作　{(skill.Animation >= 0 ? ActionNames.Of(EntityKind.Player, skill.Animation) : "沒有專屬動作（會退回 PlayerSkillHand1/2 的施法動作）")}");

        if (skill.Animation < 0)
        {
            HelpMarker("退回預設施法動作是「戰士拿著劍在原地畫圈」的成因 —— "
                     + "玩家的第一反應是技能沒放出去，而不是動作對錯了。");
        }

        ImGui.TextColored(Muted, $"音效　　　{skill.Sound ?? "無"}");

        if (skill.VisualEffectClass is string effect)
        {
            ImGui.TextColored(Good, $"視覺效果　{effect}");
        }
        else
        {
            ImGui.TextColored(Muted, "視覺效果　沒有註冊 [SkillVisualEffect]");
        }

        ImGui.Separator();
        ImGui.Text("技能用到的模型");
        HelpMarker("Data/Skill/ 底下是技能與投射物的模型。從左邊目錄的「技能模型」分類可以逐一檢視與播放。");

        if (ImGui.Button("在目錄裡找相關模型"))
        {
            _kindFilter = EntityKind.SkillModel;
            _catalogFilter = skill.Name.Split(' ').FirstOrDefault() ?? string.Empty;
            _visibleKey = string.Empty;
        }
    }
}
