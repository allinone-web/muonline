using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Worlds.LostTower
{
    public class LightBeamObject : LostTowerObject
    {
        public LightBeamObject()
        {
            BlendState = BlendState.Opaque;
            IsTransparent = true;
            Scale = 1f;
        }
    }
}
