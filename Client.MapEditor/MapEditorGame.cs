using Client.Main;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Scenes;
using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MuAssets.Core;

namespace Client.MapEditor;

/// <summary>
/// 編輯器宿主。直接繼承 <see cref="MuGame"/> —— 編輯器就是「換一個進入場景的遊戲」，
/// 地形渲染、BMD 載入、貼圖管線、主執行緒排程全部原樣沿用。
/// </summary>
/// <remarks>
/// ImGui 疊層畫在 <c>base.Draw</c> 之後：MuGame 會先把場景畫進中介 render target 再做
/// FXAA / AlphaRGB 後製，UI 若混在裡面會一起被後製糊掉。
/// </remarks>
public sealed class MapEditorGame : MuGame
{
    private readonly EditorOptions _options;
    private readonly EditorSession _session = EditorSession.Current;

    private ImGuiRenderer? _imgui;
    private EditorUi? _ui;
    private double _elapsedSeconds;
    private double _lastDiagnosticSecond = -1.0;
    private int _diagnosticFrames;
    private double _sceneMs;

    private double _uiMs;

    public MapEditorGame(EditorOptions options)
    {
        _options = options;
        Window.Title = "MU 地圖編輯器";
        Window.AllowUserResizing = true;
    }

    protected override void Initialize()
    {
        base.Initialize();

        // 畫質預設在 base.Initialize 裡套用，所以覆寫只能在這之後。
        // 只有使用者明講 --grass 才動它 —— 預設維持跟遊戲一致。
        if (EditorSession.Current.ForceGrass)
            Constants.DRAW_GRASS = true;

        Constants.GRASS_TUFTS_PER_TILE = EditorSession.Current.GrassDensity;
        Constants.GRASS_CLUSTER_PLANES = EditorSession.Current.GrassPlanes;
        Constants.GRASS_DRAW_DISTANCE = EditorSession.Current.GrassDistance;

        if (Services.GetService(typeof(IGraphicsDeviceManager)) is GraphicsDeviceManager graphics)
        {
            graphics.PreferredBackBufferWidth = _options.Width;
            graphics.PreferredBackBufferHeight = _options.Height;

            // 全螢幕能拿到螢幕原生解析度，避開「視窗不是 HiDPI 所以被系統放大」的模糊。
            if (_options.FullScreen)
            {
                var display = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode;
                graphics.PreferredBackBufferWidth = display.Width;
                graphics.PreferredBackBufferHeight = display.Height;
                graphics.IsFullScreen = true;
                graphics.HardwareModeSwitch = false;
            }

            // 自動化模式（--frames）必須關掉 vsync：從非 GUI session 啟動時，
            // SDL 的 Cocoa_GL_SwapWindow 會卡在等 display link 訊號，主執行緒就此死鎖。
            if (_options.IsAutomated)
            {
                graphics.SynchronizeWithVerticalRetrace = false;
                IsFixedTimeStep = false;
            }

            graphics.ApplyChanges();
        }

        // MuGame.Initialize 會關掉系統游標（遊戲有自己的游標貼圖），編輯器要用回系統游標。
        IsMouseVisible = true;

        // 編輯器不吃遊戲存下來的效能取捨。實測那份設定是 RENDER_SCALE = 0.9：
        // 場景先畫成 0.9 倍再放大回來，畫面糊掉，而且因為每幀都得跑一次後製，
        // 「場景沒變就重貼」的快取路徑永遠用不到。1:1 之後兩件事同時解決。
        Constants.RENDER_SCALE = 1f;
        Constants.MSAA_ENABLED = false;
        GraphicsManager.Instance.UpdateRenderScale();

        _imgui = new ImGuiRenderer(this, _session.Settings.FontSize);
        _ui = new EditorUi(this, _imgui, _session);
    }

    /// <summary>
    /// MU_EDITOR_DIAG=1 時每秒印一次視窗與緩衝區的幾何。
    /// 點擊錯位的成因全都在這幾個數字之間的不一致上，用猜的沒有意義。
    /// </summary>
    private void LogGeometry()
    {
        if (Environment.GetEnvironmentVariable("MU_EDITOR_DIAG") is null)
            return;

        _diagnosticFrames++;

        if (_elapsedSeconds - _lastDiagnosticSecond < 1.0)
            return;

        double fps = _diagnosticFrames / Math.Max(1e-6, _elapsedSeconds - _lastDiagnosticSecond);
        double sceneMs = _sceneMs / Math.Max(1, _diagnosticFrames);
        double uiMs = _uiMs / Math.Max(1, _diagnosticFrames);
        _diagnosticFrames = 0;
        _sceneMs = 0;
        _uiMs = 0;
        _lastDiagnosticSecond = _elapsedSeconds;

        var bounds = Window.ClientBounds;
        var parameters = GraphicsDevice.PresentationParameters;
        var viewport = GraphicsDevice.Viewport;
        var mouse = Microsoft.Xna.Framework.Input.Mouse.GetState();
        var io = ImGuiNET.ImGui.GetIO();

        Console.WriteLine(
            $"[diag] {fps:F1} fps（場景 {sceneMs:F1}ms、介面 {uiMs:F1}ms）  視窗 {bounds.Width}x{bounds.Height} @({bounds.X},{bounds.Y})  " +
            $"緩衝區 {parameters.BackBufferWidth}x{parameters.BackBufferHeight}  " +
            $"視區 {viewport.Width}x{viewport.Height}  " +
            $"ImGui.DisplaySize {io.DisplaySize.X}x{io.DisplaySize.Y}  " +
            $"滑鼠原始 ({mouse.X},{mouse.Y}) → ImGui ({io.MousePos.X:F0},{io.MousePos.Y:F0})");
    }

    /// <summary>
    /// MU_EDITOR_DIAG=1 時，在 ImGui 認為的滑鼠位置畫一個十字準星。
    /// </summary>
    /// <remarks>
    /// 「點擊錯位」有兩種完全不同的成因，而它們的修法無關：
    /// 準星和實際游標對得上 → 座標沒問題，是那一幀根本沒取樣到這次點擊（幀率太低）；
    /// 對不上 → 座標換算錯了，差多少一眼就看得出來。
    /// 用猜的分不出來，所以直接畫出來。
    /// </remarks>
    private static void DrawCursorProbe()
    {
        if (Environment.GetEnvironmentVariable("MU_EDITOR_DIAG") is null)
            return;

        var io = ImGui.GetIO();
        var position = io.MousePos;

        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y))
            return;

        var drawList = ImGui.GetForegroundDrawList();
        uint color = ImGui.GetColorU32(new System.Numerics.Vector4(1f, 0.2f, 0.2f, 1f));

        drawList.AddLine(position with { X = position.X - 24f }, position with { X = position.X + 24f }, color, 1.5f);
        drawList.AddLine(position with { Y = position.Y - 24f }, position with { Y = position.Y + 24f }, color, 1.5f);
        drawList.AddCircle(position, 9f, color, 24, 1.5f);
        drawList.AddText(
            position with { X = position.X + 14f, Y = position.Y + 10f },
            color,
            $"{position.X:F0},{position.Y:F0}");
    }

    protected override void Draw(GameTime gameTime)
    {
        long sceneStart = System.Diagnostics.Stopwatch.GetTimestamp();

        base.Draw(gameTime);

        _sceneMs += System.Diagnostics.Stopwatch.GetElapsedTime(sceneStart).TotalMilliseconds;

        if (_imgui is null || _ui is null)
            return;

        LogGeometry();

        long uiStart = System.Diagnostics.Stopwatch.GetTimestamp();

        // 拍黃金影像時不畫介面：基準圖要比的是「渲染」，不是面板佈局 ——
        // 面板寬度改一格就整張紅的比對，沒有人會留著。
        // 注意這裡不能提早 return：截圖與退出的邏輯就在下面同一個方法裡。
        if (EditorSession.Current.GoldenShot is null)
        {
            _imgui.BeginLayout(gameTime);
            _ui.Draw();
            DrawCursorProbe();
            _imgui.EndLayout();
            _uiMs += System.Diagnostics.Stopwatch.GetElapsedTime(uiStart).TotalMilliseconds;

            var io = ImGui.GetIO();
            if (ActiveScene is MapEditorScene scene)
                scene.UiCapturesInput = io.WantCaptureMouse || io.WantCaptureKeyboard || _ui.GizmoCapturesMouse;
        }

        // 用「經過的時間」而不是幀數：關掉 vsync 後這支程式跑到兩千多 FPS，
        // 以幀數計時等於還沒等到世界載完就截圖了。
        _elapsedSeconds += gameTime.ElapsedGameTime.TotalSeconds;

        if (_options.IsAutomated && _elapsedSeconds >= _options.RunSeconds)
        {
            if (_options.ScreenshotPath is not null)
                SaveScreenshot(_options.ScreenshotPath);

            Exit();
        }
    }

    private void SaveScreenshot(string path)
    {
        int width = GraphicsDevice.PresentationParameters.BackBufferWidth;
        int height = GraphicsDevice.PresentationParameters.BackBufferHeight;

        var pixels = new Color[width * height];
        GraphicsDevice.GetBackBufferData(pixels);

        using var texture = new Texture2D(GraphicsDevice, width, height);
        texture.SetData(pixels);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        using var stream = File.Create(path);
        texture.SaveAsPng(stream, width, height);

        Console.WriteLine($"截圖：{path}");
    }

    protected override void UnloadContent()
    {
        // 順序有意義：UI 要先把貼圖解綁並釋放，再讓 ImGuiRenderer 收掉剩下的。
        _ui?.Dispose();
        _imgui?.Dispose();
        base.UnloadContent();
    }
}

/// <param name="RunSeconds">大於 0 時進入自動化模式：跑滿這麼多秒就截圖並退出。</param>
public sealed record EditorOptions(int Width, int Height, double RunSeconds, string? ScreenshotPath, bool FullScreen = false)
{
    public bool IsAutomated => RunSeconds > 0;
}
