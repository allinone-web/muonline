using Client.Main.Content;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters.BloodCastle
{
    [NpcInfo(142, "Red Skeleton Knight")]
    public class RedSkeletonKnight7 : RedSkeletonKnight1
    {
        public RedSkeletonKnight7()
        {
        }

        public override async Task Load()
        {
            // Visual setup and model loading are inherited from the source-matched base variant.
            await base.Load();
        }
    }
}
