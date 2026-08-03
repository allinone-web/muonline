using Client.Main.Content;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters.BloodCastle
{
    [NpcInfo(99, "Giant Ogre")]
    public class GiantOgre3 : GiantOgre1
    {
        public GiantOgre3()
        {
            Scale = 0.8f;
        }

        public override async Task Load()
        {
            // Visual setup and model loading are inherited from the source-matched base variant.
            await base.Load();
        }
    }
}
