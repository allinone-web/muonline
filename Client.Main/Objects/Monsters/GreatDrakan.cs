using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Objects.Effects;
using Client.Main.Models;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(75, "Great Drakan")]
    public class GreatDrakan : MonsterObject
    {
        public GreatDrakan()
        {
            Scale = 1.0f;
            MoveSpeed = 250f;
            Children.Add(new MonsterBoneFireEffect
            {
                SourceBone = 18,
                EmissionRate = 6.25f,
                ParticleScaleMin = 0.28f,
                ParticleScaleMax = 0.32f,
                StopOnDeath = true
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

        protected override void OnIdle()
        {
            base.OnIdle();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mDrakan1.wav", Position, listenerPosition);
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mDrakanAttack1.wav", Position, listenerPosition);
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
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mDrakanDie.wav", Position, listenerPosition);
        }
    }
}
