#nullable enable
using Microsoft.Xna.Framework;
using Client.Main.Controls.UI;

namespace Client.Main.Controls.UI.Game.Common
{
    /// <summary>
    /// 遊戲內面板（背包、角色、倉庫、商店、交易、合成）共用的顏色。
    ///
    /// 桌面沿用原本的樣式：金色點綴、深淺不一的底色、明顯的內外框。
    ///
    /// <b>手機是另一套值。</b>登入、選伺服器、登入表單那幾個畫面是自己畫的
    /// 半透明面板，看起來乾淨；遊戲內的面板卻是金線、漸層、四角托架、
    /// 內外兩層框 —— 同一個 app 裡兩種語言，而且後者在小螢幕上顯得很碎。
    ///
    /// 所以手機把這裡的每一個值都換成 <see cref="MobileUi"/> 的色票：
    ///
    ///   * 底色三階全部收斂到同一個深藍灰 —— 面板不再有漸層可言
    ///     （<see cref="UiDrawHelper.DrawVerticalGradient"/> 遇到頭尾同色會直接畫一次，
    ///     順便省掉每個面板 64 次繪製）
    ///   * Accent 從金色變成中性灰 —— 裝飾線還在，但不再是視覺焦點
    ///   * TextGold 變成白色 —— 顏色留給真正帶資訊的東西
    ///
    /// 稀有度光暈、Danger／Success／Warning 不動：那些是資訊，不是裝飾。
    ///
    /// <para>
    /// 手機這一組全部寫成 <c>顏色 * 透明度</c>。MonoGame 的 AlphaBlend 是預乘的，
    /// 而 <c>Color * float</c> 會把 RGBA 一起乘 —— 這正是預乘。
    /// 桌面那組原本就是 <c>new Color(r, g, b, a)</c>（未預乘），alpha 都在 240 以上
    /// 所以看不出差別；手機的面板是 0.88，直接沿用寫法就會偏亮。
    /// </para>
    /// </summary>
    internal static class ModernHudTheme
    {
        private static readonly bool Mobile = MobileUi.IsMobile;

        // ── 底色 ──
        // 手機：三階全部指向同一個 PanelFill，面板因此完全沒有漸層。
        public static readonly Color BgDarkest = Mobile
            ? MobileUi.FieldFill * MobileUi.PanelAlpha
            : new Color(8, 10, 14, 252);

        public static readonly Color BgDark = Mobile
            ? MobileUi.PanelFill * MobileUi.PanelAlpha
            : new Color(16, 20, 26, 250);

        public static readonly Color BgMid = Mobile
            ? MobileUi.PanelFill * MobileUi.PanelAlpha
            : new Color(24, 30, 38, 248);

        public static readonly Color BgLight = Mobile
            ? MobileUi.TitleBarFill * MobileUi.PanelAlpha
            : new Color(35, 42, 52, 245);

        public static readonly Color BgLighter = Mobile
            ? MobileUi.TitleBarFill * 0.96f
            : new Color(48, 56, 68, 240);

        // ── 點綴 ──
        // 原本這一組是金色，是「很多不必要的線條」看起來最花的主因。手機一律中性灰。
        public static readonly Color Accent = Mobile ? new Color(120, 132, 152) : new Color(212, 175, 85);
        public static readonly Color AccentBright = Mobile ? new Color(170, 180, 196) : new Color(255, 215, 120);
        public static readonly Color AccentDim = Mobile ? new Color(76, 84, 100) : new Color(140, 115, 55);
        public static readonly Color AccentGlow = Mobile ? new Color(140, 152, 172) * 0.09f : new Color(255, 200, 80, 40);

        public static readonly Color Secondary = new(90, 140, 200);
        public static readonly Color SecondaryBright = new(130, 180, 240);
        public static readonly Color SecondaryDim = new(50, 80, 120);

        // ── 框線 ──
        // 手機只留一層看得見的細框。內外框差異拉到幾乎為零，
        // 免得一個面板在小螢幕上出現三條平行線。
        public static readonly Color BorderOuter = Mobile ? new Color(52, 60, 74) * 0.78f : new Color(5, 6, 8, 255);
        public static readonly Color BorderInner = Mobile ? MobileUi.PanelBorder * 0.45f : new Color(60, 70, 85, 200);
        public static readonly Color BorderHighlight = Mobile ? new Color(120, 132, 152) * 0.27f : new Color(100, 110, 130, 120);

        // ── 格子 ──
        public static readonly Color SlotBg = Mobile ? MobileUi.FieldFill * 0.82f : new Color(12, 15, 20, 240);
        public static readonly Color SlotBorder = Mobile ? new Color(58, 66, 80) * 0.59f : new Color(45, 52, 65, 180);
        // 手機的「停留」底色刻意做得很淡。觸控沒有停留這個狀態，手指離開之後
        // 游標留在原地，那一格就會一直亮著 —— 太明顯的話玩家會以為那格被選中了，
        // 甚至以為它正等著被丟棄。0.55 -> 0.22。
        public static readonly Color SlotHover = Mobile ? new Color(96, 108, 128) * 0.22f : new Color(70, 85, 110, 150);
        // 選中同理：0.43 的淺藍灰在深色面板上非常搶眼，使用者回報「看起來像等待刪除」。
        public static readonly Color SlotSelected = Mobile ? new Color(200, 208, 220) * 0.20f : new Color(212, 175, 85, 100);

        // ── 文字 ──
        public static readonly Color TextWhite = Mobile ? MobileUi.TextPrimary : new Color(240, 240, 245);
        public static readonly Color TextGold = Mobile ? MobileUi.TextPrimary : new Color(255, 220, 130);
        public static readonly Color TextGray = Mobile ? MobileUi.TextDim : new Color(160, 165, 175);
        public static readonly Color TextDark = Mobile ? new Color(112, 120, 134) : new Color(100, 105, 115);

        // ── 稀有度光暈 ──
        // 這是資訊（普通／魔法／卓越／古代／傳說），不是裝飾 —— 兩邊一樣。
        public static readonly Color GlowNormal = new(150, 150, 150, 25);
        public static readonly Color GlowMagic = new(100, 150, 255, 50);
        public static readonly Color GlowExcellent = new(120, 255, 120, 60);
        public static readonly Color GlowAncient = new(80, 200, 255, 70);
        public static readonly Color GlowLegendary = new(255, 180, 80, 70);

        // 狀態色是資訊，不是裝飾 —— 兩邊一樣。
        public static readonly Color Success = new(80, 200, 120);
        public static readonly Color Warning = new(240, 180, 60);
        public static readonly Color Danger = new(220, 80, 80);
    }
}
