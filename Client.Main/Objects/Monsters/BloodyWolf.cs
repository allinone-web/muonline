using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Objects.Effects;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(60, "BloodyWolf")]
    public class BloodyWolf : MonsterObject
    {
        private GlowingEyesEffect _eyeGlow;

        public BloodyWolf()
        {
            Scale = 2.2f;
            MoveSpeed = 250f;

            _eyeGlow = new GlowingEyesEffect { LeftEyeBone = 12, RightEyeBone = 11, GlowColor = new Color(40, 110, 255) };
            Children.Add(_eyeGlow);
            Children.Add(new SourceMonsterSandSmokeEffect { EmitDeathSmoke = false });
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster44.bmd");
            await base.Load();
            SetActionSpeed(MonsterActionType.Stop1, 0.25f);
            SetActionSpeed(MonsterActionType.Stop2, 0.20f);
            SetActionSpeed(MonsterActionType.Walk, 0.34f);
            SetActionSpeed(MonsterActionType.Attack1, 0.33f);
            SetActionSpeed(MonsterActionType.Attack2, 0.33f);
            SetActionSpeed(MonsterActionType.Shock, 0.50f);
            SetActionSpeed(MonsterActionType.Die, 0.55f);
            // C++: Models[MODEL_MONSTER01+Type].BoneHead = 7;
        }

        // Sound mapping based on C++ SetMonsterSound(MODEL_MONSTER01 + Type, 150, 150, 151, 152, 153);
        protected override void OnIdle()
        {
            base.OnIdle();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/blood1.wav", Position, listenerPosition); // Sound 150
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            if (attackType == 1)
                SoundController.Instance.PlayBufferWithAttenuation("Sound/blood_attack1.wav", Position, listenerPosition); // Sound 151
            else
                SoundController.Instance.PlayBufferWithAttenuation("Sound/blood_attack2.wav", Position, listenerPosition); // Sound 152
        }

        public override void OnReceiveDamage()
        {
            base.OnReceiveDamage();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/blood_attack1.wav", Position, listenerPosition);
        }

        public override void OnDeathAnimationStart()
        {
            base.OnDeathAnimationStart();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/blood_die.wav", Position, listenerPosition); // Sound 153
        }
    }
}
