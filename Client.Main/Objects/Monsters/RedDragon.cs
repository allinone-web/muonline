using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(42, "Red Dragon")]
    public class RedDragon : MonsterObject
    {
        public RedDragon()
        {
            RenderShadow = true;
            Scale = 1.3f; // Set according to C++ Setting_Monster
            MoveSpeed = 250f; // SourceMain5.2: default monster MoveSpeed (10 * 25 FPS)
        }

        public override async Task Load()
        {
            // Model Loading Type: 31 -> File Number: 31 + 1 = 32
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster32.bmd");
            await base.Load();

            SetActionSpeed(MonsterActionType.Stop1, 0.25f * 0.4f);
            SetActionSpeed(MonsterActionType.Stop2, 0.8f);
            SetActionSpeed(MonsterActionType.Walk, 0.34f * 0.4f);
            SetActionSpeed(MonsterActionType.Attack1, 0.5f);
            SetActionSpeed(MonsterActionType.Attack2, 0.7f);
            SetActionSpeed(MonsterActionType.Shock, 0.50f * 0.4f);
            SetActionSpeed(MonsterActionType.Die, 0.55f);
        }

        // Sound mapping based on C++ SetMonsterSound(MODEL_MONSTER01 + Type, 123, 123, 124, 124, 125); (Uses Yeti/Bull sounds)
        protected override void OnIdle()
        {
            base.OnIdle();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mYeti1.wav", Position, listenerPosition); // Index 0 -> Sound 123
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mBullAttack1.wav", Position, listenerPosition); // Index 2 -> Sound 124
        }

        public override void OnReceiveDamage()
        {
            base.OnReceiveDamage();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mBullAttack1.wav", Position, listenerPosition);
        }

        public override void OnDeathAnimationStart()
        {
            base.OnDeathAnimationStart();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mYetiDie.wav", Position, listenerPosition); // Index 4 -> Sound 125
        }
    }
}
