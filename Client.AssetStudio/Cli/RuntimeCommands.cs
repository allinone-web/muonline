using Client.AssetStudio.Project;
using Client.Main.Content;
using Client.Main.Models;
using Client.AssetStudio.Server;
using Client.Data.Texture;

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
    /// <summary>客戶端的全域動畫倍率（<c>ModelObject.AnimationSpeed</c> 的預設值）。</summary>
    private const float AnimationSpeed = 4f;

    /// <summary>
    /// 讓動畫長度跟伺服器的節奏對齊。
    /// </summary>
    /// <remarks>
    /// <b>對齊的方向是改客戶端的播放速度，不是改伺服器的 AttackDelay。</b>
    /// 伺服器的數值是遊戲平衡 —— 攻擊間隔決定這隻怪有多難打；
    /// 播放速度只影響觀感。為了讓一隻新模型看起來順而去改平衡，是本末倒置。
    /// （而且改了要重啟伺服器，會打斷別人。）
    ///
    /// 對不齊的兩種樣子：
    /// <list type="bullet">
    /// <item>動畫太慢 → 傷害數字跳出來了，刀還沒揮到</item>
    /// <item>動畫太快 → 揮完刀站著發呆等下一次攻擊</item>
    /// </list>
    /// </remarks>
    public static int Tune(AssetLibrary library, Dictionary<short, MonsterRow> servers,
                           string? filter, bool apply)
    {
        Console.WriteLine();
        Console.WriteLine($"資源庫：{library.Root}");
        Console.WriteLine($"客戶端 AnimationSpeed = {AnimationSpeed}（ModelObject 的預設值）");
        Console.WriteLine("公式：PlaySpeed = (影格數 - 1) / (目標秒數 × AnimationSpeed)");
        Console.WriteLine();

        var assets = library.Assets
            .Where(a => a.BindNumber >= 0)
            .Where(a => filter is null || a.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (assets.Length == 0)
        {
            Console.WriteLine("（沒有綁定編號的資產。先填 BindNumber。）");
            return 0;
        }

        int changed = 0;

        foreach (var asset in assets)
        {
            Console.WriteLine($"── {asset.Name}（綁定 #{asset.BindNumber}）──");

            if (!servers.TryGetValue((short)asset.BindNumber, out var server))
            {
                Console.WriteLine($"  伺服器沒有編號 {asset.BindNumber} 的定義，跳過。");
                Console.WriteLine();
                continue;
            }

            Client.Data.BMD.BMD model;
            try
            {
                model = LibraryAssetProvider.LoadAsync(asset).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  ✗ 載入失敗：{ex.Message}");
                Console.WriteLine();
                continue;
            }

            Console.WriteLine($"  伺服器：攻擊間隔 {server.AttackDelay.TotalSeconds:F2}s、"
                            + $"移動間隔 {server.MoveDelay.TotalSeconds:F2}s");
            Console.WriteLine();
            Console.WriteLine("   動作           影格   現在秒數   目標秒數   PlaySpeed");

            var speeds = new Dictionary<string, float>(asset.ActionSpeeds);

            foreach (var (slot, target, why) in Targets(server))
            {
                if (slot >= model.Actions.Length || model.Actions[slot] is not { } action)
                    continue;

                int frames = action.NumAnimationKeys;
                if (frames <= 1) continue;

                float current = action.PlaySpeed <= 0f ? 1f : action.PlaySpeed;
                double now = (frames - 1) / (current * AnimationSpeed);
                float wanted = (float)((frames - 1) / (target * AnimationSpeed));

                string label = Enum.IsDefined(typeof(MonsterActionType), (byte)slot)
                    ? ((MonsterActionType)slot).ToString()
                    : $"#{slot}";

                Console.WriteLine($"   {slot,2} {label,-11}{frames,4}   {now,7:F2}s   {target,7:F2}s   "
                                + $"{wanted,7:F3}   {why}");

                speeds[slot.ToString()] = wanted;
            }

            if (apply)
            {
                asset.ActionSpeeds = speeds;
                changed++;
            }

            Console.WriteLine();
        }

        if (apply && changed > 0)
        {
            library.Update();
            Console.WriteLine($"已寫入 {changed} 個資產的播放速度（library.json）。");
            Console.WriteLine("客戶端下次生出這些怪時就會套用，不必重啟伺服器。");
        }
        else if (!apply)
        {
            Console.WriteLine("這只是預覽。要寫進 library.json 請加 --apply。");
        }

        return 0;
    }

    /// <summary>
    /// 逐張貼圖用<b>客戶端自己的讀取器</b>真的解碼一次。
    /// </summary>
    /// <remarks>
    /// 光看「檔案在不在」不夠。踩過的坑：資源庫的貼圖是純 PNG，而客戶端把
    /// <c>.png</c> 對應到 <c>OZPReader</c> —— OZP 是「4 位元組前綴 ＋ 完整 PNG」，
    /// 那個讀取器會無條件砍掉前 4 個位元組，於是純 PNG 剩下
    /// <c>0D 0A 1A 0A IHDR…</c>，不是合法 PNG，直接拋例外。
    ///
    /// 結果就是：模型有網格、有骨骼、名牌也出得來，<b>就是畫不出來</b>。
    /// 檔案存在、路徑正確、格式合法 —— 每一項單看都沒問題。
    /// 所以這裡不驗「存不存在」，直接把讀取器叫起來解一次。
    /// </remarks>
    private static int CheckTextures(Client.Data.BMD.BMD model)
    {
        var readers = new Dictionary<string, Client.Data.BaseReader<TextureData>>(StringComparer.OrdinalIgnoreCase)
        {
            [".ozt"] = new OZTReader(), [".tga"] = new OZTReader(),
            [".ozj"] = new OZJReader(), [".jpg"] = new OZJReader(),
            [".ozp"] = new OZPReader(), [".png"] = new OZPReader(),
            [".ozd"] = new OZDReader(), [".dds"] = new OZDReader(),
        };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int bad = 0;

        foreach (var mesh in model.Meshes)
        {
            string name = mesh.TexturePath ?? string.Empty;
            if (name.Length == 0 || !seen.Add(name)) continue;

            string full = Client.Main.Content.BMDLoader.Instance.GetTexturePath(model, name);

            if (full is null || !File.Exists(full))
            {
                Console.Error.WriteLine($"      ✗ 貼圖找不到：{name}");
                bad++;
                continue;
            }

            string ext = Path.GetExtension(full);
            if (!readers.TryGetValue(ext, out var reader))
            {
                Console.Error.WriteLine($"      ✗ 沒有 {ext} 的讀取器：{name}");
                bad++;
                continue;
            }

            try
            {
                var data = reader.Load(full).GetAwaiter().GetResult();
                if (data is null || data.Width == 0 || data.Height == 0)
                {
                    Console.Error.WriteLine($"      ✗ 貼圖解出來是空的：{name}");
                    bad++;
                }
                else
                {
                    Console.WriteLine($"      貼圖 {name}  {data.Width}×{data.Height}  ✓");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"      ✗ 貼圖解碼失敗 {name}：{ex.GetType().Name} {ex.Message}");
                bad++;
            }
        }

        return bad;
    }

    /// <summary>每個動作槽該對齊到幾秒，以及理由。</summary>
    private static IEnumerable<(int Slot, double Target, string Why)> Targets(MonsterRow server)
    {
        double attack = server.AttackDelay.TotalSeconds;
        double move = server.MoveDelay.TotalSeconds;

        // 攻擊：一次揮擊要在伺服器的下一次攻擊之前結束，留 15% 餘裕，
        // 否則兩次攻擊會疊在一起，看起來像抽搐。
        if (attack > 0)
        {
            yield return (3, attack * 0.85, "＝攻擊間隔 ×0.85");
            yield return (4, attack * 0.85, "＝攻擊間隔 ×0.85");
        }

        // 走：MoveDelay 是「走一格要多久」，走路循環正好對一格才不會滑步。
        if (move > 0)
        {
            yield return (2, move, "＝移動間隔（一格）");
            yield return (10, move * 0.6, "跑步 ＝ 移動間隔 ×0.6");
        }

        // 受擊要短，不然會蓋掉下一次攻擊；死亡可以慢，那是最後一次播放。
        if (attack > 0)
        {
            yield return (5, Math.Min(0.4, attack * 0.4), "受擊要短");
            yield return (6, 1.6, "死亡固定 1.6s");
        }
    }


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

                failed += CheckTextures(model);
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
