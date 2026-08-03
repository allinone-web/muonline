using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Models;
using Client.Main.Objects.Effects;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(73, "Drakan")]
    public class Drakan : MonsterObject
    {
        public Drakan()
        {
            Scale = 0.8f;
            MoveSpeed = 250f;
            Children.Add(new MonsterBoneSpriteEffect
            {
                BoneIndices = new[]
                {
                    13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26,
                    52, 53, 54, 55, 56, 57, 58
                },
                PrimaryTexturePath = "Effect/flare01.jpg",
                PrimaryScale = 0.8f,
                LightColor = new Color(26, 26, 255),
                HideDuringDeath = true
            });
            // Set meshes that should NOT use blending (equivalent to NoneBlendMesh = true)
            NoneBlendMeshes.Add(0); // Mesh 0: no blending
            NoneBlendMeshes.Add(3); // Mesh 3: no blending
            NoneBlendMeshes.Add(4); // Mesh 4: no blending
            // Mesh 1 and 2 will use blending (not in NoneBlendMeshes set)
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster55.bmd");
            await base.Load();
            SetActionSpeed(MonsterActionType.Stop1, 0.25f);
            SetActionSpeed(MonsterActionType.Stop2, 0.20f);
            SetActionSpeed(MonsterActionType.Walk, 0.34f);
            SetActionSpeed(MonsterActionType.Attack1, 0.33f);
            SetActionSpeed(MonsterActionType.Attack2, 0.33f);
            SetActionSpeed(MonsterActionType.Shock, 0.50f);
            SetActionSpeed(MonsterActionType.Die, 0.22f);
        }

        // Sound mapping based on C++ SetMonsterSound(MODEL_MONSTER01 + Type, 165, 165, 166, 166, 167);
        protected override void OnIdle()
        {
            base.OnIdle();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mDrakan1.wav", Position, listenerPosition); // Sound 165
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mDrakanAttack1.wav", Position, listenerPosition); // Sound 166
        }

        public override void OnReceiveDamage()
        {
            base.OnReceiveDamage();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mDrakanAttack1.wav", Position, listenerPosition);
        }

        public override void OnDeathAnimationStart()
        {
            base.OnDeathAnimationStart();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mDrakanDie.wav", Position, listenerPosition); // Sound 167
        }
    }
}
