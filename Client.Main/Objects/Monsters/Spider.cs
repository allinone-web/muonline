using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(3, "Spider")]
    public class Spider : MonsterObject
    {
        public Spider()
        {
            Scale = 0.4f;
            RenderShadow = true;
            MoveSpeed = 250f; // SourceMain5.2: default monster MoveSpeed (10 * 25 FPS)
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster10.bmd");
            await base.Load();

            // SourceMain5.2 ZzzOpenData.cpp: base monster speeds with Spider walk override.
            SetActionSpeed(MonsterActionType.Stop1, 0.25f);
            SetActionSpeed(MonsterActionType.Stop2, 0.20f);
            SetActionSpeed(MonsterActionType.Walk, 1.2f);
            SetActionSpeed(MonsterActionType.Attack1, 0.33f);
            SetActionSpeed(MonsterActionType.Attack2, 0.33f);
            SetActionSpeed(MonsterActionType.Shock, 0.50f);
            SetActionSpeed(MonsterActionType.Die, 0.55f);
        }

        private void PlaySpiderSound()
        {
            Vector3 listenerPosition = ((Controls.WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mSpider1.wav", Position, listenerPosition);
        }

        protected override void OnIdle()
        {
            base.OnIdle();
            PlaySpiderSound();
        }

        protected override void OnStartWalk()
        {
            base.OnStartWalk();
            // PlaySpiderSound();
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            PlaySpiderSound();
        }

        public override void OnReceiveDamage()
        {
            base.OnReceiveDamage();
            PlaySpiderSound();
        }

        public override void OnDeathAnimationStart()
        {
            base.OnDeathAnimationStart();
            PlaySpiderSound();
        }
    }
}
