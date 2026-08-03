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
    [NpcInfo(47, "Valkyrie")]
    public class Valkyrie : MonsterObject
    {
        private WeaponObject _rightHandWeapon;

        public Valkyrie()
        {
            RenderShadow = true;
            Scale = 1.1f; // Set according to C++ Setting_Monster
            MoveSpeed = 250f; // SourceMain5.2: default monster MoveSpeed (10 * 25 FPS)
            BlendMesh = 0;
            BlendMeshLight = 1.0f;
            _rightHandWeapon = new WeaponObject
            {
                LinkParentAnimation = false,
                ParentBoneLink = 30
            };
            Children.Add(_rightHandWeapon);
        }

        public override async Task Load()
        {
            // Model Loading Type: 35 -> File Number: 35 + 1 = 36
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster36.bmd");
            var weapon = ItemDatabase.GetItemDefinition(4, 13); // Bluewing Crossbow
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

        // Sound mapping based on C++ SetMonsterSound(MODEL_MONSTER01 + Type, 135, 135, 136, 136, 137);
        protected override void OnIdle()
        {
            base.OnIdle();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mValkyrie1.wav", Position, listenerPosition);
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mBaliAttack2.wav", Position, listenerPosition);
        }

        public override void OnReceiveDamage()
        {
            base.OnReceiveDamage();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mBaliAttack2.wav", Position, listenerPosition);
        }

        public override void OnDeathAnimationStart()
        {
            base.OnDeathAnimationStart();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mValkyrieDie.wav", Position, listenerPosition);
        }
    }
}
