using Client.Main;
using Client.Main.Graphics;
using ImGuiNET;
using Microsoft.Xna.Framework;
using NVector2 = System.Numerics.Vector2;
using NVector4 = System.Numerics.Vector4;

namespace Client.MapEditor;

public enum GizmoAxis
{
    None,
    X,
    Y,
    Z,
    Yaw,
}

/// <summary>
/// 選中物件的 3D 拖曳手柄。
/// </summary>
/// <remarks>
/// 畫在 ImGui 的前景繪圖層上，而不是進 3D 場景 —— 這樣不必寫 shader、不必管深度，
/// 而且手柄永遠在最上面（被地形擋住的手柄沒有用）。
///
/// 拖曳的算法：把滑鼠位移投影到該軸在螢幕上的方向，再換算回世界單位。
/// 這是 2D 投影法，比射線與軸的最近點簡單得多，手感在一般視角下沒有差別。
/// </remarks>
public sealed class TransformGizmo
{
    /// <summary>手柄在螢幕上的長度（像素）。用固定像素長度，遠近都好抓。</summary>
    private const float ScreenLength = 70f;
    private const float GrabRadius = 10f;

    private GizmoAxis _hovered;
    private GizmoAxis _dragging;
    private NVector2 _dragOrigin;
    private Vector3 _dragStartPosition;
    private float _dragStartYaw;
    private MapObjectInstance? _dragStartSnapshot;

    public bool IsDragging => _dragging != GizmoAxis.None;

    /// <summary>拖曳開始前的狀態，放開時交給歷史。</summary>
    public MapObjectInstance? TakeCompletedDrag()
    {
        var snapshot = _dragStartSnapshot;
        _dragStartSnapshot = null;
        return snapshot;
    }

    /// <summary>
    /// 畫手柄並處理拖曳。回傳 true 表示這一幀滑鼠被手柄用掉了（相機不該再轉）。
    /// </summary>
    public bool Draw(MapObjectInstance? target, bool acceptInput)
    {
        if (target is null)
        {
            _dragging = GizmoAxis.None;
            _hovered = GizmoAxis.None;
            return false;
        }

        var origin = new Vector3(target.Position.X, target.Position.Y, target.Position.Z);

        if (!TryProject(origin, out var screenOrigin))
            return false;

        // 三個軸的螢幕方向：把「原點 + 一小段世界位移」投影後相減。
        if (!TryAxisDirection(origin, Vector3.UnitX, screenOrigin, out var axisX) ||
            !TryAxisDirection(origin, Vector3.UnitY, screenOrigin, out var axisY) ||
            !TryAxisDirection(origin, Vector3.UnitZ, screenOrigin, out var axisZ))
        {
            return false;
        }

        var drawList = ImGui.GetForegroundDrawList();
        var mouse = ImGui.GetIO().MousePos;

        if (!IsDragging && acceptInput)
            _hovered = PickAxis(mouse, screenOrigin, axisX, axisY, axisZ);

        DrawAxis(drawList, screenOrigin, axisX, GizmoAxis.X, "X", new NVector4(1f, 0.35f, 0.35f, 1f));
        DrawAxis(drawList, screenOrigin, axisY, GizmoAxis.Y, "Y", new NVector4(0.4f, 0.9f, 0.4f, 1f));
        DrawAxis(drawList, screenOrigin, axisZ, GizmoAxis.Z, "Z", new NVector4(0.45f, 0.6f, 1f, 1f));
        DrawYawRing(drawList, screenOrigin);

        drawList.AddCircleFilled(screenOrigin, 4f, ImGui.GetColorU32(new NVector4(1f, 0.9f, 0.3f, 1f)));

        if (!acceptInput)
        {
            _dragging = GizmoAxis.None;
            return false;
        }

        if (_hovered != GizmoAxis.None && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            _dragging = _hovered;
            _dragOrigin = mouse;
            _dragStartPosition = origin;
            _dragStartYaw = target.Angle.Z;
            _dragStartSnapshot = target.Clone();
        }

        if (_dragging != GizmoAxis.None)
        {
            if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            {
                _dragging = GizmoAxis.None;
                return true;
            }

            var delta = new NVector2(mouse.X - _dragOrigin.X, mouse.Y - _dragOrigin.Y);
            Apply(target, delta, axisX, axisY, axisZ);
            return true;
        }

        return _hovered != GizmoAxis.None;
    }

    private void Apply(MapObjectInstance target, NVector2 delta, NVector2 axisX, NVector2 axisY, NVector2 axisZ)
    {
        if (_dragging == GizmoAxis.Yaw)
        {
            // 水平位移換成角度，200px ≈ 180°。
            target.Angle = target.Angle with { Z = _dragStartYaw + (delta.X * 0.9f) };
            return;
        }

        (NVector2 screenAxis, Vector3 worldAxis) = _dragging switch
        {
            GizmoAxis.X => (axisX, Vector3.UnitX),
            GizmoAxis.Y => (axisY, Vector3.UnitY),
            _ => (axisZ, Vector3.UnitZ),
        };

        float lengthSquared = (screenAxis.X * screenAxis.X) + (screenAxis.Y * screenAxis.Y);
        if (lengthSquared < 1e-4f)
            return;

        // 把滑鼠位移投影到該軸的螢幕方向，除以「一世界單位有幾個螢幕像素」。
        float projected = ((delta.X * screenAxis.X) + (delta.Y * screenAxis.Y)) / lengthSquared;
        var moved = _dragStartPosition + (worldAxis * projected * ScreenLength);

        target.Position = new System.Numerics.Vector3(moved.X, moved.Y, moved.Z);
    }

    private void DrawAxis(ImDrawListPtr drawList, NVector2 origin, NVector2 direction, GizmoAxis axis, string label, NVector4 color)
    {
        bool active = _dragging == axis || (_dragging == GizmoAxis.None && _hovered == axis);
        var tint = active ? new NVector4(1f, 0.95f, 0.4f, 1f) : color;
        uint packed = ImGui.GetColorU32(tint);

        var tip = new NVector2(origin.X + direction.X, origin.Y + direction.Y);

        drawList.AddLine(origin, tip, packed, active ? 3.5f : 2f);
        drawList.AddCircleFilled(tip, active ? 6f : 4.5f, packed);
        drawList.AddText(new NVector2(tip.X + 6f, tip.Y - 8f), packed, label);
    }

    private void DrawYawRing(ImDrawListPtr drawList, NVector2 origin)
    {
        bool active = _dragging == GizmoAxis.Yaw || (_dragging == GizmoAxis.None && _hovered == GizmoAxis.Yaw);
        uint packed = ImGui.GetColorU32(active
            ? new NVector4(1f, 0.95f, 0.4f, 1f)
            : new NVector4(0.9f, 0.7f, 1f, 0.9f));

        drawList.AddCircle(origin, YawRingRadius, packed, 48, active ? 3f : 1.8f);
    }

    private const float YawRingRadius = 44f;

    private GizmoAxis PickAxis(NVector2 mouse, NVector2 origin, NVector2 axisX, NVector2 axisY, NVector2 axisZ)
    {
        // 軸先判定：它們在圓環內側，優先權要比旋轉環高。
        if (DistanceToSegment(mouse, origin, Offset(origin, axisX)) < GrabRadius) return GizmoAxis.X;
        if (DistanceToSegment(mouse, origin, Offset(origin, axisY)) < GrabRadius) return GizmoAxis.Y;
        if (DistanceToSegment(mouse, origin, Offset(origin, axisZ)) < GrabRadius) return GizmoAxis.Z;

        float toCentre = MathF.Sqrt(((mouse.X - origin.X) * (mouse.X - origin.X)) + ((mouse.Y - origin.Y) * (mouse.Y - origin.Y)));
        if (MathF.Abs(toCentre - YawRingRadius) < GrabRadius)
            return GizmoAxis.Yaw;

        return GizmoAxis.None;
    }

    private static NVector2 Offset(NVector2 origin, NVector2 direction)
        => new(origin.X + direction.X, origin.Y + direction.Y);

    private static float DistanceToSegment(NVector2 point, NVector2 a, NVector2 b)
    {
        var ab = new NVector2(b.X - a.X, b.Y - a.Y);
        float lengthSquared = (ab.X * ab.X) + (ab.Y * ab.Y);

        if (lengthSquared < 1e-4f)
            return MathF.Sqrt(((point.X - a.X) * (point.X - a.X)) + ((point.Y - a.Y) * (point.Y - a.Y)));

        float t = Math.Clamp((((point.X - a.X) * ab.X) + ((point.Y - a.Y) * ab.Y)) / lengthSquared, 0f, 1f);
        var closest = new NVector2(a.X + (ab.X * t), a.Y + (ab.Y * t));

        return MathF.Sqrt(((point.X - closest.X) * (point.X - closest.X)) + ((point.Y - closest.Y) * (point.Y - closest.Y)));
    }

    /// <summary>取該軸在螢幕上的方向，長度正規化成固定像素。</summary>
    private static bool TryAxisDirection(Vector3 origin, Vector3 axis, NVector2 screenOrigin, out NVector2 direction)
    {
        direction = default;

        // 用一格的世界距離當取樣步長，投影後再正規化。
        if (!TryProject(origin + (axis * Constants.TERRAIN_SCALE), out var screenTip))
            return false;

        var delta = new NVector2(screenTip.X - screenOrigin.X, screenTip.Y - screenOrigin.Y);
        float length = MathF.Sqrt((delta.X * delta.X) + (delta.Y * delta.Y));

        if (length < 0.5f)
            return false;

        direction = new NVector2(delta.X / length * ScreenLength, delta.Y / length * ScreenLength);
        return true;
    }

    private static bool TryProject(Vector3 world, out NVector2 screen)
    {
        screen = default;

        var device = MuGame.Instance?.GraphicsDevice;
        if (device is null)
            return false;

        var projected = device.Viewport.Project(
            world,
            Camera.Instance.Projection,
            Camera.Instance.View,
            Matrix.Identity);

        // Project 的 Z 在 0–1 之外表示點在近/遠平面外，投影出來的 X/Y 沒有意義。
        if (projected.Z is < 0f or > 1f)
            return false;

        screen = new NVector2(projected.X, projected.Y);
        return true;
    }
}
