#nullable enable
using System;
using MUnique.OpenMU.Network.Packets;

namespace Client.Main.Core.Client
{
    /// <summary>
    /// 劍士連擊的進度追蹤。<b>純粹用於顯示</b> —— 傷害加成是伺服器算的。
    ///
    /// 伺服器的規則在 <c>ComboStateMachine</c>（OpenMU），資料在
    /// <c>config."SkillComboStep"</c>。劍士（Blade Knight / Blade Master）的定義是：
    /// <code>
    /// 第一段：斬 23、旋風 22、刺 20、落石斬 19、上鉤拳 21
    /// 第二段：旋風斬 41、憤怒之錘 42、死亡之刺 43、毀滅之擊 232
    /// 第三段：旋風斬 41、憤怒之錘 42、死亡之刺 43        ← 完成
    /// 全程限時 3 秒
    /// </code>
    ///
    /// 三個容易寫錯的地方，都是照著伺服器的狀態機來的：
    /// <list type="number">
    ///   <item><b>下一段不能用同一個技能</b>。狀態機建轉換時排除了
    ///         <c>RequiredSkill == 自己</c>，所以「旋風斬 → 旋風斬」不算連擊。</item>
    ///   <item><b>接錯技能不會重新開始，而是整個歸零</b>。
    ///         `RegisterSkillAsync` 找不到轉換時退回 Initial 並回傳 false，
    ///         不會把那個技能當成新連擊的第一段。</item>
    ///   <item><b>3 秒是從第一段算起</b>，不是每段各 3 秒。</item>
    /// </list>
    ///
    /// <b>「連擊成功」不由客戶端判定。</b>伺服器成功時會送一個 SkillAnimation，
    /// 技能編號 59（<c>ShowSkillAnimationPlugIn.ComboSkillId</c>）——
    /// 那才是唯一可信的訊號。客戶端只預測「進行到第幾段」，
    /// 因為連擊還需要一個任務獎勵屬性（<c>IsSkillComboAvailable</c>），
    /// 而那個屬性不會下發給客戶端，客戶端無從得知。
    /// </summary>
    public sealed class SkillComboTracker
    {
        /// <summary>伺服器送這個技能編號代表「這一次連擊成立」。</summary>
        public const ushort ComboAchievedSkillId = 59;

        private static readonly ushort[][] BladeKnightSteps =
        {
            new ushort[] { 19, 20, 21, 22, 23 },        // 落石斬、刺、上鉤拳、旋風、斬
            new ushort[] { 41, 42, 43, 232 },           // 旋風斬、憤怒之錘、死亡之刺、毀滅之擊
            new ushort[] { 41, 42, 43 }                 // 完成段
        };

        private const double MaxCompletionSeconds = 3.0;

        /// <summary>連擊成立後，讓最後一段的指示燈多亮一下再收掉。</summary>
        private const double CompletionHoldSeconds = 1.1;

        private int _step;                  // 0 = 尚未開始，1..3
        private ushort _lastSkillId;
        private double _startedAtSeconds;

        /// <summary>本地走到第三段的時間 —— 只用來讓指示燈多亮一下，不代表連擊成立。</summary>
        private double _localFinishedAtSeconds = double.NegativeInfinity;

        /// <summary><b>伺服器確認</b>連擊成立的時間（收到技能 59）。COMBO! 只認這個。</summary>
        private double _achievedAtSeconds = double.NegativeInfinity;

        /// <summary>目前完成到第幾段（0 = 尚未開始）。</summary>
        public int CurrentStep => _step;

        /// <summary>總段數。</summary>
        public int StepCount => BladeKnightSteps.Length;

        /// <summary>剩餘的完成時間（秒）。沒有進行中的連擊時回傳 0。</summary>
        public double RemainingSeconds(double nowSeconds)
            => _step <= 0 ? 0 : Math.Max(0, MaxCompletionSeconds - (nowSeconds - _startedAtSeconds));

        /// <summary>
        /// <b>伺服器確認</b>連擊成立之後經過的秒數，供 UI 閃爍 COMBO!。
        ///
        /// 刻意不用本地的預測 —— 連擊還需要一個任務獎勵屬性
        /// （<c>IsSkillComboAvailable</c>），而那個屬性不會下發給客戶端。
        /// 用本地預測去閃，沒解過任務的玩家會看到一個假的「成立」。
        /// </summary>
        public double SinceAchieved(double nowSeconds) => nowSeconds - _achievedAtSeconds;

        /// <summary>是否要顯示連擊指示。連擊沒進行、也沒剛結束時就不要佔畫面。</summary>
        public bool ShouldDisplay(double nowSeconds)
            => _step > 0
            || SinceAchieved(nowSeconds) < CompletionHoldSeconds
            || nowSeconds - _localFinishedAtSeconds < CompletionHoldSeconds;

        /// <summary>這個職業有沒有連擊。伺服器是往前找上一個職業，劍士這條鏈只有 BK / BM。</summary>
        public static bool IsComboClass(CharacterClassNumber characterClass)
            => characterClass is CharacterClassNumber.BladeKnight or CharacterClassNumber.BladeMaster;

        /// <summary>
        /// 登記一個<b>伺服器已經確認</b>的技能（收到 SkillAnimation / AreaSkillAnimation 時呼叫）。
        ///
        /// 刻意不在「送出封包」時登記 —— 送出去的技能可能被伺服器以射程、安全區等理由丟掉，
        /// 那樣客戶端的段數會跟伺服器對不上。以伺服器回來的動畫為準才會同步。
        /// </summary>
        public void RegisterConfirmedSkill(ushort skillId, double nowSeconds, CharacterClassNumber characterClass)
        {
            if (skillId == ComboAchievedSkillId)
                return;

            // 魔劍士也有 19-23 與 41，但伺服器只給劍士這條鏈連擊定義
            // （Player.DetermineComboDefinition 往前找上一個職業，魔劍士那條找不到）。
            // 不擋的話魔劍士會看到一個永遠不會成立的進度條。
            if (!IsComboClass(characterClass))
            {
                Reset();
                return;
            }

            ushort baseSkill = (ushort)global::Client.Data.BMD.SkillDefinitions.ResolveBaseSkill(skillId);

            // 純增益技伺服器不會登記（TargetedSkillDefaultPlugin 只對 DirectHit 登記），
            // 客戶端也要跳過，否則喝個 buff 就把連擊打斷了。
            if (global::Client.Data.BMD.SkillDefinitions.IsSelfSkill(baseSkill))
                return;

            if (_step > 0 && nowSeconds - _startedAtSeconds > MaxCompletionSeconds)
                Reset();

            int next = _step + 1;
            bool valid = next <= BladeKnightSteps.Length
                && Contains(BladeKnightSteps[next - 1], baseSkill)
                && (_step == 0 || baseSkill != _lastSkillId);

            if (!valid)
            {
                // 伺服器在這裡是「退回 Initial 並回傳 false」，不會拿這個技能當新連擊的第一段。
                Reset();
                return;
            }

            if (_step == 0)
                _startedAtSeconds = nowSeconds;

            _step = next;
            _lastSkillId = baseSkill;

            if (_step >= BladeKnightSteps.Length)
            {
                // 伺服器走到最後一段就立刻回到 Initial。這裡同步歸零，
                // 但先記時間讓指示燈多亮一下 —— 真正的「成立」還是要等技能 59。
                _localFinishedAtSeconds = nowSeconds;
                _step = 0;
                _lastSkillId = 0;
            }
        }

        /// <summary>伺服器確認連擊成立（收到技能 59）。</summary>
        public void NotifyComboAchieved(double nowSeconds)
        {
            _achievedAtSeconds = nowSeconds;
            Reset();
        }

        public void Reset()
        {
            _step = 0;
            _lastSkillId = 0;
            _startedAtSeconds = 0;
        }

        private static bool Contains(ushort[] set, ushort value)
        {
            for (int i = 0; i < set.Length; i++)
            {
                if (set[i] == value)
                    return true;
            }

            return false;
        }
    }
}
