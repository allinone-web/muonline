// MU 地圖編輯器。
//
//   MuMapEditor [--data <Data目錄>] [--world N] [--tile X,Y] [--size 1600x1000] [--seconds N] [--screenshot <path>] [--grass] [--grass-density N] [--grass-planes N] [--grass-distance N] [--grass-dense N]
//   MuMapEditor --shots <契約.json> --shot <鏡位名> --screenshot <path>   拍黃金影像
//
// --seconds / --screenshot 讓它能在終端機裡跑完就退出，用於自動化驗證。

using Client.Main;
using Client.MapEditor;
using MuAssets.Core;

const string DefaultDataDir = "/Users/airtan/Documents/GitHub/mmorpg-3d-research/assets/MU_Red_1_20_61/Data";

if (args.Any(arg => arg is "--help" or "-h" or "--h"))
{
    PrintUsage();
    return;
}

Dictionary<string, string?> parsed;
try
{
    parsed = ParseArgs(args);
    ValidateModeArguments(parsed);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine($"參數錯誤：{ex.Message}");
    Console.Error.WriteLine("用 --help 查看用法；未啟動 GUI。");
    Environment.ExitCode = 2;
    return;
}

(int width, int height) = ParseSize(parsed.GetValueOrDefault("size"));
string sourceDataPath = parsed.GetValueOrDefault("data") ?? DefaultDataDir;

if (parsed.GetValueOrDefault("project-check") is string projectToCheck)
{
    projectToCheck = Path.GetFullPath(projectToCheck);
    var inspection = await MapProjectInspector.InspectAsync(
        projectToCheck, Path.GetFullPath(sourceDataPath),
        requireRendererDependencies: true,
        allowMissingModels: parsed.ContainsKey("terrain-only"));
    PrintInspection(inspection);

    bool canOpen = inspection.IsValid && inspection.IsLegacyCodecCompatible;
    if (canOpen)
    {
        try
        {
            using var checkWorkspace = await ExternalProjectWorkspace.CreateAsync(
                projectToCheck, sourceDataPath, terrainOnly: parsed.ContainsKey("terrain-only"));
            Console.WriteLine($"唯讀 overlay 建立與清理通過：World{checkWorkspace.WorldIndex}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"錯誤：唯讀 overlay 建立失敗：{ex.Message}");
            canOpen = false;
        }
    }

    Environment.ExitCode = canOpen ? 0 : 2;
    return;
}

// 這兩個必須在 MuGame 跑起來之前設好。Constants 的靜態建構子會先跑完預設值，
// 我們的指派蓋在它上面。
Constants.ENTRY_SCENE = typeof(MapEditorScene);

// 編輯器不需要遊戲的環境音與背景音樂，開著只會在切圖時亂放。
Constants.BACKGROUND_MUSIC = false;
Constants.SOUND_EFFECTS = false;

// 遊戲的除錯疊層（FPS / p95 / telemetry）會蓋在編輯器介面上。Debug 建置預設是開的。
Constants.SHOW_DEBUG_PANEL = false;

// 黃金影像：相機由契約檔決定，介面不畫，尺寸也由鏡位決定 ——
// 這三件事任何一件由別處決定，基準圖就會隨環境漂移。
GoldenShot? goldenShot = null;
if (parsed.GetValueOrDefault("shots") is string shotsPath)
{
    string? shotName = parsed.GetValueOrDefault("shot");
    goldenShot = GoldenShot.Load(shotsPath, shotName).First();

    EditorSession.Current.GoldenShot = goldenShot;
    EditorSession.Current.StartupWorldIndex = goldenShot.World;
    (width, height) = (goldenShot.Width, goldenShot.Height);

    Console.WriteLine($"鏡位：{goldenShot.Name}（World{goldenShot.World}、{width}×{height}）");
}

var options = new EditorOptions(
    Width: width,
    Height: height,
    RunSeconds: parsed.TryGetValue("seconds", out var s) && double.TryParse(s, out double seconds) ? seconds : 0d,
    ScreenshotPath: parsed.GetValueOrDefault("screenshot"),
    FullScreen: parsed.ContainsKey("fullscreen"));

EditorSession.Current.RunSelfTest = parsed.ContainsKey("selftest");
EditorSession.Current.ForceGrass = parsed.ContainsKey("grass");

if (parsed.GetValueOrDefault("grass-density") is string densityArg
    && int.TryParse(densityArg, out int density) && density >= 1)
{
    EditorSession.Current.GrassDensity = Math.Min(density, 16);
}

if (parsed.GetValueOrDefault("grass-planes") is string planesArg
    && int.TryParse(planesArg, out int planes) && planes >= 1)
{
    EditorSession.Current.GrassPlanes = Math.Min(planes, 4);
}

if (parsed.GetValueOrDefault("grass-distance") is string distArg
    && float.TryParse(distArg, out float grassDistance) && grassDistance >= 0f)
{
    EditorSession.Current.GrassDistance = grassDistance;
}

if (parsed.GetValueOrDefault("grass-dense") is string denseArg
    && float.TryParse(denseArg, out float grassDense) && grassDense >= 0f)
{
    EditorSession.Current.GrassDenseDistance = grassDense;
}

// --tile 139,84：開起來就站在那一格上，不用自己找。
if (parsed.GetValueOrDefault("tile") is string tileArg)
{
    var tileParts = tileArg.Split(',', 'x', 'X');
    if (tileParts.Length == 2
        && int.TryParse(tileParts[0].Trim(), out int tileX)
        && int.TryParse(tileParts[1].Trim(), out int tileY))
    {
        EditorSession.Current.StartupTile = (tileX, tileY);
    }
    else
    {
        Console.WriteLine($"--tile 看不懂「{tileArg}」，要 X,Y（例：139,84）。改看全圖。");
    }
}

// 鏡位自己帶世界編號，不讓 --world 蓋過去 —— 否則同一個鏡位在不同地圖上拍，
// 基準圖對不上而且看不出原因。
if (goldenShot is null
    && parsed.GetValueOrDefault("world") is string startupWorld
    && int.TryParse(startupWorld, out int startupWorldIndex))
{
    EditorSession.Current.StartupWorldIndex = startupWorldIndex;
}

EditorSession.Current.AuditObjects = parsed.ContainsKey("audit-objects");
EditorSession.Current.ExportOnStartPath = parsed.GetValueOrDefault("export-to");
EditorSession.Current.ExportOpenMuOnStartPath = parsed.GetValueOrDefault("export-openmu-to");

// 語意型別表（World 類別的 CreateMapTileObjects）—— 分類線索裡最準的一條。
// 不能在這裡直接導：它要實例化 WorldControl，而那要 MuGame 起來之後才行
// （在 CLI 階段呼叫實測 86 張圖全部 NullReferenceException）。
// 所以只記路徑，等遊戲初始化完再導，導完就結束。
EditorSession.Current.ExportSemanticTypesOnStartPath = parsed.GetValueOrDefault("export-semantic-types");

// 分類完全不需要 GPU，所以這份報告在遊戲跑起來之前就能出。
// 這裡刻意不帶語意型別表（那需要 MuGame 才能建 world 類別），
// 測的正是「純自動分類」對無意義檔名的資料夾有多少覆蓋率。
if (parsed.ContainsKey("catalog-report"))
{
    AssetCatalogReport.Print(sourceDataPath);
    return;
}

if (parsed.ContainsKey("catalog-precision"))
{
    AssetCatalogReport.PrintShapePrecision(sourceDataPath);
    return;
}

if (parsed.ContainsKey("catalog-geometry"))
{
    AssetCatalogReport.PrintGeometryStudy(sourceDataPath);
    return;
}

if (parsed.ContainsKey("catalog-signal"))
{
    AssetCatalogReport.PrintSignalStudy(sourceDataPath);
    return;
}

if (parsed.ContainsKey("catalog-unknown"))
{
    AssetCatalogReport.PrintUnknownTextures(sourceDataPath);
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

ExternalProjectWorkspace? workspace = null;
try
{
    if (parsed.GetValueOrDefault("project") is string projectDirectory)
    {
        workspace = await ExternalProjectWorkspace.CreateAsync(
            projectDirectory, sourceDataPath, terrainOnly: parsed.ContainsKey("terrain-only"));
        EditorSession.Current.ExternalProjectDirectory = workspace.ProjectDirectory;
        EditorSession.Current.StartupWorldIndex = workspace.WorldIndex;
        sourceDataPath = workspace.DataDirectory;
        Console.WriteLine($"外部專案（唯讀）：{workspace.ProjectDirectory}");
    }

    Constants.DataPath = sourceDataPath;
    Console.WriteLine($"Data 目錄：{Constants.DataPath}");

    using var game = new MapEditorGame(options);
    game.Run();
}
catch (Exception ex)
{
    Console.Error.WriteLine($"啟動失敗：{ex.Message}");
    Environment.ExitCode = 2;
}
finally
{
    workspace?.Dispose();
}

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
    var valueOptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "data", "project", "project-check", "world", "tile", "size", "seconds", "screenshot",
        "grass-density", "grass-planes", "grass-distance", "grass-dense", "shots", "shot",
        "export-to", "export-openmu-to", "client-main", "openmu", "export-semantic-types",
    };
    var flagOptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "fullscreen", "selftest", "grass", "audit-objects", "catalog-report", "catalog-precision",
        "catalog-geometry", "catalog-signal", "catalog-unknown", "build-npc-catalog",
        // 只看地形：物件模型缺了不算錯。給「開別的專案的 Godot 中立包」用。
        "terrain-only",
    };
    var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    for (int index = 0; index < args.Length; index++)
    {
        string arg = args[index];
        if (!arg.StartsWith("--", StringComparison.Ordinal) || arg.Length == 2)
            throw new ArgumentException($"無法辨識 '{arg}'；參數必須以 -- 開頭。");

        string name = arg[2..];
        if (!valueOptions.Contains(name) && !flagOptions.Contains(name))
            throw new ArgumentException($"未知參數 --{name}。");
        if (!options.TryAdd(name, null))
            throw new ArgumentException($"--{name} 不得重複指定。");

        if (!valueOptions.Contains(name))
            continue;

        if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException($"--{name} 需要一個值。");

        options[name] = args[++index];
    }

    return options;
}

static void ValidateModeArguments(Dictionary<string, string?> options)
{
    if (options.ContainsKey("project") && options.ContainsKey("project-check"))
        throw new ArgumentException("--project 與 --project-check 不能同時使用。");

    if (options.ContainsKey("project-check"))
    {
        string[] irrelevant = options.Keys
            .Where(k => k is not "project-check" and not "data" and not "terrain-only")
            .ToArray();
        if (irrelevant.Length > 0)
            throw new ArgumentException($"--project-check 不接受 {string.Join(", ", irrelevant.Select(k => $"--{k}"))}。");
    }

    if (options.ContainsKey("project"))
    {
        string[] forbidden =
        [
            "world", "shots", "shot", "selftest", "audit-objects", "export-to", "export-openmu-to",
            "catalog-report", "catalog-precision", "catalog-geometry", "catalog-signal", "catalog-unknown",
            "build-npc-catalog", "client-main", "openmu", "export-semantic-types",
        ];
        string[] conflicts = forbidden.Where(options.ContainsKey).ToArray();
        if (conflicts.Length > 0)
            throw new ArgumentException($"唯讀 --project 不接受 {string.Join(", ", conflicts.Select(k => $"--{k}"))}。");
    }
}

static void PrintInspection(MapProjectInspection inspection)
{
    if (inspection.Project is { } project)
    {
        Console.WriteLine($"authoring project：World{project.WorldIndex} / MapNumber={project.MapNumber} / AttIndex={project.AttIndex}");
        Console.WriteLine($"legacy .map/.att codec：{(inspection.IsLegacyCodecCompatible ? "可輸出" : "不可輸出（超出 0..255；未取模、未重編號）")}");
        Console.WriteLine($"terrain textures：{inspection.TerrainTextureSources.Length}");
        Console.WriteLine($"BMD models：{inspection.ModelSources.Length}");
        Console.WriteLine($"BMD material textures：{inspection.ModelTextureSources.Length}");
    }

    foreach (string warning in inspection.Warnings)
        Console.WriteLine($"警告：{warning}");
    foreach (string error in inspection.Errors)
        Console.Error.WriteLine($"錯誤：{error}");

    Console.WriteLine(inspection.IsValid ? "專案與 renderer 依賴驗證通過。" : $"驗證失敗：{inspection.Errors.Length} 項錯誤。");
}

static void PrintUsage()
{
    Console.WriteLine("""
    MuMapEditor

      --data <Data目錄>             明確指定 MU Data 依賴根目錄
      --project <專案目錄>          唯讀開啟外部 map.json + 六張 PNG；先驗證貼圖與 BMD
      --project-check <專案目錄>    無 GUI 驗證 schema、PNG、貼圖與 BMD 依賴
      --world <N>                   從 Data 啟動 WorldN
      --seconds <N>                 N 秒後退出
      --help                        只印說明，不啟動 GUI

    World/MapNumber/AttIndex 在 authoring schema 是 int。只有輸出 MU legacy .map/.att
    才限制 0..255；超界會明確失敗，絕不取模、重編號或借 donor。
    """);
}
