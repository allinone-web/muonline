using Client.Main.Content;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters.BloodCastle
{
    [NpcInfo(432, "Red Skeleton Knight (Master Level)")]
    public class RedSkeletonKnight8 : RedSkeletonKnight1
    {
        public RedSkeletonKnight8()
        {
        }

        public override async Task Load()
        {
            // Visual setup and model loading are inherited from the source-matched base variant.
            await base.Load();
        }
    }
}
