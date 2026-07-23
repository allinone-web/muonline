using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Core.Utilities;
using Client.Main.Models;
using Client.Main.Objects.Effects;
using Client.Main.Objects.Player;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(4, "Elite Bull Fighter")]
    public class EliteBullFighter : MonsterObject
    {
        private WeaponObject _rightHandWeapon;
        private GlowingEyesEffect _eyeGlow;
        private MonsterBreathEffect _breath;

        public EliteBullFighter()
        {
            RenderShadow = true;
            Scale = 1.15f;

            EnableCustomShader = true;
            SimpleColorMode = true;
            GlowColor = new Vector3(0.25f, 0.15f, 0f);
            GlowIntensity = 7.0f;

            _rightHandWeapon = new WeaponObject
            {
                LinkParentAnimation = false,
                ParentBoneLink = 42,
                ItemLevel = 1
            };
            Children.Add(_rightHandWeapon);

            // Eyes: bones 22 (L), 23 (R) — original RenderEye(o, 22, 23)
            _eyeGlow = new GlowingEyesEffect { LeftEyeBone = 22, RightEyeBone = 23, GlowColor = new Color(80, 180, 255) };
            Children.Add(_eyeGlow);

            // Breath smoke from mouth (bone 24) during idle/walk
            _breath = new MonsterBreathEffect
            {
                SourceBone = 24,
                EmissionRate = 20f,
                Triggers = new()
                {
                    new() { ActionIndex = (byte)MonsterActionType.Stop1, FrameStart = 15, FrameEnd = 20 },
                    new() { ActionIndex = (byte)MonsterActionType.Stop2, FrameStart = 20, FrameEnd = 25 },
                }
            };
            Children.Add(_breath);
        }

        public override async Task Load()
        {
            // Model Loading Type: 0 -> File Number: 0 + 1 = 1
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster01.bmd");
            var item = ItemDatabase.GetItemDefinition(3, 7); // Berdysh
            _rightHandWeapon.Model = await BMDLoader.Instance.Prepare(item.TexturePath);
            await base.Load();
        }

        // Sound mapping based on C++ SetMonsterSound(MODEL_MONSTER01 + Type, 0, 1, 2, 3, 4);
        protected override void OnIdle()
        {
            base.OnIdle();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            // Play one of the idle sounds (index 0 or 1)
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mBull1.wav", Position, listenerPosition);
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            // Play one of the attack sounds (index 2 or 3)
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mBullAttack1.wav", Position, listenerPosition);
        }

        public override void OnDeathAnimationStart()
        {
            base.OnDeathAnimationStart();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mBullDie.wav", Position, listenerPosition); // Death sound (index 4)
        }
    }
}