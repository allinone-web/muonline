#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Client.Main.Core.Client
{
    /// <summary>
    /// Buff effect identifiers matching SourceMain MagicEffect packet effect IDs.
    /// </summary>
    public enum BuffEffectId : byte
    {
        // Attack/Defense buffs
        GreaterDamage = 0,
        GreaterDefense = 1,
        ManaShield = 2,
        ElfSoldier = 3,
        SwellLife = 4,
        CriticalDamage = 5,
        HealOverTime = 6,

        // Debuffs
        Poison = 8,
        Ice = 9,
        Slow = 10,
        Weaken = 11,

        // Status effects
        Invisible = 12,
        Invincible = 13,
        Berserk = 14,
        Reflection = 15,
        Transform = 16,

        // Elf buffs
        ElfAttack = 20,
        ElfDefense = 21,
        ElfHeal = 22,
    }

    /// <summary>
    /// Event args for buff activation/deactivation on a specific player/monster.
    /// </summary>
    public class BuffStateChangedEventArgs : EventArgs
    {
        public ushort PlayerId { get; init; }
        public BuffEffectId EffectId { get; init; }
        public bool IsActive { get; init; }
    }

    /// <summary>
    /// Central buff manager — handles buff state and fires events for visual effects.
    /// Integrates with CharacterDataHandler for MagicEffectStatus packets.
    /// </summary>
    public class BuffManager
    {
        private readonly ILogger<BuffManager> _logger;

        /// <summary>Per-entity buff state: (PlayerId, EffectId) → IsActive.</summary>
        private readonly Dictionary<(ushort PlayerId, BuffEffectId EffectId), ActiveBuffState> _states = new();

        /// <summary>Fired when any entity's buff state changes. Used by visual effect controllers.</summary>
        public event EventHandler<BuffStateChangedEventArgs>? BuffStateChanged;

        public BuffManager(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<BuffManager>();
        }

        /// <summary>
        /// Processes a MagicEffectStatus from the server.
        /// </summary>
        public void ProcessMagicEffectStatus(ushort playerId, byte effectId, bool isActive)
        {
            var key = (playerId, (BuffEffectId)effectId);

            if (isActive)
            {
                if (!_states.TryGetValue(key, out var state))
                {
                    state = new ActiveBuffState();
                    _states[key] = state;
                }
                state.ActivatedAt = DateTime.UtcNow;
                state.IsActive = true;
                state.RawEffectId = effectId;
                _logger?.LogDebug("Buff activated: Player={PlayerId}, Effect={EffectId}", (playerId & 0x7FFF), effectId);
            }
            else
            {
                if (_states.TryGetValue(key, out var state))
                {
                    state.IsActive = false;
                }
                _logger?.LogDebug("Buff deactivated: Player={PlayerId}, Effect={EffectId}", (playerId & 0x7FFF), effectId);
            }

            BuffStateChanged?.Invoke(this, new BuffStateChangedEventArgs
            {
                PlayerId = playerId,
                EffectId = (BuffEffectId)effectId,
                IsActive = isActive
            });
        }

        /// <summary>
        /// Checks if a player has a specific buff active.
        /// </summary>
        public bool HasBuff(ushort playerId, BuffEffectId effectId)
        {
            return _states.TryGetValue((playerId, effectId), out var s) && s.IsActive;
        }

        /// <summary>
        /// Gets all active buffs for a player.
        /// </summary>
        public IEnumerable<BuffEffectId> GetActiveBuffs(ushort playerId)
        {
            foreach (var kv in _states)
            {
                if (kv.Key.PlayerId == playerId && kv.Value.IsActive)
                    yield return kv.Key.Item2;
            }
        }

        /// <summary>
        /// Clears all buffs for a player (e.g., on death or disconnect).
        /// </summary>
        public void ClearPlayerBuffs(ushort playerId)
        {
            var toRemove = new List<(ushort, BuffEffectId)>();
            foreach (var kv in _states)
            {
                if (kv.Key.PlayerId == playerId)
                    toRemove.Add(kv.Key);
            }

            foreach (var key in toRemove)
            {
                _states[key].IsActive = false;
                BuffStateChanged?.Invoke(this, new BuffStateChangedEventArgs
                {
                    PlayerId = playerId,
                    EffectId = key.Item2,
                    IsActive = false
                });
            }
        }

        /// <summary>
        /// Maps buff effect ID to descriptive name.
        /// </summary>
        public static string GetBuffName(BuffEffectId effectId) => effectId switch
        {
            BuffEffectId.GreaterDamage => "Greater Damage",
            BuffEffectId.GreaterDefense => "Greater Defense",
            BuffEffectId.ManaShield => "Mana Shield",
            BuffEffectId.ElfSoldier => "Elf Guardian",
            BuffEffectId.SwellLife => "Swell Life",
            BuffEffectId.CriticalDamage => "Critical Damage",
            BuffEffectId.HealOverTime => "Heal",
            BuffEffectId.Poison => "Poison",
            BuffEffectId.Ice => "Ice",
            BuffEffectId.Slow => "Slow",
            BuffEffectId.Weaken => "Weaken",
            BuffEffectId.Invisible => "Invisible",
            BuffEffectId.Invincible => "Invincible",
            BuffEffectId.Berserk => "Berserk",
            BuffEffectId.Reflection => "Reflection",
            BuffEffectId.Transform => "Transform",
            BuffEffectId.ElfAttack => "Elf Attack Buff",
            BuffEffectId.ElfDefense => "Elf Defense Buff",
            BuffEffectId.ElfHeal => "Elf Heal",
            _ => $"Unknown Buff ({effectId})"
        };

        private class ActiveBuffState
        {
            public bool IsActive;
            public DateTime ActivatedAt;
            public byte RawEffectId;
        }
    }
}
