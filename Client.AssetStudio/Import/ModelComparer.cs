using System.Numerics;
using Client.Data.BMD;

namespace Client.AssetStudio.Import;

public sealed record ComparisonResult(
    int VerticesA,
    int VerticesB,
    int BonesA,
    int BonesB,
    int TrianglesA,
    int TrianglesB,
    Vector3 SizeA,
    Vector3 SizeB,
    float MeanDistance,
    float MaxDistance)
{
    /// <summary>誤差相對於模型尺寸的比例。絕對值沒有意義 —— 一隻怪 200 單位高、一把劍 7 單位寬。</summary>
    public float RelativeError => SizeA.Length() > 0.001f ? MaxDistance / SizeA.Length() : MaxDistance;

    /// <summary>兩側都要有點才比得下去。空的那一側代表模型讀不出幾何，不是誤差大。</summary>
    public bool Comparable => VerticesA > 0 && VerticesB > 0 && !float.IsNaN(MaxDistance);
}

/// <summary>
/// 兩個模型在綁定姿勢下的幾何比對。
/// </summary>
/// <remarks>
/// 這是匯出／匯入這一對的驗收工具：<b>把遊戲裡的模型匯出成 glTF、再匯回來，
/// 幾何應該回到原處。</b>回不去就代表兩個方向至少有一個弄錯了座標系、
/// 骨骼順序或頂點空間 —— 而這三件事錯掉的症狀都是「模型看起來怪怪的」，
/// 用眼睛判斷不了程度。
///
/// 比對的是<b>蒙皮之後的世界座標點雲</b>，不是逐頂點對應：
/// glTF 匯出時會依 (頂點, 法線, UV) 重新編號，順序本來就不會一樣。
/// 所以量的是「B 的每個點離 A 最近的點有多遠」。
/// </remarks>
public static class ModelComparer
{
    public static ComparisonResult Compare(BMD a, BMD b) => Compare(a, [], b);

    /// <param name="partsA">
    /// 與 <paramref name="a"/> 共用骨架的身體部位。NPC 與角色的主模型常常一個網格都沒有
    /// （<c>Player.bmd</c> 是純骨架），不把部位算進來的話「應該長什麼樣」那一側是空的，
    /// 比對出來的誤差會是天文數字，看起來像匯入器壞了。
    /// </param>
    public static ComparisonResult Compare(BMD a, IReadOnlyList<BMD> partsA, BMD b)
    {
        var pointsA = SkinBindPose(a, partsA);
        var pointsB = SkinBindPose(b);

        if (pointsA.Count == 0 || pointsB.Count == 0)
        {
            return new ComparisonResult(
                pointsA.Count, pointsB.Count,
                a.Bones?.Length ?? 0, b.Bones?.Length ?? 0,
                a.Meshes?.Sum(m => m.Triangles.Length) ?? 0,
                b.Meshes?.Sum(m => m.Triangles.Length) ?? 0,
                Size(pointsA), Size(pointsB),
                float.NaN, float.NaN);
        }

        float total = 0f;
        float max = 0f;

        // O(n·m)。一隻怪幾百到幾千個頂點，這裡是離線的驗收工具，不值得為它建空間結構。
        foreach (var point in pointsB)
        {
            float best = float.MaxValue;

            foreach (var candidate in pointsA)
                best = MathF.Min(best, Vector3.DistanceSquared(point, candidate));

            best = MathF.Sqrt(best);
            total += best;
            max = MathF.Max(max, best);
        }

        return new ComparisonResult(
            pointsA.Count, pointsB.Count,
            a.Bones?.Length ?? 0, b.Bones?.Length ?? 0,
            (a.Meshes?.Sum(m => m.Triangles.Length) ?? 0)
                + partsA.Sum(p => p.Meshes?.Sum(m => m.Triangles.Length) ?? 0),
            b.Meshes?.Sum(m => m.Triangles.Length) ?? 0,
            Size(pointsA), Size(pointsB),
            total / pointsB.Count,
            max);
    }

    /// <summary>
    /// 綁定姿勢（動作 0、影格 0）下所有頂點的世界座標。
    /// </summary>
    /// <remarks>
    /// 骨骼算法與 <c>ModelObject.GenerateBoneMatrix</c> 一致 ——
    /// 這裡是純 <c>System.Numerics</c>，不碰 MonoGame，所以命令列模式也能跑。
    /// </remarks>
    public static List<Vector3> SkinBindPose(BMD model) => SkinBindPose(model, []);

    /// <param name="parts">共用 <paramref name="model"/> 骨架的身體部位（遊戲端的 LinkParentAnimation）。</param>
    public static List<Vector3> SkinBindPose(BMD model, IReadOnlyList<BMD> parts)
    {
        var bones = model.Bones ?? [];
        var matrices = new Matrix4x4[bones.Length];

        for (int i = 0; i < bones.Length; i++)
        {
            matrices[i] = Matrix4x4.Identity;

            var bone = bones[i];
            if (bone is null || bone == BMDTextureBone.Dummy || bone.Matrixes is not { Length: > 0 })
                continue;

            var matrix = bone.Matrixes[0];
            if (matrix.Quaternion is not { Length: > 0 } || matrix.Position is not { Length: > 0 })
                continue;

            var local = Matrix4x4.CreateFromQuaternion(matrix.Quaternion[0]);
            local.Translation = matrix.Position[0];

            matrices[i] = bone.Parent >= 0 && bone.Parent < i
                ? local * matrices[bone.Parent]
                : local;
        }

        var points = new List<Vector3>();

        // 部位用主模型的骨骼矩陣，與遊戲端的 LinkParentAnimation 以及匯出器一致。
        foreach (var source in new[] { model }.Concat(parts))
        {
            foreach (var mesh in source.Meshes ?? [])
            {
                foreach (var vertex in mesh.Vertices)
                {
                    var bone = (uint)vertex.Node < (uint)matrices.Length ? matrices[vertex.Node] : Matrix4x4.Identity;
                    points.Add(Vector3.Transform(vertex.Position, bone));
                }
            }
        }

        return points;
    }

    private static Vector3 Size(List<Vector3> points)
    {
        if (points.Count == 0)
            return Vector3.Zero;

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);

        foreach (var point in points)
        {
            min = Vector3.Min(min, point);
            max = Vector3.Max(max, point);
        }

        return max - min;
    }
}
