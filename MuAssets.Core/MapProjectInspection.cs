using Client.Data.BMD;
using Client.Data.MAP;

namespace MuAssets.Core;

public sealed record MapProjectInspection(
    MapProject? Project,
    string[] Errors,
    string[] Warnings,
    string[] TerrainTextureSources,
    string[] ModelSources,
    string[] ModelTextureSources,
    // 來源檔名不一定等於渲染端要的檔名。Godot 中立包的地形貼圖是照**索引**命名的
    // （tiles/0.png = 索引 0 = TileGrass01.ozj），直接照檔名複製過去會對不上。
    // 這裡記「哪個檔案要變成什麼名字」，暫存區照這份計畫放。
    (string Source, string TargetFileName)[]? TerrainTexturePlan = null)
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
    /// <param name="allowMissingModels">
    /// 只看地形。物件模型缺了算警告不算錯誤 —— 給「開別的專案的 Godot 中立包看看長什麼樣」用。
    /// </param>
    /// <remarks>
    /// 為什麼需要這個模式：Godot 中立包（RealmForge 用的）帶的是 <c>models/&lt;type&gt;/*.gltf</c>，
    /// 而渲染端要 <c>Object{N}/Object{type+1:00}.bmd</c>，兩者之間沒有轉換器（只有 BMD → glTF）。
    ///
    /// <b>也不能借別張圖的 Object 目錄。</b> 實測 World212 與 World422 都是同一張 fod2 的窗格，
    /// 81 個共同 type 裡**沒有一個**指到同一個模型 —— type 編號是每次匯出各自編的。
    /// 借來用會把模型全部擺錯，而且畫面上「有東西」不會報錯，比開不起來更糟。
    /// </remarks>
    public static async Task<MapProjectInspection> InspectAsync(
        string projectDirectory,
        string? dataDirectory,
        bool requireRendererDependencies,
        bool allowMissingModels = false)
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

        // Godot 中立包（tools/mu godot-export 的產物，RealmForge 用的就是這個）
        // 把地形貼圖放在 tiles/<索引>.png —— 檔名是索引不是原檔名。
        string projectTiles = Path.Combine(projectDirectory, "tiles");
        var texturePlan = new List<(string Source, string TargetFileName)>();
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

            if (source is not null)
            {
                terrainSources.Add(source);
                texturePlan.Add((source, fileName));
                continue;
            }

            // 退到 Godot 中立包的 tiles/<索引>.png。找到的話要改名成渲染端認得的檔名，
            // 所以走 texturePlan 而不是直接照檔名複製。
            string tile = Path.Combine(projectTiles, $"{index}.png");
            if (File.Exists(tile))
            {
                terrainSources.Add(tile);
                texturePlan.Add((tile, fileName));
                continue;
            }

            errors.Add(
                $"缺少地形貼圖 index {index} ({fileName})；已查 {projectTextures}、{worldDirectory} 與 {projectTiles}/{index}.png。");
        }

        int[] objectTypes = project.Objects.Select(o => (int)o.Type).Distinct().Order().ToArray();

        if (allowMissingModels && objectTypes.Length > 0)
        {
            warnings.Add(
                $"只看地形：略過 {objectTypes.Length} 種物件模型。" +
                "Godot 中立包沒有 BMD，而 glTF 沒有回轉器，所以物件不會出現在畫面上。");
            return new(project, [.. errors], [.. warnings], [.. terrainSources], [], [], [.. texturePlan]);
        }

        if (objectTypes.Length == 0)
            return new(project, [.. errors], [.. warnings], [.. terrainSources], [], [], [.. texturePlan]);

        string objectDirectory = Path.Combine(dataDirectory, $"Object{project.WorldIndex}");
        if (!Directory.Exists(objectDirectory))
        {
            errors.Add($"專案引用 {objectTypes.Length} 種物件，但缺少 BMD 目錄：{objectDirectory}");
            return new(project, [.. errors], [.. warnings], [.. terrainSources], [], [], [.. texturePlan]);
        }

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
                    string? material = FindRendererTexture(objectDirectory, texturePath);
                    if (material is null)
                        errors.Add($"{bmdPath} 引用的材質貼圖 '{texturePath}' 不在 {objectDirectory}。");
                    else
                        modelTextureSources.Add(material);
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
            [.. modelTextureSources.Distinct()],
            [.. texturePlan.DistinctBy(t => t.TargetFileName)]);
    }

    private static string? FirstExisting(params string[] candidates)
        => candidates.FirstOrDefault(File.Exists);

    /// <summary>
    /// Mirrors Client.Main TextureLoader: BMD names normally use source extensions
    /// (.tga/.jpg/.png/.dds), while Data stores the encoded counterpart. The renderer
    /// also checks a texture/ child directory and resolves file names case-insensitively.
    /// </summary>
    private static string? FindRendererTexture(string directory, string texturePath)
    {
        string? encodedExtension = Path.GetExtension(texturePath).ToLowerInvariant() switch
        {
            ".tga" or ".ozt" => ".ozt",
            ".jpg" or ".ozj" => ".ozj",
            ".png" or ".ozp" => ".ozp",
            ".dds" or ".ozd" => ".ozd",
            _ => null,
        };

        if (encodedExtension is null)
            return null;

        string fileName = Path.GetFileNameWithoutExtension(texturePath) + encodedExtension;
        return ResolveCaseInsensitive(Path.Combine(directory, fileName))
               ?? ResolveCaseInsensitive(Path.Combine(directory, "texture", fileName));
    }

    private static string? ResolveCaseInsensitive(string path)
    {
        if (File.Exists(path))
            return path;

        string? directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            return null;

        string fileName = Path.GetFileName(path);
        return Directory.EnumerateFiles(directory)
            .FirstOrDefault(p => string.Equals(Path.GetFileName(p), fileName, StringComparison.OrdinalIgnoreCase));
    }
}
