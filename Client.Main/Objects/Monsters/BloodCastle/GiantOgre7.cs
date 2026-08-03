using Client.Main.Content;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters.BloodCastle
{
    [NpcInfo(141, "Giant Ogre")]
    public class GiantOgre7 : GiantOgre1
    {
        public GiantOgre7()
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
