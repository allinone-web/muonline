using Client.Data;
using Client.Main.Content;
using Client.Main.Graphics;
using Microsoft.Xna.Framework.Graphics;
using System.Threading.Tasks;

namespace Client.Main.Objects.Worlds.Lorencia
{
    public class CarriageObject : ModelObject
    {
        public CarriageObject()
        {
            LightEnabled = true;
            BlendMesh = 2;
        }

        public override async Task Load()
        {
            var idx = (Type - (ushort)ModelType.Carriage01 + 1).ToString().PadLeft(2, '0');
            Model = await BMDLoader.Instance.Prepare($"Object1/Carriage{idx}.bmd");
            await base.Load();
        }

    }
}
