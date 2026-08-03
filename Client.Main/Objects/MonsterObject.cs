using Client.Data.BMD;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Helpers;
using Client.Main.Models;
using Client.Main.Objects.Player;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;
using System.Linq;

namespace Client.Main.Objects
{
    public abstract class MonsterObject : WalkerObject
    {
        // --- Fields ---
        private int _lastActionForIdleSound = -1;
        private bool _isFading = false;
        private float _fadeTimer = 0f;
        private float _fadeDuration = 3.5f; // longer fade for smoother disappearance
        private float _startZ;
        private float _fadeGroundZ;
        private const float SinkBelowGround = 30f; // how deep to sink below terrain surface
        private const float NameplateHeightOffset = 20f;

        private float _healthFraction = 1f;
        private bool _hasHealthFraction;
        private string _cachedDisplayName;

        /// <summary>
        /// Determines whether a blood stain should be spawned when the monster dies.
        /// </summary>
        public bool Blood { get; set; } = true;

        /// <summary>
        /// Last target id received from server animation packets (used for attack effects).
        /// </summary>
        public ushort LastAttackTargetId { get; internal set; }

        /// <summary>
        /// Set of mesh indices that should NOT use blending (equivalent to NoneBlendMesh = true in original code).
        /// </summary>
        public HashSet<int> NoneBlendMeshes { get; set; } = new HashSet<int>();

        /// <summary>
        /// Gets the monster's display name defined by <see cref="NpcInfoAttribute"/>.
        /// </summary>
        public override string DisplayName
        {
            get
            {
                if (!string.IsNullOrEmpty(_cachedDisplayName))
                    return _cachedDisplayName;

                var attr = (NpcInfoAttribute)GetType()
                    .GetCustomAttributes(typeof(NpcInfoAttribute), inherit: false)
                    .FirstOrDefault();
                _cachedDisplayName = attr?.DisplayName ?? base.DisplayName;
                return _cachedDisplayName;
            }
        }

        // --- Constructors ---
        public MonsterObject() : base()
        {
            Interactive = true;
            AnimationSpeed = 6f;
        }

        public void StartDeathFade(float duration = 3.5f)
        {
            if (_isFading) return;

            // Ensure the monster stops moving while the death animation plays
            StopMovement();
            Interactive = false; // prevent dead monsters from blocking selection
            _isFading = true;
            _fadeDuration = Math.Max(1.5f, duration);
            _fadeTimer = 0f;
            _startZ = Position.Z;
            _fadeGroundZ = World?.Terrain?.RequestTerrainHeight(Position.X, Position.Y) ?? _startZ;

            RenderShadow = false;
            var children = Children.GetSnapshotArray();
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i] is ModelObject modelChild)
                    modelChild.RenderShadow = false;
            }

            if (Blood && World != null)
            {
                var stain = new Effects.BloodStainEffect
                {
                    Position = new Vector3(Position.X, Position.Y,
                        World.Terrain.RequestTerrainHeight(Position.X, Position.Y) + Effects.BloodStainEffect.GroundOffset)
                };
                World.Objects.Add(stain);
                _ = stain.Load();
            }
        }

        /// <summary>
        /// Indicates whether the monster is in its death fade stage.
        /// While true the monster should no longer be considered alive.
        /// </summary>
        public bool IsDead => _isFading;

        // --- Public Methods ---
        public override void Update(GameTime gameTime)
        {
            bool wasMoving = IsMoving;

            // Apply fading BEFORE base.Update so buffers/shaders see updated alpha/color this frame
            if (_isFading)
            {
                _fadeTimer += (float)gameTime.ElapsedGameTime.TotalSeconds;
                float p = MathHelper.Clamp(_fadeTimer / _fadeDuration, 0f, 1f);

                // Smooth fade (ease-out)
                float alpha = 1f - p * p; // quadratic ease-out
                Alpha = MathHelper.Clamp(alpha, 0f, 1f);

                // Also darken body color for shader paths that ignore alpha
                byte shade = (byte)(255 * Alpha);
                Color = new Color(shade, shade, shade, (byte)255);
                InvalidateBuffers(MeshDirtyFlags.Material);

                float targetZ = _fadeGroundZ - SinkBelowGround;

                // Start sinking after a short delay for better readability
                const float sinkStart = 0.3f; // start sinking after 30% of fade time
                float sinkP = p <= sinkStart ? 0f : (p - sinkStart) / (1f - sinkStart);
                // Ease-in sink for natural fall
                float sinkEase = sinkP * sinkP;
                float newZ = MathHelper.Lerp(_startZ, targetZ, sinkEase);
                Position = new Vector3(Position.X, Position.Y, newZ);

                if (p >= 1f)
                {
                    _isFading = false;
                    World?.RemoveObject(this);
                    Dispose();
                    return;
                }
            }

            base.Update(gameTime);


            // If the monster just stopped moving
            if (wasMoving && !IsMoving && !IsOneShotPlaying)
            {
                // Transition to idle animation if appropriate
                if (CurrentAction == (int)MonsterActionType.Walk ||
                    CurrentAction == (int)MonsterActionType.Run)
                {
                    PlayAction((byte)MonsterActionType.Stop1);
                }

                // Play idle sound once
                if (CurrentAction == (int)MonsterActionType.Stop1 &&
                    _lastActionForIdleSound != CurrentAction)
                {
                    OnIdle();
                    _lastActionForIdleSound = CurrentAction;
                }
            }
            // If the monster just started moving
            else if (!wasMoving && IsMoving)
            {
                OnStartWalk();
                _lastActionForIdleSound = -1;
            }
            // If still moving, reset idle sound flag
            else if (IsMoving)
            {
                _lastActionForIdleSound = -1;
            }
            // If idle and sound not yet played
            else
            {
                if (!IsOneShotPlaying &&
                    CurrentAction == (int)MonsterActionType.Stop1 &&
                    _lastActionForIdleSound != CurrentAction)
                {
                    OnIdle();
                    _lastActionForIdleSound = CurrentAction;
                }
            }
        }

        public override void DrawAfter(GameTime gameTime)
        {
            base.DrawAfter(gameTime);
            DrawNameplate();
        }

        public override void DrawHoverName()
        {
            // Monster nameplates are drawn persistently; skip hover-only name rendering.
        }

        // --- Protected Virtual Methods for Overriding ---

        /// <summary>
        /// Called when the monster enters idle state.
        /// </summary>
        protected virtual void OnIdle()
        {
            // Base does nothing; override to play idle sound.
        }

        /// <summary>
        /// Called when the monster starts walking.
        /// </summary>
        protected virtual void OnStartWalk()
        {
            // Base does nothing; override to play walk sound.
        }

        /// <summary>
        /// Called when the monster performs an attack.
        /// </summary>
        /// <param name="attackType">Attack variation index.</param>
        public virtual void OnPerformAttack(int attackType = 1)
        {
            _lastActionForIdleSound = -1;
            // Override to play attack sound.
        }

        /// <summary>
        /// Starts a short bone-to-target magic attack effect using the target id
        /// captured from the server animation packet.
        /// </summary>
        protected void SpawnMagicAttackEffect(
            int[] sourceBones,
            int attackType,
            Vector3 sourceOffset = default)
        {
            if (World is not WalkableWorldControl world ||
                Status != GameControlStatus.Ready ||
                Model == null)
                return;

            int requiredAction = attackType == 2
                ? (int)MonsterActionType.Attack2
                : (int)MonsterActionType.Attack1;
            var effect = new Effects.MonsterMagicAttackEffect(
                this,
                sourceBones,
                LastAttackTargetId,
                requiredAction,
                sourceOffset);
            world.Objects.Add(effect);
            _ = effect.Load();
        }

        /// <summary>
        /// Called when the monster receives damage.
        /// </summary>
        public virtual void OnReceiveDamage()
        {
            _lastActionForIdleSound = -1;
            // Override to play hit sound.
        }

        /// <summary>
        /// Spawns the standard blood burst used by the original client when this monster is hit.
        /// </summary>
        public void SpawnHitEffect()
        {
            if (World == null || Status != GameControlStatus.Ready)
                return;

            var effect = new Effects.MonsterHitEffect(Position, Angle);
            World.Objects.Add(effect);
            _ = effect.Load();
        }

        /// <summary>
        /// Minimal health/shield update shim to accept server-provided fractions.
        /// Triggers damage reactions and death fade when health reaches zero.
        /// </summary>
        public void UpdateHealthFractions(float? healthFraction, float? shieldFraction, uint? healthDamage = null, uint? shieldDamage = null)
        {
            if (healthFraction.HasValue)
            {
                float hf = MathHelper.Clamp(healthFraction.Value, 0f, 1f);
                _healthFraction = hf;
                _hasHealthFraction = true;
                if (hf <= 0f)
                {
                    StartDeathFade();
                }
                else
                {
                    OnReceiveDamage();
                }
            }

            if (shieldFraction.HasValue)
            {
                OnReceiveDamage();
            }
        }

        private void DrawNameplate()
        {
            if (IsDead || !Visible)
                return;

            // Show monster labels only when hovered or when HP bar is available.
            // This matches expected clutter reduction while still keeping combat readability.
            bool showByHover = Constants.SHOW_NAMES_ON_HOVER && IsMouseHover;
            bool showByHealth = _hasHealthFraction;
            if (!showByHover && !showByHealth)
                return;

            var font = GraphicsManager.Instance.Font;
            if (font == null)
                return;

            string name = DisplayName;
            if (string.IsNullOrWhiteSpace(name))
                return;

            if (!OverheadNameplateRenderer.TryProject(BoundingBoxWorld, NameplateHeightOffset, out var screen))
                return;

            float? hp = _hasHealthFraction ? _healthFraction : null;
            OverheadNameplateRenderer.EnqueueNameplate(font, screen, name, hp, Constants.RENDER_SCALE);
        }

        /// <summary>
        /// Called when the monster’s death animation starts.
        /// </summary>
        public virtual void OnDeathAnimationStart()
        {
            _lastActionForIdleSound = -1;
            // Override to play death sound.
        }

        // --- Helper method for setting speed ---
        protected bool IsValidAction(int actionIndex)
        {
            return Model != null
                && Model.Actions != null
                && actionIndex >= 0
                && actionIndex < Model.Actions.Length
                && Model.Actions[actionIndex] != null;
        }

        protected void SetActionSpeed(MonsterActionType actionType, float speed)
        {
            int actionIndex = (int)actionType;
            if (IsValidAction(actionIndex))
            {
                var action = Model.Actions[actionIndex];
                action.PlaySpeed = speed * 2;
            }
            else
            {
                _logger?.LogDebug($"Warning: Cannot set PlaySpeed for action {(MonsterActionType)actionType} ({actionIndex}). Action does not exist or is null.");
            }
        }

        protected static BMDTextureAction[] BuildActionArray(
            BMD srcModel,
            int dstCount,
            IReadOnlyDictionary<int, int> map)
        {
            var actions = new BMDTextureAction[dstCount];
            foreach (var kv in map)
            {
                int dst = kv.Key;
                int src = kv.Value;
                if (dst < 0 || dst >= actions.Length ||
                    srcModel.Actions == null || src < 0 || src >= srcModel.Actions.Length)
                    continue;

                var srcAction = srcModel.Actions[src];
                if (srcAction == null)
                    continue;

                // Clone the action to avoid sharing PlaySpeed with player
                // Player's dynamic attack speed modifiers should not affect monsters
                // Use base PlaySpeed values from PlayerObject.InitializeActionSpeeds()
                float basePlaySpeed = GetPlayerActionBaseSpeed(src);
                actions[dst] = new BMDTextureAction
                {
                    NumAnimationKeys = srcAction.NumAnimationKeys,
                    LockPositions = srcAction.LockPositions,
                    Positions = srcAction.Positions, // Array reference is fine, positions don't change
                    PlaySpeed = basePlaySpeed
                };
            }
            return actions;
        }

        /// <summary>
        /// Returns base PlaySpeed for player actions used by monsters.
        /// Values mirror PlayerObject.InitializeActionSpeeds() and base attack speeds.
        /// </summary>
        private static float GetPlayerActionBaseSpeed(int playerActionIndex)
        {
            var action = (PlayerAction)playerActionIndex;

            return action switch
            {
                // Stop animations
                PlayerAction.PlayerStopMale or PlayerAction.PlayerStopFemale => 0.28f,
                PlayerAction.PlayerStopSword => 0.26f,
                PlayerAction.PlayerStopTwoHandSword => 0.24f,
                PlayerAction.PlayerStopSpear => 0.24f,
                PlayerAction.PlayerStopScythe => 0.28f,
                PlayerAction.PlayerStopBow => 0.22f,
                PlayerAction.PlayerStopCrossbow => 0.22f,

                // Walk animations
                PlayerAction.PlayerWalkMale or PlayerAction.PlayerWalkFemale or
                PlayerAction.PlayerWalkSword or PlayerAction.PlayerWalkTwoHandSword or
                PlayerAction.PlayerWalkSpear or PlayerAction.PlayerWalkScythe or
                PlayerAction.PlayerWalkBow or
                PlayerAction.PlayerWalkCrossbow => 0.38f,

                // Run animations
                PlayerAction.PlayerRun or PlayerAction.PlayerRunSword or
                PlayerAction.PlayerRunSpear => 0.34f,

                // Attack animations: base 0.25f (without player's attack speed bonus)
                PlayerAction.PlayerAttackFist => 0.25f,
                PlayerAction.PlayerAttackSwordRight1 or PlayerAction.PlayerAttackSwordRight2 or
                PlayerAction.PlayerAttackSwordLeft1 or PlayerAction.PlayerAttackSwordLeft2 or
                PlayerAction.PlayerAttackTwoHandSword1 or PlayerAction.PlayerAttackTwoHandSword2 or
                PlayerAction.PlayerAttackTwoHandSword3 or PlayerAction.PlayerAttackSpear1 or
                PlayerAction.PlayerAttackScythe1 or PlayerAction.PlayerAttackScythe2 or
                PlayerAction.PlayerAttackScythe3 => 0.25f,
                PlayerAction.PlayerAttackBow or PlayerAction.PlayerAttackCrossbow => 0.30f,

                // Two-handed staff attacks
                PlayerAction.PlayerSkillWeapon1 or PlayerAction.PlayerSkillWeapon2 => 0.29f,

                // Shock
                PlayerAction.PlayerShock => 0.40f,

                // Defense
                PlayerAction.PlayerDefense1 => 0.32f,

                // Die
                PlayerAction.PlayerDie1 or PlayerAction.PlayerDie2 => 0.45f,

                // Appear/ComeUp
                PlayerAction.PlayerComeUp => 0.40f,

                // Default fallback for any other action
                _ => 0.30f
            };
        }

        protected static BMDTextureBone[] BuildBoneArray(
            BMD srcModel,
            int actionCount,
            IReadOnlyDictionary<int, int> map)
        {
            var bones = new BMDTextureBone[srcModel.Bones.Length];

            for (int i = 0; i < bones.Length; i++)
            {
                var src = srcModel.Bones[i];
                if (ReferenceEquals(src, BMDTextureBone.Dummy))
                {
                    bones[i] = BMDTextureBone.Dummy;
                    continue;
                }

                var matrices = new BMDBoneMatrix[actionCount];
                foreach (var kv in map)
                {
                    int dst = kv.Key;
                    int srcIdx = kv.Value;
                    if (dst >= 0 && dst < matrices.Length &&
                        src.Matrixes != null && srcIdx >= 0 && srcIdx < src.Matrixes.Length)
                        matrices[dst] = src.Matrixes[srcIdx];
                }

                bones[i] = new BMDTextureBone
                {
                    Name = src.Name,
                    Parent = src.Parent,
                    Matrixes = matrices
                };
            }

            return bones;
        }

        protected new ILogger _logger = ModelObject.AppLoggerFactory?.CreateLogger<MonsterObject>();

        /// <summary>
        /// Override to support NoneBlendMeshes functionality from original code.
        /// </summary>
        protected override bool IsBlendMesh(int mesh)
        {
            // If mesh is in NoneBlendMeshes set, it should NOT use blending
            if (NoneBlendMeshes.Contains(mesh))
                return false;

            // Otherwise use the base implementation
            return base.IsBlendMesh(mesh);
        }
    }
}
