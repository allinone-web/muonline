using System.Numerics;
using Client.Data.ATT;

namespace MuAssets.Core;

/// <summary>
/// 散佈筆刷：一筆撒下一群帶隨機朝向與大小的物件。
/// </summary>
/// <remarks>
/// 草木石頭在地圖上是幾百個起跳的 —— 勒瑞西亞光是 type 24 就有 300 個。
/// 一個一個放沒有人做得下去，而且手放的東西會排得太整齊，一看就是人擺的。
///
/// 幾個刻意的取捨：
///
/// <list type="bullet">
///   <item><b>位置是連續的，不對齊格子。</b>自然物本來就不該站在格心上。</item>
///   <item><b>有最小間距。</b>沒有的話隨機會撒出一叢一叢的結塊 ——
///         隨機不等於均勻，這是泊松盤取樣要解決的老問題。這裡用最簡單的
///         「試幾次、太近就重試」，夠用而且不必額外的資料結構。</item>
///   <item><b>可以避開不可走的格子。</b>樹長在牆裡、石頭浮在水上都很難看，
///         而地圖的屬性層剛好就記著哪裡是牆、哪裡是水。</item>
/// </list>
/// </remarks>
public static class ScatterBrush
{
    /// <summary>每個位置最多試幾次。試不出來就少撒一個，不要無限找。</summary>
    private const int MaxAttemptsPerObject = 12;

    /// <summary>
    /// 在筆刷範圍內撒一批物件，回傳實際放下去的那些。
    /// </summary>
    /// <param name="existing">已經在圖上的物件，用來檢查最小間距。</param>
    public static List<MapObjectInstance> Scatter(
        ToolSettings settings, MapDocument document, int centerX, int centerY, Random random,
        IReadOnlyList<MapObjectInstance>? existing = null)
    {
        var placed = new List<MapObjectInstance>();

        int radius = Math.Max(1, settings.Brush.Radius);
        float radiusWorld = radius * MuConstants.TerrainScale;
        float spacing = Math.Max(0f, settings.ScatterSpacing) * MuConstants.TerrainScale;

        var neighbours = new List<Vector3>();

        if (spacing > 0f && existing is not null)
        {
            // 只看筆刷附近的，不必掃整張圖的幾千個物件。
            float limit = radiusWorld + spacing;
            float cx = MuConstants.TileToWorld(centerX);
            float cy = MuConstants.TileToWorld(centerY);

            foreach (var instance in existing)
            {
                float dx = instance.Position.X - cx;
                float dy = instance.Position.Y - cy;

                if ((dx * dx) + (dy * dy) <= limit * limit)
                    neighbours.Add(instance.Position);
            }
        }

        for (int i = 0; i < settings.ScatterCount; i++)
        {
            if (!TryFindSpot(settings, document, centerX, centerY, radiusWorld, spacing, random, neighbours, out var position))
                continue;

            var instance = new MapObjectInstance
            {
                Type = settings.PlaceObjectType,
                Position = position,
                Angle = new Vector3(0f, 0f, (float)(random.NextDouble() * settings.PlaceRandomYaw)),
                Scale = 1f + (float)((random.NextDouble() - 0.5) * 2.0 * settings.PlaceRandomScale),
            };

            placed.Add(instance);
            neighbours.Add(position);
        }

        return placed;
    }

    private static bool TryFindSpot(
        ToolSettings settings, MapDocument document, int centerX, int centerY,
        float radiusWorld, float spacing, Random random, List<Vector3> neighbours, out Vector3 position)
    {
        position = default;

        for (int attempt = 0; attempt < MaxAttemptsPerObject; attempt++)
        {
            // 在圓內均勻取樣：半徑取平方根，不然會全部擠在中心。
            double angle = random.NextDouble() * Math.PI * 2.0;
            double distance = Math.Sqrt(random.NextDouble()) * radiusWorld;

            float x = MuConstants.TileToWorld(centerX) + (float)(Math.Cos(angle) * distance);
            float y = MuConstants.TileToWorld(centerY) + (float)(Math.Sin(angle) * distance);

            int tileX = MuConstants.WorldToTile(x);
            int tileY = MuConstants.WorldToTile(y);

            if ((uint)tileX >= MapDocument.Size || (uint)tileY >= MapDocument.Size)
                continue;

            if (settings.ScatterAvoidBlocked)
            {
                var flags = document.Attributes[(tileY * MapDocument.Size) + tileX];

                if ((flags & (TWFlags.NoMove | TWFlags.NoGround | TWFlags.Water)) != 0)
                    continue;
            }

            var candidate = new Vector3(
                x, y, document.HeightAt((tileY * MapDocument.Size) + tileX) * MuConstants.HeightScale);

            if (spacing > 0f && neighbours.Any(n =>
                    ((n.X - x) * (n.X - x)) + ((n.Y - y) * (n.Y - y)) < spacing * spacing))
            {
                continue;
            }

            position = candidate;
            return true;
        }

        return false;
    }
}
