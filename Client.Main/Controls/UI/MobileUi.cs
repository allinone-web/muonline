#nullable enable
using System;
using System.Collections.Generic;
using Client.Main.Controllers;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Controls.UI
{
    /// <summary>
    /// 手機版介面共用的判斷與繪圖工具。
    ///
    /// 原本搖桿與技能鈕各自用「32 段線段疊 10 圈」拼出圓形 —— 一顆按鈕上百個 quad，
    /// 邊緣還有明顯鋸齒。這裡改成生成帶 alpha 的圓形／圓環貼圖，之後每個圓只要一個
    /// quad，邊緣是平滑的。手機介面圓角元素很多，這個差別直接反映在觀感上。
    ///
    /// <b>顏色一律使用預乘 alpha</b>（例如 <c>new Color(220, 90, 60) * 0.75f</c>），
    /// 與本專案其他 UI 繪製一致 —— 場景使用 <see cref="BlendState.AlphaBlend"/>，
    /// 在 MonoGame 中是預乘混合。直接寫 <c>new Color(r, g, b, a)</c> 會過亮。
    /// </summary>
    public static class MobileUi
    {
        /// <summary>是否為觸控平台。UI 版面在手機與桌面上是兩套，不是同一套縮放。</summary>
        public static bool IsMobile { get; } = OperatingSystem.IsIOS() || OperatingSystem.IsAndroid();

        /// <summary>
        /// 電量（0-1），取不到時回傳負數。由平台端設定 —— 讀電量需要 UIKit，
        /// Client.Main 不能直接引用。
        /// </summary>
        public static Func<float>? BatteryLevelProvider { get; set; }

        private const int TextureSize = 128;

        private static Texture2D? _disc;
        private static Texture2D? _glow;
        private static readonly Dictionary<int, Texture2D> _rings = new();
        private static GraphicsDevice? _device;

        /// <summary>實心圓，邊緣做抗鋸齒過渡。</summary>
        public static Texture2D? Disc => EnsureBaseTextures() ? _disc : null;

        /// <summary>由中心往外衰減的光暈，用於按鈕陰影與按下時的擴散。</summary>
        public static Texture2D? Glow => EnsureBaseTextures() ? _glow : null;

        private static bool EnsureBaseTextures()
        {
            var device = MuGame.Instance?.GraphicsDevice;
            if (device == null)
                return false;

            // 裝置重建後舊貼圖會失效，需重新生成。
            if (_disc != null && !_disc.IsDisposed && _device == device)
                return true;

            _device = device;
            _rings.Clear();
            _disc = CreateTexture(device, (d) => Falloff(1f - d, 1.5f / (TextureSize / 2f)));
            _glow = CreateTexture(device, (d) =>
            {
                float a = MathHelper.Clamp(1f - d, 0f, 1f);
                return a * a;
            });
            return true;
        }

        /// <summary>
        /// 取得相對厚度的圓環貼圖。厚度以半徑的比例表示，因此同一張貼圖放大縮小後
        /// 環的粗細會跟著等比變化 —— 這正是圓形按鈕想要的行為。
        /// </summary>
        private static Texture2D? GetRing(float relativeThickness)
        {
            if (!EnsureBaseTextures() || _device == null)
                return null;

            int key = Math.Clamp((int)MathF.Round(relativeThickness * 100f), 1, 50);
            if (_rings.TryGetValue(key, out var cached) && !cached.IsDisposed)
                return cached;

            float thickness = key / 100f;
            float inner = 1f - thickness;
            float edge = MathF.Max(1.5f / (TextureSize / 2f), thickness * 0.25f);

            var texture = CreateTexture(_device, (d) =>
            {
                float outside = Falloff(1f - d, edge);
                float insideHole = Falloff(d - inner, edge);
                return outside * insideHole;
            });

            _rings[key] = texture;
            return texture;
        }

        private static float Falloff(float value, float edge)
            => MathHelper.Clamp(value / MathF.Max(edge, 1e-5f), 0f, 1f);

        /// <summary>
        /// 生成圓形／圓環貼圖，<b>並且產生完整的 mipmap 鏈</b>。
        ///
        /// 這一點很關鍵：圓弧是用一串小圓點畫出來的，每個點只有 4x4 像素左右，
        /// 等於把 128x128 的貼圖縮小 30 倍。沒有 mipmap 時 GPU 只能點取樣，
        /// 縮小倍率越大就越亂 —— 頭像外圈那兩道弧線看起來「有強烈鋸齒」就是這個原因，
        /// 與顏色、透明度都無關。
        ///
        /// 每一層 mip 都用同一個公式重新計算（而不是把上一層平均下來），
        /// 邊緣的過渡在每個解析度下都剛好是一像素。
        /// </summary>
        private static Texture2D CreateTexture(GraphicsDevice device, Func<float, float> alphaByDistance)
        {
            var texture = new Texture2D(device, TextureSize, TextureSize, true, SurfaceFormat.Color);

            for (int level = 0, size = TextureSize; size >= 1; level++, size /= 2)
            {
                var data = new Color[size * size];
                float center = (size - 1) / 2f;
                float radius = size / 2f;

                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float dx = (x - center) / radius;
                        float dy = (y - center) / radius;
                        float distance = MathF.Sqrt(dx * dx + dy * dy);
                        float alpha = MathHelper.Clamp(alphaByDistance(distance), 0f, 1f);

                        // 預乘 alpha：RGB 與 A 同值，之後乘上預乘的色調就是正確結果。
                        data[y * size + x] = Color.White * alpha;
                    }
                }

                texture.SetData(level, null, data, 0, data.Length);
            }

            return texture;
        }

        private static Rectangle SquareAt(Vector2 center, float radius) => new(
            (int)MathF.Round(center.X - radius),
            (int)MathF.Round(center.Y - radius),
            Math.Max(1, (int)MathF.Round(radius * 2f)),
            Math.Max(1, (int)MathF.Round(radius * 2f)));

        public static void DrawDisc(SpriteBatch sb, Vector2 center, float radius, Color premultipliedColor)
        {
            var texture = Disc;
            if (texture != null && radius > 0f)
                sb.Draw(texture, SquareAt(center, radius), premultipliedColor);
        }

        public static void DrawGlow(SpriteBatch sb, Vector2 center, float radius, Color premultipliedColor)
        {
            var texture = Glow;
            if (texture != null && radius > 0f)
                sb.Draw(texture, SquareAt(center, radius), premultipliedColor);
        }

        /// <summary><paramref name="thickness"/> 以像素計，會換算成貼圖的相對厚度。</summary>
        public static void DrawRing(SpriteBatch sb, Vector2 center, float radius, Color premultipliedColor, float thickness)
        {
            if (radius <= 0f || thickness <= 0f)
                return;

            var texture = GetRing(thickness / radius);
            if (texture != null)
                sb.Draw(texture, SquareAt(center, radius), premultipliedColor);
        }

        /// <summary>
        /// 冷卻遮罩：由上往下蓋住圓形按鈕的一部分，且不超出圓的邊界。
        /// 以貼圖的來源矩形裁切，不必自行計算圓弧。
        /// </summary>
        public static void DrawDiscCooldown(SpriteBatch sb, Vector2 center, float radius, float remainingRatio, Color premultipliedColor)
        {
            var texture = Disc;
            if (texture == null || remainingRatio <= 0f || radius <= 0f)
                return;

            float ratio = MathHelper.Clamp(remainingRatio, 0f, 1f);
            var source = new Rectangle(0, 0, texture.Width, Math.Max(1, (int)MathF.Round(texture.Height * ratio)));
            var dest = new Rectangle(
                (int)MathF.Round(center.X - radius),
                (int)MathF.Round(center.Y - radius),
                Math.Max(1, (int)MathF.Round(radius * 2f)),
                Math.Max(1, (int)MathF.Round(radius * 2f * ratio)));

            sb.Draw(texture, dest, source, premultipliedColor);
        }

        /// <summary>
        /// 冷卻指示：整面壓暗，外圈畫一圈由 12 點鐘順時針收回的亮弧。
        ///
        /// 沒有走「扇形遮罩」那條路 —— SpriteBatch 只畫得了矩形，扇形得預先生成
        /// 一整組角度的遮罩貼圖，量化的角度跳動比連續的弧線還明顯。
        /// 這裡的弧線是連續的，而且沿用既有的 <see cref="DrawArc"/>。
        /// </summary>
        /// <param name="remainingRatio">剩餘比例，1 = 剛按下、0 = 可再次使用。</param>
        public static void DrawCooldownSweep(SpriteBatch sb, Vector2 center, float radius,
            float remainingRatio, Color faceColor, Color arcColor)
        {
            if (radius <= 0f)
                return;

            float ratio = MathHelper.Clamp(remainingRatio, 0f, 1f);
            if (ratio <= 0f)
                return;

            // 整面壓暗 —— 一眼就知道這顆按鈕現在按不動
            DrawDisc(sb, center, radius, faceColor);

            // 外圈的亮弧。起點 -90 度（12 點鐘），順時針收回。
            float arcRadius = radius * 0.90f;
            DrawArc(sb, center, arcRadius,
                -MathHelper.PiOver2, MathHelper.TwoPi * ratio,
                arcColor, radius * 0.11f);
        }

        /// <summary>
        /// 圓弧。以小圓點串成 —— 沒有 shader 也不必為每個比例生成貼圖，
        /// 用來做頭像框外圈的血量指示。
        /// </summary>
        public static void DrawArc(SpriteBatch sb, Vector2 center, float radius,
            float startRadians, float sweepRadians, Color premultipliedColor, float thickness)
        {
            if (radius <= 0f || thickness <= 0f || MathF.Abs(sweepRadians) < 0.001f)
                return;

            // 圓點必須確實重疊，否則邊緣會呈扇貝狀。
            // 間距取一個圓點半徑 —— 直徑是半徑的兩倍，仍有一倍的重疊，
            // 視覺上與更密的間距沒有差別，但每幀少畫一半的 quad。
            float dot = MathF.Max(thickness * 0.5f, 1.5f);
            int steps = Math.Clamp((int)(MathF.Abs(sweepRadians) * radius / dot), 2, 360);

            for (int i = 0; i <= steps; i++)
            {
                float angle = startRadians + sweepRadians * i / steps;
                var point = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
                DrawDisc(sb, point, dot, premultipliedColor);
            }
        }

        /// <summary>
        /// 手機螢幕的圓角會裁掉四個角落。安全區域已由 UiScaler 扣掉，但矩形元素
        /// 緊貼安全區邊界時，仍會被圓角的斜切吃掉一角 —— 角落元素要再往內縮。
        /// </summary>
        public const int CornerInset = 30;

        /// <summary>
        /// 畫面右側所有元件的統一寬度（虛擬座標）。
        ///
        /// 這個值來自右上角的介面按鈕區塊：3 欄 x 96 + 2 x 6 = 300。
        /// 經驗條、狀態列、撿取清單都用同一個寬度與同一條右緣，
        /// 右側才會是一整條對齊的欄，而不是各自為政的方塊。
        /// 見 docs/介面設計規範.md。
        /// </summary>
        public const int RightColumnWidth = 300;

        /// <summary>
        /// 右側對齊線（虛擬座標）。畫面右側的每一個元件 —— 介面按鈕、經驗條、
        /// 狀態列、撿取清單、增益圖示 —— 右緣都要落在這裡。
        ///
        /// 為什麼不是「盡量靠右」：靠右的結果是每個元件各自貼著螢幕邊緣，
        /// 而螢幕邊緣是圓角、是手掌握住的地方。對齊到同一條線之後，
        /// 右側讀起來是一整欄，而不是七個各自為政的方塊。
        ///
        /// <b>這條線只退圓角的餘裕，不退整個安全區域。</b>畫布是滿版的
        /// （見 UiScaler.ConfigureStretch），而 iOS 橫置時左右各回報 68 pt ——
        /// 那是為了避開鏡頭挖孔，但挖孔在畫面側邊的<b>中段</b>，四個角落並不在
        /// 它下面。整條邊都退 68 pt 的結果就是兩側各空掉一大條。
        /// 真正落在挖孔那一段的元件請改用 <see cref="EdgeInsetForBand"/>。
        /// </summary>
        public static int RightEdge => UiScaler.VirtualSize.X - CornerInset;

        /// <summary>左側對齊線。與 <see cref="RightEdge"/> 對稱。</summary>
        public static int LeftEdge => CornerInset;

        /// <summary>
        /// 鏡頭挖孔在橫置時佔畫面側邊的中段。這裡把它視為畫面高度的中間 60%。
        /// </summary>
        private const float IslandBandStart = 0.20f;
        private const float IslandBandEnd = 0.80f;

        /// <summary>
        /// 垂直範圍 <paramref name="top"/>..<paramref name="bottom"/> 的元件，
        /// 距離左右螢幕邊緣應該退多少。
        ///
        /// 落在挖孔那一段的話要退掉整個安全區域再加一點餘裕（貼著安全區邊界的
        /// 圓形按鈕仍會被圓角啃掉一角）；在上下角落的話只需要圓角的餘裕。
        /// </summary>
        public static int EdgeInsetForBand(int top, int bottom)
        {
            if (!IsMobile)
                return CornerInset;

            int height = UiScaler.VirtualSize.Y;
            float bandTop = height * IslandBandStart;
            float bandBottom = height * IslandBandEnd;

            bool overlapsIsland = bottom > bandTop && top < bandBottom;
            if (!overlapsIsland)
                return CornerInset;

            var safe = UiScaler.SafeAreaVirtual;
            int worst = (int)MathF.Ceiling(MathF.Max(safe.X, safe.Z));
            return worst + 16;
        }

        /// <summary>
        /// 這個矩形的右緣是否已經對齊 <see cref="RightEdge"/>。除錯用。
        /// </summary>
        public static bool IsRightAligned(Rectangle rect) => rect.Right == RightEdge;

        // ───────────────────────── 介面樣式 ─────────────────────────
        //
        // 登入、選伺服器、選角色、遊戲內的面板全部共用這一組值。
        // 半透明深色 + 一條細邊框 + 白灰兩色文字 —— 顏色只留給真正帶資訊的東西。

        public static readonly Color PanelFill = new(16, 20, 28);
        public static readonly Color PanelBorder = new(104, 116, 138);
        public static readonly Color TitleBarFill = new(28, 34, 44);
        public static readonly Color TextPrimary = new(238, 240, 245);
        public static readonly Color TextDim = new(150, 158, 172);
        public static readonly Color FieldFill = new(10, 13, 19);

        /// <summary>生命。<b>只用在生命上。</b></summary>
        public static readonly Color Hp = new(206, 62, 58);

        /// <summary>魔力。<b>只用在魔力上。</b></summary>
        public static readonly Color Mp = new(72, 132, 208);

        /// <summary>進度條／弧線的底槽。</summary>
        public static readonly Color Track = new(58, 64, 76);

        /// <summary>面板的預設不透明度。半透明才看得到後面的場景，畫面比較有層次。</summary>
        public const float PanelAlpha = 0.88f;

        /// <summary>
        /// 畫一個標準面板：半透明底 + 細邊框，標題列可選。
        /// </summary>
        public static void DrawPanel(SpriteBatch sb, Rectangle rect, int titleHeight = 0, float alpha = PanelAlpha)
        {
            var pixel = Controllers.GraphicsManager.Instance?.Pixel;
            if (pixel == null)
                return;

            sb.Draw(pixel, rect, PanelFill * alpha);

            if (titleHeight > 0)
            {
                sb.Draw(pixel, new Rectangle(rect.X, rect.Y, rect.Width, titleHeight), TitleBarFill * alpha);
                sb.Draw(pixel, new Rectangle(rect.X, rect.Y + titleHeight - 1, rect.Width, 1), Color.White * 0.14f);
            }

            var border = PanelBorder * 0.45f;
            sb.Draw(pixel, new Rectangle(rect.X, rect.Y, rect.Width, 1), border);
            sb.Draw(pixel, new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1), border);
            sb.Draw(pixel, new Rectangle(rect.X, rect.Y, 1, rect.Height), border);
            sb.Draw(pixel, new Rectangle(rect.Right - 1, rect.Y, 1, rect.Height), border);
        }

        /// <summary>
        /// 在給定的列數與視窗其餘高度之下，格子最大可以多大而視窗仍然放得進畫面。
        ///
        /// 為什麼需要這個：把格子統一放大到 64 之後，商店與倉庫（都是 8 x 15）
        /// 的視窗變成 960 px 高，比畫布還高 —— 標題列被推到畫面外，關閉鈕點不到。
        /// 格子大小不能只看「手指好不好按」，還要看那個視窗有幾列。
        ///
        /// <paramref name="chromeHeight"/> 是標題列、區塊標題、內距、底列、邊界的總和。
        /// 下限 40：再小就和原本的 32 沒有差別了；上限交給呼叫端（一般是 64，和背包一致）。
        /// </summary>
        public static int FitCellSize(int rows, int chromeHeight, int max, int min = 40)
        {
            if (!IsMobile || rows <= 0)
                return max;

            int available = UiScaler.VirtualSize.Y - chromeHeight - CornerInset * 2;
            int fitted = available / rows;
            return Math.Clamp(fitted, min, max);
        }

        /// <summary>
        /// 視窗的關閉鈕：一塊底 + 一個用兩條線畫出來的叉。
        ///
        /// 沒有紅色。整個面板只要有一顆飽和色的按鈕，眼睛就會一直被它拉過去 ——
        /// 而關閉鈕從來不是玩家打開視窗時要找的東西。
        ///
        /// <b>位置一律在視窗左上角。</b>螢幕右上角是那六顆介面按鈕，視窗的關閉鈕
        /// 再放右上角就會疊在同一塊區域，拇指分不開。
        /// </summary>
        public static void DrawCloseGlyph(SpriteBatch sb, Rectangle rect, bool pressed)
        {
            var pixel = Controllers.GraphicsManager.Instance?.Pixel;
            if (pixel == null)
                return;

            sb.Draw(pixel, rect, (pressed ? TitleBarFill * 1.6f : TitleBarFill) * PanelAlpha);
            sb.Draw(pixel, new Rectangle(rect.X, rect.Y, rect.Width, 1), PanelBorder * 0.35f);
            sb.Draw(pixel, new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1), PanelBorder * 0.35f);
            sb.Draw(pixel, new Rectangle(rect.X, rect.Y, 1, rect.Height), PanelBorder * 0.35f);
            sb.Draw(pixel, new Rectangle(rect.Right - 1, rect.Y, 1, rect.Height), PanelBorder * 0.35f);

            // 叉：兩條 45 度的線，用 1x1 的 pixel 旋轉出來。
            var color = (pressed ? TextPrimary : TextDim) * 0.95f;
            var center = new Vector2(rect.Center.X, rect.Center.Y);
            float arm = rect.Width * 0.30f;
            const int thickness = 2;

            for (int i = 0; i < 2; i++)
            {
                float rotation = i == 0 ? MathHelper.PiOver4 : -MathHelper.PiOver4;
                sb.Draw(
                    pixel,
                    center,
                    null,
                    color,
                    rotation,
                    new Vector2(0.5f, 0.5f),
                    new Vector2(arm * 2f, thickness),
                    SpriteEffects.None,
                    0f);
            }
        }

        /// <summary>
        /// 捲軸：一條底槽 + 一塊滑塊，都是純色矩形。
        ///
        /// 原本用的是三張 9-slice 貼圖（上端帽、可平鋪的中段、下端帽）加一張滑塊，
        /// 光是中段就得跑一個迴圈逐段貼。在手機上那三張圖總共佔不到 12 px 寬，
        /// 誰也看不出接縫在哪 —— 換成兩個矩形，順便省掉四張貼圖。
        /// </summary>
        public static void DrawScrollbar(SpriteBatch sb, Rectangle track, Rectangle thumb, bool dragging)
        {
            var pixel = Controllers.GraphicsManager.Instance?.Pixel;
            if (pixel == null)
                return;

            sb.Draw(pixel, track, Track * 0.35f);

            if (!thumb.IsEmpty)
            {
                // 太短的滑塊抓不到。給一個最小高度，代價是滑動範圍略微失真，
                // 但「抓得到」比「長度精準」重要得多。
                if (thumb.Height < 28)
                {
                    int grow = 28 - thumb.Height;
                    thumb.Y = Math.Max(track.Y, thumb.Y - grow / 2);
                    thumb.Height = Math.Min(track.Height, 28);
                    if (thumb.Bottom > track.Bottom)
                        thumb.Y = track.Bottom - thumb.Height;
                }

                sb.Draw(pixel, thumb, PanelBorder * (dragging ? 0.95f : 0.6f));
            }
        }

        /// <summary>
        /// 右下角的縮放握把：三條短斜線。取代 newui_scrollbar_stretch.jpg。
        /// </summary>
        public static void DrawResizeGrip(SpriteBatch sb, Rectangle rect, bool dragging)
        {
            var pixel = Controllers.GraphicsManager.Instance?.Pixel;
            if (pixel == null || rect.IsEmpty)
                return;

            var color = PanelBorder * (dragging ? 0.9f : 0.55f);
            for (int i = 1; i <= 3; i++)
            {
                int inset = i * 4;
                sb.Draw(pixel, new Rectangle(rect.Right - inset - 2, rect.Bottom - 3, inset + 2, 2), color);
                sb.Draw(pixel, new Rectangle(rect.Right - 3, rect.Bottom - inset - 2, 2, inset + 2), color);
            }
        }

        /// <summary>
        /// 視窗開啟時的滑入動畫。
        ///
        /// 視窗瞬間出現是最明顯的「沒做完」的感覺。位移只要 18 px、時間 0.18 秒，
        /// 眼睛就會把它讀成「這個面板是滑進來的」而不是「畫面閃了一下」。
        ///
        /// 只動<b>位置</b>不動透明度：面板裡的每個元素都是相對視窗座標繪製的，
        /// 一起移動就會一起動；透明度則要每個繪製點都乘上去，漏一個就會出現
        /// 「框在淡入、內容已經全亮」的破綻。
        /// </summary>
        public sealed class OpenAnimation
        {
            private const float DurationSeconds = 0.18f;
            private const float OffsetY = 18f;

            private float _elapsed = DurationSeconds;

            /// <summary>視窗開啟時呼叫，重新播放。</summary>
            public void Restart() => _elapsed = 0f;

            public void Update(float deltaSeconds)
            {
                if (_elapsed < DurationSeconds)
                    _elapsed = MathF.Min(_elapsed + deltaSeconds, DurationSeconds);
            }

            /// <summary>目前要加在視窗 Y 上的偏移（結束時為 0）。</summary>
            public int OffsetPixels
            {
                get
                {
                    if (!IsMobile || _elapsed >= DurationSeconds)
                        return 0;

                    float t = _elapsed / DurationSeconds;
                    // ease-out：一開始快、接近定位時慢下來
                    float eased = 1f - (1f - t) * (1f - t);
                    return (int)MathF.Round(OffsetY * (1f - eased));
                }
            }
        }

        /// <summary>手機的可用區域（虛擬座標）。UiScaler 已把安全區域併入縮放，因此就是整個虛擬畫布。</summary>
        public static Rectangle SafeArea => new(0, 0, UiScaler.VirtualSize.X, UiScaler.VirtualSize.Y);
    }
}
