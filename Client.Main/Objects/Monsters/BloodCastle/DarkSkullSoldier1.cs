using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Core.Utilities;
using Client.Main.Models;
using Client.Main.Objects.Player;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters.BloodCastle
{
    [NpcInfo(86, "Dark Skull Soldier")]
    public class DarkSkullSoldier1 : MonsterObject
    {
        private readonly WeaponObject _rightHandWeapon;
        private readonly WeaponObject _leftHandWeapon;

        public DarkSkullSoldier1()
        {
            Scale = 1.0f;
            MoveSpeed = 250f;
            _rightHandWeapon = new WeaponObject { LinkParentAnimation = false, ParentBoneLink = 33 };
            _leftHandWeapon = new WeaponObject { LinkParentAnimation = false, ParentBoneLink = 20 };
            Children.Add(_rightHandWeapon);
            Children.Add(_leftHandWeapon);
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster60.bmd");
            var axe = ItemDatabase.GetItemDefinition(1, 8); // Crescent Axe
            if (axe != null)
            {
                _rightHandWeapon.Model = await BMDLoader.Instance.Prepare(axe.TexturePath);
                _leftHandWeapon.Model = await BMDLoader.Instance.Prepare(axe.TexturePath);
            }
            await base.Load();
            SetActionSpeed(MonsterActionType.Stop1, 0.25f);
            SetActionSpeed(MonsterActionType.Stop2, 0.20f);
            SetActionSpeed(MonsterActionType.Walk, 0.34f);
            SetActionSpeed(MonsterActionType.Attack1, 0.33f);
            SetActionSpeed(MonsterActionType.Attack2, 0.33f);
            SetActionSpeed(MonsterActionType.Shock, 0.50f);
            SetActionSpeed(MonsterActionType.Die, 0.55f);
        }

        protected override void OnIdle()
        {
            base.OnIdle();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mHunter2.wav", Position, listenerPosition);
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mBlackSkullAttack.wav", Position, listenerPosition);
        }

        public override void OnReceiveDamage()
        {
            base.OnReceiveDamage();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mBlackSkullAttack.wav", Position, listenerPosition);
        }

        public override void OnDeathAnimationStart()
        {
            base.OnDeathAnimationStart();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mBlackSkullDie.wav", Position, listenerPosition);
        }
    }
}
