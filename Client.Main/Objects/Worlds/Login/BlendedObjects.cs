using Client.Main.Content;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Threading.Tasks;

namespace Client.Main.Objects.Worlds.Login
{
    public class BlendedObjects : ModelObject
    {
        public override bool ForceVisibleInWorld => true;
        public override WorldObjectRenderPolicy RenderPolicy => base.RenderPolicy.With(alwaysUpdate: true);
        protected override bool RequiresPerFrameAnimation => true;
        protected override bool PreserveBlendMeshesInLowQuality => true;

        public override async Task Load()
        {
            var idx = (Type + 1).ToString().PadLeft(2, '0');
            Model = await BMDLoader.Instance.Prepare($"Object95/Object{idx}.bmd");
            BlendState = BlendState.AlphaBlend;
            LightEnabled = true;
            IsTransparent = true;
            DepthState = DepthStencilState.DepthRead;
            BlendMesh = 0;
            BlendMeshState = BlendState.Additive;
            Position = new Vector3(Position.X, Position.Y, Position.Z - 10f);
            await base.Load();
        }
        public override void Draw(GameTime gameTime)
        {
            base.Draw(gameTime);
        }
    }
}
