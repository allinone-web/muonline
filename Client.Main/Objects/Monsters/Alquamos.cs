using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(69, "Alquamos")]
    public class Alquamos : MonsterObject
    {
        public Alquamos()
        {
            Scale = 1.0f;
            MoveSpeed = 250f;
            BlendMesh = 0; // SourceMain5.2 CreateCharacter: BlendMesh = 0
            BlendMeshLight = 1.0f;
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster51.bmd");
            await base.Load();
            SetActionSpeed(MonsterActionType.Stop1, 0.25f);
            SetActionSpeed(MonsterActionType.Stop2, 0.20f);
            SetActionSpeed(MonsterActionType.Walk, 0.34f);
            SetActionSpeed(MonsterActionType.Attack1, 0.33f);
            SetActionSpeed(MonsterActionType.Attack2, 0.33f);
            SetActionSpeed(MonsterActionType.Shock, 0.50f);
            SetActionSpeed(MonsterActionType.Die, 0.22f);
        }

        // Sound mapping based on C++ SetMonsterSound(MODEL_MONSTER01 + Type, 175, 175, 175, 175, 176);
        protected override void OnIdle()
        {
            base.OnIdle();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            // Note: C++ had commented out idle sound, but we'll use attack sound for idle as per mapping
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mAlquamosAttack1.wav", Position, listenerPosition); // Sound 175
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mAlquamosAttack1.wav", Position, listenerPosition); // Sound 175
        }

        public override void OnDeathAnimationStart()
        {
            base.OnDeathAnimationStart();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mAlquamosDie.wav", Position, listenerPosition); // Sound 176
        }
    }
}
