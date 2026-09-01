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

    public static async Task<ExternalProjectWorkspace> CreateAsync(string projectDirectory, string sourceDataDirectory)
    {
        projectDirectory = Path.GetFullPath(projectDirectory);
        sourceDataDirectory = Path.GetFullPath(sourceDataDirectory);
        var inspection = await MapProjectInspector.InspectAsync(projectDirectory, sourceDataDirectory, requireRendererDependencies: true);

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

            foreach (string textureSource in inspection.TerrainTextureSources)
                File.Copy(textureSource, Path.Combine(stagedWorld, Path.GetFileName(textureSource)), overwrite: false);

            return new ExternalProjectWorkspace(root, overlayData, projectDirectory, project.WorldIndex);
        }
        catch
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
            throw;
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
            Directory.Delete(Root, recursive: true);
    }
}
