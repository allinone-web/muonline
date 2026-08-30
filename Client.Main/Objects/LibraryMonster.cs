using System.Threading.Tasks;
using Client.AssetStudio.Project;
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

        public LibraryMonster(LibraryAsset asset)
        {
            Asset = asset;

            // 縮放已經在匯入時烘進頂點了（GltfImporter 收 asset.Scale），
            // 這裡再乘一次會變成平方。
            Scale = 1f;
        }

        public override string DisplayName => Asset.Name;

        public override async Task Load()
        {
            Model = await LibraryAssetProvider.LoadAsync(Asset);
            await base.Load();
        }
    }
}
