using Client.Data;
using Client.Main.Content;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Worlds.Lorencia
{
    public sealed class LightObject : ModelObject
    {
        private readonly LorenciaObjectParticleEffect _effect;

        public LightObject()
        {
            LightEnabled = true;
            HiddenMesh = -2;
            _effect = new LorenciaObjectParticleEffect(
                this,
                LorenciaObjectEffectKind.Fire,
                Vector3.Zero);
            Children.Add(_effect);
        }

        public override async Task Load()
        {
            _effect.Kind = (ushort)Type switch
            {
                (ushort)ModelType.Light01 => LorenciaObjectEffectKind.Fire,
                (ushort)(ModelType.Light01 + 1) => LorenciaObjectEffectKind.Smoke,
                _ => LorenciaObjectEffectKind.SmokeSubtype2,
            };

            var idx = ((short)(Type - (short)ModelType.Light01) + 1).ToString().PadLeft(2, '0');
            Model = await BMDLoader.Instance.Prepare($"Object1/Light{idx}.bmd");
            await base.Load();
        }
    }
}
