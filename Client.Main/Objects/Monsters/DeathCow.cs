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
    [NpcInfo(41, "Death Cow")]
    public class DeathCow : MonsterObject
    {
        private WeaponObject _rightHandWeapon;
        private GlowingEyesEffect _eyeGlow;

        public DeathCow()
        {
            RenderShadow = true;
            Scale = 1.1f;
            MoveSpeed = 250f; // SourceMain5.2: default monster MoveSpeed (10 * 25 FPS)
            _rightHandWeapon = new WeaponObject
            {
                LinkParentAnimation = false,
                ParentBoneLink = 42
            };
            Children.Add(_rightHandWeapon);

            // Eyes: bones 22 (L), 23 (R) — original RenderEye(o, 22, 23)
            _eyeGlow = new GlowingEyesEffect
            {
                LeftEyeBone = 22,
                RightEyeBone = 23,
                GlowColor = Color.White,
                EnableTrail = false
            };
            Children.Add(_eyeGlow);
        }

        public override async Task Load()
        {
            // Model Loading Type: 30 -> File Number: 30 + 1 = 31
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster31.bmd");
            var weapon = ItemDatabase.GetItemDefinition(2, 3); // Great Hammer
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
        }

        // Sound mapping based on C++ (uses Bull sounds)
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
            Blood = false;
            if (World != null)
            {
                var effect = new SkeletonDeathBoneEffect(Position, Angle);
                World.Objects.Add(effect);
                _ = effect.Load();
            }
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mBullDie.wav", Position, listenerPosition);
        }
    }
}
