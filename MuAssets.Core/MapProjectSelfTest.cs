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

        bool passed = results.Count == 5 && results.All(r => r.Passed);
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
}
