using Client.Main;
using Client.Main.Controls;
using Microsoft.Xna.Framework;

namespace Client.MapEditor;

/// <summary>滑鼠射線打到地形的結果。</summary>
public readonly record struct TerrainHit(bool Valid, int TileX, int TileY, Vector3 World, float Height);

/// <summary>
/// 把滑鼠射線打到地形上，換算成格子座標。
/// </summary>
/// <remarks>
/// <c>WalkableWorldControl.CalculateMouseTilePos</c> 做的是同一件事，但那條路綁著玩家與游標物件，
/// 編輯器用不了（見 <see cref="EditorWorldControl"/> 的說明）。
///
/// 作法：先和 Z=0 平面求交得到起始猜測，再用該點的地形高度重新求交，迭代幾次收斂。
/// 地形起伏遠小於相機距離，四次就夠。
/// </remarks>
public static class TerrainPicker
{
    private const float TileScale = Constants.TERRAIN_SCALE;
    private const int MaxTile = Constants.TERRAIN_SIZE - 1;
    private const int RefineIterations = 4;

    public static TerrainHit Pick(WorldControl? world, Ray ray)
    {
        if (world?.Terrain is null)
            return default;

        float height = 0f;
        Vector3 point = default;

        for (int i = 0; i <= RefineIterations; i++)
        {
            if (!IntersectHorizontalPlane(ray, height, out point))
                return default;

            height = world.Terrain.RequestTerrainHeight(point.X, point.Y);
        }

        int tileX = (int)MathF.Floor(point.X / TileScale);
        int tileY = (int)MathF.Floor(point.Y / TileScale);

        if (tileX < 0 || tileY < 0 || tileX > MaxTile || tileY > MaxTile)
            return default;

        return new TerrainHit(true, tileX, tileY, point, height);
    }

    private static bool IntersectHorizontalPlane(Ray ray, float z, out Vector3 point)
    {
        point = default;

        // 射線幾乎與平面平行時交點會飛到無限遠，直接視為沒打中。
        if (MathF.Abs(ray.Direction.Z) < 1e-5f)
            return false;

        float distance = (z - ray.Position.Z) / ray.Direction.Z;
        if (distance < 0f)
            return false;

        point = ray.Position + (ray.Direction * distance);
        return true;
    }
}
