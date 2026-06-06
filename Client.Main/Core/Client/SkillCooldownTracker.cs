#nullable enable
using System.Collections.Generic;
using Client.Main.Core.Utilities;

namespace Client.Main.Core.Client
{
    /// <summary>
    /// Static cooldown tracker shared between GameSceneSkillController (writes) and SkillSlotControl (reads).
    /// Tracks per-skill next-allowed timestamps using game time.
    /// </summary>
    public static class SkillCooldownTracker
    {
        /// <summary>SkillId → game-time-ms when the skill can be used next.</summary>
        private static readonly Dictionary<ushort, double> _nextAllowedMs = new();

        /// <summary>
        /// Attempts to consume the cooldown for a skill.
        /// Returns true if the skill was consumed (cooldown set), false if still on cooldown.
        /// </summary>
        public static bool TryConsume(ushort skillId, double nowGameTimeMs)
        {
            int delayMs = SkillDatabase.GetSkillCooldown(skillId);
            if (delayMs <= 0)
                return true;

            if (_nextAllowedMs.TryGetValue(skillId, out double nextAllowed) && nowGameTimeMs < nextAllowed)
                return false;

            _nextAllowedMs[skillId] = nowGameTimeMs + delayMs;
            return true;
        }

        /// <summary>
        /// Gets cooldown ratio in [0..1] where 0 = ready, 1 = full cooldown.
        /// </summary>
        public static float GetCooldownRatio(ushort skillId, double nowGameTimeMs)
        {
            int delayMs = SkillDatabase.GetSkillCooldown(skillId);
            if (delayMs <= 0)
                return 0f;

            if (!_nextAllowedMs.TryGetValue(skillId, out double nextAllowed))
                return 0f;

            double remaining = nextAllowed - nowGameTimeMs;
            if (remaining <= 0)
                return 0f;

            return (float)(remaining / delayMs);
        }

        /// <summary>
        /// Gets remaining cooldown time in milliseconds.
        /// </summary>
        public static int GetRemainingMs(ushort skillId, double nowGameTimeMs)
        {
            if (!_nextAllowedMs.TryGetValue(skillId, out double nextAllowed))
                return 0;

            double remaining = nextAllowed - nowGameTimeMs;
            return remaining > 0 ? (int)remaining : 0;
        }

        /// <summary>
        /// Resets all cooldowns (e.g., on map change or death).
        /// </summary>
        public static void ResetAll()
        {
            _nextAllowedMs.Clear();
        }
    }
}
