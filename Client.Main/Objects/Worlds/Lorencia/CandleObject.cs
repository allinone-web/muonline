using Client.Data;
using Client.Main.Content;
using Client.Main.Controls;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Worlds.Lorencia
{
    public sealed class CandleObject : ModelObject
    {
        private const double LegacyStepSeconds = 1.0 / 25.0;
        private readonly DynamicLight _dynamicLight = new DynamicLight
        {
            Radius = Constants.TERRAIN_SCALE * 3f,
            Intensity = 1f,
        };
        private double _legacyAccumulator;

        public CandleObject()
        {
            LightEnabled = true;
            BlendMesh = 1;
            _dynamicLight.Owner = this;
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare("Object1/Candle01.bmd");
            await base.Load();

            World?.Terrain.AddDynamicLight(_dynamicLight);
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            _dynamicLight.Position = WorldPosition.Translation;
            _legacyAccumulator += gameTime.ElapsedGameTime.TotalSeconds;
            while (_legacyAccumulator >= LegacyStepSeconds)
            {
                float luminosity = (MuGame.Random.Next(4) + 3) * 0.1f;
                _dynamicLight.Color = new Vector3(
                    luminosity,
                    luminosity * 0.6f,
                    luminosity * 0.2f);
                _legacyAccumulator -= LegacyStepSeconds;
            }
        }
    }
}
