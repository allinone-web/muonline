using Client.Main.Content;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters.BloodCastle
{
    [NpcInfo(118, "Magic Skeleton")]
    public class MagicSkeleton4 : MagicSkeleton1
    {
        public MagicSkeleton4()
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
