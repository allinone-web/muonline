using Client.AssetStudio.Textures;
using Client.Data.BMD;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using NumericsVector3 = System.Numerics.Vector3;
using XnaVector3 = Microsoft.Xna.Framework.Vector3;

namespace Client.AssetStudio.Rendering;

/// <summary>
/// 一個網格攤平成三角形清單，並保留「每個頂點綁在哪根骨頭」以便每幀重新蒙皮。
/// </summary>
/// <remarks>
/// MU 的頂點是<b>單骨綁定</b>：<see cref="BMDTextureVertex.Node"/> 是一個整數，沒有權重陣列，
/// 所以蒙皮就是一次矩陣乘法。法線<b>另有自己的骨頭索引</b>
/// （<see cref="BMDTextureNormal.Node"/>），與頂點的不一定相同 —— 拿頂點的骨頭去轉法線，
/// 受光會在關節處出現一圈錯誤的暗帶。
/// </remarks>
public sealed class MeshView
{
    private readonly SkinVertex[] _source;

    public MeshView(BMDTextureMesh mesh, int index, string modelDirectory)
    {
        Index = index;
        Directory = modelDirectory;
        TexturePath = mesh.TexturePath ?? string.Empty;
        Texture = TextureResolver.Resolve(modelDirectory, TexturePath);

        _source = Flatten(mesh);
        Vertices = new VertexPositionNormalTexture[_source.Length];

        for (int i = 0; i < _source.Length; i++)
        {
            Vertices[i] = new VertexPositionNormalTexture(
                _source[i].Position,
                _source[i].Normal,
                _source[i].TexCoord);
        }
    }

    public int Index { get; }

    /// <summary>
    /// 這個網格的貼圖要在哪個資料夾找。
    /// </summary>
    /// <remarks>
    /// 不能用主模型的資料夾：身體部位是另外載入的模型，貼圖跟著它自己走。
    /// 匯入一張新貼圖時寫錯資料夾，遊戲仍然找不到它 —— 而且完全不會報錯。
    /// </remarks>
    public string Directory { get; }

    public string TexturePath { get; }

    public TextureResolver.Resolution Texture { get; private set; }

    /// <summary>UI 可以逐一關掉網格 —— 換素材時要看清楚哪一塊是哪一塊。</summary>
    public bool Visible { get; set; } = true;

    /// <summary>
    /// 帶 alpha 通道的貼圖（<c>.ozt</c> / <c>.ozp</c> / DXT3/5 的 <c>.ozd</c>）在遊戲裡
    /// 走的是半透明路徑（<c>ModelObject.IsRgba</c>）。這裡沿用同一個判斷。
    /// </summary>
    public bool IsTransparent
    {
        get => _transparentOverride ?? DefaultTransparent;
        set => _transparentOverride = value;
    }

    private bool? _transparentOverride;

    public bool DefaultTransparent => Texture.Found
        && Path.GetExtension(Texture.FullPath!).ToLowerInvariant() is ".ozt" or ".ozp" or ".ozd" or ".png" or ".tga";

    public void ResetTransparency() => _transparentOverride = null;

    public VertexPositionNormalTexture[] Vertices { get; }

    public int TriangleCount => Vertices.Length / 3;

    public int VertexCount => _source.Length;

    public void RefreshTexture() => Texture = TextureResolver.Resolve(Directory, TexturePath);

    /// <summary>把綁定姿勢的頂點依骨骼矩陣重新算成這一幀的位置。</summary>
    public void Skin(Matrix[] bones)
    {
        for (int i = 0; i < _source.Length; i++)
        {
            ref readonly var source = ref _source[i];

            var boneMatrix = (uint)source.PositionBone < (uint)bones.Length
                ? bones[source.PositionBone]
                : Matrix.Identity;

            var normalMatrix = (uint)source.NormalBone < (uint)bones.Length
                ? bones[source.NormalBone]
                : Matrix.Identity;

            var normal = XnaVector3.TransformNormal(source.Normal, normalMatrix);
            if (normal.LengthSquared() > 1e-6f)
                normal.Normalize();
            else
                normal = XnaVector3.UnitZ;

            Vertices[i].Position = XnaVector3.Transform(source.Position, boneMatrix);
            Vertices[i].Normal = normal;
        }
    }

    /// <summary>
    /// 三角形攤平。<c>Polygon</c> 是這個面的角數；四邊形要拆成兩個三角形，
    /// 否則第四個角會被丟掉，模型上出現隨機的破洞。
    /// </summary>
    private static SkinVertex[] Flatten(BMDTextureMesh mesh)
    {
        var vertices = new List<SkinVertex>(mesh.Triangles.Length * 3);

        foreach (var triangle in mesh.Triangles)
        {
            int corners = triangle.Polygon >= 4 ? 4 : 3;
            ReadOnlySpan<int> order = corners == 4 ? [0, 1, 2, 0, 2, 3] : [0, 1, 2];

            int before = vertices.Count;
            bool ok = true;

            foreach (int corner in order)
            {
                if (!TryBuild(mesh, triangle, corner, out var vertex))
                {
                    ok = false;
                    break;
                }

                vertices.Add(vertex);
            }

            // 壞掉的面只丟這一個面，不是整個網格 —— 官方資源裡確實有個別索引越界的三角形。
            if (!ok)
                vertices.RemoveRange(before, vertices.Count - before);
        }

        return vertices.ToArray();
    }

    private static bool TryBuild(BMDTextureMesh mesh, BMDTriangle triangle, int corner, out SkinVertex vertex)
    {
        vertex = default;

        int vertexIndex = triangle.VertexIndex[corner];
        if ((uint)vertexIndex >= (uint)mesh.Vertices.Length)
            return false;

        var source = mesh.Vertices[vertexIndex];

        var normal = XnaVector3.UnitZ;
        int normalBone = source.Node;

        int normalIndex = triangle.NormalIndex[corner];
        if ((uint)normalIndex < (uint)mesh.Normals.Length)
        {
            var sourceNormal = mesh.Normals[normalIndex];
            normal = ToXna(sourceNormal.Normal);
            normalBone = sourceNormal.Node;
        }

        int texCoordIndex = triangle.TexCoordIndex[corner];
        var uv = (uint)texCoordIndex < (uint)mesh.TexCoords.Length
            ? new Vector2(mesh.TexCoords[texCoordIndex].U, mesh.TexCoords[texCoordIndex].V)
            : Vector2.Zero;

        vertex = new SkinVertex(ToXna(source.Position), normal, uv, source.Node, normalBone);
        return true;
    }

    private static XnaVector3 ToXna(NumericsVector3 value) => new(value.X, value.Y, value.Z);

    private readonly record struct SkinVertex(
        XnaVector3 Position,
        XnaVector3 Normal,
        Vector2 TexCoord,
        int PositionBone,
        int NormalBone);
}
