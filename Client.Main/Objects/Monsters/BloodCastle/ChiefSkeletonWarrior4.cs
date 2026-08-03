using Client.Main.Content;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters.BloodCastle
{
    [NpcInfo(113, "Chief Skeleton Warrior")]
    public class ChiefSkeletonWarrior4 : ChiefSkeletonWarrior1
    {
        public ChiefSkeletonWarrior4()
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
