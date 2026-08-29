using Client.Main.Controls;
using Microsoft.Xna.Framework;
using MuAssets.Core;

namespace Client.MapEditor;

/// <summary>
/// 編輯器用的世界：world index 是執行期給的，而不是像 <c>LorenciaWorld</c> 那樣寫死在類別裡。
/// </summary>
/// <remarks>
/// 刻意繼承 <see cref="WorldControl"/> 而不是 <see cref="WalkableWorldControl"/>：
/// 後者的 Update 會無條件解參考 <c>Walker.Position</c>（點擊移動與游標邏輯），
/// 編輯器沒有玩家角色，接上去只會 NPE。地形載入、物件擺放、渲染全都在 WorldControl 這層。
///
/// 物件的語意類別（Lorencia 的 0–12 是 TreeObject 之類）由 <see cref="WorldCatalog.GetTileObjectTypes"/>
/// 從對應的 world 類別抽出來後傳進來，所以編輯器裡的物件行為與遊戲一致。
/// </remarks>
public sealed class EditorWorldControl : WorldControl
{
    private readonly Type[]? _tileObjectTypes;

    public EditorWorldControl(short worldIndex, Type[]? tileObjectTypes)
        : base(worldIndex)
    {
        _tileObjectTypes = tileObjectTypes;
        Interactive = true;
    }

    /// <summary>
    /// 只顯示焦點附近這個半徑內的物件；0 表示全部顯示。
    /// </summary>
    /// <remarks>
    /// 編輯器預設是俯視整張圖，遊戲的視錐裁切在這個視角下等於沒有裁 ——
    /// 勒瑞西亞 2833 個物件全部各一次 draw call，量到場景就要 34ms（22 fps）。
    /// 幀率低到這個程度時輸入是用輪詢的，一次正常點擊（按下到放開 60–80ms）
    /// 有機會整個落在兩次取樣之間被漏掉，介面就變成「點了沒反應／點錯地方」。
    ///
    /// 所以這不是畫面問題，是**能不能用**的問題。
    /// </remarks>
    public float ObjectDrawDistance { get; set; }

    /// <summary>依 <see cref="ObjectDrawDistance"/> 更新物件的顯示與否。相機移動後呼叫。</summary>
    public void ApplyObjectDrawDistance(Vector3 focus)
    {
        bool unlimited = ObjectDrawDistance <= 0f;
        float radiusSquared = ObjectDrawDistance * ObjectDrawDistance;

        foreach (var obj in Objects)
        {
            if (unlimited)
            {
                obj.Hidden = false;
                continue;
            }

            float dx = obj.Position.X - focus.X;
            float dy = obj.Position.Y - focus.Y;

            obj.Hidden = (dx * dx) + (dy * dy) > radiusSquared;
        }
    }

    protected override void CreateMapTileObjects()
    {
        // 先全部填成泛用的 MapTileObject，再用該 world 自己的語意類別覆蓋。
        base.CreateMapTileObjects();

        if (_tileObjectTypes is null)
            return;

        int count = Math.Min(_tileObjectTypes.Length, MapTileObjects.Length);
        for (int i = 0; i < count; i++)
        {
            if (_tileObjectTypes[i] is not null)
                MapTileObjects[i] = _tileObjectTypes[i];
        }
    }
}
