using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Models;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(144, "Death Angel")]
    public class DeathAngel : MonsterObject
    {
        public DeathAngel()
        {
            Scale = 1.2f;
            MoveSpeed = 250f;
            BlendMesh = 1;
            BlendMeshLight = 1f;
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster64.bmd");
            await base.Load();
            SetActionSpeed(MonsterActionType.Stop1, 0.25f);
            SetActionSpeed(MonsterActionType.Stop2, 0.20f);
            SetActionSpeed(MonsterActionType.Walk, 0.34f);
            SetActionSpeed(MonsterActionType.Attack1, 0.33f);
            SetActionSpeed(MonsterActionType.Attack2, 0.33f);
            SetActionSpeed(MonsterActionType.Shock, 0.50f);
            SetActionSpeed(MonsterActionType.Die, 0.10f);
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            if (Status != GameControlStatus.Ready)
                return;

            BlendMeshLight = MathHelper.Clamp(
                MathF.Sin((float)gameTime.TotalGameTime.TotalMilliseconds * 0.001f) * 0.7f + 0.3f,
                0f,
                1f);
        }

        protected override void OnIdle()
        {
            base.OnIdle();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mDAngelIdle.wav", Position, listenerPosition);
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mDAngelAttack.wav", Position, listenerPosition);
        }

        public override void OnReceiveDamage() => OnPerformAttack(1);

        public override void OnDeathAnimationStart()
        {
            base.OnDeathAnimationStart();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mDAngelDeath.wav", Position, listenerPosition);
        }
    }
}
