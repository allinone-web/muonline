using Client.Main.Content;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters.BloodCastle
{
    [NpcInfo(96, "Chief Skeleton Warrior")]
    public class ChiefSkeletonWarrior3 : ChiefSkeletonWarrior1
    {
        public ChiefSkeletonWarrior3()
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
