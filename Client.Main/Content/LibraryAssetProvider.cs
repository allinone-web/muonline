using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Client.AssetStudio.Import;
using Client.AssetStudio.Project;
using Client.Data.BMD;
using Microsoft.Extensions.Logging;

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
        private static ILogger _logger;

        public static void UseLogger(ILogger logger) => _logger = logger;

        /// <summary>資源庫的根目錄。預設是 <c>~/Documents/mu-studio-library</c>。</summary>
        public static string Root => Ensure().Root;

        public static int Count => Ensure() is not null ? _byNumber.Count : 0;

        private static AssetLibrary Ensure()
        {
            lock (Gate)
            {
                if (_library is not null)
                    return _library;

                _library = new AssetLibrary();
                Reindex();

                if (_byNumber.Count > 0)
                    _logger?.LogInformation(
                        "資源庫：{Count} 個資產綁了編號（{Root}）", _byNumber.Count, _library.Root);

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

        /// <summary>重新讀一次清單。改完綁定不必重開客戶端。</summary>
        public static void Reload()
        {
            lock (Gate)
            {
                _library = new AssetLibrary();
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
