using Client.Main.Content;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Worlds.Lorencia
{
    public sealed class BridgeObject : ModelObject
    {
        public BridgeObject()
        {
            LightEnabled = true;
            Children.Add(new LorenciaObjectParticleEffect(
                this,
                LorenciaObjectEffectKind.Fire,
                new Vector3(90f, -200f, 30f)));
            Children.Add(new LorenciaObjectParticleEffect(
                this,
                LorenciaObjectEffectKind.Fire,
                new Vector3(90f, 200f, 30f)));
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare("Object1/Bridge01.bmd");
            await base.Load();
        }
    }
}
