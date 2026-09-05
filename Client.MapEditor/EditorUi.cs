using Client.Data.ATT;
using Client.Data.MAP;
using Client.Main;
using Client.Main.Controls;
using ImGuiNET;
using Microsoft.Xna.Framework;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;
using NVector4 = System.Numerics.Vector4;
using MuAssets.Core;

namespace Client.MapEditor;

/// <summary>
/// 所有 ImGui 面板。每幀由 <see cref="MapEditorGame.Draw"/> 呼叫一次。
/// </summary>
public sealed class EditorUi : IDisposable
{
    private static readonly NVector4 Warning = new(1f, 0.65f, 0.2f, 1f);
    /// <summary>
    /// 把框選的框畫出來。
    /// </summary>
    /// <remarks>
    /// 畫在 ImGui 的前景層上，和手柄同一個做法 —— 不必寫 shader、不必管深度，
    /// 而且框永遠在最上面。沒有這個框的話，框選等於閉著眼睛拉。
    ///
    /// 滑鼠座標與 ImGui 的版面座標一致（實測視窗與緩衝區是 1:1），
    /// 所以可以直接拿來畫。
    /// </remarks>
    private void DrawBoxSelection()
    {
        if (_session.BoxSelectStart is not { } start || _session.BoxSelectCurrent is not { } current)
            return;

        var a = new NVector2(MathF.Min(start.X, current.X), MathF.Min(start.Y, current.Y));
        var b = new NVector2(MathF.Max(start.X, current.X), MathF.Max(start.Y, current.Y));

        if (b.X - a.X < 2f && b.Y - a.Y < 2f)
            return;

        var drawList = ImGui.GetForegroundDrawList();

        drawList.AddRectFilled(a, b, ImGui.GetColorU32(new NVector4(0.35f, 0.6f, 1f, 0.15f)));
        drawList.AddRect(a, b, ImGui.GetColorU32(new NVector4(0.5f, 0.75f, 1f, 0.9f)), 0f, 0, 1.5f);
    }

    /// <summary>上一幀看的是哪張圖，用來偵測換圖。</summary>
    private int _cachedWorldIndex = -1;

    /// <summary>
    /// 換地圖時把 GPU 上的預覽與縮圖丟掉。
    /// </summary>
    /// <remarks>
    /// 貼圖與模型都是逐圖一套的（MU 沒有共用素材目錄），換圖之後上一張的
    /// 再也用不到。原本兩個快取都只增不減，逛完 80 張圖就是幾百 MB 的
    /// GPU 記憶體留在那裡不會回來 —— 而且沒有任何症狀，直到記憶體不夠。
    /// </remarks>
    private void ReleaseCachesOnWorldChange()
    {
        int current = _session.LoadedWorldIndex ?? -1;

        if (current == _cachedWorldIndex)
            return;

        _cachedWorldIndex = current;
        _thumbnails.Clear();
        _previews.Clear();
        _objectSummaryWorldIndex = -1;
    }

    public void Dispose()
    {
        _thumbnails.Dispose();
        _previews.Dispose();
        _layerView.Dispose();
    }

    private static readonly NVector4 Danger = new(1f, 0.42f, 0.4f, 1f);

    // 新建地圖的欄位。200 起跳是為了離官方的 0–92 遠一點，不會撞號。
    private int _newWorldIndex = 200;
    private string _newWorldName = string.Empty;
    private int _newWorldDonor = NewMapScaffold.DefaultDonorWorld;
    private static readonly NVector4 Muted = new(0.6f, 0.62f, 0.66f, 1f);
    private static readonly NVector4 Normal = new(0.88f, 0.9f, 0.92f, 1f);

    private readonly MapEditorGame _game;
    private readonly EditorSession _session;
    private readonly TexturePreviewCache _previews;
    private readonly LayerView _layerView;
    private readonly TransformGizmo _gizmo = new();
    private readonly ThumbnailCache _thumbnails;
    private readonly AssetCatalog _catalog;

    private string _worldFilter = string.Empty;
    private bool _showOnlyPlayable = true;
    private float _thumbnailSize = 96f;
    private ObjectSummary[] _objectSummary = [];
    private int _objectSummaryWorldIndex = -1;
    private AssetEntry[] _assets = [];
    private int _assetWorldIndex = -1;

    /// <summary>
    /// 正在挑「要把這種物件換成什麼」。有值時素材庫進入挑選模式，
    /// 點一個模型就整張圖換掉。null = 一般模式。
    /// </summary>
    private short? _replaceFromType;

    /// <summary>換型時要套的縮放倍率。1 = 不動；建議值由兩個模型的高度比算出來。</summary>
    private float _replaceScale = 1f;

    /// <summary>挑選模式下只顯示同類別的模型（換樹就給樹）。關掉可以跨類別換。</summary>
    private bool _replaceSameCategoryOnly = true;

    /// <summary>素材庫裡被選起來的模型，用來批次標註。</summary>
    private readonly HashSet<string> _selectedAssets = [];

    /// <summary>Shift 範圍選取的錨點。</summary>
    private string? _assetAnchor;
    private string _assetFilter = string.Empty;
    private AssetCategory _categoryFilter = AssetCategory.Unclassified;
    private float _assetThumbnailSize = 96f;
    private readonly Dictionary<string, string[]> _assetTextures = new(StringComparer.OrdinalIgnoreCase);
    private MapObjectInstance? _transformBefore;
    private string _spawnFilter = string.Empty;
    private bool _autoValidateOnce = true;
    private bool _gizmoActive;

    public EditorUi(MapEditorGame game, ImGuiRenderer imgui, EditorSession session)
    {
        _game = game;
        _session = session;
        _previews = new TexturePreviewCache(game.GraphicsDevice, imgui);
        _layerView = new LayerView(game.GraphicsDevice, imgui);
        _thumbnails = new ThumbnailCache(game.GraphicsDevice, imgui);

        // 個人標註存在使用者目錄，不污染遊戲資源；
        // 共用標註跟著 repo 走（tools/assets/object-catalog.json），大家看到同一份。
        // 個人的優先 —— 自己標過的不該被共用檔蓋掉。
        _catalog = new AssetCatalog(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".mu-editor",
                "object-catalog.json"),
            AssetCatalog.SharedCatalogPath);
    }

    public void Draw()
    {
        // PassthruCentralNode：中央留空讓 3D 視埠透出來，面板停靠在四周。
        ImGui.DockSpaceOverViewport(0, ImGui.GetMainViewport(), ImGuiDockNodeFlags.PassthruCentralNode);

        ReleaseCachesOnWorldChange();
        DrawBoxSelection();
        _thumbnails.BeginFrame();

        DrawGizmo();

        DrawWorldList();
        DrawFilePanel();
        DrawToolPanel();
        DrawViewPanel();
        DrawLayerPanel();
        DrawCoordinatePanel();
        DrawTextureMappingPanel();
        DrawObjectPanel();
        DrawValidationPanel();
        DrawAssetLibraryPanel();
        DrawStatusBar();
    }

    /// <summary>
    /// 首次啟動（還沒有 imgui.ini）時的預設版面：面板分佈在四周，中央留給 3D 視埠。
    /// 用 FirstUseEver 而不是 DockBuilder —— 一樣能避免面板疊在一起，
    /// 但使用者拖過之後 ImGui 會記住，不會每幀被重置。
    /// </summary>
    /// <remarks>
    /// <c>SetNextWindowPos</c> 只作用於「下一個」<c>Begin</c>，所以必須在每個面板各自呼叫，
    /// 不能在一幀開頭一次設完。
    /// </remarks>
    private static void PlaceWindow(string panel)
    {
        var viewport = ImGui.GetMainViewport();
        var origin = viewport.WorkPos;
        var size = viewport.WorkSize;

        const float leftWidth = 300f;
        const float rightWidth = 330f;
        const float statusHeight = 30f;
        const float gap = 8f;

        float bottomHeight = MathF.Max(230f, size.Y * 0.28f);
        float bottomY = origin.Y + size.Y - bottomHeight - statusHeight;
        float columnHeight = bottomY - origin.Y - gap;
        float rightX = origin.X + size.X - rightWidth;

        // 底部橫條切成四格。ImGui.NET 1.91 沒開放 DockBuilder，
        // 所以是用固定位置分欄而不是真正的 dock layout。
        float bottomWidth = size.X - rightWidth - gap;
        float slot = (bottomWidth - (gap * 3f)) / 4f;

        (float x, float y, float w, float h) = panel switch
        {
            // 左欄：地圖清單在上、工具在下。
            "地圖清單" => (origin.X, origin.Y, leftWidth, columnHeight * 0.55f),
            "工具" => (origin.X, origin.Y + (columnHeight * 0.55f) + gap, leftWidth, (columnHeight * 0.45f) - gap),

            // 右欄：檢視、座標、圖層。
            "檢視" => (rightX, origin.Y, rightWidth, 200f),
            "座標" => (rightX, origin.Y + 208f, rightWidth, 215f),
            "圖層" => (rightX, origin.Y + 431f, rightWidth, columnHeight - 431f),

            // 底部四格。「校驗」與「素材庫」共用最後一格，用標題列切換。
            "檔案" => (origin.X, bottomY, slot, bottomHeight),
            "物件" => (origin.X + slot + gap, bottomY, slot, bottomHeight),
            "貼圖對應" => (origin.X + ((slot + gap) * 2f), bottomY, slot, bottomHeight),
            _ => (origin.X + ((slot + gap) * 3f), bottomY, slot, bottomHeight),
        };

        ImGui.SetNextWindowPos(new NVector2(x, y), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new NVector2(w, h), ImGuiCond.FirstUseEver);
    }

    private void DrawWorldList()
    {
        PlaceWindow("地圖清單");
        ImGui.Begin("地圖清單");

        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##filter", "搜尋名稱或編號", ref _worldFilter, 64);
        ImGui.Checkbox("只顯示可載入的", ref _showOnlyPlayable);

        var worlds = _session.Worlds.Where(Matches).ToArray();
        ImGui.TextColored(Muted, $"{worlds.Length} / {_session.Worlds.Length} 張　（雙擊載入）");
        ImGui.Separator();

        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY
                                    | ImGuiTableFlags.Borders | ImGuiTableFlags.SizingStretchProp;

        if (ImGui.BeginTable("worlds", 4, flags))
        {
            ImGui.TableSetupColumn("World", ImGuiTableColumnFlags.WidthFixed, 70f);
            ImGui.TableSetupColumn("名稱");
            ImGui.TableSetupColumn("OpenMU", ImGuiTableColumnFlags.WidthFixed, 60f);
            ImGui.TableSetupColumn("檔案", ImGuiTableColumnFlags.WidthFixed, 60f);
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableHeadersRow();

            foreach (var world in worlds)
            {
                ImGui.TableNextRow();

                ImGui.TableSetColumnIndex(0);
                bool selected = _session.LoadedWorldIndex == world.Index;

                // 雙擊才載入。載一張圖要解析全部資料 + 上千個模型，
                // 誤觸一下就重載代價太大（實測過視窗搶到焦點時會被外部點擊觸發）。
                ImGui.Selectable(
                    $"World{world.Index}##w{world.Index}",
                    selected,
                    ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowDoubleClick);

                if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                    _session.RequestWorld(world.Index);

                ImGui.TableSetColumnIndex(1);
                ImGui.Text(world.Name);

                ImGui.TableSetColumnIndex(2);
                // 客戶端 worldIndex = OpenMU map number + 1；沒有登記世界類別就沒有對應編號。
                if (world.MapNumber is int number)
                    ImGui.Text(number.ToString());
                else
                    ImGui.TextColored(Muted, "－");

                ImGui.TableSetColumnIndex(3);
                ImGui.Text($"{Flag(world.HasAtt, "A")}{Flag(world.HasMap, "M")}{Flag(world.HasObj, "O")}");
            }

            ImGui.EndTable();
        }

        ImGui.Separator();
        DrawNewWorld();

        ImGui.End();
    }

    /// <summary>
    /// 從零建一張新地圖。
    /// </summary>
    /// <remarks>
    /// 客戶端要載入一張圖，光有地形檔不夠 —— 貼圖是逐圖一份、按檔名找的，
    /// 還要有 Object 目錄與帶 [WorldInfo] 的類別。這個按鈕把四樣一起建好，
    /// 細節見 MuAssets.Core/NewMapScaffold.cs。
    /// </remarks>
    private void DrawNewWorld()
    {
        if (_session.IsExternalProjectReadOnly)
        {
            ImGui.TextColored(Muted, "外部 --project 唯讀模式不提供新建地圖。");
            return;
        }

        if (!ImGui.CollapsingHeader("新建地圖"))
            return;

        ImGui.SetNextItemWidth(120f);
        ImGui.InputInt("編號", ref _newWorldIndex);

        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##newname", "地圖名稱", ref _newWorldName, 48);

        ImGui.SetNextItemWidth(120f);
        ImGui.InputInt("貼圖來源", ref _newWorldDonor);
        ImGui.SameLine();
        ImGui.TextColored(Muted, $"從 World{_newWorldDonor} 複製地形貼圖");

        ImGui.TextColored(Muted, "Client.Main/Worlds 路徑（留空就不產生世界類別）");
        ImGui.SetNextItemWidth(-1f);

        string worldsPath = _session.Settings.WorldsSourcePath;
        if (ImGui.InputText("##worldssrc", ref worldsPath, 512))
        {
            _session.Settings.WorldsSourcePath = worldsPath;
            _session.Settings.Save();
        }

        bool exists = _session.Worlds.Any(w => w.Index == _newWorldIndex);

        ImGui.BeginDisabled(_session.FileBusy || exists || _newWorldIndex <= 0);
        if (ImGui.Button("建立並載入"))
        {
            _ = (_game.ActiveScene as MapEditorScene)?.CreateNewWorldAsync(
                _newWorldIndex,
                string.IsNullOrWhiteSpace(_newWorldName) ? $"World{_newWorldIndex}" : _newWorldName.Trim(),
                _newWorldDonor);
        }

        ImGui.EndDisabled();

        if (exists)
        {
            ImGui.SameLine();
            ImGui.TextColored(Warning, $"World{_newWorldIndex} 已經存在");
        }

        if (!string.IsNullOrEmpty(_session.FileMessage))
            ImGui.TextWrapped(_session.FileMessage);
    }

    private void DrawViewPanel()
    {
        PlaceWindow("檢視");
        ImGui.Begin("檢視");

        var camera = _session.Camera;

        int mode = (int)camera.Mode;
        if (ImGui.RadioButton("環繞", ref mode, (int)CameraMode.Orbit))
            camera.Mode = CameraMode.Orbit;
        ImGui.SameLine();
        if (ImGui.RadioButton("俯視", ref mode, (int)CameraMode.TopDown))
            camera.Mode = CameraMode.TopDown;

        ImGui.Separator();

        float distance = camera.Distance;
        if (ImGui.SliderFloat("距離", ref distance, 200f, Constants.TERRAIN_SIZE * Constants.TERRAIN_SCALE * 1.5f, "%.0f"))
            camera.Distance = distance;

        float yaw = MathHelper.ToDegrees(camera.Yaw);
        if (ImGui.SliderFloat("方位", ref yaw, -180f, 180f, "%.0f°"))
            camera.Yaw = MathHelper.ToRadians(yaw);

        if (camera.Mode == CameraMode.Orbit)
        {
            float pitch = MathHelper.ToDegrees(camera.Pitch);
            if (ImGui.SliderFloat("俯角", ref pitch, 5f, 89f, "%.0f°"))
                camera.Pitch = MathHelper.ToRadians(pitch);
        }

        if (ImGui.Button("看全圖"))
            camera.FrameWholeMap();
        ImGui.SameLine();
        if (ImGui.Button("回中心"))
            camera.FocusTile(Constants.TERRAIN_SIZE / 2, Constants.TERRAIN_SIZE / 2);

        ImGui.Separator();

        // 平移一直都在（中鍵拖曳），但 Mac 沒有中鍵，等於摸不到 ——
        // 於是只能一格一格按「相機對準」。把操作列出來，別再靠猜。
        if (ImGui.CollapsingHeader("操作說明", ImGuiTreeNodeFlags.DefaultOpen))
            ImGui.TextColored(Muted, EditorCamera.ControlsHelp);

        ImGui.Separator();

        // 會搖動的草不是地形貼圖，是另一套 billboard，由 Constants.DRAW_GRASS 控制。
        // 啟動時它的值來自畫質預設：Auto 在這台 Mac 上解析成 Medium，而 Medium 關草。
        // 遊戲那邊玩家可以在暫停選單裡開；編輯器沒有那個選單，所以開關放這裡。
        // 不在程式碼裡強制 —— 換草貼圖要驗收時自己勾起來就好。
        bool drawGrass = Constants.DRAW_GRASS;
        if (ImGui.Checkbox("顯示搖動的草", ref drawGrass))
        {
            Constants.DRAW_GRASS = drawGrass;
            if (drawGrass)
                (_game.ActiveScene as MapEditorScene)?.World?.Terrain?.ReloadGrassIfNeeded();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("草的 billboard（TileGrass01–03.OZT）。\n關掉時只剩地面貼圖，跟遊戲的低／中畫質一樣。");

        if (drawGrass)
        {
            // 原版是一格一張立牌（約一平方公尺一張），所以看起來稀疏。
            // 檔位走遊戲的 ApplyGrassQuality —— 一次設齊密度、片數、alpha 門檻、
            // 兩個距離。單獨動滑桿的話門檻與距離不會跟上，預覽就跟遊戲對不上
            // （這正是驗收時發現的：編輯器好好的、手機上一片色塊）。
            ImGui.TextColored(Muted, "檔位（跟遊戲的 Grass Quality 一致）");
            int currentLevel = Constants.GRASS_TUFTS_PER_TILE >= 8 ? 8
                : Constants.GRASS_TUFTS_PER_TILE >= 4 ? 4 : 1;
            foreach ((int level, string label) in new[] { (1, "原版"), (4, "中(4)"), (8, "高(8)") })
            {
                if (level != 1) ImGui.SameLine();
                if (ImGui.RadioButton(label, currentLevel == level) && currentLevel != level)
                {
                    Client.Main.Graphics.GraphicsQualityManager.ApplyGrassQuality(level);
                    _session.GrassDensity = Constants.GRASS_TUFTS_PER_TILE;
                    _session.GrassPlanes = Constants.GRASS_CLUSTER_PLANES;
                    (_game.ActiveScene as MapEditorScene)?.World?.Terrain?.ReloadGrassIfNeeded();
                }
            }

            // 進階：單項微調。改了就跟檔位分道揚鑣，門檻與距離維持目前值。
            int density = Constants.GRASS_TUFTS_PER_TILE;
            if (ImGui.SliderInt("草的密度", ref density, 1, 12, density == 1 ? "1（原版）" : "%d 片／格"))
            {
                _session.GrassDensity = density;
                Constants.GRASS_TUFTS_PER_TILE = density;
                (_game.ActiveScene as MapEditorScene)?.World?.Terrain?.ReloadGrassIfNeeded();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("一格地面長幾片草。原版是 1 —— 一格一張立牌。\n拉高會線性增加三角形數與填充率，看右下角的 FPS。");

            int planes = Constants.GRASS_CLUSTER_PLANES;
            if (ImGui.SliderInt("交叉片數", ref planes, 1, 4, planes == 1 ? "1（平板）" : planes == 2 ? "2（十字）" : planes == 3 ? "3（三角）" : "%d"))
            {
                _session.GrassPlanes = planes;
                Constants.GRASS_CLUSTER_PLANES = planes;
                (_game.ActiveScene as MapEditorScene)?.World?.Terrain?.ReloadGrassIfNeeded();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("幾片草共用一個圓心、夾角散開。\n不增加三角形數 —— 只是把立牌重新分組。");

            float dense = Constants.GRASS_DENSE_DISTANCE;
            if (ImGui.SliderFloat("稠密距離", ref dense, 0f, 6000f, dense <= 0 ? "0（不分層）" : "%.0f"))
                Constants.GRASS_DENSE_DISTANCE = dense;   // 只影響繪製，不用重建
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("超過這個距離每格只畫一片（遠處密度看不出來，填充率照付）。\n拉遠鏡頭 20→59 fps 的關鍵。0 = 關閉。");

            float draw = Constants.GRASS_DRAW_DISTANCE;
            if (ImGui.SliderFloat("繪製距離", ref draw, 0f, 25600f, draw <= 0 ? "0（不限）" : "%.0f"))
                Constants.GRASS_DRAW_DISTANCE = draw;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("超過就完全不畫。0 = 原版行為（只有視錐剔除）。");

            float alphaRef = Constants.GRASS_ALPHA_REFERENCE;
            if (ImGui.SliderFloat("alpha 門檻", ref alphaRef, 0.01f, 0.6f, "%.2f"))
                Constants.GRASS_ALPHA_REFERENCE = alphaRef;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("低於這個 alpha 的像素直接丟棄（不混合、不寫深度）。\n0.01 = 原版；密度>1 時太低會出現一塊塊色塊，遊戲用 0.35。");
        }

        ImGui.Separator();
        ImGui.TextColored(Muted, "右鍵拖曳＝旋轉　中鍵拖曳＝平移");
        ImGui.TextColored(Muted, "滾輪＝縮放　WASD＝移動");

        ImGui.End();
    }

    private static readonly (MapLayer Layer, string Label)[] LayerTabs =
    [
        (MapLayer.Layer1, "第一層"),
        (MapLayer.Layer2, "第二層"),
        (MapLayer.Alpha, "混合"),
        (MapLayer.Attribute, "屬性"),
        (MapLayer.Height, "高度"),
        (MapLayer.Light, "光照"),
    ];

    /// <summary>
    /// 選取工具下，在選中的物件上畫 3D 拖曳手柄。
    /// </summary>
    /// <remarks>
    /// 手柄畫在 ImGui 的前景層，永遠在最上面 —— 被地形擋住的手柄沒有用。
    /// 拖曳期間要擋住相機，否則會一邊拖物件一邊轉視角。
    /// </remarks>
    private void DrawGizmo()
    {
        var scene = _game.ActiveScene as MapEditorScene;

        if (scene is null || _session.Tool != EditorToolKind.SelectObject)
        {
            _gizmoActive = false;
            return;
        }

        var io = ImGui.GetIO();
        bool acceptInput = !io.WantCaptureMouse && !_session.IsExternalProjectReadOnly;

        _gizmoActive = _gizmo.Draw(_session.SelectedObject, acceptInput);

        if (_gizmo.IsDragging)
        {
            _session.ObjectsDirty = true;
            _session.IssuesStale = true;
        }
        else if (_gizmo.TakeCompletedDrag() is MapObjectInstance before && _session.SelectedObject is MapObjectInstance current)
        {
            // 一次拖曳算一筆歷史，放開才記。
            scene.CommitObjectTransform(current, before);
        }
    }

    /// <summary>手柄這一幀有沒有吃掉滑鼠。<see cref="MapEditorGame"/> 用它決定相機要不要接受輸入。</summary>
    public bool GizmoCapturesMouse => _gizmoActive;

    /// <summary>
    /// 存檔、匯出與部署。
    /// </summary>
    /// <remarks>
    /// 三層是分開的，而且只有最後一層會動到遊戲資源：
    /// <b>專案</b>（可再編輯的 map.json + PNG）→ <b>匯出</b>（客戶端格式，寫到輸出目錄）
    /// → <b>部署</b>（複製進遊戲的 Data 目錄，每個被覆蓋的檔案都先備份）。
    /// </remarks>
    private void DrawFilePanel()
    {
        PlaceWindow("檔案");
        ImGui.Begin("檔案");

        var scene = _game.ActiveScene as MapEditorScene;
        var document = _session.Document;

        if (document is null)
        {
            ImGui.TextColored(Muted, "尚未載入地圖");
            ImGui.End();
            return;
        }

        if (_session.HasUnsavedChanges)
            ImGui.TextColored(Warning, "有未儲存的變更");
        else
            ImGui.TextColored(Muted, "沒有未儲存的變更");

        ImGui.Separator();

        ImGui.BeginDisabled(_session.FileBusy);

        if (ImGui.Button("存專案"))
            _ = scene?.SaveProjectAsync();

        ImGui.SameLine();
        if (ImGui.Button("讀專案"))
            _ = scene?.LoadProjectAsync();

        ImGui.SameLine();
        if (ImGui.Button("匯出客戶端"))
            _ = scene?.ExportAsync();

        ImGui.Separator();

        ImGui.TextColored(Muted, "部署目標（遊戲的 Data 目錄）");
        ImGui.SetNextItemWidth(-1f);

        string deployPath = _session.Settings.DeployDataPath;
        if (ImGui.InputText("##deploy", ref deployPath, 512))
        {
            _session.Settings.DeployDataPath = deployPath;
            _session.Settings.Save();
        }

        bool canDeploy = !string.IsNullOrWhiteSpace(_session.Settings.DeployDataPath);
        ImGui.BeginDisabled(!canDeploy);
        if (ImGui.Button("部署到遊戲"))
            scene?.Deploy();
        ImGui.EndDisabled();

        ImGui.EndDisabled();

        if (!canDeploy)
        {
            ImGui.SameLine();
            ImGui.TextColored(Muted, "先填上路徑");
        }

        ImGui.Separator();

        // 第四層：Godot 中立包。跟上面三層是不同方向的出口 ——
        // 前三層是「回到 MU 客戶端」，這一層是「出去給 RealmForge」。
        ImGui.TextColored(Muted, "Godot 中立包（給 RealmForge）");
        ImGui.SetNextItemWidth(-1f);

        string godotRoot = _session.Settings.GodotExportRoot;
        if (ImGui.InputText("##godotroot", ref godotRoot, 512))
        {
            _session.Settings.GodotExportRoot = godotRoot;
            _session.Settings.Save();
        }

        bool godotObjects = _session.Settings.GodotExportObjects;
        if (ImGui.Checkbox("含物件模型", ref godotObjects))
        {
            _session.Settings.GodotExportObjects = godotObjects;
            _session.Settings.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("關掉只出地形，快很多。要看完整場景就留著。");

        ImGui.BeginDisabled(_session.FileBusy);
        if (ImGui.Button("匯出到 Godot"))
            _ = scene?.ExportGodotAsync();
        ImGui.EndDisabled();

        // 匯出器讀的是磁碟上的 Data，不是這裡的記憶體文件 —— 沒講清楚的話
        // 使用者會以為剛畫的東西已經出去了。
        if (_session.HasUnsavedChanges)
        {
            ImGui.SameLine();
            ImGui.TextColored(Warning, "未存的改動不會出現在包裡");
        }

        ImGui.Separator();
        ImGui.TextColored(Muted, $"專案　{_session.Settings.ProjectDirectoryFor(document.WorldIndex)}");
        ImGui.TextColored(Muted, $"輸出　{_session.Settings.OutputDirectoryFor(document.WorldIndex)}");
        ImGui.TextColored(Muted, $"Godot　{_session.Settings.GodotExportDirectoryFor(document.WorldIndex)}");

        if (!string.IsNullOrEmpty(_session.FileMessage))
        {
            ImGui.Separator();
            ImGui.TextWrapped(_session.FileMessage);
        }

        ImGui.End();
    }

    private static readonly (EditorToolKind Kind, string Label)[] ToolButtons =
    [
        (EditorToolKind.None, "瀏覽"),
        (EditorToolKind.PaintLayer1, "第一層"),
        (EditorToolKind.PaintLayer2, "第二層"),
        (EditorToolKind.PaintAlpha, "混合"),
        (EditorToolKind.SculptHeight, "高度"),
        (EditorToolKind.PaintAttribute, "屬性"),
        (EditorToolKind.PaintLight, "光照"),
        (EditorToolKind.PlaceObject, "放置"),
        (EditorToolKind.Scatter, "散佈"),
        (EditorToolKind.SelectObject, "選取"),
        (EditorToolKind.SpawnArea, "生怪"),
    ];

    private static readonly (TWFlags Flag, string Label)[] AttributeFlags =
    [
        (TWFlags.NoMove, "不可走"),
        (TWFlags.NoGround, "無地面"),
        (TWFlags.SafeZone, "安全區"),
        (TWFlags.Water, "水"),
        (TWFlags.Height, "抬高"),
        (TWFlags.CameraUp, "相機抬升"),
        (TWFlags.NoAttackZone, "禁攻擊"),
    ];

    /// <summary>
    /// 編輯工具面板：選工具、調筆刷、撤銷重做。
    /// </summary>
    private void DrawToolPanel()
    {
        PlaceWindow("工具");
        ImGui.Begin("工具");

        if (_session.Document is null)
        {
            ImGui.TextColored(Muted, "尚未載入地圖");
            ImGui.End();
            return;
        }

        if (_session.IsExternalProjectReadOnly)
        {
            _session.Tool = EditorToolKind.None;
            ImGui.TextColored(Warning, "外部 --project 唯讀：可瀏覽、選圖層與查看依賴，編輯工具已停用。");
            ImGui.End();
            return;
        }

        for (int i = 0; i < ToolButtons.Length; i++)
        {
            var (kind, label) = ToolButtons[i];

            if (ImGui.RadioButton(label, _session.Tool == kind))
                _session.Tool = kind;

            if (i % 3 != 2)
                ImGui.SameLine();
        }

        ImGui.Separator();

        // 物件與生怪工具有自己的操作區，格子類的撤銷不畫在這裡。
        if (_session.Tool is not (EditorToolKind.PlaceObject or EditorToolKind.SelectObject or EditorToolKind.SpawnArea))
            DrawUndoRedo();

        if (_session.Tool == EditorToolKind.None)
        {
            ImGui.TextColored(Muted, "選一個工具開始編輯。");
        ImGui.TextColored(Muted, "按住 Option 點一下 = 吸管（用目前這支筆取樣）");
            ImGui.TextColored(Muted, "Cmd+C 複製游標周圍一個筆刷大小的區塊，Cmd+V 貼上");
            ImGui.End();
            return;
        }

        // 散佈也吃筆刷半徑（撒的範圍），所以它要看得到筆刷設定。
        if (_session.Tool is not (EditorToolKind.PlaceObject or EditorToolKind.SelectObject or EditorToolKind.SpawnArea))
        {
            ImGui.Separator();
            DrawBrushSettings();
        }

        ImGui.Separator();
        DrawToolSpecificSettings();

        ImGui.End();
    }

    private void DrawUndoRedo()
    {
        var history = _session.History;
        var scene = _game.ActiveScene as MapEditorScene;

        ImGui.BeginDisabled(!history.CanUndo);
        if (ImGui.Button("撤銷"))
            scene?.Undo();
        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.BeginDisabled(!history.CanRedo);
        if (ImGui.Button("重做"))
            scene?.Redo();
        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.TextColored(Muted, $"{history.UndoDepth} 筆");

        ImGui.TextColored(Muted, "Cmd+Z 撤銷、Cmd+Shift+Z 重做");

        if (history.NextUndoDescription is string next)
            ImGui.TextColored(Muted, $"下一步撤銷：{next}");
    }

    private void DrawBrushSettings()
    {
        var brush = _session.Brush;

        int shape = (int)brush.Shape;
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.Combo("##shape", ref shape, "點\0方形\0圓形\0"))
            brush.Shape = (BrushShape)shape;

        if (brush.Shape != BrushShape.Point)
        {
            int radius = brush.Radius;
            if (ImGui.SliderInt("半徑", ref radius, 0, 32))
                brush.Radius = radius;

            float falloff = brush.Falloff;
            if (ImGui.SliderFloat("衰減", ref falloff, 0f, 1f, "%.2f"))
                brush.Falloff = falloff;
        }

        // 貼圖與屬性是離散值，強度對它們沒有意義。
        if (_session.Tool is EditorToolKind.PaintAlpha or EditorToolKind.SculptHeight)
        {
            float strength = brush.Strength;
            if (ImGui.SliderFloat("強度", ref strength, 0.01f, 1f, "%.2f"))
                brush.Strength = strength;
        }
    }

    private void DrawToolSpecificSettings()
    {
        switch (_session.Tool)
        {
            case EditorToolKind.PaintLayer1:
            case EditorToolKind.PaintLayer2:
                DrawTilePicker();
                break;

            case EditorToolKind.PaintAlpha:
                float alpha = _session.PaintAlphaValue;
                if (ImGui.SliderFloat("目標混合值", ref alpha, 0f, 255f, "%.0f"))
                    _session.PaintAlphaValue = alpha;

                ImGui.TextColored(Muted, "0 = 只顯示第一層，255 = 完全蓋成第二層");
                break;

            case EditorToolKind.SculptHeight:
                DrawHeightSettings();
                break;

            case EditorToolKind.PaintAttribute:
                DrawAttributeSettings();
                break;

            case EditorToolKind.PaintLight:
                DrawLightSettings();
                break;

            case EditorToolKind.PlaceObject:
                DrawPlaceSettings();
                break;

            case EditorToolKind.Scatter:
                DrawScatterSettings();
                break;

            case EditorToolKind.SelectObject:
                DrawSelectionSettings();
                break;

            case EditorToolKind.SpawnArea:
                DrawSpawnSettings();
                break;
        }
    }

    private void DrawPlaceSettings()
    {
        int type = _session.PlaceObjectType;
        if (ImGui.InputInt("物件 type", ref type))
            _session.PlaceObjectType = (short)Math.Clamp(type, 0, 255);

        ImGui.TextColored(Muted, "在素材庫點模型可以帶入 type");

        bool snap = _session.SnapToTile;
        if (ImGui.Checkbox("貼齊格子中心", ref snap))
            _session.SnapToTile = snap;

        float yaw = _session.PlaceRandomYaw;
        if (ImGui.SliderFloat("隨機旋轉", ref yaw, 0f, 180f, "±%.0f°"))
            _session.PlaceRandomYaw = yaw;

        float scale = _session.PlaceRandomScale;
        if (ImGui.SliderFloat("隨機縮放", ref scale, 0f, 0.5f, "±%.2f"))
            _session.PlaceRandomScale = scale;

        ImGui.Separator();
        DrawObjectUndoRedo();
    }

    /// <summary>
    /// 光照筆刷的設定。
    /// </summary>
    /// <remarks>
    /// MU 的地形光照是烘焙在 TerrainLight.OZB 裡的逐格顏色，渲染時乘上去，
    /// 而且乘 2 —— 所以 128 才是「不加不減」，不是 255。
    /// 火堆旁邊的地會亮，是因為有人畫上去的，不是即時光源算的。
    /// </remarks>
    private void DrawLightSettings()
    {
        var modes = new[]
        {
            (LightMode.Paint, "塗色"),
            (LightMode.Brighten, "加亮"),
            (LightMode.Darken, "壓暗"),
        };

        foreach (var (mode, label) in modes)
        {
            if (ImGui.RadioButton(label, _session.LightMode == mode))
                _session.LightMode = mode;

            if (mode != modes[^1].Item1)
                ImGui.SameLine();
        }

        if (_session.LightMode == LightMode.Paint)
        {
            var color = new NVector3(
                _session.Tools.LightR / 255f,
                _session.Tools.LightG / 255f,
                _session.Tools.LightB / 255f);

            if (ImGui.ColorEdit3("顏色", ref color))
            {
                _session.Tools.LightR = (byte)Math.Clamp(color.X * 255f, 0f, 255f);
                _session.Tools.LightG = (byte)Math.Clamp(color.Y * 255f, 0f, 255f);
                _session.Tools.LightB = (byte)Math.Clamp(color.Z * 255f, 0f, 255f);
            }

            ImGui.TextColored(Muted, "128,128,128 是「不加不減」—— 渲染時會乘 2");
        }

        ImGui.Separator();
        DrawUndoRedo();
    }

    /// <summary>
    /// 散佈筆刷的設定。
    /// </summary>
    /// <remarks>
    /// 最小間距是這裡最不直覺但最重要的一項：隨機不等於均勻，
    /// 沒有間距限制的話會撒出一叢一叢的結塊，看起來比手放還假。
    /// </remarks>
    private void DrawScatterSettings()
    {
        ImGui.TextColored(Muted, "按著拖過去，沿路一直撒");

        int count = _session.ScatterCount;
        if (ImGui.SliderInt("每筆數量", ref count, 1, 40))
            _session.ScatterCount = count;

        float spacing = _session.ScatterSpacing;
        if (ImGui.SliderFloat("最小間距（格）", ref spacing, 0f, 8f, "%.1f"))
            _session.ScatterSpacing = spacing;

        if (spacing <= 0.01f)
            ImGui.TextColored(Warning, "間距 0 會撒出結塊 —— 隨機不等於均勻");

        bool avoid = _session.ScatterAvoidBlocked;
        if (ImGui.Checkbox("避開不可走／水的格子", ref avoid))
            _session.ScatterAvoidBlocked = avoid;

        float yaw = _session.PlaceRandomYaw;
        if (ImGui.SliderFloat("隨機朝向（度）", ref yaw, 0f, 360f, "%.0f"))
            _session.PlaceRandomYaw = yaw;

        float scale = _session.PlaceRandomScale;
        if (ImGui.SliderFloat("隨機大小", ref scale, 0f, 0.6f, "%.2f"))
            _session.PlaceRandomScale = scale;

        ImGui.Separator();

        int type = _session.PlaceObjectType;
        if (ImGui.InputInt("物件 type", ref type))
            _session.PlaceObjectType = (short)Math.Clamp(type, 0, 255);

        ImGui.TextColored(Muted, "在素材庫點模型可以帶入 type");

        ImGui.Separator();
        DrawObjectUndoRedo();
    }

    private void DrawSelectionSettings()
    {
        var scene = _game.ActiveScene as MapEditorScene;
        var selected = _session.SelectedObject;

        if (_session.SelectedObjects.Count > 1)
        {
            ImGui.Text($"選取了 {_session.SelectedObjects.Count} 個物件");

            if (ImGui.Button($"刪除這 {_session.SelectedObjects.Count} 個"))
                scene?.DeleteSelectedObject();

            ImGui.SameLine();
            if (ImGui.Button("取消選取"))
                _session.SelectedObjects.Clear();

            ImGui.TextColored(Muted, "手柄畫在第一個上；多選時只支援整批刪除");
            ImGui.Separator();
            DrawObjectUndoRedo();
            return;
        }

        if (selected is null)
        {
            ImGui.TextColored(Muted, "點一下選最近的物件，拖出一個框選一群（Shift 加選）");
            ImGui.Separator();
            DrawObjectUndoRedo();
            return;
        }

        ImGui.Text($"type {selected.Type} @ ({selected.TileX}, {selected.TileY})");

        // 拖曳期間先改值，放開才記進歷史，這樣一次拖曳只算一筆。
        _transformBefore ??= selected.Clone();

        var position = new System.Numerics.Vector3(selected.Position.X, selected.Position.Y, selected.Position.Z);
        if (ImGui.DragFloat3("位置", ref position, 5f))
        {
            selected.Position = position;
            _session.ObjectsDirty = true;
        }

        float yaw = selected.Angle.Z;
        if (ImGui.DragFloat("旋轉 Z", ref yaw, 1f, -360f, 360f, "%.0f°"))
        {
            selected.Angle = selected.Angle with { Z = yaw };
            _session.ObjectsDirty = true;
        }

        float scale = selected.Scale;
        if (ImGui.DragFloat("縮放", ref scale, 0.01f, 0.05f, 8f, "%.2f"))
        {
            selected.Scale = MathF.Max(0.05f, scale);
            _session.ObjectsDirty = true;
        }

        if (ImGui.IsMouseReleased(ImGuiMouseButton.Left) && _transformBefore is not null)
        {
            scene?.CommitObjectTransform(selected, _transformBefore);
            _transformBefore = null;
        }

        ImGui.Separator();
        DrawRoleEditor(selected);
        ImGui.Separator();

        if (ImGui.Button("刪除"))
        {
            scene?.DeleteSelectedObject();
            _transformBefore = null;
        }

        ImGui.SameLine();
        if (ImGui.Button("相機對準"))
            _session.Camera.FocusTile(selected.TileX, selected.TileY);

        ImGui.Separator();
        DrawObjectUndoRedo();
    }

    /// <summary>
    /// 選中物件的語義角色。
    /// </summary>
    /// <remarks>
    /// MU 的 .obj 只說得出「這是一扇門的模型」，說不出「這是攻城戰的 3 號城門」，
    /// 而玩法要的是後者（見 docs/系統精簡決策-保留簡化刪除.md §21）。
    ///
    /// 角色是字串不是下拉選單：玩法自己定義有哪些角色，編輯器不該預先知道。
    /// 下面那排常用值只是快捷鍵，不是全部的選項。
    /// </remarks>
    private void DrawRoleEditor(MapObjectInstance selected)
    {
        ImGui.TextColored(Muted, "語義角色（給玩法用，不寫進 .obj）");

        string role = selected.Role;
        if (ImGui.InputTextWithHint("角色", "siege.gate", ref role, 64))
        {
            selected.Role = role.Trim();
            _session.IssuesStale = true;
            _session.HasUnsavedChanges = true;
        }

        int roleId = selected.RoleId;
        if (ImGui.InputInt("編號", ref roleId))
        {
            selected.RoleId = Math.Max(0, roleId);
            _session.IssuesStale = true;
            _session.HasUnsavedChanges = true;
        }

        string tags = string.Join(", ", selected.Tags);
        if (ImGui.InputTextWithHint("標籤", "以逗號分隔", ref tags, 256))
        {
            selected.Tags = tags
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToArray();

            _session.HasUnsavedChanges = true;
        }

        foreach (string preset in RolePresets)
        {
            if (ImGui.SmallButton(preset))
            {
                selected.Role = preset;
                _session.IssuesStale = true;
                _session.HasUnsavedChanges = true;
            }

            ImGui.SameLine();
        }

        ImGui.NewLine();

        if (selected.HasRole && ImGui.SmallButton("清除角色"))
        {
            selected.Role = string.Empty;
            selected.RoleId = 0;
            selected.Tags = [];
            _session.IssuesStale = true;
            _session.HasUnsavedChanges = true;
        }
    }

    /// <summary>
    /// 依角色列出已標註的物件與生怪區。
    /// </summary>
    /// <remarks>
    /// 「城門放了幾個、編號有沒有跳號或撞號」用眼睛在地圖上找不出來 ——
    /// 城門通常長得一模一樣，而且散在地圖四個角。
    /// </remarks>
    private void DrawRoleOverview()
    {
        var document = _session.Document;
        if (document is null)
            return;

        var objectGroups = document.Objects
            .Where(o => o.HasRole)
            .GroupBy(o => o.Role)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToArray();

        var spawnGroups = document.Spawns
            .Where(s => s.HasRole)
            .GroupBy(s => s.Role)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToArray();

        if (objectGroups.Length == 0 && spawnGroups.Length == 0)
        {
            ImGui.TextColored(Muted, "還沒有任何物件或生怪區被標註角色");
            return;
        }

        foreach (var group in objectGroups)
        {
            var ids = group.Select(o => o.RoleId).ToArray();
            var duplicates = ids.GroupBy(i => i).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();

            if (!ImGui.TreeNodeEx($"{group.Key}（{group.Count()} 個）##role-{group.Key}"))
                continue;

            if (duplicates.Length > 0)
                ImGui.TextColored(Danger, $"編號重複：{string.Join("、", duplicates)}");

            foreach (var instance in group.OrderBy(o => o.RoleId))
            {
                if (ImGui.Selectable($"#{instance.RoleId}  type {instance.Type} @ ({instance.TileX}, {instance.TileY})##{instance.GetHashCode()}"))
                {
                    _session.SelectedObject = instance;
                    _session.Camera.FocusTile(instance.TileX, instance.TileY);
                }
            }

            ImGui.TreePop();
        }

        foreach (var group in spawnGroups)
        {
            if (!ImGui.TreeNodeEx($"{group.Key}（{group.Count()} 區）##spawnrole-{group.Key}"))
                continue;

            foreach (var area in group.OrderBy(s => s.TeamId))
            {
                if (ImGui.Selectable($"隊 {area.TeamId}  ({area.X1},{area.Y1})-({area.X2},{area.Y2})##{area.GetHashCode()}"))
                {
                    _session.SelectedSpawn = area;
                    _session.Camera.FocusTile(area.X1, area.Y1);
                }
            }

            ImGui.TreePop();
        }
    }

    /// <summary>常用角色的快捷鍵。不是白名單 —— 角色可以填任何字串。</summary>
    private static readonly string[] RolePresets =
    [
        "siege.gate",
        "siege.statue",
        "siege.lever",
        "siege.crown",
        "arena.spawn",
    ];

    /// <summary>
    /// 生怪工具的設定：挑怪物、看清單、改參數、匯出給 OpenMU。
    /// </summary>
    /// <remarks>
    /// 生怪區畫在「圖層」面板的俯視圖上（拖曳出矩形）—— 那裡看得到地形與屬性，
    /// 才知道怪該擺在哪。3D 視埠上不好框範圍。
    /// </remarks>
    private void DrawSpawnSettings()
    {
        var scene = _game.ActiveScene as MapEditorScene;
        var document = _session.Document;

        if (document is null)
            return;

        var catalog = _session.NpcCatalog.Entries;

        if (catalog.Length == 0)
        {
            ImGui.TextColored(Warning, "還沒有怪物目錄");
            ImGui.TextWrapped("執行 MuMapEditor --build-npc-catalog 產生");
            return;
        }

        var current = catalog.FirstOrDefault(e => e.TypeId == _session.SpawnTypeId);
        ImGui.SetNextItemWidth(-1f);

        if (ImGui.BeginCombo("##npc", current is null ? "選擇怪物／NPC" : $"{current.TypeId} {current.Name}"))
        {
            ImGui.SetNextItemWidth(-1f);
            ImGui.InputTextWithHint("##npcFilter", "搜尋名稱或編號", ref _spawnFilter, 64);

            foreach (var entry in catalog.Where(MatchesNpc).Take(200))
            {
                if (ImGui.Selectable($"{entry.TypeId,4}  {entry.Name}", entry.TypeId == _session.SpawnTypeId))
                    _session.SpawnTypeId = entry.TypeId;

                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(
                        $"{entry.ClassName}（{(entry.Kind == NpcKind.Monster ? "怪物" : "NPC")}）\n" +
                        $"模型：{entry.ModelPath ?? "－"}\n" +
                        $"伺服器名稱：{entry.ServerDesignation ?? "－"}");
                }
            }

            ImGui.EndCombo();
        }

        ImGui.TextColored(Muted, "在「圖層」面板的俯視圖上拖曳出範圍");

        ImGui.Separator();
        ImGui.Text($"{document.Spawns.Count} 個生怪區");

        if (ImGui.BeginChild("spawns", new NVector2(0f, 150f)))
        {
            foreach (var area in document.Spawns.ToArray())
            {
                bool selected = ReferenceEquals(_session.SelectedSpawn, area);
                string label = $"{area.Name} ({area.X1},{area.Y1})-({area.X2},{area.Y2}) ×{area.Quantity}";

                if (ImGui.Selectable(label + $"##{area.GetHashCode()}", selected))
                    _session.SelectedSpawn = area;
            }
        }

        ImGui.EndChild();

        if (_session.SelectedSpawn is SpawnArea spawn)
        {
            ImGui.Separator();

            int quantity = spawn.Quantity;
            if (ImGui.DragInt("數量", ref quantity, 1f, 1, 200))
            {
                spawn.Quantity = (short)quantity;
                _session.HasUnsavedChanges = true;
            }

            int direction = (int)spawn.Direction;
            if (ImGui.Combo("朝向", ref direction, "未定\0西\0西南\0南\0東南\0東\0東北\0北\0西北\0"))
            {
                spawn.Direction = (SpawnDirection)direction;
                _session.HasUnsavedChanges = true;
            }

            int trigger = (int)spawn.Trigger;
            if (ImGui.Combo("觸發", ref trigger, "自動\0活動期間\0活動開始一次\0波次期間\0波次開始一次\0程式控制\0遊蕩\0"))
            {
                spawn.Trigger = (SpawnTrigger)trigger;
                _session.HasUnsavedChanges = true;
            }

            if (ImGui.Button("刪除生怪區"))
                scene?.DeleteSpawnArea(spawn);
        }

        ImGui.Separator();
        ImGui.BeginDisabled(_session.FileBusy);
        if (ImGui.Button("匯出給 OpenMU"))
            _ = scene?.ExportToOpenMuAsync();
        ImGui.EndDisabled();

        ImGui.TextColored(Muted, "產生 Terrain{N}.att 與地圖初始化器原始碼");
    }

    private bool MatchesNpc(NpcEntry entry)
        => string.IsNullOrWhiteSpace(_spawnFilter)
        || entry.Name.Contains(_spawnFilter, StringComparison.OrdinalIgnoreCase)
        || entry.TypeId.ToString().Contains(_spawnFilter, StringComparison.Ordinal);

    private void DrawObjectUndoRedo()
    {
        var history = _session.ObjectHistory;
        var scene = _game.ActiveScene as MapEditorScene;

        ImGui.BeginDisabled(!history.CanUndo);
        if (ImGui.Button("撤銷##obj"))
            scene?.UndoObject();
        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.BeginDisabled(!history.CanRedo);
        if (ImGui.Button("重做##obj"))
            scene?.RedoObject();
        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.TextColored(Muted, $"物件 {history.Depth} 筆");
    }

    /// <summary>從這張圖實際有的貼圖裡挑一個索引。直接顯示縮圖，比輸入數字直覺。</summary>
    private void DrawTilePicker()
    {
        var entry = _session.LoadedWorld;
        if (entry is null)
            return;

        if (_session.Tool == EditorToolKind.PaintLayer1)
        {
            bool auto = _session.AutoTransition;
            if (ImGui.Checkbox("自動過渡", ref auto))
                _session.AutoTransition = auto;

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(
                    "中心塗實，邊緣用第二層 + 混合值做漸層 —— MU 的地形本來就是這樣做過渡的。\n" +
                    "要硬邊就關掉它，或把筆刷衰減設成 0。");
            }

            if (auto && _session.Brush.Falloff <= 0.01f)
                ImGui.TextColored(Warning, "筆刷衰減是 0，整個筆刷都算核心 —— 不會有過渡");
        }

        if (_session.Tool == EditorToolKind.PaintLayer2)
        {
            bool empty = _session.PaintLayer2AsEmpty;
            if (ImGui.Checkbox("塗成「無第二層」", ref empty))
                _session.PaintLayer2AsEmpty = empty;

            if (empty)
            {
                ImGui.TextColored(Muted, $"寫入哨兵值 {TerrainTextureMapping.NoLayerIndex}");
                return;
            }
        }

        ImGui.Text($"貼圖索引 {_session.PaintTileIndex}");

        var indexMap = TerrainTextureMapping.BuildIndexMap();
        var available = indexMap
            .Where(kv => entry.TileFiles.Any(f =>
                string.Equals(Path.GetFileNameWithoutExtension(f), Path.GetFileNameWithoutExtension(kv.Value), StringComparison.OrdinalIgnoreCase)))
            .OrderBy(kv => kv.Key)
            .ToArray();

        const float size = 44f;
        int perRow = Math.Max(1, (int)(ImGui.GetContentRegionAvail().X / (size + 8f)));

        for (int i = 0; i < available.Length; i++)
        {
            var (index, file) = available[i];

            string? actual = entry.TileFiles.FirstOrDefault(f =>
                string.Equals(Path.GetFileNameWithoutExtension(f), Path.GetFileNameWithoutExtension(file), StringComparison.OrdinalIgnoreCase));

            var id = actual is null ? null : _previews.Get(Path.Combine(entry.Directory, actual));

            ImGui.PushID(index);

            bool selected = _session.PaintTileIndex == index;
            if (selected)
                ImGui.PushStyleColor(ImGuiCol.Button, new NVector4(0.3f, 0.55f, 0.9f, 1f));

            if (id.HasValue)
            {
                if (ImGui.ImageButton("tile", id.Value, new NVector2(size, size)))
                    _session.PaintTileIndex = (byte)index;
            }
            else if (ImGui.Button($"{index}", new NVector2(size + 8f, size + 8f)))
            {
                _session.PaintTileIndex = (byte)index;
            }

            if (selected)
                ImGui.PopStyleColor();

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"索引 {index}　{actual ?? file}");

            ImGui.PopID();

            if ((i + 1) % perRow != 0)
                ImGui.SameLine();
        }
    }

    private void DrawHeightSettings()
    {
        int mode = (int)_session.HeightMode;
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.Combo("##heightMode", ref mode, "升高\0降低\0平滑\0壓平\0"))
            _session.HeightMode = (HeightMode)mode;

        if (_session.HeightMode is HeightMode.Raise or HeightMode.Lower)
        {
            float step = _session.HeightStep;
            if (ImGui.SliderFloat("每次幅度", ref step, 1f, 40f, "%.0f"))
                _session.HeightStep = step;
        }
        else if (_session.HeightMode == HeightMode.Flatten)
        {
            float target = _session.FlattenTarget;
            if (ImGui.SliderFloat("壓平到", ref target, 0f, 255f, "%.0f"))
                _session.FlattenTarget = target;

            var scene = _game.ActiveScene as MapEditorScene;
            if (ImGui.Button("取滑鼠所在高度") && scene?.HoveredTile.Valid == true && _session.Document is not null)
            {
                int index = (scene.HoveredTile.TileY * MapDocument.Size) + scene.HoveredTile.TileX;
                _session.FlattenTarget = _session.Document.HeightAt(index);
            }
        }

        ImGui.TextColored(Muted, "高度圖是 0–255，渲染時乘以 1.5");
    }

    private void DrawAttributeSettings()
    {
        foreach (var (flag, label) in AttributeFlags)
        {
            if (ImGui.RadioButton(label, _session.AttributeFlag == flag))
                _session.AttributeFlag = flag;
        }

        ImGui.Separator();

        bool erase = _session.AttributeErase;
        if (ImGui.Checkbox("清除（而非設定）", ref erase))
            _session.AttributeErase = erase;

        ImGui.TextColored(Muted, "切到「圖層 → 屬性」可以看到全圖分佈");
    }

    /// <summary>
    /// 圖層俯視圖。既是資料檢查工具（哪裡不可走、貼圖怎麼分佈），
    /// 也是導覽圖 —— 點一下就把相機移過去。
    /// </summary>
    private void DrawLayerPanel()
    {
        PlaceWindow("圖層");
        ImGui.Begin("圖層");

        var document = _session.Document;
        if (document is null)
        {
            ImGui.TextColored(Muted, "尚未載入地圖");
            ImGui.End();
            return;
        }

        for (int i = 0; i < LayerTabs.Length; i++)
        {
            var (layer, label) = LayerTabs[i];

            if (ImGui.RadioButton(label, _session.VisibleLayer == layer))
            {
                _session.VisibleLayer = layer;
                _session.LayerViewDirty = true;
            }

            // 一行三個，六個層剛好兩行。
            if (i % 3 != 2)
                ImGui.SameLine();
        }

        if (_session.LayerViewDirty)
        {
            _layerView.Rebuild(document, _session.VisibleLayer);
            _session.LayerViewDirty = false;
        }

        ImGui.Separator();

        if (_layerView.TextureId is not IntPtr textureId)
        {
            ImGui.End();
            return;
        }

        var available = ImGui.GetContentRegionAvail();
        float side = MathF.Max(120f, MathF.Min(available.X, available.Y - 24f));

        var imageOrigin = ImGui.GetCursorScreenPos();
        ImGui.Image(textureId, new NVector2(side, side));

        DrawSpawnOverlay(document, imageOrigin, side);

        if (ImGui.IsItemHovered())
        {
            var mouse = ImGui.GetIO().MousePos;
            int tileX = (int)((mouse.X - imageOrigin.X) / side * MapDocument.Size);
            int tileY = (int)((mouse.Y - imageOrigin.Y) / side * MapDocument.Size);

            if ((uint)tileX < MapDocument.Size && (uint)tileY < MapDocument.Size)
            {
                ImGui.SetTooltip($"({tileX}, {tileY})  {Describe(document, tileX, tileY)}");

                if (_session.Tool == EditorToolKind.SpawnArea)
                    HandleSpawnDrag(tileX, tileY);
                else if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                    _session.Camera.FocusTile(tileX, tileY);
            }
        }

        ImGui.TextColored(Muted, _session.Tool == EditorToolKind.SpawnArea
            ? "拖曳出生怪範圍"
            : "點一下把相機移到該格");

        ImGui.End();
    }

    /// <summary>在俯視圖上疊出既有的生怪區，選中的那個高亮。</summary>
    private void DrawSpawnOverlay(MapDocument document, NVector2 imageOrigin, float side)
    {
        if (document.Spawns.Count == 0)
            return;

        var drawList = ImGui.GetWindowDrawList();
        float scale = side / MapDocument.Size;

        foreach (var area in document.Spawns)
        {
            var min = new NVector2(imageOrigin.X + (area.X1 * scale), imageOrigin.Y + (area.Y1 * scale));
            var max = new NVector2(imageOrigin.X + ((area.X2 + 1) * scale), imageOrigin.Y + ((area.Y2 + 1) * scale));

            bool selected = ReferenceEquals(_session.SelectedSpawn, area);
            uint color = selected
                ? ImGui.GetColorU32(new NVector4(1f, 0.85f, 0.2f, 1f))
                : ImGui.GetColorU32(new NVector4(1f, 0.35f, 0.35f, 0.85f));

            drawList.AddRect(min, max, color, 0f, ImDrawFlags.None, selected ? 2.5f : 1.5f);
            drawList.AddRectFilled(min, max, ImGui.GetColorU32(new NVector4(1f, 0.35f, 0.35f, 0.15f)));
        }
    }

    /// <summary>按下拖到放開＝一個生怪區。</summary>
    private void HandleSpawnDrag(int tileX, int tileY)
    {
        var scene = _game.ActiveScene as MapEditorScene;

        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            _session.SpawnDragStart = (tileX, tileY);

        if (ImGui.IsMouseReleased(ImGuiMouseButton.Left) && _session.SpawnDragStart is (int startX, int startY))
        {
            scene?.AddSpawnArea(startX, startY, tileX, tileY);
            _session.SpawnDragStart = null;
        }
    }

    private string Describe(MapDocument document, int tileX, int tileY)
    {
        int index = (tileY * MapDocument.Size) + tileX;

        return _session.VisibleLayer switch
        {
            MapLayer.Layer1 => $"貼圖索引 {document.Layer1[index]}",
            MapLayer.Layer2 => document.Layer2[index] == TerrainTextureMapping.NoLayerIndex
                ? "無第二層"
                : $"貼圖索引 {document.Layer2[index]}",
            MapLayer.Alpha => $"混合 {document.Alpha[index]}",
            MapLayer.Attribute => document.Attributes[index] == TWFlags.None
                ? "None"
                : document.Attributes[index].ToString(),
            MapLayer.Height => $"高度 {document.HeightAt(index)}",
            MapLayer.Light => $"光照 {document.LightAt(index).R},{document.LightAt(index).G},{document.LightAt(index).B}",
            _ => string.Empty,
        };
    }

    /// <summary>
    /// 座標檢查器。對應 Lineage 編輯器裡的 MapCoordinateTool：
    /// 隨時能看到「滑鼠這一格」在客戶端與伺服器兩邊分別是什麼，以及那一格的行走屬性。
    /// 沒有這個，擺怪與出生點只能靠猜。
    /// </summary>
    private void DrawCoordinatePanel()
    {
        PlaceWindow("座標");
        ImGui.Begin("座標");

        var world = (_game.ActiveScene?.World) as WorldControl;
        var hit = TerrainPicker.Pick(world, MuGame.Instance.MouseRay);

        if (!hit.Valid || world is null)
        {
            ImGui.TextColored(Muted, "滑鼠不在地形上");
            ImGui.End();
            return;
        }

        var entry = _session.LoadedWorld;

        ImGui.Text($"格子　　 {hit.TileX}, {hit.TileY}");
        ImGui.Text($"世界座標 {hit.World.X:F0}, {hit.World.Y:F0}");
        ImGui.Text($"地形高度 {hit.Height:F1}");

        ImGui.Separator();

        // 伺服器那邊的地圖編號與格子座標。OpenMU 的格子索引與客戶端相同（x = i & 0xFF、y = i >> 8），
        // 差別只在地圖編號要減一。
        if (entry?.MapNumber is int mapNumber)
            ImGui.Text($"OpenMU　 map {mapNumber} @ ({hit.TileX}, {hit.TileY})");
        else
            ImGui.TextColored(Warning, "這張圖在客戶端沒有登記 WorldInfo，對不到 OpenMU 編號");

        ImGui.Separator();

        var flags = world.Terrain.RequestTerrainFlag(hit.TileX, hit.TileY);
        ImGui.Text($"屬性　　 {(flags == TWFlags.None ? "None" : flags.ToString())}");

        bool walkable = !flags.HasFlag(TWFlags.NoMove) && !flags.HasFlag(TWFlags.NoGround);
        ImGui.TextColored(walkable ? Muted : Warning, walkable ? "可行走" : "不可行走");

        if (ImGui.Button("相機對準這一格"))
            _session.Camera.FocusTile(hit.TileX, hit.TileY);

        ImGui.End();
    }

    /// <summary>
    /// 貼圖對應表：這張圖用到哪些索引、每個索引現在對到哪個檔案、哪些缺。
    /// </summary>
    /// <remarks>
    /// 這是「換貼圖素材」的入口 —— 改對應不動原始資源，寫進
    /// <c>~/.mu-editor/texture-mappings.json</c>，重新載入地圖後生效。
    ///
    /// 也是缺貼圖的修法：Season 20 的新圖用到索引 33 以上，
    /// 而 S6 世代的載入器（含原版 MuMain）只掛到 29，缺的部分在這裡自己指定。
    /// </remarks>
    private void DrawTextureMappingPanel()
    {
        PlaceWindow("貼圖對應");
        ImGui.Begin("貼圖對應");

        var entry = _session.LoadedWorld;
        var document = _session.Document;

        if (entry is null || document is null)
        {
            ImGui.TextColored(Muted, "尚未載入地圖");
            ImGui.End();
            return;
        }

        var scene = _game.ActiveScene as MapEditorScene;
        var mapping = _session.TextureMappings.BuildFor(entry.Index);

        // 這張圖實際用到的索引（Layer2 的 255 是哨兵值，不算）。
        var used = new SortedSet<int>(document.Layer1.Select(v => (int)v));
        foreach (var value in document.Layer2)
        {
            if (value != TerrainTextureMapping.NoLayerIndex)
                used.Add(value);
        }

        int custom = _session.TextureMappings.CountFor(entry.Index);
        int missing = used.Count(i => ResolveTileFile(entry, mapping, i) is null);

        ImGui.Text($"用到 {used.Count} 個索引");
        ImGui.SameLine();
        if (missing > 0)
            ImGui.TextColored(Warning, $"缺 {missing} 個");
        else
            ImGui.TextColored(Muted, "全部對得上");

        if (custom > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(Muted, $"自訂 {custom} 個");
        }

        ImGui.SameLine();
        if (ImGui.Button("重新載入地圖"))
            scene?.ReloadCurrentWorld();

        ImGui.Separator();

        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY
                                    | ImGuiTableFlags.Borders | ImGuiTableFlags.SizingStretchProp;

        if (ImGui.BeginTable("mapping", 5, flags))
        {
            ImGui.TableSetupColumn("索引", ImGuiTableColumnFlags.WidthFixed, 46f);
            ImGui.TableSetupColumn("格數", ImGuiTableColumnFlags.WidthFixed, 62f);
            ImGui.TableSetupColumn("貼圖檔");
            ImGui.TableSetupColumn("類型 / 尺寸", ImGuiTableColumnFlags.WidthFixed, 150f);
            ImGui.TableSetupColumn("預覽", ImGuiTableColumnFlags.WidthFixed, 44f);
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableHeadersRow();

            var layer1Usage = document.TileUsage(layer2: false);
            var layer2Usage = document.TileUsage(layer2: true);

            foreach (int index in used)
            {
                string? file = ResolveTileFile(entry, mapping, index);
                bool isCustom = _session.TextureMappings.Get(entry.Index, index) is not null;

                ImGui.TableNextRow();
                ImGui.PushID(index);

                ImGui.TableSetColumnIndex(0);
                ImGui.Text(index.ToString());

                ImGui.TableSetColumnIndex(1);
                ImGui.Text((layer1Usage.GetValueOrDefault((byte)index) + layer2Usage.GetValueOrDefault((byte)index)).ToString());

                ImGui.TableSetColumnIndex(2);
                DrawMappingCombo(entry, index, file, isCustom);

                // 「這張圖是什麼」——換素材時要看的就是這幾個數字。
                // 槽位來自檔名（結構上該長什麼），外觀來自影像（現在實際是什麼），
                // 兩者不一定一致而且兩邊都是真的：迪維亞斯的 TileGrass01 是雪白色。
                ImGui.TableSetColumnIndex(3);
                if (file is not null)
                {
                    var info = GetTileTextureInfo(Path.Combine(entry.Directory, file));
                    if (info is not null)
                    {
                        ImGui.TextColored(Muted, info);
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip(
                                "槽位＝檔名說它扮演什麼角色；外觀＝影像實際長什麼樣。\n" +
                                "「鋪 N×N 格」照 64 像素一格的規則算。\n" +
                                "想知道這張圖被幾張地圖共用、一次全換掉：\n" +
                                "  tools/mu map dupes --name " + Path.GetFileNameWithoutExtension(file));
                    }
                    else
                    {
                        ImGui.TextColored(Muted, "量不到");
                    }
                }

                ImGui.TableSetColumnIndex(4);
                if (file is not null && _previews.Get(Path.Combine(entry.Directory, file)) is IntPtr preview)
                    ImGui.Image(preview, new NVector2(36f, 36f));

                ImGui.PopID();
            }

            ImGui.EndTable();
        }

        ImGui.End();
    }

    /// <summary>貼圖的槽位／外觀／尺寸。量一張要解 OZJ 並掃像素，所以查過就快取。</summary>
    private readonly Dictionary<string, string?> _tileInfoCache = new(StringComparer.OrdinalIgnoreCase);

    private string? GetTileTextureInfo(string path)
    {
        if (_tileInfoCache.TryGetValue(path, out var cached))
            return cached;

        string? text = null;
        var profile = TerrainTextureClassifier.Measure(path);

        if (profile is not null)
        {
            var slot = TerrainTextureClassifier.SlotOf(Path.GetFileName(path));
            var look = TerrainTextureClassifier.LookOf(profile);
            text = $"{slot} / {look}　{profile.Width}×{profile.Height}　鋪 {profile.Width / 64f:0.##}×{profile.Height / 64f:0.##} 格";
        }

        _tileInfoCache[path] = text;
        return text;
    }

    private void DrawMappingCombo(WorldEntry entry, int index, string? current, bool isCustom)
    {
        string label = current ?? "（缺）";

        if (isCustom)
            ImGui.TextColored(new NVector4(0.45f, 0.75f, 1f, 1f), "•");
        else if (current is null)
            ImGui.TextColored(Warning, "!");
        else
            ImGui.TextColored(Muted, " ");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1f);

        if (_session.IsExternalProjectReadOnly)
        {
            ImGui.TextUnformatted(label);
            return;
        }

        if (!ImGui.BeginCombo($"##map{index}", label))
            return;

        if (isCustom && ImGui.Selectable("恢復預設"))
            _session.TextureMappings.Clear(entry.Index, index);

        foreach (var file in entry.TileFiles)
        {
            if (ImGui.Selectable(file, string.Equals(file, current, StringComparison.OrdinalIgnoreCase)))
                _session.TextureMappings.Set(entry.Index, index, file);
        }

        ImGui.EndCombo();
    }

    /// <summary>
    /// 索引表寫的是 .ozj，實際檔案可能是 .ozt（透明版），比對時忽略副檔名。
    /// 找不到就是這個索引缺貼圖。
    /// </summary>
    private static string? ResolveTileFile(WorldEntry entry, Dictionary<int, string> mapping, int index)
    {
        if (!mapping.TryGetValue(index, out var mapped))
            return null;

        return entry.TileFiles.FirstOrDefault(f =>
            string.Equals(Path.GetFileNameWithoutExtension(f), Path.GetFileNameWithoutExtension(mapped), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 這張圖實際擺了哪些物件。type 對應的模型是 <c>Object{world}/Object{type+1:00}.bmd</c>
    /// （見 <c>Client.Main.Objects.MapTileObject.Load</c>），語意類別來自該 world 的
    /// <c>CreateMapTileObjects()</c>。Phase 2 會在這裡接上縮圖與分類標註。
    /// </summary>
    private void DrawObjectPanel()
    {
        PlaceWindow("物件");
        ImGui.Begin("物件");

        var document = _session.Document;
        var entry = _session.LoadedWorld;

        if (document is null || entry is null)
        {
            ImGui.TextColored(Muted, "尚未載入地圖");
            ImGui.End();
            return;
        }

        if (ImGui.CollapsingHeader("依角色檢視"))
        {
            DrawRoleOverview();
            ImGui.Separator();
        }

        if (_objectSummaryWorldIndex != entry.Index)
        {
            _objectSummary = BuildObjectSummary(document, entry);
            _objectSummaryWorldIndex = entry.Index;
        }

        int broken = _objectSummary.Where(o => !o.HasModel).Sum(o => o.Count);

        ImGui.Text($"{document.Objects.Count} 個物件，{_objectSummary.Length} 種");

        if (broken > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(Warning, $"{broken} 個載不到模型");

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("這些物件在遊戲裡不會出現 —— 模型路徑對不到檔案");
        }

        ImGui.Separator();

        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY
                                    | ImGuiTableFlags.Borders | ImGuiTableFlags.SizingStretchProp;

        if (ImGui.BeginTable("objects", 4, flags))
        {
            ImGui.TableSetupColumn("type", ImGuiTableColumnFlags.WidthFixed, 44f);
            ImGui.TableSetupColumn("數量", ImGuiTableColumnFlags.WidthFixed, 50f);
            ImGui.TableSetupColumn("類別");
            ImGui.TableSetupColumn("##replace", ImGuiTableColumnFlags.WidthFixed, 44f);
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableHeadersRow();

            foreach (var item in _objectSummary)
            {
                ImGui.TableNextRow();

                ImGui.TableSetColumnIndex(0);
                if (ImGui.Selectable($"{item.Type}##o{item.Type}", false, ImGuiSelectableFlags.SpanAllColumns))
                    FocusObject(document, item.Type);

                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip($"Object{entry.Index}/Object{item.Type + 1:00}.bmd");

                ImGui.TableSetColumnIndex(1);
                ImGui.Text(item.Count.ToString());

                ImGui.TableSetColumnIndex(2);
                if (!item.HasModel)
                    ImGui.TextColored(Warning, "缺模型");
                else if (item.ClassName is null)
                    ImGui.TextColored(Muted, "未分類");
                else
                    ImGui.Text(item.ClassName);

                // 「這張圖的這種東西全部換掉」的入口。按下去只是進挑選模式，
                // 真正的替換要在素材庫點一個模型才發生 —— 不會誤按就改掉一萬個物件。
                ImGui.TableSetColumnIndex(3);
                ImGui.PushID(item.Type);
                ImGui.BeginDisabled(_session.IsExternalProjectReadOnly);
                if (ImGui.SmallButton("替換"))
                    BeginReplaceType(item.Type);
                ImGui.EndDisabled();
                ImGui.PopID();

                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip($"到素材庫挑一個模型，把這張圖的 {item.Count} 個 type {item.Type} 全部換掉");
            }

            ImGui.EndTable();
        }

        ImGui.End();
    }

    /// <summary>進入「挑替換目標」模式。真正的替換在素材庫點下去才發生。</summary>
    private void BeginReplaceType(short fromType)
    {
        _replaceFromType = fromType;
        _replaceScale = 1f;

        // 換樹就先只給樹看 —— 但允許關掉，因為「把石頭換成樹」也是合理的需求。
        var source = _assets.FirstOrDefault(a => a.ObjectType == fromType);
        _categoryFilter = _replaceSameCategoryOnly && source is not null
            ? source.Category
            : AssetCategory.Unclassified;

        _session.StatusMessage = $"挑一個模型來取代 type {fromType}（在「素材庫」面板）";
    }

    private static ObjectSummary[] BuildObjectSummary(MapDocument document, WorldEntry entry)
    {
        var semanticTypes = WorldCatalog.GetTileObjectTypes(entry);

        return document.Objects
            .GroupBy(o => o.Type)
            .OrderByDescending(g => g.Count())
            .Select(g =>
            {
                string? className = null;

                if (semanticTypes is not null && g.Key >= 0 && g.Key < semanticTypes.Length)
                {
                    var type = semanticTypes[g.Key];

                    // 泛用的 MapTileObject 代表這個 type 沒有被該 world 特別分類過。
                    if (type is not null && type.Name != "MapTileObject")
                        className = type.Name;
                }

                return new ObjectSummary(g.Key, g.Count(), className, ResolvesModel(entry, g.Key, className));
            })
            .ToArray();
    }

    /// <summary>把相機移到該種物件的第一個實例上。</summary>
    private void FocusObject(MapDocument document, short type)
    {
        var first = document.Objects.FirstOrDefault(o => o.Type == type);
        if (first is null)
            return;

        int tileX = first.TileX;
        int tileY = first.TileY;

        _session.Camera.Mode = CameraMode.Orbit;
        _session.Camera.Distance = 900f;
        _session.Camera.FocusTile(tileX, tileY);
    }

    private readonly record struct ObjectSummary(short Type, int Count, string? ClassName, bool HasModel);

    /// <summary>
    /// 這個 type 在遊戲裡載得到模型嗎？
    /// </summary>
    /// <remarks>
    /// 泛用的 <c>MapTileObject.Load</c> 組出來的路徑是 <c>Object{world}/Object{type+1:00}.bmd</c>，
    /// 但 <b>Object1（Lorencia）裡全是具名檔案</b>（Tree01.bmd、Bonfire01.bmd…），
    /// 一個 ObjectNN.bmd 都沒有 —— 所以沒有語意類別的 type 在 Lorencia 一定載不到，
    /// <c>WorldControl.RemoveFailed</c> 會把它們從世界移除。
    ///
    /// 有語意類別的就當作載得到：那些類別各自在 Load() 裡寫死自己的路徑，這裡驗不了。
    ///
    /// <b>這只是靜態推測，真正的答案要載入之後對帳</b> ——
    /// 見 <c>MapEditorScene.ReportObjectLoading</c> 與 <c>--audit-objects</c>。
    /// （早期的筆記說 Lorencia 有 1028 個物件載不到，那是錯的：
    /// 實測 2833 個全部載入，107 種 type 都有語意類別。）
    /// </remarks>
    private static bool ResolvesModel(WorldEntry entry, short type, string? className)
    {
        if (className is not null)
            return true;

        string path = Path.Combine(
            Path.GetDirectoryName(entry.Directory) ?? string.Empty,
            $"Object{entry.Index}",
            $"Object{type + 1:00}.bmd");

        return File.Exists(path);
    }

    private static readonly NVector4 ErrorColor = new(1f, 0.4f, 0.4f, 1f);

    /// <summary>
    /// 校驗面板：把「畫得出來但進遊戲會壞掉」的東西列出來，點一下跳過去看。
    /// </summary>
    private void DrawValidationPanel()
    {
        PlaceWindow("校驗");
        ImGui.Begin("校驗");

        var document = _session.Document;
        var entry = _session.LoadedWorld;
        var scene = _game.ActiveScene as MapEditorScene;

        if (document is null || entry is null)
        {
            ImGui.TextColored(Muted, "尚未載入地圖");
            ImGui.End();
            return;
        }

        // 校驗要掃過整張圖與所有物件，不適合每幀跑，改成手動觸發 + 標記過期。
        if (ImGui.Button("執行校驗") || (_session.IssuesStale && _session.Issues.Count == 0 && _autoValidateOnce))
        {
            _session.Issues = MapValidator.Validate(document, entry, _session.TextureMappings, _session.NpcCatalog);
            _session.IssuesStale = false;
            _autoValidateOnce = false;
        }

        ImGui.SameLine();

        int errors = _session.Issues.Count(i => i.Severity == IssueSeverity.Error);
        int warnings = _session.Issues.Count(i => i.Severity == IssueSeverity.Warning);

        if (_session.IssuesStale && _session.Issues.Count > 0)
            ImGui.TextColored(Muted, "（地圖已變動，結果可能過期）");
        else if (errors > 0)
            ImGui.TextColored(ErrorColor, $"{errors} 個錯誤、{warnings} 個警告");
        else if (warnings > 0)
            ImGui.TextColored(Warning, $"{warnings} 個警告");
        else if (_session.Issues.Count == 0)
            ImGui.TextColored(Muted, "尚未校驗");

        ImGui.Separator();

        if (_session.Issues.Count == 0)
        {
            ImGui.TextColored(Muted, "按「執行校驗」開始");
            ImGui.End();
            return;
        }

        foreach (var issue in _session.Issues)
        {
            var color = issue.Severity switch
            {
                IssueSeverity.Error => ErrorColor,
                IssueSeverity.Warning => Warning,
                _ => Muted,
            };

            string mark = issue.Severity switch
            {
                IssueSeverity.Error => "✕",
                IssueSeverity.Warning => "!",
                _ => "·",
            };

            ImGui.TextColored(color, $"{mark} [{issue.Category}]");
            ImGui.SameLine();
            ImGui.TextWrapped(issue.Message);

            // 有座標／物件／生怪區的就給一個跳過去的按鈕。
            if (issue.Tile is (int x, int y))
            {
                ImGui.PushID(issue.GetHashCode());
                if (ImGui.SmallButton($"跳到 ({x}, {y})"))
                {
                    _session.Camera.FocusTile(x, y);

                    if (issue.Spawn is not null)
                        _session.SelectedSpawn = issue.Spawn;
                }

                ImGui.PopID();
            }
            else if (issue.Object is MapObjectInstance instance)
            {
                ImGui.PushID(issue.GetHashCode());
                if (ImGui.SmallButton($"跳到物件 ({instance.TileX}, {instance.TileY})"))
                {
                    _session.SelectedObject = instance;
                    _session.Tool = EditorToolKind.SelectObject;
                    _session.Camera.Mode = CameraMode.Orbit;
                    _session.Camera.Distance = 900f;
                    _session.Camera.FocusTile(instance.TileX, instance.TileY);
                }

                ImGui.PopID();
            }

            ImGui.Separator();
        }

        ImGui.End();
    }

    /// <summary>
    /// 素材庫。列出目前這張圖的 <c>Object{N}</c> 目錄裡所有模型，畫成縮圖並依類別分組。
    /// 分類可以人工改，改完寫回 <c>~/.mu-editor/object-catalog.json</c>。
    /// </summary>
    private void DrawAssetLibraryPanel()
    {
        PlaceWindow("素材庫");
        ImGui.Begin("素材庫");

        var entry = _session.LoadedWorld;
        if (entry is null)
        {
            ImGui.TextColored(Muted, "尚未載入地圖");
            ImGui.End();
            return;
        }

        if (_assetWorldIndex != entry.Index)
        {
            _assets = _catalog.Scan(_session.DataPath, entry.Index, WorldCatalog.GetTileObjectTypes(entry));
            _assetWorldIndex = entry.Index;
            _assetFilter = string.Empty;
            _selectedAssets.Clear();
            _assetAnchor = null;
        }

        if (_assets.Length == 0)
        {
            ImGui.TextColored(Warning, $"Object{entry.Index}/ 沒有模型檔");
            ImGui.End();
            return;
        }

        DrawReplaceBanner(entry);
        DrawAssetToolbar(entry);
        ImGui.Separator();

        var visible = VisibleAssets();

        ImGui.TextColored(Muted, $"{visible.Length} / {_assets.Length} 個模型　（Cmd 點加選、Shift 點選一段）");
        DrawBatchLabelling();
        ImGui.Separator();

        if (ImGui.BeginChild("assets", new NVector2(0f, 0f)))
        {
            float cell = _assetThumbnailSize + 16f;
            int perRow = Math.Max(1, (int)(ImGui.GetContentRegionAvail().X / cell));

            for (int i = 0; i < visible.Length; i++)
            {
                DrawAssetCell(visible[i]);

                if ((i + 1) % perRow != 0)
                    ImGui.SameLine();
            }
        }

        ImGui.EndChild();
        ImGui.End();
    }

    /// <summary>
    /// 挑選模式的橫幅。沒在挑的時候完全不佔位置。
    /// </summary>
    private void DrawReplaceBanner(WorldEntry entry)
    {
        if (_replaceFromType is not short fromType)
            return;

        var document = _session.Document;
        int count = document?.Objects.Count(o => o.Type == fromType) ?? 0;

        ImGui.PushStyleColor(ImGuiCol.ChildBg, new NVector4(0.20f, 0.32f, 0.20f, 1f));
        if (ImGui.BeginChild("replaceBanner", new NVector2(0f, 0f), ImGuiChildFlags.AutoResizeY))
        {
            ImGui.TextColored(Warning, $"挑一個模型，取代這張圖的 {count} 個 type {fromType}");
            ImGui.TextColored(Muted, "點縮圖就會整批換掉。可以撤銷。");

            if (ImGui.Checkbox("只顯示同類別", ref _replaceSameCategoryOnly))
            {
                var source = _assets.FirstOrDefault(a => a.ObjectType == fromType);
                _categoryFilter = _replaceSameCategoryOnly && source is not null
                    ? source.Category
                    : AssetCategory.Unclassified;
            }

            ImGui.SetNextItemWidth(160f);
            ImGui.SliderFloat("縮放倍率", ref _replaceScale, 0.1f, 5f, "×%.2f");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(
                    "兩個模型高矮差很多時用。\n" +
                    "游標移到縮圖上會顯示照高度算出來的建議值 —— 但要不要用是美術判斷，這裡不自動套。");

            ImGui.SameLine();
            if (ImGui.SmallButton("重設"))
                _replaceScale = 1f;

            ImGui.SameLine();
            if (ImGui.SmallButton("取消替換"))
            {
                _replaceFromType = null;
                _replaceScale = 1f;
            }
        }

        ImGui.EndChild();
        ImGui.PopStyleColor();
        ImGui.Separator();
    }

    /// <summary>在挑選模式下點一個模型：整張圖換掉。</summary>
    private void ApplyReplaceWith(AssetEntry asset)
    {
        if (_replaceFromType is not short fromType || asset.ObjectType is not short toType)
            return;

        var scene = _game.ActiveScene as MapEditorScene;
        scene?.ReplaceObjectType(fromType, toType, _replaceScale);

        _replaceFromType = null;
        _replaceScale = 1f;
        _objectSummaryWorldIndex = -1;   // 下一幀重算摘要，數量才對得上
    }

    private void DrawAssetToolbar(WorldEntry entry)
    {
        ImGui.SetNextItemWidth(180f);
        ImGui.InputTextWithHint("##assetFilter", "搜尋檔名", ref _assetFilter, 64);

        ImGui.SameLine();
        ImGui.SetNextItemWidth(140f);

        // 「未分類」在這裡當成「全部」用：它是列舉的第一項，也是預設值。
        if (ImGui.BeginCombo("##category", _categoryFilter == AssetCategory.Unclassified
                ? "全部類別"
                : AssetCategoryNames.Of(_categoryFilter)))
        {
            if (ImGui.Selectable("全部類別", _categoryFilter == AssetCategory.Unclassified))
                _categoryFilter = AssetCategory.Unclassified;

            foreach (var category in AssetCategoryNames.All.Where(c => c != AssetCategory.Unclassified))
            {
                int count = _assets.Count(a => a.Category == category);
                if (count == 0)
                    continue;

                if (ImGui.Selectable($"{AssetCategoryNames.Of(category)} ({count})", _categoryFilter == category))
                    _categoryFilter = category;
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(120f);
        ImGui.SliderFloat("##assetSize", ref _assetThumbnailSize, 64f, 192f, "%.0f px");

        int unclassified = _assets.Count(a => a.Category == AssetCategory.Unclassified);
        if (unclassified > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(Warning, $"{unclassified} 個未分類");
        }
    }

    /// <summary>模型用到的貼圖清單。解 BMD 不便宜，所以查過就快取。</summary>
    private string[] GetTextureNames(AssetEntry asset)
    {
        if (_assetTextures.TryGetValue(asset.Id, out var cached))
            return cached;

        var names = AssetCatalog.TextureNames(asset.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        _assetTextures[asset.Id] = names;
        return names;
    }

    private void ToggleAssetSelection(string id)
    {
        if (!_selectedAssets.Remove(id))
            _selectedAssets.Add(id);

        _assetAnchor = id;
    }

    /// <summary>把畫面上從 <paramref name="from"/> 到 <paramref name="to"/> 之間的整段選起來。</summary>
    private void SelectAssetRange(string from, string to)
    {
        var order = VisibleAssets().Select(a => a.Id).ToList();
        int a = order.IndexOf(from);
        int b = order.IndexOf(to);

        if (a < 0 || b < 0)
            return;

        foreach (string id in order.GetRange(Math.Min(a, b), Math.Abs(a - b) + 1))
            _selectedAssets.Add(id);

        _assetAnchor = to;
    }

    /// <summary>目前篩選條件下看得到的素材，順序與畫面一致。</summary>
    private AssetEntry[] VisibleAssets()
        => _assets
            .Where(a => _categoryFilter == AssetCategory.Unclassified || a.Category == _categoryFilter)
            .Where(a => string.IsNullOrWhiteSpace(_assetFilter)
                     || a.FileName.Contains(_assetFilter, StringComparison.OrdinalIgnoreCase))
            .ToArray();

    /// <summary>
    /// 批次標註列。
    /// </summary>
    /// <remarks>
    /// 剩下 1145 個未分類的模型只能靠人工，而右鍵一個一個標是做不完的。
    /// 這一排讓「選一批 → 按一個類別」變成兩個動作。
    ///
    /// 分類器的自動規則只剩一條（貼圖鏤空 → 草木，精確度 71%），
    /// 其餘都被實測推翻了 —— 所以人工標註不是備案，是主力。
    /// </remarks>
    private void DrawBatchLabelling()
    {
        if (_selectedAssets.Count == 0)
            return;

        ImGui.Separator();
        ImGui.Text($"選取了 {_selectedAssets.Count} 個");

        ImGui.SameLine();
        if (ImGui.SmallButton("取消選取"))
            _selectedAssets.Clear();

        ImGui.SameLine();
        if (ImGui.SmallButton("全選（目前篩選）"))
        {
            foreach (var asset in VisibleAssets())
                _selectedAssets.Add(asset.Id);
        }

        ImGui.TextColored(Muted, "按一個類別，整批標註（寫進 object-catalog.json）");

        int index = 0;

        foreach (var category in AssetCategoryNames.All)
        {
            if (category == AssetCategory.Unclassified)
                continue;

            if (ImGui.SmallButton(AssetCategoryNames.Of(category)))
                ApplyCategoryToSelection(category);

            if (++index % 6 != 0)
                ImGui.SameLine();
        }

        ImGui.NewLine();

        if (ImGui.SmallButton("清除分類（回到自動判定）"))
        {
            foreach (var asset in _assets.Where(a => _selectedAssets.Contains(a.Id)))
                _catalog.ClearCategory(asset);

            RefreshAssets();
        }
    }

    private void ApplyCategoryToSelection(AssetCategory category)
    {
        foreach (var asset in _assets.Where(a => _selectedAssets.Contains(a.Id)))
            _catalog.SetCategory(asset, category);

        _session.StatusMessage = $"{_selectedAssets.Count} 個模型標成「{AssetCategoryNames.Of(category)}」";
        _selectedAssets.Clear();
        RefreshAssets();
    }

    private void RefreshAssets()
    {
        if (_session.LoadedWorld is { } world)
            _assets = _catalog.Scan(_session.DataPath, world.Index, WorldCatalog.GetTileObjectTypes(world));
    }

    private void DrawAssetCell(AssetEntry asset)
    {
        ImGui.BeginGroup();
        ImGui.PushID(asset.Id);

        var id = _thumbnails.Get(asset.Path);
        var size = new NVector2(_assetThumbnailSize, _assetThumbnailSize);

        bool selected = _selectedAssets.Contains(asset.Id);

        if (selected)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new NVector4(0.3f, 0.55f, 0.9f, 1f));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 3f);
        }

        if (id.HasValue)
            ImGui.Image(id.Value, size, NVector2.Zero, NVector2.One,
                selected ? new NVector4(0.65f, 0.8f, 1f, 1f) : NVector4.One);
        else
            ImGui.Button("…", size);

        if (selected)
        {
            ImGui.PopStyleVar();
            ImGui.PopStyleColor();
        }

        // 點擊有兩種意思，用修飾鍵分開：
        //   單純點  → 把 type 帶進放置工具，直接開始擺
        //   Cmd 點  → 加進／移出選取（批次標註用）
        //   Shift 點 → 從錨點到這裡整段選起來
        if (ImGui.IsItemClicked())
        {
            var io = ImGui.GetIO();

            if (_replaceFromType is not null && asset.ObjectType is not null)
                ApplyReplaceWith(asset);
            else if (io.KeyShift && _assetAnchor is not null)
                SelectAssetRange(_assetAnchor, asset.Id);
            else if (io.KeySuper || io.KeyCtrl)
                ToggleAssetSelection(asset.Id);
            else if (asset.ObjectType is short clickedType)
            {
                _session.PlaceObjectType = clickedType;
                _session.Tool = EditorToolKind.PlaceObject;
            }
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.Text(asset.FileName);
            ImGui.TextColored(Muted, $"類別：{AssetCategoryNames.Of(asset.Category)}（來源：{asset.CategorySource}）");
            ImGui.TextColored(Muted, asset.ObjectType is short type ? $"物件 type {type}" : "具名模型，非 ObjectNN");

            // 挑選模式下多給一行：兩個模型的高度比。承諾在橫幅的提示裡，這裡兌現。
            if (_replaceFromType is short replaceFrom && asset.ObjectType is short replaceTo)
            {
                ImGui.Separator();
                var preview = ObjectTypeReplacer.Inspect(
                    _session.Document!, replaceFrom, replaceTo, _session.DataPath);

                ImGui.TextColored(Warning, $"點下去換掉 {preview.Count} 個");

                if (preview.SuggestedScale is float suggested)
                    ImGui.TextColored(Muted,
                        $"高度 {preview.FromShape!.Height:0} → {preview.ToShape!.Height:0}，" +
                        $"要維持原高度建議 ×{suggested:0.##}");
                else
                    ImGui.TextColored(Muted, "量不到其中一個模型的尺寸，縮放請自行判斷");
            }

            // 貼圖清單：要替換素材就得先知道這個模型吃哪幾張圖。
            ImGui.Separator();
            foreach (var texture in GetTextureNames(asset))
                ImGui.TextColored(Muted, texture);

            ImGui.EndTooltip();
        }

        // 右鍵改分類，改完立刻寫檔。
        if (ImGui.BeginPopupContextItem("category"))
        {
            ImGui.TextColored(Muted, asset.FileName);
            ImGui.Separator();

            foreach (var category in AssetCategoryNames.All)
            {
                if (ImGui.MenuItem(AssetCategoryNames.Of(category), string.Empty, asset.Category == category))
                {
                    _catalog.SetCategory(asset, category);
                    _assetWorldIndex = -1; // 下一幀重掃，讓分類立刻反映
                }
            }

            ImGui.EndPopup();
        }

        // 檔名太長就截斷，格子寬度要對齊縮圖。
        string label = Path.GetFileNameWithoutExtension(asset.FileName);
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + _assetThumbnailSize);
        ImGui.TextColored(asset.Category == AssetCategory.Unclassified ? Muted : Normal, label);
        ImGui.PopTextWrapPos();

        ImGui.PopID();
        ImGui.EndGroup();
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
            ImGui.Text(_session.StatusMessage);
            ImGui.SameLine(ImGui.GetWindowWidth() - 120f);
            ImGui.TextColored(Muted, $"{ImGui.GetIO().Framerate:F0} FPS");
        }

        ImGui.End();
    }

    private bool Matches(WorldEntry world)
    {
        if (_showOnlyPlayable && !world.IsPlayable)
            return false;

        if (string.IsNullOrWhiteSpace(_worldFilter))
            return true;

        return world.Name.Contains(_worldFilter, StringComparison.OrdinalIgnoreCase)
            || world.Index.ToString().Contains(_worldFilter, StringComparison.Ordinal);
    }

    private static string Flag(bool present, string label) => present ? label : "·";
}
