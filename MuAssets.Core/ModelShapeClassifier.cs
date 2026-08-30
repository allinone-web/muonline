using Client.Data.BMD;

namespace MuAssets.Core;

/// <summary>一個模型的形狀與貼圖特徵。</summary>
public sealed record ModelShape(
    float Width,
    float Height,
    float HeightToWidth,
    int Triangles,
    float TransparentRatio);

/// <summary>
/// 從模型的貼圖鏤空程度判斷是不是草木。分類鏈裡最後、也最弱的一環。
/// </summary>
/// <remarks>
/// <b>這裡只剩一條規則，而那是量出來的結果，不是偷懶。</b>
///
/// 一開始有四條：鏤空→草木、扁平→地面、高瘦→岩石、很寬→水體。
/// 前三條看起來很合理，因為各類別的**平均值**確實分得開
/// （<c>--catalog-geometry</c>）：
///
/// <code>
/// 類別      透明%   寬    高   高寬比  三角形
/// 草木        20   311  308   1.27    261
/// 地面         1   403   76   0.24     85
/// 岩石         1   398 1117   2.70     94
/// 水體         6   639  214   0.97    272
/// </code>
///
/// 但**平均值分得開不代表個體分得開**。拿有把握的分類當答案量精確度
/// （<c>--catalog-precision</c>）：
///
/// <code>
/// 規則    次數  猜對  精確度
/// 草木     543   387   71%   ← 唯一可用
/// 岩石     382   101   26%
/// 地面     478    55   12%
/// 水體     298     8    3%
/// </code>
///
/// 「扁平又簡單」其實多半是牆與建築構件（猜地面 147 次是牆、104 次是建築），
/// 「很寬」幾乎從來不是水。三條全部刪掉。
///
/// **留著它們會讓覆蓋率從 81% 變成 88%，但那多出來的 507 個裡約 360 個是錯的。**
/// 錯的分類比沒有分類更糟：它會讓人以為那一格已經處理過了。
///
/// 分類來源標成「形狀」，一眼看得出是推測。
/// </remarks>
public static class ModelShapeClassifier
{
    /// <summary>透明像素超過這個比例就當成有鏤空 —— 樹葉、草叢、格柵。</summary>
    private const float FoliageTransparency = 0.15f;

    public static bool TryClassify(ModelShape shape, out AssetCategory category)
    {
        // 鏤空是唯一量得出精確度的訊號（71%）。幾何那三條都在 26% 以下，已刪。
        if (shape.TransparentRatio >= FoliageTransparency)
        {
            category = AssetCategory.Vegetation;
            return true;
        }

        category = AssetCategory.Unclassified;
        return false;
    }

    /// <summary>量一個模型。讀不了時回 null。</summary>
    public static ModelShape? Measure(string bmdPath, string directory)
    {
        try
        {
            var model = new BMDReader().Load(bmdPath).GetAwaiter().GetResult();

            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;
            int triangles = 0;

            foreach (var mesh in model.Meshes)
            {
                triangles += mesh.Triangles.Length;

                foreach (var vertex in mesh.Vertices)
                {
                    minX = MathF.Min(minX, vertex.Position.X); maxX = MathF.Max(maxX, vertex.Position.X);
                    minY = MathF.Min(minY, vertex.Position.Y); maxY = MathF.Max(maxY, vertex.Position.Y);
                    minZ = MathF.Min(minZ, vertex.Position.Z); maxZ = MathF.Max(maxZ, vertex.Position.Z);
                }
            }

            if (minX > maxX)
                return null;

            float width = MathF.Max(maxX - minX, maxY - minY);
            float height = maxZ - minZ;

            return new ModelShape(
                width,
                height,
                width <= 0.01f ? 0f : height / width,
                triangles,
                MeasureTransparency(model, directory));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>這個模型的貼圖平均有多少透明像素。</summary>
    private static float MeasureTransparency(BMD model, string directory)
    {
        float total = 0;
        int measured = 0;

        foreach (var mesh in model.Meshes)
        {
            string? path = FindTexture(directory, mesh.TexturePath);

            if (path is null || TerrainTextureClassifier.Measure(path) is not { } profile)
                continue;

            total += profile.TransparentRatio;
            measured++;
        }

        return measured == 0 ? 0f : total / measured;
    }

    private static string? FindTexture(string directory, string texturePath)
    {
        if (string.IsNullOrWhiteSpace(texturePath))
            return null;

        string baseName = Path.GetFileNameWithoutExtension(texturePath);

        foreach (string extension in new[] { ".ozt", ".ozj", ".ozp", ".ozd" })
        {
            string candidate = Path.Combine(directory, baseName + extension);

            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }
}
