using Client.Data;
using Client.Main.Content;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Worlds.Lorencia
{
    public class MuWallObject : ModelObject
    {
        // Opaque wall meshes may be instanced; transparent sections remain on the regular per-object pass.
        protected override bool AllowMapObjectInstancing => true;

        public MuWallObject()
        {
            LightEnabled = true;
        }

        public override async Task Load()
        {
            var idx = (Type - (ushort)ModelType.MuWall01 + 1).ToString().PadLeft(2, '0');
            Model = await BMDLoader.Instance.Prepare($"Object1/StoneMuWall{idx}.bmd");
            await base.Load();
        }
    }
}
