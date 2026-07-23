using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Objects.Effects;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Monsters
{

    [NpcInfo(57, "IronWheel")]
    public class IronWheel : MonsterObject
    {
        private GlowingEyesEffect _eyeGlow;

        public IronWheel()
        {
            Scale = 1.4f;

            _eyeGlow = new GlowingEyesEffect { LeftEyeBone = 9, RightEyeBone = 8, GlowColor = new Color(60, 150, 255) };
            Children.Add(_eyeGlow);
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster42.bmd");
            await base.Load();
            // C++: Models[MODEL_MONSTER01+Type].BoneHead = 3;
        }

        // Sound mapping based on C++ SetMonsterSound(MODEL_MONSTER01 + Type, 143, 143, 144, 144, 144);
        protected override void OnIdle()
        {
            base.OnIdle();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/iron1.wav", Position, listenerPosition); // Sound 143
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/iron_attack1.wav", Position, listenerPosition); // Sound 144
        }

        public override void OnDeathAnimationStart()
        {
            base.OnDeathAnimationStart();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/iron_attack1.wav", Position, listenerPosition); // Sound 144
        }
    }
}
