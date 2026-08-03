using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Models;
using Client.Main.Objects.Effects;
using Microsoft.Xna.Framework;
using System;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(20, "Elite Yeti")]
    public class EliteYeti : MonsterObject // Note: Sounds differ slightly from Yeti in C++
    {
        public EliteYeti()
        {
            RenderShadow = true;
            Scale = 1.4f; // Set according to C++ Setting_Monster
            MoveSpeed = 250f; // SourceMain5.2: default monster MoveSpeed (10 * 25 FPS)
            Blood = true;
            Children.Add(new MonsterBreathEffect
            {
                SourceBone = 22,
                EmitWhenNoTriggers = true,
                EmissionRate = 6.25f,
                SourceOffset = Vector3.Zero
            });
        }

        public override async Task Load()
        {
            // Model Loading Type: 13 -> File Number: 13 + 1 = 14
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster14.bmd");
            await base.Load();

            SetActionSpeed(MonsterActionType.Stop1, 0.25f);
            SetActionSpeed(MonsterActionType.Stop2, 0.20f);
            SetActionSpeed(MonsterActionType.Walk, 0.28f);   // Override from default 0.34f
            SetActionSpeed(MonsterActionType.Attack1, 0.33f);
            SetActionSpeed(MonsterActionType.Attack2, 0.33f);
            SetActionSpeed(MonsterActionType.Shock, 0.5f);
            SetActionSpeed(MonsterActionType.Die, 0.55f);
            // C++: Models[MODEL_ELITE_YETI].BoneHead = 20;
        }

        // Sound mapping based on C++ SetMonsterSound(MODEL_ELITE_YETI, 68, 69, 70, 70, 71);
        protected override void OnIdle()
        {
            base.OnIdle();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            string sound = MuGame.Random.Next(2) == 0
                ? "Sound/mYeti1.wav"
                : "Sound/mYeti2.wav";
            SoundController.Instance.PlayBufferWithAttenuation(sound, Position, listenerPosition);
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mYetiAttack1.wav", Position, listenerPosition);
        }

        public override void OnReceiveDamage()
        {
            base.OnReceiveDamage();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mYetiAttack1.wav", Position, listenerPosition);
        }

        public override void OnDeathAnimationStart()
        {
            base.OnDeathAnimationStart();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mYetiDie.wav", Position, listenerPosition); // Index 4 -> Sound 71
        }
    }
}
