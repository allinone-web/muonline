using Client.Data.ATT;
using Client.Data.MAP;

namespace MuAssets.Core;

public enum EditorToolKind
{
    None,
    PaintLayer1,
    PaintLayer2,
    PaintAlpha,
    SculptHeight,
    PaintAttribute,
    PlaceObject,
    SelectObject,
    SpawnArea,
}

public enum HeightMode
{
    Raise,
    Lower,
    Smooth,
    Flatten,
}

/// <summary>
/// 一次筆劃的執行者。工具本身不持有狀態，狀態都在 <see cref="EditorSession"/>。
/// </summary>
/// <remarks>
/// 所有工具都改 <see cref="MapDocument"/>，改完由呼叫端把整份資料推進渲染端
/// （<c>TerrainControl.ApplyEditedTerrain</c>）。渲染端是靠參考identity判斷要不要重建，
/// 所以不能就地改渲染端的陣列。
/// </remarks>
public static class EditorTools
{
    public static EditTarget TargetOf(EditorToolKind kind) => kind switch
    {
        EditorToolKind.PaintLayer1 => EditTarget.Layer1,
        EditorToolKind.PaintLayer2 => EditTarget.Layer2,
        EditorToolKind.PaintAlpha => EditTarget.Alpha,
        EditorToolKind.SculptHeight => EditTarget.Height,
        EditorToolKind.PaintAttribute => EditTarget.Attribute,
        _ => EditTarget.Layer1,
    };

    public static string DescriptionOf(EditorToolKind kind) => kind switch
    {
        EditorToolKind.PaintLayer1 => "繪製第一層",
        EditorToolKind.PaintLayer2 => "繪製第二層",
        EditorToolKind.PaintAlpha => "繪製混合",
        EditorToolKind.SculptHeight => "雕刻高度",
        EditorToolKind.PaintAttribute => "繪製屬性",
        EditorToolKind.PlaceObject => "放置物件",
        EditorToolKind.SelectObject => "選取物件",
        EditorToolKind.SpawnArea => "生怪區",
        _ => "編輯",
    };

    /// <summary>對一格施加一次筆刷。回傳這一次是否真的改到東西。</summary>
    public static void Apply(ToolSettings session, MapDocument document, EditStroke stroke, int centerX, int centerY)
    {
        var brush = session.Brush;

        switch (session.Tool)
        {
            case EditorToolKind.PaintLayer1:
                PaintIndex(brush, document.Layer1, stroke, centerX, centerY, session.PaintTileIndex);
                break;

            case EditorToolKind.PaintLayer2:
                PaintIndex(brush, document.Layer2, stroke, centerX, centerY,
                    session.PaintLayer2AsEmpty ? TerrainTextureMapping.NoLayerIndex : session.PaintTileIndex);
                break;

            case EditorToolKind.PaintAlpha:
                PaintAlpha(session, document, stroke, centerX, centerY);
                break;

            case EditorToolKind.SculptHeight:
                SculptHeight(session, document, stroke, centerX, centerY);
                break;

            case EditorToolKind.PaintAttribute:
                PaintAttribute(session, document, stroke, centerX, centerY);
                break;
        }
    }

    /// <summary>貼圖索引是離散值，不做插值 —— 權重只當成「這格算不算在筆刷內」。</summary>
    private static void PaintIndex(Brush brush, byte[] target, EditStroke stroke, int centerX, int centerY, byte value)
    {
        brush.ForEachCell(centerX, centerY, (x, y, weight) =>
        {
            if (weight <= 0f)
                return;

            int index = (y * MuConstants.TerrainSize) + x;
            stroke.Record(index, target[index], value);
            target[index] = value;
        });
    }

    /// <summary>
    /// 混合值是連續的，往目標值逼近。Alpha 決定第二層蓋過第一層多少，
    /// 是 MU 地形過渡的關鍵。
    /// </summary>
    private static void PaintAlpha(ToolSettings session, MapDocument document, EditStroke stroke, int centerX, int centerY)
    {
        var brush = session.Brush;
        float target = session.PaintAlphaValue;

        brush.ForEachCell(centerX, centerY, (x, y, weight) =>
        {
            int index = (y * MuConstants.TerrainSize) + x;
            byte before = document.Alpha[index];

            float blended = before + ((target - before) * weight * brush.Strength);
            byte after = (byte)Math.Clamp(MathF.Round(blended), 0f, 255f);

            stroke.Record(index, before, after);
            document.Alpha[index] = after;
        });
    }

    private static void SculptHeight(ToolSettings session, MapDocument document, EditStroke stroke, int centerX, int centerY)
    {
        var data = document.Height?.Data;
        if (data is null)
            return;

        var brush = session.Brush;

        // 平滑與壓平需要先知道鄰域的樣子，所以先取一份基準值。
        float reference = session.HeightMode switch
        {
            HeightMode.Flatten => session.FlattenTarget,
            HeightMode.Smooth => AverageHeight(document, brush, centerX, centerY),
            _ => 0f,
        };

        brush.ForEachCell(centerX, centerY, (x, y, weight) =>
        {
            int index = (y * MuConstants.TerrainSize) + x;
            if (index >= data.Length)
                return;

            byte before = data[index].R;
            float amount = weight * brush.Strength;

            float after = session.HeightMode switch
            {
                HeightMode.Raise => before + (amount * session.HeightStep),
                HeightMode.Lower => before - (amount * session.HeightStep),
                HeightMode.Smooth => before + ((AverageHeight(document, brush, x, y) - before) * amount),
                _ => before + ((reference - before) * amount),
            };

            byte clamped = (byte)Math.Clamp(MathF.Round(after), 0f, 255f);
            stroke.Record(index, before, clamped);
            data[index] = System.Drawing.Color.FromArgb(255, clamped, 0, 0);
        });
    }

    /// <summary>取 3×3 鄰域平均，供平滑用。</summary>
    private static float AverageHeight(MapDocument document, Brush brush, int centerX, int centerY)
    {
        float sum = 0f;
        int count = 0;

        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int x = centerX + dx;
                int y = centerY + dy;

                if ((uint)x >= MuConstants.TerrainSize || (uint)y >= MuConstants.TerrainSize)
                    continue;

                sum += document.HeightAt((y * MuConstants.TerrainSize) + x);
                count++;
            }
        }

        return count > 0 ? sum / count : 0f;
    }

    private static void PaintAttribute(ToolSettings session, MapDocument document, EditStroke stroke, int centerX, int centerY)
    {
        var brush = session.Brush;
        var flag = session.AttributeFlag;

        brush.ForEachCell(centerX, centerY, (x, y, weight) =>
        {
            if (weight <= 0f)
                return;

            int index = (y * MuConstants.TerrainSize) + x;
            var before = document.Attributes[index];

            var after = session.AttributeErase
                ? before & ~flag
                : before | flag;

            stroke.Record(index, (int)before, (int)after);
            document.Attributes[index] = after;
        });
    }

}

/// <summary>
/// 吸管：把游標下那一格的值吸回筆刷設定。
/// </summary>
/// <remarks>
/// 吸什麼取決於目前是哪支筆 —— 畫第一層就吸第一層的貼圖索引，
/// 畫高度就吸高度。這與 Photoshop、Tiled 的行為一致：
/// 吸管不是獨立的模式，是「用目前這支筆去取樣」。
///
/// 沒有它的話，畫一片混合地形要在面板與畫面之間來回幾十次 ——
/// 看到一塊想用的地面，得先猜它是哪個索引，再去清單裡找。
/// </remarks>
public static class Eyedropper
{
    /// <summary>吸一格。回傳給使用者看的描述；這支筆沒有可吸的東西時回 null。</summary>
    public static string? Pick(ToolSettings settings, MapDocument document, int tileX, int tileY)
    {
        if ((uint)tileX >= MapDocument.Size || (uint)tileY >= MapDocument.Size)
            return null;

        int index = (tileY * MapDocument.Size) + tileX;

        switch (settings.Tool)
        {
            case EditorToolKind.PaintLayer1:
                settings.PaintTileIndex = document.Layer1[index];
                return $"吸到第一層索引 {settings.PaintTileIndex}";

            case EditorToolKind.PaintLayer2:
            {
                byte value = document.Layer2[index];

                // 255 是「這一格沒有第二層」的哨兵值，不是索引。
                settings.PaintLayer2AsEmpty = value == TerrainTextureMapping.NoLayerIndex;

                if (!settings.PaintLayer2AsEmpty)
                    settings.PaintTileIndex = value;

                return settings.PaintLayer2AsEmpty ? "吸到第二層：無" : $"吸到第二層索引 {value}";
            }

            case EditorToolKind.PaintAlpha:
                settings.PaintAlphaValue = document.Alpha[index];
                return $"吸到混合值 {settings.PaintAlphaValue:F0}";

            case EditorToolKind.SculptHeight:
                // 高度筆刷吸到的是「壓平的目標高度」—— 吸一格地面，
                // 再用壓平模式把旁邊抹到同一高度，這是最常見的用法。
                settings.FlattenTarget = document.HeightAt(index);
                settings.HeightMode = HeightMode.Flatten;
                return $"吸到高度 {settings.FlattenTarget:F0}，已切到壓平模式";

            case EditorToolKind.PaintAttribute:
            {
                var flags = document.Attributes[index];

                if (flags == 0)
                    return "這一格沒有任何屬性";

                // 一格可能有多個旗標，吸最低的那一個 —— 屬性筆刷一次只畫一種。
                foreach (var candidate in new[]
                {
                    Client.Data.ATT.TWFlags.SafeZone,
                    Client.Data.ATT.TWFlags.Character,
                    Client.Data.ATT.TWFlags.NoMove,
                    Client.Data.ATT.TWFlags.NoGround,
                    Client.Data.ATT.TWFlags.Water,
                    Client.Data.ATT.TWFlags.Action,
                    Client.Data.ATT.TWFlags.Height,
                    Client.Data.ATT.TWFlags.CameraUp,
                })
                {
                    if ((flags & candidate) != 0)
                    {
                        settings.AttributeFlag = candidate;
                        settings.AttributeErase = false;
                        return $"吸到屬性 {candidate}";
                    }
                }

                return "這一格的屬性不在已知的旗標裡";
            }

            default:
                return null;
        }
    }
}
