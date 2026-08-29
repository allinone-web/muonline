using Client.Data.ATT;
using Client.Data.MAP;

namespace MuAssets.Core;

public enum IssueSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>一項校驗結果。<paramref name="Tile"/> 有值時 UI 可以「跳過去看」。</summary>
public sealed record ValidationIssue(
    IssueSeverity Severity,
    string Category,
    string Message,
    (int X, int Y)? Tile = null,
    MapObjectInstance? Object = null,
    SpawnArea? Spawn = null);

/// <summary>
/// 地圖校驗：把「畫得出來但進遊戲會壞掉」的東西找出來。
/// </summary>
/// <remarks>
/// 每一條規則都對應到實際踩過的問題，不是憑空想的檢查項。
/// </remarks>
public static class MapValidator
{
    /// <summary>物件的 Z 與地形高度差超過這個值就當作懸空或埋入。</summary>
    private const float HeightTolerance = 120f;

    public static List<ValidationIssue> Validate(
        MapDocument document,
        WorldEntry entry,
        TextureMappingStore textureMappings,
        MonsterCatalog npcCatalog)
    {
        var issues = new List<ValidationIssue>();

        CheckTerrainData(document, issues);
        CheckTextureMapping(document, entry, textureMappings, issues);
        CheckObjects(document, entry, issues);
        CheckSpawns(document, entry, npcCatalog, issues);
        CheckServerMapping(entry, issues);
        CheckRoles(document, issues);

        return issues
            .OrderByDescending(i => i.Severity)
            .ThenBy(i => i.Category, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// 語義角色的唯一性：同一個 Role 底下，RoleId 不得重複。
    /// </summary>
    /// <remarks>
    /// 玩法是靠「角色 + 編號」去找地圖上的東西的（攻城戰要開 3 號城門、
    /// 競技場要把紅隊放到 1 號出生點）。編號撞號的時候伺服器只會挑到其中一個，
    /// 而且挑到哪一個沒有保證 —— 這種錯在遊戲裡極難查，但在這裡一眼就看得到。
    ///
    /// 只查物件。生怪區的識別是 Role + TeamId，而同一隊有多個出生區是正常的。
    /// </remarks>
    private static void CheckRoles(MapDocument document, List<ValidationIssue> issues)
    {
        foreach (var group in document.Objects
            .Where(o => o.HasRole)
            .GroupBy(o => (o.Role, o.RoleId)))
        {
            if (group.Count() <= 1)
                continue;

            foreach (var duplicate in group)
            {
                issues.Add(new ValidationIssue(
                    IssueSeverity.Error, "角色",
                    $"{group.Key.Role} 的 {group.Key.RoleId} 號有 {group.Count()} 個物件",
                    (duplicate.TileX, duplicate.TileY), Object: duplicate));
            }
        }

        // 生怪區不查重：同一隊有好幾個出生區是正常的（攻方十個人要分散進場），
        // SpawnArea 也沒有 RoleId —— 它的識別是 Role + TeamId，而那本來就允許多筆。
    }

    private static void CheckTerrainData(MapDocument document, List<ValidationIssue> issues)
    {
        if (document.Height is null)
        {
            issues.Add(new ValidationIssue(IssueSeverity.Error, "地形",
                "沒有高度圖 —— 地形會整片是平的（World19 的 TerrainHeight.OZB 就是 66620 個 0）"));
        }

        if (document.Light is null)
        {
            issues.Add(new ValidationIssue(IssueSeverity.Warning, "地形",
                "沒有光照圖 —— 地形會缺少明暗變化"));
        }

        // NoGround 但沒有 NoMove：玩家走得進去，然後掉進沒有地面的格子。
        int walkableHoles = 0;
        (int X, int Y)? firstHole = null;

        for (int i = 0; i < MapDocument.CellCount; i++)
        {
            var flags = document.Attributes[i];

            if (flags.HasFlag(TWFlags.NoGround) && !flags.HasFlag(TWFlags.NoMove))
            {
                walkableHoles++;
                firstHole ??= (i % MapDocument.Size, i / MapDocument.Size);
            }
        }

        if (walkableHoles > 0)
        {
            issues.Add(new ValidationIssue(IssueSeverity.Warning, "屬性",
                $"{walkableHoles} 格標了「無地面」但沒標「不可走」—— 玩家走得進去", firstHole));
        }

        int walkable = document.Attributes.Count(f => !f.HasFlag(TWFlags.NoMove) && !f.HasFlag(TWFlags.NoGround));
        if (walkable == 0)
        {
            issues.Add(new ValidationIssue(IssueSeverity.Error, "屬性", "整張圖沒有任何可行走的格子"));
        }
        else if (walkable < MapDocument.CellCount / 100)
        {
            issues.Add(new ValidationIssue(IssueSeverity.Warning, "屬性",
                $"可行走的格子只有 {walkable} 個（不到全圖的 1%）"));
        }
    }

    private static void CheckTextureMapping(
        MapDocument document,
        WorldEntry entry,
        TextureMappingStore textureMappings,
        List<ValidationIssue> issues)
    {
        var mapping = textureMappings.BuildFor(entry.Index);

        var used = new SortedSet<int>(document.Layer1.Select(v => (int)v));
        foreach (var value in document.Layer2)
        {
            if (value != TerrainTextureMapping.NoLayerIndex)
                used.Add(value);
        }

        foreach (int index in used)
        {
            bool resolved = mapping.TryGetValue(index, out var file)
                         && entry.TileFiles.Any(f => string.Equals(
                                Path.GetFileNameWithoutExtension(f),
                                Path.GetFileNameWithoutExtension(file),
                                StringComparison.OrdinalIgnoreCase));

            if (resolved)
                continue;

            int count = document.Layer1.Count(v => v == index)
                      + document.Layer2.Count(v => v == index);

            issues.Add(new ValidationIssue(IssueSeverity.Warning, "貼圖",
                $"索引 {index} 用在 {count} 格，但對不到檔案 —— 那些格子會沒有貼圖",
                FindFirstCell(document, index)));
        }
    }

    /// <summary>
    /// 這筆物件的數值是不是壞的。
    /// </summary>
    /// <remarks>
    /// 三種情形：型別是負的（型別編號本來就沒有負數）、
    /// 座標或縮放不是有限值（NaN / Infinity）、
    /// 或者縮放小到不可能是真的（非正規值，正常最小也在 0.01 這個量級）。
    /// </remarks>
    private static bool IsCorrupt(MapObjectInstance instance)
    {
        if (instance.Type < 0)
            return true;

        foreach (float value in new[]
        {
            instance.Position.X, instance.Position.Y, instance.Position.Z,
            instance.Angle.X, instance.Angle.Y, instance.Angle.Z,
            instance.Scale,
        })
        {
            if (!float.IsFinite(value))
                return true;
        }

        return instance.Scale is not 0f && MathF.Abs(instance.Scale) < 0.0001f;
    }

    private static void CheckObjects(MapDocument document, WorldEntry entry, List<ValidationIssue> issues)
    {
        int outOfBounds = 0;
        int floating = 0;
        int corrupt = 0;
        var missingModelTypes = new SortedSet<short>();

        MapObjectInstance? firstFloating = null;
        MapObjectInstance? firstOutOfBounds = null;
        MapObjectInstance? firstCorrupt = null;

        foreach (var instance in document.Objects)
        {
            // 官方資源裡有讀壞的記錄：World7 有一個 type −515、座標 9.1e−41、
            // 縮放 1e−39 的物件，World92 則有帶 NaN / Infinity 座標的。
            // 那些數字是浮點數的非正規值，看起來像資料其實是垃圾 ——
            // 它們永遠不會出現在遊戲裡，但會一直留在檔案裡跟著存回去。
            if (IsCorrupt(instance))
            {
                corrupt++;
                firstCorrupt ??= instance;
                continue;
            }

            if ((uint)instance.TileX >= MapDocument.Size || (uint)instance.TileY >= MapDocument.Size)
            {
                outOfBounds++;
                firstOutOfBounds ??= instance;
                continue;
            }

            if (!ModelExists(entry, instance.Type))
                missingModelTypes.Add(instance.Type);

            if (document.Height is null)
                continue;

            // 高度圖是 0–255，渲染時乘 1.5（見 TerrainRenderer.EnsureVertexCache）。
            float terrainZ = document.HeightAt((instance.TileY * MapDocument.Size) + instance.TileX) * 1.5f;

            if (MathF.Abs(instance.Position.Z - terrainZ) > HeightTolerance)
            {
                floating++;
                firstFloating ??= instance;
            }
        }

        if (corrupt > 0)
        {
            issues.Add(new ValidationIssue(
                IssueSeverity.Error, "物件",
                $"{corrupt} 個物件的數值是壞的（型別為負、座標非有限值，或縮放是非正規值）—— 它們永遠不會出現在遊戲裡",
                firstCorrupt is null ? null : (firstCorrupt.TileX, firstCorrupt.TileY),
                Object: firstCorrupt));
        }

        if (outOfBounds > 0)
        {
            issues.Add(new ValidationIssue(IssueSeverity.Error, "物件",
                $"{outOfBounds} 個物件在地圖範圍之外", Object: firstOutOfBounds));
        }

        if (missingModelTypes.Count > 0)
        {
            int affected = document.Objects.Count(o => missingModelTypes.Contains(o.Type));
            issues.Add(new ValidationIssue(IssueSeverity.Warning, "物件",
                $"{affected} 個物件（{missingModelTypes.Count} 種 type）載不到模型，在遊戲裡不會出現"));
        }

        if (floating > 0)
        {
            issues.Add(new ValidationIssue(IssueSeverity.Info, "物件",
                $"{floating} 個物件的高度與地形差超過 {HeightTolerance:F0} —— 可能懸空或埋在地下",
                Object: firstFloating));
        }
    }

    private static void CheckSpawns(
        MapDocument document,
        WorldEntry entry,
        MonsterCatalog npcCatalog,
        List<ValidationIssue> issues)
    {
        foreach (var spawn in document.Spawns)
        {
            int total = 0;
            int blocked = 0;

            for (int y = spawn.Y1; y <= spawn.Y2; y++)
            {
                for (int x = spawn.X1; x <= spawn.X2; x++)
                {
                    var flags = document.Attributes[(y * MapDocument.Size) + x];
                    total++;

                    if (flags.HasFlag(TWFlags.NoMove) || flags.HasFlag(TWFlags.NoGround))
                        blocked++;
                }
            }

            if (total > 0 && blocked == total)
            {
                issues.Add(new ValidationIssue(IssueSeverity.Error, "生怪",
                    $"{spawn.Name} 的整個範圍都不可行走 —— 怪生不出來",
                    (spawn.X1, spawn.Y1), Spawn: spawn));
            }
            else if (total > 0 && blocked > total * 0.7f)
            {
                issues.Add(new ValidationIssue(IssueSeverity.Warning, "生怪",
                    $"{spawn.Name} 有 {blocked}/{total} 格不可行走",
                    (spawn.X1, spawn.Y1), Spawn: spawn));
            }

            if (npcCatalog.Entries.Length > 0 && npcCatalog.Entries.All(e => e.TypeId != spawn.TypeId))
            {
                issues.Add(new ValidationIssue(IssueSeverity.Warning, "生怪",
                    $"編號 {spawn.TypeId} 不在怪物目錄裡 —— 伺服器可能沒有這個定義",
                    (spawn.X1, spawn.Y1), Spawn: spawn));
            }

            if (spawn.Quantity <= 0)
            {
                issues.Add(new ValidationIssue(IssueSeverity.Warning, "生怪",
                    $"{spawn.Name} 的數量是 {spawn.Quantity}", (spawn.X1, spawn.Y1), Spawn: spawn));
            }
        }
    }

    private static void CheckServerMapping(WorldEntry entry, List<ValidationIssue> issues)
    {
        if (entry.MapNumber is null)
        {
            issues.Add(new ValidationIssue(IssueSeverity.Warning, "伺服器",
                $"World{entry.Index} 在客戶端沒有登記 [WorldInfo]，對不到 OpenMU 的 map number，無法匯出伺服器資料"));
        }
    }

    /// <summary>
    /// 泛用路徑是 <c>Object{world}/Object{type+1:00}.bmd</c>。
    /// Object1（Lorencia）裡全是具名檔案，所以那裡只有有語意類別的 type 才載得到 ——
    /// 這個檢查對它會偏保守（可能誤報），對其他圖是準的。
    /// </summary>
    private static bool ModelExists(WorldEntry entry, short type)
    {
        string path = Path.Combine(
            Path.GetDirectoryName(entry.Directory) ?? string.Empty,
            $"Object{entry.Index}",
            $"Object{type + 1:00}.bmd");

        return File.Exists(path);
    }

    private static (int X, int Y)? FindFirstCell(MapDocument document, int index)
    {
        for (int i = 0; i < MapDocument.CellCount; i++)
        {
            if (document.Layer1[i] == index || document.Layer2[i] == index)
                return (i % MapDocument.Size, i / MapDocument.Size);
        }

        return null;
    }
}
