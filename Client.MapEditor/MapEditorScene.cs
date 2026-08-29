using Client.Main;
using Client.Main.Controls;
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

    /// <summary>ImGui 這一幀有沒有吃掉滑鼠/鍵盤。由 <see cref="MapEditorGame"/> 每幀更新。</summary>
    public bool UiCapturesInput { get; set; }

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

        bool acceptInput = !UiCapturesInput && !_session.IsLoading;

        HoveredTile = acceptInput
            ? TerrainPicker.Pick(World, MuGame.Instance.MouseRay)
            : default;

        UpdateObjectReport();

        HandleEditing(acceptInput);
        HandleShortcuts(acceptInput);
        PushPendingEdits();

        if (_session.ObjectsDirty)
        {
            RebuildWorldObjects();
            _session.ObjectsDirty = false;
        }

        _session.Camera.Update(gameTime, acceptInput);

        ApplyObjectDrawDistance();
    }

    /// <summary>物件數連續這麼多幀沒變，就當作載完了。</summary>
    private const int ObjectSettleFrames = 90;

    private bool _pendingReport;
    private int _settleFrames;
    private int _lastObjectCount = -1;

    /// <summary>等世界的物件數穩定下來，再對帳。</summary>
    private void UpdateObjectReport()
    {
        if (!_pendingReport || _session.IsLoading || World is not EditorWorldControl world)
            return;

        int count = world.Objects.Count;

        if (count != _lastObjectCount)
        {
            _lastObjectCount = count;
            _settleFrames = 0;
            return;
        }

        if (++_settleFrames < ObjectSettleFrames)
            return;

        _pendingReport = false;

        if (_session.LoadedWorld is WorldEntry entry)
            ReportObjectLoading(entry, world);

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

        // 物件工具是單次點擊，不是連續筆劃。
        if (_session.Tool is EditorToolKind.PlaceObject or EditorToolKind.SelectObject)
        {
            if (pressed && !wasPressed && acceptInput && HoveredTile.Valid)
                HandleObjectClick(document);

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

    private void HandleObjectClick(MapDocument document)
    {
        if (_session.Tool == EditorToolKind.PlaceObject)
            PlaceObject(document);
        else
            SelectObjectAt(document);
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

        World.Terrain.ApplyEditedTerrain(
            layer1: document.Layer1,
            layer2: document.Layer2,
            alpha: document.Alpha,
            attributes: document.Attributes,
            heightMap: document.Height?.Data.Select(c => new Color(c.R, c.G, c.B)).ToArray(),
            lightData: null);

        _session.TerrainDirty = false;
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
            string directory = _session.Settings.ProjectDirectoryFor(worldIndex);
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
            _session.Document = await MapDocument.LoadAsync(entry);
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
            _session.Camera.FrameWholeMap();

            // 物件是非同步載入的：Initialize() 回來時模型還在排隊，
            // 載不到的也還沒被 RemoveFailed 移掉。這時候數是數不準的，
            // 所以只標記待對帳，等數量不再變動再算。
            _pendingReport = true;
            _settleFrames = 0;
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
}
