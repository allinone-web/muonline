using Client.Main.Content;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters.BloodCastle
{
    [NpcInfo(94, "Red Skeleton Knight")]
    public class RedSkeletonKnight2 : RedSkeletonKnight1
    {
        public RedSkeletonKnight2()
        {
        }

        public override async Task Load()
        {
            // Visual setup and model loading are inherited from the source-matched base variant.
            await base.Load();
        }
    }
}
