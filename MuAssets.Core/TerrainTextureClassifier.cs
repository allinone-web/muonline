using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MuAssets.Core;

/// <summary>貼圖在地圖裡扮演的角色，來自檔名。</summary>
public enum TextureSlot
{
    Unknown,

    /// <summary>
    /// 會搖動的草 —— 地形上長出來的billboard，不是地面貼圖。
    /// </summary>
    /// <remarks>
    /// 這是客戶端<b>寫死</b>的一組檔案，不是猜的：
    /// <c>GrassRenderer</c> 只載 <c>TileGrass01/02/03.ozt</c>
    /// （特殊混合圖是 <c>TileGrass01_R.jpg</c>），
    /// 而畫質設定的「Draw Grass」關掉的就是它們。
    ///
    /// 把它從草地地面（<see cref="Grass"/>，那是 .ozj 的地面貼圖）分出來，
    /// 因為兩者的替換價值差很多：草是站在地上、離鏡頭最近、
    /// 而且每張圖都有 —— 貼圖粗糙的話第一眼就看得到。
    ///
    /// 實測：145 個檔案、**內容唯一只有 40 個**，涵蓋 58 張會長草的地圖。
    /// 換掉那 40 張，全部地圖的草一起換。
    /// </remarks>
    GrassBillboard,

    Grass,
    Ground,
    Rock,
    Water,
    Wood,
    Ext,
    Overlay,
    Effect,
}

/// <summary>貼圖看起來像什麼，來自影像本身。</summary>
public enum TextureLook
{
    Unknown,
    Snow,
    Green,
    Soil,
    Stone,
    Water,
    Dark,
    Vivid,
}

/// <summary>一張貼圖的量測結果。</summary>
public sealed record TextureProfile(
    int Width,
    int Height,
    byte R, byte G, byte B,
    float Hue, float Saturation, float Value,
    float TransparentRatio);

/// <summary>
/// 地形貼圖的分類。
/// </summary>
/// <remarks>
/// <b>分成兩軸，因為檔名與影像會不一致，而且兩邊都是真的。</b>
///
/// 檔名說的是**槽位**（這張圖在地圖裡扮演什麼角色），影像說的是**外觀**。
/// 實測迪維亞斯（World3）的 <c>TileGrass01</c> 是雪白色 ——
/// 色相 191、飽和 0.07、明度 0.91。那是「草地槽位裝了雪」，不是分類錯誤。
///
/// 要換成自製美術時兩軸都需要：知道它是草地槽位（結構上該長什麼）、
/// 也知道它現在是雪（這張圖該畫成什麼）。只有一軸的話會畫錯。
///
/// 對 ExtTile 這種毫無語意的檔名（647 個唯一貼圖裡佔絕大多數），
/// 外觀是唯一的線索。
///
/// <b>透明度要納入平均。</b><c>.ozt</c> 有大片全透明像素，
/// 不加權的話那些像素的 RGB 會把平均拉走 ——
/// 實測 World1 的 <c>TileGrass01.OZT</c> 不加權會算成青色（色相 187），
/// 看起來像水。
/// </remarks>
public static class TerrainTextureClassifier
{
    /// <summary>量測一張貼圖。讀不了時回 null。</summary>
    public static TextureProfile? Measure(string path)
    {
        using var image = TextureExporter.DecodeFile(path);
        if (image is null)
            return null;

        double r = 0, g = 0, b = 0, weight = 0;
        int transparent = 0;
        int total = image.Width * image.Height;

        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);

                for (int x = 0; x < row.Length; x++)
                {
                    var pixel = row[x];
                    float alpha = pixel.A / 255f;

                    if (alpha < 0.05f)
                    {
                        transparent++;
                        continue;
                    }

                    r += pixel.R * alpha;
                    g += pixel.G * alpha;
                    b += pixel.B * alpha;
                    weight += alpha;
                }
            }
        });

        if (weight <= 0)
            return new TextureProfile(image.Width, image.Height, 0, 0, 0, 0, 0, 0, 1f);

        byte mr = (byte)Math.Clamp(r / weight, 0, 255);
        byte mg = (byte)Math.Clamp(g / weight, 0, 255);
        byte mb = (byte)Math.Clamp(b / weight, 0, 255);

        var (hue, saturation, value) = ToHsv(mr, mg, mb);

        return new TextureProfile(
            image.Width, image.Height, mr, mg, mb, hue, saturation, value, (float)transparent / total);
    }

    /// <summary>從檔名判斷槽位。</summary>
    public static TextureSlot SlotOf(string fileName)
    {
        string name = Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();

        if (name.StartsWith("exttile", StringComparison.Ordinal)) return TextureSlot.Ext;
        // 搖動的草只有這幾個檔名，而且必須是 .ozt（或特殊混合圖的 _R.jpg）——
        // 依據是 GrassRenderer 載入哪些檔案，不是猜的。
        if (IsGrassBillboard(fileName)) return TextureSlot.GrassBillboard;

        if (name.StartsWith("tilegrass", StringComparison.Ordinal)) return TextureSlot.Grass;
        if (name.StartsWith("tileground", StringComparison.Ordinal)) return TextureSlot.Ground;
        if (name.StartsWith("tilerock", StringComparison.Ordinal)) return TextureSlot.Rock;
        if (name.StartsWith("tilewater", StringComparison.Ordinal)) return TextureSlot.Water;
        if (name.StartsWith("tilewood", StringComparison.Ordinal)) return TextureSlot.Wood;
        if (name.StartsWith("alphatile", StringComparison.Ordinal)) return TextureSlot.Overlay;

        // 這些是疊在地形上的效果層，不是地面材質。
        if (name.StartsWith("leaf", StringComparison.Ordinal)
            || name.StartsWith("rain", StringComparison.Ordinal)
            || name.StartsWith("fog", StringComparison.Ordinal)
            || name.StartsWith("snow", StringComparison.Ordinal))
        {
            return TextureSlot.Effect;
        }

        return TextureSlot.Unknown;
    }

    /// <summary>
    /// 這個檔案是不是「會搖動的草」的貼圖。
    /// </summary>
    /// <remarks>
    /// 對應 <c>GrassRenderer</c> 的載入邏輯：三張 <c>TileGrass0N.ozt</c>，
    /// 特殊混合圖的第一張換成 <c>TileGrass01_R.jpg</c>。
    /// </remarks>
    public static bool IsGrassBillboard(string fileName)
    {
        string name = Path.GetFileNameWithoutExtension(fileName);
        string extension = Path.GetExtension(fileName);

        if (name.Equals("TileGrass01_R", StringComparison.OrdinalIgnoreCase)
            && extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return extension.Equals(".ozt", StringComparison.OrdinalIgnoreCase)
            && (name.Equals("TileGrass01", StringComparison.OrdinalIgnoreCase)
             || name.Equals("TileGrass02", StringComparison.OrdinalIgnoreCase)
             || name.Equals("TileGrass03", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 這張圖會不會長草。客戶端寫死了一份不長草的清單。
    /// </summary>
    /// <remarks>對應 <c>GrassRenderer.IsGrassDisabledWorld</c>。</remarks>
    public static bool WorldHasGrass(int worldIndex)
        => worldIndex != 7 && worldIndex != 67 && worldIndex != 52
        && (worldIndex < 11 || worldIndex > 17);

    /// <summary>
    /// 從影像判斷外觀。
    /// </summary>
    /// <remarks>
    /// 門檻是拿實際的貼圖量出來調的，順序有意義 —— 先判斷極端（很亮很淡＝雪、很暗），
    /// 再看色相。反過來的話雪會先被色相判成水（雪的平均色偏藍）。
    /// </remarks>
    public static TextureLook LookOf(TextureProfile profile)
    {
        if (profile.Value >= 0.75f && profile.Saturation <= 0.20f)
            return TextureLook.Snow;

        if (profile.Value <= 0.15f)
            return TextureLook.Dark;

        if (profile.Saturation <= 0.12f)
            return TextureLook.Stone;

        if (profile.Saturation >= 0.60f && (profile.Hue < 20f || profile.Hue >= 330f))
            return TextureLook.Vivid;

        return profile.Hue switch
        {
            >= 60f and < 170f => TextureLook.Green,
            >= 170f and < 260f => TextureLook.Water,
            >= 20f and < 60f => TextureLook.Soil,
            _ => TextureLook.Stone,
        };
    }

    private static (float Hue, float Saturation, float Value) ToHsv(byte r, byte g, byte b)
    {
        float rf = r / 255f, gf = g / 255f, bf = b / 255f;
        float max = MathF.Max(rf, MathF.Max(gf, bf));
        float min = MathF.Min(rf, MathF.Min(gf, bf));
        float delta = max - min;

        float hue = 0f;

        if (delta > 0.0001f)
        {
            if (max == rf) hue = 60f * (((gf - bf) / delta) % 6f);
            else if (max == gf) hue = 60f * (((bf - rf) / delta) + 2f);
            else hue = 60f * (((rf - gf) / delta) + 4f);
        }

        if (hue < 0f)
            hue += 360f;

        return (hue, max <= 0f ? 0f : delta / max, max);
    }
}
