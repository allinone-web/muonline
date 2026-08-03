using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Core.Utilities;
using Client.Main.Models;
using Client.Main.Objects.Player;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(23, "Hommerd")]
    public class Hommerd : MonsterObject
    {
        private readonly WeaponObject _rightHandWeapon;
        private readonly WeaponObject _leftHandWeapon;

        public Hommerd()
        {
            RenderShadow = true;
            Scale = 1.15f; // Set according to C++ Setting_Monster
            MoveSpeed = 250f; // SourceMain5.2: default monster MoveSpeed (10 * 25 FPS)
            _rightHandWeapon = new WeaponObject
            {
                LinkParentAnimation = false,
                ParentBoneLink = 30
            };
            _leftHandWeapon = new WeaponObject
            {
                LinkParentAnimation = false,
                ParentBoneLink = 39
            };
            Children.Add(_rightHandWeapon);
            Children.Add(_leftHandWeapon);
        }

        public override async Task Load()
        {
            // Model Loading Type: 16 -> File Number: 16 + 1 = 17
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster17.bmd");
            var axe = ItemDatabase.GetItemDefinition(1, 7); // Larkan Axe
            if (axe != null)
                _rightHandWeapon.Model = await BMDLoader.Instance.Prepare(axe.TexturePath);
            var shield = ItemDatabase.GetItemDefinition(6, 10); // Big Round Shield
            if (shield != null)
                _leftHandWeapon.Model = await BMDLoader.Instance.Prepare(shield.TexturePath);
            await base.Load();
            SetActionSpeed(MonsterActionType.Stop1, 0.25f);
            SetActionSpeed(MonsterActionType.Stop2, 0.20f);
            SetActionSpeed(MonsterActionType.Walk, 0.34f);
            SetActionSpeed(MonsterActionType.Attack1, 0.33f);
            SetActionSpeed(MonsterActionType.Attack2, 0.33f);
            SetActionSpeed(MonsterActionType.Shock, 0.50f);
            SetActionSpeed(MonsterActionType.Die, 0.55f);
        }

        // Sound mapping based on C++ SetMonsterSound(MODEL_MONSTER01 + Type, 56, 57, 58, 58, 59)
        protected override void OnIdle()
        {
            base.OnIdle();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            string sound = MuGame.Random.Next(2) == 0
                ? "Sound/mHomord1.wav"
                : "Sound/mHomord2.wav";
            SoundController.Instance.PlayBufferWithAttenuation(sound, Position, listenerPosition);
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mHomordAttack1.wav", Position, listenerPosition);
            // Index 3 -> Sound 58
        }

        public override void OnReceiveDamage()
        {
            base.OnReceiveDamage();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mHomordAttack1.wav", Position, listenerPosition);
        }

        public override void OnDeathAnimationStart()
        {
            base.OnDeathAnimationStart();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mHomordDie.wav", Position, listenerPosition);
        }
    }
}
