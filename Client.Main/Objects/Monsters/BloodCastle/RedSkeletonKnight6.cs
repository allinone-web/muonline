using Client.Main.Content;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters.BloodCastle
{
    [NpcInfo(129, "Red Skeleton Knight")]
    public class RedSkeletonKnight6 : RedSkeletonKnight1
    {
        public RedSkeletonKnight6()
        {
        }

        public override async Task Load()
        {
            // Visual setup and model loading are inherited from the source-matched base variant.
            await base.Load();
        }
    }
}
