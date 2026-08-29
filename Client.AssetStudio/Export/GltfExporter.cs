using System.Numerics;
using System.Text.Json;
using Client.AssetStudio.Catalog;
using Client.AssetStudio.Textures;
using Client.Data.BMD;

namespace Client.AssetStudio.Export;

/// <summary>
/// <c>.bmd</c> → glTF 2.0（`.gltf` + `.bin` + PNG 貼圖），含骨架與全部動作。
/// </summary>
/// <remarks>
/// <b>這一步的價值大於工具本身。</b><c>STRATEGY.md</c> 第 4 節的長期規劃是
/// 「在 <c>BMDLoader</c> 旁邊加一個 <c>GltfLoader</c>，新內容用 glTF」——
/// 有了匯出器才能把既有資產搬進 Blender、建立自己的美術管線，
/// 而不是長期維護一個自製的 3D 格式。
/// <b>反方向（glTF → BMD）刻意不做</b>：正確的方向是讓客戶端讀 glTF。
///
/// 三個關鍵的對應決定：
/// <list type="number">
/// <item><b>座標系</b>：MU 是 Z 軸向上，glTF 是 Y 軸向上。做法是在整個階層之上加一個
/// 繞 X 轉 −90° 的根節點，<b>骨骼與頂點資料一個位元組都不動</b>。
/// 逐點轉換會連帶要轉四元數與法線，多一個環節就多一個會錯的地方。</item>
/// <item><b>反向綁定矩陣全部是單位矩陣</b>（因此整段省略）。BMD 的頂點座標本來就存在
/// <b>骨骼的區域座標系</b>裡（遊戲直接做 <c>Transform(position, bones[node])</c>，
/// 沒有任何 inverse bind），這與 glTF 在 IBM 為單位矩陣時的語意完全相同。</item>
/// <item><b>單骨綁定</b>：<c>JOINTS_0 = (node, 0, 0, 0)</c>、<c>WEIGHTS_0 = (1, 0, 0, 0)</c>。
/// MU 沒有權重資料。法線另有自己的骨頭索引，匯出前先換算回頂點骨頭的座標系，
/// 否則 Blender 裡關節處會出現一圈錯誤的受光。</item>
/// </list>
///
/// <b>動畫速率</b>：<c>.bmd</c> <b>沒有</b>存播放速度 —— <c>BMDReader</c> 根本不讀，
/// <c>PlaySpeed</c> 一律是 1。每隻怪真正的速度是 <c>Client.Main</c> 的類別在
/// <c>Load()</c> 裡用 <c>SetActionSpeed()</c> 設的（而且會再乘 2）。
/// 所以匯出的 FPS 由呼叫端指定，預設值是遊戲未經調整時的
/// <c>PlaySpeed(1) × AnimationSpeed(4) = 4</c>。
/// </remarks>
public static class GltfExporter
{
    private const int TargetArrayBuffer = 34962;
    private const int TargetElementArrayBuffer = 34963;

    /// <summary>遊戲未經調整時的動畫速率：<c>PlaySpeed(1) × AnimationSpeed(4)</c>。</summary>
    public const float DefaultFramesPerSecond = AnimatedModelDefaults.AnimationSpeed;

    public sealed record Options(
        float FramesPerSecond = DefaultFramesPerSecond,
        bool ExportTextures = true,
        EntityKind Kind = EntityKind.Monster);

    public sealed record Result(string GltfPath, int Meshes, int Bones, int Animations, int Textures, string[] Warnings);

    public static Result Export(string bmdPath, string outputDirectory, Options? options = null)
    {
        options ??= new Options();

        var bmd = new BMDReader().Load(bmdPath).GetAwaiter().GetResult();
        return Export(bmd, bmdPath, outputDirectory, options);
    }

    public static Result Export(BMD bmd, string bmdPath, string outputDirectory, Options options)
    {
        Directory.CreateDirectory(outputDirectory);

        string baseName = Path.GetFileNameWithoutExtension(bmdPath);
        string binName = baseName + ".bin";
        string modelDirectory = Path.GetDirectoryName(bmdPath) ?? string.Empty;

        var warnings = new List<string>();
        var buffer = new BufferBuilder();
        var root = new GltfRoot();

        var bones = bmd.Bones ?? [];
        var bindPose = BuildBindPose(bmd);

        // ── 節點 ──────────────────────────────────────────────
        // 0 = 座標系轉換的根，1..boneCount = 骨骼，最後 = 網格節點。
        root.Nodes.Add(new GltfNode
        {
            Name = "MU_ZUp_To_GltfYUp",
            // 繞 X 轉 −90°：MU 的 +Z（上）變成 glTF 的 +Y。
            Rotation = [-0.70710678f, 0f, 0f, 0.70710678f],
            Children = [],
        });

        var boneNodeIndex = new int[bones.Length];

        for (int i = 0; i < bones.Length; i++)
        {
            var bone = bones[i];
            boneNodeIndex[i] = root.Nodes.Count;

            var node = new GltfNode { Name = BoneName(bone, i) };

            if (bone is not null && bone != BMDTextureBone.Dummy
                && bone.Matrixes is { Length: > 0 }
                && bone.Matrixes[0].Position is { Length: > 0 }
                && bone.Matrixes[0].Quaternion is { Length: > 0 })
            {
                var position = bone.Matrixes[0].Position[0];
                var rotation = bone.Matrixes[0].Quaternion[0];

                node.Translation = [position.X, position.Y, position.Z];
                node.Rotation = [rotation.X, rotation.Y, rotation.Z, rotation.W];
            }

            root.Nodes.Add(node);
        }

        // 父子關係。BMD 的骨骼是父在前的順序，但保險起見不假設。
        for (int i = 0; i < bones.Length; i++)
        {
            var bone = bones[i];
            short parent = bone is null || bone == BMDTextureBone.Dummy ? (short)-1 : bone.Parent;

            if (parent >= 0 && parent < bones.Length)
            {
                var parentNode = root.Nodes[boneNodeIndex[parent]];
                (parentNode.Children ??= []).Add(boneNodeIndex[i]);
            }
            else
            {
                root.Nodes[0].Children!.Add(boneNodeIndex[i]);
            }
        }

        // ── 貼圖與材質 ────────────────────────────────────────
        var materialIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int exportedTextures = 0;

        // ── 網格 ──────────────────────────────────────────────
        var mesh = new GltfMesh { Name = baseName };

        foreach (var (source, meshIndex) in (bmd.Meshes ?? []).Select((m, i) => (m, i)))
        {
            var primitive = BuildPrimitive(source, bindPose, bones.Length, buffer, root, warnings, meshIndex);
            if (primitive is null)
                continue;

            if (options.ExportTextures && !string.IsNullOrWhiteSpace(source.TexturePath))
            {
                int? material = ResolveMaterial(
                    source.TexturePath, modelDirectory, outputDirectory,
                    root, materialIndex, warnings, ref exportedTextures);

                primitive.Material = material;
            }

            mesh.Primitives.Add(primitive);
        }

        if (mesh.Primitives.Count == 0)
            warnings.Add("這個模型沒有任何可匯出的網格");

        root.Meshes.Add(mesh);

        int meshNodeIndex = root.Nodes.Count;
        root.Nodes.Add(new GltfNode { Name = baseName + "_Mesh", Mesh = 0, Skin = bones.Length > 0 ? 0 : null });
        root.Nodes[0].Children!.Add(meshNodeIndex);

        if (bones.Length > 0)
        {
            // InverseBindMatrices 省略 = 全部單位矩陣，正是 BMD 的語意（見類別註解）。
            root.Skins = [new GltfSkin
            {
                Name = baseName + "_Skin",
                Joints = boneNodeIndex.ToList(),
                Skeleton = boneNodeIndex.Length > 0 ? boneNodeIndex[0] : null,
            }];
        }

        root.Scenes.Add(new GltfScene { Nodes = [0] });

        // ── 動畫 ──────────────────────────────────────────────
        var animations = BuildAnimations(bmd, boneNodeIndex, buffer, root, options.FramesPerSecond, options.Kind);
        if (animations.Count > 0)
            root.Animations = animations;

        // ── 輸出 ──────────────────────────────────────────────
        var binary = buffer.ToArray();
        root.Buffers.Add(new GltfBuffer { Uri = binName, ByteLength = binary.Length });

        string gltfPath = Path.Combine(outputDirectory, baseName + ".gltf");
        File.WriteAllBytes(Path.Combine(outputDirectory, binName), binary);
        File.WriteAllText(gltfPath, JsonSerializer.Serialize(root, GltfJsonContext.Default.GltfRoot));

        return new Result(
            gltfPath,
            mesh.Primitives.Count,
            bones.Length,
            root.Animations?.Count ?? 0,
            exportedTextures,
            warnings.ToArray());
    }

    // ── 網格 ─────────────────────────────────────────────────────

    private static GltfPrimitive? BuildPrimitive(
        BMDTextureMesh source,
        Matrix4x4[] bindPose,
        int boneCount,
        BufferBuilder buffer,
        GltfRoot root,
        List<string> warnings,
        int meshIndex)
    {
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var texCoords = new List<Vector2>();
        var joints = new List<ushort>();
        var indices = new List<uint>();

        // BMD 的頂點/法線/UV 是三組獨立索引，glTF 要求同一個索引取三者，所以要重新編號。
        var lookup = new Dictionary<(short V, short N, short T), uint>();

        foreach (var triangle in source.Triangles)
        {
            int corners = triangle.Polygon >= 4 ? 4 : 3;
            ReadOnlySpan<int> order = corners == 4 ? [0, 1, 2, 0, 2, 3] : [0, 1, 2];

            int before = indices.Count;
            bool ok = true;

            foreach (int corner in order)
            {
                short v = triangle.VertexIndex[corner];
                short n = triangle.NormalIndex[corner];
                short t = triangle.TexCoordIndex[corner];

                if ((uint)v >= (uint)source.Vertices.Length)
                {
                    ok = false;
                    break;
                }

                if (!lookup.TryGetValue((v, n, t), out uint index))
                {
                    var vertex = source.Vertices[v];
                    int vertexBone = vertex.Node;

                    var normal = Vector3.UnitZ;
                    if ((uint)n < (uint)source.Normals.Length)
                        normal = Rebase(source.Normals[n], vertexBone, bindPose);

                    var uv = (uint)t < (uint)source.TexCoords.Length
                        ? new Vector2(source.TexCoords[t].U, source.TexCoords[t].V)
                        : Vector2.Zero;

                    index = (uint)positions.Count;
                    positions.Add(vertex.Position);
                    normals.Add(normal);
                    texCoords.Add(uv);
                    joints.Add((ushort)Math.Clamp(vertexBone, 0, Math.Max(boneCount - 1, 0)));
                    lookup[(v, n, t)] = index;
                }

                indices.Add(index);
            }

            if (!ok)
                indices.RemoveRange(before, indices.Count - before);
        }

        if (positions.Count == 0 || indices.Count < 3)
        {
            warnings.Add($"網格 {meshIndex} 沒有有效的三角形，已略過");
            return null;
        }

        var primitive = new GltfPrimitive
        {
            Indices = buffer.AddScalarUInt(root, indices, TargetElementArrayBuffer),
        };

        primitive.Attributes["POSITION"] = buffer.AddVec3(root, positions, TargetArrayBuffer, withBounds: true);
        primitive.Attributes["NORMAL"] = buffer.AddVec3(root, normals, TargetArrayBuffer, withBounds: false);
        primitive.Attributes["TEXCOORD_0"] = buffer.AddVec2(root, texCoords, TargetArrayBuffer);

        if (boneCount > 0)
        {
            primitive.Attributes["JOINTS_0"] = buffer.AddJoints(root, joints);
            primitive.Attributes["WEIGHTS_0"] = buffer.AddWeights(root, joints.Count);
        }

        return primitive;
    }

    /// <summary>
    /// 法線的骨頭索引可以與頂點的不同，但 glTF 一個頂點只有一組關節。
    /// 先用法線自己的骨頭轉到綁定姿勢的世界空間，再轉回頂點骨頭的區域空間。
    /// </summary>
    private static Vector3 Rebase(BMDTextureNormal normal, int vertexBone, Matrix4x4[] bindPose)
    {
        if (normal.Node == vertexBone
            || (uint)normal.Node >= (uint)bindPose.Length
            || (uint)vertexBone >= (uint)bindPose.Length)
        {
            return Normalize(normal.Normal);
        }

        var world = Vector3.TransformNormal(normal.Normal, bindPose[normal.Node]);

        if (!Matrix4x4.Invert(bindPose[vertexBone], out var inverse))
            return Normalize(world);

        return Normalize(Vector3.TransformNormal(world, inverse));
    }

    private static Vector3 Normalize(Vector3 value)
        => value.LengthSquared() > 1e-9f ? Vector3.Normalize(value) : Vector3.UnitZ;

    /// <summary>綁定姿勢（動作 0、影格 0）的骨骼世界矩陣。只用於法線換算。</summary>
    private static Matrix4x4[] BuildBindPose(BMD bmd)
    {
        var bones = bmd.Bones ?? [];
        var result = new Matrix4x4[bones.Length];

        for (int i = 0; i < bones.Length; i++)
        {
            result[i] = Matrix4x4.Identity;

            var bone = bones[i];
            if (bone is null || bone == BMDTextureBone.Dummy || bone.Matrixes is not { Length: > 0 })
                continue;

            var matrix = bone.Matrixes[0];
            if (matrix.Quaternion is not { Length: > 0 } || matrix.Position is not { Length: > 0 })
                continue;

            var local = Matrix4x4.CreateFromQuaternion(matrix.Quaternion[0]);
            local.Translation = matrix.Position[0];

            result[i] = bone.Parent >= 0 && bone.Parent < i
                ? local * result[bone.Parent]
                : local;
        }

        return result;
    }

    // ── 動畫 ─────────────────────────────────────────────────────

    private static List<GltfAnimation> BuildAnimations(
        BMD bmd,
        int[] boneNodeIndex,
        BufferBuilder buffer,
        GltfRoot root,
        float framesPerSecond,
        EntityKind kind)
    {
        var animations = new List<GltfAnimation>();
        var actions = bmd.Actions ?? [];
        var bones = bmd.Bones ?? [];

        float secondsPerFrame = 1f / MathF.Max(framesPerSecond, 0.01f);

        for (int actionIndex = 0; actionIndex < actions.Length; actionIndex++)
        {
            var action = actions[actionIndex];
            if (action is null)
                continue;

            // LockPositions 的動作最後一格是位移資料，不是姿勢 —— 與遊戲的 totalFrames 一致。
            int keys = action.LockPositions ? action.NumAnimationKeys - 1 : action.NumAnimationKeys;
            if (keys < 2)
                continue;

            var times = new List<float>(keys);
            for (int k = 0; k < keys; k++)
                times.Add(k * secondsPerFrame);

            int timeAccessor = buffer.AddScalarFloat(root, times, withBounds: true);

            var animation = new GltfAnimation { Name = ActionNames.Of(kind, actionIndex) };

            for (int boneIndex = 0; boneIndex < bones.Length; boneIndex++)
            {
                var bone = bones[boneIndex];
                if (bone is null || bone == BMDTextureBone.Dummy
                    || bone.Matrixes is null || actionIndex >= bone.Matrixes.Length)
                {
                    continue;
                }

                var matrix = bone.Matrixes[actionIndex];
                if (matrix.Position is not { Length: > 0 } || matrix.Quaternion is not { Length: > 0 })
                    continue;

                int available = Math.Min(matrix.Position.Length, matrix.Quaternion.Length);

                var translations = new List<Vector3>(keys);
                var rotations = new List<Vector4>(keys);

                for (int k = 0; k < keys; k++)
                {
                    int source = Math.Min(k, available - 1);
                    var position = matrix.Position[source];

                    // 根骨在 LockPositions 的動作裡把 XY 鎖在第一格 —— 那條曲線是給世界移動用的，
                    // 照著播的話模型會自己飄走（遊戲端的 GenerateBoneMatrix 也是這樣處理）。
                    if (boneIndex == 0 && action.LockPositions)
                        position = new Vector3(matrix.Position[0].X, matrix.Position[0].Y, position.Z);

                    translations.Add(position);

                    var q = matrix.Quaternion[source];
                    rotations.Add(new Vector4(q.X, q.Y, q.Z, q.W));
                }

                int translationSampler = animation.Samplers.Count;
                animation.Samplers.Add(new GltfAnimationSampler
                {
                    Input = timeAccessor,
                    Output = buffer.AddVec3(root, translations, target: null, withBounds: false),
                });

                animation.Channels.Add(new GltfAnimationChannel
                {
                    Sampler = translationSampler,
                    Target = new GltfAnimationTarget { Node = boneNodeIndex[boneIndex], Path = "translation" },
                });

                int rotationSampler = animation.Samplers.Count;
                animation.Samplers.Add(new GltfAnimationSampler
                {
                    Input = timeAccessor,
                    Output = buffer.AddVec4(root, rotations),
                });

                animation.Channels.Add(new GltfAnimationChannel
                {
                    Sampler = rotationSampler,
                    Target = new GltfAnimationTarget { Node = boneNodeIndex[boneIndex], Path = "rotation" },
                });
            }

            if (animation.Channels.Count > 0)
                animations.Add(animation);
        }

        return animations;
    }

    // ── 材質 ─────────────────────────────────────────────────────

    private static int? ResolveMaterial(
        string texturePath,
        string modelDirectory,
        string outputDirectory,
        GltfRoot root,
        Dictionary<string, int> cache,
        List<string> warnings,
        ref int exportedTextures)
    {
        if (cache.TryGetValue(texturePath, out int existing))
            return existing;

        var resolution = TextureResolver.Resolve(modelDirectory, texturePath);

        if (!resolution.Found)
        {
            warnings.Add($"缺貼圖：{texturePath}");
            return null;
        }

        string pngName = Path.GetFileNameWithoutExtension(resolution.FullPath!) + ".png";
        string pngPath = Path.Combine(outputDirectory, pngName);

        try
        {
            TextureIO.ExportPng(resolution.FullPath!, pngPath);
            exportedTextures++;
        }
        catch (Exception ex)
        {
            warnings.Add($"貼圖轉檔失敗 {resolution.FileName}：{ex.Message}");
            return null;
        }

        root.Samplers ??= [new GltfSampler()];
        root.Images ??= [];
        root.Textures ??= [];
        root.Materials ??= [];

        root.Images.Add(new GltfImage { Uri = pngName, Name = Path.GetFileNameWithoutExtension(pngName) });
        root.Textures.Add(new GltfTexture { Sampler = 0, Source = root.Images.Count - 1 });

        // 帶 alpha 的來源走 BLEND —— 與遊戲端「IsRgba 就是半透明網格」的判斷一致。
        string extension = Path.GetExtension(resolution.FullPath!).ToLowerInvariant();
        bool hasAlpha = extension is ".ozt" or ".ozp" or ".ozd" or ".png" or ".tga";

        root.Materials.Add(new GltfMaterial
        {
            Name = Path.GetFileNameWithoutExtension(pngName),
            AlphaMode = hasAlpha ? "BLEND" : "OPAQUE",
            PbrMetallicRoughness = new GltfPbr
            {
                BaseColorTexture = new GltfTextureRef { Index = root.Textures.Count - 1 },
            },
        });

        int index = root.Materials.Count - 1;
        cache[texturePath] = index;
        return index;
    }

    private static string BoneName(BMDTextureBone? bone, int index)
    {
        if (bone is null || bone == BMDTextureBone.Dummy || string.IsNullOrWhiteSpace(bone.Name))
            return $"Bone{index:000}";

        // Blender 的骨頭名稱不能重複，加上索引保證唯一。
        return $"{bone.Name}_{index:000}";
    }
}

/// <summary>與 <c>ModelObject</c> 的預設值同步，避免兩處各寫一個魔術數字。</summary>
internal static class AnimatedModelDefaults
{
    public const float AnimationSpeed = 4f;
}
