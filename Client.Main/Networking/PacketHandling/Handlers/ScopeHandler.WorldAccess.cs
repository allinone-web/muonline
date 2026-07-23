using Client.Main.Controls;

namespace Client.Main.Networking.PacketHandling.Handlers
{
    public partial class ScopeHandler
    {
        private static bool TryGetActiveWalkableWorld(out WalkableWorldControl world)
        {
            world = MuGame.Instance.ActiveScene?.World as WalkableWorldControl;
            return world != null;
        }
    }
}
