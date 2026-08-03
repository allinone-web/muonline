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
    [NpcInfo(38, "Balrog")]
    public class Balrog : MonsterObject
    {
        private WeaponObject _rightHandWeapon;
        public Balrog()
        {
            RenderShadow = true;
            Scale = 1.6f; // Set according to C++ Setting_Monster
            MoveSpeed = 250f; // SourceMain5.2: default monster MoveSpeed (10 * 25 FPS)
            _rightHandWeapon = new WeaponObject
            {
                LinkParentAnimation = false,
                ParentBoneLink = 17
            };
            Children.Add(_rightHandWeapon);
        }

        public override async Task Load()
        {
            // Model Loading Type: 27 -> File Number: 27 + 1 = 28
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster28.bmd");
            var item = ItemDatabase.GetItemDefinition(3, 9); // Bill of Balrog
            if (item != null)
                _rightHandWeapon.Model = await BMDLoader.Instance.Prepare(item.TexturePath);
            await base.Load();
            SetActionSpeed(MonsterActionType.Stop1, 0.25f);
            SetActionSpeed(MonsterActionType.Stop2, 0.20f);
            SetActionSpeed(MonsterActionType.Walk, 0.34f);
            SetActionSpeed(MonsterActionType.Attack1, 0.33f);
            SetActionSpeed(MonsterActionType.Attack2, 0.33f);
            SetActionSpeed(MonsterActionType.Shock, 0.50f);
            SetActionSpeed(MonsterActionType.Die, 0.55f);
            // C++: Models[MODEL_MONSTER01+Type].BoneHead = 6;
            // C++: Models[MODEL_MONSTER01+Type].StreamMesh = 1; // May need special handling for streaming meshes if applicable
        }

        // Sound mapping based on C++ SetMonsterSound(MODEL_MONSTER01 + Type, 108, 109, 110, 111, 112);
        protected override void OnIdle()
        {
            base.OnIdle();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            string sound = MuGame.Random.Next(2) == 0
                ? "Sound/mBalrog1.wav"
                : "Sound/mBalrog2.wav";
            SoundController.Instance.PlayBufferWithAttenuation(sound, Position, listenerPosition);
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            string sound = MuGame.Random.Next(2) == 0
                ? "Sound/mWizardAttack2.wav"
                : "Sound/mGorgonAttack2.wav";
            SoundController.Instance.PlayBufferWithAttenuation(sound, Position, listenerPosition);
        }

        public override void OnReceiveDamage()
        {
            base.OnReceiveDamage();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            string sound = MuGame.Random.Next(2) == 0
                ? "Sound/mWizardAttack2.wav"
                : "Sound/mGorgonAttack2.wav";
            SoundController.Instance.PlayBufferWithAttenuation(sound, Position, listenerPosition);
        }

        public override void OnDeathAnimationStart()
        {
            base.OnDeathAnimationStart();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mBalrogDie.wav", Position, listenerPosition); // Index 4 -> Sound 112
        }
    }
}
