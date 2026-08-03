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
        private SourceMonsterEyeEffect _eyeGlow;
        private MonsterBreathEffect _breath;

        public EliteBullFighter()
        {
            RenderShadow = true;
            Scale = 1.15f;
            MoveSpeed = 250f; // SourceMain5.2: default monster MoveSpeed (10 * 25 FPS)

            _rightHandWeapon = new WeaponObject
            {
                LinkParentAnimation = false,
                ParentBoneLink = 42,
                ItemLevel = 1
            };
            Children.Add(_rightHandWeapon);

            // SourceMain5.2 RenderEye(o, 22, 23): eye01.jpg, white pulse, +/-5 local X.
            _eyeGlow = new SourceMonsterEyeEffect
            {
                LeftEyeBone = 22,
                RightEyeBone = 23,
                LeftEyeOffset = new Vector3(5f, 0f, 0f),
                RightEyeOffset = new Vector3(-5f, 0f, 0f)
            };
            Children.Add(_eyeGlow);

            // Breath smoke from mouth (bone 24) during idle/walk
            _breath = new MonsterBreathEffect
            {
                SourceBone = 24,
                EmissionRate = 12.5f,
                Triggers = new()
                {
                    new() { ActionIndex = (byte)MonsterActionType.Stop1, FrameStart = 15, FrameEnd = 20 },
                    new() { ActionIndex = (byte)MonsterActionType.Stop2, FrameStart = 20, FrameEnd = 25 },
                    new() { ActionIndex = (byte)MonsterActionType.Walk, FrameStart = 2, FrameEnd = 3 },
                    new() { ActionIndex = (byte)MonsterActionType.Walk, FrameStart = 5, FrameEnd = 6 },
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

            // SourceMain5.2 ZzzOpenData.cpp: base monster action speeds.
            SetActionSpeed(MonsterActionType.Stop1, 0.25f);
            SetActionSpeed(MonsterActionType.Stop2, 0.20f);
            SetActionSpeed(MonsterActionType.Walk, 0.34f);
            SetActionSpeed(MonsterActionType.Attack1, 0.33f);
            SetActionSpeed(MonsterActionType.Attack2, 0.33f);
            SetActionSpeed(MonsterActionType.Shock, 0.50f);
            SetActionSpeed(MonsterActionType.Die, 0.55f);
        }

        // Sound mapping based on C++ SetMonsterSound(MODEL_MONSTER01 + Type, 0, 1, 2, 3, 4);
        protected override void OnIdle()
        {
            base.OnIdle();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            string sound = MuGame.Random.Next(2) == 0
                ? "Sound/mBull1.wav"
                : "Sound/mBull2.wav";
            SoundController.Instance.PlayBufferWithAttenuation(sound, Position, listenerPosition);
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            string sound = MuGame.Random.Next(2) == 0
                ? "Sound/mBullAttack1.wav"
                : "Sound/mBullAttack2.wav";
            SoundController.Instance.PlayBufferWithAttenuation(sound, Position, listenerPosition);
        }

        public override void OnReceiveDamage()
        {
            base.OnReceiveDamage();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            string sound = MuGame.Random.Next(2) == 0
                ? "Sound/mBullAttack1.wav"
                : "Sound/mBullAttack2.wav";
            SoundController.Instance.PlayBufferWithAttenuation(sound, Position, listenerPosition);
        }

        public override void OnDeathAnimationStart()
        {
            base.OnDeathAnimationStart();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mBullDie.wav", Position, listenerPosition); // Death sound (index 4)
        }
    }
}
