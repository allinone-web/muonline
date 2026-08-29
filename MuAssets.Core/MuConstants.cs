namespace MuAssets.Core;

/// <summary>
/// MU 地圖的格式常數。
/// </summary>
/// <remarks>
/// 這些值在 <c>Client.Main.Constants</c> 也有一份，但那是引擎那一側的。
/// 這裡重新宣告是刻意的：<b>它們是格式的一部分，不是引擎的一部分</b>，
/// Core 不能為了兩個常數就去依賴客戶端。
/// </remarks>
public static class MuConstants
{
    /// <summary>地形一邊的格數。地圖固定 256×256。</summary>
    public const int TerrainSize = 256;

    /// <summary>一格地形的世界單位長度。</summary>
    public const float TerrainScale = 100f;

    /// <summary>高度圖的值域是 0–255，渲染時乘上這個係數。</summary>
    public const float HeightScale = 1.5f;

    public const int CellCount = TerrainSize * TerrainSize;

    /// <summary>格子座標轉世界座標（取格子中心）。</summary>
    public static float TileToWorld(int tile) => (tile + 0.5f) * TerrainScale;

    /// <summary>世界座標轉格子座標。</summary>
    public static int WorldToTile(float world) => (int)(world / TerrainScale);
}
