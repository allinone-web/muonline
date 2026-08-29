using System.Text.Json;
using System.Text.Json.Serialization;

namespace MuAssets.Core;

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
/// 目錄是**一次性產生**存成 JSON（產生的部分要讀客戶端原始碼，見編輯器的
/// <c>NpcCatalogBuilder</c>），之後只讀 JSON —— 所以這一半是純的。
/// </remarks>
public sealed class MonsterCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

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

    public static void Save(NpcEntry[] entries, string? path = null)
    {
        path ??= DefaultPath;

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(path, JsonSerializer.Serialize(entries, JsonOptions));
    }
}
