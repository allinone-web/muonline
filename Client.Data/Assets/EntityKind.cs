namespace Client.AssetStudio.Catalog;

// 這個列舉住在 Client.Data，命名空間卻是 Client.AssetStudio.Catalog。
//
// 原因：Client.AssetStudio 參考 Client.Main，所以 Client.Main 不可能反向參考它
// （會是循環相依）。但執行期要能載入資源庫的自有資產，就必須讓 Client.Main
// 拿得到 AssetLibrary 與 GltfImporter —— 於是這三個檔案搬進兩邊都參考的 Client.Data。
//
// 命名空間刻意不改：.NET 的命名空間本來就不必等於組件名，
// 而改名要動 23 個檔案，換來的只是「看起來比較整齊」，不值得那個風險。
// 真正重要的是「誰依賴誰」，那件事已經對了。

public enum EntityKind
{
    Monster,
    Npc,
    Player,
    Pet,
    SkillModel,
    Item,
    Effect,

    /// <summary>
    /// 從外部匯入、存在資源庫裡的資產（glTF ＋ PNG）。
    /// </summary>
    /// <remarks>
    /// 與其他分類的差別：這一類的 <see cref="EntityEntry.FullPath"/> 指向的是
    /// <b>.glb</b> 而不是 .bmd，載入時要走 <c>GltfImporter</c>。
    /// 之所以要讓它出現在主目錄而不是只留在資源庫面板裡，是因為
    /// 「這隻怪長什麼樣」與「它從哪來」是兩件事 ——
    /// 要比較天堂的死亡騎士跟 MU 的 Balrog，得能在同一個縮圖牆上並排看。
    /// </remarks>
    Library,
}
