using Client.Data.MAP;

namespace MuAssets.Core;

public sealed record ScaffoldResult(
    bool Success,
    string WorldDirectory,
    string[] Files,
    string[] CopiedTextures,
    string? WorldClassPath,
    string[] Warnings,
    string? Error);

/// <summary>
/// 從零建一張新地圖需要的所有東西。
/// </summary>
/// <remarks>
/// 「新建地圖」不只是寫五個地形檔。客戶端要能載入一張圖，四樣缺一不可：
///
/// <list type="number">
///   <item><c>Data/World{N}/</c> 的五個地形檔 —— 地形本身</item>
///   <item>同一個目錄裡的**貼圖檔** —— 貼圖是按檔名找的，
///         每張圖各有一份，沒有共用目錄。少了它們地形會是一片白</item>
///   <item><c>Data/Object{N}/</c> —— 就算是空的也要有，
///         物件的路徑規則是 <c>Object{world}/Object{type+1:00}.bmd</c></item>
///   <item><c>Client.Main/Worlds/World{N}.cs</c> —— 帶 <c>[WorldInfo]</c> 的類別，
///         客戶端靠反射掃這個屬性建立「地圖編號 → 類別」的對照表</item>
/// </list>
///
/// 第 2 項最容易漏。貼圖從一張既有的圖複製過來（預設 World1），
/// 之後要換風格就是替換這些檔案 —— 那正是「用替換貼圖來改進地圖外觀」的做法。
/// </remarks>
public static class NewMapScaffold
{
    /// <summary>沒有指定來源時，從這張圖借貼圖。World1（勒瑞西亞）的地貌最通用。</summary>
    public const int DefaultDonorWorld = 1;

    public static async Task<ScaffoldResult> CreateAsync(
        string dataDirectory,
        int worldIndex,
        string mapName,
        int donorWorldIndex = DefaultDonorWorld,
        byte groundTile = 0,
        string? worldClassDirectory = null,
        bool overwrite = false)
    {
        var warnings = new List<string>();
        string worldDirectory = Path.Combine(dataDirectory, $"World{worldIndex}");

        try
        {
            if (Directory.Exists(worldDirectory) && !overwrite)
            {
                return new ScaffoldResult(false, worldDirectory, [], [], null, [],
                    $"{worldDirectory} 已經存在。要覆蓋請明確指定 overwrite。");
            }

            var document = MapDocument.CreateBlank(worldIndex, groundTile);

            var export = await MapExporter.ExportAsync(document, worldDirectory, worldIndex);
            if (!export.Success)
                return new ScaffoldResult(false, worldDirectory, export.Files, [], null, [.. warnings], export.Error);

            var textures = CopyTextures(dataDirectory, donorWorldIndex, worldDirectory, warnings);

            // 空的 Object 目錄也要建：物件的路徑規則是 Object{world}/…，
            // 目錄不存在時載入端會整組跳過，而不是「這張圖沒有物件」。
            Directory.CreateDirectory(Path.Combine(dataDirectory, $"Object{worldIndex}"));

            string? classPath = null;
            if (worldClassDirectory is not null)
            {
                classPath = Path.Combine(worldClassDirectory, $"World{worldIndex}.cs");

                if (File.Exists(classPath) && !overwrite)
                    warnings.Add($"{classPath} 已經存在，沒有覆蓋");
                else
                    await File.WriteAllTextAsync(classPath, BuildWorldClass(worldIndex, mapName));
            }

            return new ScaffoldResult(true, worldDirectory, export.Files, textures, classPath, [.. warnings], null);
        }
        catch (Exception ex)
        {
            return new ScaffoldResult(false, worldDirectory, [], [], null, [.. warnings], ex.Message);
        }
    }

    /// <summary>
    /// 把來源地圖的地形貼圖複製過來。只複製對應表裡列到的檔名 ——
    /// 來源目錄裡還有模型、音效之類的東西，那些跟地形無關。
    /// </summary>
    private static string[] CopyTextures(
        string dataDirectory, int donorWorldIndex, string worldDirectory, List<string> warnings)
    {
        string donor = Path.Combine(dataDirectory, $"World{donorWorldIndex}");

        if (!Directory.Exists(donor))
        {
            warnings.Add($"找不到貼圖來源 {donor}，新地圖會是一片白");
            return [];
        }

        var copied = new List<string>();

        foreach (string fileName in TerrainTextureMapping.BuildIndexMap().Values.Distinct())
        {
            // 對應表寫的是 .ozj，但同一張貼圖也可能以 .ozt / .ozd / .ozp 存在。
            // 照客戶端的順序找，找到哪個複製哪個。
            foreach (string candidate in Candidates(fileName))
            {
                string source = Path.Combine(donor, candidate);
                if (!File.Exists(source))
                    continue;

                File.Copy(source, Path.Combine(worldDirectory, candidate), overwrite: true);
                copied.Add(candidate);
                break;
            }
        }

        if (copied.Count == 0)
            warnings.Add($"{donor} 裡一個地形貼圖都沒找到，新地圖會是一片白");

        return [.. copied];
    }

    private static IEnumerable<string> Candidates(string fileName)
    {
        yield return fileName;

        string stem = Path.GetFileNameWithoutExtension(fileName);
        foreach (string extension in new[] { ".ozj", ".ozt", ".ozd", ".ozp" })
        {
            if (!fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                yield return stem + extension;
        }
    }

    /// <summary>
    /// 產生 <c>Client.Main/Worlds/World{N}.cs</c>。
    /// </summary>
    /// <remarks>
    /// <c>[WorldInfo]</c> 的第一個參數是 <b>OpenMU 的地圖編號</b>，
    /// 建構子的 <c>worldIndex</c> 是 <b>客戶端的編號</b>，兩者差一。
    /// 這個 off-by-one 是這個專案裡最常踩的坑，所以直接寫進產生的註解裡。
    /// </remarks>
    public static string BuildWorldClass(int worldIndex, string mapName)
    {
        string className = $"World{worldIndex}";
        int openMuNumber = worldIndex - 1;

        return $$"""
            using Client.Main.Controls;
            using Client.Main.Core.Utilities;

            namespace Client.Main.Worlds
            {
                /// <summary>
                /// {{mapName}}（地圖編輯器產生的新地圖）。
                /// </summary>
                /// <remarks>
                /// [WorldInfo] 的編號是 OpenMU 的地圖編號，建構子的 worldIndex 是客戶端的編號，
                /// 兩者差一（客戶端 = OpenMU + 1）。這裡是 OpenMU {{openMuNumber}} / 客戶端 {{worldIndex}}。
                ///
                /// 要讓地圖上的物件有語意行為（樹會搖、火會亮），
                /// 覆寫 CreateMapTileObjects() 把 MapTileObjects[型別編號] 指到對應的類別，
                /// 可以參考 LorenciaWorld。
                /// </remarks>
                [WorldInfo({{openMuNumber}}, "{{mapName}}")]
                public class {{className}} : WalkableWorldControl
                {
                    public {{className}}() : base(worldIndex: {{worldIndex}})
                    {
                        Name = "{{mapName}}";
                    }
                }
            }

            """;
    }
}
