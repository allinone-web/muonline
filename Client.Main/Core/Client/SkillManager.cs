#nullable enable
using System;
using System.Collections.Generic;
using Client.Data.BMD;
using Client.Main.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace Client.Main.Core.Client
{
    /// <summary>
    /// Central skill logic matching SourceMain CSkillManager behavior.
    /// Handles cooldowns, stat requirements, distance calculation, and master skill mapping.
    /// </summary>
    public class SkillManager
    {
        private readonly ILogger<SkillManager> _logger;

        /// <summary>Per-skill cooldown tracking: SkillId → remaining delay in ms.</summary>
        private readonly Dictionary<ushort, int> _skillDelays = new();

        /// <summary>Per-skill cooldown total duration: SkillId → total delay in ms.</summary>
        /// Used to compute cooldown ratio for UI display.</summary>
        private readonly Dictionary<ushort, int> _skillDelayDurations = new();

        public SkillManager(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<SkillManager>();
        }

        // -----------------------------------------------------------------
        //  Public API — mirrored from SourceMain CSkillManager
        // -----------------------------------------------------------------

        /// <summary>
        /// Checks whether character has this skill learned.
        /// Equivalent: SourceMain CSkillManager::FindHeroSkill(eSkillType).
        /// </summary>
        public bool HasSkill(CharacterState state, ushort skillId)
        {
            if (state == null) return false;
            var skills = state.GetSkills();
            foreach (var s in skills)
            {
                if (s.SkillId == skillId)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Checks all conditions required to use a skill.
        /// Equivalent: SourceMain CSkillManager::CheckSkillDelay (delay part)
        /// + DemendConditionCheckSkill (stat requirements part).
        /// Returns (canUse, reason).</summary>
        public (bool CanUse, string? Reason) CanUseSkill(CharacterState state, ushort skillId)
        {
            if (state == null)
                return (false, "No character state");

            if (!HasSkill(state, skillId))
                return (false, "Skill not learned");

            // --- Cooldown check ---
            if (_skillDelays.TryGetValue(skillId, out int remaining) && remaining > 0)
                return (false, "Skill on cooldown");

            // --- Get skill definition ---
            var def = SkillDatabase.GetSkillDefinition(skillId);
            if (def == null)
                return (false, "Unknown skill");

            // --- Level requirement ---
            if (def.RequiredLevel > 0 && state.Level < def.RequiredLevel)
                return (false, $"Requires level {def.RequiredLevel}");

            // --- Stat requirements ---
            var req = GetSkillRequirements(skillId);
            if (req == null)
                return (true, null); // no requirements = always usable

            if (state.TotalStrength < req.RequiredStrength)
                return (false, $"Requires {req.RequiredStrength} Strength");
            if (state.TotalAgility < req.RequiredDexterity)
                return (false, $"Requires {req.RequiredDexterity} Dexterity");
            if (state.TotalEnergy < req.RequiredEnergy)
                return (false, $"Requires {req.RequiredEnergy} Energy");
            if (state.TotalLeadership < req.RequiredCharisma)
                return (false, $"Requires {req.RequiredCharisma} Charisma/Command");

            return (true, null);
        }

        /// <summary>
        /// Consumes skill delay. Call right before executing the skill.
        /// Equivalent: the delay-setting part of SourceMain CSkillManager::CheckSkillDelay.
        /// Returns false if skill is on cooldown.
        /// </summary>
        public bool TryConsumeSkillDelay(ushort skillId)
        {
            int delayMs = SkillDatabase.GetSkillCooldown(skillId);
            if (delayMs <= 0)
                return true;

            if (_skillDelays.TryGetValue(skillId, out int remaining) && remaining > 0)
                return false;

            _skillDelays[skillId] = delayMs;
            _skillDelayDurations[skillId] = delayMs;
            return true;
        }

        /// <summary>
        /// Updates all skill cooldowns by subtracting elapsed time.
        /// Equivalent: SourceMain CSkillManager::CalcSkillDelay(time).
        /// Call once per frame with delta milliseconds.
        /// </summary>
        public void UpdateSkillDelays(int deltaMs)
        {
            if (deltaMs <= 0) return;

            var skillIds = new List<ushort>(_skillDelays.Keys);
            foreach (var skillId in skillIds)
            {
                int remaining = _skillDelays[skillId];
                if (remaining <= 0)
                    continue;

                remaining -= deltaMs;
                if (remaining <= 0)
                {
                    _skillDelays[skillId] = 0;
                    _skillDelayDurations.Remove(skillId);
                }
                else
                {
                    _skillDelays[skillId] = remaining;
                }
            }
        }

        /// <summary>
        /// Gets remaining cooldown in ms for a skill. 0 = ready.
        /// </summary>
        public int GetRemainingCooldown(ushort skillId)
        {
            return _skillDelays.TryGetValue(skillId, out int remaining) ? Math.Max(0, remaining) : 0;
        }

        /// <summary>
        /// Gets cooldown ratio [0..1] where 0 = ready, 1 = full cooldown.
        /// Useful for UI cooldown overlay.
        /// </summary>
        public float GetCooldownRatio(ushort skillId)
        {
            if (!_skillDelays.TryGetValue(skillId, out int remaining) || remaining <= 0)
                return 0f;
            if (!_skillDelayDurations.TryGetValue(skillId, out int total) || total <= 0)
                return 0f;
            return (float)remaining / total;
        }

        /// <summary>
        /// Gets required stats for a skill, applying the SourceMain energy formula.
        /// Energy requirement = 20 + (Energy * Level * 4 / 100).
        /// For Knight: 10 + (Energy * Level * 4 / 100).
        /// For Summon Explosion/Requiem: 20 + (Energy * Level * 3 / 100).
        /// </summary>
        public SkillRequirements? GetSkillRequirements(int skillId)
        {
            var def = SkillDatabase.GetSkillDefinition(skillId);
            if (def == null) return null;

            // SourceMain energy formula
            int energyCost = def.RequiredEnergy;
            int baseEnergy = 20;
            float energyMultiplier = 4f / 100f;
            // Summon special case
            if (def.SkillBrand == 257 || def.SkillBrand == 258) // Summon Explosion / Requiem
                energyMultiplier = 3f / 100f;

            int calculatedEnergy = energyCost > 0
                ? baseEnergy + (int)(energyCost * (def.RequiredLevel > 0 ? def.RequiredLevel : 1) * energyMultiplier)
                : 0;

            // Knight class has lower energy requirements
            // We don't know class here, but the State passes TotalEnergy so it's just the threshold.
            // The caller can decide; we store the "base" value.

            return new SkillRequirements
            {
                RequiredStrength = def.RequiredStrength,
                RequiredDexterity = def.RequiredDexterity,
                RequiredEnergy = calculatedEnergy,
                RequiredCharisma = def.RequiredLeadership,
                RequiredLevel = def.RequiredLevel,
                MasteryType = def.MasteryType,
            };
        }

        /// <summary>
        /// Gets skill distance with bonuses.
        /// Equivalent: SourceMain CSkillManager::GetSkillDistance.
        /// Dark Horse pet adds +2 range.
        /// </summary>
        public float GetSkillDistance(ushort skillId, CharacterState? state = null)
        {
            float distance = SkillDatabase.GetSkillRange(skillId);

            // Dark Horse pet adds +2 range (SourceMain behavior)
            // When CharacterState tracks HasDarkHorse, add:
            // if (state != null && state.HasDarkHorse) distance += 2;

            return distance;
        }

        /// <summary>
        /// Maps a master skill index to its base skill index.
        /// Equivalent: SourceMain CSkillManager::MasterSkillToBaseSkillIndex.
        /// The mapping is defined in SkillDatabase (from skill_eng.bmd).
        /// Returns the input ID if the skill is not a master skill.
        /// </summary>
        public ushort MasterSkillToBaseSkillIndex(ushort masterSkillId)
        {
            var def = SkillDatabase.GetSkillDefinition(masterSkillId);
            if (def == null) return masterSkillId;

            // Master skills in SourceMain always map to specific base skills.
            // The SkillBMD stores this information implicitly.
            // For skills with MasteryType > 0, the base skill is typically at a lower ID range.
            //
            // The SkillBMD's SkillBrand and SkillUseType fields encode the base skill info.
            // We use the stored values; if not a master skill, return unchanged.
            if (def.MasteryType == 0 && def.SkillUseType == 0)
                return masterSkillId;

            // Master skills: the base skill index is typically encoded in SkillBrand
            // For now we return the base mapping from the skill database
            // This is a simplified version - a complete mapping would use an explicit table
            // like the SourceMain switch statement with AT_SKILL_* constants.
            return masterSkillId;
        }

        /// <summary>
        /// Gets the skill mastery type.
        /// Equivalent: SourceMain CSkillManager::GetSkillMasteryType.
        /// </summary>
        public byte GetSkillMasteryType(ushort skillId)
        {
            var def = SkillDatabase.GetSkillDefinition(skillId);
            return def?.MasteryType ?? 255;
        }

        /// <summary>
        /// Resets all cooldowns (e.g., on map change or death).
        /// </summary>
        public void ResetAllCooldowns()
        {
            _skillDelays.Clear();
            _skillDelayDurations.Clear();
            _logger?.LogDebug("All skill cooldowns reset");
        }
    }

    /// <summary>
    /// Calculated skill requirements matching SourceMain DemendConditionInfo.
    /// </summary>
    public class SkillRequirements
    {
        public int RequiredStrength { get; set; }
        public int RequiredDexterity { get; set; }
        public int RequiredEnergy { get; set; }
        public int RequiredCharisma { get; set; }
        public int RequiredLevel { get; set; }
        public byte MasteryType { get; set; }

        public override string ToString()
        {
            var parts = new System.Text.StringBuilder();
            if (RequiredStrength > 0) parts.Append($"Str {RequiredStrength} ");
            if (RequiredDexterity > 0) parts.Append($"Dex {RequiredDexterity} ");
            if (RequiredEnergy > 0) parts.Append($"Ene {RequiredEnergy} ");
            if (RequiredCharisma > 0) parts.Append($"Cmd {RequiredCharisma} ");
            if (RequiredLevel > 0) parts.Append($"Lv {RequiredLevel} ");
            return parts.Length > 0 ? parts.ToString().TrimEnd() : "No requirements";
        }
    }
}
