using Client.Main.Content;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters.BloodCastle
{
    [NpcInfo(431, "Giant Ogre (Master Level)")]
    public class GiantOgre8 : GiantOgre1
    {
        public GiantOgre8()
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
