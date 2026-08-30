using System.Text.Json.Serialization;

namespace Client.AssetStudio.Export;

// glTF 2.0 的 JSON 結構，只保留這個匯出器會用到的欄位。
// 規格：https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html
//
// 刻意用 POCO 而不是 Dictionary<string, object>：欄位名寫錯在 glTF 裡是「安靜地少一塊」，
// 匯進 Blender 只會看到模型少了法線或動畫完全沒有，不會有任何錯誤訊息。

// WriteIndented 是關的：一個 379 個動作的角色模型，縮排會讓 .gltf 從 4 MB 變成 9 MB，
// 而這個檔案是給 Blender 讀的，不是給人讀的。要看內容的話 `python3 -m json.tool` 一行就排好。
[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false)]
[JsonSerializable(typeof(GltfRoot))]
internal sealed partial class GltfJsonContext : JsonSerializerContext;

internal sealed class GltfRoot
{
    public GltfAsset Asset { get; set; } = new();
    public int Scene { get; set; }
    public List<GltfScene> Scenes { get; set; } = [];
    public List<GltfNode> Nodes { get; set; } = [];
    /// <summary>
    /// 純骨架模型（player.bmd 這類）一個網格都沒有，這時候整個 <c>meshes</c> 不能落地 ——
    /// glTF 規格禁止空陣列，寫出去的檔案會被驗證器與 Blender 整份拒絕。
    /// </summary>
    public List<GltfMesh>? Meshes { get; set; }
    public List<GltfSkin>? Skins { get; set; }
    public List<GltfAnimation>? Animations { get; set; }
    public List<GltfMaterial>? Materials { get; set; }
    public List<GltfTexture>? Textures { get; set; }
    public List<GltfImage>? Images { get; set; }
    public List<GltfSampler>? Samplers { get; set; }
    public List<GltfAccessor> Accessors { get; set; } = [];
    public List<GltfBufferView> BufferViews { get; set; } = [];
    public List<GltfBuffer> Buffers { get; set; } = [];
}

internal sealed class GltfAsset
{
    public string Version { get; set; } = "2.0";
    public string Generator { get; set; } = "MuAssetStudio (BMD → glTF)";
}

internal sealed class GltfScene
{
    public List<int> Nodes { get; set; } = [];
}

internal sealed class GltfNode
{
    public string? Name { get; set; }
    public List<int>? Children { get; set; }
    public int? Mesh { get; set; }
    public int? Skin { get; set; }

    /// <summary>[x, y, z]。省略等同於 (0,0,0)。</summary>
    public float[]? Translation { get; set; }

    /// <summary>[x, y, z, w]。省略等同於單位四元數。</summary>
    public float[]? Rotation { get; set; }
}

internal sealed class GltfMesh
{
    public string? Name { get; set; }
    public List<GltfPrimitive> Primitives { get; set; } = [];
}

internal sealed class GltfPrimitive
{
    public Dictionary<string, int> Attributes { get; set; } = [];
    public int? Indices { get; set; }
    public int? Material { get; set; }
}

internal sealed class GltfSkin
{
    public string? Name { get; set; }
    public int? InverseBindMatrices { get; set; }
    public int? Skeleton { get; set; }
    public List<int> Joints { get; set; } = [];
}

internal sealed class GltfAnimation
{
    public string? Name { get; set; }
    public List<GltfAnimationChannel> Channels { get; set; } = [];
    public List<GltfAnimationSampler> Samplers { get; set; } = [];
}

internal sealed class GltfAnimationChannel
{
    public int Sampler { get; set; }
    public GltfAnimationTarget Target { get; set; } = new();
}

internal sealed class GltfAnimationTarget
{
    public int Node { get; set; }

    /// <summary>translation / rotation / scale / weights。</summary>
    public string Path { get; set; } = "translation";
}

internal sealed class GltfAnimationSampler
{
    public int Input { get; set; }
    public int Output { get; set; }
    public string Interpolation { get; set; } = "LINEAR";
}

internal sealed class GltfMaterial
{
    public string? Name { get; set; }
    public GltfPbr PbrMetallicRoughness { get; set; } = new();
    public bool DoubleSided { get; set; } = true;

    /// <summary>OPAQUE / MASK / BLEND。</summary>
    public string? AlphaMode { get; set; }
}

internal sealed class GltfPbr
{
    public GltfTextureRef? BaseColorTexture { get; set; }
    public float MetallicFactor { get; set; }
    public float RoughnessFactor { get; set; } = 1f;
}

internal sealed class GltfTextureRef
{
    public int Index { get; set; }
}

internal sealed class GltfTexture
{
    public int? Sampler { get; set; }
    public int? Source { get; set; }
}

internal sealed class GltfImage
{
    public string? Uri { get; set; }
    public string? Name { get; set; }
}

internal sealed class GltfSampler
{
    /// <summary>9729 = LINEAR。</summary>
    public int MagFilter { get; set; } = 9729;

    /// <summary>9987 = LINEAR_MIPMAP_LINEAR。</summary>
    public int MinFilter { get; set; } = 9987;

    /// <summary>10497 = REPEAT。MU 的貼圖大量依賴重複取樣。</summary>
    public int WrapS { get; set; } = 10497;

    public int WrapT { get; set; } = 10497;
}

internal sealed class GltfAccessor
{
    public int? BufferView { get; set; }
    public int ByteOffset { get; set; }

    /// <summary>5126 = FLOAT、5125 = UNSIGNED_INT、5123 = UNSIGNED_SHORT。</summary>
    public int ComponentType { get; set; }

    public int Count { get; set; }

    /// <summary>SCALAR / VEC2 / VEC3 / VEC4 / MAT4。</summary>
    public string Type { get; set; } = "SCALAR";

    /// <summary>POSITION 與動畫的 input 必須帶 min/max，少了它 Blender 會拒絕載入。</summary>
    public float[]? Min { get; set; }

    public float[]? Max { get; set; }
}

internal sealed class GltfBufferView
{
    public int Buffer { get; set; }
    public int ByteOffset { get; set; }
    public int ByteLength { get; set; }

    /// <summary>34962 = ARRAY_BUFFER、34963 = ELEMENT_ARRAY_BUFFER。動畫資料不設。</summary>
    public int? Target { get; set; }
}

internal sealed class GltfBuffer
{
    public string? Uri { get; set; }
    public int ByteLength { get; set; }
}
