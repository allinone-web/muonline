using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    public class LightEffect : SpriteObject
    {
        public override string TexturePath => $"Effect/flare01.jpg";

        /// <summary>Optional parent model bone to follow.</summary>
        public int SourceBone { get; set; } = -1;

        /// <summary>Local offset applied in the source bone space.</summary>
        public Vector3 SourceOffset { get; set; } = Vector3.Zero;

        public LightEffect()
        {
            BlendState = BlendState.Additive;
            LightEnabled = true;
            Light = Vector3.One;
            DepthState = DepthStencilState.DepthRead;
        }

        public override void Update(GameTime gameTime)
        {
            if (SourceBone >= 0 && Parent is ModelObject parentModel)
            {
                var bones = parentModel.GetBoneTransforms();
                if (bones != null && SourceBone < bones.Length)
                    Position = Vector3.Transform(SourceOffset, bones[SourceBone]);
            }

            base.Update(gameTime);
        }
    }
}
