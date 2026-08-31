using Client.Main.Controls;
using Client.Main.Core.Utilities;

namespace Client.Main.Worlds
{
    /// <summary>
    /// 天堂_說話之島（地圖編輯器產生的新地圖）。
    /// </summary>
    /// <remarks>
    /// [WorldInfo] 的編號是 OpenMU 的地圖編號，建構子的 worldIndex 是客戶端的編號，
    /// 兩者差一（客戶端 = OpenMU + 1）。這裡是 OpenMU 210 / 客戶端 211。
    ///
    /// 要讓地圖上的物件有語意行為（樹會搖、火會亮），
    /// 覆寫 CreateMapTileObjects() 把 MapTileObjects[型別編號] 指到對應的類別，
    /// 可以參考 LorenciaWorld。
    /// </remarks>
    [WorldInfo(210, "天堂_說話之島")]
    public class World211 : WalkableWorldControl
    {
        public World211() : base(worldIndex: 211)
        {
            Name = "天堂_說話之島";
        }
    }
}
