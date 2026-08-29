namespace MuAssets.Core;

/// <summary>與 OpenMU 的 <c>SpawnTrigger</c> 逐項對應。</summary>
public enum SpawnTrigger
{
    Automatic,
    AutomaticDuringEvent,
    OnceAtEventStart,
    AutomaticDuringWave,
    OnceAtWaveStart,
    ManuallyForEvent,
    Wandering,
}

/// <summary>與 OpenMU 的 <c>Direction</c> 逐項對應。</summary>
public enum SpawnDirection
{
    Undefined,
    West,
    SouthWest,
    South,
    SouthEast,
    East,
    NorthEast,
    North,
    NorthWest,
}

/// <summary>
/// 一個生怪／NPC 區域。
/// </summary>
/// <remarks>
/// 欄位刻意對齊 OpenMU 的 <c>MonsterSpawnArea</c>（<c>X1/Y1/X2/Y2</c>、<c>Quantity</c>、
/// <c>Direction</c>、<c>SpawnTrigger</c>），匯出時才不需要轉換。
///
/// <b>只存編號不存 MonsterDefinition</b>：長期目標是 Lineage 與 MU 融合，
/// 角色與怪物來源會不只一種，寫死 OpenMU 的型別會擋住那條路。
/// </remarks>
public sealed class SpawnArea
{
    /// <summary>怪物／NPC 的編號（客戶端 <c>[NpcInfo]</c> 的 TypeId，與 OpenMU 的 Number 相同）。</summary>
    public ushort TypeId { get; set; }

    public byte X1 { get; set; }
    public byte Y1 { get; set; }
    public byte X2 { get; set; }
    public byte Y2 { get; set; }

    public short Quantity { get; set; } = 1;
    public SpawnDirection Direction { get; set; } = SpawnDirection.Undefined;
    public SpawnTrigger Trigger { get; set; } = SpawnTrigger.Automatic;

    /// <summary>顯示用的名稱，從目錄帶進來，方便在清單上辨認。</summary>
    public string Name { get; set; } = string.Empty;

    public bool IsPoint => X1 == X2 && Y1 == Y2;

    public int Width => X2 - X1 + 1;
    public int Height => Y2 - Y1 + 1;

    public SpawnArea Clone() => (SpawnArea)MemberwiseClone();

    /// <summary>把兩個角落正規化成左上／右下。</summary>
    public static SpawnArea FromCorners(int ax, int ay, int bx, int by) => new()
    {
        X1 = (byte)Math.Clamp(Math.Min(ax, bx), 0, 255),
        Y1 = (byte)Math.Clamp(Math.Min(ay, by), 0, 255),
        X2 = (byte)Math.Clamp(Math.Max(ax, bx), 0, 255),
        Y2 = (byte)Math.Clamp(Math.Max(ay, by), 0, 255),
    };
}
