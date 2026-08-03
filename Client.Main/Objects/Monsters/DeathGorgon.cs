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
    [NpcInfo(35, "Death Gorgon")]
    public class DeathGorgon : MonsterObject
    {
        private WeaponObject _rightHandWeapon;
        private WeaponObject _leftHandWeapon;
        private readonly FieryAuraEffect _fireAura;

        public DeathGorgon()
        {
            RenderShadow = true;
            Scale = 1.3f; // Set according to C++ Setting_Monster
            MoveSpeed = 250f; // SourceMain5.2: default monster MoveSpeed (10 * 25 FPS)
            BlendMesh = 1;
            BlendMeshLight = 1f;
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
            _fireAura = new FieryAuraEffect();
            Children.Add(_fireAura);
        }

        public override async Task Load()
        {
            // Model Loading Type: 11 -> File Number: 11 + 1 = 12
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster12.bmd");
            var weapon = ItemDatabase.GetItemDefinition(1, 8); // Crescent Axe
            if (weapon != null)
            {
                _rightHandWeapon.Model = await BMDLoader.Instance.Prepare(weapon.TexturePath);
                _leftHandWeapon.Model = await BMDLoader.Instance.Prepare(weapon.TexturePath);
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

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (Status != GameControlStatus.Ready)
                return;

            if (IsDead)
                return;

            BlendMesh = 1;
            BlendMeshLight = MuGame.Random.Next(10) * 0.1f;
        }

        // Sound mapping based on C++ SetMonsterSound(MODEL_MONSTER01 + Type, 45, 46, 47, 48, 49);
        protected override void OnIdle()
        {
            base.OnIdle();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            string sound = MuGame.Random.Next(2) == 0
                ? "Sound/mGorgon1.wav"
                : "Sound/mGorgon2.wav";
            SoundController.Instance.PlayBufferWithAttenuation(sound, Position, listenerPosition);
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            string sound = MuGame.Random.Next(2) == 0
                ? "Sound/mGorgonAttack1.wav"
                : "Sound/mGorgonAttack2.wav";
            SoundController.Instance.PlayBufferWithAttenuation(sound, Position, listenerPosition);
        }

        public override void OnReceiveDamage()
        {
            base.OnReceiveDamage();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            string sound = MuGame.Random.Next(2) == 0
                ? "Sound/mGorgonAttack1.wav"
                : "Sound/mGorgonAttack2.wav";
            SoundController.Instance.PlayBufferWithAttenuation(sound, Position, listenerPosition);
        }

        public override void OnDeathAnimationStart()
        {
            base.OnDeathAnimationStart();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mGorgonDie.wav", Position, listenerPosition); // Index 4 -> Sound 49
        }
    }
}
