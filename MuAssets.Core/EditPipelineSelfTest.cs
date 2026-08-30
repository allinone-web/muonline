using Client.Data.ATT;
using Client.Data.MAP;

namespace MuAssets.Core;

/// <summary>
/// 不用人動滑鼠就能驗證編輯管線：程式化地下筆、檢查資料、撤銷、再檢查。
/// </summary>
/// <remarks>
/// **零引擎相依**，所以有兩條跑法：
/// <list type="bullet">
///   <item>MonoGame 編輯器的 <c>--selftest</c>（順便驗改動有沒有推進渲染端）；</item>
///   <item><c>tools/MapCoreTests</c> 的無頭回歸測試，不開視窗、可進 CI。</item>
/// </list>
/// 兩邊跑的是同一份斷言，所以 Godot 版接手時這份仍然是驗收標準。
/// </remarks>
public static class EditPipelineSelfTest
{
    /// <summary>測試下筆的格子原點。宿主截圖驗證時把相機對到這附近。</summary>
    public const int OriginX = 100;
    public const int OriginY = 100;

    public static bool Run(EditSession session, WorldEntry? world)
    {
        var document = session.Document;
        if (document is null)
        {
            Console.WriteLine("[selftest] 沒有載入地圖");
            return false;
        }

        var results = new List<(string Name, bool Passed, string Detail)>
        {
            PaintTile(session, document),
            SculptHeight(session, document),
            PaintAttribute(session, document),
            UndoRedo(session, document),
            PlaceAndDeleteObject(session, document),
            SaveAndReloadProject(session, document),
            RoleAnnotations(session, document, world),
            BlankMap(),
            EyedropperPick(session, document),
            AutoTransition(session, document),
            BoxSelectAndScatter(session, document),
            LightBrush(session, document),
            CopyPasteBlock(session, document),
            ExportToClientFormat(session, document),
            SpawnAreasAndOpenMuExport(session, document),
            Validation(session, document, world),
        };

        Console.WriteLine();
        Console.WriteLine("=== 編輯管線自我測試 ===");

        foreach (var (name, passed, detail) in results)
            Console.WriteLine($"[{(passed ? " ok " : "FAIL")}] {name,-14} {detail}");

        bool allPassed = results.All(r => r.Passed);
        Console.WriteLine();
        Console.WriteLine(allPassed ? "全部通過。" : "有項目失敗。");

        return allPassed;
    }

    private static (string, bool, string) PaintTile(EditSession session, MapDocument document)
    {
        const byte target = 5; // TileWater01

        // 這一項測的是「硬邊繪製」那條路：整個筆刷都換成同一個索引。
        // 自動過渡是另一條路，由 AutoTransition() 那一項負責。
        bool autoTransition = session.AutoTransition;
        session.AutoTransition = false;
        int index = Index(OriginX, OriginY);
        byte before = document.Layer1[index];

        session.Tool = EditorToolKind.PaintLayer1;
        session.PaintTileIndex = target;
        session.Brush.Shape = BrushShape.Square;
        session.Brush.Radius = 4;

        session.ApplyStroke(OriginX, OriginY);

        int painted = CountInSquare(document.Layer1, OriginX, OriginY, 4, target);
        bool passed = document.Layer1[index] == target && painted == 81;

        session.AutoTransition = autoTransition;

        return ("貼圖繪製", passed, $"9×9 = {painted} 格塗成索引 {target}（原本 {before}）");
    }

    private static (string, bool, string) SculptHeight(EditSession session, MapDocument document)
    {
        int index = Index(OriginX + 20, OriginY);
        byte before = document.HeightAt(index);

        session.Tool = EditorToolKind.SculptHeight;
        session.HeightMode = HeightMode.Raise;
        session.HeightStep = 30f;
        session.Brush.Shape = BrushShape.Circle;
        session.Brush.Radius = 6;
        session.Brush.Strength = 1f;
        session.Brush.Falloff = 1f;

        session.ApplyStroke(OriginX + 20, OriginY);

        byte after = document.HeightAt(index);

        // 中心衰減後權重最大，邊緣應該幾乎沒動 —— 驗證衰減有作用。
        byte edge = document.HeightAt(Index(OriginX + 20 + 6, OriginY));
        bool passed = after > before && edge <= after;

        return ("高度雕刻", passed, $"中心 {before} → {after}，邊緣 {edge}");
    }

    private static (string, bool, string) PaintAttribute(EditSession session, MapDocument document)
    {
        int index = Index(OriginX, OriginY + 20);

        session.Tool = EditorToolKind.PaintAttribute;
        session.AttributeFlag = TWFlags.NoMove;
        session.AttributeErase = false;
        session.Brush.Shape = BrushShape.Square;
        session.Brush.Radius = 2;

        session.ApplyStroke(OriginX, OriginY + 20);

        bool set = document.Attributes[index].HasFlag(TWFlags.NoMove);

        session.AttributeErase = true;
        session.ApplyStroke(OriginX, OriginY + 20);

        bool cleared = !document.Attributes[index].HasFlag(TWFlags.NoMove);

        return ("屬性繪製", set && cleared, $"設定 {set}、清除 {cleared}");
    }

    private static (string, bool, string) UndoRedo(EditSession session, MapDocument document)
    {
        int index = Index(OriginX + 40, OriginY + 40);
        byte original = document.Layer1[index];

        session.Tool = EditorToolKind.PaintLayer1;
        session.PaintTileIndex = (byte)(original == 7 ? 8 : 7);
        session.Brush.Shape = BrushShape.Point;

        session.ApplyStroke(OriginX + 40, OriginY + 40);
        byte painted = document.Layer1[index];

        session.Undo();
        byte undone = document.Layer1[index];

        session.Redo();
        byte redone = document.Layer1[index];

        bool passed = painted != original && undone == original && redone == painted;
        return ("撤銷／重做", passed, $"{original} → {painted} → 撤銷 {undone} → 重做 {redone}");
    }

    private static (string, bool, string) PlaceAndDeleteObject(EditSession session, MapDocument document)
    {
        int originalCount = document.Objects.Count;
        short type = document.Objects.Count > 0 ? document.Objects[0].Type : (short)0;

        // 直接建物件，等同於「放置工具在該格點一下」。
        var placed = new MapObjectInstance
        {
            Type = type,
            Position = new System.Numerics.Vector3(
                (OriginX + 6 + 0.5f) * MuConstants.TerrainScale,
                (OriginY + 6 + 0.5f) * MuConstants.TerrainScale,
                document.HeightAt(Index(OriginX + 6, OriginY + 6)) * 1.5f),
            Scale = 1.25f,
        };

        document.Objects.Add(placed);
        session.ObjectHistory.Push(ObjectEdit.Add(placed));
        session.ObjectsDirty = true;

        int afterPlace = document.Objects.Count;

        session.SelectedObject = placed;
        session.DeleteSelectedObject();
        int afterDelete = document.Objects.Count;

        session.UndoObject();   // 復原刪除
        int afterUndoDelete = document.Objects.Count;

        session.UndoObject();   // 復原放置
        int afterUndoPlace = document.Objects.Count;

        bool passed = afterPlace == originalCount + 1
                   && afterDelete == originalCount
                   && afterUndoDelete == originalCount + 1
                   && afterUndoPlace == originalCount;

        return ("物件放置刪除", passed,
            $"{originalCount} → 放置 {afterPlace} → 刪除 {afterDelete} → 撤銷 {afterUndoDelete} → 撤銷 {afterUndoPlace}");
    }

    /// <summary>存成專案再讀回來，比對資料是否一致。</summary>
    /// <summary>
    /// 語義角色：存檔後讀回不遺失，而且撞號會被校驗器抓到。
    /// </summary>
    /// <remarks>
    /// 這兩件事是攻城戰、競技場、任務觸發點共用的基礎（見
    /// docs/系統精簡決策-保留簡化刪除.md §21）。標註掉了不會有任何錯誤訊息 ——
    /// 地圖照樣打得開、照樣畫得出來，只是玩法在伺服器端找不到那扇門，
    /// 所以只能靠測試守住。
    /// </remarks>
    private static (string, bool, string) RoleAnnotations(EditSession session, MapDocument document, WorldEntry? world)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"mu-editor-selftest-roles-{Environment.ProcessId}");

        // 記下原本的樣子：這個測試會改文件，後面的測試還要用。
        var touched = document.Objects.Take(3).ToArray();
        var before = touched.Select(o => o.Clone()).ToArray();

        try
        {
            // 空白的新地圖上沒有物件可標註。那不是失敗，是這個案例不適用。
            if (touched.Length < 3)
                return ("語義角色", true, "略過（地圖上的物件不足三個）");

            touched[0].Role = "siege.gate";
            touched[0].RoleId = 3;
            touched[0].Tags = ["breakable", "phase2"];

            touched[1].Role = "siege.statue";
            touched[1].RoleId = 1;

            // 和第一個撞號，校驗器應該要抓到。
            touched[2].Role = "siege.gate";
            touched[2].RoleId = 3;

            MapProjectIo.SaveAsync(document, directory).GetAwaiter().GetResult();
            var reloaded = MapProjectIo.LoadAsync(directory).GetAwaiter().GetResult();

            bool rolesMatch = document.Objects.Count == reloaded.Objects.Count
                           && document.Objects.Zip(reloaded.Objects).All(pair =>
                                  pair.First.Role == pair.Second.Role &&
                                  pair.First.RoleId == pair.Second.RoleId &&
                                  pair.First.Tags.SequenceEqual(pair.Second.Tags));

            // 沒有標註的物件不該多出欄位來，舊專案才讀得回原樣。
            bool cleanDefaults = reloaded.Objects.Skip(3).All(o => !o.HasRole && o.RoleId == 0 && o.Tags.Length == 0);

            int duplicates = world is null
                ? -1
                : MapValidator
                    .Validate(reloaded, world, session.TextureMappings, session.NpcCatalog)
                    .Count(i => i.Category == "角色");

            bool passed = rolesMatch && cleanDefaults && duplicates == 2;

            return ("語義角色", passed,
                $"存讀一致 {rolesMatch}、未標註物件保持乾淨 {cleanDefaults}、撞號抓到 {duplicates} 筆（應為 2）");
        }
        catch (Exception ex)
        {
            return ("語義角色", false, ex.Message);
        }
        finally
        {
            for (int i = 0; i < touched.Length && i < before.Length; i++)
            {
                touched[i].Role = before[i].Role;
                touched[i].RoleId = before[i].RoleId;
                touched[i].Tags = before[i].Tags;
            }

            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// 從零建一張空白地圖，存成客戶端格式再讀回來。
    /// </summary>
    /// <remarks>
    /// 空白地圖有三個值錯了不會報錯、只會讓地圖看起來壞掉，所以逐一檢查：
    /// 第二層要是哨兵值 255（填 0 的話整張圖被 0 號貼圖蓋掉）、
    /// 光照要是 128（填 0 是一張全黑的地圖）、屬性要全部可走。
    /// </remarks>
    private static (string, bool, string) BlankMap()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"mu-editor-selftest-blank-{Environment.ProcessId}");
        const int worldIndex = 999;

        try
        {
            var blank = MapDocument.CreateBlank(worldIndex);

            var export = MapExporter.ExportAsync(blank, directory, worldIndex).GetAwaiter().GetResult();
            if (!export.Success)
                return ("空白新地圖", false, export.Error ?? "匯出失敗");

            var entry = new WorldEntry(worldIndex, worldIndex - 1, $"World{worldIndex}", directory, true, true, false, []);
            var reloaded = MapDocument.LoadAsync(entry).GetAwaiter().GetResult();

            bool layer2IsSentinel = reloaded.Layer2.All(v => v == TerrainTextureMapping.NoLayerIndex);
            bool allWalkable = reloaded.Attributes.All(a => a == 0);
            bool flat = reloaded.Height is not null
                     && Enumerable.Range(0, MapDocument.CellCount).All(i => reloaded.HeightAt(i) == 0);
            bool neutralLight = reloaded.Light is not null
                             && reloaded.Light.Data.Take(MapDocument.CellCount).All(c => c.R == 128 && c.G == 128 && c.B == 128);

            // 伺服器那一側：OpenMU 的規則是「0 或 1 可走、1 是安全區」，
            // 所以全 0 代表整張圖都能走。
            var terrain = MapExporter.BuildServerTerrainData(reloaded);
            bool serverWalkable = terrain.Length == MapDocument.CellCount + 3
                               && terrain.Skip(3).All(b => b is 0 or 1);

            bool passed = layer2IsSentinel && allWalkable && flat && neutralLight && serverWalkable;

            return ("空白新地圖", passed,
                $"第二層哨兵 {layer2IsSentinel}、全可走 {allWalkable}、平地 {flat}、中性光照 {neutralLight}、伺服器地形 {serverWalkable}");
        }
        catch (Exception ex)
        {
            return ("空白新地圖", false, ex.Message);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// 吸管：畫下去、換掉筆刷設定、再吸回來，要拿到原本畫的值。
    /// </summary>
    /// <remarks>
    /// 每支筆吸的東西不同，所以逐一驗。第二層要特別驗哨兵值 255：
    /// 吸到「沒有第二層」時要設 PaintLayer2AsEmpty，而不是把 255 當成索引。
    /// </remarks>
    private static (string, bool, string) EyedropperPick(EditSession session, MapDocument document)
    {
        const int x = OriginX + 60;
        const int y = OriginY + 60;
        int index = Index(x, y);

        var before = (session.Tool, session.PaintTileIndex, session.PaintAlphaValue,
                      session.FlattenTarget, session.AttributeFlag, session.HeightMode,
                      session.PaintLayer2AsEmpty);

        try
        {
            document.Layer1[index] = 42;
            document.Layer2[index] = TerrainTextureMapping.NoLayerIndex;
            document.Alpha[index] = 200;
            document.Attributes[index] = TWFlags.SafeZone;

            session.Tool = EditorToolKind.PaintLayer1;
            session.PaintTileIndex = 0;
            Eyedropper.Pick(session.Tools, document, x, y);
            bool layer1 = session.PaintTileIndex == 42;

            session.Tool = EditorToolKind.PaintLayer2;
            session.PaintLayer2AsEmpty = false;
            Eyedropper.Pick(session.Tools, document, x, y);
            bool layer2 = session.PaintLayer2AsEmpty;

            session.Tool = EditorToolKind.PaintAlpha;
            session.PaintAlphaValue = 0f;
            Eyedropper.Pick(session.Tools, document, x, y);
            bool alpha = Math.Abs(session.PaintAlphaValue - 200f) < 0.01f;

            session.Tool = EditorToolKind.PaintAttribute;
            session.AttributeFlag = TWFlags.NoMove;
            Eyedropper.Pick(session.Tools, document, x, y);
            bool attribute = session.AttributeFlag == TWFlags.SafeZone;

            session.Tool = EditorToolKind.SculptHeight;
            session.FlattenTarget = -1f;
            Eyedropper.Pick(session.Tools, document, x, y);
            bool height = Math.Abs(session.FlattenTarget - document.HeightAt(index)) < 0.01f
                       && session.HeightMode == HeightMode.Flatten;

            // 邊界外不該吸到東西，也不該炸掉。
            bool outside = Eyedropper.Pick(session.Tools, document, -1, 999) is null;

            bool passed = layer1 && layer2 && alpha && attribute && height && outside;

            return ("吸管", passed,
                $"第一層 {layer1}、第二層哨兵 {layer2}、混合 {alpha}、屬性 {attribute}、高度 {height}、界外安全 {outside}");
        }
        catch (Exception ex)
        {
            return ("吸管", false, ex.Message);
        }
        finally
        {
            (session.Tool, session.PaintTileIndex, session.PaintAlphaValue,
             session.FlattenTarget, session.AttributeFlag, session.HeightMode,
             session.PaintLayer2AsEmpty) = before;
        }
    }

    /// <summary>
    /// 自動過渡：核心塗實、邊緣做漸層，而且一次撤銷要三樣一起還原。
    /// </summary>
    /// <remarks>
    /// 撤銷那一項是重點。這支筆同時改第一層、第二層與混合值，
    /// 而筆劃原本只記得一種目標 —— 那樣撤銷只會還原其中一樣，
    /// 畫面看起來復原了、資料其實是壞的。
    /// </remarks>
    private static (string, bool, string) AutoTransition(EditSession session, MapDocument document)
    {
        const int x = OriginX + 80;
        const int y = OriginY + 80;
        const byte painted = 7;

        var before = (session.Tool, session.PaintTileIndex, session.AutoTransition,
                      session.Brush.Radius, session.Brush.Falloff, session.Brush.Shape);

        try
        {
            // 先把這一帶鋪成別的貼圖，才看得出過渡。
            for (int dy = -8; dy <= 8; dy++)
            {
                for (int dx = -8; dx <= 8; dx++)
                {
                    int i = Index(x + dx, y + dy);
                    document.Layer1[i] = 1;
                    document.Layer2[i] = TerrainTextureMapping.NoLayerIndex;
                    document.Alpha[i] = 0;
                }
            }

            session.Tool = EditorToolKind.PaintLayer1;
            session.PaintTileIndex = painted;
            session.AutoTransition = true;
            session.Brush.Shape = BrushShape.Circle;
            session.Brush.Radius = 5;
            session.Brush.Falloff = 1f;

            var stroke = new EditStroke(EditTarget.Layer1, "測試");
            EditorTools.Apply(session.Tools, document, stroke, x, y);

            int center = Index(x, y);
            bool solidCore = document.Layer1[center] == painted
                          && document.Layer2[center] == TerrainTextureMapping.NoLayerIndex;

            // 邊緣：第一層保持原樣，新貼圖在第二層，混合值介於中間。
            int edge = Index(x + 4, y);
            bool blendedEdge = document.Layer1[edge] == 1
                            && document.Layer2[edge] == painted
                            && document.Alpha[edge] is > 0 and < 255;

            // 由內往外混合值要遞減。
            bool ramps = document.Alpha[Index(x + 2, y)] > document.Alpha[Index(x + 4, y)];

            stroke.Apply(document, undo: true);

            bool undone = document.Layer1[center] == 1
                       && document.Layer2[edge] == TerrainTextureMapping.NoLayerIndex
                       && document.Alpha[edge] == 0;

            bool passed = solidCore && blendedEdge && ramps && undone;

            return ("自動過渡", passed,
                $"核心塗實 {solidCore}、邊緣混合 {blendedEdge}、漸層遞減 {ramps}、三樣一起撤銷 {undone}");
        }
        catch (Exception ex)
        {
            return ("自動過渡", false, ex.Message);
        }
        finally
        {
            (session.Tool, session.PaintTileIndex, session.AutoTransition,
             session.Brush.Radius, session.Brush.Falloff, session.Brush.Shape) = before;
        }
    }

    /// <summary>
    /// 框選多選與散佈筆刷：整批動作要算一次撤銷。
    /// </summary>
    /// <remarks>
    /// 「一次撤銷」是這兩個功能的重點，不是附帶條件：
    /// 框選刪掉 30 個物件如果變成 30 筆歷史，使用者要按 30 次撤銷，
    /// 而且中途停手會留下半毀的地圖。刪除還原是「插回原本的索引」，
    /// 所以批次撤銷必須反序執行 —— 這裡連刪除後的順序一起驗。
    /// </remarks>
    private static (string, bool, string) BoxSelectAndScatter(EditSession session, MapDocument document)
    {
        const int x = OriginX + 100;
        const int y = OriginY + 100;

        var before = (session.Tool, session.PlaceObjectType, session.ScatterCount,
                      session.ScatterSpacing, session.ScatterAvoidBlocked, session.Brush.Radius);

        int originalCount = document.Objects.Count;
        var originalOrder = document.Objects.ToArray();

        try
        {
            session.PlaceObjectType = document.Objects.Count > 0 ? document.Objects[0].Type : (short)0;
            session.ScatterCount = 12;
            session.ScatterSpacing = 1f;
            session.ScatterAvoidBlocked = false;
            session.Brush.Radius = 6;

            int scattered = session.ScatterAt(x, y);
            bool scatterPlaced = scattered > 0 && document.Objects.Count == originalCount + scattered;

            // 撒出來的東西應該散開，不是疊在同一點上。
            var fresh = document.Objects.Skip(originalCount).ToArray();
            bool spread = fresh.Length < 2 || fresh.Select(o => (o.TileX, o.TileY)).Distinct().Count() > 1;

            // 隨機大小要真的隨機，不是全部 1.0。
            bool varied = fresh.Length < 2 || fresh.Select(o => MathF.Round(o.Scale, 3)).Distinct().Count() > 1;

            // 框選整個散佈範圍，應該至少抓到剛剛撒的那些。
            session.SelectedObjects.Clear();
            session.SelectInRectangle(x - 8, y - 8, x + 8, y + 8);
            bool selected = session.SelectedObjects.Count >= scattered;

            int selectedCount = session.SelectedObjects.Count;
            session.DeleteSelectedObject();
            bool deleted = document.Objects.Count == originalCount + scattered - selectedCount;

            // 一次撤銷就要把整批放回來，而且順序與原本一致。
            session.UndoObject();
            bool restoredCount = document.Objects.Count == originalCount + scattered;

            session.UndoObject();
            bool backToStart = document.Objects.Count == originalCount
                            && document.Objects.SequenceEqual(originalOrder);

            bool passed = scatterPlaced && spread && varied && selected && deleted && restoredCount && backToStart;

            return ("框選與散佈", passed,
                $"撒了 {scattered} 個、散開 {spread}、大小有變化 {varied}、框選 {selectedCount} 個、" +
                $"整批刪除 {deleted}、一次撤銷還原 {restoredCount}、順序一致 {backToStart}");
        }
        catch (Exception ex)
        {
            return ("框選與散佈", false, ex.Message);
        }
        finally
        {
            (session.Tool, session.PlaceObjectType, session.ScatterCount,
             session.ScatterSpacing, session.ScatterAvoidBlocked, session.Brush.Radius) = before;

            session.SelectedObjects.Clear();
        }
    }

    /// <summary>
    /// 光照筆刷：塗、加亮、壓暗，而且撤銷要還原。
    /// </summary>
    /// <remarks>
    /// MU 的地形光照是烘焙在 TerrainLight.OZB 裡的逐格顏色，渲染時乘上去 ——
    /// 「打光」在這裡不是放光源，是直接畫在地上。火堆旁邊的地會亮，
    /// 是因為有人畫上去的。
    /// </remarks>
    private static (string, bool, string) LightBrush(EditSession session, MapDocument document)
    {
        const int x = OriginX + 120;
        const int y = OriginY + 120;

        if (document.Light?.Data is null)
            return ("光照筆刷", true, "略過（這張圖沒有光照資料）");

        var before = (session.Tool, session.LightMode, session.Brush.Radius,
                      session.Brush.Falloff, session.Brush.Strength, session.Brush.Shape);

        int index = Index(x, y);
        var original = document.LightAt(index);

        try
        {
            session.Tool = EditorToolKind.PaintLight;
            session.Brush.Shape = BrushShape.Circle;
            session.Brush.Radius = 4;
            session.Brush.Falloff = 0f;
            session.Brush.Strength = 1f;

            session.LightMode = LightMode.Darken;
            var darkStroke = new EditStroke(EditTarget.Light, "測試");
            EditorTools.Apply(session.Tools, document, darkStroke, x, y);
            bool darkened = document.LightAt(index).R < original.R || original.R == 0;

            session.LightMode = LightMode.Brighten;
            var brightStroke = new EditStroke(EditTarget.Light, "測試");
            EditorTools.Apply(session.Tools, document, brightStroke, x, y);
            bool brightened = document.LightAt(index).R == 255;

            // 撤銷要一路退回原本的顏色。
            brightStroke.Apply(document, undo: true);
            darkStroke.Apply(document, undo: true);

            var restored = document.LightAt(index);
            bool undone = restored.R == original.R && restored.G == original.G && restored.B == original.B;

            // 衰減開著時，邊緣的變化要比中心小。
            session.Brush.Falloff = 1f;
            session.LightMode = LightMode.Darken;
            var falloffStroke = new EditStroke(EditTarget.Light, "測試");
            EditorTools.Apply(session.Tools, document, falloffStroke, x, y);

            bool ramps = document.LightAt(Index(x + 3, y)).R > document.LightAt(index).R;
            falloffStroke.Apply(document, undo: true);

            bool passed = darkened && brightened && undone && ramps;

            return ("光照筆刷", passed,
                $"壓暗 {darkened}、加亮 {brightened}、撤銷還原 {undone}、邊緣較弱 {ramps}");
        }
        catch (Exception ex)
        {
            return ("光照筆刷", false, ex.Message);
        }
        finally
        {
            (session.Tool, session.LightMode, session.Brush.Radius,
             session.Brush.Falloff, session.Brush.Strength, session.Brush.Shape) = before;
        }
    }

    /// <summary>
    /// 區塊複製貼上：地形五種資料與物件都要跟著過去。
    /// </summary>
    private static (string, bool, string) CopyPasteBlock(EditSession session, MapDocument document)
    {
        // 用絕對座標而不是 OriginX + n：OriginX 是 100，加到 160 就是 260，
        // 超出 256 的地圖邊界。這一項自己踩過。
        const int sourceX = 20;
        const int sourceY = 20;
        const int targetX = 40;
        const int targetY = 40;
        const int size = 6;

        int objectsBefore = document.Objects.Count;
        int undoBefore = session.History.UndoDepth;

        try
        {
            // 先在來源區弄出一個可辨識的圖案。
            for (int dy = 0; dy < size; dy++)
            {
                for (int dx = 0; dx < size; dx++)
                {
                    int i = Index(sourceX + dx, sourceY + dy);
                    document.Layer1[i] = (byte)(10 + dx);
                    document.Alpha[i] = (byte)(dy * 20);
                    document.Attributes[i] = dx == 0 ? TWFlags.NoMove : 0;
                }
            }

            session.ClipboardIncludesObjects = true;
            session.CopyRegion(sourceX, sourceY, sourceX + size - 1, sourceY + size - 1);

            bool copied = session.Clipboard is { Width: size, Height: size };

            session.PasteAt(targetX, targetY);

            bool layersMatch = true;
            bool attributesMatch = true;

            for (int dy = 0; dy < size && layersMatch; dy++)
            {
                for (int dx = 0; dx < size; dx++)
                {
                    int from = Index(sourceX + dx, sourceY + dy);
                    int to = Index(targetX + dx, targetY + dy);

                    if (document.Layer1[from] != document.Layer1[to] || document.Alpha[from] != document.Alpha[to])
                    {
                        layersMatch = false;
                        break;
                    }

                    if (document.Attributes[from] != document.Attributes[to])
                        attributesMatch = false;
                }
            }

            int pastedObjects = document.Objects.Count - objectsBefore;

            // 貼上是一筆多目標筆劃，撤銷一次就該把整塊地形還原。
            session.Undo();

            bool undone = true;
            for (int dy = 0; dy < size && undone; dy++)
            {
                for (int dx = 0; dx < size; dx++)
                {
                    int to = Index(targetX + dx, targetY + dy);

                    if (document.Layer1[to] == (byte)(10 + dx) && document.Alpha[to] == (byte)(dy * 20))
                    {
                        undone = false;
                        break;
                    }
                }
            }

            if (pastedObjects > 0)
                session.UndoObject();

            bool objectsRestored = document.Objects.Count == objectsBefore;
            bool passed = copied && layersMatch && attributesMatch && undone && objectsRestored;

            return ("區塊複製貼上", passed,
                $"複製 {copied}、貼圖與混合一致 {layersMatch}、屬性一致 {attributesMatch}、" +
                $"物件 {pastedObjects} 個、一次撤銷還原地形 {undone}、物件還原 {objectsRestored}");
        }
        catch (Exception ex)
        {
            return ("區塊複製貼上", false, ex.Message);
        }
        finally
        {
            while (session.History.UndoDepth > undoBefore)
                session.Undo();
        }
    }

    private static (string, bool, string) SaveAndReloadProject(EditSession session, MapDocument document)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"mu-editor-selftest-project-{Environment.ProcessId}");

        try
        {
            MapProjectIo.SaveAsync(document, directory).GetAwaiter().GetResult();
            var reloaded = MapProjectIo.LoadAsync(directory).GetAwaiter().GetResult();

            bool layersMatch = document.Layer1.SequenceEqual(reloaded.Layer1)
                            && document.Layer2.SequenceEqual(reloaded.Layer2)
                            && document.Alpha.SequenceEqual(reloaded.Alpha);

            bool attributesMatch = document.Attributes.SequenceEqual(reloaded.Attributes);

            bool heightMatch = document.Height is null
                ? reloaded.Height is null
                : reloaded.Height is not null && Enumerable.Range(0, MapDocument.CellCount)
                    .All(i => document.HeightAt(i) == reloaded.HeightAt(i));

            bool objectsMatch = document.Objects.Count == reloaded.Objects.Count
                             && document.Objects.Zip(reloaded.Objects).All(pair =>
                                    pair.First.Type == pair.Second.Type &&
                                    pair.First.Position == pair.Second.Position &&
                                    Math.Abs(pair.First.Scale - pair.Second.Scale) < 0.0001f);

            bool passed = layersMatch && attributesMatch && heightMatch && objectsMatch;

            return ("專案存讀", passed,
                $"層 {layersMatch}、屬性 {attributesMatch}、高度 {heightMatch}、物件 {objectsMatch}（{reloaded.Objects.Count} 個）");
        }
        catch (Exception ex)
        {
            return ("專案存讀", false, ex.Message);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// 匯出成客戶端格式，再用 Client.Data 的 Reader 讀回來比對 ——
    /// 這是「畫出來的東西進不進得了遊戲」的直接驗證。
    /// </summary>
    private static (string, bool, string) ExportToClientFormat(EditSession session, MapDocument document)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"mu-editor-selftest-export-{Environment.ProcessId}");

        try
        {
            var result = MapExporter.ExportAsync(document, directory, document.WorldIndex).GetAwaiter().GetResult();
            if (!result.Success)
                return ("匯出客戶端格式", false, result.Error ?? "未知錯誤");

            int worldIndex = document.WorldIndex;

            var map = new Client.Data.MAP.MapReader()
                .Load(Path.Combine(directory, $"EncTerrain{worldIndex}.map")).GetAwaiter().GetResult();

            var att = new Client.Data.ATT.ATTReader()
                .Load(Path.Combine(directory, $"EncTerrain{worldIndex}.att")).GetAwaiter().GetResult();

            var obj = new Client.Data.OBJS.OBJReader()
                .Load(Path.Combine(directory, $"EncTerrain{worldIndex}.obj")).GetAwaiter().GetResult();

            bool layersMatch = document.Layer1.SequenceEqual(map.Layer1)
                            && document.Layer2.SequenceEqual(map.Layer2)
                            && document.Alpha.SequenceEqual(map.Alpha);

            // ATTWriter 只寫低 7 位（ATTReader 拒絕 >= 0x80），所以比對時也要遮罩。
            bool attributesMatch = Enumerable.Range(0, MapDocument.CellCount)
                .All(i => ((ushort)document.Attributes[i] & 0x7F) == (ushort)att.TerrainWall[i]);

            bool objectsMatch = obj.Objects.Length == document.Objects.Count;

            // 伺服器格式：3 byte 標頭 + 1 byte/格。
            var serverData = MapExporter.BuildServerTerrainData(document);
            bool serverMatch = serverData.Length == MapDocument.CellCount + 3;

            bool passed = layersMatch && attributesMatch && objectsMatch && serverMatch;

            return ("匯出客戶端格式", passed,
                $"{result.Files.Length} 個檔案、層 {layersMatch}、屬性 {attributesMatch}、物件 {obj.Objects.Length}、伺服器 att {serverData.Length} bytes");
        }
        catch (Exception ex)
        {
            return ("匯出客戶端格式", false, ex.Message);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>擺生怪區，匯出給 OpenMU，再檢查產出的內容。</summary>
    private static (string, bool, string) SpawnAreasAndOpenMuExport(EditSession session, MapDocument document)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"mu-editor-selftest-openmu-{Environment.ProcessId}");

        try
        {
            var catalog = session.NpcCatalog.Entries;
            if (catalog.Length == 0)
                return ("生怪與 OpenMU 匯出", false, "怪物目錄是空的，先跑 --build-npc-catalog");

            // 挑一個有伺服器名稱的怪，確保匯出的編號兩邊對得上。
            var monster = catalog.FirstOrDefault(e => e.Kind == NpcKind.Monster && e.ServerDesignation is not null)
                          ?? catalog[0];

            session.SpawnTypeId = monster.TypeId;
            int before = document.Spawns.Count;

            session.AddSpawnArea(OriginX, OriginY, OriginX + 15, OriginY + 15);
            session.AddSpawnArea(OriginX + 30, OriginY, OriginX + 30, OriginY);   // 單格

            int after = document.Spawns.Count;
            var area = document.Spawns[^2];

            var result = OpenMuExporter
                .ExportAsync(document, document.Spawns, "SelfTestMap", 0, directory)
                .GetAwaiter().GetResult();

            if (!result.Success)
                return ("生怪與 OpenMU 匯出", false, result.Error ?? "未知錯誤");

            // 伺服器地形資料：3 byte 標頭 + 1 byte/格。
            var attBytes = File.ReadAllBytes(Path.Combine(directory, "Terrain1.att"));
            bool attOk = attBytes.Length == MapDocument.CellCount + 3 && attBytes[1] == 255 && attBytes[2] == 255;

            string source = File.ReadAllText(Path.Combine(directory, "SelfTestMap.cs"));

            // CreateMonsterSpawn 的參數順序是 x1, x2, y1, y2。
            string expected = $"this.NpcDictionary[{monster.TypeId}], {area.X1}, {area.X2}, {area.Y1}, {area.Y2}";
            bool sourceOk = source.Contains(expected, StringComparison.Ordinal)
                         && source.Contains("internal const byte Number = 0;", StringComparison.Ordinal)
                         && source.Contains("protected override IEnumerable<MonsterSpawnArea> CreateMonsterSpawns()", StringComparison.Ordinal);

            bool passed = after == before + 2 && attOk && sourceOk;

            return ("生怪與 OpenMU 匯出", passed,
                $"{monster.Name}（#{monster.TypeId}）、生怪區 {before}→{after}、att {attBytes.Length} bytes、原始碼 {sourceOk}");
        }
        catch (Exception ex)
        {
            return ("生怪與 OpenMU 匯出", false, ex.Message);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>跑一次校驗，並確認它抓得到刻意製造的問題。</summary>
    private static (string, bool, string) Validation(EditSession session, MapDocument document, WorldEntry? world)
    {
        var entry = world;
        if (entry is null)
            return ("校驗器", false, "沒有載入地圖");

        try
        {
            // 刻意造一個「整片不可走的生怪區」，校驗器應該要抓到。
            for (int y = OriginY + 40; y <= OriginY + 45; y++)
            {
                for (int x = OriginX + 40; x <= OriginX + 45; x++)
                    document.Attributes[(y * MapDocument.Size) + x] = TWFlags.NoMove;
            }

            var blocked = SpawnArea.FromCorners(OriginX + 40, OriginY + 40, OriginX + 45, OriginY + 45);
            blocked.TypeId = session.SpawnTypeId;
            blocked.Name = "自我測試";
            document.Spawns.Add(blocked);

            var issues = MapValidator.Validate(document, entry, session.TextureMappings, session.NpcCatalog);

            bool foundBlockedSpawn = issues.Any(i =>
                i.Category == "生怪" && i.Severity == IssueSeverity.Error && ReferenceEquals(i.Spawn, blocked));

            document.Spawns.Remove(blocked);

            session.Issues = issues;
            session.IssuesStale = false;

            var byCategory = issues
                .GroupBy(i => i.Category)
                .Select(g => $"{g.Key} {g.Count()}");

            return ("校驗器", foundBlockedSpawn,
                $"{issues.Count} 項（{string.Join("、", byCategory)}）、抓到全不可走的生怪區 {foundBlockedSpawn}");
        }
        catch (Exception ex)
        {
            return ("校驗器", false, ex.Message);
        }
    }

    private static int Index(int x, int y) => (y * MapDocument.Size) + x;

    private static int CountInSquare(byte[] data, int centerX, int centerY, int radius, byte value)
    {
        int count = 0;

        for (int y = centerY - radius; y <= centerY + radius; y++)
        {
            for (int x = centerX - radius; x <= centerX + radius; x++)
            {
                if ((uint)x < MapDocument.Size && (uint)y < MapDocument.Size && data[Index(x, y)] == value)
                    count++;
            }
        }

        return count;
    }
}
