using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Objects.Effects;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(62, "Mutant")]
    public class Mutant : MonsterObject
    {
        private GlowingEyesEffect _eyeGlow;

        public Mutant()
        {
            Scale = 1.5f;
            MoveSpeed = 250f;

            _eyeGlow = new GlowingEyesEffect { LeftEyeBone = 9, RightEyeBone = 8, GlowColor = new Color(50, 140, 255) };
            Children.Add(_eyeGlow);
            Children.Add(new SourceMonsterSandSmokeEffect { EmitDeathSmoke = false });
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster46.bmd");
            await base.Load();
            SetActionSpeed(MonsterActionType.Stop1, 0.25f);
            SetActionSpeed(MonsterActionType.Stop2, 0.20f);
            SetActionSpeed(MonsterActionType.Walk, 0.34f);
            SetActionSpeed(MonsterActionType.Attack1, 0.33f);
            SetActionSpeed(MonsterActionType.Attack2, 0.33f);
            SetActionSpeed(MonsterActionType.Shock, 0.50f);
            SetActionSpeed(MonsterActionType.Die, 0.55f);
            // C++: Models[MODEL_MONSTER01+Type].BoneHead = 6;
        }

        // Sound mapping based on C++ SetMonsterSound(MODEL_MONSTER01 + Type, 157, 158, 159, 159, 159);
        protected override void OnIdle()
        {
            base.OnIdle();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mutant1.wav", Position, listenerPosition); // Sound 157
            // Consider adding logic for Sound 158 (mutant2.wav) if desired
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mutant_attack1.wav", Position, listenerPosition); // Sound 159
        }

        public override void OnReceiveDamage()
        {
            base.OnReceiveDamage();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mutant_attack1.wav", Position, listenerPosition);
        }

        public override void OnDeathAnimationStart()
        {
            base.OnDeathAnimationStart();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mutant_attack1.wav", Position, listenerPosition); // Sound 159
        }
    }
}
