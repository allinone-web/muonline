using Client.AssetStudio.Rendering;
using Client.AssetStudio.Ui;
using Client.MapEditor;
using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.AssetStudio;

/// <param name="RunSeconds">大於 0 時進入自動化模式：跑滿這麼多秒就截圖並退出。</param>
public sealed record StudioOptions(
    int Width,
    int Height,
    double RunSeconds,
    string? ScreenshotPath,
    string? InitialSelection,
    string? InitialPanels,
    string? InitialKind,
    string? InitialLibraryAsset,
    int? InitialAction,
    bool StartPaused,
    bool ShowSkeleton,
    bool ConnectToServer,
    string? ConnectionString)
{
    public bool IsAutomated => RunSeconds > 0;
}

/// <summary>
/// 資源瀏覽器的宿主。
/// </summary>
/// <remarks>
/// <b>刻意不繼承 <c>MuGame</c></b>（地圖編輯器繼承了，因為它需要地形渲染管線）。
/// 這個工具要的是「開一個 .bmd 來看」，把整個遊戲拉起來會連帶要處理場景、網路設定、
/// 音效、後製、以及 <c>ENTRY_SCENE</c> 的生命週期，全部都與檢視一個模型無關。
/// 相對地，Client.Main 仍然被引用 —— 但只用它的<b>知識</b>
/// （怪物編號對應、動作列舉、技能圖示表），不用它的執行期。
/// </remarks>
public sealed class StudioGame : Game
{
    private readonly GraphicsDeviceManager _graphics;
    private readonly StudioOptions _options;
    private readonly StudioSession _session = StudioSession.Current;

    private ImGuiRenderer? _imgui;
    private ModelViewport? _viewport;
    private StudioUi? _ui;
    private double _elapsedSeconds;

    public StudioGame(StudioOptions options)
    {
        _options = options;

        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = options.Width,
            PreferredBackBufferHeight = options.Height,
            PreferredDepthStencilFormat = DepthFormat.Depth24,
        };

        // 從非 GUI session 啟動時 vsync 會讓主執行緒卡死在 Cocoa_GL_SwapWindow
        // （在等一個永遠不會來的 display link 訊號）—— 與地圖編輯器踩到的是同一個坑。
        if (options.IsAutomated)
        {
            _graphics.SynchronizeWithVerticalRetrace = false;
            IsFixedTimeStep = false;
        }

        Window.Title = "MU 資源瀏覽器";
        Window.AllowUserResizing = true;
        IsMouseVisible = true;
        Content.RootDirectory = "Content";
    }

    public ModelViewport Viewport => _viewport!;

    protected override void Initialize()
    {
        base.Initialize();

        // vsync 必須在圖形裝置建立之後再套一次：GraphicsDeviceManager 只有在
        // ApplyChanges 時才把它轉成 SDL_GL_SetSwapInterval。
        // 沒有這一步，從非 GUI 的工作階段啟動時主執行緒會卡死在
        // Cocoa_GL_SwapWindow → SDL_CondWait（等一個永遠不會來的 display link 訊號），
        // 而且沒有任何錯誤訊息 —— 看起來就只是「程式不動了」。
        if (_options.IsAutomated)
        {
            _graphics.SynchronizeWithVerticalRetrace = false;
            IsFixedTimeStep = false;
            _graphics.ApplyChanges();
        }

        _imgui = new ImGuiRenderer(this);
        _viewport = new ModelViewport(GraphicsDevice, _imgui);
        _ui = new StudioUi(this, _imgui, _viewport, _session);

        _session.StatusMessage = $"目錄：{_session.Catalog.Entries.Length} 筆　"
                               + $"（類別綁定 {_session.Catalog.Stats.ClassBound}、"
                               + $"孤兒模型 {_session.Catalog.Stats.OrphanModels}、"
                               + $"缺模型 {_session.Catalog.Stats.MissingModels}）";

        // 分類要先切。切分類會重設清單與選擇，
        // 所以 --open 放在它前面的話會被清掉（看起來就像 --open 沒作用）。
        if (_options.InitialKind is string initialKind && !_ui.SelectKind(initialKind))
            _session.Report($"沒有「{initialKind}」這個分類", failed: true);

        if (_options.InitialSelection is string wanted)
            SelectByName(wanted);

        // 自動化截圖時要能打開預設關著的面板 —— 否則終端機裡驗不到它們長什麼樣。
        if (_options.InitialPanels is string panels)
            _ui.OpenPanels(panels);

        if (_options.InitialLibraryAsset is string libraryAsset && !_ui.SelectLibraryAsset(libraryAsset))
            _session.Report($"資源庫裡沒有「{libraryAsset}」", failed: true);

        _viewport.ShowSkeleton = _options.ShowSkeleton;

        if (_options.ConnectionString is string connection)
            _session.Server.ConnectionString = connection;

        // 背景連線：連不上也要能用，所以不擋啟動。
        if (_options.ConnectToServer)
            _ = _session.ReloadServerAsync();
    }

    protected override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        _session.ApplyPendingServerData();
        ProcessLoadRequest();
        ProcessLibraryRequest();
        AdvanceAnimation(gameTime);
    }

    private void ProcessLoadRequest()
    {
        if (_session.Requested is not { } entry || _session.IsLoading)
            return;

        _session.Requested = null;
        _session.IsLoading = true;

        try
        {
            _session.Model?.Dispose();
            _session.Model = null;

            if (entry.FullPath is null)
            {
                _session.Selected = entry;
                _session.Report($"{entry.Name}：找不到模型檔 {entry.ModelPath}", failed: true);
                return;
            }

            var model = AnimatedModel.Load(GraphicsDevice, entry.FullPath);

            // 身體部位共用主模型的骨架，掛上去才看得到完整的 NPC／角色。
            foreach (var part in entry.BodyParts)
            {
                string full = System.IO.Path.Combine(_session.DataPath, part);
                if (File.Exists(full))
                    model.AttachPart(full);
            }

            _session.Model = model;
            _session.Selected = entry;
            _session.CurrentAction = _options.InitialAction is int action
                ? Math.Clamp(action, 0, Math.Max(model.ActionCount - 1, 0))
                : DefaultAction(entry, model);

            _session.AnimTime = 0d;

            // 自動化截圖要能停在確定的一格，否則每次跑出來的姿勢都不一樣，無法比對。
            if (_options.StartPaused)
                _session.Playing = false;

            _viewport!.Camera.Frame(model.Bounds);

            int missing = model.AllMeshes.Count(m => !m.Texture.Found);
            _session.Report(
                $"{entry.Name}：{model.AllMeshes.Count()} 網格"
                + (model.Parts.Count > 0 ? $"（含 {model.Parts.Count} 個身體部位）" : string.Empty)
                + $"、{model.BoneCount} 骨骼、{model.ActionCount} 動作"
                + (missing > 0 ? $"　注意：{missing} 個網格缺貼圖" : string.Empty),
                failed: missing > 0);
        }
        catch (Exception ex)
        {
            _session.Selected = entry;
            _session.Report($"{entry.ModelPath} 載入失敗：{ex.GetType().Name} {ex.Message}", failed: true);
        }
        finally
        {
            _session.IsLoading = false;
        }
    }

    /// <summary>
    /// 一開啟先播哪個動作。
    /// </summary>
    /// <remarks>
    /// 怪物的動作 0 就是待機（<c>MonsterActionType.Stop1</c>），直接用。
    /// 角色與 NPC 的動作 0 是 <c>Set</c> —— 那是設定用的姿勢，不是待機，
    /// 播出來是一個扭曲的姿勢，第一眼會以為模型壞了。
    /// 遊戲裡 <c>PlayerObject</c> 的預設是 <c>PlayerAction.PlayerStopMale</c>（1），照抄。
    /// </remarks>
    private static int DefaultAction(Catalog.EntityEntry entry, AnimatedModel model)
    {
        if (entry.Kind is not (Catalog.EntityKind.Npc or Catalog.EntityKind.Player))
            return 0;

        int stopMale = (int)Client.Main.Models.PlayerAction.PlayerStopMale;
        return stopMale < model.ActionCount ? stopMale : 0;
    }

    /// <summary>
    /// 把資源庫裡的自有資產掛上檢視器。
    /// </summary>
    /// <remarks>
    /// 走的是與遊戲原本資產完全相同的渲染路徑 —— 這正是重點：
    /// 「我的模型放進遊戲會長什麼樣」不該用另一個檢視器回答。
    /// </remarks>
    private void ProcessLibraryRequest()
    {
        if (_session.RequestedLibraryAsset is not { } request || _session.IsLoading)
            return;

        _session.RequestedLibraryAsset = null;
        _session.IsLoading = true;

        try
        {
            _session.Model?.Dispose();

            var model = AnimatedModel.FromBmd(
                GraphicsDevice, request.Model.Model,
                System.IO.Path.Combine(request.TextureDirectory, request.Asset.Id),
                request.TextureDirectory);

            _session.Model = model;

            // 自有資產不在目錄裡，所以清掉選取 —— 右邊的伺服器面板不該顯示上一隻怪的數值。
            _session.Selected = null;
            _session.CurrentAction = 0;
            _session.AnimTime = 0d;

            _viewport!.Camera.Frame(model.Bounds);

            int missing = model.AllMeshes.Count(m => !m.Texture.Found);
            _session.Report(
                $"{request.Asset.Name}：{model.AllMeshes.Count()} 網格、{model.BoneCount} 骨骼、"
              + $"{model.ActionCount} 動作"
              + (missing > 0 ? $"　注意：{missing} 個網格缺貼圖" : string.Empty),
                failed: missing > 0);
        }
        catch (Exception ex)
        {
            _session.Report($"{request.Asset.Name} 載入失敗：{ex.GetType().Name} {ex.Message}", failed: true);
        }
        finally
        {
            _session.IsLoading = false;
        }
    }

    private void AdvanceAnimation(GameTime gameTime)
    {
        var model = _session.Model;
        if (model is null)
            return;

        int action = Math.Clamp(_session.CurrentAction, 0, Math.Max(model.ActionCount - 1, 0));

        if (_session.Playing)
        {
            _session.AnimTime = model.Advance(
                _session.AnimTime,
                action,
                (float)gameTime.ElapsedGameTime.TotalSeconds,
                _session.AnimationSpeed);
        }

        var (frame0, frame1, blend) = model.Apply(action, _session.AnimTime);
        _session.Frame0 = frame0;
        _session.Frame1 = frame1;
        _session.FrameBlend = blend;
    }

    protected override void Draw(GameTime gameTime)
    {
        if (_imgui is null || _ui is null)
            return;

        // 順序：先把 3D 畫進 render target，再切回背景緩衝並清畫面。
        // 反過來的話，切回背景緩衝時 RenderTargetUsage.DiscardContents 會把剛清好的內容丟掉。
        _ui.PrepareFrame();

        GraphicsDevice.SetRenderTarget(null);
        GraphicsDevice.Clear(new Color(18, 19, 23));

        _imgui.BeginLayout(gameTime);
        _ui.Draw();
        _imgui.EndLayout();

        _elapsedSeconds += gameTime.ElapsedGameTime.TotalSeconds;

        if (_options.IsAutomated && _elapsedSeconds >= _options.RunSeconds)
        {
            if (_options.ScreenshotPath is not null)
                SaveScreenshot(_options.ScreenshotPath);

            Exit();
        }

        base.Draw(gameTime);
    }

    private void SelectByName(string wanted)
    {
        var entry = _session.Catalog.Entries.FirstOrDefault(e =>
                        e.Name.Equals(wanted, StringComparison.OrdinalIgnoreCase)
                     || e.ClassName?.Equals(wanted, StringComparison.OrdinalIgnoreCase) == true
                     || e.ModelPath.EndsWith(wanted, StringComparison.OrdinalIgnoreCase))
                    // 精確比對找不到就退成子字串。名稱是「幻影騎士 Illusion Knight」
                    // 這種中英並列的形式，要求打完整串太苛刻。
                 ?? _session.Catalog.Entries.FirstOrDefault(e =>
                        e.Name.Contains(wanted, StringComparison.OrdinalIgnoreCase)
                     || e.Detail.Contains(wanted, StringComparison.OrdinalIgnoreCase));

        if (entry is not null)
        {
            _session.Select(entry);
            _ui!.RevealInCatalog(entry);
        }
        else
        {
            _session.Report($"找不到「{wanted}」", failed: true);
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

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        using var stream = File.Create(path);
        texture.SaveAsPng(stream, width, height);

        Console.WriteLine($"截圖：{path}");
    }

    protected override void UnloadContent()
    {
        _session.Model?.Dispose();
        _viewport?.Dispose();
        _imgui?.Dispose();
        base.UnloadContent();
    }
}
