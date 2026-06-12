#nullable enable
using Client.Main.Objects;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Effects.Particles
{
    public struct SourceParticle
    {
        public bool Live;
        public int Type;
        public int TexType;
        public int SubType;
        public float Scale;
        public Vector3 Position;
        public Vector3 Angle;
        public Vector3 Light;
        public float Alpha;
        public float LifeTime;
        public float MaxLifeTime;
        public Vector3 Target;
        public float Rotation;
        public int Frame;
        public bool EnableMove;
        public float Gravity;
        public Vector3 Velocity;
        public Vector3 TurningForce;
        public Vector3 StartPosition;
        public WorldObject? Owner;
    }
}
