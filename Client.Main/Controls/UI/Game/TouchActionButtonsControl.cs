#nullable enable
using System;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls.UI.Game.Skills;
using Client.Main.Core.Client;
using Client.Main.Core.Utilities;
using Client.Main.Graphics;
using Client.Main.Helpers;
using Client.Main.Models;
using Client.Main.Scenes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Client.Main.Controls.UI.Game
{
    /// <summary>
    /// 手機用的攻擊／技能按鈕，配置在畫面右下角。
    ///
    /// 桌面的流程是「先按 1-0 選技能、再點目標」，手機上要點兩次而且得點準怪物。
    /// 這裡改成手遊 MMO 的標準做法：一顆大的普通攻擊鈕，外圈是技能鈕，
    /// 按下去就自動鎖定最近的敵人出手，不需要先選再點。
    ///
    /// 技能沿用底部快捷列的格子（第 4-7 格），但<b>指派也在這裡完成</b> ——
    /// 長按技能鈕就開啟技能選擇面板。手機版的快捷列不再顯示技能格，
    /// 若不在這裡提供指派入口，玩家將完全無法設定技能。
    /// </summary>
    public class TouchActionButtonsControl : UIControl
    {
        public const int MaxSkillButtons = 4;

        private const float MainButtonRadius = 64f;
        private const float SkillButtonRadius = 40f;

        /// <summary>
        /// 主按鈕圓心與畫面右下角的距離（虛擬座標）。
        ///
        /// 右邊距不是隨手挑的：它要讓整個按鈕群（主按鈕 + 左上方那條技能弧線）
        /// 的最右緣落在 <see cref="MobileUi.RightEdge"/> 上，和右上角的介面按鈕
        /// 同一條線。弧線上最靠右的是第四顆技能鈕（288 度），它的圓心在主按鈕
        /// 右邊 41 px，半徑 40 —— 所以整群的最右緣是「圓心 + 81」。
        ///
        /// 額外再退 24 px，是給橫置時的鏡頭挖孔留的餘裕：安全區域雖然已經由
        /// UiScaler 扣掉（見 MuGame.PollSafeArea），但挖孔周圍的圓角仍會斜切，
        /// 貼著安全區邊界的圓形按鈕還是會被啃掉一角。
        /// </summary>
        private const float NotchClearance = 24f;
        private const float SkillArcRightOverhang = 81f;
        private static float MarginRight => MobileUi.CornerInset + SkillArcRightOverhang + NotchClearance;
        private const float MarginBottom = 120f;

        /// <summary>技能鈕排在主按鈕左上方的弧線上。</summary>
        private const float SkillArcRadius = 132f;
        private const float SkillArcStartDegrees = 150f;
        // 42 度時相鄰技能鈕只差 15 px（虛擬座標），實機約 26 px —— 拇指容易按錯。
        // 46 度讓每對按鈕之間都留下 20 px 以上。
        private const float SkillArcStepDegrees = 46f;

        // 字級。字型基準高度是 Constants.BASE_FONT_SIZE（25 px），
        // 因此 1.0 約等於 25 px 高的字 —— 直徑 128 px 的主按鈕配 0.95 剛好。
        // （先前寫成「半徑 x 係數」，ATK 算出 3.33，字寬是圓的兩倍。）
        private const float MainLabelScale = 0.95f;
        private const float SkillPlaceholderScale = 0.85f;
        private const float CooldownTextScale = 0.80f;
        private const float FallbackIdScale = 0.50f;

        /// <summary>按下後的視覺回饋時間。</summary>
        private const double PressFeedbackSeconds = 0.18;

        /// <summary>長按多久算「要指派技能」而不是「要施放」。</summary>
        private const double LongPressSeconds = 0.45;

        private readonly Func<int, SkillEntryState?> _skillAt;
        private readonly Action<int> _requestAssign;

        private Vector2 _mainCenter;
        private readonly Vector2[] _skillCenters = new Vector2[MaxSkillButtons];
        private readonly double[] _skillPressedAt = new double[MaxSkillButtons];
        private double _mainPressedAt;

        /// <summary>出手失敗（附近沒有敵人／魔力不足）時閃紅，否則玩家完全得不到回饋。</summary>
        private double _failedAt = double.NegativeInfinity;

        /// <summary>失敗原因，畫在按鈕上方。只閃紅的話玩家不知道要補魔還是換位置。</summary>
        private string? _failureReason;
        private const double FailureReasonSeconds = 1.6;
        private const float FailureReasonScale = 0.62f;

        // ── 施放回饋（冷卻圈）──
        //
        // skill_eng.bmd 的 Delay 欄位在 Season 20 的資料裡幾乎全是 0
        // （實測 1024 筆中只有地裂 62、連環腿 262、龍吼 264、龍斬 265、鳳凰擊 270 有值），
        // 所以只靠 SkillCooldownTracker 的話，冷卻圈對絕大多數技能永遠不會出現。
        //
        // 真正限制連續出手的是「出手動作還沒演完」（TryBeginSkillCast 的第一個判斷），
        // 因此這裡另外記一段本地的施放視窗：按下成功就開始，動作演完就收掉。
        // 畫出來的比例取兩者的較大值 —— 有真冷卻就照真冷卻，沒有就反映動作鎖。
        private readonly double[] _castStartedAt = new double[MaxSkillButtons];
        private readonly double[] _castDuration = new double[MaxSkillButtons];
        private double _mainCastStartedAt;
        private double _mainCastDuration;

        /// <summary>沒有真實冷卻資料時，施放回饋的名目長度（秒）。動作提早結束就提早收掉。</summary>
        private const double DefaultCastFeedbackSeconds = 0.9;

        /// <summary>動作鎖至少顯示這麼久，否則太短的動作會讓圓圈只閃一格。</summary>
        private const double MinCastFeedbackSeconds = 0.22;

        // ── 連擊指示（劍士）──
        // 伺服器算連擊，客戶端只顯示進度。段數與規則見 SkillComboTracker。
        private const float ComboPipRadius = 7f;
        private const float ComboPipSpacing = 22f;
        private const float ComboLabelScale = 0.58f;
        private const double ComboFlashSeconds = 1.1;

        // 觸控狀態：-1 = 沒有按住，0 = 主按鈕，1..N = 技能鈕
        private int _heldButton = -1;
        private double _heldElapsed;
        private bool _longPressHandled;
        private bool _wasPressed;

        private SpriteFont? _font;

        public TouchActionButtonsControl(Func<int, SkillEntryState?> skillAt, Action<int> requestAssign)
        {
            _skillAt = skillAt;
            _requestAssign = requestAssign;
            Interactive = false;   // 自行處理觸控，避免與 UI 點擊路由互搶
            AutoViewSize = false;
            ViewSize = new Point(1, 1);
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (Status != GameControlStatus.Ready || !Visible)
                return;

            RefreshLayout();

            var mouse = MuGame.Instance.UiMouseState;
            bool pressed = mouse.LeftButton == ButtonState.Pressed;
            var position = new Vector2(mouse.X, mouse.Y);
            double now = gameTime.TotalGameTime.TotalSeconds;

            if (pressed && !_wasPressed)
            {
                // 這些按鈕不走 UI 的點擊路由，必須自己避開開著的視窗。
                // 技能面板就在畫面中央，較大的版面會壓到技能弧線的左端 ——
                // 少了這個判斷，在面板裡選技能會同時把技能放出去。
                if (MuGame.Instance.ActiveScene is GameScene guardScene &&
                    guardScene.IsPointOverOpenWindow(new Point(mouse.X, mouse.Y)))
                {
                    _heldButton = -1;
                    _wasPressed = true;
                    return;
                }

                _heldButton = HitTest(position);
                _heldElapsed = 0;
                _longPressHandled = false;
            }
            else if (pressed && _heldButton >= 0)
            {
                // 手指滑出按鈕就取消，避免誤觸
                if (HitTest(position) != _heldButton)
                {
                    _heldButton = -1;
                }
                else
                {
                    _heldElapsed += gameTime.ElapsedGameTime.TotalSeconds;
                    if (_heldElapsed >= LongPressSeconds && !_longPressHandled && _heldButton > 0)
                    {
                        _longPressHandled = true;
                        _requestAssign?.Invoke(_heldButton - 1);
                    }
                }
            }
            else if (!pressed && _wasPressed)
            {
                if (_heldButton >= 0 && !_longPressHandled)
                    Activate(_heldButton, now);

                _heldButton = -1;
                _longPressHandled = false;
            }

            _wasPressed = pressed;
        }

        private void RefreshLayout()
        {
            var size = UiScaler.VirtualSize;
            _mainCenter = new Vector2(size.X - MarginRight, size.Y - MarginBottom);

            for (int i = 0; i < MaxSkillButtons; i++)
            {
                float radians = MathHelper.ToRadians(SkillArcStartDegrees + SkillArcStepDegrees * i);
                _skillCenters[i] = _mainCenter + new Vector2(
                    MathF.Cos(radians) * SkillArcRadius,
                    MathF.Sin(radians) * SkillArcRadius);
            }
        }

        /// <summary>回傳 0 = 主按鈕、1..N = 第 N 顆技能鈕、-1 = 沒有命中。</summary>
        private int HitTest(Vector2 position)
        {
            if (Vector2.Distance(position, _mainCenter) <= MainButtonRadius)
                return 0;

            for (int i = 0; i < MaxSkillButtons; i++)
            {
                if (Vector2.Distance(position, _skillCenters[i]) <= SkillButtonRadius)
                    return i + 1;
            }

            return -1;
        }

        private void Activate(int button, double now)
        {
            if (MuGame.Instance.ActiveScene is not GameScene scene)
                return;

            if (button == 0)
            {
                _mainPressedAt = now;
                if (scene.AttackNearestEnemy(null))
                {
                    _mainCastStartedAt = now;
                    _mainCastDuration = DefaultCastFeedbackSeconds;
                }
                else
                {
                    ReportFailure(scene, now);
                }
                return;
            }

            int index = button - 1;
            var skill = _skillAt?.Invoke(index);
            if (skill == null)
            {
                // 空鈕輕點也開指派面板 —— 不必先知道「要長按」才設定得了技能
                _requestAssign?.Invoke(index);
                return;
            }

            _skillPressedAt[index] = now;
            if (scene.AttackNearestEnemy(skill))
            {
                int delayMs = SkillDatabase.GetSkillCooldown(skill.SkillId);
                _castStartedAt[index] = now;
                _castDuration[index] = delayMs > 0
                    ? delayMs / 1000.0
                    : DefaultCastFeedbackSeconds;
            }
            else
            {
                ReportFailure(scene, now);
            }
        }

        /// <summary>
        /// 記下失敗與原因。原因可能是 null —— 例如「上一個動作還沒演完」，
        /// 那是正常節奏，連紅光都不該閃。
        /// </summary>
        private void ReportFailure(GameScene scene, double now)
        {
            string? reason = scene.LastSkillFailureReason;
            if (string.IsNullOrEmpty(reason))
                return;

            _failedAt = now;
            _failureReason = reason;
        }

        /// <summary>
        /// 這顆技能鈕現在該畫多少冷卻。取「真實冷卻」與「本地施放視窗」的較大值。
        /// </summary>
        private float GetCooldownRatio(int index, SkillEntryState skill, double nowSeconds)
        {
            double nowMs = MuGame.Instance?.GameTime?.TotalGameTime.TotalMilliseconds ?? Environment.TickCount64;
            float ratio = SkillCooldownTracker.GetCooldownRatio(skill.SkillId, nowMs);

            double duration = _castDuration[index];
            if (duration <= 0)
                return ratio;

            double elapsed = nowSeconds - _castStartedAt[index];
            if (elapsed < 0 || elapsed >= duration)
            {
                _castDuration[index] = 0;
                return ratio;
            }

            // 沒有真實冷卻資料時，出手動作結束就代表可以再按了 —— 提早收掉圓圈，
            // 不要讓玩家對著一個其實已經可用的按鈕乾等。
            if (SkillDatabase.GetSkillCooldown(skill.SkillId) <= 0 &&
                elapsed >= MinCastFeedbackSeconds &&
                !IsHeroBusy())
            {
                _castDuration[index] = 0;
                return ratio;
            }

            return MathF.Max(ratio, (float)(1.0 - elapsed / duration));
        }

        private static bool IsHeroBusy()
        {
            return MuGame.Instance?.ActiveScene is GameScene scene
                && (scene.Hero?.IsAttackOrSkillAnimationPlaying() ?? false);
        }

        /// <summary>按鈕是否吃掉了這個座標的觸控 —— 供外部避免同時觸發搖桿或世界點擊。</summary>
        public bool ContainsPoint(Vector2 position) => Visible && HitTest(position) >= 0;

        public override void Draw(GameTime gameTime)
        {
            if (Status != GameControlStatus.Ready || !Visible)
                return;

            var sb = GraphicsManager.Instance.Sprite;
            if (sb == null)
                return;

            _font ??= GraphicsManager.Instance.Font;
            double now = gameTime.TotalGameTime.TotalSeconds;
            bool failing = now - _failedAt < PressFeedbackSeconds * 2;

            // 場景可能已經開好批次；重複 Begin 會失敗，畫面上就什麼都看不到。
            SpriteBatchScope? scope = null;
            if (!SpriteBatchScope.BatchIsBegun)
            {
                scope = new SpriteBatchScope(
                    sb, SpriteSortMode.Deferred, BlendState.AlphaBlend,
                    SamplerState.LinearClamp, transform: UiScaler.SpriteTransform);
            }

            try
            {
                DrawMainButton(sb, now, failing);

                for (int i = 0; i < MaxSkillButtons; i++)
                    DrawSkillButton(sb, i, now, failing);

                DrawComboIndicator(sb, now);
                DrawFailureReason(sb, now);
            }
            finally
            {
                scope?.Dispose();
            }

            base.Draw(gameTime);
        }

        private void DrawMainButton(SpriteBatch sb, double now, bool failing)
        {
            bool held = _heldButton == 0;
            float pressPulse = PressPulse(_mainPressedAt, now);

            // 不再用實心紅。半透明的白比較耐看，也不會跟遊戲畫面搶顏色。
            // 只有「打不到」的回饋才短暫染紅 —— 顏色留給真正有意義的事。
            Color face = failing
                ? new Color(190, 70, 66) * 0.55f
                : Color.White * (0.15f + (held ? 0.16f : 0f) + 0.18f * pressPulse);

            DrawButtonBody(sb, _mainCenter, MainButtonRadius, face, pressPulse, held);

            // 普通攻擊也走一圈施放回饋 —— 出手節奏由動作長度決定，
            // 玩家看得到「還在揮」而不是覺得按鈕沒反應。
            if (_mainCastDuration > 0)
            {
                double elapsed = now - _mainCastStartedAt;
                if (elapsed < 0 || elapsed >= _mainCastDuration ||
                    (elapsed >= MinCastFeedbackSeconds && !IsHeroBusy()))
                {
                    _mainCastDuration = 0;
                }
                else
                {
                    MobileUi.DrawCooldownSweep(sb, _mainCenter, MainButtonRadius * 0.93f,
                        (float)(1.0 - elapsed / _mainCastDuration),
                        Color.Black * 0.30f, Color.White * 0.55f);
                }
            }

            if (_font != null)
            {
                DrawCenteredLabel(sb, "ATK", _mainCenter, MainLabelScale, Color.White * 0.95f);
            }
        }

        private void DrawSkillButton(SpriteBatch sb, int index, double now, bool failing)
        {
            var center = _skillCenters[index];
            var skill = _skillAt?.Invoke(index);
            bool held = _heldButton == index + 1;
            float pressPulse = PressPulse(_skillPressedAt[index], now);

            if (skill == null)
            {
                // 未指派：低調的虛位，並標示可以按下去設定
                DrawButtonBody(sb, center, SkillButtonRadius, Color.White * 0.07f, 0f, held);
                if (_font != null)
                    DrawCenteredLabel(sb, "+", center, SkillPlaceholderScale, Color.White * 0.55f);
                return;
            }

            bool enoughMana = HasEnoughResources(skill);

            // 技能鈕本身保持中性 —— 圖示已經夠花了，底色再上藍只會更亂
            Color face = !enoughMana
                ? new Color(10, 12, 18) * 0.45f
                : Color.White * (0.12f + (held ? 0.14f : 0f) + 0.16f * pressPulse);

            DrawButtonBody(sb, center, SkillButtonRadius, face, pressPulse, held);
            DrawSkillIcon(sb, center, SkillButtonRadius, skill, enoughMana);
            DrawCooldown(sb, center, SkillButtonRadius, index, skill, now);
        }

        /// <summary>圓形按鈕的共通外觀：底部陰影、面、外環、按下時的擴散。</summary>
        private static void DrawButtonBody(SpriteBatch sb, Vector2 center, float radius, Color face, float pressPulse, bool held)
        {
            // 半透明的按鈕：底下墊一層薄薄的暗色讓圖示與文字有對比，
            // 但不做成實心色塊 —— 遊戲畫面要看得見。
            MobileUi.DrawGlow(sb, center + new Vector2(0f, radius * 0.10f), radius * 1.28f, Color.Black * 0.34f);
            MobileUi.DrawDisc(sb, center, radius, new Color(8, 10, 14) * 0.34f);
            MobileUi.DrawDisc(sb, center, radius * 0.93f, face);

            // 一圈細白邊界定形狀，不用彩色
            MobileUi.DrawRing(sb, center, radius, Color.White * (held ? 0.55f : 0.34f), radius * 0.045f);

            if (pressPulse > 0f)
                MobileUi.DrawRing(sb, center, radius * (1f + 0.28f * (1f - pressPulse)),
                    Color.White * (0.45f * pressPulse), radius * 0.05f);
        }

        private void DrawSkillIcon(SpriteBatch sb, Vector2 center, float radius, SkillEntryState skill, bool enabled)
        {
            var definition = SkillDatabase.GetSkillDefinition(skill.SkillId);
            if (!SkillIconAtlas.TryResolve(skill.SkillId, definition, out var frame))
            {
                // 沒有對應圖示時退回顯示技能等級，總比一片空白好
                if (_font != null)
                    DrawCenteredLabel(sb, skill.SkillId.ToString(), center, FallbackIdScale, Color.White * 0.8f);
                return;
            }

            var tex = TextureLoader.Instance.GetTexture2D(frame.TexturePath);
            if (tex == null)
                return;

            var tint = Color.White * (enabled ? 1f : 0.45f);

            // 圖集裡是 20x28 的直式滿版插畫。做成圓形貼圖後才能完整填滿圓形按鈕；
            // 直接畫原圖會有上下兩截凸出圓外（實機截圖確認過）。
            var circular = SkillIconCircleCache.TryGet(
                MuGame.Instance?.GraphicsDevice, tex, frame.TexturePath, frame.SourceRectangle);

            if (circular != null)
            {
                float r = radius * 0.90f;
                sb.Draw(circular, new Rectangle(
                    (int)MathF.Round(center.X - r),
                    (int)MathF.Round(center.Y - r),
                    (int)MathF.Round(r * 2f),
                    (int)MathF.Round(r * 2f)), tint);
                return;
            }

            // 退路：等比縮到圓的內接正方形內，角落就不會超出圓周
            float box = radius * 1.41f;
            float fit = MathF.Min(box / SkillIconAtlas.IconWidth, box / SkillIconAtlas.IconHeight);
            int w = Math.Max(1, (int)MathF.Round(SkillIconAtlas.IconWidth * fit));
            int h = Math.Max(1, (int)MathF.Round(SkillIconAtlas.IconHeight * fit));

            var dest = new Rectangle(
                (int)MathF.Round(center.X - w * 0.5f),
                (int)MathF.Round(center.Y - h * 0.5f),
                w, h);

            sb.Draw(tex, dest, frame.SourceRectangle, tint);
        }

        private void DrawCooldown(SpriteBatch sb, Vector2 center, float radius, int index, SkillEntryState skill, double nowSeconds)
        {
            float ratio = GetCooldownRatio(index, skill, nowSeconds);
            if (ratio <= 0f)
                return;

            MobileUi.DrawCooldownSweep(sb, center, radius * 0.93f, ratio,
                Color.Black * 0.42f, new Color(120, 190, 255) * 0.85f);

            // 秒數只在「真的有冷卻資料」時顯示 —— 為出手動作標上 0.4 秒是雜訊。
            double nowMs = MuGame.Instance?.GameTime?.TotalGameTime.TotalMilliseconds ?? Environment.TickCount64;
            int remainingMs = SkillCooldownTracker.GetRemainingMs(skill.SkillId, nowMs);
            if (_font != null && remainingMs > 0)
            {
                // 一秒以上顯示整數秒（無條件進位，最後一秒才會看到 1）；
                // 一秒以內顯示到小數一位，且無條件<b>捨去</b>到十分位 —— 顯示的數字
                // 永遠不會大於真正剩下的時間。
                //
                // 原本的寫法是 (remainingMs + 99) / 100f，少除了一個 10：
                // 剩 999 ms 時算出 10.98，於是最後一秒不是從 1.0 數到 0，
                // 而是從 11.0 一路數回 1.0 —— 看起來就像數字突然跳掉。
                string text = remainingMs >= 1000
                    ? ((remainingMs + 999) / 1000).ToString()
                    : (MathF.Floor(remainingMs / 100f) / 10f).ToString("F1");
                DrawCenteredLabel(sb, text, center, CooldownTextScale, Color.White);
            }
        }

        /// <summary>
        /// 連擊進度：主按鈕正上方三顆指示燈，成立時閃一次 COMBO。
        ///
        /// 位置刻意貼著 ATK 鈕 —— 連擊是靠連續出手達成的，
        /// 玩家的視線在按鈕上，指示放到畫面別處就等於沒有。
        /// </summary>
        private void DrawComboIndicator(SpriteBatch sb, double now)
        {
            if (MuGame.Instance?.ActiveScene is not GameScene scene)
                return;

            var tracker = scene.ComboTracker;
            if (tracker == null || !tracker.ShouldDisplay(now))
                return;

            // COMBO! 只認伺服器確認的那一次（技能 59），不看本地預測
            double sinceAchieved = tracker.SinceAchieved(now);
            bool flashing = sinceAchieved >= 0 && sinceAchieved < ComboFlashSeconds;
            int step = tracker.CurrentStep;
            int total = tracker.StepCount;

            var center = new Vector2(_mainCenter.X, _mainCenter.Y - MainButtonRadius - 26f);
            float startX = center.X - (total - 1) * ComboPipSpacing * 0.5f;

            for (int i = 0; i < total; i++)
            {
                var pip = new Vector2(startX + i * ComboPipSpacing, center.Y);

                // 連擊成立時三顆全亮並一起閃；否則只亮已完成的段
                bool lit = flashing || i < step;

                Color fill = lit
                    ? (flashing ? new Color(255, 210, 120) : new Color(255, 190, 90)) * (flashing ? FlashAlpha(sinceAchieved) : 0.9f)
                    : Color.White * 0.14f;

                MobileUi.DrawDisc(sb, pip, ComboPipRadius, fill);
                MobileUi.DrawRing(sb, pip, ComboPipRadius + 1.5f, Color.White * (lit ? 0.5f : 0.22f), 1.6f);
            }

            // 剩餘時間：最後一顆指示燈右側的細弧。3 秒的限制不寫出來玩家不會知道。
            if (step > 0 && !flashing)
            {
                double remaining = tracker.RemainingSeconds(now);
                if (remaining > 0)
                {
                    var arcCenter = new Vector2(startX + total * ComboPipSpacing, center.Y);
                    MobileUi.DrawArc(sb, arcCenter, ComboPipRadius + 2f,
                        -MathHelper.PiOver2, MathHelper.TwoPi * (float)(remaining / 3.0),
                        new Color(255, 190, 90) * 0.75f, 2.2f);
                }
            }

            if (flashing && _font != null)
            {
                DrawCenteredLabel(sb, "COMBO!", new Vector2(center.X, center.Y - 20f),
                    ComboLabelScale, new Color(255, 225, 150) * FlashAlpha(sinceAchieved));
            }
        }

        /// <summary>成立後先亮著、最後三分之一淡出。</summary>
        private static float FlashAlpha(double sinceAchieved)
            => (float)Math.Min(1.0, (ComboFlashSeconds - sinceAchieved) / (ComboFlashSeconds / 3.0));

        /// <summary>
        /// 失敗原因，畫在整組按鈕的上方。位置固定在主按鈕正上方，
        /// 不跟著哪一顆按鈕跑 —— 眼睛不必先找它在哪。
        /// </summary>
        private void DrawFailureReason(SpriteBatch sb, double now)
        {
            if (_font == null || string.IsNullOrEmpty(_failureReason))
                return;

            double elapsed = now - _failedAt;
            if (elapsed < 0 || elapsed >= FailureReasonSeconds)
            {
                _failureReason = null;
                return;
            }

            // 最後三分之一淡出
            float alpha = (float)Math.Min(1.0, (FailureReasonSeconds - elapsed) / (FailureReasonSeconds / 3.0));

            var anchor = new Vector2(
                _mainCenter.X,
                _mainCenter.Y - MainButtonRadius - SkillArcRadius * 0.55f);

            var size = _font.MeasureString(_failureReason) * FailureReasonScale;
            var position = anchor - size * 0.5f;

            // 底板讓文字在任何場景上都讀得到
            var pad = new Vector2(10f, 4f);
            sb.Draw(GraphicsManager.Instance.Pixel,
                new Rectangle(
                    (int)(position.X - pad.X), (int)(position.Y - pad.Y),
                    (int)(size.X + pad.X * 2f), (int)(size.Y + pad.Y * 2f)),
                Color.Black * (0.55f * alpha));

            sb.DrawString(_font, _failureReason, position + Vector2.One, Color.Black * (0.75f * alpha),
                0f, Vector2.Zero, FailureReasonScale, SpriteEffects.None, 0f);
            sb.DrawString(_font, _failureReason, position, new Color(255, 190, 150) * alpha,
                0f, Vector2.Zero, FailureReasonScale, SpriteEffects.None, 0f);
        }

        private static bool HasEnoughResources(SkillEntryState skill)
        {
            var state = MuGame.Network?.GetCharacterState();
            if (state == null)
                return true;

            return state.CurrentMana >= SkillDatabase.GetSkillManaCost(skill.SkillId)
                && state.CurrentAbility >= SkillDatabase.GetSkillAGCost(skill.SkillId);
        }

        private static float PressPulse(double pressedAt, double now)
        {
            double elapsed = now - pressedAt;
            if (elapsed < 0 || elapsed >= PressFeedbackSeconds)
                return 0f;

            return (float)(1.0 - elapsed / PressFeedbackSeconds);
        }

        private void DrawCenteredLabel(SpriteBatch sb, string text, Vector2 center, float scale, Color color)
        {
            if (_font == null || string.IsNullOrEmpty(text))
                return;

            var size = _font.MeasureString(text) * scale;
            var position = center - size * 0.5f;

            sb.DrawString(_font, text, position + Vector2.One, Color.Black * 0.75f,
                0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            sb.DrawString(_font, text, position, color,
                0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }
    }
}
