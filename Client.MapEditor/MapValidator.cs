using Client.Data.ATT;
using Client.Data.MAP;

namespace Client.MapEditor;

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

        return issues
            .OrderByDescending(i => i.Severity)
            .ThenBy(i => i.Category, StringComparer.Ordinal)
            .ToList();
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

    private static void CheckObjects(MapDocument document, WorldEntry entry, List<ValidationIssue> issues)
    {
        int outOfBounds = 0;
        int floating = 0;
        var missingModelTypes = new SortedSet<short>();

        MapObjectInstance? firstFloating = null;
        MapObjectInstance? firstOutOfBounds = null;

        foreach (var instance in document.Objects)
        {
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
