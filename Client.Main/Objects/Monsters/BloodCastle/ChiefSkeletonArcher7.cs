using Client.Main.Content;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters.BloodCastle
{
    [NpcInfo(139, "Chief Skeleton Archer")]
    public class ChiefSkeletonArcher7 : ChiefSkeletonArcher1
    {
        public ChiefSkeletonArcher7()
        {
            Scale = 1.1f;
        }

        public override async Task Load()
        {
            // Visual setup and model loading are inherited from the source-matched base variant.
            await base.Load();
        }
    }
}
