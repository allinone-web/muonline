using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects.Effects;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics; // Needed for BlendState
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(36, "Shadow")]
    public class ShadowMonster : MonsterObject // Renamed
    {
        public ShadowMonster() : this(false)
        {
        }

        protected ShadowMonster(bool poisonVariant)
        {
            RenderShadow = true;
            Scale = 1.2f; // Set according to C++ Setting_Monster
            MoveSpeed = 250f; // SourceMain5.2: default monster MoveSpeed (10 * 25 FPS)
            Children.Add(new ShadowMonsterVisualEffect { PoisonVariant = poisonVariant });
            Children.Add(new MonsterBoneEnergyEffect
            {
                EmitOnlyDuringAttack = true
            });
        }

        public override async Task Load()
        {
            // Model Loading Type: 28 -> File Number: 28 + 1 = 29
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster29.bmd");
            await base.Load();

            SetActionSpeed(MonsterActionType.Stop1, 0.25f);
            SetActionSpeed(MonsterActionType.Stop2, 0.20f);
            SetActionSpeed(MonsterActionType.Walk, 0.30f);
            SetActionSpeed(MonsterActionType.Attack1, 0.33f);
            SetActionSpeed(MonsterActionType.Attack2, 0.33f);
            SetActionSpeed(MonsterActionType.Shock, 0.50f);
            SetActionSpeed(MonsterActionType.Die, 0.55f);
            // C++: Models[MODEL_MONSTER01+Type].BoneHead = 5;
        }

        // Sound mapping based on C++ SetMonsterSound(MODEL_MONSTER01 + Type, 113, 114, 115, 116, 117);
        protected override void OnIdle()
        {
            base.OnIdle();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            string sound = MuGame.Random.Next(2) == 0
                ? "Sound/mShadow1.wav"
                : "Sound/mShadow2.wav";
            SoundController.Instance.PlayBufferWithAttenuation(sound, Position, listenerPosition);
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            string sound = MuGame.Random.Next(2) == 0
                ? "Sound/mShadowAttack1.wav"
                : "Sound/mShadowAttack2.wav";
            SoundController.Instance.PlayBufferWithAttenuation(sound, Position, listenerPosition);
        }

        public override void OnReceiveDamage()
        {
            base.OnReceiveDamage();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            string sound = MuGame.Random.Next(2) == 0
                ? "Sound/mShadowAttack1.wav"
                : "Sound/mShadowAttack2.wav";
            SoundController.Instance.PlayBufferWithAttenuation(sound, Position, listenerPosition);
        }

        public override void OnDeathAnimationStart()
        {
            base.OnDeathAnimationStart();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mShadowDie.wav", Position, listenerPosition); // Index 4 -> Sound 117
        }
    }
}
