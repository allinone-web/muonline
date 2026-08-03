using Client.Main.Content;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters.BloodCastle
{
    [NpcInfo(126, "Chief Skeleton Archer")]
    public class ChiefSkeletonArcher6 : ChiefSkeletonArcher1
    {
        public ChiefSkeletonArcher6()
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
