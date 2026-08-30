using System.Text.Json;
using System.Text.Json.Serialization;
using Client.AssetStudio.Catalog;
using Client.AssetStudio.Import;

namespace Client.AssetStudio.Project;

/// <summary>資源庫裡的一筆自有資產。</summary>
public sealed class LibraryAsset
{
    /// <summary>資料夾名稱，也是唯一鍵。</summary>
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>要取代遊戲裡的哪一類東西。</summary>
    public EntityKind Kind { get; set; } = EntityKind.Monster;

    /// <summary>相對於資源庫根目錄的原始檔路徑。</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>匯入時套用的縮放。</summary>
    public float Scale { get; set; } = 1f;

    /// <summary>
    /// 遊戲的動作編號 → 外部檔案裡的動作名稱。
    /// </summary>
    /// <remarks>
    /// 這是整個資源庫最重要、也最無法自動化的一份資料。
    /// MU 的動作是<b>編號</b>（<c>MonsterActionType.Die</c> 就是 6），
    /// 外部模型的動作是<b>名字</b>（"Death"、"die_01"、"Armature|Die"）。
    /// 沒有這張表，匯進來的角色會用錯的動作播放，而且不會有任何錯誤。
    /// </remarks>
    public Dictionary<string, string> Actions { get; set; } = [];

    /// <summary>
    /// 遊戲的動作編號 → 播放速度（<c>BMDTextureAction.PlaySpeed</c>）。
    /// </summary>
    /// <remarks>
    /// 動畫長度必須跟伺服器的節奏對得上，否則會看到
    /// 「攻擊動畫還沒揮完，傷害數字已經跳出來」這種視覺落差。
    ///
    /// 對齊的方向刻意是<b>改客戶端的播放速度，而不是改伺服器的 AttackDelay</b>：
    /// 伺服器的數值是遊戲平衡，動的是玩法；播放速度只影響觀感。
    /// 換一隻怪就要重調平衡，那是本末倒置。
    ///
    /// 公式來自 <c>ModelObject.Animation.cs</c>：
    /// <c>_animTime += delta * PlaySpeed * AnimationSpeed</c>，
    /// <c>_animTime</c> 的單位是影格，播完的條件是 <c>&gt;= 影格數 - 1</c>。
    /// 所以 <c>PlaySpeed = (影格數 - 1) / (目標秒數 * AnimationSpeed)</c>，
    /// <c>AnimationSpeed</c> 預設 4。
    /// </remarks>
    public Dictionary<string, float> ActionSpeeds { get; set; } = [];

    /// <summary>要接管遊戲裡的哪一個編號（怪物／NPC 的 <c>[NpcInfo]</c> typeId）。-1 表示還沒決定。</summary>
    public int BindNumber { get; set; } = -1;

    /// <summary>要取代的模型路徑（相對於 <c>Data/</c>），例如 <c>Monster/Monster33.bmd</c>。</summary>
    public string? BindModelPath { get; set; }

    public string? Note { get; set; }

    public DateTimeOffset ImportedAt { get; set; } = DateTimeOffset.Now;
}

/// <summary>
/// 自有資產的資源庫：<b>引擎中立</b>的一個資料夾加一份清單。
/// </summary>
/// <remarks>
/// <b>這是「完全成為自己的遊戲」缺的那一塊。</b>
/// 在這之前，這個工具只能瀏覽 Webzen 的資產 —— 沒有任何地方放<b>你自己的</b>東西。
///
/// 格式刻意與 <c>docs/引擎轉換方案-工具與客戶端遷移到Godot.md</c> 的三條鐵律一致：
/// <list type="bullet">
/// <item>網格與動畫存 <b>glTF</b>（原始檔原封不動複製進來，不轉檔）</item>
/// <item>貼圖存 <b>PNG</b></item>
/// <item>清單是 <b>JSON</b>，git 友善、可以 diff</item>
/// </list>
/// 沒有任何一個位元組是 MonoGame 或 Godot 的格式。
/// 客戶端將來換到哪個引擎，這個資料夾都不用動 ——
/// 換的是「安裝到執行期」那一步，不是資產本身。
///
/// 也刻意<b>不</b>把 <c>.bmd</c> 寫進資源庫：那會變成第三種要維護的格式，
/// 而且是三種裡唯一沒有工具鏈的那一種。匯入時產生的 BMD 只活在記憶體裡，
/// 用途是走既有的檢視器（見 <see cref="GltfImporter"/>）。
/// </remarks>
public sealed class AssetLibrary
{
    private const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private sealed class Manifest
    {
        public int Version { get; set; } = CurrentVersion;
        public List<LibraryAsset> Assets { get; set; } = [];
    }

    private Manifest _manifest = new();

    public string Root { get; private set; }

    public string ManifestPath => Path.Combine(Root, "library.json");

    public IReadOnlyList<LibraryAsset> Assets => _manifest.Assets;

    public string? LastError { get; private set; }

    public AssetLibrary(string? root = null)
    {
        Root = root ?? DefaultRoot;
        Load();
    }

    public static string DefaultRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Documents", "mu-studio-library");

    public void Open(string root)
    {
        Root = root;
        Load();
    }

    /// <summary>某一筆資產的資料夾。</summary>
    public string DirectoryOf(LibraryAsset asset) => Path.Combine(Root, asset.Id);

    /// <summary>貼圖抽出來放的地方。檢視器直接從這裡解析貼圖名稱。</summary>
    public string TextureDirectoryOf(LibraryAsset asset) => Path.Combine(DirectoryOf(asset), "textures");

    public string SourcePathOf(LibraryAsset asset) => Path.Combine(Root, asset.Source);

    /// <summary>
    /// 把一個外部模型收進資源庫。
    /// </summary>
    /// <remarks>
    /// <b>原始檔原封不動複製</b>，不轉檔也不重寫 ——
    /// 資源庫存的是「你的來源檔」，衍生物（貼圖 PNG、將來的引擎資源）都能從它重建。
    /// 這與地圖那邊的 <c>map.json + PNG</c> 是同一個原則。
    /// </remarks>
    public LibraryAsset? Add(string sourcePath, string? name, EntityKind kind, out ImportedModel? imported)
    {
        imported = null;
        LastError = null;

        if (!File.Exists(sourcePath))
        {
            LastError = $"找不到 {sourcePath}";
            return null;
        }

        try
        {
            var model = GltfImporter.Import(sourcePath, new GltfImporter.Options(AutoScale: true));
            imported = model;

            if (model.Report.HasErrors)
            {
                LastError = model.Report.Summary;
                return null;
            }

            string id = MakeId(name ?? Path.GetFileNameWithoutExtension(sourcePath));
            string directory = Path.Combine(Root, id);
            Directory.CreateDirectory(directory);

            // 來源檔連同它旁邊的相依檔（.bin、貼圖）一起複製。
            // .gltf 是「一份 JSON 加一堆外部檔」，只複製 JSON 會得到一個開不起來的資產。
            string sourceName = Path.GetFileName(sourcePath);
            CopySourceWithDependencies(sourcePath, directory);

            string textures = Path.Combine(directory, "textures");
            Directory.CreateDirectory(textures);

            foreach (var texture in model.Textures)
                File.WriteAllBytes(Path.Combine(textures, texture.Name), texture.Content);

            var asset = new LibraryAsset
            {
                Id = id,
                Name = name ?? Path.GetFileNameWithoutExtension(sourcePath),
                Kind = kind,
                Source = Path.Combine(id, sourceName),
                Scale = model.Report.SuggestedScale,
            };

            _manifest.Assets.Add(asset);
            Save();
            return asset;
        }
        catch (Exception ex)
        {
            LastError = $"{ex.GetType().Name}：{ex.Message}";
            return null;
        }
    }

    public bool Remove(string id)
    {
        var asset = _manifest.Assets.FirstOrDefault(a => a.Id == id);
        if (asset is null)
            return false;

        _manifest.Assets.Remove(asset);
        Save();
        return true;
    }

    public LibraryAsset? Find(string idOrName) => _manifest.Assets.FirstOrDefault(a =>
        a.Id.Equals(idOrName, StringComparison.OrdinalIgnoreCase)
     || a.Name.Equals(idOrName, StringComparison.OrdinalIgnoreCase));

    public void Update() => Save();

    /// <summary>把外部的動作名稱對到遊戲的動作編號。</summary>
    public void MapAction(LibraryAsset asset, int action, string? clip)
    {
        if (string.IsNullOrWhiteSpace(clip))
            asset.Actions.Remove(action.ToString());
        else
            asset.Actions[action.ToString()] = clip;

        Save();
    }

    public string? ClipFor(LibraryAsset asset, int action)
        => asset.Actions.TryGetValue(action.ToString(), out var clip) ? clip : null;

    // ── 檔案 ─────────────────────────────────────────────────────

    private static void CopySourceWithDependencies(string sourcePath, string destination)
    {
        string sourceDirectory = Path.GetDirectoryName(sourcePath) ?? ".";
        string stem = Path.GetFileNameWithoutExtension(sourcePath);

        File.Copy(sourcePath, Path.Combine(destination, Path.GetFileName(sourcePath)), overwrite: true);

        // .gltf 會把幾何放在同名的 .bin，貼圖放在旁邊。GLB 是單檔，這個迴圈就什麼都不做。
        foreach (var companion in Directory.EnumerateFiles(sourceDirectory, stem + ".*"))
        {
            if (companion.Equals(sourcePath, StringComparison.OrdinalIgnoreCase))
                continue;

            File.Copy(companion, Path.Combine(destination, Path.GetFileName(companion)), overwrite: true);
        }
    }

    private string MakeId(string name)
    {
        string id = new string(name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray())
            .Trim('-')
            .ToLowerInvariant();

        if (string.IsNullOrEmpty(id))
            id = "asset";

        string candidate = id;
        int suffix = 2;

        while (_manifest.Assets.Any(a => a.Id == candidate))
            candidate = $"{id}-{suffix++}";

        return candidate;
    }

    private void Load()
    {
        _manifest = new Manifest();
        LastError = null;

        try
        {
            if (!File.Exists(ManifestPath))
                return;

            var loaded = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(ManifestPath), SerializerOptions);

            if (loaded is not null)
                _manifest = loaded;
        }
        catch (Exception ex)
        {
            LastError = $"讀取資源庫失敗：{ex.Message}";
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Root);

            // 與標註檔同樣的理由：先寫暫存檔再換上去。
            // 這份清單是使用者一件一件累積的成果，寫壞的代價比重跑一次匯入大得多。
            string temporary = ManifestPath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(_manifest, SerializerOptions));
            File.Move(temporary, ManifestPath, overwrite: true);

            LastError = null;
        }
        catch (Exception ex)
        {
            LastError = $"資源庫存檔失敗：{ex.Message}";
        }
    }
}
