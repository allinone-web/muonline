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
        /// <b>遊戲客戶端</b>實際掛載的 ExtTile 數量。原版 MuMain 也是這個數字
        /// （<c>MapManager.cpp</c> 的迴圈是 <c>i = 1..16</c>），所以這不是 muonline 的疏漏。
        /// </summary>
        public const int LoadedExtTileCount = 16;

        /// <summary>
        /// 資源包裡**實際存在**的 ExtTile 數量。Season 20 的圖用到 ExtTile01–35。
        /// </summary>
        /// <remarks>
        /// 這個數字比 <see cref="LoadedExtTileCount"/> 大，兩者意義不同：
        /// 前者是「檔案有多少」，後者是「S6 世代的載入器吃得下多少」。
        /// 工具鏈（編輯器、匯出器、校驗器）要用前者 —— 我們的目的是把資料完整取出來，
        /// 不是模擬舊載入器的限制。
        /// </remarks>
        public const int AvailableExtTileCount = 35;

        private static Dictionary<int, string> Base { get; } = new()
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
        /// 索引 → 檔名的完整對應。
        /// </summary>
        /// <remarks>
        /// <b>ExtTile 的條目也在這裡面</b>（索引 14 起）。它們本來只有
        /// <see cref="BuildIndexMap"/> 才會加上去，而直接讀這張表的使用端
        /// （例如 Godot 匯出器）因此拿不到檔名 —— 實測 81 張圖有 217 個
        /// 索引 14–29 的使用點會整批掉掉。這張表是「索引叫什麼檔案」的唯一真相，
        /// 少一半就不叫真相。
        ///
        /// 索引 30–32 是草地的 .ozt 疊層，不是 ExtTile17–19 ——
        /// 那一段的編號規則在這裡斷開，是資料本身就這樣，不是筆誤。
        /// </remarks>
        public static IReadOnlyDictionary<int, string> Default { get; } = BuildDefault();

        private static Dictionary<int, string> BuildDefault()
        {
            var map = new Dictionary<int, string>(Base);

            for (int i = 1; i <= AvailableExtTileCount; i++)
            {
                int index = ExtTileBaseIndex + i;

                // 30–32 已經被草地疊層佔用，不覆蓋。
                if (!map.ContainsKey(index))
                    map[index] = $"ExtTile{i:00}.ozj";
            }

            return map;
        }


        /// <summary>
        /// 產生一份「索引 → 檔名」的完整對應，含 ExtTile 區段。
        /// </summary>
        /// <param name="extTileCount">要掛載的 ExtTile 數量，預設維持客戶端現況的 16。</param>
        /// <summary>
        /// 產生索引對應。預設就是 <see cref="Default"/>；
        /// 傳一個較小的 <paramref name="extTileCount"/> 可以模擬舊載入器的限制。
        /// </summary>
        public static Dictionary<int, string> BuildIndexMap(int extTileCount = AvailableExtTileCount)
        {
            var map = new Dictionary<int, string>(Base);

            for (int i = 1; i <= extTileCount; i++)
            {
                int index = ExtTileBaseIndex + i;

                if (!map.ContainsKey(index))
                    map[index] = $"ExtTile{i:00}.ozj";
            }

            return map;
        }
    }
}
