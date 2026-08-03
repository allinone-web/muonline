using Client.Main.Content;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters.BloodCastle
{
    [NpcInfo(119, "Chief Skeleton Warrior")]
    public class ChiefSkeletonWarrior5 : ChiefSkeletonWarrior1
    {
        public ChiefSkeletonWarrior5()
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
