namespace Client.Data.MAP
{
    /// <summary>
    /// <c>.map</c> 的 Layer1/Layer2 存的是貼圖索引，索引到檔名的對應規則放在這裡。
    /// </summary>
    /// <remarks>
    /// 這份表與 <c>Client.Main.Controls.Terrain.TerrainData.GetDefaultTextureMappings()</c> 相同，
    /// 但放在 Client.Data 讓不依賴 MonoGame 的工具（地圖編輯器、MapTool）也能用。
    /// </remarks>
    public static class TerrainTextureMapping
    {
        /// <summary>ExtTile01 對應的索引；ExtTileNN 是 <c>ExtTileBaseIndex + NN</c>。</summary>
        public const int ExtTileBaseIndex = 13;

        /// <summary>
        /// Layer2 用 255 表示「這格沒有第二層」，不是貼圖索引。
        /// 實測：World1 的 Layer2 有 42562 格是 255，Layer1 則從不出現 255。
        /// 渲染端在 <c>TerrainRenderer.RenderTextureIndexed</c> 找不到貼圖時直接跳過，所以不會出錯。
        /// </summary>
        public const byte NoLayerIndex = 255;

        /// <summary>
        /// <c>TerrainLoader</c> 目前只掛載 ExtTile01–16（索引 14–29），
        /// 但 Season 20 資源裡實際存在 ExtTile01–35。超出這個數字的貼圖現在載不到。
        /// </summary>
        public const int LoadedExtTileCount = 16;

        public static IReadOnlyDictionary<int, string> Default { get; } = new Dictionary<int, string>
        {
            {   0, "TileGrass01.ozj" },
            {   1, "TileGrass02.ozj" },
            {   2, "TileGround01.ozj" },
            {   3, "TileGround02.ozj" },
            {   4, "TileGround03.ozj" },
            {   5, "TileWater01.ozj" },
            {   6, "TileWood01.ozj" },
            {   7, "TileRock01.ozj" },
            {   8, "TileRock02.ozj" },
            {   9, "TileRock03.ozj" },
            {  10, "TileRock04.ozj" },
            {  11, "TileRock05.ozj" },
            {  12, "TileRock06.ozj" },
            {  13, "TileRock07.ozj" },
            {  30, "TileGrass01.ozt" },
            {  31, "TileGrass02.ozt" },
            {  32, "TileGrass03.ozt" },
            { 100, "leaf01.ozt" },
            { 101, "leaf02.ozj" },
            { 102, "rain01.ozt" },
            { 103, "rain02.ozt" },
            { 104, "rain03.ozt" },
        };

        /// <summary>
        /// 產生一份「索引 → 檔名」的完整對應，含 ExtTile 區段。
        /// </summary>
        /// <param name="extTileCount">要掛載的 ExtTile 數量，預設維持客戶端現況的 16。</param>
        public static Dictionary<int, string> BuildIndexMap(int extTileCount = LoadedExtTileCount)
        {
            var map = new Dictionary<int, string>(Default);

            for (int i = 1; i <= extTileCount; i++)
                map[ExtTileBaseIndex + i] = $"ExtTile{i:00}.ozj";

            return map;
        }
    }
}
