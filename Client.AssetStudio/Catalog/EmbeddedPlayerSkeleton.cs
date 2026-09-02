namespace Client.AssetStudio.Catalog;

/// <summary>
/// 玩家共用骨架（<c>Player/Player.bmd</c>）的<b>唯一正確來源</b>：
/// Client.Main 內嵌的 S6 版 player.bmd。
/// </summary>
/// <remarks>
/// <b>為什麼不能用資料目錄裡的那份。</b>下載包（MU_Red_1_20_61）的
/// <c>Player/Player.bmd</c> 是 Season 20 動作表——380 個動作、走路在 47–59、
/// Die1 在 314；而 <c>Client.Main.Models.PlayerAction</c> 的現行列舉是
/// <b>S6 動作表</b>（走路 15、AttackFist 38、Die1 231、共 284 個，
/// <c>MaxPlayerAction=284</c>）。遊戲執行期靠 <c>BMDLoader.Prepare</c>
/// 對 <c>Player/Player.bmd</c> 的特判（BMDLoader.cs:435）一律載入
/// <b>內嵌 S6 檔</b>，兩張表因此咬合。任何離線工具若直接讀資料目錄的
/// S20 檔再套 S6 列舉命名，就是 2026-09-02 動畫全錯位事故的根因
/// （walk 變成腳不交替的未知動作、die 末幀站立）。
///
/// 這裡把內嵌資源實體化成快取檔，讓既有「用路徑讀 BMD」的程式
/// （匯出、manifest 雜湊）一行換源即可，語意與執行期完全一致。
/// </remarks>
public static class EmbeddedPlayerSkeleton
{
    private const string ResourceName = "Client.Main.Data.S6.player.bmd";

    /// <summary>快取檔路徑；首次呼叫時從內嵌資源解出（每次覆寫，確保與組件一致）。</summary>
    public static string MaterializePath()
    {
        string directory = Path.Combine(Path.GetTempPath(), "mu-embedded-s6", "Player");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "player.bmd");

        using var stream = typeof(Client.Main.Constants).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new FileNotFoundException($"內嵌 S6 骨架資源不存在：{ResourceName}", ResourceName);
        using var file = File.Create(path);
        stream.CopyTo(file);

        return path;
    }

    /// <summary>路徑是不是玩家共用骨架（比對規則照抄 BMDLoader.cs:435 的特判）。</summary>
    public static bool IsPlayerSkeletonPath(string path)
    {
        string normalized = path.Replace('\\', '/');
        return normalized.EndsWith("Player/Player.bmd", StringComparison.OrdinalIgnoreCase);
    }
}
