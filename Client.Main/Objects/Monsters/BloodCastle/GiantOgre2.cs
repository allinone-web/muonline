using Client.Main.Content;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters.BloodCastle
{
    [NpcInfo(93, "Giant Ogre")]
    public class GiantOgre2 : GiantOgre1
    {
        public GiantOgre2()
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
