using MuAssets.Core;

namespace Client.MapEditor;

/// <summary>
/// Read-only overlay which derives legacy renderer files from an external project
/// without modifying the project or the user's MU Data directory.
/// </summary>
public sealed class ExternalProjectWorkspace : IDisposable
{
    private ExternalProjectWorkspace(string root, string dataDirectory, string projectDirectory, int worldIndex)
        => (Root, DataDirectory, ProjectDirectory, WorldIndex) = (root, dataDirectory, projectDirectory, worldIndex);

    public string Root { get; }
    public string DataDirectory { get; }
    public string ProjectDirectory { get; }
    public int WorldIndex { get; }

    public static async Task<ExternalProjectWorkspace> CreateAsync(
        string projectDirectory,
        string sourceDataDirectory,
        bool terrainOnly = false)
    {
        projectDirectory = Path.GetFullPath(projectDirectory);
        sourceDataDirectory = Path.GetFullPath(sourceDataDirectory);
        var inspection = await MapProjectInspector.InspectAsync(
            projectDirectory, sourceDataDirectory,
            requireRendererDependencies: true,
            allowMissingModels: terrainOnly);

        if (!inspection.IsValid)
            throw new InvalidDataException("外部專案依賴驗證失敗：\n  - " + string.Join("\n  - ", inspection.Errors));

        var project = inspection.Project!;
        LegacyMapCodec.Validate(project);

        string root = Path.Combine(Path.GetTempPath(), $"mu-map-editor-project-{Environment.ProcessId}-{Guid.NewGuid():N}");
        string overlayData = Path.Combine(root, "Data");
        string stagedWorld = Path.Combine(overlayData, $"World{project.WorldIndex}");
        Directory.CreateDirectory(overlayData);

        try
        {
            foreach (string source in Directory.EnumerateFileSystemEntries(sourceDataDirectory))
            {
                string name = Path.GetFileName(source);
                // The overlay catalog must contain exactly the external project world.
                // Keeping other WorldN directories would let the UI select a renderer world
                // while still binding the external authoring document, producing a false view.
                if (name.StartsWith("World", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(name[5..], out _))
                    continue;

                string target = Path.Combine(overlayData, name);
                if (Directory.Exists(source))
                    Directory.CreateSymbolicLink(target, source);
                else
                    File.CreateSymbolicLink(target, source);
            }

            var document = await MapProjectIo.LoadAsync(projectDirectory);
            var exported = await MapExporter.ExportAsync(document, stagedWorld, project.WorldIndex);
            if (!exported.Success)
                throw new InvalidDataException(exported.Error ?? "legacy renderer 衍生檔輸出失敗");

            StageTerrainTextures(inspection, stagedWorld, sourceDataDirectory);

            return new ExternalProjectWorkspace(root, overlayData, projectDirectory, project.WorldIndex);
        }
        catch
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
            throw;
        }
    }

    /// <summary>
    /// 把地形貼圖放進暫存的 World 目錄。
    /// </summary>
    /// <remarks>
    /// 不能單純照原檔名複製：Godot 中立包的地形貼圖叫 <c>tiles/&lt;索引&gt;.png</c>，
    /// 索引不是檔名，直接複製會變成 <c>0.png</c>，渲染端找 <c>TileGrass01.ozj</c> 找不到。
    /// 所以走 inspector 給的「來源 → 該叫什麼」計畫，而且 PNG 要轉成 OZJ/OZT ——
    /// 渲染端的 TextureLoader 認的是 MU 的容器格式。
    /// </remarks>
    private static void StageTerrainTextures(
        MapProjectInspection inspection,
        string stagedWorld,
        string sourceDataDirectory)
    {
        var plan = inspection.TerrainTexturePlan;

        if (plan is null || plan.Length == 0)
        {
            // 沒有計畫就是舊路徑（檔名本來就對），照原樣複製。
            foreach (string textureSource in inspection.TerrainTextureSources)
                File.Copy(textureSource, Path.Combine(stagedWorld, Path.GetFileName(textureSource)), overwrite: false);
            return;
        }

        foreach (var (source, targetFileName) in plan)
        {
            string target = Path.Combine(stagedWorld, targetFileName);

            if (string.Equals(Path.GetExtension(source), Path.GetExtension(targetFileName),
                    StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(source, target, overwrite: true);
                continue;
            }

            // TextureWriter 沿用原檔的標頭（.ozj 的 byte 17 是 top-down 旗標，
            // .ozt 有一個用途未明的位元組），而 Godot 包沒有原檔。
            // 從 Data 借一個同副檔名的真標頭 —— 不要自己造一個，那會在
            // 上下翻轉這種地方靜默出錯。
            byte[] donor = BorrowHeader(sourceDataDirectory, Path.GetExtension(targetFileName));
            using var image = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(source);
            File.WriteAllBytes(target, TextureWriter.Build(image, target, donor));
        }
    }

    /// <summary>從 Data 裡隨便找一個同副檔名的貼圖，借它的標頭。找不到就講清楚。</summary>
    private static byte[] BorrowHeader(string sourceDataDirectory, string extension)
    {
        foreach (string world in Directory.EnumerateDirectories(sourceDataDirectory, "World*").Order())
        {
            foreach (string file in Directory.EnumerateFiles(world, "*" + extension))
            {
                var bytes = File.ReadAllBytes(file);
                if (bytes.Length >= 64)
                    return bytes;
            }
        }

        throw new InvalidDataException(
            $"要把 Godot 中立包的 PNG 轉成 {extension}，但 {sourceDataDirectory} 裡找不到任何 {extension} 可以借標頭。");
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
            Directory.Delete(Root, recursive: true);
    }
}
