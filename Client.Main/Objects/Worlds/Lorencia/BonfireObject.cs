using Client.Main.Content;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Worlds.Lorencia
{
    public sealed class BonfireObject : ModelObject
    {
        private const double LegacyStepSeconds = 1.0 / 25.0;
        private readonly LorenciaObjectParticleEffect _fireEffect;
        private double _legacyAccumulator;

        public BonfireObject()
        {
            LightEnabled = true;
            BlendMesh = 1;
            _fireEffect = new LorenciaObjectParticleEffect(
                this,
                LorenciaObjectEffectKind.Fire,
                new Vector3(0f, 0f, 60f));
            Children.Add(_fireEffect);
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare("Object1/Bonfire01.bmd");
            await base.Load();
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            _legacyAccumulator += gameTime.ElapsedGameTime.TotalSeconds;
            while (_legacyAccumulator >= LegacyStepSeconds)
            {
                BlendMeshLight = (MuGame.Random.Next(6) + 4) * 0.1f;
                _legacyAccumulator -= LegacyStepSeconds;
            }
        }
    }
}
