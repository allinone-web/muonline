#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Client.Main.Core.Client
{
    /// <summary>
    /// Tracks Dark Knight / Blade Knight combo skill chain.
    /// SourceMain combo: Weapon Skill → Combo Skill (GIANTSWING or DRAGON_KICK) → Finisher (DRAGON_LOWER).
    /// </summary>
    public class MonkSystem
    {
        private readonly ILogger<MonkSystem> _logger;

        public enum ComboStep
        {
            Idle = 0,
            WeaponSkillUsed = 1,
            ComboSkillUsed = 2,
        }

        private ComboStep _currentStep = ComboStep.Idle;
        private DateTime _lastSkillTime = DateTime.MinValue;
        private static readonly TimeSpan ComboTimeout = TimeSpan.FromSeconds(3);

        /// <summary>Fired when combo is completed (3-skill chain).</summary>
        public event Action<int>? ComboCompleted;

        /// <summary>Current combo multiplier (1.0 = no combo).</summary>
        public float ComboDamageMultiplier { get; private set; } = 1.0f;

        public MonkSystem(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<MonkSystem>();
        }

        /// <summary>
        /// Registers a skill usage and advances the combo chain.
        /// Returns true if the combo chain completed (3rd skill landed).
        /// </summary>
        public bool RegisterSkillUse(ushort skillId, bool isWeaponSkill)
        {
            var now = DateTime.UtcNow;

            // Reset if timeout
            if (_currentStep != ComboStep.Idle && (now - _lastSkillTime) > ComboTimeout)
            {
                ResetCombo();
            }

            _lastSkillTime = now;

            if (isWeaponSkill)
            {
                // Step 1: Weapon skill (e.g., Cyclone, Twisting Slash)
                _currentStep = ComboStep.WeaponSkillUsed;
                ComboDamageMultiplier = 1.0f;
                _logger?.LogDebug("Combo step 1: Weapon skill {SkillId}", skillId);
                return false;
            }

            if (_currentStep == ComboStep.WeaponSkillUsed)
            {
                // Step 2: Combo skill (Giant Swing or Dragon Kick)
                if (IsComboSkill(skillId))
                {
                    _currentStep = ComboStep.ComboSkillUsed;
                    ComboDamageMultiplier = 1.2f;
                    _logger?.LogDebug("Combo step 2: Combo skill {SkillId}", skillId);
                    return false;
                }
            }

            if (_currentStep == ComboStep.ComboSkillUsed)
            {
                // Step 3: Finisher — any skill after combo skill completes the chain
                ComboDamageMultiplier = 1.5f;
                _logger?.LogInformation("COMBO COMPLETE! 3-skill chain finished. Damage ×1.5");
                ComboCompleted?.Invoke(3);
                ResetCombo();
                return true;
            }

            // Invalid chain — reset
            ResetCombo();
            return false;
        }

        /// <summary>
        /// SourceMain combo skills: AT_SKILL_GIANTSWING, AT_SKILL_DRAGON_KICK.
        /// These trigger the 2nd step of the combo chain.
        /// </summary>
        private static bool IsComboSkill(ushort skillId)
        {
            // SourceMain IDs (approximate mapping):
            // AT_SKILL_GIANTSWING  = 229
            // AT_SKILL_DRAGON_KICK = 230
            return skillId is 229 or 230;
        }

        public void ResetCombo()
        {
            _currentStep = ComboStep.Idle;
            ComboDamageMultiplier = 1.0f;
        }

        public ComboStep GetCurrentStep() => _currentStep;
    }
}
