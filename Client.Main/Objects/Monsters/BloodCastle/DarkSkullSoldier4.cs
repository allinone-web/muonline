using Client.Main.Content;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters.BloodCastle
{
    [NpcInfo(115, "Dark Skull Soldier")]
    public class DarkSkullSoldier4 : DarkSkullSoldier1
    {
        public DarkSkullSoldier4()
        {
            Scale = 1.0f;
        }

        public override async Task Load()
        {
            // Visual setup and model loading are inherited from the source-matched base variant.
            await base.Load();
        }
    }
}
