using Client.Main.Content;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Worlds.Lorencia
{
    public sealed class DungeonGateObject : ModelObject
    {
        public DungeonGateObject()
        {
            LightEnabled = true;
            Children.Add(new LorenciaObjectParticleEffect(
                this,
                LorenciaObjectEffectKind.Fire,
                new Vector3(-150f, -150f, 140f)));
            Children.Add(new LorenciaObjectParticleEffect(
                this,
                LorenciaObjectEffectKind.Fire,
                new Vector3(150f, -150f, 140f)));
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare("Object1/DoungeonGate01.bmd");
            await base.Load();
        }
    }
}
