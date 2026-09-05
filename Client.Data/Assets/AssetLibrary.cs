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
    /// 這一筆只記路徑、沒有把來源檔複製進資源庫。
    /// </summary>
    /// <remarks>
    /// 批次收進別人維護的成品時用。<b>來源移動或刪掉這一筆就壞了</b>，
    /// 所以自己做的資產不要用這個模式。
    /// </remarks>
    public bool Linked { get; set; }

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

    /// <summary>
    /// 事件 → 音效檔。鍵是 <c>idle</c> / <c>attack1</c> / <c>attack2</c> /
    /// <c>hurt</c> / <c>death</c>。
    /// </summary>
    /// <remarks>
    /// <b>匯進來的資產本來一定是啞的。</b>模型與動作有地方放，音效沒有 ——
    /// 而 MU 原生怪物的音效是寫死在各自的 <c>.cs</c> 檔裡的，
    /// 資源庫的資產沒有那個檔案，所以不管綁到哪一號都不會有聲音。
    ///
    /// 值可以是兩種：
    /// <list type="bullet">
    ///   <item><c>Sound/mEsisAttack1.wav</c> —— 沿用遊戲本來就有的音效</item>
    ///   <item><c>sfx/atk1.wav</c> —— 資產自己的資料夾底下，跟模型放在一起</item>
    /// </list>
    /// 解析順序是「先看資產資料夾、再看 <c>Data/</c>」，跟模型與貼圖同一個原則：
    /// <b>資源庫是覆寫</b>。
    /// </remarks>
    public Dictionary<string, string> Sounds { get; set; } = [];

    /// <summary>
    /// 額外的自發光亮度，加在地形光之上（0 = 不加，就是純漫反射的 MU 原生外觀）。
    /// </summary>
    /// <remarks>
    /// <b>MU 的著色器沒有高光項</b>（DynamicLighting.fx 的最終色 = 貼圖 × (環境光 + 太陽漫反射)）。
    /// Lineage 原版引擎有鏡面反射，所以角色在原版看起來有金屬光澤；搬進 MU 之後
    /// 只剩漫反射，就顯得比較「霧面」。這不是 bug —— 468 個 MU 原生怪物裡有 459 個
    /// 也是這種霧面外觀。想讓匯入的角色比 MU 原生更亮一點，可以在這裡加一個
    /// 自發光偏移（例如 0.15），效果等同 WorldObject.Light。0 代表跟 MU 怪物一致。
    /// </remarks>
    public float LightBoost { get; set; }

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
    /// <param name="link">
    /// <b>只記路徑，不複製來源檔。</b>批次收進上千個外部資產時用 ——
    /// 天堂那批角色平均 2.7 MB，1,500 個複製下來是 4 GB，
    /// 而它們本來就在另一個 repo 裡受版本管理，複製一份只是多一份會過期的副本。
    ///
    /// 代價要講清楚：<b>來源那邊移動或刪掉檔案，這一筆就壞了</b>。
    /// 所以自己做的資產仍然應該用複製（預設），link 只給「別人維護的成品」。
    /// </param>
    public LibraryAsset? Add(string sourcePath, string? name, EntityKind kind,
                             out ImportedModel? imported, bool link = false)
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
            if (!link)
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
                // link 時存絕對路徑。SourcePathOf 用的是 Path.Combine，
                // 而 Path.Combine 遇到絕對路徑的第二段會直接回傳它 —— 不必改解析。
                Source = link ? Path.GetFullPath(sourcePath) : Path.Combine(id, sourceName),
                Linked = link,
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

    /// <summary>這個資產有沒有給某個事件配音效。</summary>
    public string? SoundFor(LibraryAsset asset, string sound)
        => asset.Sounds.TryGetValue(sound, out var path) && !string.IsNullOrWhiteSpace(path)
            ? path
            : null;

    /// <summary>綁一個事件的音效；<paramref name="path"/> 給 null 就是解除。</summary>
    public void MapSound(LibraryAsset asset, string sound, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            asset.Sounds.Remove(sound);
        else
            asset.Sounds[sound] = path.Replace('\\', '/');

        Save();
    }

    /// <summary>
    /// 音效檔在磁碟上的真正位置；兩個地方都沒有就回傳 null。
    /// </summary>
    /// <remarks>
    /// <b>先找資產自己的資料夾</b>，找不到才當成 <c>Data/</c> 底下的相對路徑。
    /// 順序反過來的話，資產帶進來的音效永遠蓋不掉同名的遊戲音效。
    /// </remarks>
    public string? ResolveSound(LibraryAsset asset, string sound, string dataPath)
    {
        string? value = SoundFor(asset, sound);
        if (value is null)
            return null;

        string local = Path.Combine(Root, asset.Id, value);
        if (File.Exists(local))
            return local;

        string shared = Path.Combine(dataPath, value);
        return File.Exists(shared) ? shared : null;
    }

    /// <summary>資源庫認得的事件名稱。順序就是列印的順序。</summary>
    public static readonly string[] SoundEvents = ["idle", "attack1", "attack2", "hurt", "death"];

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

    /// <summary>
    /// 名稱 → 資料夾名。<b>只留 ASCII。</b>
    /// </summary>
    /// <remarks>
    /// 原本用 <c>char.IsLetterOrDigit</c>，而那個對 CJK 回傳 true ——
    /// 於是「天堂_死亡騎士」原封不動變成資料夾名。在 macOS 上完全正常，
    /// 但這個資料夾要被複製到 iOS 裝置上：macOS 的檔名是 NFD（分解式）、
    /// iOS 是 NFC（組合式），複製過去之後 <c>File.Exists</c> 對不上，
    /// 載入安靜地失敗，世界裡就出現一隻看得到名字、看不到身體的怪。
    ///
    /// 資產的<b>顯示名稱</b>（<see cref="LibraryAsset.Name"/>）想用什麼語言都行，
    /// 但<b>檔案系統上的 id</b> 要能安全地跨平台複製。
    /// </remarks>
    private string MakeId(string name)
    {
        static bool Keep(char c) =>
            (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c is '-' or '_';

        string id = new string(name.Select(c => Keep(c) ? c : '-').ToArray())
            .Trim('-')
            .ToLowerInvariant();

        while (id.Contains("--"))
            id = id.Replace("--", "-");

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
