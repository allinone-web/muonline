using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects.Effects.Particles;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// SourceMain5.2 CreateParticle(BITMAP_FIRE) emitter attached to one bone or
    /// to randomly selected bones of the parent model.
    /// </summary>
    public sealed class MonsterBoneFireEffect : SourceParticleSystem
    {
        private const float LegacyFramesPerSecond = 25f;
        private readonly int _capacity;
        private Texture2D _texture;
        private Vector2 _textureCenter;
        private float _emissionAccumulator;

        public int SourceBone { get; set; } = -1;
        public Vector3 SourceOffset { get; set; } = Vector3.Zero;
        public Vector3[] SourceOffsets { get; set; } = Array.Empty<Vector3>();
        public int[] SourceBones { get; set; } = Array.Empty<int>();
        public bool RandomBone { get; set; }
        public float EmissionRate { get; set; }
        public bool EmitAllSourceBones { get; set; }
        public bool EmitOnlyDuringAttack { get; set; }
        public Dictionary<int, int> ActionBoneMap { get; set; } = new();
        public string TexturePath { get; set; } = "Effect/Fire01.jpg";
        public int TextureColumns { get; set; } = 4;
        public int SourceParticleSubType { get; set; }
        public Vector3 ParticleLight { get; set; } = Vector3.One;
        public bool StopOnDeath { get; set; }
        public float ParticleScaleMin { get; set; } = 0.10f;
        public float ParticleScaleMax { get; set; } = 0.13f;
        public float ParticleLifetimeFrames { get; set; } = 24f;

        protected override Texture2D ParticleTexture => _texture;
        protected override Vector2 ParticleTextureCenter => _textureCenter;

        public MonsterBoneFireEffect(int capacity = 128)
            : base(capacity)
        {
            _capacity = capacity;
            BlendState = BlendState.Additive;
            ScaleGrowth = 0f;
            MaxDistance = 2500f;
        }

        public override async Task LoadContent()
        {
            _texture = await TextureLoader.Instance.PrepareAndGetTexture(TexturePath);
            if (_texture != null)
            {
                float columns = Math.Max(1, TextureColumns);
                _textureCenter = new Vector2(_texture.Width / columns * 0.5f, _texture.Height * 0.5f);
            }
        }

        protected override void OnBeforeParticlesUpdated(float dt)
        {
            if (EmissionRate <= 0f || Parent is not ModelObject parentModel ||
                EmitOnlyDuringAttack && parentModel is MonsterObject monster &&
                monster.CurrentAction != (int)MonsterActionType.Attack1 &&
                monster.CurrentAction != (int)MonsterActionType.Attack2 ||
                StopOnDeath && parentModel.CurrentAction == (int)MonsterActionType.Die ||
                parentModel.Model == null || parentModel is MonsterObject { IsDead: true })
                return;

            Matrix[] bones = parentModel.GetBoneTransforms();
            if (bones == null || bones.Length == 0)
                return;

            int actionBone = -1;
            if (ActionBoneMap.Count > 0 &&
                !ActionBoneMap.TryGetValue(parentModel.CurrentAction, out actionBone))
                return;

            _emissionAccumulator += EmissionRate * dt;
            int emit = Math.Min((int)_emissionAccumulator, _capacity - ActiveCount);
            _emissionAccumulator -= emit;

            for (int i = 0; i < emit; i++)
            {
                if (SourceBones.Length > 0)
                {
                    if (EmitAllSourceBones)
                    {
                        for (int bone = 0; bone < SourceBones.Length; bone++)
                            EmitAtBone(bones, parentModel, SourceBones[bone]);
                    }
                    else
                    {
                        EmitAtBone(bones, parentModel, SourceBones[MuGame.Random.Next(SourceBones.Length)]);
                    }
                }
                else
                {
                    int boneIndex = RandomBone
                        ? MuGame.Random.Next(bones.Length)
                        : ActionBoneMap.Count > 0 ? actionBone : SourceBone;
                    EmitAtBone(bones, parentModel, boneIndex);
                }
            }
        }

        private void EmitAtBone(Matrix[] bones, ModelObject parentModel, int boneIndex)
        {
            if (boneIndex < 0 || boneIndex >= bones.Length)
                return;

            Vector3 offset = SourceOffsets.Length > 0
                ? SourceOffsets[MuGame.Random.Next(SourceOffsets.Length)]
                : SourceOffset;
            Vector3 localPosition = Vector3.Transform(offset, bones[boneIndex]);
            Vector3 position = Vector3.Transform(localPosition, parentModel.WorldPosition);
            CreateParticle(0, position, Vector3.Zero, ParticleLight, SourceParticleSubType);
        }

        protected override void OnParticleCreated(ref SourceParticle particle)
        {
            particle.LifeTime = ParticleLifetimeFrames / LegacyFramesPerSecond;
            particle.MaxLifeTime = particle.LifeTime;
            particle.Velocity = new Vector3(
                0f,
                -(32 + MuGame.Random.Next(16)) * 0.1f,
                0f);
            particle.Scale = ParticleScaleMin +
                (float)MuGame.Random.NextDouble() * (ParticleScaleMax - ParticleScaleMin);
            particle.Rotation = MathHelper.ToRadians(MuGame.Random.Next(360));
        }

        protected override void UpdateLiveParticle(ref SourceParticle particle, float dt)
        {
            float frameFactor = dt * LegacyFramesPerSecond;
            particle.Gravity += 0.004f * frameFactor;
            particle.Scale += particle.Gravity * frameFactor;
            particle.Velocity *= MathF.Pow(0.98f, frameFactor);
            particle.Position.Z += particle.Gravity * 10f * frameFactor;

            float lifeInFrames = particle.LifeTime * LegacyFramesPerSecond;
            particle.Frame = MathHelper.Clamp((int)((23f - lifeInFrames) / 6f), 0, 3);
        }

        protected override Color GetParticleColor(in SourceParticle particle, float lifeRatio) =>
            new(particle.Light.X, particle.Light.Y, particle.Light.Z, lifeRatio);

        protected override Rectangle? GetParticleSourceRectangle(Texture2D texture, in SourceParticle particle) =>
            TextureColumns <= 1
                ? null
                : new Rectangle(
                    particle.Frame * (texture.Width / TextureColumns),
                    0,
                    texture.Width / TextureColumns,
                    texture.Height);
    }
}
