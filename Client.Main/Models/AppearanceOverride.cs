using System.Collections.Generic;

namespace Client.Main.Models
{
    /// <summary>
    /// 外觀汰換：舊職業改用新職業的身體。
    /// </summary>
    /// <remarks>
    /// <b>只換外觀，其餘一律不動。</b>伺服器送的還是舊職業，數值、技能、
    /// 協議、資料庫全部不變 —— 這裡只影響客戶端載哪五個部位檔。
    /// 要換回原樣就把 <see cref="Map"/> 清空。
    ///
    /// <b>為什麼不在伺服器改。</b>外觀封包的第 0 個位元組只用 4 個位元表示職業，
    /// 16 個值全被舊 7 職與它們的轉職佔滿，<b>沒有空位可以表示編號 8–15 的新職業</b>
    /// （見 <c>CharacterClassDatabase.TryParseClassFromAppearance</c>）。
    /// 所以這件事只能在客戶端做。
    ///
    /// <b>動作不受影響。</b>380 個動作全部在共用的 <c>Player/Player.bmd</c> 裡，
    /// 而用哪一個動作是由<b>武器類型</b>決定的（<c>PlayerActionMapper</c>），
    /// 跟身體是哪一個職業無關 —— 所以換身體不會讓動作跑掉。
    ///
    /// 幾何量的差別（五個部位的三角形總和，實測）：
    /// <code>
    /// 戰士 Dark Knight   1,407  →  幻影騎士 Illusion Knight  2,410
    /// 法師 Dark Wizard   1,383  →  魔導士   Mage             2,130
    /// </code>
    /// 弓箭手刻意不換：新職業裡沒有弓手造型，而 Fairy Elf 本來就有 1,752，
    /// 在舊職業裡排第三高。
    /// </remarks>
    public static class AppearanceOverride
    {
        /// <summary>舊職業 → 拿來當外觀的職業。空的代表不換。</summary>
        public static readonly Dictionary<PlayerClass, PlayerClass> Map = new()
        {
            [PlayerClass.DarkKnight] = PlayerClass.IllusionKnight,
            [PlayerClass.DarkWizard] = PlayerClass.Mage,
        };

        /// <summary>把職業換成它的外觀職業；沒有對映就原樣回傳。</summary>
        /// <remarks>
        /// <b>轉職階段自動跟著換。</b><c>PlayerClass</c> 的編號是「階段偏移 + 基礎職業」
        /// （二轉 +200、三轉 +300、四轉 +400），所以把基礎職業換掉之後
        /// 把階段偏移加回去就行 —— 不必為 8 個轉職階段各寫一行。
        ///
        /// 換過去的那一階不存在時（例如魔劍士沒有二轉）就維持原樣，不要猜。
        /// </remarks>
        public static PlayerClass Apply(PlayerClass playerClass)
        {
            int value = (int)playerClass;
            int tier = value / 100 * 100;
            var baseClass = (PlayerClass)(value - tier);

            if (!Map.TryGetValue(baseClass, out var replacement))
                return playerClass;

            var target = (PlayerClass)((int)replacement + tier);
            return System.Enum.IsDefined(typeof(PlayerClass), target) ? target : playerClass;
        }
    }
}
