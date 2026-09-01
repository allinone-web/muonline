using Client.Data.BMD;
using Client.Data.MAP;

namespace MuAssets.Core;

public sealed record MapProjectInspection(
    MapProject? Project,
    string[] Errors,
    string[] Warnings,
    string[] TerrainTextureSources,
    string[] ModelSources,
    string[] ModelTextureSources)
{
    public bool IsValid => Project is not null && Errors.Length == 0;
    public bool IsLegacyCodecCompatible
        => Project is not null
           && Project.MapNumber is >= byte.MinValue and <= byte.MaxValue
           && Project.AttIndex is >= byte.MinValue and <= byte.MaxValue;
}

/// <summary>
/// Headless inspection for an external authoring project and the concrete assets
/// required by the legacy MU renderer. It never substitutes donors or defaults.
/// </summary>
public static class MapProjectInspector
{
    public static async Task<MapProjectInspection> InspectAsync(
        string projectDirectory,
        string? dataDirectory,
        bool requireRendererDependencies)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var terrainSources = new List<string>();
        var modelSources = new List<string>();
        var modelTextureSources = new List<string>();
        MapProject? project = null;
        MapDocument? document = null;

        try
        {
            project = await MapProjectIo.ReadAsync(projectDirectory);
            document = await MapProjectIo.LoadAsync(projectDirectory);
        }
        catch (Exception ex)
        {
            errors.Add(ex.Message);
        }

        if (project is null || document is null)
            return new(project, [.. errors], [.. warnings], [], [], []);

        if (!requireRendererDependencies)
            return new(project, [.. errors], [.. warnings], [], [], []);

        if (string.IsNullOrWhiteSpace(dataDirectory) || !Directory.Exists(dataDirectory))
        {
            errors.Add($"找不到 MU Data 目錄：{dataDirectory ?? "（未指定）"}");
            return new(project, [.. errors], [.. warnings], [], [], []);
        }

        string worldDirectory = Path.Combine(dataDirectory, $"World{project.WorldIndex}");
        string projectTextures = Path.Combine(projectDirectory, "textures");
        var usedTextureIndexes = document.Layer1
            .Concat(document.Layer2.Where(v => v != TerrainTextureMapping.NoLayerIndex))
            .Distinct()
            .Order();

        foreach (int index in usedTextureIndexes)
        {
            if (!TerrainTextureMapping.Default.TryGetValue(index, out string? fileName))
            {
                errors.Add($"非法地形貼圖索引 {index}；沒有檔名映射。");
                continue;
            }

            string? source = FirstExisting(
                Path.Combine(projectTextures, fileName),
                Path.Combine(worldDirectory, fileName));

            if (source is null)
                errors.Add($"缺少地形貼圖 index {index} ({fileName})；已查 {projectTextures} 與 {worldDirectory}。");
            else
                terrainSources.Add(source);
        }

        int[] objectTypes = project.Objects.Select(o => (int)o.Type).Distinct().Order().ToArray();
        if (objectTypes.Length == 0)
            return new(project, [.. errors], [.. warnings], [.. terrainSources], [], []);

        string objectDirectory = Path.Combine(dataDirectory, $"Object{project.WorldIndex}");
        if (!Directory.Exists(objectDirectory))
        {
            errors.Add($"專案引用 {objectTypes.Length} 種物件，但缺少 BMD 目錄：{objectDirectory}");
            return new(project, [.. errors], [.. warnings], [.. terrainSources], [], []);
        }

        var objectFilesByStem = Directory.EnumerateFiles(objectDirectory)
            .GroupBy(p => Path.GetFileNameWithoutExtension(p), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.OrdinalIgnoreCase);

        foreach (int type in objectTypes)
        {
            // Client.Main/Objects/MapTileObject.cs is the renderer truth:
            // source object Type N resolves to Object{N+1:D2}.bmd.
            string bmdPath = Path.Combine(objectDirectory, $"Object{type + 1:D2}.bmd");
            if (!File.Exists(bmdPath))
            {
                errors.Add($"物件 Type={type} 缺少 BMD：{bmdPath}");
                continue;
            }

            modelSources.Add(bmdPath);

            try
            {
                var bmd = await new BMDReader().Load(bmdPath);
                foreach (string texturePath in bmd.Meshes.Select(m => m.TexturePath).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    string stem = Path.GetFileNameWithoutExtension(texturePath);
                    if (!objectFilesByStem.TryGetValue(stem, out string[]? candidates))
                        errors.Add($"{bmdPath} 引用的材質貼圖 '{texturePath}' 不在 {objectDirectory}。");
                    else
                        modelTextureSources.Add(candidates[0]);
                }
            }
            catch (Exception ex)
            {
                errors.Add($"BMD 無法解析：{bmdPath}：{ex.Message}");
            }
        }

        return new(
            project,
            [.. errors.Distinct()],
            [.. warnings.Distinct()],
            [.. terrainSources.Distinct()],
            [.. modelSources.Distinct()],
            [.. modelTextureSources.Distinct()]);
    }

    private static string? FirstExisting(params string[] candidates)
        => candidates.FirstOrDefault(File.Exists);
}
