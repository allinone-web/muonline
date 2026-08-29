// MU 資源瀏覽器 / 編輯器。
//
//   MuAssetStudio [--data <Data目錄>] [--size 1700x1000] [--open <名稱>]
//                 [--panels skills,export,library] [--kind 道具] [--open-library <id>]
//                 [--action N] [--pause]
//                 [--seconds N] [--screenshot <path>]
//
//   MuAssetStudio --report                          目錄盤點（不開視窗）
//   MuAssetStudio --verify [分類]                    解析每一個模型，回報解不開的與缺貼圖的
//   MuAssetStudio --items [篩選]                     道具模型的語意分類（劍／斧／盔甲…）
//   MuAssetStudio --skeleton-diff <主模型> --with <部位>  兩個模型的骨頭索引對不對得上
//   MuAssetStudio --godot-export --world N --out <資料夾> [--max-types 64] [--no-objects]
//                                                   整張地圖 → map.json + PNG + glTF（給 Godot 原型）
//   MuAssetStudio --check <相對路徑|名稱>            某個模型的貼圖是否齊全
//   MuAssetStudio --db [篩選] [--conn <連線字串>]     客戶端類別 vs OpenMU MonsterDefinition 對照
//   MuAssetStudio --skills [篩選] [--no-db]           技能盤點：型別、動作、視覺效果、與伺服器對照
//   MuAssetStudio --export <名稱或相對路徑> --out <資料夾> [--fps 4]
//                                                   BMD → glTF，不開視窗
//   MuAssetStudio --tex-export <貼圖檔> [--out <png>]        OZJ/OZT/OZD/OZP → PNG
//   MuAssetStudio --tex-import <png> --out <ozj|ozt> [--quality 92] [--no-backup]
//                                                   PNG → OZJ/OZT（會先備份原檔）
//   MuAssetStudio --textures-export <模型> [--out <資料夾>]   整套貼圖 → PNG
//   MuAssetStudio --textures-import <模型> --from <資料夾>    改過的 PNG → 寫回遊戲資源
//   MuAssetStudio --import <gltf|glb> [--scale N]    讀外部模型，印出相容性報告
//   MuAssetStudio --roundtrip <名稱> [--gltf <file>] [--scale N]
//                                                   匯出成 glTF 再讀回來，比對幾何；
//                                                   給 --gltf 就改比對現成的檔案
//
//   自有資產的資源庫（引擎中立：glTF + PNG + JSON 清單）
//   MuAssetStudio --library-list [--library <資料夾>]
//   MuAssetStudio --library-add <gltf|glb> [--name X] [--kind 怪物]
//   MuAssetStudio --library-show <id>                 相容性報告 + 目前的動作對映
//   MuAssetStudio --library-map <id> --action N [--clip <動作名稱>]
//
// 所有 CLI 模式都不需要圖形裝置，可以在沒有視窗的終端機工作階段裡跑。

using Client.AssetStudio;
using Client.AssetStudio.Catalog;
using Client.AssetStudio.Cli;

const string DefaultDataDir = "/Users/airtan/Documents/GitHub/mmorpg-3d-research/assets/MU_Red_1_20_61/Data";

var parsed = ParseArgs(args);
string dataPath = parsed.GetValueOrDefault("data") ?? DefaultDataDir;

if (!Directory.Exists(dataPath))
{
    Console.Error.WriteLine($"找不到 Data 目錄：{dataPath}");
    return 1;
}

var session = StudioSession.Current;
session.DataPath = dataPath;

Console.WriteLine($"Data 目錄：{dataPath}");
Console.WriteLine("掃描資源目錄…");

// 道具分類要在目錄之前建好：目錄會用它把 2715 個沒有語意的檔名換成道具名稱。
session.Items.Build();
session.Catalog.Build(dataPath, session.Items);

// 刻意用 GetAwaiter().GetResult() 而不是 await：
// 頂層陳述式裡只要出現一個 await，編譯器就會產生 async Main，
// await 之後的程式碼會落到執行緒集區的執行緒上 —— 而 SDL 的視訊初始化
// 必須在主執行緒（macOS 會丟 NSInternalInconsistencyException：
// "setting the main menu on a non-main thread"）。CLI 模式看不出來，只有開視窗時會炸。
session.Skills.BuildAsync(dataPath).GetAwaiter().GetResult();

Console.WriteLine($"　{session.Catalog.Entries.Length} 筆資源、{session.Skills.Entries.Length} 個技能");

// ── 不開視窗的模式 ────────────────────────────────────────────

if (parsed.ContainsKey("report"))
{
    CatalogReport.Print(session.Catalog);
    return 0;
}

if (parsed.TryGetValue("tex-export", out var textureSource) && textureSource is not null)
    return TextureCommands.Export(textureSource, parsed.GetValueOrDefault("out"));

if (parsed.TryGetValue("tex-import", out var imageSource) && imageSource is not null)
{
    string? textureTarget = parsed.GetValueOrDefault("out");

    if (textureTarget is null)
    {
        Console.Error.WriteLine("--tex-import 需要 --out <目標 .ozj 或 .ozt>");
        return 2;
    }

    int quality = parsed.TryGetValue("quality", out var q) && int.TryParse(q, out int parsedQuality)
        ? Math.Clamp(parsedQuality, 1, 100)
        : 92;

    return TextureCommands.Import(imageSource, textureTarget, quality, backup: !parsed.ContainsKey("no-backup"));
}

if (parsed.TryGetValue("skeleton-diff", out var skeletonBase) && skeletonBase is not null)
{
    string? part = parsed.GetValueOrDefault("with");

    if (part is null)
    {
        Console.Error.WriteLine("--skeleton-diff <主模型> 需要 --with <部位模型>");
        return 2;
    }

    return CatalogReport.CompareSkeletons(session.Catalog, skeletonBase, part);
}

if (parsed.ContainsKey("items"))
    return ItemReport.Print(session.Catalog, session.Items, parsed.GetValueOrDefault("items"));

if (parsed.GetValueOrDefault("library") is string libraryRoot)
    session.Library.Open(libraryRoot);

if (parsed.ContainsKey("library-list"))
    return LibraryCommands.List(session.Library);

if (parsed.TryGetValue("library-add", out var libraryAdd) && libraryAdd is not null)
{
    return LibraryCommands.Add(session.Library, libraryAdd,
                               parsed.GetValueOrDefault("name"), parsed.GetValueOrDefault("kind"));
}

if (parsed.TryGetValue("library-show", out var libraryShow) && libraryShow is not null)
    return LibraryCommands.Show(session.Library, libraryShow);

if (parsed.TryGetValue("library-map", out var libraryMap) && libraryMap is not null)
{
    if (!int.TryParse(parsed.GetValueOrDefault("action"), out int mappedAction))
    {
        Console.Error.WriteLine("--library-map <id> 需要 --action <編號> 與 --clip <動作名稱>");
        return 2;
    }

    return LibraryCommands.Map(session.Library, libraryMap, mappedAction, parsed.GetValueOrDefault("clip"));
}

if (parsed.TryGetValue("textures-export", out var texturesModel) && texturesModel is not null)
{
    string output = parsed.GetValueOrDefault("out")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        "Documents", "mu-textures", texturesModel.Replace('/', '_'));

    return TextureCommands.ExportAll(session.Catalog, texturesModel, output, dataPath);
}

if (parsed.TryGetValue("textures-import", out var texturesTarget) && texturesTarget is not null)
{
    string? from = parsed.GetValueOrDefault("from");

    if (from is null)
    {
        Console.Error.WriteLine("--textures-import <模型> 需要 --from <資料夾>");
        return 2;
    }

    int batchQuality = parsed.TryGetValue("quality", out var bq) && int.TryParse(bq, out int parsedBatchQuality)
        ? Math.Clamp(parsedBatchQuality, 1, 100)
        : 92;

    return TextureCommands.ImportAll(session.Catalog, texturesTarget, from, dataPath,
                                     batchQuality, backup: !parsed.ContainsKey("no-backup"));
}

if (parsed.TryGetValue("import", out var importPath) && importPath is not null)
{
    float? importScale = parsed.TryGetValue("scale", out var sc) && float.TryParse(sc, out float parsedScale)
        ? parsedScale
        : null;

    return ImportCommands.Inspect(importPath, importScale);
}

if (parsed.TryGetValue("roundtrip", out var roundtripTarget) && roundtripTarget is not null)
{
    float? roundtripScale = parsed.TryGetValue("scale", out var rs) && float.TryParse(rs, out float parsedRoundtripScale)
        ? parsedRoundtripScale
        : null;

    return ImportCommands.RoundTrip(session.Catalog, roundtripTarget, parsed.GetValueOrDefault("out"),
                                    dataPath, parsed.GetValueOrDefault("gltf"), roundtripScale);
}

if (parsed.ContainsKey("godot-export"))
{
    if (!int.TryParse(parsed.GetValueOrDefault("world"), out int godotWorld))
    {
        Console.Error.WriteLine("--godot-export 需要 --world N");
        return 2;
    }

    string godotOut = parsed.GetValueOrDefault("out")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        "Documents", "mu-godot", $"World{godotWorld}");

    var godotOptions = new Client.AssetStudio.Export.GodotSceneExporter.Options(
        MaxObjectTypes: parsed.TryGetValue("max-types", out var mt) && int.TryParse(mt, out int maxTypes)
            ? maxTypes
            : 64,
        ExportObjects: !parsed.ContainsKey("no-objects"));

    var godotResult = Client.AssetStudio.Export.GodotSceneExporter.Export(
        dataPath, godotWorld, godotOut, godotOptions);

    Console.WriteLine();
    Console.WriteLine($"World{godotResult.WorldIndex}（{godotOut}）");
    Console.WriteLine($"地形貼圖 {godotResult.TileTextures}　"
                    + $"物件型別 {godotResult.ObjectTypesExported} / {godotResult.ObjectTypes}　"
                    + $"物件實例 {godotResult.ObjectInstances}");

    foreach (var warning in godotResult.Warnings.Take(20))
        Console.WriteLine("  " + warning);

    if (godotResult.Warnings.Length > 20)
        Console.WriteLine($"  …另有 {godotResult.Warnings.Length - 20} 項");

    return 0;
}

if (parsed.ContainsKey("verify"))
    return VerifyCommand.Run(session.Catalog, parsed.GetValueOrDefault("verify"));

if (parsed.ContainsKey("skills"))
{
    return SkillReport
        .PrintAsync(session.Skills, parsed.GetValueOrDefault("skills"),
                    parsed.GetValueOrDefault("conn"), includeServer: !parsed.ContainsKey("no-db"))
        .GetAwaiter().GetResult();
}

if (parsed.ContainsKey("db"))
{
    return CatalogReport
        .PrintServerAsync(session.Catalog, parsed.GetValueOrDefault("conn"), parsed.GetValueOrDefault("db"))
        .GetAwaiter().GetResult();
}

if (parsed.TryGetValue("check", out var check) && check is not null)
    return CatalogReport.Check(session.Catalog, check);

if (parsed.TryGetValue("export", out var target) && target is not null)
{
    string output = parsed.GetValueOrDefault("out")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "mu-export");

    float fps = parsed.TryGetValue("fps", out var fpsText) && float.TryParse(fpsText, out float parsedFps)
        ? parsedFps
        : Client.AssetStudio.Export.GltfExporter.DefaultFramesPerSecond;

    return CatalogReport.Export(session.Catalog, target, output, fps, dataPath);
}

// ── 視窗模式 ─────────────────────────────────────────────────

(int width, int height) = ParseSize(parsed.GetValueOrDefault("size"));

var options = new StudioOptions(
    Width: width,
    Height: height,
    RunSeconds: parsed.TryGetValue("seconds", out var s) && double.TryParse(s, out double seconds) ? seconds : 0d,
    ScreenshotPath: parsed.GetValueOrDefault("screenshot"),
    InitialSelection: parsed.GetValueOrDefault("open"),
    InitialPanels: parsed.GetValueOrDefault("panels"),
    InitialKind: parsed.GetValueOrDefault("kind"),
    InitialLibraryAsset: parsed.GetValueOrDefault("open-library"),
    InitialAction: parsed.TryGetValue("action", out var a) && int.TryParse(a, out int actionIndex) ? actionIndex : null,
    StartPaused: parsed.ContainsKey("pause"),
    ShowSkeleton: parsed.ContainsKey("skeleton"),
    ConnectToServer: !parsed.ContainsKey("no-db"),
    ConnectionString: parsed.GetValueOrDefault("conn"));

using var game = new StudioGame(options);
game.Run();

return 0;

static (int, int) ParseSize(string? value)
{
    if (value is null)
        return (1700, 1000);

    var parts = value.Split('x', 'X');

    return parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h)
        ? (w, h)
        : (1700, 1000);
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
