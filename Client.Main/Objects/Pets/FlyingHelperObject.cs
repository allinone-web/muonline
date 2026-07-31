#nullable enable
using Client.Data.BMD;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Core.Utilities;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects.Player;
using Client.Main.Scenes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Client.Main.Objects.Pets
{
    public enum FlyingHelperKind
    {
        None = -1,
        GuardianAngel = 0,
        Imp = 1
    }

    /// <summary>
    /// Guardian Angel flies independently around its owner. Imp keeps its own animated BMD pose,
    /// but follows the player's right shoulder instead of using the free-flight simulation.
    /// </summary>
    public sealed class FlyingHelperObject : ModelObject
    {
        private const string GuardianModelPath = "Player/Helper01.bmd";
        private const string ImpModelPath = "Player/Helper02.bmd";
        private const string SparkTexturePath = "Effect/Spark01.jpg";
        private const string LightTexturePath = "Effect/flare01.jpg";

        private const float LegacyStepSeconds = 1f / 25f;
        // ModelObject advances BMD frames in real-time units. The previous 12.5 fps port was
        // visibly 2-3x faster than the classic helper wing cycle, so keep the helper animation
        // at a deliberately slower 5 frames per second.
        private const float HelperWingAnimationSpeed = 5f;
        private const float GuardianWorldScale = 0.65f;
        private const float GuardianCharacterSceneScale = 0.78f;
        private const float ImpWorldScale = 0.5f;
        private const float ImpCharacterSceneScale = 0.6f;
        private const int MaxLegacyStepsPerFrame = 5;
        private const float FlyRange = 150f;
        private const int UnresolvedBoneIndex = int.MinValue;
        private const int MaxSparks = 128;
        private const int MaxBillboardQuads = MaxSparks + 1;

        private readonly Random _random = new();
        private readonly SemaphoreSlim _modelApplyGate = new(1, 1);
        private readonly SparkParticle[] _sparks = new SparkParticle[MaxSparks];
        private readonly VertexPositionColorTexture[] _billboardVertices =
            new VertexPositionColorTexture[MaxBillboardQuads * 4];
        private readonly short[] _billboardIndices = new short[MaxBillboardQuads * 6];

        private FlyingHelperKind _kind = FlyingHelperKind.None;
        private int _modelRequestVersion;
        private bool _spawnInitialized;
        private bool _modelContentReady;
        private bool _hasPendingKind;
        private FlyingHelperKind _pendingKind;
        private Vector3 _previousSimulationPosition;
        private Vector3 _simulationPosition;
        private float _simulationYaw;
        private float _localSpeed;
        private float _verticalSpeed;
        private float _legacyAccumulator;
        private int _impShoulderBoneIndex = UnresolvedBoneIndex;

        // Offsets applied in world space: horizontal X/Y pushes the helper outward from the
        // body axis onto the shoulder, and world Z (height) lifts it onto the shoulder line.
        private const float ImpShoulderOutwardOffset = 15f;
        private const float ImpShoulderHeightOffset = 12f;

        private BasicEffect? _billboardEffect;
        private Texture2D? _sparkTexture;
        private Texture2D? _lightTexture;
        private bool _ownsSparkTexture;
        private bool _ownsLightTexture;

        public FlyingHelperKind Kind => _kind;

        protected override bool RequiresPerFrameAnimation => true;
        protected override bool PreserveBlendMeshesInLowQuality => true;
        // Helper01/02 are legacy flying-effect models. The inventory preview renders them
        // correctly with CPU geometry and culling disabled, while the generic world GPU/culling
        // path can reject their entire mesh. Keep these two models on the compatibility path.
        protected override bool AllowGpuSkinning => false;
        protected override bool AllowDynamicLightingShader => false;
        protected override bool ForceTwoSidedMeshes => true;

        public FlyingHelperObject()
        {
            Hidden = true;
            RenderShadow = false;
            IsTransparent = true;
            AffectedByTransparency = true;
            BlendState = BlendState.AlphaBlend;
            BlendMeshState = Blendings.OneOneAdditive;
            DepthState = DepthStencilState.DepthRead;
            BlendMesh = -1;
            Alpha = 0f;
            Light = new Vector3(3f, 3f, 3f);
            LightEnabled = true;
            Scale = ImpWorldScale;
            CurrentAction = 0;
            AnimationSpeed = HelperWingAnimationSpeed;
            ContinuousAnimation = true;
            LinkParentAnimation = false;
            ParentBoneLink = -1;
            BoundingBoxLocal = new BoundingBox(
                new Vector3(-80f, -80f, -80f),
                new Vector3(80f, 80f, 120f));

            BuildStaticIndices();
        }

        public override async Task Load()
        {
            await base.Load();

            // ModelObject.LoadContent() treats a null model as a valid ready state. Equipment
            // updates can arrive while the outer WorldObject.Load() is still in progress, so the
            // helper kind must be applied only after the complete child lifecycle has finished.
            if (Status == GameControlStatus.Ready && _hasPendingKind)
            {
                FlyingHelperKind pendingKind = _pendingKind;
                _hasPendingKind = false;
                await SetKindAsync(pendingKind);
            }
        }

        public async Task SetKindAsync(FlyingHelperKind kind)
        {
            int requestVersion = Interlocked.Increment(ref _modelRequestVersion);

            if (IsDisposeRequested || Status == GameControlStatus.Disposed)
                return;

            // Parent and child content loads run concurrently. Defer equipment changes until the
            // complete WorldObject.Load() has finished, not merely until a null-model content pass
            // temporarily reports Ready.
            if ((Status is GameControlStatus.NonInitialized or GameControlStatus.Initializing) ||
                IsLoadInProgress)
            {
                _pendingKind = kind;
                _hasPendingKind = true;
                return;
            }

            _hasPendingKind = false;

            BMD? model = null;
            if (kind != FlyingHelperKind.None)
            {
                if (_kind == kind && Model != null && _modelContentReady)
                    return;

                string primaryPath = kind == FlyingHelperKind.GuardianAngel
                    ? GuardianModelPath
                    : ImpModelPath;

                model = await PrepareModelWithFallbackAsync(primaryPath, kind);
                if (requestVersion != Volatile.Read(ref _modelRequestVersion))
                    return;
            }

            await ApplyResolvedKindOnMainThreadAsync(kind, model, requestVersion);
        }

        private Task ApplyResolvedKindOnMainThreadAsync(
            FlyingHelperKind kind,
            BMD? model,
            int requestVersion)
        {
            if (MuGame.IsMainThread)
                return ApplyResolvedKindAsync(kind, model, requestVersion);

            var completion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            MuGame.ScheduleOnMainThread(async () =>
            {
                try
                {
                    await ApplyResolvedKindAsync(kind, model, requestVersion);
                    completion.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            }, MainThreadDispatcher.WorkPriority.High, "FlyingHelper.ApplyModel");

            return completion.Task;
        }

        private async Task ApplyResolvedKindAsync(
            FlyingHelperKind requestedKind,
            BMD? resolvedModel,
            int requestVersion)
        {
            await _modelApplyGate.WaitAsync();
            try
            {
                if (requestVersion != Volatile.Read(ref _modelRequestVersion) ||
                    IsDisposeRequested || Status == GameControlStatus.Disposed)
                {
                    return;
                }

                // Assigning Model while Ready starts an unobserved fire-and-forget reload in the
                // base property setter. Put the helper into Initializing and perform exactly one
                // awaited model-content pass, so meshes, textures, bone matrices and buffers are
                // ready before the object becomes visible.
                Status = GameControlStatus.Initializing;
                Hidden = true;
                _modelContentReady = false;
                _spawnInitialized = false;
                ClearSparks();

                _kind = resolvedModel == null ? FlyingHelperKind.None : requestedKind;
                Model = resolvedModel;
                BlendMesh = _kind == FlyingHelperKind.GuardianAngel ? 1 : -1;
                BlendMeshLight = 1f;
                Alpha = 0f;
                CurrentAction = 0;
                AnimationSpeed = HelperWingAnimationSpeed;
                Scale = GetWorldScale(requestedKind);
                _impShoulderBoneIndex = UnresolvedBoneIndex;

                // Load only the ModelObject content here. Billboard resources belong to the helper
                // itself and were already initialized by the normal child Load() lifecycle.
                await base.LoadContent();
                ConfigureWingAnimation();

                bool stillCurrent = requestVersion == Volatile.Read(ref _modelRequestVersion);
                _modelContentReady = stillCurrent && resolvedModel != null && Model == resolvedModel;
                Hidden = !_modelContentReady;
                Status = GameControlStatus.Ready;
            }
            catch
            {
                _kind = FlyingHelperKind.None;
                _modelContentReady = false;
                _spawnInitialized = false;
                Hidden = true;
                Model = null;
                Status = GameControlStatus.Error;
                throw;
            }
            finally
            {
                _modelApplyGate.Release();
            }

            // A newer equipment update may have arrived while this model was loading. Apply only
            // the final requested helper after the current content pass has fully completed.
            if (_hasPendingKind && Status == GameControlStatus.Ready)
            {
                FlyingHelperKind pendingKind = _pendingKind;
                _hasPendingKind = false;
                await SetKindAsync(pendingKind);
            }
        }

        public override async Task LoadContent()
        {
            await base.LoadContent();

            if (_sparkTexture == null || _sparkTexture.IsDisposed)
            {
                _sparkTexture = await PrepareTextureAsync(SparkTexturePath);
                if (_sparkTexture == null)
                {
                    _sparkTexture = CreateRadialTexture(GraphicsDevice, 32, 2.8f);
                    _ownsSparkTexture = true;
                }
            }

            if (_lightTexture == null || _lightTexture.IsDisposed)
            {
                _lightTexture = await PrepareTextureAsync(LightTexturePath);
                if (_lightTexture == null)
                {
                    _lightTexture = CreateRadialTexture(GraphicsDevice, 64, 2.2f);
                    _ownsLightTexture = true;
                }
            }

            _billboardEffect ??= new BasicEffect(GraphicsDevice)
            {
                TextureEnabled = true,
                VertexColorEnabled = true,
                LightingEnabled = false,
                FogEnabled = false,
                World = Matrix.Identity
            };
        }

        public override void Update(GameTime gameTime)
        {
            if (Status != GameControlStatus.Ready || Parent is not PlayerObject owner)
                return;

            bool blocked = _kind == FlyingHelperKind.None ||
                           Model == null ||
                           !_modelContentReady ||
                           owner.Hidden ||
                           owner.World == null ||
                           IsChaosCastleMap(owner.World.WorldIndex);

            if (blocked)
            {
                Hidden = true;
                _spawnInitialized = false;
                ClearSparks();
                return;
            }

            if (!_spawnInitialized)
                InitializeSpawn(owner);

            Hidden = false;
            bool characterScene = MuGame.Instance.ActiveScene is SelectCharacterScene;
            Scale = characterScene
                ? GetCharacterSceneScale(_kind)
                : GetWorldScale(_kind);

            float elapsed = MathHelper.Clamp(
                (float)gameTime.ElapsedGameTime.TotalSeconds,
                0f,
                LegacyStepSeconds * MaxLegacyStepsPerFrame);

            _legacyAccumulator += elapsed;
            int steps = 0;
            while (_legacyAccumulator >= LegacyStepSeconds && steps < MaxLegacyStepsPerFrame)
            {
                TickLegacy(owner);
                _legacyAccumulator -= LegacyStepSeconds;
                steps++;
            }

            if (steps == MaxLegacyStepsPerFrame)
                _legacyAccumulator = MathF.Min(_legacyAccumulator, LegacyStepSeconds);

            if (_kind == FlyingHelperKind.Imp)
            {
                UpdateImpShoulderTransform(owner);
            }
            else
            {
                float interpolation = MathHelper.Clamp(_legacyAccumulator / LegacyStepSeconds, 0f, 1f);
                Position = Vector3.Lerp(_previousSimulationPosition, _simulationPosition, interpolation);
                Angle = new Vector3(0f, 0f, _simulationYaw);
            }

            base.Update(gameTime);
        }

        public override void DrawAfter(GameTime gameTime)
        {
            base.DrawAfter(gameTime);

            if (!Visible || _kind != FlyingHelperKind.GuardianAngel ||
                _billboardEffect == null || Parent is not PlayerObject owner)
            {
                return;
            }

            float inheritedAlpha = MathHelper.Clamp(TotalAlpha, 0f, 1f);
            if (inheritedAlpha <= 0.01f)
                return;

            BlendState previousBlend = GraphicsDevice.BlendState;
            DepthStencilState previousDepth = GraphicsDevice.DepthStencilState;
            RasterizerState previousRasterizer = GraphicsDevice.RasterizerState;
            SamplerState previousSampler = GraphicsDevice.SamplerStates[0];

            try
            {
                GraphicsDevice.BlendState = Blendings.OneOneAdditive;
                GraphicsDevice.DepthStencilState = DepthStencilState.DepthRead;
                GraphicsDevice.RasterizerState = RasterizerState.CullNone;
                GraphicsDevice.SamplerStates[0] = SamplerState.LinearClamp;

                _billboardEffect.World = Matrix.Identity;
                _billboardEffect.View = Camera.Instance.View;
                _billboardEffect.Projection = Camera.Instance.Projection;

                DrawGuardianGlow(inheritedAlpha);

                // The original suppresses spark emission during cloaking, while still requesting
                // the helper glow. TotalAlpha is the available equivalent of the cloak render state.
                if (owner.TotalAlpha >= 0.8f)
                    DrawGuardianSparks(inheritedAlpha);
            }
            finally
            {
                GraphicsDevice.BlendState = previousBlend;
                GraphicsDevice.DepthStencilState = previousDepth;
                GraphicsDevice.RasterizerState = previousRasterizer;
                GraphicsDevice.SamplerStates[0] = previousSampler;
            }
        }

        protected override void RecalculateWorldPosition()
        {
            // Position is already expressed in world coordinates. Guardian uses its simulated
            // position, while Imp receives a shoulder position sampled from the player's current
            // bone palette. The helper still uses its own bones for wing animation.
            WorldPosition = Matrix.CreateScale(Scale)
                * Matrix.CreateFromQuaternion(Client.Main.Core.Utilities.MathUtils.AngleQuaternion(Angle))
                * Matrix.CreateTranslation(Position);
        }

        private void InitializeSpawn(PlayerObject owner)
        {
            Vector3 ownerPosition = owner.WorldPosition.Translation;
            Vector3 spawn;

            if (_kind == FlyingHelperKind.GuardianAngel)
            {
                spawn = new Vector3(
                    ownerPosition.X + _random.Next(-256, 256),
                    ownerPosition.Y + _random.Next(-256, 256),
                    ownerPosition.Z + _random.Next(128, 256));
            }
            else
            {
                spawn = GetImpShoulderPosition(owner);
            }

            _previousSimulationPosition = spawn;
            _simulationPosition = spawn;
            _simulationYaw = owner.Angle.Z;
            _localSpeed = 0f;
            _verticalSpeed = 0f;
            _legacyAccumulator = 0f;
            Alpha = 0f;
            BlendMeshLight = 1f;
            // ModelObject's CPU path bakes TotalAlpha into vertex lighting. A reused helper may
            // still have buffers created at full alpha, so explicitly rebuild them for fade-in.
            InvalidateBuffers(BufferFlagLighting | BufferFlagMaterial);
            Position = spawn;
            Angle = new Vector3(0f, 0f, _simulationYaw);
            ClearSparks();
            _spawnInitialized = true;
        }

        private void TickLegacy(PlayerObject owner)
        {
            _previousSimulationPosition = _simulationPosition;

            float previousAlpha = Alpha;
            Alpha += (1f - Alpha) * 0.1f;
            if (MathF.Abs(Alpha - previousAlpha) > 0.0001f)
            {
                // Non-blend CPU meshes store alpha in their vertex RGB lighting. Without this
                // invalidation Helper01/02 are built once at Alpha=0 and remain black forever,
                // even though the independently rendered Guardian billboard fades in correctly.
                InvalidateBuffers(BufferFlagLighting | BufferFlagMaterial);
            }

            if (BlendMeshLight > Alpha)
                BlendMeshLight = Alpha;

            if (_kind == FlyingHelperKind.Imp)
            {
                Vector3 shoulder = GetImpShoulderPosition(owner);
                _previousSimulationPosition = shoulder;
                _simulationPosition = shoulder;
                _simulationYaw = owner.Angle.Z;
                _localSpeed = 0f;
                _verticalSpeed = 0f;
                ClearSparks();
                return;
            }

            Vector3 ownerPosition = owner.WorldPosition.Translation;
            Vector2 delta = new(
                ownerPosition.X - _simulationPosition.X,
                ownerPosition.Y - _simulationPosition.Y);
            float distanceSquared = delta.LengthSquared();

            if (distanceSquared >= FlyRange * FlyRange)
            {
                float targetYaw = MathF.Atan2(delta.X, -delta.Y);
                _simulationYaw = TurnTowards(_simulationYaw, targetYaw, MathHelper.ToRadians(20f));
            }

            float forwardX = MathF.Sin(_simulationYaw);
            float forwardY = -MathF.Cos(_simulationYaw);
            _simulationPosition.X += forwardX * -_localSpeed;
            _simulationPosition.Y += forwardY * -_localSpeed;
            _simulationPosition.Z += _verticalSpeed;
            _simulationPosition.Z += _random.Next(-8, 8);

            if (_random.Next(32) == 0)
            {
                if (distanceSquared >= FlyRange * FlyRange)
                {
                    _localSpeed = -(_random.Next(128, 192) * 0.1f);
                }
                else
                {
                    _localSpeed = -(_random.Next(16, 80) * 0.1f);
                    _simulationYaw = MathHelper.ToRadians(_random.Next(0, 360));
                }

                _verticalSpeed = _random.Next(-32, 32) * 0.1f;
            }

            if (_simulationPosition.Z < ownerPosition.Z + 100f)
                _verticalSpeed += 1.5f;
            if (_simulationPosition.Z > ownerPosition.Z + 200f)
                _verticalSpeed -= 1.5f;

            if (_kind == FlyingHelperKind.GuardianAngel && owner.TotalAlpha >= 0.8f)
            {
                for (int i = 0; i < 4; i++)
                    SpawnSpark();
            }

            UpdateSparks();
        }

        /// <summary>
        /// Rebinds an equipped helper after the persistent player object is moved to another
        /// WorldControl. Map changes intentionally retain the local player, so no equipment packet
        /// is guaranteed to arrive and recreate the helper.
        /// </summary>
        public async Task RestoreAfterWorldChangeAsync(FlyingHelperKind equippedKind)
        {
            if (IsDisposeRequested || Status == GameControlStatus.Disposed)
                return;

            if (equippedKind != FlyingHelperKind.None &&
                (_kind != equippedKind || Model == null || !_modelContentReady ||
                 Status != GameControlStatus.Ready))
            {
                await SetKindAsync(equippedKind);
            }
            else if (equippedKind == FlyingHelperKind.None && _kind != FlyingHelperKind.None)
            {
                await SetKindAsync(FlyingHelperKind.None);
            }

            if (equippedKind == FlyingHelperKind.None || _kind == FlyingHelperKind.None)
                return;

            _spawnInitialized = false;
            _legacyAccumulator = 0f;
            _impShoulderBoneIndex = UnresolvedBoneIndex;
            Hidden = true;
            Alpha = 0f;
            BlendMeshLight = 1f;
            ClearSparks();
            InvalidateBuffers(BufferFlagLighting | BufferFlagMaterial | BufferFlagAnimation);
        }

        private static float GetWorldScale(FlyingHelperKind kind) =>
            kind == FlyingHelperKind.GuardianAngel ? GuardianWorldScale : ImpWorldScale;

        private static float GetCharacterSceneScale(FlyingHelperKind kind) =>
            kind == FlyingHelperKind.GuardianAngel
                ? GuardianCharacterSceneScale
                : ImpCharacterSceneScale;

        private void ConfigureWingAnimation()
        {
            CurrentAction = ResolveWingAnimationAction();
            AnimationSpeed = HelperWingAnimationSpeed;
            ContinuousAnimation = true;
            FreezeAnimationPose = false;
            InvalidateBuffers(BufferFlagAnimation);
        }

        private int ResolveWingAnimationAction()
        {
            if (Model?.Actions == null || Model.Actions.Length == 0)
                return 0;

            if (Model.Actions[0] != null && Model.Actions[0].NumAnimationKeys > 1)
                return 0;

            for (int i = 1; i < Model.Actions.Length; i++)
            {
                if (Model.Actions[i] != null && Model.Actions[i].NumAnimationKeys > 1)
                    return i;
            }

            return 0;
        }

        private void UpdateImpShoulderTransform(PlayerObject owner)
        {
            Vector3 shoulder = GetImpShoulderPosition(owner);
            _previousSimulationPosition = shoulder;
            _simulationPosition = shoulder;
            _simulationYaw = owner.Angle.Z;
            Position = shoulder;
            Angle = new Vector3(0f, 0f, owner.Angle.Z);
        }

        private Vector3 GetImpShoulderPosition(PlayerObject owner)
        {
            int boneIndex = ResolveImpShoulderBoneIndex(owner);
            Vector3 ownerCenter = owner.WorldPosition.Translation;
            Vector3 anchor;

            if (boneIndex >= 0 && owner.TryGetBoneWorldMatrix(boneIndex, out Matrix shoulderMatrix))
            {
                anchor = shoulderMatrix.Translation;
            }
            else
            {
                float shoulderHeight = owner.BodyHeight > 1f ? owner.BodyHeight * 0.72f : 110f;
                anchor = ownerCenter + new Vector3(0f, 0f, shoulderHeight);
            }

            // The clavicle pivot sits on the shoulder line, close to the neck. Push the helper
            // outward along the horizontal (world X/Y) direction from the body center to the
            // selected shoulder, then lift it slightly (world Z is height). Working in world axes
            // keeps the helper on the shoulder regardless of the player skeleton variant.
            Vector2 outward = new(anchor.X - ownerCenter.X, anchor.Y - ownerCenter.Y);
            if (outward.LengthSquared() < 1f)
            {
                outward = GetLocalRightDirection(owner);
            }
            else
            {
                outward.Normalize();
            }

            anchor.X += outward.X * ImpShoulderOutwardOffset;
            anchor.Y += outward.Y * ImpShoulderOutwardOffset;
            anchor.Z += ImpShoulderHeightOffset;
            return anchor;
        }

        /// <summary>
        /// World-space horizontal direction of the character's right side. Used only when the
        /// resolved shoulder bone sits on the body axis. Mirrors the cape cloth's convention
        /// where the owner's local +Y is the backward axis, so local -Z is the right side.
        /// </summary>
        private static Vector2 GetLocalRightDirection(PlayerObject owner)
        {
            Matrix ownerRotation = Matrix.CreateFromQuaternion(
                Client.Main.Core.Utilities.MathUtils.AngleQuaternion(owner.Angle));
            Vector3 right = Vector3.TransformNormal(-Vector3.UnitZ, ownerRotation);
            return new Vector2(right.X, right.Y);
        }

        private int ResolveImpShoulderBoneIndex(PlayerObject owner)
        {
            if (_impShoulderBoneIndex != UnresolvedBoneIndex)
                return _impShoulderBoneIndex;

            var bones = owner.Model?.Bones;
            if (bones == null || bones.Length == 0)
                return -1;

            for (int i = 0; i < bones.Length; i++)
            {
                string name = bones[i].Name ?? string.Empty;
                bool rightSide = name.Contains("Bip01 R", StringComparison.OrdinalIgnoreCase) ||
                                 name.Contains("Right", StringComparison.OrdinalIgnoreCase);
                bool shoulder = name.Contains("Clavicle", StringComparison.OrdinalIgnoreCase) ||
                                name.Contains("Shoulder", StringComparison.OrdinalIgnoreCase);
                if (rightSide && shoulder)
                {
                    _impShoulderBoneIndex = i;
                    return i;
                }
            }

            // Older Player.bmd variants do not always preserve descriptive bone names. Walk from
            // the known right-hand anchor towards the torso and use the third valid ancestor,
            // which corresponds to the upper-arm/clavicle area in the classic player skeleton.
            int current = PlayerObject.RightHandBoneIndex;
            int fallback = -1;
            int depth = 0;
            while ((uint)current < (uint)bones.Length && depth < 12)
            {
                string name = bones[current].Name ?? string.Empty;
                if (name.Contains("Clavicle", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Shoulder", StringComparison.OrdinalIgnoreCase))
                {
                    _impShoulderBoneIndex = current;
                    return current;
                }

                if (depth == 3)
                    fallback = current;

                current = bones[current].Parent;
                depth++;
            }

            _impShoulderBoneIndex = fallback;
            return fallback;
        }

        private void SpawnSpark()
        {
            int slot = -1;
            for (int i = 0; i < _sparks.Length; i++)
            {
                if (!_sparks[i].Active)
                {
                    slot = i;
                    break;
                }
            }

            if (slot < 0)
                slot = _random.Next(_sparks.Length);

            Vector3 position = _simulationPosition + new Vector3(
                _random.Next(-8, 8),
                _random.Next(-8, 8),
                _random.Next(-8, 8));

            int lifeTicks = 10 + _random.Next(0, 5);
            _sparks[slot] = new SparkParticle
            {
                Active = true,
                Position = position,
                PreviousPosition = position,
                VelocityPerTick = new Vector3(
                    (_random.NextSingle() - 0.5f) * 1.2f,
                    (_random.NextSingle() - 0.5f) * 1.2f,
                    0.7f + _random.NextSingle() * 1.1f),
                Rotation = _random.NextSingle() * MathHelper.TwoPi,
                RotationPerTick = (_random.NextSingle() - 0.5f) * 0.35f,
                Scale = 0.16f + _random.NextSingle() * 0.10f,
                LifeTicks = lifeTicks,
                MaxLifeTicks = lifeTicks
            };
        }

        private void UpdateSparks()
        {
            for (int i = 0; i < _sparks.Length; i++)
            {
                if (!_sparks[i].Active)
                    continue;

                SparkParticle spark = _sparks[i];
                spark.PreviousPosition = spark.Position;
                spark.Position += spark.VelocityPerTick;
                spark.VelocityPerTick *= 0.92f;
                spark.Rotation += spark.RotationPerTick;
                spark.LifeTicks--;
                if (spark.LifeTicks <= 0)
                    spark.Active = false;
                _sparks[i] = spark;
            }
        }

        private void DrawGuardianGlow(float alpha)
        {
            if (_lightTexture == null || _lightTexture.IsDisposed)
                return;

            Matrix inverseView = Matrix.Invert(Camera.Instance.View);
            Vector3 cameraRight = inverseView.Right;
            Vector3 cameraUp = inverseView.Up;
            float luminosity = 0.70f + _random.NextSingle() * 0.30f;
            Vector3 light = new(
                luminosity * 0.5f,
                luminosity * 0.8f,
                luminosity * 0.6f);
            Color color = ToAdditiveColor(light * alpha);
            float halfSize = 32f * (Scale / 1f);

            WriteBillboardQuad(0, Position, cameraRight * halfSize, cameraUp * halfSize, color);
            DrawBillboardBatch(_lightTexture, 1);
        }

        private void DrawGuardianSparks(float alpha)
        {
            if (_sparkTexture == null || _sparkTexture.IsDisposed)
                return;

            Matrix inverseView = Matrix.Invert(Camera.Instance.View);
            Vector3 cameraRight = inverseView.Right;
            Vector3 cameraUp = inverseView.Up;
            float interpolation = MathHelper.Clamp(_legacyAccumulator / LegacyStepSeconds, 0f, 1f);
            int quadCount = 0;

            for (int i = 0; i < _sparks.Length && quadCount < MaxSparks; i++)
            {
                SparkParticle spark = _sparks[i];
                if (!spark.Active)
                    continue;

                Vector3 center = Vector3.Lerp(spark.PreviousPosition, spark.Position, interpolation);
                float life = MathHelper.Clamp(spark.LifeTicks / (float)Math.Max(1, spark.MaxLifeTicks), 0f, 1f);
                float fadeIn = MathHelper.Clamp((1f - life) * 5f, 0f, 1f);
                float intensity = MathF.Pow(life, 0.8f) * fadeIn * alpha;
                float halfSize = 9f * spark.Scale * (0.65f + life * 0.35f) * (Scale / 1f);

                Vector3 right = cameraRight * halfSize;
                Vector3 up = cameraUp * halfSize;
                RotateBillboardAxes(ref right, ref up, spark.Rotation);
                Color color = ToAdditiveColor(new Vector3(0.4f, 0.4f, 0.4f) * intensity);
                WriteBillboardQuad(quadCount++, center, right, up, color);
            }

            DrawBillboardBatch(_sparkTexture, quadCount);
        }

        private void DrawBillboardBatch(Texture2D texture, int quadCount)
        {
            if (quadCount <= 0 || _billboardEffect == null)
                return;

            _billboardEffect.Texture = texture;
            foreach (EffectPass pass in _billboardEffect.CurrentTechnique.Passes)
            {
                pass.Apply();
                GraphicsDevice.DrawUserIndexedPrimitives(
                    PrimitiveType.TriangleList,
                    _billboardVertices,
                    0,
                    quadCount * 4,
                    _billboardIndices,
                    0,
                    quadCount * 2);
            }
        }

        private void WriteBillboardQuad(
            int quadIndex,
            Vector3 center,
            Vector3 right,
            Vector3 up,
            Color color)
        {
            int vertex = quadIndex * 4;
            _billboardVertices[vertex] = new VertexPositionColorTexture(
                center - right - up, color, new Vector2(0f, 1f));
            _billboardVertices[vertex + 1] = new VertexPositionColorTexture(
                center + right - up, color, new Vector2(1f, 1f));
            _billboardVertices[vertex + 2] = new VertexPositionColorTexture(
                center + right + up, color, new Vector2(1f, 0f));
            _billboardVertices[vertex + 3] = new VertexPositionColorTexture(
                center - right + up, color, new Vector2(0f, 0f));
        }

        private void BuildStaticIndices()
        {
            for (int i = 0; i < MaxBillboardQuads; i++)
            {
                int vertex = i * 4;
                int index = i * 6;
                _billboardIndices[index] = checked((short)vertex);
                _billboardIndices[index + 1] = checked((short)(vertex + 1));
                _billboardIndices[index + 2] = checked((short)(vertex + 2));
                _billboardIndices[index + 3] = checked((short)vertex);
                _billboardIndices[index + 4] = checked((short)(vertex + 2));
                _billboardIndices[index + 5] = checked((short)(vertex + 3));
            }
        }

        private void ClearSparks()
        {
            Array.Clear(_sparks, 0, _sparks.Length);
        }

        private static float TurnTowards(float current, float target, float maxStep)
        {
            float delta = MathHelper.WrapAngle(target - current);
            return MathHelper.WrapAngle(current + MathHelper.Clamp(delta, -maxStep, maxStep));
        }

        private static void RotateBillboardAxes(ref Vector3 right, ref Vector3 up, float radians)
        {
            float cosine = MathF.Cos(radians);
            float sine = MathF.Sin(radians);
            Vector3 originalRight = right;
            right = originalRight * cosine + up * sine;
            up = up * cosine - originalRight * sine;
        }

        private static Color ToAdditiveColor(Vector3 color)
        {
            return new Color(
                MathHelper.Clamp(color.X, 0f, 1f),
                MathHelper.Clamp(color.Y, 0f, 1f),
                MathHelper.Clamp(color.Z, 0f, 1f),
                1f);
        }

        private async Task<BMD?> PrepareModelWithFallbackAsync(
            string primaryPath,
            FlyingHelperKind kind)
        {
            string fileName = kind == FlyingHelperKind.GuardianAngel ? "helper01.bmd" : "helper02.bmd";
            short itemNumber = kind == FlyingHelperKind.GuardianAngel ? (short)0 : (short)1;
            var definition = ItemDatabase.GetItemDefinition(13, itemNumber);
            string? inventoryPath = NormalizeModelPath(definition?.TexturePath);

            // Use exactly the model selected by the item database first. This is the asset which
            // is already proven to render in the inventory preview. Keep SourceMain's Player model
            // and the historical Item model as fallbacks for different data packs.
            string?[] paths =
            {
                inventoryPath,
                primaryPath,
                $"Item/{fileName}"
            };

            for (int i = 0; i < paths.Length; i++)
            {
                string? path = paths[i];
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                bool duplicate = false;
                for (int previous = 0; previous < i; previous++)
                {
                    if (string.Equals(paths[previous], path, StringComparison.OrdinalIgnoreCase))
                    {
                        duplicate = true;
                        break;
                    }
                }

                if (duplicate)
                    continue;

                var model = await BMDLoader.Instance.Prepare(path).ConfigureAwait(false);
                if (model != null)
                    return model;
            }

            return null;
        }

        private static string? NormalizeModelPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            string normalized = path.Replace('\\', '/').TrimStart('/');
            if (normalized.StartsWith("Data/", StringComparison.OrdinalIgnoreCase))
                normalized = normalized[5..];

            return normalized;
        }

        private static async Task<Texture2D?> PrepareTextureAsync(string path)
        {
            try
            {
                await TextureLoader.Instance.Prepare(path);
                return TextureLoader.Instance.GetTexture2D(path);
            }
            catch
            {
                return null;
            }
        }

        private static Texture2D CreateRadialTexture(GraphicsDevice graphicsDevice, int size, float exponent)
        {
            var texture = new Texture2D(graphicsDevice, size, size);
            var pixels = new Color[size * size];
            float center = (size - 1) * 0.5f;
            float radius = size * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - center) / radius;
                    float dy = (y - center) / radius;
                    float distance = MathF.Sqrt(dx * dx + dy * dy);
                    float intensity = MathF.Pow(MathHelper.Clamp(1f - distance, 0f, 1f), exponent);
                    pixels[y * size + x] = new Color(intensity, intensity, intensity, intensity);
                }
            }

            texture.SetData(pixels);
            return texture;
        }

        private static bool IsChaosCastleMap(short worldIndex) =>
            (worldIndex >= 18 && worldIndex <= 23) || worldIndex == 53 || worldIndex == 97;

        public override void Dispose()
        {
            _billboardEffect?.Dispose();
            _billboardEffect = null;

            if (_ownsSparkTexture)
                _sparkTexture?.Dispose();
            if (_ownsLightTexture)
                _lightTexture?.Dispose();

            _sparkTexture = null;
            _lightTexture = null;
            base.Dispose();
        }

        private struct SparkParticle
        {
            public bool Active;
            public Vector3 Position;
            public Vector3 PreviousPosition;
            public Vector3 VelocityPerTick;
            public float Rotation;
            public float RotationPerTick;
            public float Scale;
            public int LifeTicks;
            public int MaxLifeTicks;
        }
    }
}
