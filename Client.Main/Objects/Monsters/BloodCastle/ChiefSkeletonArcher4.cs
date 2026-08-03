using Client.Main.Content;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters.BloodCastle
{
    [NpcInfo(114, "Chief Skeleton Archer")]
    public class ChiefSkeletonArcher4 : ChiefSkeletonArcher1
    {
        public ChiefSkeletonArcher4()
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
