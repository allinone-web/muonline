using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MuAssets.Core;

/// <summary>Engine-neutral, no-GUI regression tests for the authoring/legacy boundary.</summary>
public static class MapProjectSelfTest
{
    public static bool Run()
    {
        string root = Path.Combine(Path.GetTempPath(), $"map-project-selftest-{Environment.ProcessId}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var results = new List<(string Name, bool Passed, string Detail)>();

        try
        {
            var highWorld = MapDocument.CreateBlank(300);
            highWorld.MapNumber = 299;
            highWorld.AttIndex = 299;
            MapProjectIo.SaveAsync(highWorld, root).GetAwaiter().GetResult();

            var loaded = MapProjectIo.LoadAsync(root).GetAwaiter().GetResult();
            results.Add(("World>255 schema", loaded.WorldIndex == 300 && loaded.MapNumber == 299 && loaded.AttIndex == 299,
                $"World{loaded.WorldIndex}, map={loaded.MapNumber}, att={loaded.AttIndex}"));

            bool boundaryFailed = Throws<InvalidDataException>(() => LegacyMapCodec.Validate(loaded), "0..255");
            results.Add(("legacy byte boundary", boundaryFailed, "超界輸出明確失敗"));

            string alpha = Path.Combine(root, "alpha.png");
            File.Delete(alpha);
            results.Add(("missing PNG", Throws<FileNotFoundException>(() => MapProjectIo.LoadAsync(root).GetAwaiter().GetResult(), "alpha.png"),
                "缺 alpha.png 不得補預設資料"));

            MapProjectIo.SaveAsync(highWorld, root).GetAwaiter().GetResult();
            using (var wrong = new Image<L8>(64, 64))
                wrong.SaveAsPng(Path.Combine(root, "height.png"));
            results.Add(("wrong PNG dimensions", Throws<InvalidDataException>(() => MapProjectIo.LoadAsync(root).GetAwaiter().GetResult(), "256x256"),
                "64x64 height.png 被拒絕"));

            MapProjectIo.SaveAsync(highWorld, root).GetAwaiter().GetResult();
            highWorld.Layer1[0] = 250;
            MapProjectIo.SaveAsync(highWorld, root).GetAwaiter().GetResult();
            results.Add(("illegal reference", Throws<InvalidDataException>(() => MapProjectIo.LoadAsync(root).GetAwaiter().GetResult(), "索引 250"),
                "未映射貼圖索引不得忽略"));

            string objectProject = Path.Combine(root, "object-project");
            var objectDocument = MapDocument.CreateBlank(300);
            objectDocument.MapNumber = 299;
            objectDocument.AttIndex = 299;
            objectDocument.Objects.Add(new MapObjectInstance { Type = -1, Scale = 1f });
            MapProjectIo.SaveAsync(objectDocument, objectProject).GetAwaiter().GetResult();
            results.Add(("illegal object reference", Throws<InvalidDataException>(() => MapProjectIo.LoadAsync(objectProject).GetAwaiter().GetResult(), "Type=-1"),
                "負數物件 Type 不得進 renderer"));

            objectDocument.Objects[0].Type = 0;
            MapProjectIo.SaveAsync(objectDocument, objectProject).GetAwaiter().GetResult();
            string missingData = Path.Combine(root, "missing-data");
            var inspection = MapProjectInspector.InspectAsync(objectProject, missingData, requireRendererDependencies: true).GetAwaiter().GetResult();
            results.Add(("missing Data", HasError(inspection, "找不到 MU Data"), "沒有 Data 根目錄不得開啟"));

            string data = Path.Combine(root, "Data");
            Directory.CreateDirectory(Path.Combine(data, "World300"));
            Directory.CreateDirectory(Path.Combine(data, "Object300"));
            inspection = MapProjectInspector.InspectAsync(objectProject, data, requireRendererDependencies: true).GetAwaiter().GetResult();
            results.Add(("missing terrain texture", HasError(inspection, "TileGrass01"), "不借 donor 或預設貼圖"));

            string textures = Path.Combine(objectProject, "textures");
            Directory.CreateDirectory(textures);
            File.WriteAllBytes(Path.Combine(textures, "TileGrass01.ozj"), []);
            inspection = MapProjectInspector.InspectAsync(objectProject, data, requireRendererDependencies: true).GetAwaiter().GetResult();
            results.Add(("missing BMD", HasError(inspection, "Object01.bmd"), "物件 Type=0 必須精確對應 Object01.bmd"));

            string objectDirectory = Path.Combine(data, "Object300");
            WriteMinimalBmd(Path.Combine(objectDirectory, "Object01.bmd"), "missing-material.tga");
            File.WriteAllBytes(Path.Combine(objectDirectory, "missing-material.bmd"), []);
            inspection = MapProjectInspector.InspectAsync(objectProject, data, requireRendererDependencies: true).GetAwaiter().GetResult();
            results.Add(("missing BMD material", HasError(inspection, "missing-material.tga"), "同名非貼圖檔不得冒充材質"));

            string nestedTextures = Path.Combine(objectDirectory, "texture");
            Directory.CreateDirectory(nestedTextures);
            File.WriteAllBytes(Path.Combine(nestedTextures, "MISSING-MATERIAL.OZT"), []);
            inspection = MapProjectInspector.InspectAsync(objectProject, data, requireRendererDependencies: true).GetAwaiter().GetResult();
            results.Add(("renderer dependencies", inspection.IsValid
                && inspection.TerrainTextureSources.Length == 1
                && inspection.ModelSources.Length == 1
                && inspection.ModelTextureSources.Length == 1,
                "renderer 同款副檔名、texture/ 與大小寫解析"));
        }
        catch (Exception ex)
        {
            results.Add(("selftest harness", false, ex.ToString()));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }

        Console.WriteLine("=== map authoring project selftest（無 GUI）===");
        foreach (var result in results)
            Console.WriteLine($"[{(result.Passed ? " ok " : "FAIL")}] {result.Name,-24} {result.Detail}");

        bool passed = results.Count == 11 && results.All(r => r.Passed);
        Console.WriteLine(passed ? "全部通過。" : "有項目失敗。");
        return passed;
    }

    private static bool Throws<T>(Action action, string messagePart) where T : Exception
    {
        try
        {
            action();
            return false;
        }
        catch (T ex)
        {
            return ex.ToString().Contains(messagePart, StringComparison.Ordinal);
        }
    }

    private static bool HasError(MapProjectInspection inspection, string messagePart)
        => !inspection.IsValid && inspection.Errors.Any(e => e.Contains(messagePart, StringComparison.Ordinal));

    /// <summary>Writes the smallest unencrypted BMD understood by Client.Data: one empty mesh with one material name.</summary>
    private static void WriteMinimalBmd(string path, string texturePath)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: false);
        writer.Write(Encoding.ASCII.GetBytes("BMD"));
        writer.Write((byte)1);
        WriteFixedString(writer, "selftest", 32);
        writer.Write((ushort)1); // meshes
        writer.Write((ushort)0); // bones
        writer.Write((ushort)0); // actions
        writer.Write((short)0); // vertices
        writer.Write((short)0); // normals
        writer.Write((short)0); // texture coordinates
        writer.Write((short)0); // triangles
        writer.Write((short)0); // texture index
        WriteFixedString(writer, texturePath, 32);
    }

    private static void WriteFixedString(BinaryWriter writer, string value, int length)
    {
        byte[] target = new byte[length];
        byte[] source = Encoding.ASCII.GetBytes(value);
        Array.Copy(source, target, Math.Min(source.Length, target.Length - 1));
        writer.Write(target);
    }
}
