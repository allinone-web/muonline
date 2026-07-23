using System;
using System.Collections.Generic;
using System.Text;

namespace Client.Main.Objects
{
    public abstract class EffectObject : WorldObject
    {
        public override WorldObjectRenderPolicy RenderPolicy => base.RenderPolicy.With(
            forceVisible: true,
            alwaysUpdate: true);
    }
}
