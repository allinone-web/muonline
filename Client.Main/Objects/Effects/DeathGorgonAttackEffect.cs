using Client.Main.Controllers;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// SourceMain5.2 MODEL_FIRE burst used by the Gorgon boss attack.
    /// The source creates eighteen falling fire models for sixty legacy frames.
    /// </summary>
    public sealed class DeathGorgonAttackEffect : EffectObject
    {
        private const float LifetimeSeconds = 60f / 25f;
        private float _elapsed;

        public DeathGorgonAttackEffect(Vector3 position)
        {
            Position = position;
            IsTransparent = true;
            AffectedByTransparency = true;
            BlendState = BlendState.Additive;
            DepthState = DepthStencilState.DepthRead;
            BoundingBoxLocal = new BoundingBox(
                new Vector3(-500f, -500f, -100f),
                new Vector3(500f, 500f, 500f));

            for (int i = 0; i < 18; i++)
            {
                Children.Add(new FireBallCoreModel
                {
                    Position = new Vector3(0f, 0f, 120f),
                    Angle = new Vector3(0f, 0f, MathHelper.ToRadians(i * 20f)),
                    Scale = 0.8f + (float)MuGame.Random.NextDouble() * 0.3f
                });
            }
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (Status != GameControlStatus.Ready)
                return;

            _elapsed += (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (_elapsed >= LifetimeSeconds)
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
