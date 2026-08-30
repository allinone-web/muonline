using Client.Data.BMD;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using NumericsVector3 = System.Numerics.Vector3;
using XnaVector3 = Microsoft.Xna.Framework.Vector3;
using MuAssets.Core;

namespace Client.MapEditor;

/// <summary>
/// 把一個 <c>.bmd</c> 模型以綁定姿勢（action 0 / frame 0）畫成一張縮圖。
/// </summary>
/// <remarks>
/// 刻意不走 <c>Client.Main.Objects.ModelObject</c>：那條路綁著 <c>World</c>、<c>Camera.Instance</c>、
/// <c>GraphicsManager</c> 與整套動畫狀態機，為了畫一張靜態縮圖把它們拉起來不划算。
/// 這裡只做最小的事 —— 算骨骼矩陣、攤平三角形、用 <see cref="BasicEffect"/> 畫。
///
/// 骨骼算法與 <c>ModelObject.Animation</c> 一致：
/// <c>local = CreateFromQuaternion(q[0])</c>、平移取 <c>Position[0]</c>、
/// 再乘上父骨的世界矩陣。
/// </remarks>
public sealed class BmdThumbnailRenderer : IDisposable
{
    /// <summary>客戶端會把要求的副檔名換成 reader 支援的再找，順序與 TextureLoader 一致。</summary>
    private static readonly string[] TextureExtensions = ["ozj", "ozt", "ozd", "ozp", "jpg", "tga", "png", "bmp"];

    private readonly GraphicsDevice _device;
    private readonly BasicEffect _effect;
    private readonly RenderTarget2D _target;
    private readonly Texture2D _white;
    private readonly Dictionary<string, Texture2D?> _textures = new(StringComparer.OrdinalIgnoreCase);

    public int Size { get; }

    public BmdThumbnailRenderer(GraphicsDevice device, int size = 128)
    {
        _device = device;
        Size = size;

        _target = new RenderTarget2D(device, size, size, mipMap: false, SurfaceFormat.Color, DepthFormat.Depth24);

        _effect = new BasicEffect(device)
        {
            TextureEnabled = true,
            VertexColorEnabled = false,
            LightingEnabled = true,
        };
        _effect.EnableDefaultLighting();
        _effect.PreferPerPixelLighting = true;

        _white = new Texture2D(device, 1, 1);
        _white.SetData([Color.White]);
    }

    /// <summary>
    /// 畫一張縮圖。回傳的 <see cref="Texture2D"/> 由呼叫端負責釋放。
    /// 模型讀不到或沒有可見網格時回傳 null。
    /// </summary>
    public Texture2D? Render(string bmdPath)
    {
        BMD model;
        try
        {
            model = new BMDReader().Load(bmdPath).GetAwaiter().GetResult();
        }
        catch
        {
            return null;
        }

        return Render(model, Path.GetDirectoryName(bmdPath) ?? string.Empty);
    }

    /// <summary>
    /// 直接畫一個已經在記憶體裡的 <see cref="BMD"/>。
    /// </summary>
    /// <remarks>
    /// 給資源庫的自有資產用：那些是 glTF，磁碟上沒有 .bmd，
    /// BMD 是 <c>GltfImporter</c> 當場轉出來的（見 <c>EntityCatalog.LibraryEntries</c>）。
    /// 沒有這個多載，外部匯入的資產在縮圖牆上永遠只會顯示「...」。
    /// </remarks>
    /// <param name="textureDirectory">找貼圖的目錄。BMD 裡存的是檔名，不是路徑。</param>
    public Texture2D? Render(BMD model, string textureDirectory)
    {
        string bmdPath = Path.Combine(textureDirectory, "in-memory.bmd");
        var bones = BuildBoneMatrices(model);
        var meshes = BuildMeshes(model, bones, out var bounds);

        if (meshes.Count == 0)
            return null;

        var previousTargets = _device.GetRenderTargets();
        var previousBlend = _device.BlendState;
        var previousDepth = _device.DepthStencilState;
        var previousRasterizer = _device.RasterizerState;

        try
        {
            _device.SetRenderTarget(_target);
            _device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, new Color(0, 0, 0, 0), 1f, 0);
            _device.BlendState = BlendState.AlphaBlend;
            _device.DepthStencilState = DepthStencilState.Default;
            _device.RasterizerState = RasterizerState.CullNone;

            ConfigureCamera(bounds);

            string modelDirectory = textureDirectory;

            foreach (var mesh in meshes)
            {
                _effect.Texture = ResolveTexture(modelDirectory, mesh.TexturePath) ?? _white;

                foreach (var pass in _effect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    _device.DrawUserPrimitives(PrimitiveType.TriangleList, mesh.Vertices, 0, mesh.Vertices.Length / 3);
                }
            }

            // RenderTarget2D 在下次 SetRenderTarget 之後才安全可讀，所以複製一份出來。
            var pixels = new Color[Size * Size];
            _target.GetData(pixels);

            var result = new Texture2D(_device, Size, Size);
            result.SetData(pixels);
            return result;
        }
        catch
        {
            return null;
        }
        finally
        {
            _device.SetRenderTargets(previousTargets);
            _device.BlendState = previousBlend;
            _device.DepthStencilState = previousDepth;
            _device.RasterizerState = previousRasterizer;
        }
    }

    private void ConfigureCamera(BoundingBox bounds)
    {
        var center = (bounds.Min + bounds.Max) * 0.5f;
        float radius = MathF.Max((bounds.Max - bounds.Min).Length() * 0.5f, 1f);

        // 從斜上前方看，接近遊戲裡的觀察角度，體積感最清楚。
        var direction = XnaVector3.Normalize(new XnaVector3(-1f, -1.4f, 0.85f));
        float distance = radius * 2.4f;

        _effect.World = Matrix.Identity;
        _effect.View = Matrix.CreateLookAt(center - (direction * distance), center, XnaVector3.UnitZ);
        _effect.Projection = Matrix.CreatePerspectiveFieldOfView(
            MathHelper.ToRadians(35f),
            1f,
            MathF.Max(distance - (radius * 2f), 1f),
            distance + (radius * 3f));
    }

    /// <summary>算出綁定姿勢（action 0 / frame 0）的骨骼世界矩陣。</summary>
    private static Matrix[] BuildBoneMatrices(BMD model)
    {
        var bones = model.Bones ?? [];
        var output = new Matrix[bones.Length];

        for (int i = 0; i < bones.Length; i++)
        {
            output[i] = Matrix.Identity;

            var bone = bones[i];
            if (bone is null || bone == BMDTextureBone.Dummy || bone.Matrixes.Length == 0)
                continue;

            var matrix = bone.Matrixes[0];
            if (matrix.Quaternion is not { Length: > 0 } || matrix.Position is not { Length: > 0 })
                continue;

            var q = matrix.Quaternion[0];
            var local = Matrix.CreateFromQuaternion(new Microsoft.Xna.Framework.Quaternion(q.X, q.Y, q.Z, q.W));
            local.Translation = ToXna(matrix.Position[0]);

            output[i] = bone.Parent >= 0 && bone.Parent < i
                ? local * output[bone.Parent]
                : local;
        }

        return output;
    }

    private static List<ThumbnailMesh> BuildMeshes(BMD model, Matrix[] bones, out BoundingBox bounds)
    {
        var meshes = new List<ThumbnailMesh>();
        var min = new XnaVector3(float.MaxValue);
        var max = new XnaVector3(float.MinValue);

        foreach (var mesh in model.Meshes ?? [])
        {
            var vertices = new List<VertexPositionNormalTexture>(mesh.Triangles.Length * 3);

            foreach (var triangle in mesh.Triangles)
            {
                // Polygon 是這個面的頂點數；MU 的模型幾乎都是三角形，四邊形拆成兩個三角形。
                int cornerCount = triangle.Polygon >= 4 ? 4 : 3;
                Span<int> order = cornerCount == 4 ? [0, 1, 2, 0, 2, 3] : [0, 1, 2];

                foreach (int corner in order)
                {
                    if (!TryBuildVertex(model, mesh, bones, triangle, corner, out var vertex))
                    {
                        vertices.Clear();
                        break;
                    }

                    vertices.Add(vertex);
                    min = XnaVector3.Min(min, vertex.Position);
                    max = XnaVector3.Max(max, vertex.Position);
                }
            }

            if (vertices.Count >= 3)
                meshes.Add(new ThumbnailMesh(vertices.ToArray(), mesh.TexturePath));
        }

        bounds = meshes.Count > 0
            ? new BoundingBox(min, max)
            : new BoundingBox(XnaVector3.Zero, XnaVector3.One);

        return meshes;
    }

    private static bool TryBuildVertex(
        BMD model,
        BMDTextureMesh mesh,
        Matrix[] bones,
        BMDTriangle triangle,
        int corner,
        out VertexPositionNormalTexture vertex)
    {
        vertex = default;

        int vertexIndex = triangle.VertexIndex[corner];
        int normalIndex = triangle.NormalIndex[corner];
        int texCoordIndex = triangle.TexCoordIndex[corner];

        if ((uint)vertexIndex >= (uint)mesh.Vertices.Length)
            return false;

        var source = mesh.Vertices[vertexIndex];
        var bone = (uint)source.Node < (uint)bones.Length ? bones[source.Node] : Matrix.Identity;

        var position = XnaVector3.Transform(ToXna(source.Position), bone);

        var normal = XnaVector3.UnitZ;
        if ((uint)normalIndex < (uint)mesh.Normals.Length)
        {
            var sourceNormal = mesh.Normals[normalIndex];
            var boneForNormal = (uint)sourceNormal.Node < (uint)bones.Length ? bones[sourceNormal.Node] : Matrix.Identity;
            normal = XnaVector3.TransformNormal(ToXna(sourceNormal.Normal), boneForNormal);

            if (normal.LengthSquared() > 1e-6f)
                normal.Normalize();
            else
                normal = XnaVector3.UnitZ;
        }

        var uv = (uint)texCoordIndex < (uint)mesh.TexCoords.Length
            ? new Vector2(mesh.TexCoords[texCoordIndex].U, mesh.TexCoords[texCoordIndex].V)
            : Vector2.Zero;

        vertex = new VertexPositionNormalTexture(position, normal, uv);
        return true;
    }

    private Texture2D? ResolveTexture(string modelDirectory, string texturePath)
    {
        if (string.IsNullOrWhiteSpace(texturePath))
            return null;

        if (_textures.TryGetValue(texturePath, out var cached))
            return cached;

        Texture2D? texture = null;
        string? file = FindTextureFile(modelDirectory, texturePath);

        if (file is not null)
        {
            try
            {
                texture = TextureDecoder.Decode(_device, file);
            }
            catch
            {
                texture = null;
            }
        }

        _textures[texturePath] = texture;
        return texture;
    }

    /// <summary>與 <c>tools/AssetCheck</c> 相同的找法：換副檔名 + 大小寫容錯 + 也找 texture/ 子目錄。</summary>
    private static string? FindTextureFile(string directory, string texturePath)
    {
        string baseName = Path.GetFileNameWithoutExtension(texturePath);

        foreach (var extension in TextureExtensions)
        {
            string candidate = ResolveCaseInsensitive(Path.Combine(directory, $"{baseName}.{extension}"));
            if (candidate is not null)
                return candidate;

            string nested = ResolveCaseInsensitive(Path.Combine(directory, "texture", $"{baseName}.{extension}"));
            if (nested is not null)
                return nested;
        }

        return null;
    }

    private static string? ResolveCaseInsensitive(string path)
    {
        if (File.Exists(path))
            return path;

        string? directory = Path.GetDirectoryName(path);
        string name = Path.GetFileName(path);

        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            return null;

        return Directory.EnumerateFiles(directory)
            .FirstOrDefault(f => string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase));
    }

    private static XnaVector3 ToXna(NumericsVector3 value) => new(value.X, value.Y, value.Z);

    public void Dispose()
    {
        foreach (var texture in _textures.Values)
            texture?.Dispose();

        _textures.Clear();
        _white.Dispose();
        _effect.Dispose();
        _target.Dispose();
    }

    private readonly record struct ThumbnailMesh(VertexPositionNormalTexture[] Vertices, string TexturePath);
}
