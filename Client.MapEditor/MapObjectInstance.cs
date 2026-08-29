using System.Numerics;
using Client.Data.OBJS;

namespace Client.MapEditor;

/// <summary>
/// 地圖上的一個物件，編輯器用的可變版本。
/// </summary>
/// <remarks>
/// <c>Client.Data.OBJS</c> 的 <c>MapObjectV0..V5</c> 是 struct，放進 List 會被裝箱，
/// 每改一次就換一個新箱子 —— 編輯器需要穩定的身分才能把「文件裡的物件」對應到
/// 「畫面上的物件」，所以這裡用 class。存檔時再轉回對應版本的 struct。
/// </remarks>
public sealed class MapObjectInstance
{
    public short Type { get; set; }
    public Vector3 Position { get; set; }
    public Vector3 Angle { get; set; }
    public float Scale { get; set; } = 1f;

    // 版本專屬欄位，原樣保留才能無損寫回。
    public byte UnknownX { get; set; }
    public byte UnknownY { get; set; }
    public byte UnknownZ { get; set; }
    public Vector3 Lightning { get; set; }
    public byte UnknownByte { get; set; }
    public float UnknownFloat1 { get; set; }
    public float UnknownFloat2 { get; set; }

    public int TileX => (int)(Position.X / Client.Main.Constants.TERRAIN_SCALE);
    public int TileY => (int)(Position.Y / Client.Main.Constants.TERRAIN_SCALE);

    public static MapObjectInstance From(IMapObject source)
    {
        var instance = new MapObjectInstance
        {
            Type = source.Type,
            Position = source.Position,
            Angle = source.Angle,
            Scale = source.Scale,
        };

        switch (source)
        {
            case MapObjectV1 v1:
                (instance.UnknownX, instance.UnknownY) = (v1.UnknownX, v1.UnknownY);
                break;
            case MapObjectV2 v2:
                (instance.UnknownX, instance.UnknownY, instance.UnknownZ) = (v2.UnknownX, v2.UnknownY, v2.UnknownZ);
                break;
            case MapObjectV3 v3:
                (instance.UnknownX, instance.UnknownY, instance.UnknownZ) = (v3.UnknownX, v3.UnknownY, v3.UnknownZ);
                instance.Lightning = v3.Ligthning;
                break;
            case MapObjectV4 v4:
                (instance.UnknownX, instance.UnknownY, instance.UnknownZ) = (v4.UnknownX, v4.UnknownY, v4.UnknownZ);
                instance.Lightning = v4.Ligthning;
                instance.UnknownByte = v4.UnknownByte;
                break;
            case MapObjectV5 v5:
                (instance.UnknownX, instance.UnknownY, instance.UnknownZ) = (v5.UnknownX, v5.UnknownY, v5.UnknownZ);
                instance.Lightning = v5.Ligthning;
                instance.UnknownByte = v5.UnknownByte;
                instance.UnknownFloat1 = v5.UnknownFloat1;
                instance.UnknownFloat2 = v5.UnknownFloat2;
                break;
        }

        return instance;
    }

    public IMapObject To(byte version) => version switch
    {
        0 => new MapObjectV0 { Type = Type, Position = Position, Angle = Angle, Scale = Scale },
        1 => new MapObjectV1
        {
            Type = Type, Position = Position, Angle = Angle, Scale = Scale,
            UnknownX = UnknownX, UnknownY = UnknownY,
        },
        2 => new MapObjectV2
        {
            Type = Type, Position = Position, Angle = Angle, Scale = Scale,
            UnknownX = UnknownX, UnknownY = UnknownY, UnknownZ = UnknownZ,
        },
        3 => new MapObjectV3
        {
            Type = Type, Position = Position, Angle = Angle, Scale = Scale,
            UnknownX = UnknownX, UnknownY = UnknownY, UnknownZ = UnknownZ,
            Ligthning = Lightning,
        },
        4 => new MapObjectV4
        {
            Type = Type, Position = Position, Angle = Angle, Scale = Scale,
            UnknownX = UnknownX, UnknownY = UnknownY, UnknownZ = UnknownZ,
            Ligthning = Lightning, UnknownByte = UnknownByte,
        },
        5 => new MapObjectV5
        {
            Type = Type, Position = Position, Angle = Angle, Scale = Scale,
            UnknownX = UnknownX, UnknownY = UnknownY, UnknownZ = UnknownZ,
            Ligthning = Lightning, UnknownByte = UnknownByte,
            UnknownFloat1 = UnknownFloat1, UnknownFloat2 = UnknownFloat2,
        },
        _ => throw new NotSupportedException($"Unsupported .obj version {version}."),
    };

    public MapObjectInstance Clone() => (MapObjectInstance)MemberwiseClone();
}
