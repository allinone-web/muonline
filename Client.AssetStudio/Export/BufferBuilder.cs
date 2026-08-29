using System.Numerics;

namespace Client.AssetStudio.Export;

/// <summary>
/// 累積 glTF 的二進位緩衝，並同步登記 bufferView 與 accessor。
/// </summary>
/// <remarks>
/// 每一段資料各自一個 bufferView，起點對齊 4 個位元組。
/// 規格要求 accessor 在 buffer 裡的位移必須是元件大小的倍數；
/// 共用 bufferView 再靠 byteStride 交錯雖然檔案更小，但這裡的資料量（一隻怪幾千個頂點）
/// 完全不值得為此多一層會算錯的偏移量。
/// </remarks>
internal sealed class BufferBuilder
{
    private readonly MemoryStream _stream = new();

    public byte[] ToArray() => _stream.ToArray();

    public int AddVec3(GltfRoot root, IReadOnlyList<Vector3> values, int? target, bool withBounds)
    {
        int offset = Begin();
        var writer = new BinaryWriter(_stream);

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);

        foreach (var value in values)
        {
            writer.Write(value.X);
            writer.Write(value.Y);
            writer.Write(value.Z);

            min = Vector3.Min(min, value);
            max = Vector3.Max(max, value);
        }

        int view = AddView(root, offset, values.Count * 12, target);

        return AddAccessor(root, view, ComponentType.Float, values.Count, "VEC3",
            withBounds && values.Count > 0 ? [min.X, min.Y, min.Z] : null,
            withBounds && values.Count > 0 ? [max.X, max.Y, max.Z] : null);
    }

    public int AddVec2(GltfRoot root, IReadOnlyList<Vector2> values, int? target)
    {
        int offset = Begin();
        var writer = new BinaryWriter(_stream);

        foreach (var value in values)
        {
            writer.Write(value.X);
            writer.Write(value.Y);
        }

        int view = AddView(root, offset, values.Count * 8, target);
        return AddAccessor(root, view, ComponentType.Float, values.Count, "VEC2", null, null);
    }

    public int AddVec4(GltfRoot root, IReadOnlyList<Vector4> values)
    {
        int offset = Begin();
        var writer = new BinaryWriter(_stream);

        foreach (var value in values)
        {
            writer.Write(value.X);
            writer.Write(value.Y);
            writer.Write(value.Z);
            writer.Write(value.W);
        }

        int view = AddView(root, offset, values.Count * 16, target: null);
        return AddAccessor(root, view, ComponentType.Float, values.Count, "VEC4", null, null);
    }

    public int AddScalarFloat(GltfRoot root, IReadOnlyList<float> values, bool withBounds)
    {
        int offset = Begin();
        var writer = new BinaryWriter(_stream);

        float min = float.MaxValue;
        float max = float.MinValue;

        foreach (var value in values)
        {
            writer.Write(value);
            min = MathF.Min(min, value);
            max = MathF.Max(max, value);
        }

        int view = AddView(root, offset, values.Count * 4, target: null);

        return AddAccessor(root, view, ComponentType.Float, values.Count, "SCALAR",
            withBounds && values.Count > 0 ? [min] : null,
            withBounds && values.Count > 0 ? [max] : null);
    }

    public int AddScalarUInt(GltfRoot root, IReadOnlyList<uint> values, int? target)
    {
        int offset = Begin();
        var writer = new BinaryWriter(_stream);

        foreach (var value in values)
            writer.Write(value);

        int view = AddView(root, offset, values.Count * 4, target);
        return AddAccessor(root, view, ComponentType.UnsignedInt, values.Count, "SCALAR", null, null);
    }

    /// <summary>JOINTS_0：單骨綁定，所以只有第一個分量有值。</summary>
    public int AddJoints(GltfRoot root, IReadOnlyList<ushort> values)
    {
        int offset = Begin();
        var writer = new BinaryWriter(_stream);

        foreach (var value in values)
        {
            writer.Write(value);
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            writer.Write((ushort)0);
        }

        int view = AddView(root, offset, values.Count * 8, 34962);
        return AddAccessor(root, view, ComponentType.UnsignedShort, values.Count, "VEC4", null, null);
    }

    /// <summary>WEIGHTS_0：永遠是 (1, 0, 0, 0)。</summary>
    public int AddWeights(GltfRoot root, int count)
    {
        int offset = Begin();
        var writer = new BinaryWriter(_stream);

        for (int i = 0; i < count; i++)
        {
            writer.Write(1f);
            writer.Write(0f);
            writer.Write(0f);
            writer.Write(0f);
        }

        int view = AddView(root, offset, count * 16, 34962);
        return AddAccessor(root, view, ComponentType.Float, count, "VEC4", null, null);
    }

    private int Begin()
    {
        while (_stream.Length % 4 != 0)
            _stream.WriteByte(0);

        return (int)_stream.Length;
    }

    private static int AddView(GltfRoot root, int offset, int length, int? target)
    {
        root.BufferViews.Add(new GltfBufferView
        {
            Buffer = 0,
            ByteOffset = offset,
            ByteLength = length,
            Target = target,
        });

        return root.BufferViews.Count - 1;
    }

    private static int AddAccessor(
        GltfRoot root, int view, ComponentType componentType, int count, string type, float[]? min, float[]? max)
    {
        root.Accessors.Add(new GltfAccessor
        {
            BufferView = view,
            ComponentType = (int)componentType,
            Count = count,
            Type = type,
            Min = min,
            Max = max,
        });

        return root.Accessors.Count - 1;
    }

    private enum ComponentType
    {
        UnsignedShort = 5123,
        UnsignedInt = 5125,
        Float = 5126,
    }
}
