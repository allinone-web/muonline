using Client.Main.Models;
using Client.Main.Objects.Particles.Effects;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace Client.Main.Objects.Particles
{
    public class ParticleSystemRegister(ParticleSystem issuer)
    {
        public ParticleSystem System { get; } = issuer;
        public Type ParticleType { get; set; }
        public Vector3 PositionMin { get; set; } = new(0, 0, 0);
        public Vector3 PositionMax { get; set; } = new(0, 0, 0);
        public float ScaleMin { get; set; } = 1f;
        public float ScaleMax { get; set; } = 1f;
        public bool Rotation { get; set; }
        public List<BaseEffect> Effects { get; set; } = [];
        private readonly Queue<Particle> _pool = new();
        private int _effectConfigurationVersion;

        public Particle Emit() => RentParticle();

        public ParticleSystemRegister UseEffect(BaseEffect effect)
        {
            if (effect == null)
                return this;

            Effects.Add(effect);
            _effectConfigurationVersion++;
            return this;
        }

        public ParticleSystemRegister SetPosition(Vector3 min, Vector3 max)
        {
            PositionMin = min;
            PositionMax = max;
            return this;
        }

        public ParticleSystemRegister SetScale(float min, float max)
        {
            ScaleMin = min;
            ScaleMax = max;
            return this;
        }

        public ParticleSystemRegister EnableRotation()
        {
            Rotation = true;
            return this;
        }

        private float RandomScale()
        {
            return (float)(MuGame.Random.NextDouble() * (ScaleMax - ScaleMin) + ScaleMin);
        }

        private Vector3 RandomPosition()
        {
            return new Vector3(
                PositionMin.X + (float)(MuGame.Random.NextDouble() * (PositionMax.X - PositionMin.X)),
                PositionMin.Y + (float)(MuGame.Random.NextDouble() * (PositionMax.Y - PositionMin.Y)),
                PositionMin.Z + (float)(MuGame.Random.NextDouble() * (PositionMax.Z - PositionMin.Z))
            );
        }

        private Vector3 RandomAngle()
        {
            if (!Rotation)
                return Vector3.Zero;

            return new Vector3(
                (float)(MuGame.Random.NextDouble() * MathF.PI * 2),
                (float)(MuGame.Random.NextDouble() * MathF.PI * 2),
                (float)(MuGame.Random.NextDouble() * MathF.PI * 2)
            );
        }

        private Particle RentParticle()
        {
            var position = RandomPosition();
            var angle = RandomAngle();
            var scale = RandomScale();

            Particle particle;
            if (_pool.Count > 0)
            {
                particle = _pool.Dequeue();
                particle.OwnerRegister = this;
                particle.Hidden = false;
            }
            else
            {
                particle = new Particle(ParticleType)
                {
                    OwnerRegister = this
                };
            }

            if (particle.EffectConfigurationVersion != _effectConfigurationVersion)
            {
                particle.Effects = CreateEffectInstances();
                particle.EffectConfigurationVersion = _effectConfigurationVersion;
            }

            bool initializeEffects = particle.Status == GameControlStatus.Ready;
            particle.ConfigureForReuse(position, angle, scale, particle.Effects, initializeEffects);
            return particle;
        }

        private BaseEffect[] CreateEffectInstances()
        {
            if (Effects.Count == 0)
                return Array.Empty<BaseEffect>();

            var result = new BaseEffect[Effects.Count];
            for (int i = 0; i < Effects.Count; i++)
            {
                BaseEffect effect = Effects[i].Copy();
                if (effect is DurationEffect duration)
                    duration.OnExpired = System.RecycleParticle;

                result[i] = effect;
            }

            return result;
        }

        internal void ReturnToPool(Particle particle)
        {
            if (particle == null)
                return;

            // Keep the per-particle effect instances. Init() resets their mutable state on
            // the next rent, avoiding a new effect array and effect objects on every emission.
            particle.Hidden = true;
            particle.OwnerRegister = this;
            _pool.Enqueue(particle);
        }
    }
}
