using Client.AssetStudio.Textures;
using Client.Data.BMD;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using NumericsVector3 = System.Numerics.Vector3;
using XnaQuaternion = Microsoft.Xna.Framework.Quaternion;
using XnaVector3 = Microsoft.Xna.Framework.Vector3;

namespace Client.AssetStudio.Rendering;

/// <summary>
/// 一個載進來、可以播動畫的 <c>.bmd</c>。
/// </summary>
/// <remarks>
/// <b>骨骼與取樣算法刻意與 <c>ModelObject.Animation</c> 逐行對齊</b>
/// （<c>Client.Main/Objects/ModelObject.Animation.cs</c> 的 <c>GenerateBoneMatrix</c>）：
/// <code>
/// totalFrames = LockPositions ? NumAnimationKeys - 1 : NumAnimationKeys
/// animTime   += dt * PlaySpeed * AnimationSpeed
/// f0 = (int)(animTime % totalFrames)、f1 = (f0 + 1) % totalFrames、t = 小數部分
/// local = CreateFromQuaternion(Nlerp(q[f0], q[f1], t))，平移為 Position 的線性內插
/// world = local * 父骨的 world
/// </code>
/// 差一個環節（例如用 Slerp、或漏掉 <c>LockPositions</c> 的減一）動作看起來就會
/// 「差不多但不對」—— 而這正是這種工具最沒有價值的失敗方式：看起來對，實際上不是遊戲裡的樣子。
///
/// 蒙皮在 CPU 做。MU 的頂點是<b>單骨綁定</b>（<c>BMDTextureVertex.Node</c> 一個整數，
/// 沒有權重），一隻怪幾千個三角形，每幀重算的成本遠低於為此寫一個自訂 shader ——
/// 而且 macOS 上根本編不了自訂 shader（MGFXC 需要 Wine，見 HANDOFF 第 3 節）。
/// </remarks>
public sealed class AnimatedModel : IDisposable
{
    /// <summary>與 <c>ModelObject.AnimationSpeed</c> 的預設值相同。</summary>
    public const float DefaultAnimationSpeed = 4f;

    private readonly GraphicsDevice _device;
    private readonly Texture2D _white;
    private readonly Dictionary<string, Texture2D?> _textures = new(StringComparer.OrdinalIgnoreCase);

    private Matrix[] _bones = [];

    public BMD Bmd { get; }

    public string Path { get; }

    public string Directory { get; }

    public MeshView[] Meshes { get; private set; }

    /// <summary>掛在同一副骨架上的身體部位（NPC 與角色的可見身體）。</summary>
    public List<BodyPart> Parts { get; } = [];

    /// <summary>綁定姿勢下的包圍盒，相機用它決定初始距離。</summary>
    public BoundingBox Bounds { get; private set; }

    public IReadOnlyList<Matrix> BoneMatrices => _bones;

    public int ActionCount => Bmd.Actions?.Length ?? 0;

    public int BoneCount => Bmd.Bones?.Length ?? 0;

    public int TriangleCount { get; private set; }

    public long FileSize { get; }

    private AnimatedModel(GraphicsDevice device, BMD bmd, string path)
    {
        _device = device;
        Bmd = bmd;
        Path = path;
        Directory = System.IO.Path.GetDirectoryName(path) ?? string.Empty;
        FileSize = new FileInfo(path).Length;

        _white = new Texture2D(device, 1, 1);
        _white.SetData([Color.White]);

        Meshes = (bmd.Meshes ?? []).Select((mesh, index) => new MeshView(mesh, index, Directory)).ToArray();
        TriangleCount = Meshes.Sum(m => m.TriangleCount);

        _bones = new Matrix[BoneCount];
        Sample(0, 0, 0, 0f);
        Bounds = ComputeBounds();
    }

    /// <summary>
    /// 掛一個共用骨架的身體部位。
    /// </summary>
    /// <remarks>
    /// 遊戲端對應的是 <c>LinkParentAnimation</c>：部位模型有自己的網格，
    /// 但骨骼矩陣<b>整份用主模型的</b>。NPC 的主模型（<c>Man01.bmd</c>）常常一個網格都沒有，
    /// 不掛部位的話檢視器就只顯示一副看不見的骨頭 —— 那正是「NPC 只剩人頭」那類問題最初的樣子。
    /// </remarks>
    public void AttachPart(string bmdPath)
    {
        BMD part;

        try
        {
            part = new BMDReader().Load(bmdPath).GetAwaiter().GetResult();
        }
        catch
        {
            return;
        }

        string directory = System.IO.Path.GetDirectoryName(bmdPath) ?? Directory;
        var meshes = (part.Meshes ?? [])
            .Select((mesh, index) => new MeshView(mesh, Meshes.Length + Parts.Sum(p => p.Meshes.Length) + index, directory))
            .ToArray();

        if (meshes.Length == 0)
            return;

        Parts.Add(new BodyPart(System.IO.Path.GetFileName(bmdPath), directory, meshes));

        foreach (var mesh in meshes)
            mesh.Skin(_bones);

        TriangleCount = AllMeshes.Sum(m => m.TriangleCount);
        Bounds = ComputeBounds();
    }

    /// <summary>主模型 + 所有部位的網格，UI 的網格清單用這個。</summary>
    public IEnumerable<MeshView> AllMeshes => Meshes.Concat(Parts.SelectMany(p => p.Meshes));

    /// <summary>
    /// 讀一個 <c>.bmd</c> 並建好可繪製的狀態。<b>必須在主執行緒呼叫。</b>
    /// </summary>
    /// <remarks>
    /// 刻意是同步的。寫成 <c>async Task&lt;AnimatedModel&gt;</c> 再由主執行緒
    /// <c>GetAwaiter().GetResult()</c> 會<b>死鎖</b>：
    /// <c>await</c> 之後的程式（也就是這個建構子）會在執行緒集區的執行緒上跑，
    /// 而建構子要建 <c>Texture2D</c>；MonoGame 的 GL 後端遇到非 UI 執行緒的 GPU 操作
    /// 會用 <c>Threading.BlockOnUIThread</c> 把工作排給主執行緒再等它 ——
    /// 但主執行緒正卡在 <c>GetResult()</c> 等這個 Task。兩邊互等，沒有任何錯誤訊息。
    ///
    /// 這裡把 <c>await</c> 收在方法內部：BMD 的讀檔仍然可以在集區完成，
    /// 但<b>建構子一定回到呼叫端的執行緒</b>執行。
    /// </remarks>
    public static AnimatedModel Load(GraphicsDevice device, string path)
    {
        var bmd = new BMDReader().Load(path).GetAwaiter().GetResult();
        return new AnimatedModel(device, bmd, path);
    }

    // ── 動畫取樣 ──────────────────────────────────────────────────

    /// <summary>這個動作的可播放格數。<c>LockPositions</c> 的動作最後一格是位移資料，不參與循環。</summary>
    public int FrameCount(int action)
    {
        var actions = Bmd.Actions;
        if (actions is null || (uint)action >= (uint)actions.Length || actions[action] is null)
            return 1;

        var a = actions[action];
        return Math.Max(a.LockPositions ? a.NumAnimationKeys - 1 : a.NumAnimationKeys, 1);
    }

    public float PlaySpeed(int action)
    {
        var actions = Bmd.Actions;
        if (actions is null || (uint)action >= (uint)actions.Length || actions[action] is null)
            return 1f;

        return actions[action].PlaySpeed == 0f ? 1f : actions[action].PlaySpeed;
    }

    /// <summary>把骨架擺到 (動作, 影格0, 影格1, 內插) 這個姿勢，並重新蒙皮所有網格。</summary>
    public void Sample(int action, int frame0, int frame1, float t)
    {
        var bones = Bmd.Bones;
        if (bones is null || bones.Length == 0)
            return;

        if (_bones.Length != bones.Length)
            _bones = new Matrix[bones.Length];

        var actions = Bmd.Actions ?? [];
        int actionIndex = actions.Length == 0 ? -1 : Math.Clamp(action, 0, actions.Length - 1);
        bool lockPositions = actionIndex >= 0 && actions[actionIndex] is { LockPositions: true };

        for (int i = 0; i < bones.Length; i++)
        {
            var bone = bones[i];

            if (actionIndex < 0 || bone is null || bone == BMDTextureBone.Dummy
                || bone.Matrixes is null || actionIndex >= bone.Matrixes.Length)
            {
                _bones[i] = Matrix.Identity;
                continue;
            }

            var matrix = bone.Matrixes[actionIndex];
            int positionKeys = matrix.Position?.Length ?? 0;
            int quaternionKeys = matrix.Quaternion?.Length ?? 0;

            if (positionKeys == 0 || quaternionKeys == 0)
            {
                _bones[i] = Matrix.Identity;
                continue;
            }

            // 每根骨頭的關鍵影格數可以少於動作宣告的數量，各自夾住。
            int maxFrame = Math.Min(positionKeys, quaternionKeys) - 1;
            int f0 = Math.Clamp(frame0, 0, maxFrame);
            int f1 = Math.Clamp(frame1, 0, maxFrame);
            float blend = f0 == f1 ? 0f : t;

            Matrix local;

            if (blend == 0f)
            {
                local = Matrix.CreateFromQuaternion(ToXna(matrix.Quaternion![f0]));
                local.Translation = ToXna(matrix.Position![f0]);
            }
            else
            {
                local = Matrix.CreateFromQuaternion(Nlerp(ToXna(matrix.Quaternion![f0]), ToXna(matrix.Quaternion[f1]), blend));

                var p0 = matrix.Position![f0];
                var p1 = matrix.Position[f1];
                local.M41 = p0.X + ((p1.X - p0.X) * blend);
                local.M42 = p0.Y + ((p1.Y - p0.Y) * blend);
                local.M43 = p0.Z + ((p1.Z - p0.Z) * blend);
            }

            // 根骨在 LockPositions 的動作裡不跟著位移曲線跑（那條曲線是給世界移動用的）。
            if (i == 0 && lockPositions && positionKeys > 0)
            {
                var root = matrix.Position![0];
                local.Translation = new XnaVector3(root.X, root.Y, local.M43);
            }

            _bones[i] = bone.Parent >= 0 && bone.Parent < bones.Length
                ? local * _bones[bone.Parent]
                : local;
        }

        foreach (var mesh in AllMeshes)
            mesh.Skin(_bones);
    }

    /// <summary>依經過時間推進，回傳新的動畫時間（單位是影格）。</summary>
    public double Advance(double animTime, int action, float deltaSeconds, float animationSpeed)
    {
        int totalFrames = FrameCount(action);
        double advanced = animTime + (deltaSeconds * PlaySpeed(action) * animationSpeed);

        return totalFrames <= 1 ? 0d : advanced % totalFrames;
    }

    /// <summary>把「動畫時間」換成兩格加內插值，然後取樣。</summary>
    public (int Frame0, int Frame1, float T) Apply(int action, double animTime)
    {
        int totalFrames = FrameCount(action);
        double position = totalFrames <= 1 ? 0d : animTime % totalFrames;

        int frame0 = (int)position;
        int frame1 = totalFrames <= 1 ? 0 : (frame0 + 1) % totalFrames;
        float t = (float)(position - frame0);

        Sample(action, frame0, frame1, t);
        return (frame0, frame1, t);
    }

    // ── 繪製 ─────────────────────────────────────────────────────

    public void Draw(BasicEffect effect, RenderOptions options)
    {
        var previousBlend = _device.BlendState;
        var previousDepth = _device.DepthStencilState;
        var previousRasterizer = _device.RasterizerState;

        try
        {
            // 兩趟：先畫不透明的，再畫半透明的。同一趟裡畫會讓半透明網格被自己的
            // 深度寫入切掉後面的部分（怪物的翅膀、光暈幾乎都是這樣壞掉的）。
            DrawPass(effect, options, transparent: false);
            DrawPass(effect, options, transparent: true);
        }
        finally
        {
            _device.BlendState = previousBlend;
            _device.DepthStencilState = previousDepth;
            _device.RasterizerState = previousRasterizer;
        }
    }

    private void DrawPass(BasicEffect effect, RenderOptions options, bool transparent)
    {
        _device.BlendState = transparent ? BlendState.NonPremultiplied : BlendState.Opaque;
        _device.DepthStencilState = transparent ? DepthStencilState.DepthRead : DepthStencilState.Default;
        _device.RasterizerState = options.Wireframe
            ? WireframeState
            : RasterizerState.CullNone;

        foreach (var mesh in AllMeshes)
        {
            if (!mesh.Visible || mesh.Vertices.Length < 3)
                continue;

            if (mesh.IsTransparent != transparent)
                continue;

            effect.Texture = options.ShowTextures ? (Resolve(mesh) ?? _white) : _white;
            effect.TextureEnabled = true;

            foreach (var pass in effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                _device.DrawUserPrimitives(PrimitiveType.TriangleList, mesh.Vertices, 0, mesh.Vertices.Length / 3);
            }
        }
    }

    private static readonly RasterizerState WireframeState = new()
    {
        FillMode = FillMode.WireFrame,
        CullMode = CullMode.None,
    };

    /// <summary>骨架的線段（父骨 → 子骨），給「顯示骨骼」用。</summary>
    public VertexPositionColor[] BuildSkeletonLines()
    {
        var bones = Bmd.Bones ?? [];
        var lines = new List<VertexPositionColor>(bones.Length * 2);

        for (int i = 0; i < bones.Length && i < _bones.Length; i++)
        {
            var bone = bones[i];
            if (bone is null || bone == BMDTextureBone.Dummy)
                continue;

            if (bone.Parent < 0 || bone.Parent >= _bones.Length)
                continue;

            lines.Add(new VertexPositionColor(_bones[bone.Parent].Translation, Color.Orange));
            lines.Add(new VertexPositionColor(_bones[i].Translation, Color.Yellow));
        }

        return lines.ToArray();
    }

    private Texture2D? Resolve(MeshView mesh)
    {
        if (_textures.TryGetValue(mesh.TexturePath, out var cached))
            return cached;

        Texture2D? texture = null;

        if (mesh.Texture.Found)
        {
            try
            {
                texture = Client.MapEditor.TextureDecoder.Decode(_device, mesh.Texture.FullPath!);
            }
            catch
            {
                texture = null;
            }
        }

        _textures[mesh.TexturePath] = texture;
        return texture;
    }

    /// <summary>換過貼圖檔之後叫這個，下次繪製會重新解碼。</summary>
    public void ReloadTextures()
    {
        foreach (var texture in _textures.Values)
            texture?.Dispose();

        _textures.Clear();
        TextureResolver.Invalidate(Directory);

        foreach (var part in Parts)
            TextureResolver.Invalidate(part.Directory);

        foreach (var mesh in AllMeshes)
            mesh.RefreshTexture();
    }

    private BoundingBox ComputeBounds()
    {
        var min = new XnaVector3(float.MaxValue);
        var max = new XnaVector3(float.MinValue);
        bool any = false;

        foreach (var mesh in AllMeshes)
        {
            foreach (var vertex in mesh.Vertices)
            {
                min = XnaVector3.Min(min, vertex.Position);
                max = XnaVector3.Max(max, vertex.Position);
                any = true;
            }
        }

        return any ? new BoundingBox(min, max) : new BoundingBox(XnaVector3.Zero, XnaVector3.One * 100f);
    }

    /// <summary>正規化線性內插。與 <c>ModelObject.Nlerp</c> 相同 —— 不是 Slerp。</summary>
    private static XnaQuaternion Nlerp(XnaQuaternion a, XnaQuaternion b, float t)
    {
        // 走短弧：四元數 q 與 -q 是同一個旋轉，點積為負時要翻一邊，否則會繞遠路。
        if (XnaQuaternion.Dot(a, b) < 0f)
            b = new XnaQuaternion(-b.X, -b.Y, -b.Z, -b.W);

        var result = new XnaQuaternion(
            a.X + ((b.X - a.X) * t),
            a.Y + ((b.Y - a.Y) * t),
            a.Z + ((b.Z - a.Z) * t),
            a.W + ((b.W - a.W) * t));

        return XnaQuaternion.Normalize(result);
    }

    private static XnaVector3 ToXna(NumericsVector3 value) => new(value.X, value.Y, value.Z);

    private static XnaQuaternion ToXna(System.Numerics.Quaternion value) => new(value.X, value.Y, value.Z, value.W);

    public void Dispose()
    {
        foreach (var texture in _textures.Values)
            texture?.Dispose();

        _textures.Clear();
        _white.Dispose();
    }

    public sealed record RenderOptions(bool ShowTextures, bool Wireframe);

    /// <param name="Directory">部位的貼圖在它自己的資料夾找，不一定與主模型同一個。</param>
    public sealed record BodyPart(string FileName, string Directory, MeshView[] Meshes);
}
