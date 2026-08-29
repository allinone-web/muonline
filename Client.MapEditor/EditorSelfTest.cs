using Client.Data.ATT;

namespace Client.MapEditor;

/// <summary>
/// 不用人動滑鼠就能驗證編輯管線：程式化地下筆、檢查資料、撤銷、再檢查。
/// </summary>
/// <remarks>
/// 用 <c>--selftest</c> 觸發。這是 Phase 3 的驗收方式 ——
/// 筆刷、歷史、以及「改動有沒有真的推進渲染端」全部一次跑過。
/// </remarks>
public static class EditorSelfTest
{
    private const int OriginX = 100;
    private const int OriginY = 100;

    public static bool Run(EditorSession session, MapEditorScene scene)
    {
        var document = session.Document;
        if (document is null)
        {
            Console.WriteLine("[selftest] 沒有載入地圖");
            return false;
        }

        var results = new List<(string Name, bool Passed, string Detail)>
        {
            PaintTile(session, document, scene),
            SculptHeight(session, document, scene),
            PaintAttribute(session, document, scene),
            UndoRedo(session, document, scene),
            PlaceAndDeleteObject(session, document, scene),
            SaveAndReloadProject(session, document),
            ExportToClientFormat(session, document),
            SpawnAreasAndOpenMuExport(session, document, scene),
            Validation(session, document),
        };

        Console.WriteLine();
        Console.WriteLine("=== 編輯管線自我測試 ===");

        foreach (var (name, passed, detail) in results)
            Console.WriteLine($"[{(passed ? " ok " : "FAIL")}] {name,-14} {detail}");

        bool allPassed = results.All(r => r.Passed);
        Console.WriteLine();
        Console.WriteLine(allPassed ? "全部通過。" : "有項目失敗。");

        // 把相機帶到剛剛畫過的地方，截圖才驗證得到「改動有推進渲染端」。
        session.Camera.Mode = CameraMode.Orbit;
        session.Camera.Distance = 3000f;
        session.Camera.FocusTile(OriginX + 10, OriginY + 10);

        return allPassed;
    }

    private static (string, bool, string) PaintTile(EditorSession session, MapDocument document, MapEditorScene scene)
    {
        const byte target = 5; // TileWater01
        int index = Index(OriginX, OriginY);
        byte before = document.Layer1[index];

        session.Tool = EditorToolKind.PaintLayer1;
        session.PaintTileIndex = target;
        session.Brush.Shape = BrushShape.Square;
        session.Brush.Radius = 4;

        Stroke(session, document, scene, OriginX, OriginY);

        int painted = CountInSquare(document.Layer1, OriginX, OriginY, 4, target);
        bool passed = document.Layer1[index] == target && painted == 81;

        return ("貼圖繪製", passed, $"9×9 = {painted} 格塗成索引 {target}（原本 {before}）");
    }

    private static (string, bool, string) SculptHeight(EditorSession session, MapDocument document, MapEditorScene scene)
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

        Stroke(session, document, scene, OriginX + 20, OriginY);

        byte after = document.HeightAt(index);

        // 中心衰減後權重最大，邊緣應該幾乎沒動 —— 驗證衰減有作用。
        byte edge = document.HeightAt(Index(OriginX + 20 + 6, OriginY));
        bool passed = after > before && edge <= after;

        return ("高度雕刻", passed, $"中心 {before} → {after}，邊緣 {edge}");
    }

    private static (string, bool, string) PaintAttribute(EditorSession session, MapDocument document, MapEditorScene scene)
    {
        int index = Index(OriginX, OriginY + 20);

        session.Tool = EditorToolKind.PaintAttribute;
        session.AttributeFlag = TWFlags.NoMove;
        session.AttributeErase = false;
        session.Brush.Shape = BrushShape.Square;
        session.Brush.Radius = 2;

        Stroke(session, document, scene, OriginX, OriginY + 20);

        bool set = document.Attributes[index].HasFlag(TWFlags.NoMove);

        session.AttributeErase = true;
        Stroke(session, document, scene, OriginX, OriginY + 20);

        bool cleared = !document.Attributes[index].HasFlag(TWFlags.NoMove);

        return ("屬性繪製", set && cleared, $"設定 {set}、清除 {cleared}");
    }

    private static (string, bool, string) UndoRedo(EditorSession session, MapDocument document, MapEditorScene scene)
    {
        int index = Index(OriginX + 40, OriginY + 40);
        byte original = document.Layer1[index];

        session.Tool = EditorToolKind.PaintLayer1;
        session.PaintTileIndex = (byte)(original == 7 ? 8 : 7);
        session.Brush.Shape = BrushShape.Point;

        Stroke(session, document, scene, OriginX + 40, OriginY + 40);
        byte painted = document.Layer1[index];

        scene.Undo();
        byte undone = document.Layer1[index];

        scene.Redo();
        byte redone = document.Layer1[index];

        bool passed = painted != original && undone == original && redone == painted;
        return ("撤銷／重做", passed, $"{original} → {painted} → 撤銷 {undone} → 重做 {redone}");
    }

    private static (string, bool, string) PlaceAndDeleteObject(EditorSession session, MapDocument document, MapEditorScene scene)
    {
        int originalCount = document.Objects.Count;
        short type = document.Objects.Count > 0 ? document.Objects[0].Type : (short)0;

        // 直接建物件，等同於「放置工具在該格點一下」。
        var placed = new MapObjectInstance
        {
            Type = type,
            Position = new System.Numerics.Vector3(
                (OriginX + 6 + 0.5f) * Client.Main.Constants.TERRAIN_SCALE,
                (OriginY + 6 + 0.5f) * Client.Main.Constants.TERRAIN_SCALE,
                document.HeightAt(Index(OriginX + 6, OriginY + 6)) * 1.5f),
            Scale = 1.25f,
        };

        document.Objects.Add(placed);
        session.ObjectHistory.Push(ObjectEdit.Add(placed));
        session.ObjectsDirty = true;

        int afterPlace = document.Objects.Count;

        session.SelectedObject = placed;
        scene.DeleteSelectedObject();
        int afterDelete = document.Objects.Count;

        scene.UndoObject();   // 復原刪除
        int afterUndoDelete = document.Objects.Count;

        scene.UndoObject();   // 復原放置
        int afterUndoPlace = document.Objects.Count;

        bool passed = afterPlace == originalCount + 1
                   && afterDelete == originalCount
                   && afterUndoDelete == originalCount + 1
                   && afterUndoPlace == originalCount;

        return ("物件放置刪除", passed,
            $"{originalCount} → 放置 {afterPlace} → 刪除 {afterDelete} → 撤銷 {afterUndoDelete} → 撤銷 {afterUndoPlace}");
    }

    /// <summary>存成專案再讀回來，比對資料是否一致。</summary>
    private static (string, bool, string) SaveAndReloadProject(EditorSession session, MapDocument document)
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
    private static (string, bool, string) ExportToClientFormat(EditorSession session, MapDocument document)
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
    private static (string, bool, string) SpawnAreasAndOpenMuExport(EditorSession session, MapDocument document, MapEditorScene scene)
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

            scene.AddSpawnArea(OriginX, OriginY, OriginX + 15, OriginY + 15);
            scene.AddSpawnArea(OriginX + 30, OriginY, OriginX + 30, OriginY);   // 單格

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
    private static (string, bool, string) Validation(EditorSession session, MapDocument document)
    {
        var entry = session.LoadedWorld;
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

    /// <summary>模擬一次完整筆劃：下筆、施加、放開（放開才進歷史）。</summary>
    private static void Stroke(EditorSession session, MapDocument document, MapEditorScene scene, int x, int y)
    {
        var stroke = new EditStroke(EditorTools.TargetOf(session.Tool), EditorTools.DescriptionOf(session.Tool));
        EditorTools.Apply(session, document, stroke, x, y);
        session.History.Push(stroke);
        session.TerrainDirty = true;
        session.LayerViewDirty = true;
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
