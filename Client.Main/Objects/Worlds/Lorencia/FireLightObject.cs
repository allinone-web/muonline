using Client.Data;
using Client.Main.Content;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Worlds.Lorencia
{
    public sealed class FireLightObject : ModelObject
    {
        private readonly LorenciaObjectParticleEffect _fireEffect;

        public FireLightObject()
        {
            LightEnabled = true;
            _fireEffect = new LorenciaObjectParticleEffect(
                this,
                LorenciaObjectEffectKind.Fire,
                Vector3.Zero);
            Children.Add(_fireEffect);
        }

        public override async Task Load()
        {
            _fireEffect.LocalOffset = Type == (ushort)ModelType.FireLight01
                ? new Vector3(0f, 0f, 200f)
                : new Vector3(0f, -30f, 60f);

            var idx = (Type - (ushort)ModelType.FireLight01 + 1).ToString().PadLeft(2, '0');
            Model = await BMDLoader.Instance.Prepare($"Object1/FireLight{idx}.bmd");
            await base.Load();
        }
    }
}
