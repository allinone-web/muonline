using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Client.MapEditor;

public enum NpcKind
{
    Monster,
    Npc,
}

/// <summary>怪物／NPC 調色盤的一筆。</summary>
public sealed record NpcEntry(
    ushort TypeId,
    string Name,
    string ClassName,
    NpcKind Kind,
    string? ModelPath,
    string? ServerDesignation);

/// <summary>
/// 怪物與 NPC 的目錄：編號、名稱、模型檔。
/// </summary>
/// <remarks>
/// 三個來源湊起來：
/// <list type="number">
/// <item>客戶端的 <c>[NpcInfo(147, "Aegis")]</c> 屬性 —— 反射就拿得到編號與名稱，共 253 個</item>
/// <item>各類別的 <c>Load()</c> 裡寫死的模型路徑 —— <b>編號與模型檔沒有公式</b>
///       （147→Monster67、31→Monster25、74→Monster53），只能從原始碼抓</item>
/// <item>OpenMU 原始碼的 <c>monster.Number = N; monster.Designation = "…";</c> —— 對照伺服器那邊的名稱</item>
/// </list>
///
/// 因為 (2) 要讀 <c>.cs</c> 原始碼，目錄是**一次性產生**存成 JSON，
/// 之後編輯器只讀 JSON。用 <c>--build-npc-catalog</c> 重新產生。
/// </remarks>
public sealed class MonsterCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>抓 <c>BMDLoader.Instance.Prepare($"Monster/Monster67.bmd")</c> 這種寫死的路徑。</summary>
    private static readonly Regex ModelPathPattern = new(
        @"Prepare\(\s*\$?""(?<path>(?:Monster|NPC|Npc)/[^""]+\.bmd)""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>抓 OpenMU 的 <c>monster.Number = 334;</c> 後面跟著 <c>monster.Designation = "…";</c>。</summary>
    private static readonly Regex DesignationPattern = new(
        @"\.Number\s*=\s*(?<number>\d+)\s*;[\s\S]{0,200}?\.Designation\s*=\s*""(?<name>[^""]+)""",
        RegexOptions.Compiled);

    public NpcEntry[] Entries { get; private set; } = [];

    public static string DefaultPath => Path.Combine(EditorSettings.ConfigDirectory, "npc-catalog.json");

    public static MonsterCatalog Load(string? path = null)
    {
        var catalog = new MonsterCatalog();
        path ??= DefaultPath;

        if (!File.Exists(path))
            return catalog;

        try
        {
            catalog.Entries = JsonSerializer.Deserialize<NpcEntry[]>(File.ReadAllText(path), JsonOptions) ?? [];
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MonsterCatalog] 讀取 {path} 失敗：{ex.Message}");
        }

        return catalog;
    }

    /// <summary>
    /// 掃原始碼重新產生目錄。
    /// </summary>
    /// <param name="clientMainRoot">`Client.Main` 的目錄。</param>
    /// <param name="openMuRoot">OpenMU 的 `src` 目錄，null 就跳過伺服器名稱比對。</param>
    public static NpcEntry[] Build(string clientMainRoot, string? openMuRoot)
    {
        var designations = openMuRoot is null
            ? []
            : ReadServerDesignations(openMuRoot);

        var entries = new List<NpcEntry>();

        // 反射拿編號與名稱：這兩個是屬性參數，不必碰原始碼。
        foreach (var type in typeof(Client.Main.MuGame).Assembly.GetTypes())
        {
            var info = type.GetCustomAttribute<NpcInfoAttribute>();
            if (info is null)
                continue;

            var kind = type.Namespace?.Contains(".NPCS", StringComparison.OrdinalIgnoreCase) == true
                ? NpcKind.Npc
                : NpcKind.Monster;

            entries.Add(new NpcEntry(
                info.TypeId,
                info.DisplayName,
                type.Name,
                kind,
                FindModelPath(clientMainRoot, type.Name),
                designations.GetValueOrDefault(info.TypeId)));
        }

        return entries.OrderBy(e => e.TypeId).ToArray();
    }

    public static void Save(NpcEntry[] entries, string? path = null)
    {
        path ??= DefaultPath;

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(path, JsonSerializer.Serialize(entries, JsonOptions));
    }

    /// <summary>
    /// 從類別的 <c>.cs</c> 抓模型路徑。找不到檔案或抓不到路徑都回 null ——
    /// 沒有縮圖不影響擺怪，只是清單上少一張圖。
    /// </summary>
    private static string? FindModelPath(string clientMainRoot, string className)
    {
        string objectsRoot = Path.Combine(clientMainRoot, "Objects");
        if (!Directory.Exists(objectsRoot))
            return null;

        string? file = Directory
            .EnumerateFiles(objectsRoot, $"{className}.cs", SearchOption.AllDirectories)
            .FirstOrDefault();

        if (file is null)
            return null;

        var match = ModelPathPattern.Match(File.ReadAllText(file));
        return match.Success ? match.Groups["path"].Value : null;
    }

    private static Dictionary<ushort, string> ReadServerDesignations(string openMuRoot)
    {
        var result = new Dictionary<ushort, string>();

        string initializationRoot = Path.Combine(openMuRoot, "Persistence", "Initialization");
        if (!Directory.Exists(initializationRoot))
            return result;

        foreach (var file in Directory.EnumerateFiles(initializationRoot, "*.cs", SearchOption.AllDirectories))
        {
            string text;
            try
            {
                text = File.ReadAllText(file);
            }
            catch
            {
                continue;
            }

            foreach (Match match in DesignationPattern.Matches(text))
            {
                if (ushort.TryParse(match.Groups["number"].Value, out ushort number))
                    result.TryAdd(number, match.Groups["name"].Value);
            }
        }

        return result;
    }
}
