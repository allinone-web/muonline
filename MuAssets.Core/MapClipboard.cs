using System.Numerics;
using Client.Data.ATT;

namespace MuAssets.Core;

/// <summary>
/// 剪貼簿裡的一塊地圖：逐格資料 + 落在範圍內的物件。
/// </summary>
/// <remarks>
/// 物件的座標存成<b>相對於區塊左上角</b>的世界座標，貼上時再加回去。
/// 存絕對座標的話貼到別處就得逐一換算，而且沒辦法貼到另一張圖上。
/// </remarks>
public sealed class MapBlock
{
    public required int Width { get; init; }
    public required int Height { get; init; }

    public required byte[] Layer1 { get; init; }
    public required byte[] Layer2 { get; init; }
    public required byte[] Alpha { get; init; }
    public required TWFlags[] Attributes { get; init; }
    public required byte[] TerrainHeight { get; init; }
    public required int[] Light { get; init; }

    /// <summary>範圍內的物件，位置是相對於區塊左上角的世界座標。</summary>
    public required List<MapObjectInstance> Objects { get; init; }

    public int CellCount => Width * Height;
}

/// <summary>
/// 區塊複製貼上。
/// </summary>
/// <remarks>
/// 蓋好一間房子就能複製整條街 —— 這是把「畫地圖」從逐格勞動變成組裝的關鍵一步。
///
/// 一次貼上會同時改地形的五種資料與物件清單，所以：
/// 逐格的部分收成<b>一筆多目標筆劃</b>（見 <see cref="EditStroke"/>），
/// 物件的部分收成<b>一筆批次</b>（見 <see cref="ObjectEdit.Batch"/>）。
/// 兩邊的歷史本來就是分開的，所以貼上會是兩次撤銷 —— 這一點有寫在狀態列上，
/// 不然使用者按一次撤銷會看到地形回去了、物件還在。
/// </remarks>
public static class MapClipboard
{
    /// <summary>複製一個矩形範圍（格子座標，兩個角落任意順序）。</summary>
    public static MapBlock Copy(MapDocument document, int ax, int ay, int bx, int by, bool includeObjects = true)
    {
        int minX = Math.Clamp(Math.Min(ax, bx), 0, MapDocument.Size - 1);
        int maxX = Math.Clamp(Math.Max(ax, bx), 0, MapDocument.Size - 1);
        int minY = Math.Clamp(Math.Min(ay, by), 0, MapDocument.Size - 1);
        int maxY = Math.Clamp(Math.Max(ay, by), 0, MapDocument.Size - 1);

        int width = maxX - minX + 1;
        int height = maxY - minY + 1;
        int cells = width * height;

        var block = new MapBlock
        {
            Width = width,
            Height = height,
            Layer1 = new byte[cells],
            Layer2 = new byte[cells],
            Alpha = new byte[cells],
            Attributes = new TWFlags[cells],
            TerrainHeight = new byte[cells],
            Light = new int[cells],
            Objects = [],
        };

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int source = ((minY + y) * MapDocument.Size) + minX + x;
                int target = (y * width) + x;

                block.Layer1[target] = document.Layer1[source];
                block.Layer2[target] = document.Layer2[source];
                block.Alpha[target] = document.Alpha[source];
                block.Attributes[target] = document.Attributes[source];
                block.TerrainHeight[target] = document.HeightAt(source);

                var light = document.LightAt(source);
                block.Light[target] = EditStroke.PackLight(light.R, light.G, light.B);
            }
        }

        if (!includeObjects)
            return block;

        float originX = minX * MuConstants.TerrainScale;
        float originY = minY * MuConstants.TerrainScale;

        foreach (var instance in document.Objects)
        {
            if (instance.TileX < minX || instance.TileX > maxX
                || instance.TileY < minY || instance.TileY > maxY)
            {
                continue;
            }

            var copy = instance.Clone();
            copy.Position = new Vector3(
                instance.Position.X - originX,
                instance.Position.Y - originY,
                instance.Position.Z);

            block.Objects.Add(copy);
        }

        return block;
    }

    /// <summary>
    /// 把區塊貼到某個位置（左上角對齊該格），逐格的變動記進 <paramref name="stroke"/>。
    /// </summary>
    /// <returns>實際貼上的物件（呼叫端負責加進文件與歷史）。</returns>
    public static List<MapObjectInstance> Paste(
        MapDocument document, MapBlock block, int tileX, int tileY, EditStroke stroke, bool includeObjects = true)
    {
        for (int y = 0; y < block.Height; y++)
        {
            int targetY = tileY + y;
            if ((uint)targetY >= MapDocument.Size)
                continue;

            for (int x = 0; x < block.Width; x++)
            {
                int targetX = tileX + x;
                if ((uint)targetX >= MapDocument.Size)
                    continue;

                int source = (y * block.Width) + x;
                int target = (targetY * MapDocument.Size) + targetX;

                Write(stroke, document, target, EditTarget.Layer1, block.Layer1[source]);
                Write(stroke, document, target, EditTarget.Layer2, block.Layer2[source]);
                Write(stroke, document, target, EditTarget.Alpha, block.Alpha[source]);
                Write(stroke, document, target, EditTarget.Attribute, (int)block.Attributes[source]);
                Write(stroke, document, target, EditTarget.Height, block.TerrainHeight[source]);
                Write(stroke, document, target, EditTarget.Light, block.Light[source]);
            }
        }

        if (!includeObjects)
            return [];

        float originX = tileX * MuConstants.TerrainScale;
        float originY = tileY * MuConstants.TerrainScale;
        var pasted = new List<MapObjectInstance>(block.Objects.Count);

        foreach (var template in block.Objects)
        {
            var instance = template.Clone();
            instance.Position = new Vector3(
                template.Position.X + originX,
                template.Position.Y + originY,
                template.Position.Z);

            // 貼到別的地方時地形高度不一樣，貼齊過去才不會浮空或埋住。
            int cell = (Math.Clamp(instance.TileY, 0, MapDocument.Size - 1) * MapDocument.Size)
                     + Math.Clamp(instance.TileX, 0, MapDocument.Size - 1);

            instance.Position = instance.Position with
            {
                Z = document.HeightAt(cell) * MuConstants.HeightScale,
            };

            pasted.Add(instance);
        }

        return pasted;
    }

    private static void Write(EditStroke stroke, MapDocument document, int index, EditTarget target, int value)
    {
        int before = target switch
        {
            EditTarget.Layer1 => document.Layer1[index],
            EditTarget.Layer2 => document.Layer2[index],
            EditTarget.Alpha => document.Alpha[index],
            EditTarget.Attribute => (int)document.Attributes[index],
            EditTarget.Height => document.HeightAt(index),
            _ => Pack(document.LightAt(index)),
        };

        if (before == value)
            return;

        stroke.Record(target, index, before, value);

        switch (target)
        {
            case EditTarget.Layer1: document.Layer1[index] = (byte)value; break;
            case EditTarget.Layer2: document.Layer2[index] = (byte)value; break;
            case EditTarget.Alpha: document.Alpha[index] = (byte)value; break;
            case EditTarget.Attribute: document.Attributes[index] = (TWFlags)value; break;

            case EditTarget.Height:
                if (document.Height?.Data is { } height && index < height.Length)
                    height[index] = System.Drawing.Color.FromArgb(255, (byte)value, 0, 0);
                break;

            default:
                if (document.Light?.Data is { } light && index < light.Length)
                {
                    light[index] = System.Drawing.Color.FromArgb(
                        255, (value >> 16) & 0xFF, (value >> 8) & 0xFF, value & 0xFF);
                }

                break;
        }
    }

    private static int Pack(System.Drawing.Color color) => EditStroke.PackLight(color.R, color.G, color.B);
}
