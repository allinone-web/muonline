using Client.Data;
using Client.Main.Content;
using System.Threading.Tasks;

namespace Client.Main.Objects.Worlds.Lorencia
{
    public class HouseEtcObject : ModelObject
    {
        // Opaque meshes may be instanced; transparent sections remain on the regular per-object pass.
        protected override bool AllowMapObjectInstancing => true;
        public HouseEtcObject()
        {
            LightEnabled = true;
        }

        public override async Task Load()
        {
            var idx = (Type - (ushort)ModelType.HouseEtc01 + 1).ToString().PadLeft(2, '0');
            Model = await BMDLoader.Instance.Prepare($"Object1/HouseEtc{idx}.bmd");
            await base.Load();
        }

    }
}
