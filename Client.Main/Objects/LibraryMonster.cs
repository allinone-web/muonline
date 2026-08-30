using System;
using System.IO;
using System.Linq;
using System.Text;
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
            Diagnose();
        }

        /// <summary>
        /// 把「這隻怪到底載到了什麼」寫成檔案。
        /// </summary>
        /// <remarks>
        /// 為什麼要寫檔而不是 <c>Console.WriteLine</c>：
        /// 在 iPhone 上 Console 只進系統 log，要開 Console.app 才看得到。
        /// 而這一類問題（模型載進來了、名牌出得來、就是畫不出來）光看畫面
        /// 分辨不出是「貼圖沒解開」「網格是空的」還是「緩衝區沒建起來」——
        /// 每一種在畫面上都長得一模一樣：什麼都沒有。
        ///
        /// 檔案落在資源庫旁邊，用 <c>tools/mu pull-diagnostic</c> 拉回來。
        /// </remarks>
        private void Diagnose()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine($"=== {Asset.Id} (#{Asset.BindNumber}) {DateTime.Now:HH:mm:ss} ===");
                sb.AppendLine($"  Status={Status}  Visible={Visible}  Scale={Scale}");

                if (Model is null)
                {
                    sb.AppendLine("  Model=null  ← 模型根本沒指派");
                }
                else
                {
                    sb.AppendLine($"  網格 {Model.Meshes.Length}、骨骼 {Model.Bones.Length}、動作 {Model.Actions.Length}");

                    for (int i = 0; i < Model.Meshes.Length; i++)
                    {
                        var mesh = Model.Meshes[i];
                        string wanted = mesh.TexturePath ?? "(空)";
                        string resolved = BMDLoader.Instance.GetTexturePath(Model, wanted) ?? "(解析不出路徑)";
                        var data = TextureLoader.Instance.Get(resolved);

                        sb.AppendLine($"  網格{i}: 頂點 {mesh.Vertices?.Length ?? 0}、三角形 {mesh.Triangles?.Length ?? 0}");
                        sb.AppendLine($"    貼圖名 '{wanted}'");
                        sb.AppendLine($"    解析成 '{resolved}'  檔案在={File.Exists(resolved)}");
                        sb.AppendLine($"    載入結果={(data is null ? "NULL ← 這就是畫不出來的原因" : $"{data.Width}×{data.Height} 元件{data.Components}")}");
                    }
                }

                string text = sb.ToString();
                Console.WriteLine(text);

                var root = LibraryAssetProvider.Root;
                var parent = Path.GetDirectoryName(root);
                if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                    File.AppendAllText(Path.Combine(parent, "library-diagnostic.log"), text);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Library] 診斷本身失敗：{ex.Message}");
            }
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
