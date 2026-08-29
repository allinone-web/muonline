using Client.AssetStudio.Rendering;
using Client.MapEditor;
using ImGuiNET;
using NVector2 = System.Numerics.Vector2;
using NVector4 = System.Numerics.Vector4;

namespace Client.AssetStudio.Ui;

/// <summary>
/// 所有 ImGui 面板。每幀由 <see cref="StudioGame.Draw"/> 呼叫一次。
/// </summary>
/// <remarks>
/// 版面的設計前提只有一句：<b>外觀在客戶端，行為在伺服器。</b>
/// 所以「模型 / 動作」與「伺服器數值」是同一個選取物件的兩側，永遠並排 ——
/// 把它們拆到不同畫面的話，使用者會改了半天 <c>.bmd</c> 才發現攻擊速度沒變。
/// </remarks>
public sealed partial class StudioUi
{
    private static readonly NVector4 Warning = new(1f, 0.65f, 0.2f, 1f);
    private static readonly NVector4 Danger = new(0.95f, 0.42f, 0.4f, 1f);
    private static readonly NVector4 Muted = new(0.6f, 0.62f, 0.66f, 1f);
    private static readonly NVector4 Good = new(0.5f, 0.82f, 0.55f, 1f);

    private readonly StudioGame _game;
    private readonly ImGuiRenderer _imgui;
    private readonly ModelViewport _viewport;
    private readonly StudioSession _session;
    private readonly BoundedThumbnailCache _thumbnails;
    private TexturePreviewCache _previews;

    /// <summary>視埠上一幀量到的大小。render target 在 ImGui 佈局之前就要畫好，所以慢一幀。</summary>
    private int _viewportWidth = 960;
    private int _viewportHeight = 640;
    private IntPtr? _viewportTexture;

    public StudioUi(StudioGame game, ImGuiRenderer imgui, ModelViewport viewport, StudioSession session)
    {
        _game = game;
        _imgui = imgui;
        _viewport = viewport;
        _session = session;
        _thumbnails = new BoundedThumbnailCache(game.GraphicsDevice, imgui);
        _previews = new TexturePreviewCache(game.GraphicsDevice, imgui);

        _exportDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Documents", "mu-export");
    }

    /// <summary>
    /// 在清畫面與 ImGui 佈局之前把 3D 畫進 render target。
    /// </summary>
    /// <remarks>
    /// 順序很重要：在 ImGui 佈局<b>中間</b>切 render target，回到背景緩衝時
    /// <c>RenderTargetUsage.DiscardContents</c> 會把剛清好的畫面丟掉 ——
    /// 症狀是背景隨機閃爍，而且只在某些驅動上出現。
    /// </remarks>
    public void PrepareFrame()
    {
        _viewportTexture = _viewport.Render(_session.Model, _viewportWidth, _viewportHeight);
    }

    /// <summary>次要面板預設關著。一開啟就把六個視窗全攤在畫面上，第一眼只會看到一團重疊的框。</summary>
    private bool _showSkills;
    private bool _showExport;

    public void Draw()
    {
        DrawMenuBar();

        ImGui.DockSpaceOverViewport(0, ImGui.GetMainViewport(), ImGuiDockNodeFlags.None);

        _thumbnails.BeginFrame();

        DrawCatalogPanel();
        DrawViewportPanel();
        DrawAnimationPanel();
        DrawModelPanel();
        DrawServerPanel();

        if (_showSkills)
            DrawSkillPanel();

        if (_showExport)
            DrawExportPanel();

        DrawStatusBar();
    }

    /// <summary>逗號分隔的面板名稱（skills、export）。給 <c>--panels</c> 用。</summary>
    public void OpenPanels(string panels)
    {
        foreach (var panel in panels.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (panel.Equals("skills", StringComparison.OrdinalIgnoreCase))
                _showSkills = true;
            else if (panel.Equals("export", StringComparison.OrdinalIgnoreCase))
                _showExport = true;
        }
    }

    private void DrawMenuBar()
    {
        if (!ImGui.BeginMainMenuBar())
            return;

        if (ImGui.BeginMenu("視窗"))
        {
            ImGui.MenuItem("魔法（技能）", string.Empty, ref _showSkills);
            ImGui.MenuItem("匯出 glTF", string.Empty, ref _showExport);
            ImGui.EndMenu();
        }

        // 選取的物件是這個工具的主體，放在選單列上永遠看得到。
        if (_session.Selected is { } selected)
        {
            ImGui.Separator();
            ImGui.TextColored(Muted, selected.Number >= 0
                ? $"{selected.Name}　#{selected.Number}　{selected.ModelPath}"
                : $"{selected.Name}　{selected.ModelPath}");
        }

        ImGui.EndMainMenuBar();
    }

    /// <summary>
    /// 首次啟動的預設版面。與地圖編輯器同樣用 <c>FirstUseEver</c> 而不是 DockBuilder ——
    /// 使用者拖過之後 ImGui 會記進 <c>imgui.ini</c>，不會每幀被重設回去。
    /// </summary>
    private static void PlaceWindow(string panel)
    {
        var viewport = ImGui.GetMainViewport();
        var origin = viewport.WorkPos;
        var size = viewport.WorkSize;

        const float leftWidth = 330f;
        const float rightWidth = 400f;
        const float statusHeight = 30f;
        float bottomHeight = MathF.Max(190f, size.Y * 0.24f);
        float centerWidth = MathF.Max(320f, size.X - leftWidth - rightWidth);
        float centerHeight = size.Y - bottomHeight - statusHeight;

        (float x, float y, float w, float h) = panel switch
        {
            "資源目錄" => (origin.X, origin.Y, leftWidth, size.Y - statusHeight),
            "檢視" => (origin.X + leftWidth, origin.Y, centerWidth, centerHeight),
            "動作" => (origin.X + leftWidth, origin.Y + centerHeight, centerWidth, bottomHeight),
            "模型" => (origin.X + size.X - rightWidth, origin.Y, rightWidth, size.Y * 0.46f),
            "伺服器數值" => (origin.X + size.X - rightWidth, origin.Y + (size.Y * 0.46f), rightWidth, size.Y * 0.54f - statusHeight),
            "魔法" => (origin.X + leftWidth + 40f, origin.Y + 40f, 900f, 620f),
            _ => (origin.X + leftWidth + 80f, origin.Y + 80f, 620f, 460f),
        };

        ImGui.SetNextWindowPos(new NVector2(x, y), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new NVector2(w, h), ImGuiCond.FirstUseEver);
    }

    private void DrawStatusBar()
    {
        var viewport = ImGui.GetMainViewport();
        const float height = 28f;

        ImGui.SetNextWindowPos(new NVector2(viewport.WorkPos.X, viewport.WorkPos.Y + viewport.WorkSize.Y - height));
        ImGui.SetNextWindowSize(new NVector2(viewport.WorkSize.X, height));

        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize
                                     | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollbar
                                     | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoDocking;

        if (ImGui.Begin("##status", flags))
        {
            ImGui.TextColored(Muted, _session.StatusMessage);

            if (_session.ActionMessage is string message)
            {
                ImGui.SameLine();
                ImGui.TextColored(_session.ActionFailed ? Warning : Good, "｜ " + message);
            }

            ImGui.SameLine(ImGui.GetWindowWidth() - 110f);
            ImGui.TextColored(Muted, $"{ImGui.GetIO().Framerate:F0} FPS");
        }

        ImGui.End();
    }

    /// <summary>在 Finder 裡打開一個路徑。工具鏈的最後一公里，省下手動貼路徑。</summary>
    private void RevealInFinder(string path)
    {
        try
        {
            System.Diagnostics.Process.Start("open", ["-R", path]);
        }
        catch (Exception ex)
        {
            _session.Report($"開啟 Finder 失敗：{ex.Message}", failed: true);
        }
    }

    private static void HelpMarker(string text)
    {
        ImGui.SameLine();
        ImGui.TextColored(Muted, "(?)");

        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 28f);
            ImGui.TextUnformatted(text);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes / (1024.0 * 1024.0):F1} MB",
    };
}
