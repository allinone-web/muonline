using Client.AssetStudio.Project;
using Client.Main.Content;
using Client.Main.Models;

namespace Client.AssetStudio.Cli;

/// <summary>
/// 驗證「資源庫的資產真的能被<b>遊戲客戶端</b>載入」這條路。
/// </summary>
/// <remarks>
/// 這個檢查存在的理由，是因為工具能載入 ≠ 客戶端能載入。
/// 在打通執行期之前，<c>MuAssetStudio</c> 一直都能好好地顯示資源庫的資產，
/// 但 <c>Client.Main</c> 從來沒讀過 <c>BindNumber</c> —— 兩邊看起來都「正常」，
/// 只是中間根本沒有連線。所以驗的必須是 <c>Client.Main</c> 那一側的程式碼路徑
/// （<see cref="LibraryAssetProvider"/>），不是工具自己的。
/// </remarks>
public static class RuntimeCommands
{
    public static int Check(AssetLibrary library, string? filter)
    {
        Console.WriteLine();
        Console.WriteLine($"資源庫：{library.Root}");
        Console.WriteLine("驗證的是 Client.Main 的載入路徑（LibraryAssetProvider），不是工具自己的。");
        Console.WriteLine();

        var assets = library.Assets
            .Where(a => filter is null || a.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                                       || a.Id.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (assets.Length == 0)
        {
            Console.WriteLine("（沒有符合的資產）");
            return 0;
        }

        int failed = 0;

        foreach (var asset in assets)
        {
            Console.WriteLine($"── {asset.Name} ──");

            if (asset.BindNumber < 0)
            {
                Console.WriteLine("  未綁定編號 —— 客戶端不會用到它。用 GUI 或 library.json 填 BindNumber。");
                Console.WriteLine();
                continue;
            }

            // 這一步走的就是 ScopeHandler 生怪時走的那條查詢。
            if (!LibraryAssetProvider.TryGet((ushort)asset.BindNumber, out var resolved)
                || resolved.Id != asset.Id)
            {
                Console.Error.WriteLine($"  ✗ 編號 {asset.BindNumber} 查不回自己");
                failed++;
                Console.WriteLine();
                continue;
            }

            try
            {
                var model = LibraryAssetProvider.LoadAsync(asset).GetAwaiter().GetResult();

                Console.WriteLine($"  ✓ 編號 {asset.BindNumber} → {model.Meshes.Length} 網格、"
                                + $"{model.Bones.Length} 骨骼、{model.Actions.Length} 動作槽");

                // 動作槽的內容才是重點：重排錯了不會報錯，只會播錯動作。
                foreach (var (key, clip) in asset.Actions.Where(kv => kv.Value.Length > 0)
                                                         .OrderBy(kv => int.Parse(kv.Key)))
                {
                    int slot = int.Parse(key);
                    string label = Enum.IsDefined(typeof(MonsterActionType), (byte)slot)
                        ? ((MonsterActionType)slot).ToString()
                        : $"#{slot}";

                    bool inRange = slot < model.Actions.Length;
                    int keys = inRange ? model.Actions[slot].NumAnimationKeys : 0;

                    Console.WriteLine(inRange && keys > 1
                        ? $"      {slot,3} {label,-9} {keys,3} 影格  ← {clip}"
                        : $"      {slot,3} {label,-9} ✗ 空的（{keys} 影格）← {clip}");

                    if (!inRange || keys <= 1) failed++;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  ✗ 載入失敗：{ex.GetType().Name} {ex.Message}");
                failed++;
            }

            Console.WriteLine();
        }

        Console.WriteLine(failed == 0
            ? "全部通過：客戶端生到這些編號的怪時，會用資源庫的模型與動作。"
            : $"{failed} 項有問題。");

        return failed == 0 ? 0 : 1;
    }
}
