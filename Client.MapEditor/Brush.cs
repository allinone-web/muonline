namespace Client.MapEditor;

public enum BrushShape
{
    Point,
    Square,
    Circle,
}

/// <summary>
/// 筆刷的形狀與強度。所有格子類工具共用。
/// </summary>
public sealed class Brush
{
    public BrushShape Shape { get; set; } = BrushShape.Circle;

    /// <summary>半徑，單位是格。0 等於只有中心那一格。</summary>
    public int Radius { get; set; } = 3;

    /// <summary>0–1。連續類工具（高度、混合）用它當每次施加的比例。</summary>
    public float Strength { get; set; } = 0.5f;

    /// <summary>0 = 整個筆刷等強度；1 = 從中心到邊緣線性衰減到 0。</summary>
    public float Falloff { get; set; } = 0.6f;

    /// <summary>
    /// 走訪筆刷覆蓋到的格子，回呼帶上該格的權重（0–1，已含衰減）。
    /// </summary>
    public void ForEachCell(int centerX, int centerY, Action<int, int, float> action)
    {
        if (Shape == BrushShape.Point)
        {
            if (InBounds(centerX, centerY))
                action(centerX, centerY, 1f);

            return;
        }

        int radius = Math.Max(0, Radius);

        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                int x = centerX + dx;
                int y = centerY + dy;

                if (!InBounds(x, y))
                    continue;

                float weight = Weight(dx, dy, radius);
                if (weight > 0f)
                    action(x, y, weight);
            }
        }
    }

    private float Weight(int dx, int dy, int radius)
    {
        if (radius == 0)
            return dx == 0 && dy == 0 ? 1f : 0f;

        float normalized = Shape == BrushShape.Circle
            ? MathF.Sqrt((dx * dx) + (dy * dy)) / radius
            : MathF.Max(MathF.Abs(dx), MathF.Abs(dy)) / (float)radius;

        if (Shape == BrushShape.Circle && normalized > 1f)
            return 0f;

        // Falloff = 0 時整片等強度；= 1 時邊緣衰減到 0。
        return Math.Clamp(1f - (normalized * Falloff), 0f, 1f);
    }

    private static bool InBounds(int x, int y)
        => (uint)x < MapDocument.Size && (uint)y < MapDocument.Size;
}
