using Client.Main.Content;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters.BloodCastle
{
    [NpcInfo(116, "Giant Ogre")]
    public class GiantOgre4 : GiantOgre1
    {
        public GiantOgre4()
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
