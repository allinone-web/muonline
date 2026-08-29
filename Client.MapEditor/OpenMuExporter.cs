using System.Text;

namespace Client.MapEditor;

public sealed record OpenMuExportResult(bool Success, string[] Files, string? Error);

/// <summary>
/// 把地圖的伺服器端資料匯出給 OpenMU。
/// </summary>
/// <remarks>
/// 產出兩個東西：
/// <list type="number">
/// <item><c>Terrain{N}.att</c> —— 未加密的 3 byte 標頭 + 1 byte/格，
///       放進 <c>src/Persistence/Initialization/Resources/</c> 當內嵌資源</item>
/// <item><c>{MapName}.cs</c> —— 繼承 <c>BaseMapInitializer</c> 的地圖初始化器，
///       裡面是 <c>CreateMonsterSpawns()</c> 的生怪清單</item>
/// </list>
///
/// 產生的是**原始碼**而不是直接寫資料庫：OpenMU 的設定是用程式碼初始化的，
/// 產出原始碼才進得了版本控制、才跟得上它自己的升級流程。
/// </remarks>
public static class OpenMuExporter
{
    public static async Task<OpenMuExportResult> ExportAsync(
        MapDocument document,
        IEnumerable<SpawnArea> spawns,
        string mapName,
        int openMuMapNumber,
        string outputDirectory)
    {
        var files = new List<string>();

        try
        {
            Directory.CreateDirectory(outputDirectory);

            // 1) 伺服器讀的地形資料。OpenMU 的 GameMapTerrain 讀 AsSpan(3)，
            //    每格 1 byte，值 0 或 1 為可走、1 為安全區。
            string attPath = Path.Combine(outputDirectory, $"Terrain{openMuMapNumber + 1}.att");
            await File.WriteAllBytesAsync(attPath, MapExporter.BuildServerTerrainData(document));
            files.Add(Path.GetFileName(attPath));

            // 2) 地圖初始化器
            string sourcePath = Path.Combine(outputDirectory, $"{Sanitize(mapName)}.cs");
            await File.WriteAllTextAsync(sourcePath, BuildInitializer(spawns, mapName, openMuMapNumber), Encoding.UTF8);
            files.Add(Path.GetFileName(sourcePath));

            return new OpenMuExportResult(true, [.. files], null);
        }
        catch (Exception ex)
        {
            return new OpenMuExportResult(false, [.. files], ex.Message);
        }
    }

    private static string BuildInitializer(IEnumerable<SpawnArea> spawns, string mapName, int mapNumber)
    {
        string className = Sanitize(mapName);
        var builder = new StringBuilder();

        builder.AppendLine("// <copyright file=\"" + className + ".cs\" company=\"MUnique\">");
        builder.AppendLine("// Licensed under the MIT License. See LICENSE file in the project root for full license information.");
        builder.AppendLine("// </copyright>");
        builder.AppendLine();
        builder.AppendLine("// 由 MU 地圖編輯器產生。手動修改會在下次匯出時被覆蓋。");
        builder.AppendLine();
        builder.AppendLine("namespace MUnique.OpenMU.Persistence.Initialization.VersionSeasonSix.Maps;");
        builder.AppendLine();
        builder.AppendLine("using MUnique.OpenMU.DataModel.Configuration;");
        builder.AppendLine("using MUnique.OpenMU.GameLogic;");
        builder.AppendLine();
        builder.AppendLine("/// <summary>");
        builder.AppendLine($"/// The initialization for the {mapName} map.");
        builder.AppendLine("/// </summary>");
        builder.AppendLine($"internal class {className} : BaseMapInitializer");
        builder.AppendLine("{");
        builder.AppendLine($"    /// <summary>The map number, as used by the client (client world index is {mapNumber + 1}).</summary>");
        builder.AppendLine($"    internal const byte Number = {mapNumber};");
        builder.AppendLine();
        builder.AppendLine($"    /// <summary>The name of the map.</summary>");
        builder.AppendLine($"    internal const string Name = \"{mapName}\";");
        builder.AppendLine();
        builder.AppendLine($"    /// <summary>Initializes a new instance of the <see cref=\"{className}\"/> class.</summary>");
        builder.AppendLine("    /// <param name=\"context\">The context.</param>");
        builder.AppendLine("    /// <param name=\"gameConfiguration\">The game configuration.</param>");
        builder.AppendLine($"    public {className}(IContext context, GameConfiguration gameConfiguration)");
        builder.AppendLine("        : base(context, gameConfiguration)");
        builder.AppendLine("    {");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    /// <inheritdoc/>");
        builder.AppendLine("    protected override byte MapNumber => Number;");
        builder.AppendLine();
        builder.AppendLine("    /// <inheritdoc/>");
        builder.AppendLine("    protected override string MapName => Name;");
        builder.AppendLine();
        builder.AppendLine("    /// <inheritdoc/>");
        builder.AppendLine("    protected override IEnumerable<MonsterSpawnArea> CreateMonsterSpawns()");
        builder.AppendLine("    {");

        short number = 0;
        bool any = false;

        foreach (var spawn in spawns)
        {
            any = true;
            string comment = string.IsNullOrWhiteSpace(spawn.Name) ? string.Empty : $" // {spawn.Name}";

            // CreateMonsterSpawn 的參數順序是 x1, x2, y1, y2 —— 不是 x1, y1, x2, y2。
            builder.AppendLine(
                $"        yield return this.CreateMonsterSpawn({number}, this.NpcDictionary[{spawn.TypeId}], " +
                $"{spawn.X1}, {spawn.X2}, {spawn.Y1}, {spawn.Y2}, {spawn.Quantity}, " +
                $"Direction.{spawn.Direction}, SpawnTrigger.{spawn.Trigger});{comment}");

            number++;
        }

        if (!any)
            builder.AppendLine("        yield break;");

        builder.AppendLine("    }");
        builder.AppendLine("}");

        return builder.ToString();
    }

    /// <summary>把地圖名稱變成合法的 C# 類別名。</summary>
    private static string Sanitize(string name)
    {
        var builder = new StringBuilder();

        foreach (char c in name)
        {
            if (char.IsLetterOrDigit(c))
                builder.Append(c);
        }

        string result = builder.ToString();

        if (result.Length == 0)
            return "GeneratedMap";

        return char.IsDigit(result[0]) ? "Map" + result : result;
    }
}
