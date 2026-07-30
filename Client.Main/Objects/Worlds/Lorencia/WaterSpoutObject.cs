using Client.Main.Content;
using Client.Main.Objects.Effects;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Threading.Tasks;

namespace Client.Main.Objects.Worlds.Lorencia
{
    public class WaterSpoutObject : ModelObject
    {
        private readonly LorenciaFountainSmokeEffect _smokeEffect;

        public WaterSpoutObject()
        {
            LightEnabled = true;
            Light = Vector3.Zero;
            BlendMesh = 3;
            TextureCoordinateOffsetMeshIndex = BlendMesh;
            BlendMeshLight = 1f;
            BlendMeshState = BlendState.Additive;

            _smokeEffect = new LorenciaFountainSmokeEffect(this);
            Children.Add(_smokeEffect);
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare("Object1/Waterspout01.bmd");
            await base.Load();
        }

        public override void Update(GameTime gameTime)
        {
            TextureCoordinateOffset = new Vector2(
                0f,
                -(float)((long)gameTime.TotalGameTime.TotalMilliseconds % 1000L) * 0.001f);

            base.Update(gameTime);
        }
    }
}
