using Client.Main;
using Client.Main.Controls;
using Client.Main.Graphics;
using Client.Main.Objects;

// MapObjectInstance 的向量是 System.Numerics（與 Client.Data 的結構一致），
// 場景這邊其餘都用 MonoGame 的，只別名需要的那一個。
using NumericsVector3 = System.Numerics.Vector3;
using Client.Main.Scenes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using MuAssets.Core;

namespace Client.MapEditor;

/// <summary>
/// 編輯器的場景。負責載入世界並驅動相機；UI 疊層畫在 <see cref="MapEditorGame.Draw"/>。
/// </summary>
/// <remarks>
/// <see cref="BaseScene.ChangeWorldAsync{T}"/> 是泛型的（要編譯期型別 + <c>new()</c>），
/// 但編輯器要在執行期切換任一張圖，所以這裡自己做一份非泛型的載入流程。
/// </remarks>
public sealed class MapEditorScene : BaseScene
{
    private readonly EditorSession _session = EditorSession.Current;

    /// <summary>ImGui 這一幀有沒有吃掉滑鼠。由 <see cref="MapEditorGame"/> 每幀更新。</summary>
    public bool UiCapturesMouse { get; set; }

    /// <summary>
    /// ImGui 這一幀有沒有吃掉鍵盤（有輸入框在打字）。
    /// 跟滑鼠分開記：合成一個旗標的話，面板裡點過一次輸入框就連滑鼠轉鏡頭都動不了。
    /// </summary>
    public bool UiCapturesKeyboard { get; set; }

    /// <summary>ImGui 這一幀有沒有吃掉滑鼠或鍵盤。</summary>
    public bool UiCapturesInput
    {
        get => UiCapturesMouse || UiCapturesKeyboard;
        set => UiCapturesMouse = UiCapturesKeyboard = value;
    }

    private MouseState _previousMouse;
    private KeyboardState _previousKeyboard;

    /// <summary>滑鼠目前指到的格子，UI 也要用。</summary>
    public TerrainHit HoveredTile { get; private set; }

    public override async Task Load()
    {
        _session.DataPath = Constants.DataPath;
        _session.Worlds = WorldCatalog.Discover(Constants.DataPath);
        _session.StatusMessage = $"找到 {_session.Worlds.Length} 張地圖";

        await base.Load();

        // --world 指定哪張就開哪張；沒指定時預設 Lorencia（World1），
        // 再沒有就開第一張可用的圖。
        var initial = _session.StartupWorldIndex is int wanted
            ? _session.Worlds.FirstOrDefault(w => w.Index == wanted)
            : null;

        if (_session.StartupWorldIndex is int missing && initial is null)
            _session.StatusMessage = $"找不到 World{missing}，改開預設的圖";

        initial ??= _session.Worlds.FirstOrDefault(w => w.Index == 1 && w.IsPlayable)
                    ?? _session.Worlds.FirstOrDefault(w => w.IsPlayable);

        // --audit-objects：把每張圖都載一次，對帳物件有沒有全部活下來。
        if (_session.AuditObjects)
        {
            _auditQueue = new Queue<int>(_session.Worlds.Where(w => w.IsPlayable).Select(w => w.Index).Order());
            Console.WriteLine($"[稽核] 要走過 {_auditQueue.Count} 張圖");

            if (_auditQueue.TryDequeue(out int first))
                _session.RequestWorld(first);

            return;
        }

        if (initial is not null)
            _session.RequestWorld(initial.Index);
    }

    /// <summary>--audit-objects 還沒走到的圖。</summary>
    private Queue<int>? _auditQueue;

    private readonly List<string> _auditLosses = [];

    private Vector3 _lastDrawDistanceFocus = new(float.MinValue, float.MinValue, float.MinValue);

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        if (_session.RequestedWorldIndex is int requested && !_session.IsLoading)
        {
            _session.RequestedWorldIndex = null;
            _ = LoadWorldAsync(requested);
        }

        bool loading = _session.IsLoading;
        bool acceptMouse = !UiCapturesMouse && !loading;
        bool acceptKeyboard = !UiCapturesKeyboard && !loading;
        bool acceptInput = acceptMouse && acceptKeyboard;

        // 相機先更新，畫筆才問得到「這一幀是不是在用抓手平移」。
        // 擺在編輯之後的話，抓手要等一幀才生效，按下去的那一瞬間會先點出一筆。
        //
        // 黃金影像模式：相機由鏡位獨佔。
        // 每一幀都套而不是載入時套一次 —— 世界載完之後有 FrameWholeMap 之類的動作會改相機，
        // 只套一次的話基準圖會隨著「哪一幀截到」而變，那就不是基準了。
        if (_session.GoldenShot is { } goldenShot)
        {
            goldenShot.ApplyTo(_session.Camera);
            _session.Camera.Update(gameTime, acceptInput: false);
        }
        else
        {
            _session.Camera.Update(gameTime, acceptMouse, acceptKeyboard);
        }

        // 抓手平移時左鍵是在推鏡頭，不是在下筆 —— 不擋的話拖到哪就畫到哪。
        bool acceptEdits = acceptInput
                           && !_session.IsExternalProjectReadOnly
                           && !_session.Camera.IsPanning;

        HoveredTile = acceptMouse
            ? TerrainPicker.Pick(World, MuGame.Instance.MouseRay)
            : default;

        UpdateObjectReport();

        HandleEditing(acceptEdits);
        HandleShortcuts(acceptEdits);
        HandleClipboard(acceptEdits);
        PushPendingEdits();

        if (_session.ObjectsDirty)
        {
            RebuildWorldObjects();
            _session.ObjectsDirty = false;
        }

        ApplyObjectDrawDistance();
    }

    /// <summary>物件數連續這麼多幀沒變，就當作載完了。</summary>
    private const int ObjectSettleFrames = 90;

    /// <summary>
    /// 最多等這麼多幀。有些圖的物件數永遠不會停 —— 環境特效會持續生滅，
    /// 所以「連續 N 幀不變」在那些圖上永遠不成立，稽核會卡死在那裡。
    /// </summary>
    private const int ObjectSettleTimeoutFrames = 600;

    private bool _startupTileApplied;
    private bool _pendingReport;
    private int _settleFrames;
    private int _waitedFrames;
    private int _lastObjectCount = -1;

    /// <summary>等世界的物件數穩定下來，再對帳。</summary>
    private void UpdateObjectReport()
    {
        if (!_pendingReport || _session.IsLoading || World is not EditorWorldControl world)
            return;

        int count = world.Objects.Count;
        _waitedFrames++;

        if (count != _lastObjectCount)
        {
            _lastObjectCount = count;
            _settleFrames = 0;
        }
        else
        {
            _settleFrames++;
        }

        if (_settleFrames < ObjectSettleFrames && _waitedFrames < ObjectSettleTimeoutFrames)
            return;

        bool settled = _settleFrames >= ObjectSettleFrames;
        _pendingReport = false;

        if (_session.LoadedWorld is WorldEntry entry)
        {
            if (!settled)
                Console.WriteLine($"[物件] World{entry.Index}：物件數一直在變（環境特效），等到上限就先對帳");

            ReportObjectLoading(entry, world);
        }

        if (_auditQueue is null)
            return;

        if (_auditQueue.TryDequeue(out int next))
        {
            _session.RequestWorld(next);
        }
        else
        {
            Console.WriteLine("[稽核] 走完。掉物件的圖："
                + (_auditLosses.Count == 0 ? "沒有" : string.Join("、", _auditLosses)));
            MuGame.Instance.Exit();
        }
    }

    /// <summary>
    /// 載入後對一次帳：文件裡有幾個物件，世界裡真的活下來幾個。
    /// </summary>
    /// <remarks>
    /// 模型載不到的物件會被 <c>WorldControl.RemoveFailed</c> 靜靜地從世界移除 ——
    /// 畫面上就是少了東西，但沒有任何錯誤。編輯器如果不對帳，
    /// 使用者會以為自己畫的圖是對的，進遊戲才發現不見了一半。
    /// </remarks>
    private void ReportObjectLoading(WorldEntry entry, EditorWorldControl world)
    {
        var document = _session.Document;
        if (document is null)
            return;

        // 逐 type 比對，不能比總數：世界會自己加東西（鳥、環境特效、草），
        // 那些不在 .obj 裡。實測 World8 的世界物件比文件還多 845 個。
        var alive = world.Objects
            .GroupBy(o => o.Type)
            .ToDictionary(g => g.Key, g => g.Count());

        var lost = document.Objects
            .GroupBy(o => o.Type)
            .Select(g => (Type: g.Key, Missing: g.Count() - alive.GetValueOrDefault(g.Key)))
            .Where(x => x.Missing > 0)
            .OrderByDescending(x => x.Missing)
            .ToArray();

        int missing = lost.Sum(x => x.Missing);

        if (missing == 0)
        {
            Console.WriteLine($"[物件] World{entry.Index}：{document.Objects.Count} 個全部載入");
            return;
        }

        _auditLosses.Add($"World{entry.Index}（少 {missing}／{document.Objects.Count}）");

        Console.WriteLine(
            $"[物件] World{entry.Index}：{document.Objects.Count} 個裡有 {missing} 個沒進到世界，" +
            $"涉及 {lost.Length} 種 type：" +
            string.Join("、", lost.Take(10).Select(x => $"{x.Type}×{x.Missing}")));
    }

    /// <summary>
    /// 從零建一張新地圖，建完直接載入。
    /// </summary>
    /// <remarks>
    /// 直接寫進遊戲的 Data 目錄（就是編輯器正在讀的那個）——
    /// 新地圖的意義就在於馬上能載入來畫，中間再隔一層輸出目錄只是多一步。
    /// 已經存在的 WorldN 不會被覆蓋。
    /// </remarks>
    public async Task CreateNewWorldAsync(int worldIndex, string mapName, int donorWorldIndex)
    {
        if (_session.FileBusy)
            return;

        _session.FileBusy = true;

        try
        {
            string? worldsPath = string.IsNullOrWhiteSpace(_session.Settings.WorldsSourcePath)
                ? null
                : _session.Settings.WorldsSourcePath;

            var result = await NewMapScaffold.CreateAsync(
                _session.DataPath, worldIndex, mapName, donorWorldIndex, worldClassDirectory: worldsPath);

            if (!result.Success)
            {
                _session.FileMessage = $"建立失敗：{result.Error}";
                return;
            }

            _session.Worlds = WorldCatalog.Discover(_session.DataPath);

            _session.FileMessage =
                $"已建立 World{worldIndex}：地形 {result.Files.Length} 個、貼圖 {result.CopiedTextures.Length} 個" +
                (result.WorldClassPath is null ? "（沒有產生世界類別）" : "、含世界類別") +
                (result.Warnings.Length > 0 ? $"　警告：{string.Join("；", result.Warnings)}" : string.Empty);

            _session.RequestWorld(worldIndex);
        }
        catch (Exception ex)
        {
            _session.FileMessage = $"建立失敗：{ex.Message}";
        }
        finally
        {
            _session.FileBusy = false;
        }
    }

    /// <summary>
    /// 相機焦點移動後，依設定重算哪些物件要顯示。
    /// </summary>
    /// <remarks>
    /// 每幀都掃 2833 個物件太浪費，而焦點沒動時結果也不會變，所以只在移動超過一格時重算。
    /// </remarks>
    private void ApplyObjectDrawDistance()
    {
        if (World is not EditorWorldControl world)
            return;

        float distance = _session.Settings.ObjectDrawDistance;
        var focus = _session.Camera.Focus;

        if (world.ObjectDrawDistance == distance
            && Vector3.DistanceSquared(focus, _lastDrawDistanceFocus) < MuConstants.TerrainScale * MuConstants.TerrainScale)
        {
            return;
        }

        world.ObjectDrawDistance = distance;
        world.ApplyObjectDrawDistance(focus);
        _lastDrawDistanceFocus = focus;
    }

    /// <summary>
    /// 左鍵按住＝下筆。整段拖曳算一筆，放開才推進歷史。
    /// </summary>
    private void HandleEditing(bool acceptInput)
    {
        var mouse = Mouse.GetState();
        var document = _session.Document;

        bool pressed = mouse.LeftButton == ButtonState.Pressed;
        bool wasPressed = _previousMouse.LeftButton == ButtonState.Pressed;
        _previousMouse = mouse;

        if (document is null || _session.Tool == EditorToolKind.None)
            return;

        // 按住 Option（Alt）點一下 = 吸管。與 Photoshop、Tiled 一致：
        // 吸管不是獨立的模式，是「用目前這支筆去取樣」，所以不必離開手上的工具。
        var keyboard = Keyboard.GetState();
        bool eyedropper = keyboard.IsKeyDown(Keys.LeftAlt) || keyboard.IsKeyDown(Keys.RightAlt);

        if (eyedropper)
        {
            if (pressed && !wasPressed && acceptInput && HoveredTile.Valid)
            {
                _session.StatusMessage =
                    Eyedropper.Pick(_session.Tools, document, HoveredTile.TileX, HoveredTile.TileY)
                    ?? "這支筆沒有可以吸的東西";
            }

            // 吸的時候不要順手畫下去。
            return;
        }

        // 散佈是「按著拖過去一路撒」，與地形筆刷同一種手感。
        if (_session.Tool == EditorToolKind.Scatter)
        {
            if (pressed && acceptInput && HoveredTile.Valid)
                _session.ScatterAt(HoveredTile.TileX, HoveredTile.TileY);

            return;
        }

        // 選取：點一下選一個，拖出一個框選一群。
        if (_session.Tool == EditorToolKind.SelectObject)
        {
            HandleSelection(document, mouse, pressed, wasPressed, acceptInput);
            return;
        }

        if (_session.Tool == EditorToolKind.PlaceObject)
        {
            if (pressed && !wasPressed && acceptInput && HoveredTile.Valid)
                PlaceObject(document);

            return;
        }

        if (pressed && acceptInput && HoveredTile.Valid)
        {
            _session.ActiveStroke ??= new EditStroke(
                EditorTools.TargetOf(_session.Tool),
                EditorTools.DescriptionOf(_session.Tool));

            EditorTools.Apply(_session.Tools, document, _session.ActiveStroke, HoveredTile.TileX, HoveredTile.TileY);
            _session.TerrainDirty = true;
            _session.IssuesStale = true;
        }
        else if (!pressed && wasPressed && _session.ActiveStroke is not null)
        {
            // 放開左鍵才把這一筆收進歷史，這樣一次拖曳只需要撤銷一次。
            _session.History.Push(_session.ActiveStroke);
            _session.HasUnsavedChanges = true;
            _session.ActiveStroke = null;
            _session.LayerViewDirty = true;
        }
    }

    /// <summary>
    /// Cmd+C 複製游標周圍的區塊、Cmd+V 貼到游標處。
    /// </summary>
    /// <remarks>
    /// 複製的範圍用目前的筆刷半徑 —— 少一個「先框出區塊」的步驟，
    /// 而筆刷半徑本來就是「我現在關心多大範圍」的意思。
    /// 要精確的範圍就調筆刷半徑，畫面上的筆刷圈圈就是預覽。
    /// </remarks>
    private void HandleClipboard(bool acceptInput)
    {
        var keyboard = Keyboard.GetState();

        bool command = keyboard.IsKeyDown(Keys.LeftWindows) || keyboard.IsKeyDown(Keys.RightWindows)
                    || keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl);

        bool copy = command && keyboard.IsKeyDown(Keys.C) && !_previousKeyboard.IsKeyDown(Keys.C);
        bool paste = command && keyboard.IsKeyDown(Keys.V) && !_previousKeyboard.IsKeyDown(Keys.V);

        _previousKeyboard = keyboard;

        if (!acceptInput || !HoveredTile.Valid)
            return;

        int radius = Math.Max(1, _session.Brush.Radius);

        if (copy)
        {
            _session.CopyRegion(
                HoveredTile.TileX - radius, HoveredTile.TileY - radius,
                HoveredTile.TileX + radius, HoveredTile.TileY + radius);
        }
        else if (paste)
        {
            _session.PasteAt(HoveredTile.TileX - radius, HoveredTile.TileY - radius);
        }
    }

    /// <summary>拖曳超過這麼多像素才算框選，否則當成單擊。</summary>
    private const float BoxSelectThreshold = 6f;

    /// <summary>
    /// 選取工具：點一下選最近的一個，拖出一個框選一群。
    /// </summary>
    /// <remarks>
    /// 框是畫在<b>螢幕空間</b>的，判定也在螢幕空間 —— 把每個物件投影到畫面上，
    /// 看它落不落在框裡。用世界空間的矩形去框的話，斜角視角下框出來的東西
    /// 和使用者看到的對不上（畫面上明明在框裡的，世界座標卻在框外）。
    ///
    /// 投影只在放開的時候做一次。每幀投影 2833 個物件沒有必要。
    /// </remarks>
    private void HandleSelection(
        MapDocument document, MouseState mouse, bool pressed, bool wasPressed, bool acceptInput)
    {
        if (pressed && !wasPressed && acceptInput)
        {
            _session.BoxSelectStart = new Vector2(mouse.X, mouse.Y);
            _session.BoxSelectCurrent = _session.BoxSelectStart;
            return;
        }

        if (pressed && _session.BoxSelectStart is not null)
        {
            _session.BoxSelectCurrent = new Vector2(mouse.X, mouse.Y);
            return;
        }

        if (!pressed && wasPressed && _session.BoxSelectStart is Vector2 start)
        {
            var end = new Vector2(mouse.X, mouse.Y);
            _session.BoxSelectStart = null;
            _session.BoxSelectCurrent = null;

            if (!acceptInput)
                return;

            if (Vector2.Distance(start, end) < BoxSelectThreshold)
            {
                if (HoveredTile.Valid)
                    SelectObjectAt(document);

                return;
            }

            var keyboard = Keyboard.GetState();
            bool additive = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);

            SelectObjectsInScreenRectangle(document, start, end, additive);
        }
    }

    private void SelectObjectsInScreenRectangle(MapDocument document, Vector2 start, Vector2 end, bool additive)
    {
        float minX = MathF.Min(start.X, end.X);
        float maxX = MathF.Max(start.X, end.X);
        float minY = MathF.Min(start.Y, end.Y);
        float maxY = MathF.Max(start.Y, end.Y);

        if (!additive)
            _session.SelectedObjects.Clear();

        var device = MuGame.Instance.GraphicsDevice;
        int added = 0;

        foreach (var instance in document.Objects)
        {
            var projected = device.Viewport.Project(
                new Vector3(instance.Position.X, instance.Position.Y, instance.Position.Z),
                Camera.Instance.Projection,
                Camera.Instance.View,
                Matrix.Identity);

            // Project 的 Z 落在 0–1 之外表示點在near/far 平面外，X/Y 沒有意義。
            if (projected.Z is < 0f or > 1f)
                continue;

            if (projected.X < minX || projected.X > maxX || projected.Y < minY || projected.Y > maxY)
                continue;

            if (_session.SelectedObjects.Contains(instance))
                continue;

            _session.SelectedObjects.Add(instance);
            added++;
        }

        _session.StatusMessage = _session.SelectedObjects.Count == 0
            ? "框裡沒有物件"
            : $"選取 {_session.SelectedObjects.Count} 個物件（新增 {added}）";
    }

    private void PlaceObject(MapDocument document)
    {
        float x = _session.SnapToTile
            ? ((HoveredTile.TileX + 0.5f) * Constants.TERRAIN_SCALE)
            : HoveredTile.World.X;

        float y = _session.SnapToTile
            ? ((HoveredTile.TileY + 0.5f) * Constants.TERRAIN_SCALE)
            : HoveredTile.World.Y;

        // 物件的 Z 用該點的地形高度，否則會浮空或埋進地裡。
        float z = World?.Terrain?.RequestTerrainHeight(x, y) ?? HoveredTile.Height;

        float yaw = _session.PlaceRandomYaw > 0f
            ? (float)((_session.Random.NextDouble() * 2.0 - 1.0) * _session.PlaceRandomYaw)
            : 0f;

        float scale = 1f;
        if (_session.PlaceRandomScale > 0f)
            scale += (float)((_session.Random.NextDouble() * 2.0 - 1.0) * _session.PlaceRandomScale);

        var instance = new MapObjectInstance
        {
            Type = _session.PlaceObjectType,
            Position = new NumericsVector3(x, y, z),

            // .obj 裡的角度是「度」，客戶端載入時才轉弧度（見 WorldObjectFactory）。
            Angle = new NumericsVector3(0f, 0f, yaw),
            Scale = MathF.Max(0.05f, scale),
        };

        document.Objects.Add(instance);
        _session.ObjectHistory.Push(ObjectEdit.Add(instance));
        _session.IssuesStale = true;
        _session.SelectedObject = instance;
        _session.ObjectsDirty = true;
        _session.HasUnsavedChanges = true;
        _session.StatusMessage = $"放置 type {instance.Type} @ ({HoveredTile.TileX}, {HoveredTile.TileY})";
    }

    /// <summary>
    /// 把整張圖的某一種物件換成另一種（「這張圖的樹全部換成那棵樹」）。
    /// </summary>
    /// <remarks>
    /// 換的是每個實例的 type，原始 <c>.bmd</c> 完全沒動，所以撤銷一次就全部回來。
    /// 回傳換掉的數量。
    /// </remarks>
    public int ReplaceObjectType(short fromType, short toType, float scaleMultiplier = 1f)
    {
        var document = _session.Document;
        if (document is null || _session.IsExternalProjectReadOnly)
            return 0;

        var (edit, result) = ObjectTypeReplacer.Replace(document, fromType, toType, scaleMultiplier);
        if (edit is null)
        {
            _session.StatusMessage = $"沒有 type {fromType} 可以換";
            return 0;
        }

        _session.ObjectHistory.Push(edit);
        _session.IssuesStale = true;
        _session.ObjectsDirty = true;
        _session.HasUnsavedChanges = true;

        // 選取的物件可能就是剛被換掉的那個，型別已經變了，留著會誤導。
        _session.SelectedObject = null;

        string scaleNote = Math.Abs(scaleMultiplier - 1f) > 0.0001f
            ? $"，縮放 ×{scaleMultiplier:0.##}"
            : string.Empty;

        _session.StatusMessage =
            $"已把 {result.Replaced} 個 type {fromType} 換成 type {toType}{scaleNote}（可撤銷）";

        return result.Replaced;
    }

    /// <summary>選最靠近點擊處的物件。用平面距離就夠，物件通常貼著地面。</summary>
    private void SelectObjectAt(MapDocument document)
    {
        const float maxDistance = Constants.TERRAIN_SCALE * 2.5f;

        MapObjectInstance? best = null;
        float bestDistance = float.MaxValue;

        foreach (var candidate in document.Objects)
        {
            float dx = candidate.Position.X - HoveredTile.World.X;
            float dy = candidate.Position.Y - HoveredTile.World.Y;
            float distance = MathF.Sqrt((dx * dx) + (dy * dy));

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }

        _session.SelectedObject = bestDistance <= maxDistance ? best : null;

        _session.StatusMessage = _session.SelectedObject is null
            ? "這附近沒有物件"
            : $"選取 type {_session.SelectedObject.Type} @ ({_session.SelectedObject.TileX}, {_session.SelectedObject.TileY})";
    }

    public void DeleteSelectedObject() => _session.DeleteSelectedObject();

    public void CommitObjectTransform(MapObjectInstance instance, MapObjectInstance before)
        => _session.CommitObjectTransform(instance, before);

    public void UndoObject() => _session.UndoObject();

    public void RedoObject() => _session.RedoObject();

    /// <summary>
    /// 把文件的物件清單同步到畫面上的世界。
    /// </summary>
    /// <remarks>
    /// 整批重建而不是精細地增刪：物件數通常幾百到一萬，重建一次幾十毫秒，
    /// 但要精細同步就得維護「文件物件 ↔ 場景物件」的雙向對應，
    /// 在物件可以被撤銷／重做移動的情況下很容易對不上。
    /// </remarks>
    private void RebuildWorldObjects()
    {
        var document = _session.Document;
        if (document is null || World is null)
            return;

        foreach (var existing in World.Objects.Where(o => o.IsMapPlacementObject).ToArray())
        {
            World.RemoveObject(existing);
            existing.Dispose();
        }

        foreach (var instance in document.Objects)
        {
            var created = World.CreateMapTileObject(instance.To(document.ObjVersion));
            if (created is not null)
                _ = created.Load();
        }
    }

    private void HandleShortcuts(bool acceptInput)
    {
        var keyboard = Keyboard.GetState();
        var previous = _previousKeyboard;
        _previousKeyboard = keyboard;

        if (!acceptInput || _session.Document is null)
            return;

        bool command = keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl)
                    || keyboard.IsKeyDown(Keys.LeftWindows) || keyboard.IsKeyDown(Keys.RightWindows);

        if (!command)
            return;

        bool shift = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);

        // 物件工具與格子工具各有自己的歷史，快捷鍵依目前工具決定操作哪一個。
        bool objectMode = _session.Tool is EditorToolKind.PlaceObject or EditorToolKind.SelectObject;

        if (WasPressed(keyboard, previous, Keys.Z))
        {
            if (shift)
            {
                if (objectMode) RedoObject(); else Redo();
            }
            else
            {
                if (objectMode) UndoObject(); else Undo();
            }
        }
        else if (WasPressed(keyboard, previous, Keys.Y))
        {
            if (objectMode) RedoObject(); else Redo();
        }
    }

    public void Undo() => _session.Undo();

    public void Redo() => _session.Redo();

    /// <summary>
    /// 把文件的改動推進渲染端。每幀最多一次 —— 這個動作會讓地形快取整個重建。
    /// </summary>
    private void PushPendingEdits()
    {
        if (!_session.TerrainDirty || _session.Document is null || World?.Terrain is null)
            return;

        var document = _session.Document;

        long started = System.Diagnostics.Stopwatch.GetTimestamp();

        World.Terrain.ApplyEditedTerrain(
            layer1: document.Layer1,
            layer2: document.Layer2,
            alpha: document.Alpha,
            attributes: document.Attributes,
            heightMap: document.Height?.Data.Select(c => new Color(c.R, c.G, c.B)).ToArray(),
            // 光照只在真的改過時才送：它會讓渲染端重算整張的頂點色，
            // 每次下筆都送等於白付一次代價。
            lightData: _session.LightDirty
                ? document.Light?.Data.Select(c => new Color(c.R, c.G, c.B)).ToArray()
                : null);

        _session.LightDirty = false;

        _session.TerrainDirty = false;

        if (Environment.GetEnvironmentVariable("MU_EDITOR_DIAG") is not null)
        {
            Console.WriteLine(
                $"[編輯] 推進渲染端耗時 {System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds:F1} ms");
        }
    }

    /// <summary>在圖層俯視圖上拖出一個生怪區。</summary>
    public void AddSpawnArea(int startX, int startY, int endX, int endY)
        => _session.AddSpawnArea(startX, startY, endX, endY);

    public void DeleteSpawnArea(SpawnArea area)
    {
        var document = _session.Document;
        if (document is null)
            return;

        document.Spawns.Remove(area);

        if (ReferenceEquals(_session.SelectedSpawn, area))
            _session.SelectedSpawn = null;

        _session.HasUnsavedChanges = true;
    }

    /// <summary>匯出伺服器端資料：Terrain{N}.att + 地圖初始化器原始碼。</summary>
    public async Task ExportToOpenMuAsync()
    {
        var document = _session.Document;
        var entry = _session.LoadedWorld;

        if (document is null || entry is null || _session.FileBusy)
            return;

        if (RejectExternalProjectWrite())
            return;

        if (entry.MapNumber is not int mapNumber)
        {
            _session.FileMessage = "這張圖在客戶端沒有登記 WorldInfo，對不到 OpenMU 編號";
            return;
        }

        _session.FileBusy = true;

        try
        {
            string directory = Path.Combine(_session.Settings.OutputRoot, "openmu", $"World{entry.Index}");
            var result = await OpenMuExporter.ExportAsync(
                document, document.Spawns, entry.Name, mapNumber, directory);

            _session.FileMessage = result.Success
                ? $"已匯出 OpenMU 資料（{document.Spawns.Count} 個生怪區）到 {directory}"
                : $"匯出 OpenMU 失敗：{result.Error}";
        }
        finally
        {
            _session.FileBusy = false;
        }
    }

    /// <summary>重新載入目前這張圖。改過貼圖對應之後要用它才會生效。</summary>
    public void ReloadCurrentWorld()
    {
        if (_session.LoadedWorldIndex is not int worldIndex || _session.IsLoading)
            return;

        _session.LoadedWorldIndex = null;
        _session.RequestWorld(worldIndex);
    }

    /// <summary>把目前的地圖存成專案（map.json + PNG）。</summary>
    public async Task SaveProjectAsync()
    {
        var document = _session.Document;
        if (document is null || _session.FileBusy)
            return;

        if (RejectExternalProjectWrite())
            return;

        _session.FileBusy = true;

        try
        {
            string directory = _session.Settings.ProjectDirectoryFor(document.WorldIndex);
            await MapProjectIo.SaveAsync(document, directory);

            _session.HasUnsavedChanges = false;
            _session.FileMessage = $"已存專案：{directory}";
        }
        catch (Exception ex)
        {
            _session.FileMessage = $"存專案失敗：{ex.Message}";
        }
        finally
        {
            _session.FileBusy = false;
        }
    }

    /// <summary>讀回專案，取代目前的文件並重建畫面。</summary>
    public async Task LoadProjectAsync()
    {
        if (_session.LoadedWorldIndex is not int worldIndex || _session.FileBusy)
            return;

        _session.FileBusy = true;

        try
        {
            string directory = _session.ExternalProjectDirectory
                ?? _session.Settings.ProjectDirectoryFor(worldIndex);
            _session.Document = await MapProjectIo.LoadAsync(directory);

            _session.History.Clear();
            _session.ObjectHistory.Clear();
            _session.SelectedObject = null;
            _session.TerrainDirty = true;
            _session.ObjectsDirty = true;
            _session.LayerViewDirty = true;
            _session.HasUnsavedChanges = false;
            _session.FileMessage = $"已讀專案：{directory}（{_session.Document.Objects.Count} 個物件）";
        }
        catch (Exception ex)
        {
            _session.FileMessage = $"讀專案失敗：{ex.Message}";
        }
        finally
        {
            _session.FileBusy = false;
        }
    }

    /// <summary>匯出成客戶端讀得懂的五個檔案。</summary>
    public async Task ExportAsync()
    {
        var document = _session.Document;
        if (document is null || _session.FileBusy)
            return;

        if (RejectExternalProjectWrite())
            return;

        _session.FileBusy = true;

        try
        {
            string directory = _session.Settings.OutputDirectoryFor(document.WorldIndex);
            var result = await MapExporter.ExportAsync(document, directory, document.WorldIndex);

            _session.FileMessage = result.Success
                ? $"已匯出 {result.Files.Length} 個檔案到 {directory}" +
                  (result.BackedUp.Length > 0 ? $"（備份 {result.BackedUp.Length} 個）" : string.Empty)
                : $"匯出失敗：{result.Error}";
        }
        finally
        {
            _session.FileBusy = false;
        }
    }

    /// <summary>
    /// 把整張圖匯出成 Godot 吃得下的中立包（給 RealmForge 用）。
    /// </summary>
    /// <remarks>
    /// <b>來源是磁碟上的 Data 目錄，不是編輯器記憶體裡的文件。</b>
    /// <see cref="Client.AssetStudio.Export.GodotSceneExporter"/> 自己去讀 <c>World{N}/</c>，
    /// 所以還沒「匯出客戶端 → 部署到遊戲」的改動不會出現在中立包裡。
    /// 這裡不偷偷代跑部署（那會動到遊戲資源，不該是一個匯出按鈕的副作用），
    /// 改成把狀態講清楚，讓使用者自己決定。
    ///
    /// 匯出一張圖大約 8 秒、11 MB，所以整段丟到背景執行緒，不要卡住畫面。
    /// </remarks>
    public async Task ExportGodotAsync()
    {
        var document = _session.Document;
        if (document is null || _session.FileBusy)
            return;

        _session.FileBusy = true;
        int worldIndex = document.WorldIndex;
        string directory = _session.Settings.GodotExportDirectoryFor(worldIndex);
        string dataPath = _session.DataPath;
        bool withObjects = _session.Settings.GodotExportObjects;

        try
        {
            var result = await Task.Run(() =>
                Client.AssetStudio.Export.GodotSceneExporter.Export(
                    dataPath,
                    worldIndex,
                    directory,
                    new Client.AssetStudio.Export.GodotSceneExporter.Options(
                        ExportObjects: withObjects)));

            string warnings = result.Warnings.Length > 0
                ? $"\n警告 {result.Warnings.Length} 則：" +
                  string.Join("\n  ", result.Warnings.Take(5))
                : string.Empty;

            _session.FileMessage =
                $"已匯出 Godot 中立包到 {directory}\n" +
                $"地形貼圖 {result.TileTextures}、草 {result.GrassTextures}、" +
                $"物件 {result.ObjectInstances} 個（{result.ObjectTypesExported}/{result.ObjectTypes} 種模型）" +
                warnings +
                "\n在 RealmForge 端執行：tools/client/import_mu_map.sh " + worldIndex;
        }
        catch (Exception ex)
        {
            _session.FileMessage = $"Godot 匯出失敗：{ex.Message}";
        }
        finally
        {
            _session.FileBusy = false;
        }
    }

    /// <summary>
    /// 把匯出結果複製到遊戲的 Data 目錄。
    /// </summary>
    /// <remarks>
    /// 這是唯一會動到遊戲資源的操作，所以：目標路徑要使用者明確設定，
    /// 而且每個被覆蓋的檔案都先備份成 <c>.bak</c>。
    /// </remarks>
    public void Deploy()
    {
        var document = _session.Document;
        string target = _session.Settings.DeployDataPath;

        if (document is null || _session.FileBusy)
            return;

        if (RejectExternalProjectWrite())
            return;

        if (string.IsNullOrWhiteSpace(target) || !Directory.Exists(target))
        {
            _session.FileMessage = "請先設定部署目標（遊戲的 Data 目錄）";
            return;
        }

        _session.FileBusy = true;

        try
        {
            string source = _session.Settings.OutputDirectoryFor(document.WorldIndex);
            if (!Directory.Exists(source))
            {
                _session.FileMessage = "還沒有匯出結果，請先按「匯出」";
                return;
            }

            string destination = Path.Combine(target, $"World{document.WorldIndex}");
            Directory.CreateDirectory(destination);

            int copied = 0;
            int backed = 0;

            foreach (var file in Directory.EnumerateFiles(source).Where(f => !f.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)))
            {
                string destinationFile = Path.Combine(destination, Path.GetFileName(file));

                if (File.Exists(destinationFile) && !File.Exists(destinationFile + ".bak"))
                {
                    File.Copy(destinationFile, destinationFile + ".bak");
                    backed++;
                }

                File.Copy(file, destinationFile, overwrite: true);
                copied++;
            }

            _session.FileMessage = $"已部署 {copied} 個檔案到 {destination}（備份 {backed} 個）";
        }
        catch (Exception ex)
        {
            _session.FileMessage = $"部署失敗：{ex.Message}";
        }
        finally
        {
            _session.FileBusy = false;
        }
    }

    private static bool WasPressed(KeyboardState current, KeyboardState previous, Keys key)
        => current.IsKeyDown(key) && !previous.IsKeyDown(key);

    private async Task LoadWorldAsync(int worldIndex)
    {
        var entry = _session.Worlds.FirstOrDefault(w => w.Index == worldIndex);
        if (entry is null)
            return;

        _session.IsLoading = true;
        _session.StatusMessage = $"載入 World{worldIndex}（{entry.Name}）…";

        try
        {
            // 編輯器自己的資料複本，與渲染用的那份分開載入。
            _session.Document = _session.ExternalProjectDirectory is string externalProject
                ? await MapProjectIo.LoadAsync(externalProject)
                : await MapDocument.LoadAsync(entry);
            _session.LayerViewDirty = true;

            var tileObjectTypes = WorldCatalog.GetTileObjectTypes(entry);
            var world = new EditorWorldControl((short)worldIndex, tileObjectTypes);

            // 自訂的貼圖對應必須在 Initialize 之前設好 ——
            // TerrainLoader 在載入時才會去讀這張表，之後改就沒作用了。
            world.Terrain.TextureMappingFiles = _session.TextureMappings.BuildFor(worldIndex);

            // 與 BaseScene.ChangeWorldAsync 同樣的順序：先丟掉舊世界，再初始化新的。
            World?.Dispose();
            Controls.Add(world);
            await world.Initialize();

            World = world;
            _session.LoadedWorldIndex = worldIndex;
            // --tile 指定了就對到那一格（環繞、貼近地面），否則看全圖。
            // 只在第一次載入時用 —— 之後在編輯器裡切地圖應該回到全圖，
            // 不然換一張圖還停在上一張的座標上，會以為地圖載錯了。
            if (_session.StartupTile is (int startX, int startY) && !_startupTileApplied)
            {
                _startupTileApplied = true;
                _session.Camera.Mode = CameraMode.Orbit;
                _session.Camera.Distance = 1400f;
                _session.Camera.Yaw = MathHelper.ToRadians(-45f);
                _session.Camera.Pitch = MathHelper.ToRadians(30f);
                _session.Camera.FocusTile(startX, startY);
                _session.StatusMessage = $"對準格 ({startX}, {startY})";
            }
            else
            {
                _session.Camera.FrameWholeMap();
            }

            // 物件是非同步載入的：Initialize() 回來時模型還在排隊，
            // 載不到的也還沒被 RemoveFailed 移掉。這時候數是數不準的，
            // 所以只標記待對帳，等數量不再變動再算。
            _pendingReport = true;
            _settleFrames = 0;
            _waitedFrames = 0;
            _lastObjectCount = -1;

            if (_session.RunSelfTest)
            {
                _session.SelfTestPassed = EditorSelfTest.Run(_session, this);
                _session.RunSelfTest = false;
            }

            if (_session.ExportOnStartPath is string exportPath)
            {
                var exported = await MapExporter.ExportAsync(_session.Document, exportPath, worldIndex);
                Console.WriteLine(exported.Success
                    ? $"[export] {exported.Files.Length} 個檔案 -> {exportPath}"
                    : $"[export] 失敗：{exported.Error}");
                _session.ExportOnStartPath = null;
            }

            if (_session.ExportOpenMuOnStartPath is string openMuPath)
            {
                var exported = await OpenMuExporter.ExportAsync(
                    _session.Document, _session.Document.Spawns, entry.Name, entry.MapNumber ?? 0, openMuPath);

                Console.WriteLine(exported.Success
                    ? $"[openmu] {exported.Files.Length} 個檔案 -> {openMuPath}"
                    : $"[openmu] 失敗：{exported.Error}");
                _session.ExportOpenMuOnStartPath = null;
            }

            var warnings = _session.Document.Warnings;
            _session.StatusMessage =
                $"World{worldIndex}（{entry.Name}）：{world.Objects.Count} 個物件" +
                (tileObjectTypes is null ? "，無語意物件類別表" : string.Empty) +
                (warnings.Count > 0 ? $"，{warnings.Count} 項資料讀取失敗" : string.Empty);
        }
        catch (Exception ex)
        {
            _session.StatusMessage = $"World{worldIndex} 載入失敗：{ex.Message}";
            Console.WriteLine($"[MapEditorScene] World{worldIndex} 載入失敗：{ex}");
        }
        finally
        {
            _session.IsLoading = false;
        }
    }

    private bool RejectExternalProjectWrite()
    {
        if (!_session.IsExternalProjectReadOnly)
            return false;

        _session.FileMessage = "外部 --project 以唯讀模式開啟；未寫入 project、Data 或輸出目錄。";
        return true;
    }
}
