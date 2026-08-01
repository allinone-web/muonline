using Client.Data.BMD;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Core.Utilities;
using Client.Main.Controls;
using Client.Main.Models;
using Client.Main.Objects.Player;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Client.Main.Graphics;

namespace Client.Main.Objects.Wings
{
    internal enum CapeClothKind
    {
        None,
        CapeOfLord,
        SmallCapeOfLord,
        CapeOfEmperor,
        CapeOfFighter,
        SmallCapeOfFighter,
        CapeOfOverrule
    }

    internal readonly struct CapeRigidAttachment
    {
        public CapeRigidAttachment(bool visible, Vector3 position, Vector3 angle)
        {
            Visible = visible;
            Position = position;
            Angle = angle;
        }

        public bool Visible { get; }
        public Vector3 Position { get; }
        public Vector3 Angle { get; }
    }

    internal readonly struct CapeClothProfile
    {
        private CapeClothProfile(
            CapeClothKind kind,
            float width,
            float height,
            Vector3 offset,
            bool heavy,
            float upperSphereRadius,
            float lowerSphereRadius,
            string[] textureCandidates,
            CapeRigidAttachment rigidAttachment)
        {
            Kind = kind;
            Width = width;
            Height = height;
            Offset = offset;
            Heavy = heavy;
            UpperSphereRadius = upperSphereRadius;
            LowerSphereRadius = lowerSphereRadius;
            TextureCandidates = textureCandidates;
            RigidAttachment = rigidAttachment;
        }

        public CapeClothKind Kind { get; }
        public float Width { get; }
        public float Height { get; }
        public Vector3 Offset { get; }
        public bool Heavy { get; }
        public float UpperSphereRadius { get; }
        public float LowerSphereRadius { get; }
        public string[] TextureCandidates { get; }
        public CapeRigidAttachment RigidAttachment { get; }
        public bool HasSideRibbons => Kind is CapeClothKind.CapeOfEmperor or CapeClothKind.CapeOfOverrule;
        public bool IsRageFighterCape => Kind is CapeClothKind.CapeOfFighter or CapeClothKind.SmallCapeOfFighter or CapeClothKind.CapeOfOverrule;

        public static bool TryCreate(short itemIndex, string modelPath, out CapeClothProfile profile)
        {
            CapeClothKind kind = itemIndex switch
            {
                30 => CapeClothKind.CapeOfLord,
                40 => CapeClothKind.CapeOfEmperor,
                49 => CapeClothKind.CapeOfFighter,
                50 => CapeClothKind.CapeOfOverrule,
                130 => CapeClothKind.SmallCapeOfLord,
                135 => CapeClothKind.SmallCapeOfFighter,
                _ => DetectKindFromPath(modelPath)
            };

            profile = kind switch
            {
                CapeClothKind.CapeOfLord => new CapeClothProfile(
                    kind, 180f, 180f, new Vector3(0f, 8f, 10f), false, 25f, 27f,
                    Array.Empty<string>(),
                    new CapeRigidAttachment(false, Vector3.Zero, Vector3.Zero)),

                CapeClothKind.SmallCapeOfLord => new CapeClothProfile(
                    kind, 100f, 100f, new Vector3(0f, 8f, 10f), false, 25f, 27f,
                    Array.Empty<string>(),
                    new CapeRigidAttachment(false, Vector3.Zero, Vector3.Zero)),

                CapeClothKind.CapeOfEmperor => new CapeClothProfile(
                    kind, 180f, 180f, new Vector3(0f, 8f, 10f), true, 25f, 27f,
                    new[] { "Item/dl_redwings02.tga" },
                    new CapeRigidAttachment(
                        false,
                        new Vector3(-47f, -7f, 0f),
                        new Vector3(0f, MathHelper.ToRadians(90f), 0f))),

                CapeClothKind.CapeOfFighter => new CapeClothProfile(
                    kind, 180f, 170f, new Vector3(0f, 15f, 5f), true, 35f, 37f,
                    new[] { "Item/NCcape.tga", "Item/NCcape.jpg", "Item/NCcape.ozj" },
                    new CapeRigidAttachment(false, Vector3.Zero, Vector3.Zero)),

                CapeClothKind.SmallCapeOfFighter => new CapeClothProfile(
                    kind, 150f, 130f, new Vector3(0f, 15f, 5f), true, 35f, 37f,
                    new[] { "Item/NCcape.tga", "Item/NCcape.jpg", "Item/NCcape.ozj" },
                    new CapeRigidAttachment(false, Vector3.Zero, Vector3.Zero)),

                CapeClothKind.CapeOfOverrule => new CapeClothProfile(
                    kind, 180f, 170f, new Vector3(0f, 15f, 5f), true, 35f, 37f,
                    new[] { "Item/monke_manto.TGA", "Item/monke_manto.tga", "Item/monke_manto.jpg" },
                    new CapeRigidAttachment(
                        true,
                        new Vector3(10f, -15f, 15f),
                        new Vector3(0f, MathHelper.ToRadians(90f), 0f))),

                _ => default
            };

            return kind != CapeClothKind.None;
        }

        private static CapeClothKind DetectKindFromPath(string modelPath)
        {
            if (string.IsNullOrWhiteSpace(modelPath))
                return CapeClothKind.None;

            string path = modelPath.Replace('\\', '/');
            if (path.Contains("DarkLordRobe02", StringComparison.OrdinalIgnoreCase))
                return CapeClothKind.CapeOfEmperor;
            if (path.Contains("DarkLordRobe", StringComparison.OrdinalIgnoreCase))
                return CapeClothKind.CapeOfLord;
            if (path.Contains("Wing51", StringComparison.OrdinalIgnoreCase))
                return CapeClothKind.CapeOfOverrule;
            if (path.Contains("Wing50", StringComparison.OrdinalIgnoreCase))
                return CapeClothKind.CapeOfFighter;

            return CapeClothKind.None;
        }
    }

    /// <summary>
    /// CPU mass-spring cloth used by the classic MU capes. The visible sheet is a separate
    /// 10x10 grid; only its top row is pinned to the animated player skeleton. The remaining
    /// vertices retain velocity, react to wind and gravity, and are pushed out of torso spheres.
    /// </summary>
    internal sealed class CapeClothObject : EffectObject
    {
        private const int AnchorBone = 19;
        private const int TorsoBone = 17;
        private const int LowerTorsoBone = 2;
        private const int MainColumns = 10;
        private const int MainRows = 10;
        private const float LegacyTickSeconds = 1f / 25f;
        private const float SolverSubstepSeconds = 0.005f;
        private const int SolverSubsteps = 5;
        private const int MaxCatchUpSteps = 5;
        private const float Gravity = 9.8f;
        private const float ParticleMass = 0.0025f;
        private const float InverseParticleMass = 1f / ParticleMass;
        private const float TeleportResetDistanceSquared = 450f * 450f;
        private const float ConstraintFailureMultiplier = 20f;
        private const float MaximumParticleSpeed = 2400f;
        private const float SpringStiffness = 7.5f;
        private const float RibbonSpringStiffness = 9.5f;
        private const int ConstraintIterations = 2;

        private readonly List<ClothPiece> _pieces = new(3);
        private CapeClothProfile _profile;
        private float _accumulator;
        private float _interpolation;
        private float _simulationTime;
        private float _wind;
        private uint _randomState = 0xA341316Cu;
        private int _configurationVersion;
        private bool _needsReset = true;
        private bool _configured;
        private Vector3 _lastAnchor;
        private float _lastOwnerScale;

        public CapeClothObject()
        {
            Hidden = true;
            IsTransparent = true;
            AffectedByTransparency = true;
            BlendState = BlendState.NonPremultiplied;
            DepthState = DepthStencilState.Default;
            BoundingBoxLocal = new BoundingBox(
                new Vector3(-260f, -260f, -260f),
                new Vector3(260f, 260f, 300f));
        }

        public async Task<bool> ConfigureAsync(CapeClothProfile profile, BMD model)
        {
            int version = Interlocked.Increment(ref _configurationVersion);
            Texture2D mainTexture = await ResolveTextureAsync(profile.TextureCandidates, model, 0).ConfigureAwait(false);
            Texture2D ribbonTexture = mainTexture;

            if (profile.HasSideRibbons)
            {
                string[] ribbonCandidates = profile.Kind == CapeClothKind.CapeOfOverrule
                    ? new[] { "Item/monk_manto01.TGA", "Item/monk_manto01.tga", "Item/monk_manto01.jpg" }
                    : Array.Empty<string>();
                ribbonTexture = await ResolveTextureAsync(ribbonCandidates, model, 1).ConfigureAwait(false) ?? mainTexture;
            }

            if (version != Volatile.Read(ref _configurationVersion))
                return false;

            if (MuGame.IsMainThread)
                return ApplyConfiguration(version, profile, mainTexture, ribbonTexture);

            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            MuGame.ScheduleOnMainThread(static state =>
            {
                var (cloth, requestedVersion, requestedProfile, main, ribbon, source) = state;
                try
                {
                    source.TrySetResult(cloth.ApplyConfiguration(
                        requestedVersion,
                        requestedProfile,
                        main,
                        ribbon));
                }
                catch (Exception ex)
                {
                    source.TrySetException(ex);
                }
            },
            (this, version, profile, mainTexture, ribbonTexture, completion),
            name: "CapeCloth.Configure");

            return await completion.Task.ConfigureAwait(false);
        }

        private bool ApplyConfiguration(
            int version,
            CapeClothProfile profile,
            Texture2D mainTexture,
            Texture2D ribbonTexture)
        {
            if (version != Volatile.Read(ref _configurationVersion))
                return false;

            _profile = profile;
            _pieces.Clear();

            if (mainTexture != null)
            {
                _pieces.Add(ClothPiece.CreateMain(profile, mainTexture));

                if (profile.HasSideRibbons && ribbonTexture != null)
                {
                    if (profile.Kind == CapeClothKind.CapeOfEmperor)
                    {
                        _pieces.Add(ClothPiece.CreateRibbon(
                            new Vector3(30f, 15f, 10f), 12f, 200f, ribbonTexture, 0f, 30f, 35f));
                        _pieces.Add(ClothPiece.CreateRibbon(
                            new Vector3(-30f, 20f, 10f), 12f, 200f, ribbonTexture, 0f, 30f, 35f));
                    }
                    else
                    {
                        _pieces.Add(ClothPiece.CreateRibbon(
                            new Vector3(25f, 15f, 2f), 12f, 180f, ribbonTexture, 18f, 35f, 45f));
                        _pieces.Add(ClothPiece.CreateRibbon(
                            new Vector3(-25f, 15f, 2f), 12f, 180f, ribbonTexture, -18f, 35f, 50f));
                    }
                }
            }

            _configured = _pieces.Count > 0;
            Hidden = !_configured;
            _needsReset = true;
            _accumulator = 0f;
            _interpolation = 0f;
            return _configured;
        }

        public void Disable()
        {
            Interlocked.Increment(ref _configurationVersion);
            _configured = false;
            _pieces.Clear();
            Hidden = true;
            _needsReset = true;
            _accumulator = 0f;
            _interpolation = 0f;
        }

        protected override void OnWorldChanged(WorldControl newWorld, WorldControl prevWorld)
        {
            base.OnWorldChanged(newWorld, prevWorld);
            _needsReset = true;
            _accumulator = 0f;
        }

        public override void Update(GameTime gameTime)
        {
            // The owning PlayerObject invokes UpdateAfterOwner after movement and skeleton updates.
            // Updating here would pin the cloth to the previous frame's back-bone transform.
        }

        internal void UpdateAfterOwner(GameTime gameTime)
        {
            if (Status != GameControlStatus.Ready || !_configured || Hidden)
                return;

            PlayerObject player = GetOwner();
            if (player == null || TotalAlpha <= 0.001f)
                return;

            if (!TryGetAnchor(player, AnchorBone, out _, out Vector3 anchorPosition, out float ownerScale))
            {
                _needsReset = true;
                return;
            }

            if (_needsReset ||
                Vector3.DistanceSquared(anchorPosition, _lastAnchor) > TeleportResetDistanceSquared ||
                MathF.Abs(ownerScale - _lastOwnerScale) > 0.08f)
            {
                ResetAll(player);
                anchorPosition = _lastAnchor;
            }

            float elapsed = MathHelper.Clamp(
                (float)gameTime.ElapsedGameTime.TotalSeconds,
                0f,
                LegacyTickSeconds * MaxCatchUpSteps);
            _simulationTime += elapsed;
            _accumulator += elapsed;

            int ticks = 0;
            while (_accumulator >= LegacyTickSeconds && ticks < MaxCatchUpSteps)
            {
                Tick(player);
                _accumulator -= LegacyTickSeconds;
                ticks++;
            }

            if (ticks == MaxCatchUpSteps)
                _accumulator = MathF.Min(_accumulator, LegacyTickSeconds);

            _interpolation = MathHelper.Clamp(_accumulator / LegacyTickSeconds, 0f, 1f);
            _lastAnchor = anchorPosition;
            _lastOwnerScale = ownerScale;
        }

        private void Tick(PlayerObject player)
        {
            _wind = MathHelper.Clamp(_wind + NextRandom(-0.1f, 0.099f), -0.2f, 1f);

            for (int i = 0; i < _pieces.Count; i++)
                _pieces[i].BeginTick();

            for (int substep = 0; substep < SolverSubsteps; substep++)
            {
                for (int pieceIndex = 0; pieceIndex < _pieces.Count; pieceIndex++)
                {
                    ClothPiece piece = _pieces[pieceIndex];
                    if (!piece.Step(
                        player,
                        _wind,
                        _simulationTime,
                        SolverSubstepSeconds))
                    {
                        _needsReset = true;
                        ResetAll(player);
                        return;
                    }
                }
            }
        }

        private void ResetAll(PlayerObject player)
        {
            for (int i = 0; i < _pieces.Count; i++)
                _pieces[i].Reset(player);

            if (TryGetAnchor(player, AnchorBone, out _, out Vector3 anchor, out float ownerScale))
            {
                _lastAnchor = anchor;
                _lastOwnerScale = ownerScale;
            }

            _needsReset = false;
            _accumulator = 0f;
            _interpolation = 0f;
            _wind = 0f;
        }

        public override void Draw(GameTime gameTime)
        {
            // Cloth uses alpha-tested textures and is intentionally rendered in the after pass.
        }

        public override void DrawAfter(GameTime gameTime)
        {
            if (!Visible || !_configured || _pieces.Count == 0)
                return;

            PlayerObject player = GetOwner();
            if (player == null || player.Hidden || player.World == null)
                return;

            float alpha = TotalAlpha;
            if (alpha <= 0.001f)
                return;

            GraphicsDevice gd = GraphicsDevice;
            BlendState previousBlend = gd.BlendState;
            DepthStencilState previousDepth = gd.DepthStencilState;
            RasterizerState previousRasterizer = gd.RasterizerState;
            SamplerState previousSampler = gd.SamplerStates[0];

            AlphaTestEffect effect = GraphicsManager.Instance.AlphaTestEffect3D;
            Matrix previousWorld = effect.World;
            Matrix previousView = effect.View;
            Matrix previousProjection = effect.Projection;
            Texture2D previousTexture = effect.Texture;
            Vector3 previousDiffuse = effect.DiffuseColor;
            float previousAlpha = effect.Alpha;
            bool previousVertexColor = effect.VertexColorEnabled;
            int previousReferenceAlpha = effect.ReferenceAlpha;

            try
            {
                gd.BlendState = alpha < 0.999f
                    ? BlendState.NonPremultiplied
                    : BlendState.Opaque;
                gd.DepthStencilState = DepthStencilState.Default;
                gd.RasterizerState = RasterizerState.CullNone;
                gd.SamplerStates[0] = SamplerState.LinearWrap;

                effect.World = Matrix.Identity;
                effect.View = Camera.Instance.View;
                effect.Projection = Camera.Instance.Projection;
                effect.DiffuseColor = Vector3.One;
                effect.Alpha = alpha;
                effect.VertexColorEnabled = true;
                effect.ReferenceAlpha = 8;

                for (int i = 0; i < _pieces.Count; i++)
                    _pieces[i].Draw(gd, effect, _interpolation);
            }
            finally
            {
                effect.World = previousWorld;
                effect.View = previousView;
                effect.Projection = previousProjection;
                effect.Texture = previousTexture;
                effect.DiffuseColor = previousDiffuse;
                effect.Alpha = previousAlpha;
                effect.VertexColorEnabled = previousVertexColor;
                effect.ReferenceAlpha = previousReferenceAlpha;

                gd.BlendState = previousBlend;
                gd.DepthStencilState = previousDepth;
                gd.RasterizerState = previousRasterizer;
                gd.SamplerStates[0] = previousSampler;
            }

            DrawChildrenAfterOnly(gameTime);
        }

        private PlayerObject GetOwner()
        {
            if (Parent is WingObject wing && wing.Parent is PlayerObject player)
                return player;
            return null;
        }

        private float NextRandom(float minimum, float maximum)
        {
            _randomState ^= _randomState << 13;
            _randomState ^= _randomState >> 17;
            _randomState ^= _randomState << 5;
            float normalized = (_randomState & 0x00FFFFFFu) / 16777215f;
            return MathHelper.Lerp(minimum, maximum, normalized);
        }

        private static async Task<Texture2D> ResolveTextureAsync(
            IReadOnlyList<string> candidates,
            BMD model,
            int preferredMeshIndex)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                Texture2D texture = await TryLoadTextureAsync(candidates[i]).ConfigureAwait(false);
                if (texture != null)
                    return texture;
            }

            if (model?.Meshes == null || model.Meshes.Length == 0)
                return null;

            if ((uint)preferredMeshIndex < (uint)model.Meshes.Length)
            {
                string preferred = BMDLoader.Instance.GetTexturePath(model, model.Meshes[preferredMeshIndex].TexturePath);
                Texture2D texture = await TryLoadTextureAsync(preferred).ConfigureAwait(false);
                if (texture != null)
                    return texture;
            }

            // The cloth surface is usually the largest mesh in the legacy cape BMD.
            bool[] attempted = new bool[model.Meshes.Length];
            if ((uint)preferredMeshIndex < (uint)attempted.Length)
                attempted[preferredMeshIndex] = true;

            for (int pass = 0; pass < model.Meshes.Length; pass++)
            {
                int bestIndex = -1;
                int bestVertexCount = -1;
                for (int meshIndex = 0; meshIndex < model.Meshes.Length; meshIndex++)
                {
                    if (attempted[meshIndex])
                        continue;
                    int vertexCount = model.Meshes[meshIndex].Vertices?.Length ?? 0;
                    if (vertexCount > bestVertexCount)
                    {
                        bestVertexCount = vertexCount;
                        bestIndex = meshIndex;
                    }
                }

                if (bestIndex < 0)
                    break;

                attempted[bestIndex] = true;
                string path = BMDLoader.Instance.GetTexturePath(model, model.Meshes[bestIndex].TexturePath);
                Texture2D texture = await TryLoadTextureAsync(path).ConfigureAwait(false);
                if (texture != null)
                    return texture;
            }

            return null;
        }

        private static async Task<Texture2D> TryLoadTextureAsync(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            try
            {
                return await TextureLoader.Instance.PrepareAndGetTexture(path).ConfigureAwait(false);
            }
            catch
            {
                return null;
            }
        }

        private static bool TryGetAnchor(
            PlayerObject player,
            int boneIndex,
            out Matrix worldMatrix,
            out Vector3 position,
            out float ownerScale)
        {
            worldMatrix = Matrix.Identity;
            position = Vector3.Zero;
            ownerScale = 1f;

            Matrix[] bones = player?.GetBoneTransforms();
            if (player == null || bones == null || bones.Length == 0)
                return false;

            int safeBone = Math.Clamp(boneIndex, 0, bones.Length - 1);
            worldMatrix = bones[safeBone] * player.WorldPosition;
            position = worldMatrix.Translation;
            ownerScale = (worldMatrix.Right.Length() + worldMatrix.Up.Length() + worldMatrix.Backward.Length()) / 3f;
            if (!float.IsFinite(ownerScale) || ownerScale <= 0.0001f)
                ownerScale = MathF.Max(0.01f, player.TotalScale);
            return true;
        }

        private sealed class ClothPiece
        {
            private readonly int _columns;
            private readonly int _rows;
            private readonly int _anchorBone;
            private readonly Vector3 _offset;
            private readonly float _width;
            private readonly float _height;
            private readonly bool _shortShoulders;
            private readonly bool _heavy;
            private readonly float _lateralForce;
            private readonly ClothParticle[] _particles;
            private readonly Vector3[] _previousTickPositions;
            private readonly Vector3[] _substepStartPositions;
            private readonly Vector3[] _forces;
            private readonly ClothConstraint[] _constraints;
            private readonly SphereDefinition[] _sphereDefinitions;
            private readonly VertexPositionColorTexture[] _renderVertices;
            private readonly short[] _indices;
            private readonly Texture2D _texture;
            private readonly float _maximumRestLength;

            private ClothPiece(
                int columns,
                int rows,
                int anchorBone,
                Vector3 offset,
                float width,
                float height,
                bool shortShoulders,
                bool heavy,
                float lateralForce,
                Texture2D texture,
                SphereDefinition[] sphereDefinitions)
            {
                _columns = columns;
                _rows = rows;
                _anchorBone = anchorBone;
                _offset = offset;
                _width = width;
                _height = height;
                _shortShoulders = shortShoulders;
                _heavy = heavy;
                _lateralForce = lateralForce;
                _texture = texture;
                _sphereDefinitions = sphereDefinitions;

                _particles = new ClothParticle[columns * rows];
                _previousTickPositions = new Vector3[_particles.Length];
                _substepStartPositions = new Vector3[_particles.Length];
                _forces = new Vector3[_particles.Length];
                _renderVertices = new VertexPositionColorTexture[_particles.Length];
                _indices = BuildIndices(columns, rows);
                _constraints = BuildConstraints(columns, rows, width, height, shortShoulders, out _maximumRestLength);
            }

            public static ClothPiece CreateMain(CapeClothProfile profile, Texture2D texture)
            {
                SphereDefinition[] spheres =
                {
                    new(TorsoBone, new Vector3(-10f, -10f, -10f), profile.UpperSphereRadius),
                    new(TorsoBone, new Vector3( 10f, -10f, -10f), profile.UpperSphereRadius),
                    new(TorsoBone, new Vector3(-10f, -10f,  20f), profile.LowerSphereRadius),
                    new(TorsoBone, new Vector3( 10f, -10f,  20f), profile.LowerSphereRadius)
                };

                return new ClothPiece(
                    MainColumns,
                    MainRows,
                    AnchorBone,
                    profile.Offset,
                    profile.Width,
                    profile.Height,
                    true,
                    profile.Heavy,
                    0f,
                    texture,
                    spheres);
            }

            public static ClothPiece CreateRibbon(
                Vector3 offset,
                float width,
                float height,
                Texture2D texture,
                float lateralForce,
                float lowerSphereRadius,
                float torsoSphereRadius)
            {
                SphereDefinition[] spheres =
                {
                    new(LowerTorsoBone, new Vector3(0f, -15f, -20f), lowerSphereRadius),
                    new(TorsoBone, Vector3.Zero, torsoSphereRadius)
                };

                return new ClothPiece(
                    2,
                    5,
                    AnchorBone,
                    offset,
                    width,
                    height,
                    false,
                    false,
                    lateralForce,
                    texture,
                    spheres);
            }

            public void Reset(PlayerObject player)
            {
                if (!TryGetAnchor(player, _anchorBone, out Matrix anchor, out _, out _))
                    return;

                for (int row = 0; row < _rows; row++)
                {
                    float rowT = row / (float)(_rows - 1);
                    float rowWidth = _shortShoulders
                        ? _width * MathHelper.Lerp(0.60f, 1f, rowT)
                        : _width;

                    for (int column = 0; column < _columns; column++)
                    {
                        float columnT = _columns == 1 ? 0.5f : column / (float)(_columns - 1);
                        float x = MathHelper.Lerp(-rowWidth * 0.5f, rowWidth * 0.5f, columnT);
                        float edge = 2f * MathF.Abs(columnT - 0.5f);
                        float curve = _shortShoulders ? 10f * edge * edge : 0f;
                        // SourceMain CPhysicsCloth::Create applies the fxPos/fyPos/fzPos
                        // offsets only to the pinned top row; the sheet body hangs from
                        // y=20 with z=-height*row. Adding the offsets here pushed the cape
                        // too far off the back.
                        float y = 20f - curve;
                        float z = -_height * rowT;
                        Vector3 world = Vector3.Transform(ToBoneLocal(new Vector3(x, y, z)), anchor);
                        int index = row * _columns + column;
                        _particles[index] = new ClothParticle
                        {
                            Position = world,
                            Velocity = Vector3.Zero,
                            Fixed = row == 0
                        };
                        _previousTickPositions[index] = world;
                    }
                }
            }

            public void BeginTick()
            {
                for (int i = 0; i < _particles.Length; i++)
                    _previousTickPositions[i] = _particles[i].Position;
            }

            public bool Step(
                PlayerObject player,
                float wind,
                float simulationTime,
                float deltaTime)
            {
                if (!TryGetAnchor(player, _anchorBone, out Matrix anchor, out Vector3 anchorPosition, out float ownerScale))
                    return false;

                UpdateFixedVertices(anchor);

                Matrix ownerRotation = Matrix.CreateFromQuaternion(MathUtils.AngleQuaternion(player.Angle));
                Vector3 backward = Vector3.TransformNormal(Vector3.UnitY, ownerRotation);
                backward.Z = 0f;
                if (backward.LengthSquared() < 0.0001f)
                    backward = Vector3.UnitY;
                else
                    backward.Normalize();

                Vector3 side = Vector3.Cross(Vector3.UnitZ, backward);
                if (side.LengthSquared() < 0.0001f)
                    side = Vector3.UnitX;
                else
                    side.Normalize();

                float gravityMultiplier = _heavy ? 180f : 100f;
                float windMultiplier = _heavy ? 0.62f : 1f;
                float movingGust = simulationTime / 0.4f;

                for (int i = 0; i < _particles.Length; i++)
                {
                    ref ClothParticle particle = ref _particles[i];
                    _substepStartPositions[i] = particle.Position;
                    _forces[i] = Vector3.Zero;
                    if (particle.Fixed)
                        continue;

                    float normalizedIndex = i / (float)Math.Max(1, _particles.Length - 1);
                    float gustDistance = MathF.Abs(normalizedIndex - (movingGust - MathF.Floor(movingGust)));
                    gustDistance = MathF.Min(gustDistance, 1f - gustDistance);
                    float localGust = 0.62f + MathF.Exp(-gustDistance * gustDistance * 36f) * 0.78f;
                    float irregularity = MathF.Sin(
                        i * 12.9898f +
                        simulationTime * 7.13f +
                        _lateralForce * 0.031f);
                    localGust *= 0.92f + irregularity * 0.08f;

                    Vector3 force = new(0f, 0f, -Gravity * ParticleMass * gravityMultiplier);
                    force += backward * (wind * windMultiplier * localGust * ParticleMass * 420f);
                    force += side * (_lateralForce * ParticleMass);
                    force -= particle.Velocity * 0.01f;
                    _forces[i] = force;
                }

                ApplySpringForces(ownerScale);

                for (int i = 0; i < _particles.Length; i++)
                {
                    ref ClothParticle particle = ref _particles[i];
                    if (particle.Fixed)
                        continue;

                    particle.Velocity += _forces[i] * (InverseParticleMass * deltaTime);
                    float speedSquared = particle.Velocity.LengthSquared();
                    if (speedSquared > MaximumParticleSpeed * MaximumParticleSpeed)
                        particle.Velocity *= MaximumParticleSpeed / MathF.Sqrt(speedSquared);
                    particle.Position += particle.Velocity * deltaTime;
                }

                SolveSphereCollisions(player, ownerScale, backward);
                for (int iteration = 0; iteration < ConstraintIterations; iteration++)
                {
                    SolveConstraints(ownerScale);
                    SolveSphereCollisions(player, ownerScale, backward);
                    UpdateFixedVertices(anchor);
                }
                ReconcileVelocities(deltaTime);

                float maximumDistance = MathF.Max(_maximumRestLength, MathF.Max(_width, _height)) * ConstraintFailureMultiplier * ownerScale;
                float maximumDistanceSquared = maximumDistance * maximumDistance;
                for (int i = 0; i < _particles.Length; i++)
                {
                    ref ClothParticle particle = ref _particles[i];
                    if (!IsFinite(particle.Position) || !IsFinite(particle.Velocity))
                        return false;
                    if (Vector3.DistanceSquared(particle.Position, anchorPosition) > maximumDistanceSquared)
                        return false;
                }

                return true;
            }

            private void UpdateFixedVertices(Matrix anchor)
            {
                for (int column = 0; column < _columns; column++)
                {
                    float columnT = _columns == 1 ? 0.5f : column / (float)(_columns - 1);
                    float topWidth = _shortShoulders ? _width * 0.60f : _width;
                    float x = _offset.X + MathHelper.Lerp(-topWidth * 0.5f, topWidth * 0.5f, columnT);
                    float edge = 2f * MathF.Abs(columnT - 0.5f);
                    float curve = _shortShoulders ? 10f * edge * edge : 0f;
                    // SourceMain SetFixedVertices pins the top row at y=fyPos (not 20f).
                    Vector3 local = new(x, _offset.Y - curve, _offset.Z);
                    int index = column;
                    Vector3 target = Vector3.Transform(ToBoneLocal(local), anchor);
                    ref ClothParticle particle = ref _particles[index];
                    particle.Position = target;
                    particle.Velocity = Vector3.Zero;
                }
            }

            private void ApplySpringForces(float ownerScale)
            {
                float stiffness = _columns <= 2 ? RibbonSpringStiffness : SpringStiffness;
                for (int i = 0; i < _constraints.Length; i++)
                {
                    ref readonly ClothConstraint constraint = ref _constraints[i];
                    ref ClothParticle a = ref _particles[constraint.A];
                    ref ClothParticle b = ref _particles[constraint.B];
                    Vector3 delta = b.Position - a.Position;
                    float distanceSquared = delta.LengthSquared();
                    if (distanceSquared <= 0.0000001f)
                        continue;

                    float distance = MathF.Sqrt(distanceSquared);
                    float restLength = constraint.RestLength * ownerScale;
                    if (distance <= restLength)
                        continue;

                    Vector3 direction = delta / distance;
                    Vector3 springForce = direction * ((distance - restLength) * stiffness * ParticleMass);
                    if (!a.Fixed)
                        _forces[constraint.A] += springForce;
                    if (!b.Fixed)
                        _forces[constraint.B] -= springForce;
                }
            }

            private void ReconcileVelocities(float deltaTime)
            {
                if (deltaTime <= 0f)
                    return;

                float inverseDelta = 1f / deltaTime;
                for (int i = 0; i < _particles.Length; i++)
                {
                    ref ClothParticle particle = ref _particles[i];
                    if (particle.Fixed)
                    {
                        particle.Velocity = Vector3.Zero;
                        continue;
                    }

                    Vector3 correctedVelocity = (particle.Position - _substepStartPositions[i]) * inverseDelta;
                    particle.Velocity = Vector3.Lerp(particle.Velocity, correctedVelocity, 0.72f);
                    float speedSquared = particle.Velocity.LengthSquared();
                    if (speedSquared > MaximumParticleSpeed * MaximumParticleSpeed)
                        particle.Velocity *= MaximumParticleSpeed / MathF.Sqrt(speedSquared);
                }
            }

            private static Vector3 ToBoneLocal(Vector3 clothLocal) =>
                new(clothLocal.Z, -clothLocal.Y, clothLocal.X);

            private void SolveConstraints(float ownerScale)
            {
                for (int i = 0; i < _constraints.Length; i++)
                {
                    ref ClothConstraint constraint = ref _constraints[i];
                    ref ClothParticle a = ref _particles[constraint.A];
                    ref ClothParticle b = ref _particles[constraint.B];
                    Vector3 delta = b.Position - a.Position;
                    float distanceSquared = delta.LengthSquared();
                    if (distanceSquared <= 0.0000001f)
                        continue;

                    float distance = MathF.Sqrt(distanceSquared);
                    float minimumLength = constraint.MinLength * ownerScale;
                    float maximumLength = constraint.MaxLength * ownerScale;
                    float targetLength;
                    if (distance > maximumLength)
                        targetLength = maximumLength;
                    else if (distance < minimumLength)
                        targetLength = minimumLength;
                    else
                        continue;

                    float correctionScale = (distance - targetLength) / distance;
                    Vector3 correction = delta * correctionScale;

                    if (a.Fixed && b.Fixed)
                        continue;
                    if (a.Fixed)
                    {
                        b.Position -= correction;
                    }
                    else if (b.Fixed)
                    {
                        a.Position += correction;
                    }
                    else
                    {
                        Vector3 half = correction * 0.5f;
                        a.Position += half;
                        b.Position -= half;
                    }
                }
            }

            private void SolveSphereCollisions(PlayerObject player, float ownerScale, Vector3 fallbackDirection)
            {
                Matrix[] bones = player.GetBoneTransforms();
                if (bones == null || bones.Length == 0)
                    return;

                for (int sphereIndex = 0; sphereIndex < _sphereDefinitions.Length; sphereIndex++)
                {
                    SphereDefinition definition = _sphereDefinitions[sphereIndex];
                    int boneIndex = Math.Clamp(definition.BoneIndex, 0, bones.Length - 1);
                    Matrix sphereMatrix = bones[boneIndex] * player.WorldPosition;
                    Vector3 center = Vector3.Transform(definition.LocalCenter, sphereMatrix);
                    float radius = definition.Radius * ownerScale;
                    float radiusSquared = radius * radius;

                    for (int particleIndex = _columns; particleIndex < _particles.Length; particleIndex++)
                    {
                        ref ClothParticle particle = ref _particles[particleIndex];
                        Vector3 offset = particle.Position - center;
                        float distanceSquared = offset.LengthSquared();
                        if (distanceSquared >= radiusSquared)
                            continue;

                        Vector3 direction;
                        if (distanceSquared <= 0.000001f)
                        {
                            direction = fallbackDirection;
                        }
                        else
                        {
                            direction = offset / MathF.Sqrt(distanceSquared);
                        }

                        particle.Position = center + direction * radius;
                        float inwardVelocity = Vector3.Dot(particle.Velocity, direction);
                        if (inwardVelocity < 0f)
                            particle.Velocity -= direction * inwardVelocity;
                    }
                }
            }

            public void Draw(GraphicsDevice graphicsDevice, AlphaTestEffect effect, float interpolation)
            {
                if (_texture == null || _texture.IsDisposed)
                    return;

                Color color = Color.White;
                for (int row = 0; row < _rows; row++)
                {
                    float v = MathF.Min(0.99f, row / (float)(_rows - 1));
                    for (int column = 0; column < _columns; column++)
                    {
                        float u = column / (float)(_columns - 1);
                        int index = row * _columns + column;
                        Vector3 position = Vector3.Lerp(
                            _previousTickPositions[index],
                            _particles[index].Position,
                            interpolation);
                        _renderVertices[index] = new VertexPositionColorTexture(position, color, new Vector2(u, v));
                    }
                }

                effect.Texture = _texture;
                foreach (EffectPass pass in effect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    graphicsDevice.DrawUserIndexedPrimitives(
                        PrimitiveType.TriangleList,
                        _renderVertices,
                        0,
                        _renderVertices.Length,
                        _indices,
                        0,
                        _indices.Length / 3);
                }
            }

            private static ClothConstraint[] BuildConstraints(
                int columns,
                int rows,
                float width,
                float height,
                bool shortShoulders,
                out float maximumRestLength)
            {
                List<ClothConstraint> constraints = new(
                    (columns - 1) * rows +
                    columns * (rows - 1) +
                    2 * (columns - 1) * (rows - 1));
                float maxRestLength = 0f;

                Vector3[] localPositions = new Vector3[columns * rows];
                for (int row = 0; row < rows; row++)
                {
                    float rowT = row / (float)(rows - 1);
                    float rowWidth = shortShoulders ? width * MathHelper.Lerp(0.60f, 1f, rowT) : width;
                    for (int column = 0; column < columns; column++)
                    {
                        float columnT = columns == 1 ? 0.5f : column / (float)(columns - 1);
                        float edge = 2f * MathF.Abs(columnT - 0.5f);
                        float curve = shortShoulders ? 10f * edge * edge : 0f;
                        localPositions[row * columns + column] = new Vector3(
                            MathHelper.Lerp(-rowWidth * 0.5f, rowWidth * 0.5f, columnT),
                            -curve,
                            -height * rowT);
                    }
                }

                void Add(int a, int b)
                {
                    float length = Vector3.Distance(localPositions[a], localPositions[b]);
                    constraints.Add(new ClothConstraint(a, b, length * 0.8f, length));
                    maxRestLength = MathF.Max(maxRestLength, length);
                }

                for (int row = 0; row < rows; row++)
                {
                    for (int column = 0; column < columns; column++)
                    {
                        int index = row * columns + column;
                        if (column + 1 < columns)
                            Add(index, index + 1);
                        if (row + 1 < rows)
                            Add(index, index + columns);
                        if (row + 1 < rows && column + 1 < columns)
                            Add(index, index + columns + 1);
                        // SourceMain creates 72 reverse diagonals for a 10x10 cloth;
                        // the top strip intentionally omits this second shear link.
                        if (row > 0 && row + 1 < rows && column > 0)
                            Add(index, index + columns - 1);
                    }
                }

                maximumRestLength = maxRestLength;
                return constraints.ToArray();
            }

            private static short[] BuildIndices(int columns, int rows)
            {
                short[] indices = new short[(columns - 1) * (rows - 1) * 6];
                int destination = 0;
                for (int row = 0; row < rows - 1; row++)
                {
                    for (int column = 0; column < columns - 1; column++)
                    {
                        short topLeft = checked((short)(row * columns + column));
                        short topRight = checked((short)(topLeft + 1));
                        short bottomLeft = checked((short)(topLeft + columns));
                        short bottomRight = checked((short)(bottomLeft + 1));

                        indices[destination++] = topLeft;
                        indices[destination++] = topRight;
                        indices[destination++] = bottomRight;
                        indices[destination++] = topLeft;
                        indices[destination++] = bottomRight;
                        indices[destination++] = bottomLeft;
                    }
                }

                return indices;
            }

            private static bool IsFinite(Vector3 value) =>
                float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
        }

        private struct ClothParticle
        {
            public Vector3 Position;
            public Vector3 Velocity;
            public bool Fixed;
        }

        private struct ClothConstraint
        {
            public ClothConstraint(int a, int b, float minLength, float maxLength)
            {
                A = a;
                B = b;
                RestLength = maxLength;
                MinLength = minLength;
                MaxLength = maxLength;
            }

            public int A;
            public int B;
            public float RestLength;
            public float MinLength;
            public float MaxLength;
        }

        private readonly struct SphereDefinition
        {
            public SphereDefinition(int boneIndex, Vector3 localCenter, float radius)
            {
                BoneIndex = boneIndex;
                LocalCenter = localCenter;
                Radius = radius;
            }

            public int BoneIndex { get; }
            public Vector3 LocalCenter { get; }
            public float Radius { get; }
        }
    }
}
