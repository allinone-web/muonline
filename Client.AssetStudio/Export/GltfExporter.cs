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

    /// <param name="BodyParts">
    /// 共用主模型骨架的身體部位（<c>NPCObject.SetBodyPartsAsync</c> 組出來的那幾個）。
    /// 相對於 <c>Data/</c> 的路徑。
    /// </param>
    public sealed record Options(
        float FramesPerSecond = DefaultFramesPerSecond,
        bool ExportTextures = true,
        EntityKind Kind = EntityKind.Monster,
        IReadOnlyList<string>? BodyParts = null,
        string? DataPath = null);

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

            if (HasPose(bone))
            {
                var position = bone!.Matrixes[0].Position[0];
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

            // 沒有姿勢資料的骨頭（Dummy，或關鍵影格是空的）在遊戲裡是
            // **絕對的**單位矩陣（GenerateBoneMatrix 直接 worldTransform = Identity），
            // 不是「相對父骨的單位矩陣」。glTF 的節點變換一律是相對父節點的，
            // 所以要把它們掛到座標系根節點底下 —— 那個節點的世界變換
            // 正好就是 MU 空間的單位矩陣。
            // 掛在原本的父骨底下會讓它繼承父骨的姿勢，子骨跟著整串偏掉。
            short parent = HasPose(bone) ? bone!.Parent : (short)-1;

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

        // 身體部位。NPC 與角色的主模型常常一個網格都沒有（Man01.bmd 有 43 骨、0 網格），
        // 不把部位合進來的話匯出的就是一副空骨架 —— 檔案存在、Blender 打得開、但什麼都看不到。
        // 部位共用主模型的骨架（遊戲端的 LinkParentAnimation），所以直接併成同一個 mesh 的
        // 額外 primitive，skin 不用動。
        foreach (var partPath in options.BodyParts ?? [])
        {
            string full = Path.Combine(options.DataPath ?? string.Empty, partPath);

            if (!File.Exists(full))
                continue;

            BMD part;
            try
            {
                part = new BMDReader().Load(full).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                warnings.Add($"身體部位讀取失敗 {partPath}：{ex.Message}");
                continue;
            }

            string partDirectory = Path.GetDirectoryName(full) ?? modelDirectory;
            var partBindPose = BuildBindPose(part);

            foreach (var (source, meshIndex) in (part.Meshes ?? []).Select((m, i) => (m, i)))
            {
                // 骨骼矩陣用主模型的，但法線換算要用部位自己的綁定姿勢
                // （部位的骨頭數可以少於主模型，見 --skeleton-diff）。
                var primitive = BuildPrimitive(source, partBindPose, bones.Length, buffer, root, warnings, meshIndex);
                if (primitive is null)
                    continue;

                if (options.ExportTextures && !string.IsNullOrWhiteSpace(source.TexturePath))
                {
                    primitive.Material = ResolveMaterial(
                        source.TexturePath, partDirectory, outputDirectory,
                        root, materialIndex, warnings, ref exportedTextures);
                }

                mesh.Primitives.Add(primitive);
            }
        }

        // 純骨架模型在沒有身體部位可併的時候一個 primitive 都沒有 —— player.bmd 就是這樣，
        // 它自己 0 網格、60 骨、380 個動作，幾何全在 ArmorClass/HelmClass 那些部位檔裡。
        // 這時候不能照樣寫出 mesh：glTF 規格要求 primitives 至少一筆，空的會讓
        // SharpGLTF、Blender、Khronos 驗證器整份拒絕，連骨架和動作一起賠掉。
        // 改成不寫 mesh，輸出就是一份合法的「動作庫」——骨架在、380 個動作也都在。
        bool hasGeometry = mesh.Primitives.Count > 0;

        if (hasGeometry)
        {
            root.Meshes = [mesh];

            int meshNodeIndex = root.Nodes.Count;
            root.Nodes.Add(new GltfNode { Name = baseName + "_Mesh", Mesh = 0, Skin = bones.Length > 0 ? 0 : null });
            root.Nodes[0].Children!.Add(meshNodeIndex);
        }
        else
        {
            warnings.Add("這個模型沒有網格，只輸出骨架與動作");
        }

        // 規格規定 skin 只能掛在有 mesh 的節點上，所以沒有幾何時連 skin 都不能寫。
        // 骨架仍然以節點階層存在，Blender 匯進去是一串可動的節點。
        if (bones.Length > 0 && hasGeometry)
        {
            // InverseBindMatrices 省略 = 全部單位矩陣，正是 BMD 的語意（見類別註解）。
            root.Skins = [new GltfSkin
            {
                Name = baseName + "_Skin",
                Joints = boneNodeIndex.ToList(),

                // 座標系根節點才是所有關節的共同祖先。指向 joints[0] 是錯的：
                // 沒有姿勢的骨頭會直接掛在根底下，不在 joints[0] 的子樹裡。
                Skeleton = 0,
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

    /// <summary>
    /// 把每個動作轉成一個 glTF animation。
    /// </summary>
    /// <remarks>
    /// <b>常數曲線會被壓掉。</b>角色模型有 379 個動作、60 根骨頭，全展開是
    /// 45,000 個 sampler，光 JSON 就 17 MB —— 而一個 694 面的角色沒有必要那麼大。
    /// 實際上絕大多數骨頭在絕大多數動作裡完全不動：
    /// <list type="bullet">
    /// <item>整條曲線都等於節點自己的預設姿勢 → <b>整個 channel 省略</b>
    /// （glTF 沒有 channel 時就用節點的 TRS，語意完全相同）。</item>
    /// <item>整條曲線是常數但不等於預設姿勢 → 只寫<b>一個關鍵影格</b>。</item>
    /// </list>
    /// 兩者都是無損的：取樣任何時間點得到的姿勢與展開版一模一樣。
    /// </remarks>

    /// <summary>原版標記 Loop=true（=播到尾停住）的玩家一次性動作：
    /// 231 Die1、232 Die2、229 ComeUp、72 SkillHellBegin（ZzzOpenData.cpp:366–370；
    /// 編號對照 muonline PlayerAction.cs）。</summary>
    private static readonly HashSet<int> OneShotClosedLoopActions = [231, 232, 229, 72];

    /// <summary>這個動作是否「末鍵==首鍵」的閉合環（逐骨比對位置與旋轉）。</summary>
    private static bool ActionEndsWhereItStarts(BMD bmd, int actionIndex, int keys)
    {
        const float epsilon = 1e-4f;
        var bones = bmd.Bones ?? [];
        foreach (var bone in bones)
        {
            if (bone is null || bone == BMDTextureBone.Dummy ||
                bone.Matrixes is null || actionIndex >= bone.Matrixes.Length)
                continue;

            var matrix = bone.Matrixes[actionIndex];
            if (matrix.Position is not { Length: > 0 } || matrix.Quaternion is not { Length: > 0 })
                continue;

            int last = Math.Min(keys, Math.Min(matrix.Position.Length, matrix.Quaternion.Length)) - 1;
            if (last <= 0)
                continue;

            if ((matrix.Position[last] - matrix.Position[0]).LengthSquared() > epsilon)
                return false;

            var q0 = matrix.Quaternion[0];
            var q1 = matrix.Quaternion[last];
            float dot = MathF.Abs((q0.X * q1.X) + (q0.Y * q1.Y) + (q0.Z * q1.Z) + (q0.W * q1.W));
            if (1f - dot > epsilon)
                return false;
        }

        return true;
    }

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

        // 玩家的動畫時基是 25fps，不是通用預設的 4fps（B43 滑行感的資產側環節）。
        //
        // 真值：PlayerObject.cs:234 `AnimationSpeed = 25`（經典 MU 25FPS 幀系統；
        // 通用 ModelObject 才是 4）。原版有效幀速 = PlaySpeed × AnimationSpeed——
        // walk 0.38×25 = 9.5 幀/秒、一循環 0.95 秒；按 4fps 匯出的時間軸被
        // 原速播放會慢 25/4 倍基準（walk 慢 2.4 倍）＝「腿在動但像太空漫步」。
        // 職責分界照原版語意拆：**資產＝25fps 基準時間軸；每動作的 PlaySpeed
        // 倍率（walk 0.38/attack 0.32/die 0.45…，PlayerObject.cs:1212–1269）
        // 由 runtime 套用**——匯出器不複製那張表，避免雙份維護。
        if (kind == EntityKind.Player)
            framesPerSecond = 25f;

        float secondsPerFrame = 1f / MathF.Max(framesPerSecond, 0.01f);

        // 所有「只有一格」的曲線共用同一個時間 accessor。
        int? singleKeyTime = null;

        for (int actionIndex = 0; actionIndex < actions.Length; actionIndex++)
        {
            var action = actions[actionIndex];
            if (action is null)
                continue;

            // LockPositions 的動作最後一格是位移資料，不是姿勢 —— 與遊戲的 totalFrames 一致。
            int keys = Math.Max(action.LockPositions ? action.NumAnimationKeys - 1 : action.NumAnimationKeys, 1);

            // 一次性動作的 loop 閉合鍵（docs/33 建議 #2）。
            //
            // BMD 原始資料把這幾個動作的最後一鍵做成「回到第一鍵」（實測 231/232
            // 全 60 骨末鍵==首鍵）——原版 runtime 從不播到它：mumain 對這批動作標
            // Actions[..].Loop = true（ZzzOpenData.cpp:366–370，語意=播到尾「停住」，
            // ZzzBMD.cpp:735–742 clamp 不迴繞；命名反直覺），muonline 則 clamp 到
            // totalFrames-2（ModelObject.Animation.cs:222–227）。glTF 播放器一次性
            // 播放會播到末鍵→角色回站姿，所以匯出時把閉合鍵去掉，終幀=真實最終姿勢。
            // 名單取自原版 Loop=true 四動作（非猜測）；再驗末鍵確實==首鍵才減，
            // 資料不閉合就原樣保留（fail open 到「不動」，不猜）。
            if (kind == EntityKind.Player && OneShotClosedLoopActions.Contains(actionIndex) &&
                keys >= 2 && ActionEndsWhereItStarts(bmd, actionIndex, keys))
            {
                keys -= 1;
            }

            // 時間 accessor 也是需要時才建，否則整個動作被壓掉時會留下沒人用的緩衝資料。
            int? timeAccessor = null;

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

                var node = root.Nodes[boneNodeIndex[boneIndex]];

                AddChannel(
                    animation, buffer, root, boneNodeIndex[boneIndex], "translation",
                    IsConstant(translations, (a, b) => (a - b).LengthSquared() <= Epsilon),
                    Matches(node.Translation, translations[0]),
                    values => buffer.AddVec3(root, values, target: null, withBounds: false),
                    translations,
                    ref timeAccessor, ref singleKeyTime, keys, secondsPerFrame);

                AddChannel(
                    animation, buffer, root, boneNodeIndex[boneIndex], "rotation",
                    IsConstant(rotations, (a, b) => (a - b).LengthSquared() <= Epsilon),
                    Matches(node.Rotation, rotations[0]),
                    values => buffer.AddVec4(root, values),
                    rotations,
                    ref timeAccessor, ref singleKeyTime, keys, secondsPerFrame);
            }

            // **動作編號就是身分。** MonsterActionType.Stop1 是 0、Die 是 6；
            // PlayerAction 那一套有 380 個編號。少匯出一個動作，後面全部的編號就位移一格 ——
            // 在 Blender 裡看只是「少了一個 action」，但任何依編號查表的東西都會拿到錯的動作，
            // 而且是靜默的。所以即使整個動作完全沒有變化，也要留一個空殼。
            //
            // glTF 規定 animation.channels 至少要有一項，所以補一條單影格的曲線。
            if (animation.Channels.Count == 0 && boneNodeIndex.Length > 0)
            {
                var node = root.Nodes[boneNodeIndex[0]];

                singleKeyTime ??= buffer.AddScalarFloat(root, [0f], withBounds: true);

                var value = node.Translation is { Length: 3 } t
                    ? new Vector3(t[0], t[1], t[2])
                    : Vector3.Zero;

                animation.Samplers.Add(new GltfAnimationSampler
                {
                    Input = singleKeyTime.Value,
                    Output = buffer.AddVec3(root, [value], target: null, withBounds: false),
                });

                animation.Channels.Add(new GltfAnimationChannel
                {
                    Sampler = 0,
                    Target = new GltfAnimationTarget { Node = boneNodeIndex[0], Path = "translation" },
                });
            }

            animations.Add(animation);
        }

        return animations;
    }

    /// <summary>浮點比較的容忍度。四元數與位置都在同一個量級上，用同一個值。</summary>
    private const float Epsilon = 1e-8f;

    private static bool IsConstant<T>(List<T> values, Func<T, T, bool> equal)
    {
        for (int i = 1; i < values.Count; i++)
        {
            if (!equal(values[0], values[i]))
                return false;
        }

        return true;
    }

    /// <summary>這個常數值與節點的預設 TRS 一樣嗎？一樣的話整個 channel 都可以省略。</summary>
    private static bool Matches(float[]? nodeValue, Vector3 value)
        => nodeValue is null
            ? value.LengthSquared() <= Epsilon
            : nodeValue.Length == 3
              && MathF.Abs(nodeValue[0] - value.X) <= 1e-4f
              && MathF.Abs(nodeValue[1] - value.Y) <= 1e-4f
              && MathF.Abs(nodeValue[2] - value.Z) <= 1e-4f;

    private static bool Matches(float[]? nodeValue, Vector4 value)
        => nodeValue is null
            ? MathF.Abs(value.X) <= 1e-4f && MathF.Abs(value.Y) <= 1e-4f
              && MathF.Abs(value.Z) <= 1e-4f && MathF.Abs(value.W - 1f) <= 1e-4f
            : nodeValue.Length == 4
              && MathF.Abs(nodeValue[0] - value.X) <= 1e-4f
              && MathF.Abs(nodeValue[1] - value.Y) <= 1e-4f
              && MathF.Abs(nodeValue[2] - value.Z) <= 1e-4f
              && MathF.Abs(nodeValue[3] - value.W) <= 1e-4f;

    private static void AddChannel<T>(
        GltfAnimation animation,
        BufferBuilder buffer,
        GltfRoot root,
        int node,
        string path,
        bool constant,
        bool matchesDefault,
        Func<List<T>, int> write,
        List<T> values,
        ref int? timeAccessor,
        ref int? singleKeyTime,
        int keys,
        float secondsPerFrame)
    {
        // 常數而且等於節點預設 → 這個 channel 什麼都沒說，直接不寫。
        if (constant && matchesDefault)
            return;

        int input;
        List<T> output;

        if (constant)
        {
            singleKeyTime ??= buffer.AddScalarFloat(root, [0f], withBounds: true);
            input = singleKeyTime.Value;
            output = [values[0]];
        }
        else
        {
            if (timeAccessor is null)
            {
                var times = new List<float>(keys);
                for (int k = 0; k < keys; k++)
                    times.Add(k * secondsPerFrame);

                timeAccessor = buffer.AddScalarFloat(root, times, withBounds: true);
            }

            input = timeAccessor.Value;
            output = values;
        }

        int sampler = animation.Samplers.Count;
        animation.Samplers.Add(new GltfAnimationSampler { Input = input, Output = write(output) });
        animation.Channels.Add(new GltfAnimationChannel
        {
            Sampler = sampler,
            Target = new GltfAnimationTarget { Node = node, Path = path },
        });
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

    /// <summary>這根骨頭有沒有可用的姿勢資料（不是 Dummy，而且第一個動作有關鍵影格）。</summary>
    private static bool HasPose(BMDTextureBone? bone)
        => bone is not null
        && bone != BMDTextureBone.Dummy
        && bone.Matrixes is { Length: > 0 }
        && bone.Matrixes[0].Position is { Length: > 0 }
        && bone.Matrixes[0].Quaternion is { Length: > 0 };

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
