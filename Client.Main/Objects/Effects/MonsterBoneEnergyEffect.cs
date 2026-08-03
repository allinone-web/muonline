#nullable enable
using Client.Data.BMD;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Models;
using Client.Main.Objects.Effects.Particles;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Threading.Tasks;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// SourceMain5.2 BITMAP_ENERGY emitted from animated monster bones.
    /// </summary>
    public sealed class MonsterBoneEnergyEffect : SourceParticleSystem
    {
        private const float LegacyFramesPerSecond = 25f;

        private Texture2D? _texture;
        private Vector2 _textureCenter;
        private float _legacyAccumulator;
        private bool _emittedOnce;

        public ModelObject? SourceModel { get; set; }
        public int[] SourceBoneIndices { get; set; } = Array.Empty<int>();
        public Vector3 SourceOffset { get; set; }
        public int RequiredAction { get; set; } = -1;
        public bool EmitOnlyDuringAttack { get; set; }
        public bool EmitOnce { get; set; }
        public float EmissionChancePercent { get; set; } = 4f;
        public Vector3 ParticleLight { get; set; } = Vector3.One;

        protected override Texture2D? ParticleTexture => _texture;
        protected override Vector2 ParticleTextureCenter => _textureCenter;

        public MonsterBoneEnergyEffect(int capacity = 128)
            : base(capacity)
        {
            ScaleGrowth = 0f;
            MaxDistance = 2500f;
        }

        public override async Task LoadContent()
        {
            _texture = await TextureLoader.Instance.PrepareAndGetTexture("Effect/Thunder01.jpg");
            if (_texture != null)
                _textureCenter = new Vector2(_texture.Width * 0.5f, _texture.Height * 0.5f);
        }

        protected override void OnBeforeParticlesUpdated(float dt)
        {
            ModelObject? parentModel = SourceModel ?? Parent as ModelObject;
            if (parentModel == null || parentModel.Model == null ||
                parentModel.Hidden || parentModel is MonsterObject { IsDead: true } ||
                RequiredAction >= 0 && parentModel.CurrentAction != RequiredAction ||
                EmitOnlyDuringAttack && parentModel.CurrentAction != (int)MonsterActionType.Attack1 &&
                parentModel.CurrentAction != (int)MonsterActionType.Attack2)
                return;

            Matrix[] bones = parentModel.GetBoneTransforms();
            if (bones == null || bones.Length == 0)
                return;

            if (EmitOnce)
            {
                if (_emittedOnce)
                    return;

                _emittedOnce = true;
                if (SourceBoneIndices.Length > 0)
                {
                    for (int i = 0; i < SourceBoneIndices.Length; i++)
                        EmitAtBone(parentModel, bones, SourceBoneIndices[i]);
                }
                else
                {
                    for (int i = 0; i < bones.Length; i++)
                        if (IsRenderableSourceBone(parentModel, i))
                            EmitAtBone(parentModel, bones, i);
                }
                return;
            }

            _legacyAccumulator += dt * LegacyFramesPerSecond;
            int ticks = Math.Min((int)_legacyAccumulator, 5);
            if (ticks <= 0)
                return;

            _legacyAccumulator -= ticks;
            for (int tick = 0; tick < ticks; tick++)
            {
                if (SourceBoneIndices.Length > 0)
                {
                    for (int i = 0; i < SourceBoneIndices.Length; i++)
                    {
                        if (MuGame.Random.Next(100) < EmissionChancePercent)
                            EmitAtBone(parentModel, bones, SourceBoneIndices[i]);
                    }
                }
                else
                {
                    for (int i = 0; i < bones.Length; i++)
                    {
                        if (IsRenderableSourceBone(parentModel, i) &&
                            MuGame.Random.Next(100) < EmissionChancePercent)
                            EmitAtBone(parentModel, bones, i);
                    }
                }
            }
        }

        private void EmitAtBone(ModelObject parentModel, Matrix[] bones, int boneIndex)
        {
            if (boneIndex < 0 || boneIndex >= bones.Length)
                return;

            Vector3 localPosition = Vector3.Transform(SourceOffset, bones[boneIndex]);
            Vector3 worldPosition = Vector3.Transform(localPosition, parentModel.WorldPosition);
            CreateParticle(0, worldPosition, Vector3.Zero, ParticleLight);
        }

        private static bool IsRenderableSourceBone(ModelObject parentModel, int boneIndex)
        {
            if (boneIndex < 0 || boneIndex >= parentModel.Model.Bones.Length)
                return false;

            if (ReferenceEquals(parentModel.Model.Bones[boneIndex], BMDTextureBone.Dummy))
                return false;

            return boneIndex < 15 || boneIndex > 20 && (boneIndex < 27 || boneIndex > 32);
        }

        protected override void OnParticleCreated(ref SourceParticle particle)
        {
            particle.LifeTime = 10f / LegacyFramesPerSecond;
            particle.MaxLifeTime = particle.LifeTime;
            particle.Scale = (6 + MuGame.Random.Next(8)) * 0.1f;
            particle.Rotation = MathHelper.ToRadians(MuGame.Random.Next(360));
            particle.Gravity = 20f;
            particle.EnableMove = false;
        }

        protected override void UpdateLiveParticle(ref SourceParticle particle, float dt)
        {
            particle.Rotation += MathHelper.ToRadians(20f) * dt * LegacyFramesPerSecond;
        }
    }
}
