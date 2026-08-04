using Client.Data;
using Client.Main.Content;
using Client.Main.Graphics;
using System.Threading.Tasks;

namespace Client.Main.Objects.Worlds.Lorencia
{
    public class StrawObject : ModelObject
    {
        public StrawObject()
        {
            LightEnabled = true;
        }

        public override async Task Load()
        {
            var idx = (Type - (ushort)ModelType.Straw01 + 1).ToString().PadLeft(2, '0');
            Model = await BMDLoader.Instance.Prepare($"Object1/Straw{idx}.bmd");

            // Type 102 was switching to freshly allocated opaque/depth states inside every
            // DrawMesh call, then switching back to alpha. Configure the object once instead;
            // the previous override applied the same state to every mesh anyway.
            BlendState = Type == 102 ? Microsoft.Xna.Framework.Graphics.BlendState.Opaque : Blendings.Alpha;
            DepthState = Microsoft.Xna.Framework.Graphics.DepthStencilState.Default;
            await base.Load();
        }
    }
}
