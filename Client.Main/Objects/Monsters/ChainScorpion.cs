using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Models;
using Client.Main.Objects.Effects;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(27, "Chain Scorpion")]
    public class ChainScorpion : MonsterObject
    {
        private readonly LightEffect _tailLight;

        public ChainScorpion()
        {
            RenderShadow = true;
            Scale = 1.1f; // Set according to C++ Setting_Monster
            MoveSpeed = 250f; // SourceMain5.2: default monster MoveSpeed (10 * 25 FPS)
            Children.Add(new SourceMonsterSmokeEffect());
            _tailLight = new LightEffect
            {
                SourceBone = 7,
                Light = new Vector3(1f, 0.4f, 0.2f),
                Scale = 1f
            };
            Children.Add(_tailLight);
        }

        public override async Task Load()
        {
            // Model Loading Type: 20 -> File Number: 20 + 1 = 21
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster21.bmd");
            await base.Load();
            SetActionSpeed(MonsterActionType.Stop1, 0.25f);
            SetActionSpeed(MonsterActionType.Stop2, 0.20f);
            SetActionSpeed(MonsterActionType.Walk, 0.4f);
            SetActionSpeed(MonsterActionType.Attack1, 0.33f);
            SetActionSpeed(MonsterActionType.Attack2, 0.33f);
            SetActionSpeed(MonsterActionType.Shock, 0.50f);
            SetActionSpeed(MonsterActionType.Die, 0.55f);
        }

        // Sound mapping based on C++ SetMonsterSound(MODEL_MONSTER01 + Type, 77, 78, 79, 80, 81);
        protected override void OnIdle()
        {
            base.OnIdle();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            string sound = MuGame.Random.Next(2) == 0
                ? "Sound/mScorpion1.wav"
                : "Sound/mScorpion2.wav";
            SoundController.Instance.PlayBufferWithAttenuation(sound, Position, listenerPosition);
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            string sound = MuGame.Random.Next(2) == 0
                ? "Sound/mScorpionAttack1.wav"
                : "Sound/mScorpionAttack2.wav";
            SoundController.Instance.PlayBufferWithAttenuation(sound, Position, listenerPosition);
        }

        public override void OnReceiveDamage()
        {
            base.OnReceiveDamage();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            string sound = MuGame.Random.Next(2) == 0
                ? "Sound/mScorpionAttack1.wav"
                : "Sound/mScorpionAttack2.wav";
            SoundController.Instance.PlayBufferWithAttenuation(sound, Position, listenerPosition);
        }

        public override void OnDeathAnimationStart()
        {
            base.OnDeathAnimationStart();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mScorpionDie.wav", Position, listenerPosition); // Index 4 -> Sound 81
        }
    }
}
