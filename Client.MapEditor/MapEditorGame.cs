using Client.Main;
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

    public MapEditorGame(EditorOptions options)
    {
        _options = options;
        Window.Title = "MU 地圖編輯器";
        Window.AllowUserResizing = true;
    }

    protected override void Initialize()
    {
        base.Initialize();

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

        _imgui = new ImGuiRenderer(this, _session.Settings.FontSize);
        _ui = new EditorUi(this, _imgui, _session);
    }

    protected override void Draw(GameTime gameTime)
    {
        base.Draw(gameTime);

        if (_imgui is null || _ui is null)
            return;

        _imgui.BeginLayout(gameTime);
        _ui.Draw();
        _imgui.EndLayout();

        var io = ImGui.GetIO();
        if (ActiveScene is MapEditorScene scene)
            scene.UiCapturesInput = io.WantCaptureMouse || io.WantCaptureKeyboard || _ui.GizmoCapturesMouse;

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
        _imgui?.Dispose();
        base.UnloadContent();
    }
}

/// <param name="RunSeconds">大於 0 時進入自動化模式：跑滿這麼多秒就截圖並退出。</param>
public sealed record EditorOptions(int Width, int Height, double RunSeconds, string? ScreenshotPath, bool FullScreen = false)
{
    public bool IsAutomated => RunSeconds > 0;
}
