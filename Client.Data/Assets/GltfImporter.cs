using System.Numerics;
using Client.Data.BMD;
using SharpGLTF.Schema2;

namespace Client.AssetStudio.Import;

/// <summary>匯進來的一張貼圖：名稱 + 原始位元組（PNG / JPEG）。</summary>
public sealed record ImportedTexture(string Name, byte[] Content);

/// <summary>
/// 匯入的結果。<see cref="Model"/> 是「MU 表達得出來的那一份」，
/// 給檢視器與縮圖用；<see cref="Clips"/> 保留原始的動作名稱，供動作對映使用。
/// </summary>
public sealed record ImportedModel(
    BMD Model,
    ImportedTexture[] Textures,
    string[] Clips,
    ImportReport Report);

/// <summary>
/// 讀外部的 glTF / GLB，轉成 MU 的模型表達。
/// </summary>
/// <remarks>
/// <b>這不是「glTF → BMD 轉檔器」。</b>轉出來的 <see cref="BMD"/> 不會被寫進磁碟 ——
/// 它只是<b>轉接層</b>，讓外部資產能走既有的檢視器、縮圖與貼圖檢查，
/// 而且讓「MU 表達不出來的東西」在畫面上被看見而不是被靜默丟掉。
/// 進資源庫的永遠是原始的 glTF（見 <c>Project/AssetLibrary.cs</c>）。
///
/// 這個區分很重要：`docs/引擎轉換方案-工具與客戶端遷移到Godot.md` 的鐵律是
/// 「不做 glTF → BMD」，理由是自製格式轉換器會變成長期負債。
/// 那條鐵律講的是<b>資產的儲存格式</b>，而這裡沒有產生任何要長期維護的檔案格式。
///
/// 四個必須做的對應（每一個做錯都是靜默的）：
/// <list type="number">
/// <item><b>座標系</b>：glTF 是 Y 軸向上、MU 是 Z 軸向上。只轉根骨的區域變換，
/// 頂點資料一個位元組都不動 —— 與匯出器的作法對稱。</item>
/// <item><b>頂點座標系</b>：glTF 的頂點在網格空間，MU 的頂點在<b>骨骼的區域空間</b>。
/// 換算就是乘上該骨頭的 inverse bind matrix。</item>
/// <item><b>單骨綁定</b>：MU 一個頂點只能綁一根骨頭。取權重最大的那一根，
/// 並把「有多少頂點因此失真」報出來。</item>
/// <item><b>骨骼順序</b>：MU 算世界矩陣時假設<b>父骨排在子骨前面</b>
/// （<c>local * BoneTransform[parent]</c>）。glTF 沒有這個保證，所以要拓撲重排，
/// 並同步重寫頂點的骨頭索引。順序錯了模型會像被拆開一樣散掉。</item>
/// </list>
/// </remarks>
public static class GltfImporter
{
    /// <summary>MU 角色大約這麼高（世界單位）。用來建議匯入縮放。</summary>
    private const float ReferenceCharacterHeight = 175f;

    /// <summary>動畫取樣率。與匯出端一致：遊戲未經調整時是 4 影格／秒。</summary>
    /// <summary>
    /// 動畫取樣率。
    /// </summary>
    /// <remarks>
    /// 原本是 4。太低了 —— 一個走路循環只取到 4 個姿勢，客戶端在關鍵影格之間
    /// 用 Nlerp 內插時，相鄰姿勢的夾角常常超過 90°，四肢會沿直線穿過身體，
    /// 看起來就是「有動作但扭曲成一團」。
    ///
    /// 診斷的關鍵是：把縮圖渲染器改成畫<b>精確的關鍵影格</b>（走路第 2 格）時，
    /// 姿勢完全正常 —— 也就是資料沒錯，錯的是影格之間。
    /// MU 自己的模型影格數也不多（走路 6 格），但那是<b>照那個速率手工調過</b>的，
    /// 姿勢本來就選得能好好內插；重新取樣的動畫沒有這個保證。
    /// </remarks>
    public const float DefaultSampleFps = 24f;

    /// <summary>GPU 蒙皮的骨骼上限（<c>ModelObject.MaxGpuSkinBones</c>）。超過會退回 CPU 蒙皮。</summary>
    private const int GpuSkinBoneLimit = 256;

    public sealed record Options(float Scale = 1f, float SampleFps = DefaultSampleFps, bool AutoScale = true);

    public static ImportedModel Import(string path, Options? options = null)
    {
        options ??= new Options();
        var report = new ImportReport();

        ModelRoot root;
        try
        {
            root = ModelRoot.Load(path, new ReadSettings { Validation = SharpGLTF.Validation.ValidationMode.TryFix });
        }
        catch (Exception ex)
        {
            report.Error("讀不開這個檔案", $"{ex.GetType().Name}：{ex.Message}");
            return new ImportedModel(new BMD(), [], [], report);
        }

        var skin = root.LogicalSkins.FirstOrDefault();
        var (joints, jointRemap) = BuildJoints(skin, report);

        var (meshes, textures) = BuildMeshes(root, joints, jointRemap, report);

        if (meshes.Count == 0)
            report.Error("沒有任何網格", "glTF 裡找不到可用的三角形。");

        // 先照原尺寸組出完整的模型，量完高度才決定縮放。
        var actions = BuildActions(root, options.SampleFps, report);
        var bones = BuildBones(joints, actions.Length, root, options.SampleFps, report);

        var bmd = new BMD
        {
            Version = 10,
            Name = Path.GetFileNameWithoutExtension(path),
            Meshes = meshes.ToArray(),
            Bones = bones,
            Actions = actions,
        };

        // 高度要量<b>蒙皮之後</b>的世界座標，不能量頂點陣列本身：
        // MU 的頂點存在骨骼的區域空間裡，一個綁在頭骨上的頂點座標是「相對頭骨」的幾公分，
        // 不是角色的身高。量錯的話建議縮放會差好幾倍，而且看起來很合理。
        float height = MeasureHeight(bmd);
        report.SuggestedScale = height > 0.0001f ? ReferenceCharacterHeight / height : 1f;

        float scale = options.AutoScale ? report.SuggestedScale : options.Scale;

        if (MathF.Abs(scale - 1f) > 0.001f)
        {
            report.Info("已套用匯入縮放", $"×{scale:F3}（原始高度 {height:F2} → {height * scale:F0} 世界單位）");
            ApplyScale(bmd, scale);
        }
        else
        {
            scale = 1f;
        }

        report.Height = height * scale;

        report.Meshes = meshes.Count;
        report.Triangles = meshes.Sum(m => m.Triangles.Length);
        report.Vertices = meshes.Sum(m => m.Vertices.Length);
        report.Bones = bones.Length;
        report.Animations = actions.Length;
        report.Textures = textures.Count;

        if (bones.Length > GpuSkinBoneLimit)
        {
            report.Warn($"骨骼數 {bones.Length} 超過 GPU 蒙皮上限 {GpuSkinBoneLimit}",
                "還是能顯示，但會退回 CPU 蒙皮，同畫面很多隻時會慢。");
        }

        if (root.LogicalMeshes.Any(m => m.Primitives.Any(p => p.MorphTargetsCount > 0)))
            report.Warn("有 morph target（表情變形）", "MU 的模型格式沒有這個概念，會被丟掉。");

        return new ImportedModel(bmd, textures.ToArray(),
            root.LogicalAnimations.Select(a => a.Name ?? $"animation{a.LogicalIndex}").ToArray(), report);
    }

    // ── 骨骼 ─────────────────────────────────────────────────────

    /// <summary>重排過的關節：父在前、子在後。</summary>
    private sealed record Joint(Node Node, Matrix4x4 InverseBind, int Parent);

    /// <summary>
    /// 取出關節並<b>拓撲排序</b>。
    /// </summary>
    /// <remarks>
    /// MU 算骨骼世界矩陣時是一路 <c>local * BoneTransform[parent]</c> 往下乘，
    /// 而且是單層迴圈 —— 也就是<b>假設父骨的索引比子骨小</b>。
    /// glTF 沒有這個保證（Blender 匯出的順序常常不是）。
    /// 不重排的話子骨會乘到「還沒算好的」父矩陣，模型會像被拆開一樣散掉。
    /// </remarks>
    private static (Joint[] Joints, int[] Remap) BuildJoints(Skin? skin, ImportReport report)
    {
        if (skin is null)
        {
            // 沒有骨架的模型（靜態道具）仍然可以用：給一根單位矩陣的根骨。
            report.Info("這個模型沒有骨架", "會當成靜態模型匯入（道具、場景物件多半是這樣）。");
            return ([], []);
        }

        var raw = new List<(Node Node, Matrix4x4 InverseBind)>();
        for (int i = 0; i < skin.JointsCount; i++)
        {
            var (node, inverseBind) = skin.GetJoint(i);
            raw.Add((node, inverseBind));
        }

        var index = raw.Select((j, i) => (j.Node, i)).ToDictionary(x => x.Node, x => x.i);
        var ordered = new List<int>(raw.Count);
        var visiting = new HashSet<int>();

        void Visit(int i)
        {
            if (ordered.Contains(i) || !visiting.Add(i))
                return;

            var parent = raw[i].Node.VisualParent;
            if (parent is not null && index.TryGetValue(parent, out int parentIndex))
                Visit(parentIndex);

            ordered.Add(i);
        }

        for (int i = 0; i < raw.Count; i++)
            Visit(i);

        var position = ordered.Select((original, sorted) => (original, sorted))
            .ToDictionary(x => x.original, x => x.sorted);

        var joints = new Joint[raw.Count];

        for (int sorted = 0; sorted < ordered.Count; sorted++)
        {
            int original = ordered[sorted];
            var parent = raw[original].Node.VisualParent;

            int parentIndex = parent is not null && index.TryGetValue(parent, out int p)
                ? position[p]
                : -1;

            joints[sorted] = new Joint(raw[original].Node, raw[original].InverseBind, parentIndex);
        }

        if (!ordered.Select((o, i) => o == i).All(x => x))
            report.Info("骨骼順序已重排", "MU 需要父骨排在子骨前面，頂點的骨頭索引已同步更新。");

        // 原始關節索引 → 重排後的索引。網格的頂點骨頭索引要照這張表改寫。
        var remap = raw.Select((_, original) => position[original]).ToArray();
        return (joints, remap);
    }

    private static BMDTextureBone[] BuildBones(
        Joint[] joints, int actionCount, ModelRoot root, float fps, ImportReport report)
    {
        if (joints.Length == 0)
        {
            // 靜態模型：一根不動的根骨，所有頂點綁在它上面。
            var single = new BMDTextureBone { Name = "Root", Parent = -1, Matrixes = new BMDBoneMatrix[Math.Max(actionCount, 1)] };

            for (int a = 0; a < single.Matrixes.Length; a++)
            {
                single.Matrixes[a] = new BMDBoneMatrix
                {
                    Position = [Vector3.Zero],
                    Rotation = [Vector3.Zero],
                    Quaternion = [Quaternion.Identity],
                };
            }

            return [single];
        }

        var animations = root.LogicalAnimations.ToArray();
        var bones = new BMDTextureBone[joints.Length];
        bool scaleWarned = false;

        // 根骨要把「骨架之上的所有變換」與座標系轉換一起烘進去。
        //
        // 為什麼不能直接對根骨套一個 +90°：那假設了骨架直接掛在場景根底下。
        // 我們自己匯出的 glTF 就不是 —— 匯出器在骨架上面加了一個 −90° 的座標系節點。
        // 直接套 +90° 會變成兩層旋轉疊加，模型躺著而且不會有任何錯誤訊息。
        //
        // 正確的規則是「把根骨之上的世界矩陣也算進來」：
        //   自己匯出的：parentWorld = conv⁻¹，乘上 conv 之後剛好抵銷 → 原封不動回來
        //   Blender 匯出的：parentWorld = 單位矩陣 → 就是單純的 Y 軸向上轉 Z 軸向上
        var rootParentWorld = joints.Length > 0 && joints[0].Node.VisualParent is { } above
            ? above.WorldMatrix
            : Matrix4x4.Identity;

        var rootTransform = rootParentWorld * Matrix4x4.CreateFromQuaternion(YUpToZUp);

        for (int i = 0; i < joints.Length; i++)
        {
            var joint = joints[i];
            var matrixes = new BMDBoneMatrix[Math.Max(actionCount, 1)];

            for (int a = 0; a < matrixes.Length; a++)
            {
                var animation = a < animations.Length ? animations[a] : null;
                int keys = animation is null ? 1 : KeyCount(animation, fps);

                var positions = new Vector3[keys];
                var rotations = new Vector3[keys];
                var quaternions = new Quaternion[keys];

                for (int k = 0; k < keys; k++)
                {
                    // GetDecomposed()：glTF 的節點變換可以存成「T/R/S 三個欄位」，
                    // 也可以存成一個 4×4 矩陣。存成矩陣時直接讀 .Scale / .Rotation 會丟
                    // InvalidOperationException（"Needs to be in SRT representation"）。
                    // 兩種寫法都合法而且都會遇到 —— Blender 兩種都可能輸出，
                    // 這個專案自己的兩個匯出器就剛好一邊一種。
                    var local = (animation is null
                        ? joint.Node.LocalTransform
                        : joint.Node.GetLocalTransform(animation, k / fps)).GetDecomposed();

                    if (!scaleWarned && (local.Scale - Vector3.One).Length() > 0.01f)
                    {
                        report.Warn("骨骼上有縮放", "MU 的骨骼矩陣只有旋轉與位移，縮放會被忽略。"
                                                + "請在 Blender 裡先 Apply Scale 再匯出。");
                        scaleWarned = true;
                    }

                    Vector3 translation;
                    Quaternion rotation;

                    if (joint.Parent < 0)
                    {
                        // 根骨：把骨架之上的變換與座標系轉換一起烘進區域變換。
                        var effective = Matrix4x4.CreateFromQuaternion(local.Rotation)
                                      * Matrix4x4.CreateTranslation(local.Translation)
                                      * rootTransform;

                        translation = effective.Translation;
                        rotation = Quaternion.CreateFromRotationMatrix(effective);
                    }
                    else
                    {
                        translation = local.Translation;
                        rotation = local.Rotation;
                    }

                    positions[k] = translation;
                    quaternions[k] = Quaternion.Normalize(rotation);
                    rotations[k] = Vector3.Zero; // MU 只在讀檔時用尤拉角推四元數，這裡直接給四元數。
                }

                matrixes[a] = new BMDBoneMatrix
                {
                    Position = positions,
                    Rotation = rotations,
                    Quaternion = quaternions,
                };
            }

            bones[i] = new BMDTextureBone
            {
                Name = string.IsNullOrWhiteSpace(joint.Node.Name) ? $"Bone{i:000}" : joint.Node.Name,
                Parent = (short)joint.Parent,
                Matrixes = matrixes,
            };
        }

        return bones;
    }

    /// <summary>繞 X 軸 +90°：glTF 的 +Y（上）變成 MU 的 +Z。</summary>
    private static readonly Quaternion YUpToZUp = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2f);

    private static BMDTextureAction[] BuildActions(ModelRoot root, float fps, ImportReport report)
    {
        var animations = root.LogicalAnimations.ToArray();

        if (animations.Length == 0)
        {
            report.Info("沒有動畫", "會建立一個單影格的綁定姿勢。");
            return [new BMDTextureAction { NumAnimationKeys = 1, LockPositions = false, PlaySpeed = 1f }];
        }

        return animations.Select(a => new BMDTextureAction
        {
            NumAnimationKeys = KeyCount(a, fps),
            LockPositions = false,
            PlaySpeed = 1f,
        }).ToArray();
    }

    private static int KeyCount(Animation animation, float fps)
        => Math.Clamp((int)MathF.Round(animation.Duration * fps) + 1, 1, 4096);

    // ── 網格 ─────────────────────────────────────────────────────

    private static (List<BMDTextureMesh> Meshes, List<ImportedTexture> Textures) BuildMeshes(
        ModelRoot root, Joint[] joints, int[] jointRemap, ImportReport report)
    {
        var meshes = new List<BMDTextureMesh>();
        var textures = new List<ImportedTexture>();
        var textureNames = new Dictionary<string, string>(StringComparer.Ordinal);

        int blendedVertices = 0;
        int totalVertices = 0;

        foreach (var node in root.DefaultScene?.VisualChildren.SelectMany(Flatten) ?? [])
        {
            if (node.Mesh is null)
                continue;

            // 有骨架時頂點已經在骨骼空間裡處理；沒有骨架時要把節點自己的變換烘進去。
            var nodeMatrix = joints.Length == 0 ? node.WorldMatrix : Matrix4x4.Identity;

            foreach (var primitive in node.Mesh.Primitives)
            {
                var mesh = BuildPrimitive(primitive, joints, jointRemap, nodeMatrix, ref blendedVertices, ref totalVertices);
                if (mesh is null)
                    continue;

                mesh.TexturePath = ResolveTexture(primitive.Material, textures, textureNames);
                meshes.Add(mesh);
            }
        }

        if (blendedVertices > 0)
        {
            report.Warn(
                $"{blendedVertices:N0} / {totalVertices:N0} 個頂點是多骨權重",
                "MU 一個頂點只能綁一根骨頭，這些頂點只保留權重最大的那一根。"
              + "關節處（肩膀、髖部）會比原檔硬。想避免的話請在建模時就用單骨綁定，"
              + "或接受這個限制 —— MU 自己的模型全部都是單骨的。");
        }

        return (meshes, textures);
    }

    private static IEnumerable<Node> Flatten(Node node)
    {
        yield return node;

        foreach (var child in node.VisualChildren)
        {
            foreach (var descendant in Flatten(child))
                yield return descendant;
        }
    }

    private static BMDTextureMesh? BuildPrimitive(
        MeshPrimitive primitive, Joint[] joints, int[] jointRemap, Matrix4x4 nodeMatrix,
        ref int blendedVertices, ref int totalVertices)
    {
        var positions = primitive.GetVertexAccessor("POSITION")?.AsVector3Array();
        if (positions is null || positions.Count == 0)
            return null;

        var triangles = primitive.GetTriangleIndices().ToArray();
        if (triangles.Length == 0)
            return null;

        var normals = primitive.GetVertexAccessor("NORMAL")?.AsVector3Array();
        var texCoords = primitive.GetVertexAccessor("TEXCOORD_0")?.AsVector2Array();
        var jointIndices = primitive.GetVertexAccessor("JOINTS_0")?.AsVector4Array();
        var weights = primitive.GetVertexAccessor("WEIGHTS_0")?.AsVector4Array();

        var vertices = new BMDTextureVertex[positions.Count];
        var meshNormals = new BMDTextureNormal[positions.Count];
        var meshTexCoords = new BMDTexCoord[positions.Count];

        for (int i = 0; i < positions.Count; i++)
        {
            totalVertices++;

            int bone = 0;

            if (jointIndices is not null && weights is not null && joints.Length > 0)
            {
                var w = weights[i];
                var j = jointIndices[i];

                // 單骨綁定：取權重最大的那一根。
                float best = w.X;
                int bestJoint = (int)j.X;

                if (w.Y > best) { best = w.Y; bestJoint = (int)j.Y; }
                if (w.Z > best) { best = w.Z; bestJoint = (int)j.Z; }
                if (w.W > best) { best = w.W; bestJoint = (int)j.W; }

                if (best < 0.999f)
                    blendedVertices++;

                bone = bestJoint < jointRemap.Length ? jointRemap[bestJoint] : bestJoint;
                bone = Math.Clamp(bone, 0, Math.Max(joints.Length - 1, 0));
            }

            var position = Vector3.Transform(positions[i], nodeMatrix);
            var normal = normals is null ? Vector3.UnitZ : Vector3.TransformNormal(normals[i], nodeMatrix);

            // glTF 的頂點在網格空間，MU 的頂點在骨骼的區域空間 —— 差一個 inverse bind matrix。
            if (joints.Length > 0)
            {
                var inverseBind = joints[bone].InverseBind;
                position = Vector3.Transform(position, inverseBind);
                normal = Vector3.TransformNormal(normal, inverseBind);
            }

            vertices[i] = new BMDTextureVertex { Node = (short)bone, Position = position };
            meshNormals[i] = new BMDTextureNormal
            {
                Node = (short)bone,
                Normal = normal.LengthSquared() > 1e-9f ? Vector3.Normalize(normal) : Vector3.UnitZ,
                BindVertex = (short)i,
            };

            var uv = texCoords is null ? Vector2.Zero : texCoords[i];
            meshTexCoords[i] = new BMDTexCoord { U = uv.X, V = uv.Y };
        }

        var faces = new BMDTriangle[triangles.Length];

        for (int i = 0; i < triangles.Length; i++)
        {
            var (a, b, c) = triangles[i];

            faces[i] = new BMDTriangle
            {
                Polygon = 3,
                VertexIndex = [(short)a, (short)b, (short)c, 0],
                NormalIndex = [(short)a, (short)b, (short)c, 0],
                TexCoordIndex = [(short)a, (short)b, (short)c, 0],
                LightMapCoord = [default, default, default, default],
                LightMapIndexes = 0,
            };
        }

        return new BMDTextureMesh
        {
            Vertices = vertices,
            Normals = meshNormals,
            TexCoords = meshTexCoords,
            Triangles = faces,
        };
    }

    private static string ResolveTexture(
        Material? material, List<ImportedTexture> textures, Dictionary<string, string> names)
    {
        var image = material?.FindChannel("BaseColor")?.Texture?.PrimaryImage;
        var content = image?.Content;

        if (content is null || content.Value.Content.Length == 0)
            return string.Empty;

        string key = image!.LogicalIndex.ToString();

        if (names.TryGetValue(key, out var existing))
            return existing;

        // 副檔名與內容都要用 MU 的格式，不能直接留 .png。
        //
        // 客戶端的 TextureLoader.FindTexturePath 有一段 MU 的老慣例：
        // 它會把要求的副檔名<b>換成讀取器對應的格式</b>再去找檔案
        // （BMD 裡寫 foo.jpg，磁碟上其實是 foo.OZJ）。
        // .png 對到 OZPReader，於是它實際去找的是 foo.ozp ——
        // 留一個純 .png 檔在那裡，客戶端<b>永遠找不到</b>，
        // 而且症狀是「檔案明明在、就是載不出來」。
        //
        // 所以這裡直接產生 MU 認得的檔名與檔頭。
        bool isJpg = content.Value.IsJpg;
        string baseName = Sanitize(image.Name ?? material?.Name ?? $"texture{image.LogicalIndex}");

        // Sanitize 之後名字裡可能還留著原本的 .png，去掉免得變成 foo.png.ozp。
        if (baseName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            || baseName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
            baseName = baseName[..^4];

        string name = baseName + (isJpg ? ".ozj" : ".ozp");
        byte[] payload = isJpg
            ? content.Value.Content.ToArray()
            : WrapAsOzp(content.Value.Content.ToArray());

        textures.Add(new ImportedTexture(name, payload));
        names[key] = name;
        return name;
    }

    /// <summary>
    /// 把純 PNG 包成 OZP：前面補 4 個位元組，後面接原本完整的 PNG。
    /// </summary>
    /// <remarks>
    /// OZP 的結構就是「<c>89 50 4E 47</c> ＋ 一份完整的 PNG」，
    /// 所以讀取器砍掉前 4 個位元組之後拿到的正好是合法的 PNG。
    /// </remarks>
    private static byte[] WrapAsOzp(byte[] png)
    {
        var wrapped = new byte[png.Length + 4];
        wrapped[0] = 0x89; wrapped[1] = 0x50; wrapped[2] = 0x4E; wrapped[3] = 0x47;
        png.CopyTo(wrapped, 4);
        return wrapped;
    }

    private static string Sanitize(string name)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
            name = name.Replace(invalid, '_');

        return string.IsNullOrWhiteSpace(name) ? "texture" : name;
    }

    // ── 縮放與量測 ────────────────────────────────────────────────

    /// <summary>蒙皮之後的世界高度（MU 是 Z 軸向上）。</summary>
    private static float MeasureHeight(BMD model)
    {
        var points = ModelComparer.SkinBindPose(model);

        if (points.Count == 0)
            return 0f;

        float min = points.Min(p => p.Z);
        float max = points.Max(p => p.Z);
        return max - min;
    }

    /// <summary>
    /// 等比放大整個模型：頂點與<b>每一根骨頭的每一個位移關鍵影格</b>都要乘。
    /// </summary>
    /// <remarks>
    /// 只縮頂點的話，骨架還是原來的大小，動起來會看到網格從骨架上飛出去；
    /// 只縮骨架的話，綁定姿勢就已經散了。這是等比縮放一個剛體階層的定義：
    /// 所有的平移乘上倍率，旋轉不動。
    /// </remarks>
    private static void ApplyScale(BMD model, float scale)
    {
        foreach (var mesh in model.Meshes ?? [])
        {
            for (int v = 0; v < mesh.Vertices.Length; v++)
                mesh.Vertices[v].Position *= scale;
        }

        foreach (var bone in model.Bones ?? [])
        {
            if (bone is null || bone == BMDTextureBone.Dummy || bone.Matrixes is null)
                continue;

            foreach (var matrix in bone.Matrixes)
            {
                if (matrix.Position is null)
                    continue;

                for (int k = 0; k < matrix.Position.Length; k++)
                    matrix.Position[k] *= scale;
            }
        }
    }
}
