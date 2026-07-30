namespace Client.Main.Objects.Worlds.Atlans
{
    /// <summary>
    /// Invisible logical partner of the visible Atlans water gate.
    /// The original client registers map object type 39 as an operate marker and hides
    /// its complete mesh; passage/collision remains driven by map and server state.
    /// </summary>
    public sealed class GateOperateObject : WorldObject
    {
        public GateOperateObject()
        {
            Hidden = true;
            Interactive = false;
        }
    }
}
