using Client.Main.Content;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters.BloodCastle
{
    [NpcInfo(95, "Magic Skeleton")]
    public class MagicSkeleton2 : MagicSkeleton1
    {
        public MagicSkeleton2()
        {
            Scale = 1.2f;
        }

        public override async Task Load()
        {
            // Visual setup and model loading are inherited from the source-matched base variant.
            await base.Load();
        }
    }
}
