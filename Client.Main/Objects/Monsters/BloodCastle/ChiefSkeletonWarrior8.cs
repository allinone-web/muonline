using Client.Main.Content;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters.BloodCastle
{
    [NpcInfo(428, "Chief Skeleton Warrior (Master Level)")]
    public class ChiefSkeletonWarrior8 : ChiefSkeletonWarrior1
    {
        public ChiefSkeletonWarrior8()
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
