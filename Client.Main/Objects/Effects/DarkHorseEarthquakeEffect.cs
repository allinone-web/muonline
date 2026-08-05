#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Core.Utilities;
using Client.Main.Graphics;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Dark Horse Earthquake (Earth Shake) visual sequence.
    ///
    /// SourceMain5.2 renders this from the horse attack animation: a shrinking
    /// ShockWave, six ground stones around the horse at animation frame 8..9.5,
    /// then the EarthQuake model chain at frame 19. The horse attack itself is
    /// selected by SkillDefinitions as PLAYER_ATTACK_DARKHORSE.
    /// </summary>
    public sealed class DarkHorseEarthquakeEffect : EffectObject
    {
        private const string ShockWaveModelPath = "Effect/shockwave_ground01.bmd";
        private const string GroundStonePath = "Skill/groundStone.bmd";
        private const string GroundStone2Path = "Skill/groundStone2.bmd";

        private const float ShockWaveLifeFrames = 30f;
        private const float ShockWaveStartTiles = 20f;
        private const float ShockWaveScalePerTile =
            0.807f * Constants.TERRAIN_SCALE / 195f;
        private const float StoneSpawnFrame = 8f;
        private const float FurySpawnFrame = 19f;
        private const float SequenceLifeFrames = 30f;
        // SourceMain's WeaponLevel is a render-frame counter, not the skill
        // animation frame. Replaying that counter in this network-timed effect
        // pushed the six stones several tiles past the visible EarthQuake core.
        // Keep them on the same outer radius as the original EarthQuake bursts
        // (rand() % 150 + 100 => 100..249 world units).
        private const float StoneRadius = 250f;

        private readonly WalkerObject _caster;
        private Vector3 _origin;
        private string _groundStonePath = GroundStonePath;
        private string _groundStone2Path = GroundStone2Path;
        private float _elapsedFrames;
        private float _nextShockWaveFrame;
        private bool _initialized;
        private bool _stonesSpawned;
        private bool _furySpawned;

        public DarkHorseEarthquakeEffect(WalkerObject caster)
        {
            _caster = caster ?? throw new ArgumentNullException(nameof(caster));

            IsTransparent = true;
            AffectedByTransparency = true;
            BlendState = BlendState.Additive;
            DepthState = DepthStencilState.DepthRead;
            BoundingBoxLocal = new BoundingBox(
                new Vector3(-1800f, -1800f, -100f),
                new Vector3(1800f, 1800f, 300f));
        }

        public override async Task LoadContent()
        {
            await base.LoadContent();

            _groundStonePath = await ResolveModelPath(
                GroundStonePath,
                "Skill/GroundStone.bmd",
                "Skill/groundStone01.bmd");
            _groundStone2Path = await ResolveModelPath(
                GroundStone2Path,
                "Skill/GroundStone2.bmd",
                "Skill/groundStone02.bmd",
                _groundStonePath);
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (Status == GameControlStatus.NonInitialized)
                _ = Load();

            if (Status != GameControlStatus.Ready)
                return;

            if (_caster.Status == GameControlStatus.Disposed || _caster.World == null)
            {
                RemoveSelf();
                return;
            }

            if (!_initialized)
            {
                _origin = _caster.WorldPosition.Translation;
                SetGroundHeight(ref _origin, 3f);
                _nextShockWaveFrame = 0f;
                _initialized = true;
            }

            float factor = FPSCounter.Instance.FPS_ANIMATION_FACTOR;

            // SourceMain's LastHorseWaveEffect is a 400 ms interval. At the
            // legacy 25 FPS animation clock that is ten animation frames.
            if (_elapsedFrames >= _nextShockWaveFrame)
            {
                SpawnShockWave();
                _nextShockWaveFrame += 10f;
            }

            if (!_stonesSpawned && _elapsedFrames >= StoneSpawnFrame)
            {
                _stonesSpawned = true;
                SpawnGroundStones();
                // Match the impact shake used by the original area skills.
                Camera.Instance.Shake(3.5f, 0.35f, 20f);
            }

            if (!_furySpawned && _elapsedFrames >= FurySpawnFrame)
            {
                _furySpawned = true;
                SpawnFuryStrike();
            }

            _elapsedFrames += factor;
            if (_elapsedFrames >= SequenceLifeFrames)
                RemoveSelf();
        }

        private void SpawnShockWave()
        {
            if (World == null)
                return;

            var wave = new DarkHorseShockWaveModel
            {
                Position = _origin,
                Angle = Vector3.Zero,
                Scale = ShockWaveStartTiles * ShockWaveScalePerTile
            };

            World.Objects.Add(wave);
            _ = wave.Load();
        }

        private void SpawnGroundStones()
        {
            if (World == null)
                return;

            // SourceMain starts at a random heading and emits six stones exactly
            // 60 degrees apart. The original render-frame WeaponLevel counter
            // is intentionally not reused here; the effect is network-timed and
            // its visible core uses the 100..249-unit EarthQuake radius.
            float startAngle = MuGame.Random.Next(0, 360);
            for (int i = 0; i < 6; i++)
            {
                // SourceMain increments the random angle before the first stone.
                float angle = startAngle + (i + 1) * 60f;
                Vector3 offset = MathUtils.VectorRotate(
                    new Vector3(0f, StoneRadius, 0f),
                    MathUtils.AngleMatrix(new Vector3(0f, 0f, angle)));
                Vector3 position = _origin + offset;
                SetGroundHeight(ref position, 2f);

                string modelPath = (MuGame.Random.Next(2) == 0)
                    ? _groundStonePath
                    : _groundStone2Path;
                var stone = new DarkHorseGroundStoneModel(modelPath, 40f)
                {
                    Position = position,
                    Angle = _caster.Angle,
                    Scale = 1f
                };

                World.Objects.Add(stone);
                _ = stone.Load();
            }
        }

        private void SpawnFuryStrike()
        {
            if (World == null)
                return;

            // SourceMain calls RenderSkillFuryStrike with Kind=2 here: the
            // EarthQuake chain remains, while the generic Rageful Blow tail,
            // sparks, wave and its three sounds are deliberately suppressed.
            var fury = new RagefulBlowEffect(
                _caster,
                targetPosition: null,
                includeTail: false,
                includeImpactSparks: false,
                includeWave: false,
                playImpactSound: false,
                playTailSound: false,
                playSecondarySound: false,
                subTypeOverride: 0)
            {
                Position = _origin,
                Angle = _caster.Angle
            };

            World.Objects.Add(fury);
            _ = fury.Load();
        }

        private void SetGroundHeight(ref Vector3 position, float offset)
        {
            if (World?.Terrain != null)
                position.Z = World.Terrain.RequestTerrainHeight(position.X, position.Y) + offset;
            else
                position.Z += offset;
        }

        private static async Task<string> ResolveModelPath(params string[] candidates)
        {
            for (int i = 0; i < candidates.Length; i++)
            {
                if (await BMDLoader.Instance.AssestExist(candidates[i]))
                    return candidates[i];
            }

            return candidates[0];
        }

        private void RemoveSelf()
        {
            if (Parent != null)
                Parent.Children.Remove(this);
            else
                World?.RemoveObject(this);

            Dispose();
        }

        private sealed class DarkHorseShockWaveModel : ModelObject
        {
            private float _lifeFrames = ShockWaveLifeFrames;

            public DarkHorseShockWaveModel()
            {
                ContinuousAnimation = false;
                IsTransparent = true;
                AffectedByTransparency = true;
                BlendState = BlendState.Additive;
                BlendMeshState = BlendState.Additive;
                BlendMesh = 0;
                DepthState = DepthStencilState.DepthRead;
                LightEnabled = true;
                Alpha = 0.8f;
            }

            public override async Task Load()
            {
                Model = await BMDLoader.Instance.Prepare(ShockWaveModelPath);
                await base.Load();
            }

            public override void Update(GameTime gameTime)
            {
                base.Update(gameTime);
                if (Status != GameControlStatus.Ready)
                    return;

                float factor = FPSCounter.Instance.FPS_ANIMATION_FACTOR;
                Scale = MathF.Max(0f, Scale - ShockWaveScalePerTile * factor);
                _lifeFrames -= factor;
                Alpha = MathHelper.Clamp(_lifeFrames / ShockWaveLifeFrames, 0f, 1f) * 0.8f;

                if (_lifeFrames <= 0f || Scale <= 0f)
                    RemoveSelf();
            }

            private void RemoveSelf()
            {
                if (Parent != null)
                    Parent.Children.Remove(this);
                else
                    World?.RemoveObject(this);

                Dispose();
            }
        }

        private sealed class DarkHorseGroundStoneModel : ModelObject
        {
            private readonly string _modelPath;
            private float _lifeFrames;
            private int _lastAnimationFrame = -1;

            public DarkHorseGroundStoneModel(string modelPath, float lifeFrames)
            {
                _modelPath = modelPath;
                _lifeFrames = lifeFrames;
                ContinuousAnimation = false;
                LightEnabled = true;
                DepthState = DepthStencilState.Default;
            }

            public override async Task Load()
            {
                Model = await BMDLoader.Instance.Prepare(_modelPath);
                await base.Load();
            }

            public override void Update(GameTime gameTime)
            {
                base.Update(gameTime);
                if (Status != GameControlStatus.Ready)
                    return;

                _lifeFrames -= FPSCounter.Instance.FPS_ANIMATION_FACTOR;

                // SourceMain explicitly sets groundStone action 0 to Loop=false.
                // ModelObject's generic animation path intentionally loops normal
                // BMD actions, so catch that wrap locally before frame 0 is drawn a
                // second time (the visible "stone twice" glitch).
                if (Model?.Actions != null && Model.Actions.Length > 0)
                {
                    int actionIndex = Math.Clamp(CurrentAction, 0, Model.Actions.Length - 1);
                    var action = Model.Actions[actionIndex];
                    if (action != null && action.NumAnimationKeys > 1)
                    {
                        if (_lastAnimationFrame >= 0 && CurrentFrame < _lastAnimationFrame)
                        {
                            RemoveSelf();
                            return;
                        }

                        _lastAnimationFrame = CurrentFrame;
                    }
                }

                if (_lifeFrames <= 0f)
                    RemoveSelf();
            }

            private void RemoveSelf()
            {
                if (Parent != null)
                    Parent.Children.Remove(this);
                else
                    World?.RemoveObject(this);

                Dispose();
            }
        }
    }
}
