using Client.AssetStudio.Catalog;
using ImGuiNET;
using NVector2 = System.Numerics.Vector2;

namespace Client.AssetStudio.Ui;

public sealed partial class StudioUi
{
    // ImGui 載入的 CJK 字型用 GetGlyphRangesChineseFull()，涵蓋
    // 0x0020-0x00FF、0x2000-0x206F、0x3000-0x30FF、0xFF00-0xFFEF、0x4E00-0x9FAF。
    // 幾何圖形（▶ U+25B6）與箭頭（→ U+2192）都<b>不在裡面</b>，畫出來是「?」。
    // 所以介面文字只用中文、半形符號與全形標點。

    /// <summary>
    /// 動作清單與播放控制。
    /// </summary>
    /// <remarks>
    /// 動作編號的語意來自程式碼的列舉，不在 <c>.bmd</c> 裡（見 <see cref="ActionNames"/>）。
    /// 怪物只有 11 個具名動作，但資源檔常常帶更多 —— 多出來的照樣列出來，
    /// 標成「未命名」而不是隱藏：那些正是「這隻怪還有什麼沒被用到的動作」的線索。
    /// </remarks>
    private void DrawAnimationPanel()
    {
        PlaceWindow("動作");
        ImGui.Begin("動作");

        var model = _session.Model;
        if (model is null)
        {
            ImGui.TextColored(Muted, "尚未選取模型");
            ImGui.End();
            return;
        }

        DrawTransport(model.FrameCount(_session.CurrentAction));
        ImGui.Separator();

        var kind = _session.Selected?.Kind ?? EntityKind.Monster;

        if (ImGui.BeginChild("actionList", new NVector2(0f, 0f)))
        {
            const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY
                                        | ImGuiTableFlags.Borders | ImGuiTableFlags.SizingStretchProp;

            if (ImGui.BeginTable("actions", 3, flags))
            {
                ImGui.TableSetupColumn("動作");
                ImGui.TableSetupColumn("影格", ImGuiTableColumnFlags.WidthFixed, 56f);
                ImGui.TableSetupColumn("鎖位移", ImGuiTableColumnFlags.WidthFixed, 62f);
                ImGui.TableSetupScrollFreeze(0, 1);
                ImGui.TableHeadersRow();

                for (int i = 0; i < model.ActionCount; i++)
                {
                    var action = model.Bmd.Actions![i];
                    if (action is null)
                        continue;

                    ImGui.TableNextRow();

                    ImGui.TableSetColumnIndex(0);
                    bool selected = _session.CurrentAction == i;

                    if (ImGui.Selectable($"{ActionNames.Of(kind, i)}##a{i}", selected, ImGuiSelectableFlags.SpanAllColumns))
                    {
                        _session.CurrentAction = i;
                        _session.AnimTime = 0d;
                    }

                    if (!ActionNames.IsNamed(kind, i) && ImGui.IsItemHovered())
                        ImGui.SetTooltip("這個編號在程式的動作列舉裡沒有名稱 —— 資源自帶的額外動作，不是錯誤。");

                    ImGui.TableSetColumnIndex(1);
                    ImGui.Text(model.FrameCount(i).ToString());

                    ImGui.TableSetColumnIndex(2);
                    if (action.LockPositions)
                        ImGui.TextColored(Muted, "是");
                }

                ImGui.EndTable();
            }
        }

        ImGui.EndChild();
        ImGui.End();
    }

    private void DrawTransport(int frameCount)
    {
        bool playing = _session.Playing;
        if (ImGui.Button(playing ? "暫停" : "播放", new NVector2(64f, 0f)))
            _session.Playing = !playing;

        ImGui.SameLine();
        if (ImGui.Button("上一格"))
            StepFrame(-1, frameCount);

        ImGui.SameLine();
        if (ImGui.Button("下一格"))
            StepFrame(1, frameCount);

        ImGui.SameLine();
        if (ImGui.Button("回到第一格"))
            _session.AnimTime = 0d;

        ImGui.SameLine();
        ImGui.SetNextItemWidth(160f);

        float speed = _session.AnimationSpeed;
        if (ImGui.SliderFloat("速度", ref speed, 0.25f, 20f, "%.2f×"))
            _session.AnimationSpeed = speed;

        HelpMarker("對應 ModelObject.AnimationSpeed，遊戲預設 4。\n"
                 + "注意：每隻怪真正的播放速度是 Client.Main 的類別在 Load() 裡用 SetActionSpeed() 設的"
                 + "（而且會再乘 2），那個值不存在 .bmd 裡，所以這裡只能是一個可調的基準。");

        // 逐格檢視。拖動時自動暫停，否則放開手指的瞬間又跳回去，看起來像沒有反應。
        float position = (float)(frameCount <= 1 ? 0d : _session.AnimTime % frameCount);

        ImGui.SetNextItemWidth(-1f);
        if (ImGui.SliderFloat("##frame", ref position, 0f, MathF.Max(frameCount - 1, 0.001f),
                $"影格 {_session.Frame0}－{_session.Frame1}　內插 {_session.FrameBlend:F2}"))
        {
            _session.Playing = false;
            _session.AnimTime = position;
        }
    }

    private void StepFrame(int direction, int frameCount)
    {
        _session.Playing = false;

        if (frameCount <= 1)
        {
            _session.AnimTime = 0d;
            return;
        }

        double next = Math.Floor(_session.AnimTime) + direction;

        while (next < 0)
            next += frameCount;

        _session.AnimTime = next % frameCount;
    }
}
