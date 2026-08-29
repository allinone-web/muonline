using Client.Data.ATT;
using Client.Data.MAP;
using Client.Data.OBJS;

namespace MuAssets.Core;

/// <summary>一種物件在某張圖上的擺放統計。</summary>
public readonly record struct PlacementProfile(
    int Count,
    float BlockedRatio,
    float SafeZoneRatio,
    float WaterRatio,
    float AverageScale);

/// <summary>
/// 從「物件實際擺在哪些格子上」反推它是什麼。
/// </summary>
/// <remarks>
/// 檔名與貼圖名對半數以上的模型毫無語意（<c>ObjectNN.bmd</c> + <c>br001.ozj</c>），
/// 但**擺放位置本身就是資訊**：擋路的格子上放的是建築與牆，水面上放的是水景，
/// 安全區裡放的是城鎮擺設，散佈在可走地面上、數量幾百的是草木。
///
/// 這條線索只在檔名與貼圖名都失敗時才用，而且分類來源會標成「擺放位置」，
/// 讓人一眼看出這是推測而不是確定。
/// </remarks>
public static class PlacementStats
{
    /// <summary>掃一張圖的 <c>.obj</c> + <c>.att</c>，算出每種 type 的擺放輪廓。</summary>
    public static Dictionary<short, PlacementProfile> Build(string dataPath, int worldIndex)
    {
        var result = new Dictionary<short, PlacementProfile>();

        string directory = Path.Combine(dataPath, $"World{worldIndex}");
        string objPath = Path.Combine(directory, $"EncTerrain{worldIndex}.obj");
        string attPath = Path.Combine(directory, $"EncTerrain{worldIndex}.att");

        if (!File.Exists(objPath) || !File.Exists(attPath))
            return result;

        OBJ obj;
        TerrainAttribute attributes;

        try
        {
            obj = new OBJReader().Load(objPath).GetAwaiter().GetResult();
            attributes = new ATTReader().Load(attPath).GetAwaiter().GetResult();
        }
        catch
        {
            return result;
        }

        var accumulator = new Dictionary<short, (int Count, int Blocked, int Safe, int Water, float Scale)>();

        foreach (var mapObject in obj.Objects)
        {
            int tileX = (int)(mapObject.Position.X / MuConstants.TerrainScale);
            int tileY = (int)(mapObject.Position.Y / MuConstants.TerrainScale);

            if ((uint)tileX >= MuConstants.TerrainSize || (uint)tileY >= MuConstants.TerrainSize)
                continue;

            var flags = attributes.TerrainWall[(tileY * MuConstants.TerrainSize) + tileX];

            var current = accumulator.GetValueOrDefault(mapObject.Type);
            accumulator[mapObject.Type] = (
                current.Count + 1,
                current.Blocked + (flags.HasFlag(TWFlags.NoMove) || flags.HasFlag(TWFlags.NoGround) ? 1 : 0),
                current.Safe + (flags.HasFlag(TWFlags.SafeZone) ? 1 : 0),
                current.Water + (flags.HasFlag(TWFlags.Water) ? 1 : 0),
                current.Scale + mapObject.Scale);
        }

        foreach (var (type, value) in accumulator)
        {
            result[type] = new PlacementProfile(
                value.Count,
                value.Blocked / (float)value.Count,
                value.Safe / (float)value.Count,
                value.Water / (float)value.Count,
                value.Scale / value.Count);
        }

        return result;
    }

    /// <summary>
    /// 依擺放輪廓推測分類。判斷不出來時回傳 false —— 寧可留「未分類」也不要標錯。
    /// </summary>
    public static bool TryClassify(PlacementProfile profile, out AssetCategory category)
    {
        category = AssetCategory.Unclassified;

        // 樣本太少，統計沒有意義。
        if (profile.Count < 3)
            return false;

        if (profile.WaterRatio >= 0.6f)
        {
            category = AssetCategory.Water;
            return true;
        }

        if (profile.BlockedRatio >= 0.7f)
        {
            // 擋路且數量少 = 大件建築；擋路但成排出現 = 牆與圍籬。
            category = profile.Count <= 20 ? AssetCategory.Building : AssetCategory.Wall;
            return true;
        }

        // 大量散佈在可走地面上的小東西，幾乎都是草木。
        if (profile.Count >= 100 && profile.BlockedRatio <= 0.15f)
        {
            category = AssetCategory.Vegetation;
            return true;
        }

        if (profile.SafeZoneRatio >= 0.6f)
        {
            category = AssetCategory.Decoration;
            return true;
        }

        return false;
    }
}
