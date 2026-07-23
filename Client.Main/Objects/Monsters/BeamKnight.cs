using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Objects.Effects;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(61, "Beam Knight")]
    public class BeamKnight : MonsterObject
    {
        private GlowingEyesEffect _eyeGlow;

        public BeamKnight()
        {
            Scale = 1.5f;

            // Eyes: bones 8 (Right), 9 (Left) — original MoveEye(o, b, 8, 9)
            _eyeGlow = new GlowingEyesEffect
            {
                LeftEyeBone = 9,
                RightEyeBone = 8,
                GlowColor = new Color(40, 100, 255),
                GlowScale = 0.8f,
                GlowAlpha = 0.85f,
                TrailWidth = 4f,
                TrailDuration = 0.5f
            };
            Children.Add(_eyeGlow);
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster45.bmd");
            await base.Load();
            // C++: Models[MODEL_MONSTER01+Type].BoneHead = 6;
        }

        // Sound mapping based on C++ SetMonsterSound(MODEL_MONSTER01 + Type, 154, 154, 155, 155, 156);
        protected override void OnIdle()
        {
            base.OnIdle();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/death1.wav", Position, listenerPosition); // Sound 154
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/death_attack1.wav", Position, listenerPosition); // Sound 155
        }

        public override void OnDeathAnimationStart()
        {
            base.OnDeathAnimationStart();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/death_die.wav", Position, listenerPosition); // Sound 156
        }
    }
}
