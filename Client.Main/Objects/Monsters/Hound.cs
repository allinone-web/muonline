using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Models;
using Client.Main.Objects.Player;
using Client.Main.Core.Utilities;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(1, "Hound")]
    public class Hound : MonsterObject
    {
        protected readonly WeaponObject _defaultWeapon;
        public Hound()
        {
            RenderShadow = true;
            Scale = 0.85f; // Set according to C++ Setting_Monster
            HiddenMesh = 0; // SourceMain5.2: c->Object.HiddenMesh = 0
            MoveSpeed = 250f; // SourceMain5.2: default monster MoveSpeed (10 * 25 FPS)
            _defaultWeapon = new WeaponObject
            {
                LinkParentAnimation = false,
                ParentBoneLink = 19
            };
            Children.Add(_defaultWeapon);
        }

        public override async Task Load()
        {
            // Model Loading Type: 1 -> File Number: 1 + 1 = 2
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster02.bmd");
            if (_defaultWeapon.Parent == this)
            {
                var item = ItemDatabase.GetItemDefinition(0, 4); // Sword of Assassin
                if (item != null)
                    _defaultWeapon.Model = await BMDLoader.Instance.Prepare(item.TexturePath);
            }
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

        // Sound mapping based on C++ SetMonsterSound(MODEL_MONSTER01 + Type, 5, 6, 7, 8, 9);
        protected override void OnIdle()
        {
            base.OnIdle();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            // Play one of the idle sounds (index 0 or 1)
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mHound1.wav", Position, listenerPosition); // Index 0 -> Sound 5
            // SoundController.Instance.PlayBufferWithAttenuation("Sound/mHound2.wav", Position, listenerPosition); // Index 1 -> Sound 6
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            // Play one of the attack sounds (index 2 or 3)
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mHoundAttack1.wav", Position, listenerPosition); // Index 2 -> Sound 7
            // SoundController.Instance.PlayBufferWithAttenuation("Sound/mHoundAttack2.wav", Position, listenerPosition); // Index 3 -> Sound 8
        }

        public override void OnDeathAnimationStart()
        {
            base.OnDeathAnimationStart();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mHoundDie.wav", Position, listenerPosition); // Index 4 -> Sound 9
        }
    }
}
