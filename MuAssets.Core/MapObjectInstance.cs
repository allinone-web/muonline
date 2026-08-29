using System.Numerics;
using Client.Data.OBJS;

namespace MuAssets.Core;

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

    // ── 語義標註 ──────────────────────────────────────────────
    //
    // MU 的 .obj 只有型別編號，說得出「這是一扇門的模型」，說不出
    // 「這是攻城戰的 3 號城門」。玩法要的是後者。
    //
    // 刻意用字串而不是列舉：角色由玩法定義，編輯器不該預先知道有哪些。
    // 命名慣例 <系統>.<角色>，例如 siege.gate、siege.statue、arena.spawn。
    //
    // 這幾個欄位不寫進 .obj（那是客戶端的格式，多寫會讀不了），
    // 只存在 map.json 裡，由伺服器端的產生器去用。

    /// <summary>語義角色；空字串表示只是布景。</summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>同一個角色的第幾個（3 號城門）。同 Role 內不得重複，見 <see cref="MapValidator"/>。</summary>
    public int RoleId { get; set; }

    /// <summary>自由標籤，給還沒定案的玩法用。</summary>
    public string[] Tags { get; set; } = [];

    /// <summary>有沒有被標註成某個角色。</summary>
    public bool HasRole => !string.IsNullOrWhiteSpace(Role);

    public int TileX => (int)(Position.X / MuConstants.TerrainScale);
    public int TileY => (int)(Position.Y / MuConstants.TerrainScale);

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

    public MapObjectInstance Clone()
    {
        var copy = (MapObjectInstance)MemberwiseClone();

        // MemberwiseClone 是淺複製，標籤陣列要另外複製一份，
        // 不然撤銷用的快照會跟著現行物件一起被改掉。
        copy.Tags = (string[])Tags.Clone();

        return copy;
    }
}
