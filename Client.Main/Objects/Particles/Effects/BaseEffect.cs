using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Particles.Effects
{
    public abstract class BaseEffect
    {
        public Particle Particle { get; set; }
        public abstract void Init();
        public abstract void Update(GameTime time);

        public T GetEffect<T>() where T : BaseEffect
        {
            if (Particle?.Effects == null)
                return null;

            for (int i = 0; i < Particle.Effects.Length; i++)
            {
                if (Particle.Effects[i] is T effect)
                    return effect;
            }

            return null;
        }

        public abstract BaseEffect Copy();
    }
}
