// MU 地圖編輯器。
//
//   MuMapEditor [--data <Data目錄>] [--world N] [--size 1600x1000] [--seconds N] [--screenshot <path>]
//
// --seconds / --screenshot 讓它能在終端機裡跑完就退出，用於自動化驗證。

using Client.Main;
using Client.MapEditor;
using MuAssets.Core;

const string DefaultDataDir = "/Users/airtan/Documents/GitHub/mmorpg-3d-research/assets/MU_Red_1_20_61/Data";

var parsed = ParseArgs(args);
(int width, int height) = ParseSize(parsed.GetValueOrDefault("size"));

// 這兩個必須在 MuGame 跑起來之前設好。Constants 的靜態建構子會先跑完預設值，
// 我們的指派蓋在它上面。
Constants.DataPath = parsed.GetValueOrDefault("data") ?? DefaultDataDir;
Constants.ENTRY_SCENE = typeof(MapEditorScene);

// 編輯器不需要遊戲的環境音與背景音樂，開著只會在切圖時亂放。
Constants.BACKGROUND_MUSIC = false;
Constants.SOUND_EFFECTS = false;

// 遊戲的除錯疊層（FPS / p95 / telemetry）會蓋在編輯器介面上。Debug 建置預設是開的。
Constants.SHOW_DEBUG_PANEL = false;

var options = new EditorOptions(
    Width: width,
    Height: height,
    RunSeconds: parsed.TryGetValue("seconds", out var s) && double.TryParse(s, out double seconds) ? seconds : 0d,
    ScreenshotPath: parsed.GetValueOrDefault("screenshot"),
    FullScreen: parsed.ContainsKey("fullscreen"));

EditorSession.Current.RunSelfTest = parsed.ContainsKey("selftest");
if (parsed.GetValueOrDefault("world") is string startupWorld && int.TryParse(startupWorld, out int startupWorldIndex))
    EditorSession.Current.StartupWorldIndex = startupWorldIndex;

EditorSession.Current.AuditObjects = parsed.ContainsKey("audit-objects");
EditorSession.Current.ExportOnStartPath = parsed.GetValueOrDefault("export-to");
EditorSession.Current.ExportOpenMuOnStartPath = parsed.GetValueOrDefault("export-openmu-to");

Console.WriteLine($"Data 目錄：{Constants.DataPath}");

// 分類完全不需要 GPU，所以這份報告在遊戲跑起來之前就能出。
// 這裡刻意不帶語意型別表（那需要 MuGame 才能建 world 類別），
// 測的正是「純自動分類」對無意義檔名的資料夾有多少覆蓋率。
if (parsed.ContainsKey("catalog-report"))
{
    AssetCatalogReport.Print(Constants.DataPath);
    return;
}

if (parsed.ContainsKey("catalog-unknown"))
{
    AssetCatalogReport.PrintUnknownTextures(Constants.DataPath);
    return;
}

// 怪物／NPC 目錄要讀 .cs 原始碼（編號與模型檔沒有公式），所以是一次性產生存成 JSON。
if (parsed.ContainsKey("build-npc-catalog"))
{
    string clientMainRoot = parsed.GetValueOrDefault("client-main")
        ?? "/Users/airtan/Documents/GitHub/mmorpg-3d-research/repos/muonline/Client.Main";
    string? openMuRoot = parsed.GetValueOrDefault("openmu")
        ?? "/Users/airtan/Documents/GitHub/mmorpg-3d-research/repos/openmu/src";

    var entries = NpcCatalogBuilder.Build(clientMainRoot, Directory.Exists(openMuRoot) ? openMuRoot : null);
    MonsterCatalog.Save(entries);

    int withModel = entries.Count(e => e.ModelPath is not null);
    int withServerName = entries.Count(e => e.ServerDesignation is not null);

    Console.WriteLine($"怪物／NPC 目錄：{entries.Length} 筆 -> {MonsterCatalog.DefaultPath}");
    Console.WriteLine($"  怪物 {entries.Count(e => e.Kind == NpcKind.Monster)}、NPC {entries.Count(e => e.Kind == NpcKind.Npc)}");
    Console.WriteLine($"  有模型路徑 {withModel}、對得上伺服器名稱 {withServerName}");

    foreach (var entry in entries.Take(8))
        Console.WriteLine($"  {entry.TypeId,4}  {entry.Name,-24} {entry.ModelPath ?? "－",-28} {entry.ServerDesignation ?? ""}");

    return;
}

using var game = new MapEditorGame(options);
game.Run();

static (int, int) ParseSize(string? value)
{
    if (value is null)
        return (1600, 1000);

    var parts = value.Split('x', 'X');
    return parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h)
        ? (w, h)
        : (1600, 1000);
}

static Dictionary<string, string?> ParseArgs(string[] args)
{
    var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    string? pending = null;

    foreach (var arg in args)
    {
        if (arg.StartsWith("--", StringComparison.Ordinal))
        {
            pending = arg[2..];
            options[pending] = null;
        }
        else if (pending is not null)
        {
            options[pending] = arg;
            pending = null;
        }
    }

    return options;
}
