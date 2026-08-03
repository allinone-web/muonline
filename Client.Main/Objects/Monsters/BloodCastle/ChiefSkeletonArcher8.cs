using Client.Main.Content;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters.BloodCastle
{
    [NpcInfo(429, "Chief Skeleton Archer (Master Level)")]
    public class ChiefSkeletonArcher8 : ChiefSkeletonArcher1
    {
        public ChiefSkeletonArcher8()
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
