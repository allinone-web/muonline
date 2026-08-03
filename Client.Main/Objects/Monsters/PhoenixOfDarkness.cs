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
    [NpcInfo(77, "Phoenix Of Darkness")]
    public class PhoenixOfDarkness : MonsterObject
    {
        private readonly PhoenixBodyObject _body;

        public PhoenixOfDarkness()
        {
            Scale = 1.0f;
            MoveSpeed = 250f;
            RenderShadow = false;

            _body = new PhoenixBodyObject
            {
                RenderShadow = false
            };
            Children.Add(_body);
        }

        public override async Task Load()
        {
            // SourceMain5.2 renders MODEL_DARK_PHEONIX_SHIELD (Monster57)
            // and then MODEL_DARK_PHOENIX (Monster56) with the same pose.
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster57.bmd");
            _body.Model = await BMDLoader.Instance.Prepare($"Monster/Monster56.bmd");
            BlendMesh = 0;
            BlendMeshLight = 0.6f;
            await base.Load();
            SetActionSpeed(MonsterActionType.Stop1, 0.25f);
            SetActionSpeed(MonsterActionType.Stop2, 0.20f);
            SetActionSpeed(MonsterActionType.Walk, 0.34f);
            SetActionSpeed(MonsterActionType.Attack1, 0.33f);
            SetActionSpeed(MonsterActionType.Attack2, 0.33f);
            SetActionSpeed(MonsterActionType.Shock, 0.50f);
            SetActionSpeed(MonsterActionType.Die, 0.22f);

            // Monster56 and Monster57 use different skeletons (61 vs 48 bones).
            // Keep the original action timing, but never share Monster57's bone palette
            // with the body model.
            _body.CopyActionSpeedsFrom(this);
        }

        // Sound mapping based on C++ SetMonsterSound(MODEL_MONSTER01 + Type, 183, 184, 185, 185, -1);
        protected override void OnIdle()
        {
            base.OnIdle();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mPhoenix1.wav", Position, listenerPosition); // Sound 183
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            base.OnPerformAttack(attackType);
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mPhoenixAttack1.wav", Position, listenerPosition); // Sound 185
        }

        public override void OnReceiveDamage()
        {
            base.OnReceiveDamage();
            Vector3 listenerPosition = ((WalkableWorldControl)World).Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/mPhoenixAttack1.wav", Position, listenerPosition);
        }

        // Note: No death sound according to C++ mapping (death sound index was -1)

        private sealed class PhoenixBodyObject : ModelObject
        {
            public PhoenixBodyObject()
            {
                // MonsterObject uses this animation speed for the Phoenix root.
                AnimationSpeed = 6f;
            }

            public override void Update(GameTime gameTime)
            {
                if (Parent is PhoenixOfDarkness phoenix)
                {
                    CurrentAction = phoenix.CurrentAction;
                    HoldOnLastFrame = CurrentAction == (int)MonsterActionType.Die || phoenix.IsDead;
                }

                base.Update(gameTime);
            }

            public void CopyActionSpeedsFrom(ModelObject source)
            {
                if (source?.Model?.Actions == null || Model?.Actions == null)
                    return;

                int count = Math.Min(source.Model.Actions.Length, Model.Actions.Length);
                for (int i = 0; i < count; i++)
                {
                    if (source.Model.Actions[i] != null && Model.Actions[i] != null)
                        Model.Actions[i].PlaySpeed = source.Model.Actions[i].PlaySpeed;
                }
            }
        }
    }
}
