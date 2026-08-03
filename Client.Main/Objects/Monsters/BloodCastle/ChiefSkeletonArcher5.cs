using Client.Main.Content;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters.BloodCastle
{
    [NpcInfo(120, "Chief Skeleton Archer")]
    public class ChiefSkeletonArcher5 : ChiefSkeletonArcher1
    {
        public ChiefSkeletonArcher5()
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
