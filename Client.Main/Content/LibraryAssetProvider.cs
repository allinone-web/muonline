using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Client.AssetStudio.Import;
using Client.AssetStudio.Project;
using Client.Data.BMD;
using Microsoft.Extensions.Logging;
using Client.Main;

namespace Client.Main.Content
{
    /// <summary>
    /// 讓執行期能用資源庫（<c>mu-studio-library</c>）裡的自有資產取代遊戲原本的模型。
    /// </summary>
    /// <remarks>
    /// <b>在這之前，<c>LibraryAsset.BindNumber</c> 只有 MuAssetStudio 在讀。</b>
    /// 工具裡可以填「這個資產要接管 150 號怪」，但客戶端從來沒問過這件事，
    /// 所以填了等於沒填。這個類別就是把那條線接上。
    ///
    /// 查詢順序刻意是「先看資源庫，沒有才走 <c>NpcDatabase</c>」：
    /// 資源庫是<b>覆寫</b>，目的就是要蓋掉原本的模型；反過來的話永遠蓋不掉。
    ///
    /// 貼圖不複製進 <c>Data/</c>，直接用資源庫的絕對路徑
    /// （見 <see cref="BMDLoader.RegisterExternalModel"/>）。
    /// </remarks>
    public static class LibraryAssetProvider
    {
        private static readonly object Gate = new();
        private static AssetLibrary _library;
        private static Dictionary<ushort, LibraryAsset> _byNumber;
        private static readonly Dictionary<string, BMD> _cache = new(StringComparer.OrdinalIgnoreCase);
        private static ILogger _logger = MuGame.AppLoggerFactory?.CreateLogger("LibraryAssetProvider");

        public static void UseLogger(ILogger logger) => _logger = logger;

        /// <summary>啟動時印一次現況。沒有這行，「資源庫是空的」在裝置上完全看不出來。</summary>
        public static string Describe()
        {
            var library = Ensure();
            return _byNumber.Count > 0
                ? $"資源庫 {library.Root}：{library.Assets.Count} 個資產、{_byNumber.Count} 個綁了編號"
                : $"資源庫 {library.Root}：沒有綁定編號的資產（找不到目錄或清單是空的）";
        }

        /// <summary>資源庫的根目錄。預設是 <c>~/Documents/mu-studio-library</c>。</summary>
        public static string Root => Ensure().Root;

        public static int Count => Ensure() is not null ? _byNumber.Count : 0;

        private static AssetLibrary Ensure()
        {
            lock (Gate)
            {
                if (_library is not null)
                    return _library;

                _library = new AssetLibrary(ResolveRoot());
                Reindex();

                // 有沒有東西都要印。之前只在「有」的時候印，於是裝置上資源庫是空的時
                // 什麼訊息都沒有 —— 看起來就像功能沒做，而不是資料沒推上去。
                if (_byNumber.Count > 0)
                    _logger?.LogInformation(
                        "資源庫：{Count} 個資產綁了編號（{Root}）", _byNumber.Count, _library.Root);
                else
                    _logger?.LogWarning(
                        "資源庫沒有可用的綁定：{Root}（資產 {Assets} 個）。"
                      + "裝置上要先推：tools/mu push-assets --library",
                        _library.Root, _library.Assets.Count);

                return _library;
            }
        }

        private static void Reindex()
        {
            _byNumber = new Dictionary<ushort, LibraryAsset>();

            foreach (var asset in _library.Assets)
            {
                if (asset.BindNumber < 0 || asset.BindNumber > ushort.MaxValue)
                    continue;

                // 同一個編號被綁兩次的話，後面的贏 —— 與 NpcDatabase 的行為一致。
                _byNumber[(ushort)asset.BindNumber] = asset;
            }
        }

        /// <summary>
        /// 找資源庫在哪。
        /// </summary>
        /// <remarks>
        /// <b>不要相信 <c>AssetLibrary.DefaultRoot</c> 在每個平台都對。</b>
        /// 它用的是 <c>SpecialFolder.UserProfile</c> + "Documents"，
        /// 在 macOS 上剛好是 <c>~/Documents</c>，但 iOS 上 UserProfile 回傳什麼
        /// 是執行期才知道的（容器根目錄？還是 Documents 本身？）——
        /// 猜錯就是安靜地讀到空清單，每隻怪都落回原本的模型而且不報錯。
        /// <b>這個 bug 已經吃過一次了。</b>
        ///
        /// 所以錨定在 <see cref="Constants.DataPath"/> 旁邊：那是每個平台都明確
        /// 設定過、而且一定存在的路徑（iOS 由 MuIos/Program.cs 指定容器裡的
        /// Documents/Data）。資源庫就放它隔壁。桌面環境維持原本的家目錄慣例。
        /// 兩個都試，哪個真的有 library.json 就用哪個。
        /// </remarks>
        private static string ResolveRoot()
        {
            var candidates = new List<string>();

            if (!string.IsNullOrEmpty(Constants.DataPath))
            {
                var beside = Path.GetDirectoryName(Path.GetFullPath(Constants.DataPath));
                if (!string.IsNullOrEmpty(beside))
                    candidates.Add(Path.Combine(beside, "mu-studio-library"));
            }

            candidates.Add(AssetLibrary.DefaultRoot);

            foreach (var candidate in candidates)
            {
                if (File.Exists(Path.Combine(candidate, "library.json")))
                {
                    _logger?.LogInformation("資源庫根目錄：{Root}", candidate);
                    return candidate;
                }
            }

            _logger?.LogWarning("找不到資源庫（試過：{Tried}）", string.Join("、", candidates));
            return candidates[0];
        }

        /// <summary>重新讀一次清單。改完綁定不必重開客戶端。</summary>
        public static void Reload()
        {
            lock (Gate)
            {
                _library = new AssetLibrary(ResolveRoot());
                Reindex();
                _cache.Clear();
            }
        }

        /// <summary>這個怪物／NPC 編號有沒有被資源庫接管。</summary>
        public static bool TryGet(ushort typeId, out LibraryAsset asset)
        {
            Ensure();
            lock (Gate)
                return _byNumber.TryGetValue(typeId, out asset);
        }

        /// <summary>
        /// 查出這個編號有沒有被接管，<b>而且確認它真的載得起來</b>。
        /// </summary>
        /// <remarks>
        /// 為什麼要在這裡就把模型載出來，而不是等 <c>LibraryMonster.Load()</c>：
        ///
        /// 一開始是那樣做的，結果 glTF 在裝置上載失敗時，<c>Model</c> 留在 null，
        /// 於是世界裡出現一隻<b>看得到名字、看不到身體</b>的怪 ——
        /// 而且原本那隻正常的怪也被它取代掉了，等於「覆寫失敗 = 少一隻怪」。
        ///
        /// 覆寫失敗應該<b>退回原本的模型</b>，不是留一個洞。
        /// 所以先載、載得起來才回報接管；BMD 有快取，這一步不會多花成本。
        /// </remarks>
        public static async Task<(LibraryAsset Asset, BMD Model)?> TryPrepareAsync(ushort typeId)
        {
            if (!TryGet(typeId, out var asset))
                return null;

            try
            {
                var model = await LoadAsync(asset).ConfigureAwait(false);

                if (model.Meshes is not { Length: > 0 })
                {
                    _logger?.LogWarning(
                        "資源庫 {Name}（#{Number}）沒有網格，退回原本的模型。", asset.Name, typeId);
                    return null;
                }

                return (asset, model);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex,
                    "資源庫 {Name}（#{Number}）載入失敗，退回原本的模型：{Message}",
                    asset.Name, typeId, ex.Message);
                return null;
            }
        }

        /// <summary>把資產轉成可以直接指給 <c>ModelObject.Model</c> 的 BMD。</summary>
        public static Task<BMD> LoadAsync(LibraryAsset asset)
        {
            lock (Gate)
            {
                if (_cache.TryGetValue(asset.Id, out var cached))
                    return Task.FromResult(cached);
            }

            return Task.Run(() =>
            {
                var library = Ensure();
                string source = library.SourcePathOf(asset);

                var imported = GltfImporter.Import(
                    source, new GltfImporter.Options(Scale: asset.Scale, AutoScale: false));

                var model = imported.Model;
                RemapActions(model, imported.Clips, asset.Actions);
                ApplyActionSpeeds(model, asset);

                // 貼圖用資源庫的絕對路徑，不複製進 Data。
                string textureDir = library.TextureDirectoryOf(asset);
                var paths = new Dictionary<string, string>();
                foreach (var texture in imported.Textures)
                    paths[texture.Name] = Path.Combine(textureDir, texture.Name);

                BMDLoader.Instance.RegisterExternalModel(model, paths);

                lock (Gate)
                    _cache[asset.Id] = model;

                _logger?.LogInformation(
                    "資源庫載入 {Name}：{Meshes} 網格、{Bones} 骨骼、{Actions} 動作",
                    asset.Name, model.Meshes.Length, model.Bones.Length, model.Actions.Length);

                return model;
            });
        }

        /// <summary>
        /// 套用資源庫記下來的播放速度，讓動畫長度跟伺服器的節奏對上。
        /// </summary>
        /// <remarks>
        /// 放在<b>這裡</b>而不是 <c>LibraryMonster</c>，是因為這是唯一把資產變成 BMD 的地方。
        /// 放在物件那一層的話，工具（<c>--library-tune</c>）讀到的會是沒套速度的原始 BMD，
        /// 於是「現在幾秒」那一欄永遠顯示舊值，看起來像是設定沒生效。
        /// 同一份資料要有同一個真相來源。
        /// </remarks>
        internal static void ApplyActionSpeeds(BMD model, LibraryAsset asset)
        {
            if (asset.ActionSpeeds is not { Count: > 0 } speeds || model.Actions.Length == 0)
                return;

            foreach (var (key, speed) in speeds)
            {
                if (!int.TryParse(key, out int slot) || slot < 0 || slot >= model.Actions.Length)
                    continue;

                if (model.Actions[slot] is { } action && speed > 0f)
                    action.PlaySpeed = speed;
            }
        }

        /// <summary>
        /// 把動作重排成遊戲要的順序。
        /// </summary>
        /// <remarks>
        /// glTF 的動作是<b>有名字、沒編號</b>的，順序就是它在檔案裡的順序；
        /// 遊戲的動作是<b>有編號、沒名字</b>的（<c>MonsterActionType.Walk</c> 就是 2）。
        /// <c>LibraryAsset.Actions</c> 那張表是使用者在 GUI 裡一格一格對出來的，
        /// 這裡把它套用上去。
        ///
        /// <b>骨頭的 Matrixes 是以動作為索引的</b>（<c>bone.Matrixes[action]</c>），
        /// 所以重排動作時每一根骨頭的矩陣陣列都要跟著重排，
        /// 否則會播出「動作 2 的名字配動作 5 的姿勢」——而且不會有任何錯誤。
        /// </remarks>
        internal static void RemapActions(BMD model, string[] clips, IDictionary<string, string> mapping)
        {
            if (mapping is null || mapping.Count == 0 || model.Actions.Length == 0)
                return;

            int highest = -1;
            var wanted = new Dictionary<int, int>();   // 遊戲動作編號 → 原始索引

            foreach (var (key, clipName) in mapping)
            {
                if (string.IsNullOrEmpty(clipName) || !int.TryParse(key, out int number) || number < 0)
                    continue;

                int index = Array.IndexOf(clips, clipName);
                if (index < 0)
                {
                    _logger?.LogWarning("資源庫：動作 {Number} 指到不存在的 {Clip}", number, clipName);
                    continue;
                }

                wanted[number] = index;
                highest = Math.Max(highest, number);
            }

            if (highest < 0)
                return;

            var actions = new BMDTextureAction[highest + 1];
            var perBone = new BMDBoneMatrix[model.Bones.Length][];

            for (int b = 0; b < model.Bones.Length; b++)
                perBone[b] = new BMDBoneMatrix[highest + 1];

            for (int slot = 0; slot <= highest; slot++)
            {
                // 沒對映到的槽位留一個空動作，而不是讓陣列有洞 ——
                // 客戶端會直接用編號索引，越界就是當場崩。
                int src = wanted.TryGetValue(slot, out int index) ? index : -1;

                actions[slot] = src >= 0 && src < model.Actions.Length
                    ? model.Actions[src]
                    : new BMDTextureAction { NumAnimationKeys = 1 };

                for (int b = 0; b < model.Bones.Length; b++)
                {
                    var matrixes = model.Bones[b]?.Matrixes;
                    perBone[b][slot] = src >= 0 && matrixes is not null && src < matrixes.Length
                        ? matrixes[src]
                        : (matrixes is { Length: > 0 } ? matrixes[0] : default);
                }
            }

            model.Actions = actions;
            for (int b = 0; b < model.Bones.Length; b++)
                if (model.Bones[b] is not null)
                    model.Bones[b].Matrixes = perBone[b];
        }
    }
}
