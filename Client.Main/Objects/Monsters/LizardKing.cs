using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Models;
using Client.Main.Objects.Effects;
using Client.Main.Objects.Player;
using Client.Main.Core.Utilities;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(48, "Lizard King")]
    public class LizardKing : MonsterObject
    {
        private WeaponObject _rightHandWeapon;

        private SourceMonsterEyeEffect _eyeGlow;

        public LizardKing()
        {
            RenderShadow = true;
            Scale = 1.4f;
            MoveSpeed = 250f; // SourceMain5.2: default monster MoveSpeed (10 * 25 FPS)

            // Eyes: bones 42 (L), 43 (R) — original RenderEye(o, 42, 43)
            _eyeGlow = new SourceMonsterEyeEffect { LeftEyeBone = 42, RightEyeBone = 43 };
            Children.Add(_eyeGlow);
            Children.Add(new MonsterBoneSpriteEffect
            {
                BoneIndices = new[] { 26, 31, 36, 41 },
                PrimaryTexturePath = "Effect/Spark02.jpg",
                PrimaryScale = 2f,
                SecondaryTexturePath = "Effect/Shiny03.jpg",
                SecondaryScale = 1f
            });
            _rightHandWeapon = new WeaponObject
            {
                LinkParentAnimation = false,
                ParentBoneLink = 52
            };
            Children.Add(_rightHandWeapon);
        }

        public override async Task Load()
        {
            // Model Loading Type: 36 -> File Number: 36 + 1 = 37
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster37.bmd");
            var weapon = ItemDatabase.GetItemDefinition(5, 11); // Staff of Resurrection
            if (weapon != null)
                _rightHandWeapon.Model = await BMDLoader.Instance.Prepare(weapon.TexturePath);
            await base.Load();
            SetActionSpeed(MonsterActionType.Stop1, 0.25f);
            SetActionSpeed(MonsterActionType.Stop2, 0.20f);
            SetActionSpeed(MonsterActionType.Walk, 0.34f);
            SetActionSpeed(MonsterActionType.Attack1, 0.33f);
            SetActionSpeed(MonsterActionType.Attack2, 0.33f);
            SetActionSpeed(MonsterActionType.Shock, 0.50f);
            SetActionSpeed(MonsterActionType.Die, 0.55f);
            // C++: Models[MODEL_MONSTER01+Type].BoneHead = 19;
        }

        // Sound mapping based on C++ SetMonsterSound(MODEL_MONSTER01 + Type, 138, 139, 138, 139, 140)
        protected override void OnIdle()
        {
            base.OnIdle();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            string sound = MuGame.Random.Next(2) == 0
                ? "Sound/mLizardKing1.wav"
                : "Sound/mLizardKing2.wav";
            SoundController.Instance.PlayBufferWithAttenuation(sound, Position, listenerPosition);
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            string sound = MuGame.Random.Next(2) == 0
                ? "Sound/mLizardKing1.wav"
                : "Sound/mLizardKing2.wav";
            SoundController.Instance.PlayBufferWithAttenuation(sound, Position, listenerPosition);
        }

        public override void OnReceiveDamage()
        {
            base.OnReceiveDamage();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            string sound = MuGame.Random.Next(2) == 0
                ? "Sound/mLizardKing1.wav"
                : "Sound/mLizardKing2.wav";
            SoundController.Instance.PlayBufferWithAttenuation(sound, Position, listenerPosition);
        }

        public override void OnDeathAnimationStart()
        {
            base.OnDeathAnimationStart();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mGorgonDie.wav", Position, listenerPosition);
        }
    }
}
