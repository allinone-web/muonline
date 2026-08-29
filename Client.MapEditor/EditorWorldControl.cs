using Client.Main.Controls;
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
