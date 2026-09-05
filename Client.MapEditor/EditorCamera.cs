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
///
/// 操作（<see cref="ControlsHelp"/> 是同一份說明，改這裡也要改那裡）：
///   空白鍵 + 左鍵拖曳 ── 平移（抓手）。主要的看圖手段。
///   中鍵拖曳           ── 平移。有三鍵滑鼠時比較順，但 Mac 觸控板／Magic Mouse 沒有中鍵，
///                         所以不能只留這一種 —— 這正是先前「只能靠『相機對準』按鈕」的原因。
///   右鍵拖曳           ── 旋轉。
///   滾輪               ── 縮放。
///   WASD／方向鍵       ── 平移焦點；Q／E 升降；Shift 加速、Ctrl 減速。
/// </remarks>
public sealed class EditorCamera
{
    private const float TileScale = Constants.TERRAIN_SCALE;
    private const float MapExtent = Constants.TERRAIN_SIZE * TileScale;

    private const float OrbitFov = 35f;
    private const float TopDownFov = 12f;

    /// <summary>視口高度取不到時的退路（預設視窗高度量級）。</summary>
    private const float FallbackViewportHeight = 1080f;

    /// <summary>操作說明。UI 面板直接顯示這一份，不要另外抄一份在別處。</summary>
    public const string ControlsHelp =
        "空白鍵+左鍵拖曳 或 中鍵拖曳：平移\n" +
        "右鍵拖曳：旋轉\n" +
        "滾輪：縮放\n" +
        "WASD／方向鍵：平移　Q/E：升降\n" +
        "Shift：加速　Ctrl：減速";

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

    /// <summary>
    /// 這一幀正在用抓手平移。畫筆要看它 —— 空白鍵拖曳時左鍵是在移動鏡頭，不是在下筆，
    /// 不擋的話拖到哪裡就畫到哪裡。
    /// </summary>
    public bool IsPanning { get; private set; }

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

    /// <param name="acceptInput">滑鼠與鍵盤一起開關。</param>
    public void Update(GameTime time, bool acceptInput)
        => Update(time, acceptInput, acceptInput);

    /// <remarks>
    /// 滑鼠與鍵盤分開判斷。合成一個旗標的話，ImGui 只要有輸入框拿到焦點
    /// （<c>WantCaptureKeyboard</c>）就連帶把滑鼠轉鏡頭一起關掉，
    /// 於是「在面板裡改完一個數字，回到 3D 視圖卻推不動鏡頭」。
    /// </remarks>
    /// <param name="acceptMouse">ImGui 佔用滑鼠時傳 false，否則在面板上拖曳會連帶轉相機。</param>
    /// <param name="acceptKeyboard">ImGui 有輸入框在打字時傳 false，否則打 "wasd" 會把鏡頭推走。</param>
    public void Update(GameTime time, bool acceptMouse, bool acceptKeyboard)
    {
        var mouse = Mouse.GetState();
        var keyboard = Keyboard.GetState();

        if (!_initialized)
        {
            _previousMouse = mouse;
            _initialized = true;
        }

        IsPanning = false;

        if (acceptMouse)
            ApplyMouse(mouse, keyboard, acceptKeyboard);

        if (acceptKeyboard)
            ApplyKeyboard(keyboard, (float)time.ElapsedGameTime.TotalSeconds);

        _previousMouse = mouse;
        Apply();
    }

    /// <summary>
    /// 一個螢幕像素等於多少世界單位（在焦點所在的深度上）。
    /// </summary>
    /// <remarks>
    /// 先前是寫死的 <c>Distance * 0.0015f</c>，那是照 Orbit 的 FOV 35 調出來的。
    /// 俯視模式的 FOV 只有 12，可見範圍窄得多，同一條係數會讓平移快上約七倍 ——
    /// 在俯視下輕輕一拖就飛出地圖，這是「查看地圖非常不方便」的主因之一。
    /// 改成照投影算：可見高度 = 2 · 距離 · tan(FOV/2)，除以視口像素高就是每像素的世界單位。
    /// </remarks>
    private float WorldUnitsPerPixel()
    {
        float fov = Mode == CameraMode.TopDown ? TopDownFov : OrbitFov;
        float viewportHeight = MuGame.Instance?.GraphicsDevice?.Viewport.Height ?? 0;

        if (viewportHeight <= 0f)
            viewportHeight = FallbackViewportHeight;

        return 2f * Distance * MathF.Tan(MathHelper.ToRadians(fov) / 2f) / viewportHeight;
    }

    private void ApplyMouse(MouseState mouse, KeyboardState keyboard, bool acceptKeyboard)
    {
        int dx = mouse.X - _previousMouse.X;
        int dy = mouse.Y - _previousMouse.Y;

        // 抓手：空白鍵按住時左鍵＝平移。跟 Photoshop／Blender／Figma 一致，
        // 而且不需要中鍵 —— 觸控板與 Magic Mouse 都用得了。
        bool handTool = acceptKeyboard && keyboard.IsKeyDown(Keys.Space);
        bool panDrag = (handTool && mouse.LeftButton == ButtonState.Pressed)
                       || mouse.MiddleButton == ButtonState.Pressed;

        // 抓手一按住就算數（還沒拖曳也一樣），畫筆才不會在按下的那一刻先點出一筆。
        IsPanning = handTool && mouse.LeftButton == ButtonState.Pressed;

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

        if (panDrag && (dx != 0 || dy != 0))
            Pan(dx, dy);

        int wheel = mouse.ScrollWheelValue - _previousMouse.ScrollWheelValue;
        if (wheel != 0)
            Distance = Math.Clamp(Distance * MathF.Pow(0.9f, wheel / 120f), 200f, MapExtent * 3f);
    }

    /// <summary>照滑鼠位移平移焦點。dx/dy 是螢幕像素。</summary>
    private void Pan(int dx, int dy)
    {
        float scale = WorldUnitsPerPixel();

        // 螢幕上的「往上拖」要對應到地面上遠離相機的方向；地面前進方向由 Yaw 決定。
        // 俯視時投影到地面的前進向量會縮短，除以 cos(pitch) 補回來，
        // 不然越接近正上方往下看、拖曳越黏。
        float pitch = Mode == CameraMode.TopDown ? MathHelper.ToRadians(89.9f) : Pitch;
        float forwardScale = scale / MathF.Max(MathF.Cos(pitch), 0.05f);

        var forward = new Vector3(MathF.Cos(Yaw), MathF.Sin(Yaw), 0f);
        var right = new Vector3(-forward.Y, forward.X, 0f);

        Focus -= (right * dx * scale) + (forward * dy * forwardScale);
        ClampFocus();
    }

    private void ApplyKeyboard(KeyboardState keyboard, float dt)
    {
        float forwardInput =
            (keyboard.IsKeyDown(Keys.W) || keyboard.IsKeyDown(Keys.Up) ? 1f : 0f)
            - (keyboard.IsKeyDown(Keys.S) || keyboard.IsKeyDown(Keys.Down) ? 1f : 0f);

        float rightInput =
            (keyboard.IsKeyDown(Keys.D) || keyboard.IsKeyDown(Keys.Right) ? 1f : 0f)
            - (keyboard.IsKeyDown(Keys.A) || keyboard.IsKeyDown(Keys.Left) ? 1f : 0f);

        float upInput = (keyboard.IsKeyDown(Keys.E) ? 1f : 0f) - (keyboard.IsKeyDown(Keys.Q) ? 1f : 0f);

        if (forwardInput == 0f && rightInput == 0f && upInput == 0f)
            return;

        float speed = MoveSpeed * dt * (Distance / 6000f);

        if (keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift))
            speed *= 3f;

        if (keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl))
            speed *= 0.25f;

        var forward = new Vector3(MathF.Cos(Yaw), MathF.Sin(Yaw), 0f);
        var right = new Vector3(-forward.Y, forward.X, 0f);

        Focus += ((forward * forwardInput) + (right * rightInput)) * speed;
        Focus = Focus with { Z = Focus.Z + (upInput * speed) };
        ClampFocus();
    }

    /// <summary>
    /// 焦點限制在地圖範圍外擴一格的框裡。放任它飄到幾萬單位外的話，
    /// 畫面上什麼都沒有、又看不出自己在哪，只能重開 —— 這也是先前很難用的一環。
    /// </summary>
    private void ClampFocus()
    {
        const float margin = TileScale;
        Focus = new Vector3(
            Math.Clamp(Focus.X, -margin, MapExtent + margin),
            Math.Clamp(Focus.Y, -margin, MapExtent + margin),
            Math.Clamp(Focus.Z, -MapExtent, MapExtent));
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
