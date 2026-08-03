using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Models;
using Client.Main.Objects.Effects;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(45, "Bahamut")]
    public class Bahamut : MonsterObject
    {
        public Bahamut() : this(false)
        {
        }

        protected Bahamut(bool greatBahamut)
        {
            RenderShadow = true;
            Scale = 0.6f; // Set according to C++ Setting_Monster
            MoveSpeed = 250f; // SourceMain5.2: default monster MoveSpeed (10 * 25 FPS)
            Children.Add(new MonsterBoneSpriteEffect
            {
                BoneIndices = new[] { 9 },
                PrimaryTexturePath = "Effect/Spark02.jpg",
                PrimaryScale = 4f,
                SecondaryTexturePath = "Effect/Shiny03.jpg",
                SecondaryScale = 3f
            });
            if (greatBahamut)
                Children.Add(new SourceMonsterSmokeEffect());
        }

        public override async Task Load()
        {
            // Model Loading Type: 33 -> File Number: 33 + 1 = 34
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster34.bmd");
            await base.Load();
            SetActionSpeed(MonsterActionType.Stop1, 0.25f);
            SetActionSpeed(MonsterActionType.Stop2, 0.20f);
            SetActionSpeed(MonsterActionType.Walk, 0.34f);
            SetActionSpeed(MonsterActionType.Attack1, 0.33f);
            SetActionSpeed(MonsterActionType.Attack2, 0.33f);
            SetActionSpeed(MonsterActionType.Shock, 0.50f);
            SetActionSpeed(MonsterActionType.Die, 0.55f);
        }

        // Sound mapping based on C++ SetMonsterSound(MODEL_MONSTER01 + Type, 130, 130, 131, 131, 130);
        protected override void OnIdle()
        {
            base.OnIdle();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mBahamut1.wav", Position, listenerPosition);
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mYeti1.wav", Position, listenerPosition);
        }

        public override void OnReceiveDamage()
        {
            base.OnReceiveDamage();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mYeti1.wav", Position, listenerPosition);
        }

        public override void OnDeathAnimationStart()
        {
            base.OnDeathAnimationStart();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mBahamut1.wav", Position, listenerPosition);
        }
    }
}
