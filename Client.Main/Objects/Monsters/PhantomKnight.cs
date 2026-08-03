using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Objects.Player;
using Client.Main.Core.Utilities;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Client.Main.Models;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(72, "Phantom Knight")]
    public class PhantomKnight : MonsterObject
    {
        private WeaponObject _rightHandWeapon;
        public PhantomKnight()
        {
            Scale = 1.45f;
            MoveSpeed = 250f;
            _rightHandWeapon = new WeaponObject
            {
                LinkParentAnimation = false,
                ParentBoneLink = 30,
                ItemLevel = 5
            };
            Children.Add(_rightHandWeapon);
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster54.bmd");
            var item = ItemDatabase.GetItemDefinition(0, 17); // Dark Breaker
            if (item != null)
                _rightHandWeapon.Model = await BMDLoader.Instance.Prepare(item.TexturePath);

            await base.Load();
            SetActionSpeed(MonsterActionType.Stop1, 0.25f);
            SetActionSpeed(MonsterActionType.Stop2, 0.20f);
            SetActionSpeed(MonsterActionType.Walk, 0.34f);
            SetActionSpeed(MonsterActionType.Attack1, 0.33f);
            SetActionSpeed(MonsterActionType.Attack2, 0.33f);
            SetActionSpeed(MonsterActionType.Shock, 0.50f);
            SetActionSpeed(MonsterActionType.Die, 0.22f);
        }

        // Sound mapping based on C++ SetMonsterSound(MODEL_MONSTER01 + Type, 168, 168, 169, 169, 170);
        protected override void OnIdle()
        {
            base.OnIdle();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mPhantom1.wav", Position, listenerPosition); // Sound 168
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mPhantomAttack1.wav", Position, listenerPosition); // Sound 169
        }

        public override void OnReceiveDamage()
        {
            base.OnReceiveDamage();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mPhantomAttack1.wav", Position, listenerPosition);
        }

        public override void OnDeathAnimationStart()
        {
            base.OnDeathAnimationStart();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mPhantomDie.wav", Position, listenerPosition); // Sound 170
        }
    }
}
