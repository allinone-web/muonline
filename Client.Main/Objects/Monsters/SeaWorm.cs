using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(50, "Sea Worm")]
    public class SeaWorm : MonsterObject
    {
        public SeaWorm()
        {
            RenderShadow = true;
            Scale = 1.8f; // Set according to C++ Setting_Monster
            MoveSpeed = 250f; // SourceMain5.2: default monster MoveSpeed (10 * 25 FPS)
        }

        public override async Task Load()
        {
            // Model Loading Type: 38 -> File Number: 38 + 1 = 39
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster39.bmd");
            await base.Load();
            SetActionSpeed(MonsterActionType.Stop1, 0.25f);
            SetActionSpeed(MonsterActionType.Stop2, 0.20f);
            SetActionSpeed(MonsterActionType.Walk, 0.34f);
            SetActionSpeed(MonsterActionType.Attack1, 0.33f);
            SetActionSpeed(MonsterActionType.Attack2, 0.33f);
            SetActionSpeed(MonsterActionType.Shock, 0.50f);
            SetActionSpeed(MonsterActionType.Die, 0.55f);
        }
        // No sounds assigned in C++ Setting_Monster
    }
}
