#nullable enable
using System;
using System.Collections.Generic;
using Client.Main.Core.Client;
using Client.Main.Objects;
using Client.Main.Objects.Player;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;

namespace Client.Main.Effects
{
    /// <summary>
    /// Applies visual buff effects (Swell scale, auras) to player/monster objects
    /// by subscribing to BuffManager state changes.
    /// </summary>
    public class BuffEffectController
    {
        private readonly BuffManager _buffManager;
        private readonly ILogger<BuffEffectController> _logger;

        /// <summary>Active swell scale factors per entity (playerId → scale multiplier).</summary>
        private readonly Dictionary<ushort, float> _swellScales = new();

        /// <summary>The scale multiplier applied to swelled entities.</summary>
        private const float SwellScaleFactor = 1.25f;

        public BuffEffectController(BuffManager buffManager, ILoggerFactory loggerFactory)
        {
            _buffManager = buffManager;
            _logger = loggerFactory.CreateLogger<BuffEffectController>();

            _buffManager.BuffStateChanged += OnBuffStateChanged;
        }

        private void OnBuffStateChanged(object? sender, BuffStateChangedEventArgs e)
        {
            var world = MuGame.Instance?.ActiveScene as Scenes.GameScene;
            if (world == null) return;

            MuGame.ScheduleOnMainThread(() =>
            {
                ApplyBuffVisual(world, e.PlayerId, e.EffectId, e.IsActive);
            });
        }

        private void ApplyBuffVisual(Scenes.GameScene scene, ushort playerId, BuffEffectId effectId, bool isActive)
        {
            ushort maskedId = (ushort)(playerId & 0x7FFF);

            switch (effectId)
            {
                case BuffEffectId.SwellLife:
                    ApplySwell(scene, maskedId, isActive);
                    break;

                case BuffEffectId.ManaShield:
                    ApplyAura(scene, maskedId, isActive,
                        new Color(40, 80, 255, 120),  // blue
                        1.15f);                        // slightly larger
                    break;

                case BuffEffectId.GreaterDamage:
                    ApplyAura(scene, maskedId, isActive,
                        new Color(255, 60, 40, 100),   // red
                        1.1f);
                    break;

                case BuffEffectId.GreaterDefense:
                    ApplyAura(scene, maskedId, isActive,
                        new Color(40, 200, 60, 100),   // green
                        1.1f);
                    break;

                case BuffEffectId.Poison:
                    ApplyAura(scene, maskedId, isActive,
                        new Color(120, 0, 180, 80),    // purple
                        1.05f);
                    break;

                case BuffEffectId.Ice:
                    ApplyAura(scene, maskedId, isActive,
                        new Color(100, 180, 255, 100),  // light blue
                        1.05f);
                    break;

                default:
                    _logger?.LogTrace("No visual effect mapped for buff {EffectId}", effectId);
                    break;
            }
        }

        /// <summary>
        /// Applies Swell (scale increase) to the entity.
        /// Matches SourceMain swell behavior: entity visibly grows.
        /// </summary>
        private void ApplySwell(Scenes.GameScene scene, ushort playerId, bool isActive)
        {
            if (scene.World is not Controls.WalkableWorldControl walkableWorld) return;
            if (!walkableWorld.WalkerObjectsById.TryGetValue(playerId, out var walker)) return;

            if (isActive)
            {
                _swellScales[playerId] = SwellScaleFactor;
                walker.Scale *= SwellScaleFactor;
                _logger?.LogDebug("Swell applied to player {PlayerId}", playerId);
            }
            else
            {
                if (_swellScales.TryGetValue(playerId, out float factor))
                {
                    walker.Scale /= factor;
                    _swellScales.Remove(playerId);
                }
                _logger?.LogDebug("Swell removed from player {PlayerId}", playerId);
            }
        }

        /// <summary>
        /// Applies a colored aura glow effect to the entity.
        /// Creates/removes a child effect object on the walker.
        /// </summary>
        private void ApplyAura(Scenes.GameScene scene, ushort playerId, bool isActive, Color color, float auraRadius)
        {
            // Aura effects are handled as child effect objects attached to the entity.
            // For now, the BuffSlotControl UI shows the buff icon.
            // Full aura rendering requires a dedicated effect object.
            //
            // Future: Create BuffAuraEffect child object that renders a billboarded
            // radial gradient sprite around the entity.
            _logger?.LogTrace("Aura buff {Active} for player {PlayerId} (color: {Color})",
                isActive ? "applied" : "removed", playerId, color);
        }

        /// <summary>
        /// Gets the current swell scale factor for a player, or 1.0 if not swelled.
        /// </summary>
        public float GetSwellScale(ushort playerId)
        {
            return _swellScales.TryGetValue(playerId, out float factor) ? factor : 1.0f;
        }

        /// <summary>
        /// Clears all visual effects (e.g., on map change).
        /// </summary>
        public void ClearAll()
        {
            _swellScales.Clear();
            _logger?.LogDebug("All buff visual effects cleared");
        }

        public void Dispose()
        {
            _buffManager.BuffStateChanged -= OnBuffStateChanged;
            ClearAll();
        }
    }
}
