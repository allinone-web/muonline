using System.Numerics;
using System.Text.Json.Serialization;
using Client.Data.OBJS;
using Client.Data.OZB;

namespace MuAssets.Core;

/// <summary>
/// Engine-neutral authoring schema for <c>map.json</c>.
/// Legacy MU byte limits are deliberately not represented by these property types.
/// </summary>
public sealed class MapProject
{
    public int WorldIndex { get; set; }

    public byte MapVersion { get; set; }
    public int MapNumber { get; set; }

    public byte AttVersion { get; set; }
    public int AttIndex { get; set; }

    public byte ObjVersion { get; set; }
    public int ObjMapNumber { get; set; }
    public List<MapProjectObject> Objects { get; set; } = [];
    public List<SpawnArea> Spawns { get; set; } = [];

    public byte HeightVersion { get; set; }
    public string HeightFileType { get; set; } = OZBFileType.BM8;
    public string? HeightHeaderBase64 { get; set; }
    public byte LightVersion { get; set; }
    public string LightFileType { get; set; } = OZBFileType.BM6;
    public string? LightHeaderBase64 { get; set; }
}

public sealed class MapProjectObject
{
    public short Type { get; set; }
    public float[] Position { get; set; } = [0, 0, 0];
    public float[] Angle { get; set; } = [0, 0, 0];
    public float Scale { get; set; } = 1f;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public byte? UnknownX { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public byte? UnknownY { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public byte? UnknownZ { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float[]? Lightning { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public byte? UnknownByte { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? UnknownFloat1 { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? UnknownFloat2 { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Role { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? RoleId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? Tags { get; set; }

    public static MapProjectObject From(MapObjectInstance instance) => new()
    {
        Type = instance.Type,
        Position = [instance.Position.X, instance.Position.Y, instance.Position.Z],
        Angle = [instance.Angle.X, instance.Angle.Y, instance.Angle.Z],
        Scale = instance.Scale,
        UnknownX = instance.UnknownX,
        UnknownY = instance.UnknownY,
        UnknownZ = instance.UnknownZ,
        Lightning = [instance.Lightning.X, instance.Lightning.Y, instance.Lightning.Z],
        UnknownByte = instance.UnknownByte,
        UnknownFloat1 = instance.UnknownFloat1,
        UnknownFloat2 = instance.UnknownFloat2,
        Role = instance.HasRole ? instance.Role : null,
        RoleId = instance.RoleId == 0 ? null : instance.RoleId,
        Tags = instance.Tags.Length == 0 ? null : instance.Tags,
    };

    public static MapProjectObject From(IMapObject value)
    {
        var record = new MapProjectObject
        {
            Type = value.Type,
            Position = [value.Position.X, value.Position.Y, value.Position.Z],
            Angle = [value.Angle.X, value.Angle.Y, value.Angle.Z],
            Scale = value.Scale,
        };

        switch (value)
        {
            case MapObjectV1 v1:
                (record.UnknownX, record.UnknownY) = (v1.UnknownX, v1.UnknownY);
                break;
            case MapObjectV2 v2:
                (record.UnknownX, record.UnknownY, record.UnknownZ) = (v2.UnknownX, v2.UnknownY, v2.UnknownZ);
                break;
            case MapObjectV3 v3:
                (record.UnknownX, record.UnknownY, record.UnknownZ) = (v3.UnknownX, v3.UnknownY, v3.UnknownZ);
                record.Lightning = [v3.Ligthning.X, v3.Ligthning.Y, v3.Ligthning.Z];
                break;
            case MapObjectV4 v4:
                (record.UnknownX, record.UnknownY, record.UnknownZ) = (v4.UnknownX, v4.UnknownY, v4.UnknownZ);
                record.Lightning = [v4.Ligthning.X, v4.Ligthning.Y, v4.Ligthning.Z];
                record.UnknownByte = v4.UnknownByte;
                break;
            case MapObjectV5 v5:
                (record.UnknownX, record.UnknownY, record.UnknownZ) = (v5.UnknownX, v5.UnknownY, v5.UnknownZ);
                record.Lightning = [v5.Ligthning.X, v5.Ligthning.Y, v5.Ligthning.Z];
                record.UnknownByte = v5.UnknownByte;
                record.UnknownFloat1 = v5.UnknownFloat1;
                record.UnknownFloat2 = v5.UnknownFloat2;
                break;
        }

        return record;
    }

    public MapObjectInstance ToDocumentObject() => new()
    {
        Type = Type,
        Position = Vector(Position, nameof(Position)),
        Angle = Vector(Angle, nameof(Angle)),
        Scale = Scale,
        UnknownX = UnknownX ?? 0,
        UnknownY = UnknownY ?? 0,
        UnknownZ = UnknownZ ?? 0,
        Lightning = Lightning is null ? default : Vector(Lightning, nameof(Lightning)),
        UnknownByte = UnknownByte ?? 0,
        UnknownFloat1 = UnknownFloat1 ?? 0f,
        UnknownFloat2 = UnknownFloat2 ?? 0f,
        Role = Role ?? string.Empty,
        RoleId = RoleId ?? 0,
        Tags = Tags ?? [],
    };

    public IMapObject ToLegacyObject(byte version)
    {
        Vector3 position = Vector(Position, nameof(Position));
        Vector3 angle = Vector(Angle, nameof(Angle));
        Vector3 lightning = Lightning is null ? Vector3.Zero : Vector(Lightning, nameof(Lightning));

        return version switch
        {
            0 => new MapObjectV0 { Type = Type, Position = position, Angle = angle, Scale = Scale },
            1 => new MapObjectV1 { Type = Type, Position = position, Angle = angle, Scale = Scale, UnknownX = UnknownX ?? 0, UnknownY = UnknownY ?? 0 },
            2 => new MapObjectV2 { Type = Type, Position = position, Angle = angle, Scale = Scale, UnknownX = UnknownX ?? 0, UnknownY = UnknownY ?? 0, UnknownZ = UnknownZ ?? 0 },
            3 => new MapObjectV3 { Type = Type, Position = position, Angle = angle, Scale = Scale, UnknownX = UnknownX ?? 0, UnknownY = UnknownY ?? 0, UnknownZ = UnknownZ ?? 0, Ligthning = lightning },
            4 => new MapObjectV4 { Type = Type, Position = position, Angle = angle, Scale = Scale, UnknownX = UnknownX ?? 0, UnknownY = UnknownY ?? 0, UnknownZ = UnknownZ ?? 0, Ligthning = lightning, UnknownByte = UnknownByte ?? 0 },
            5 => new MapObjectV5 { Type = Type, Position = position, Angle = angle, Scale = Scale, UnknownX = UnknownX ?? 0, UnknownY = UnknownY ?? 0, UnknownZ = UnknownZ ?? 0, Ligthning = lightning, UnknownByte = UnknownByte ?? 0, UnknownFloat1 = UnknownFloat1 ?? 0f, UnknownFloat2 = UnknownFloat2 ?? 0f },
            _ => throw new InvalidDataException($"不支援 .obj version {version}；只允許 0..5。"),
        };
    }

    private Vector3 Vector(float[] values, string field)
    {
        if (values.Length != 3 || values.Any(v => !float.IsFinite(v)))
            throw new InvalidDataException($"物件 {Type} 的 {field} 必須是三個有限數值。");

        return new Vector3(values[0], values[1], values[2]);
    }
}
