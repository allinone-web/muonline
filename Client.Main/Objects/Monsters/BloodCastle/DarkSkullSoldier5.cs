using Client.Main.Content;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters.BloodCastle
{
    [NpcInfo(121, "Dark Skull Soldier")]
    public class DarkSkullSoldier5 : DarkSkullSoldier1
    {
        public DarkSkullSoldier5()
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
