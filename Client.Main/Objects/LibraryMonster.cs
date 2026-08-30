using System.Linq;
using System.Threading.Tasks;
using Client.AssetStudio.Project;
using Client.Data.BMD;
using Client.Main.Content;

namespace Client.Main.Objects
{
    /// <summary>
    /// 模型來自資源庫、而不是 <c>Data/Monster/MonsterNN.bmd</c> 的怪物。
    /// </summary>
    /// <remarks>
    /// 其他怪物都是「一個編號一個 C# 類別」，模型路徑寫死在自己的 <c>Load()</c> 裡
    /// （例如 <c>Aegis</c> 寫死 <c>Monster/Monster67.bmd</c>）。那套靠的是編譯期的
    /// <c>[NpcInfo]</c> 屬性，資源庫的資產沒辦法用 —— 它們是執行期才知道的。
    ///
    /// 所以這裡反過來：<b>一個類別服務所有資源庫資產</b>，
    /// 要載哪一個由建構子帶進來（見 <c>ScopeHandler</c> 的生成點）。
    ///
    /// 行為（移動速度、攻擊間隔、掉落）仍然由伺服器決定，這裡只管外觀 ——
    /// 與 <c>docs/資源瀏覽器.md</c> 的「外觀在客戶端，行為在伺服器」一致。
    /// </remarks>
    public class LibraryMonster : MonsterObject
    {
        public LibraryAsset Asset { get; }

        private readonly BMD _model;
        private readonly string _displayName;

        /// <param name="model">
        /// 已經載好的模型。<see cref="LibraryAssetProvider.TryPrepareAsync"/> 先載過一次，
        /// 確認載得起來才會走到這裡 —— 載不起來的話生怪流程會退回原本的怪物類別。
        /// </param>
        public LibraryMonster(LibraryAsset asset, BMD model)
        {
            Asset = asset;
            _model = model;
            _displayName = SafeName(asset);

            // 縮放已經在匯入時烘進頂點了（GltfImporter 收 asset.Scale），
            // 這裡再乘一次會變成平方。
            Scale = 1f;
        }

        public override string DisplayName => _displayName;

        public override async Task Load()
        {
            Model = _model;
            await base.Load();
        }

        /// <summary>
        /// 把名字換成畫得出來的字。
        /// </summary>
        /// <remarks>
        /// MU 的名牌用的是點陣字型，只有 ASCII。中文名字（「天堂_死亡騎士」）
        /// 會整串變成「？？？？」—— 這不是編碼壞掉，是字型裡根本沒有那些字。
        /// 所以非 ASCII 的名字退回用模型檔名（那是英文的，例如 Mon_DeathKnight_UW）。
        /// </remarks>
        internal static string SafeName(LibraryAsset asset)
        {
            if (!string.IsNullOrEmpty(asset.Name) && asset.Name.All(c => c < 128))
                return asset.Name;

            // 檔名是提取管線產生的，一定是英文（SK_Mon_DeathKnight_UW）。
            var fromFile = System.IO.Path.GetFileNameWithoutExtension(asset.Source);
            return string.IsNullOrEmpty(fromFile) ? "Library Asset" : fromFile;
        }
    }
}
