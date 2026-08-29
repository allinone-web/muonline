using Client.Main;
using Client.Main.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using MuAssets.Core;

namespace Client.MapEditor;

public enum CameraMode
{
    /// <summary>環繞焦點的自由視角，用來看 3D 場景。</summary>
    Orbit,

    /// <summary>近乎正交的俯視，貼圖與屬性繪製的主要視圖。</summary>
    TopDown,
}

/// <summary>
/// 編輯器的相機。直接驅動 <see cref="Camera.Instance"/>（渲染管線全都讀它）。
/// </summary>
/// <remarks>
/// 座標系依 AGENTS.md：X/Y 是地圖平面、Z 是高度，一格 <c>TERRAIN_SCALE == 100</c> 世界單位。
///
/// MonoGame 的 <see cref="Camera"/> 只有透視投影，沒有正交。俯視模式用「很小的 FOV +
/// 很遠的距離」逼近正交 —— 對格子對位來說夠用，真正的正交投影等有需要再說。
/// </remarks>
public sealed class EditorCamera
{
    private const float TileScale = Constants.TERRAIN_SCALE;
    private const float MapExtent = Constants.TERRAIN_SIZE * TileScale;

    private const float OrbitFov = 35f;
    private const float TopDownFov = 12f;

    private MouseState _previousMouse;
    private bool _initialized;

    public CameraMode Mode { get; set; } = CameraMode.Orbit;

    /// <summary>相機看向的地面點（世界座標，Z 為地表高度）。</summary>
    public Vector3 Focus { get; set; } = new(MapExtent / 2f, MapExtent / 2f, 0f);

    public float Distance { get; set; } = 6000f;

    /// <summary>水平方位角（弧度）。</summary>
    public float Yaw { get; set; } = MathHelper.ToRadians(-45f);

    /// <summary>俯角（弧度）。0 = 水平，π/2 = 正上方往下看。</summary>
    public float Pitch { get; set; } = MathHelper.ToRadians(50f);

    public float MoveSpeed { get; set; } = 2500f;

    /// <summary>把相機拉到能看見整張地圖的位置。</summary>
    public void FrameWholeMap()
    {
        Focus = new Vector3(MapExtent / 2f, MapExtent / 2f, 0f);
        Mode = CameraMode.TopDown;
        Distance = MapExtent / (2f * MathF.Tan(MathHelper.ToRadians(TopDownFov) / 2f));
    }

    /// <summary>把相機拉到某一格的上方，維持目前的模式與距離。</summary>
    public void FocusTile(int tileX, int tileY)
        => Focus = new Vector3((tileX + 0.5f) * TileScale, (tileY + 0.5f) * TileScale, Focus.Z);

    /// <param name="acceptInput">ImGui 佔用滑鼠/鍵盤時傳 false，否則在面板上拖曳會連帶轉相機。</param>
    public void Update(GameTime time, bool acceptInput)
    {
        var mouse = Mouse.GetState();

        if (!_initialized)
        {
            _previousMouse = mouse;
            _initialized = true;
        }

        if (acceptInput)
        {
            ApplyMouse(mouse);
            ApplyKeyboard((float)time.ElapsedGameTime.TotalSeconds);
        }

        _previousMouse = mouse;
        Apply();
    }

    private void ApplyMouse(MouseState mouse)
    {
        int dx = mouse.X - _previousMouse.X;
        int dy = mouse.Y - _previousMouse.Y;

        // 右鍵拖曳＝旋轉（俯視模式只轉方位角，維持垂直向下）。
        if (mouse.RightButton == ButtonState.Pressed)
        {
            Yaw -= dx * 0.005f;

            if (Mode == CameraMode.Orbit)
            {
                Pitch = Math.Clamp(
                    Pitch + (dy * 0.005f),
                    MathHelper.ToRadians(5f),
                    MathHelper.ToRadians(89f));
            }
        }

        // 中鍵拖曳＝平移，位移量隨距離縮放，遠近手感才一致。
        if (mouse.MiddleButton == ButtonState.Pressed && (dx != 0 || dy != 0))
        {
            float scale = Distance * 0.0015f;
            var forward = new Vector3(MathF.Cos(Yaw), MathF.Sin(Yaw), 0f);
            var right = new Vector3(-forward.Y, forward.X, 0f);
            Focus -= (right * dx * scale) + (forward * dy * scale);
        }

        int wheel = mouse.ScrollWheelValue - _previousMouse.ScrollWheelValue;
        if (wheel != 0)
            Distance = Math.Clamp(Distance * MathF.Pow(0.9f, wheel / 120f), 200f, MapExtent * 3f);
    }

    private void ApplyKeyboard(float dt)
    {
        var keyboard = Keyboard.GetState();

        float forwardInput = (keyboard.IsKeyDown(Keys.W) ? 1f : 0f) - (keyboard.IsKeyDown(Keys.S) ? 1f : 0f);
        float rightInput = (keyboard.IsKeyDown(Keys.D) ? 1f : 0f) - (keyboard.IsKeyDown(Keys.A) ? 1f : 0f);

        if (forwardInput == 0f && rightInput == 0f)
            return;

        var forward = new Vector3(MathF.Cos(Yaw), MathF.Sin(Yaw), 0f);
        var right = new Vector3(-forward.Y, forward.X, 0f);

        float speed = MoveSpeed * dt * (Distance / 6000f);
        Focus += ((forward * forwardInput) + (right * rightInput)) * speed;
    }

    private void Apply()
    {
        var camera = Camera.Instance;

        float pitch = Mode == CameraMode.TopDown ? MathHelper.ToRadians(89.9f) : Pitch;
        float fov = Mode == CameraMode.TopDown ? TopDownFov : OrbitFov;

        float horizontal = Distance * MathF.Cos(pitch);
        float vertical = Distance * MathF.Sin(pitch);

        var offset = new Vector3(
            -MathF.Cos(Yaw) * horizontal,
            -MathF.Sin(Yaw) * horizontal,
            vertical);

        camera.FOV = fov;

        // 預設遠平面只有 1800，但整張地圖有 25600 單位寬 —— 不放寬會直接被裁掉。
        camera.ViewNear = 20f;
        camera.ViewFar = Math.Max(8000f, Distance * 3f);

        camera.Position = Focus + offset;
        camera.Target = Focus;
    }
}
