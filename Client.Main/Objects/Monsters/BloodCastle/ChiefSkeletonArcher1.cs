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
    [NpcInfo(85, "Chief Skeleton Archer")]
    public class ChiefSkeletonArcher1 : MonsterObject
    {
        private readonly WeaponObject _bow;

        public ChiefSkeletonArcher1()
        {
            Scale = 1.1f;
            MoveSpeed = 250f;
            _bow = new WeaponObject { LinkParentAnimation = false, ParentBoneLink = 39, ItemLevel = 1 };
            Children.Add(_bow);
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster47.bmd");
            var item = ItemDatabase.GetItemDefinition(4, 3); // Battle Bow
            if (item != null)
                _bow.Model = await BMDLoader.Instance.Prepare(item.TexturePath);
            await base.Load();
            SetActionSpeed(MonsterActionType.Stop1, 0.25f);
            SetActionSpeed(MonsterActionType.Stop2, 0.20f);
            SetActionSpeed(MonsterActionType.Walk, 0.34f);
            SetActionSpeed(MonsterActionType.Attack1, 0.33f);
            SetActionSpeed(MonsterActionType.Attack2, 0.33f);
            SetActionSpeed(MonsterActionType.Shock, 0.50f);
            SetActionSpeed(MonsterActionType.Die, 0.55f);
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mOrcArcherAttack1.wav", Position, listenerPosition);
        }

        public override void OnReceiveDamage()
        {
            base.OnReceiveDamage();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mOrcArcherAttack1.wav", Position, listenerPosition);
        }

        public override void OnDeathAnimationStart()
        {
            base.OnDeathAnimationStart();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mBullDie.wav", Position, listenerPosition);
        }
    }
}
