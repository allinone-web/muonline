using Client.Main.Content;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters.BloodCastle
{
    [NpcInfo(430, "Dark Skull Soldier (Master Level)")]
    public class DarkSkullSoldier8 : DarkSkullSoldier1
    {
        public DarkSkullSoldier8()
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
