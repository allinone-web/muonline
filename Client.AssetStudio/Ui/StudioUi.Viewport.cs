using ImGuiNET;
using Microsoft.Xna.Framework;
using NVector2 = System.Numerics.Vector2;

namespace Client.AssetStudio.Ui;

public sealed partial class StudioUi
{
    /// <summary>
    /// 3D 視埠。上一幀畫好的 render target 在這裡當成一般圖片顯示，滑鼠事件轉成相機操作。
    /// </summary>
    private void DrawViewportPanel()
    {
        PlaceWindow("檢視");
        ImGui.Begin("檢視");

        DrawViewportToolbar();

        var available = ImGui.GetContentRegionAvail();
        int width = Math.Max(64, (int)available.X);
        int height = Math.Max(64, (int)available.Y);

        // 下一幀才會用到這個尺寸（render target 在 ImGui 佈局之前就畫好了）。
        _viewportWidth = width;
        _viewportHeight = height;

        if (_viewportTexture is IntPtr texture)
        {
            ImGui.Image(texture, new NVector2(width, height));
            HandleViewportInput();
        }
        else
        {
            ImGui.TextColored(Muted, "尚未建立視埠");
        }

        ImGui.End();
    }

    private void DrawViewportToolbar()
    {
        bool grid = _viewport.ShowGrid;
        if (ImGui.Checkbox("格線", ref grid))
            _viewport.ShowGrid = grid;
        HelpMarker("一格 = 一個 MU 地形格（TERRAIN_SCALE = 100 世界單位）。用來估模型的實際大小。");

        ImGui.SameLine();
        bool skeleton = _viewport.ShowSkeleton;
        if (ImGui.Checkbox("骨骼", ref skeleton))
            _viewport.ShowSkeleton = skeleton;

        ImGui.SameLine();
        bool textures = _viewport.ShowTextures;
        if (ImGui.Checkbox("貼圖", ref textures))
            _viewport.ShowTextures = textures;

        ImGui.SameLine();
        bool wireframe = _viewport.Wireframe;
        if (ImGui.Checkbox("線框", ref wireframe))
            _viewport.Wireframe = wireframe;

        ImGui.SameLine();
        if (ImGui.Button("看全身") && _session.Model is not null)
            _viewport.Camera.Frame(_session.Model.Bounds);

        ImGui.SameLine();
        ImGui.TextColored(Muted, "左鍵拖曳＝旋轉　中鍵／Shift＋左鍵＝平移　滾輪＝縮放");
    }

    /// <summary>
    /// 相機操作。只在滑鼠壓在視埠這張圖上時作用 ——
    /// 否則在別的面板裡拖曳也會轉動模型，那是很難察覺自己做了什麼的錯誤。
    /// </summary>
    private void HandleViewportInput()
    {
        if (!ImGui.IsItemHovered())
            return;

        var io = ImGui.GetIO();
        var camera = _viewport.Camera;

        if (io.MouseWheel != 0f)
            camera.Zoom(io.MouseWheel);

        if (ImGui.IsMouseDragging(ImGuiMouseButton.Middle)
            || (ImGui.IsMouseDragging(ImGuiMouseButton.Left) && io.KeyShift))
        {
            var delta = ImGui.GetMouseDragDelta(
                io.KeyShift ? ImGuiMouseButton.Left : ImGuiMouseButton.Middle);

            camera.Pan(delta.X, delta.Y);
            ImGui.ResetMouseDragDelta(io.KeyShift ? ImGuiMouseButton.Left : ImGuiMouseButton.Middle);
        }
        else if (ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            var delta = ImGui.GetMouseDragDelta(ImGuiMouseButton.Left);

            camera.Orbit(
                MathHelper.ToRadians(-delta.X * 0.4f),
                MathHelper.ToRadians(delta.Y * 0.4f));

            ImGui.ResetMouseDragDelta(ImGuiMouseButton.Left);
        }
    }
}
