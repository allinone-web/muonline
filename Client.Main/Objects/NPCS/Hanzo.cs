using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Core.Utilities;
using Client.Main.Objects.Effects;
using Microsoft.Xna.Framework;
using System;
using System.Threading.Tasks;

namespace Client.Main.Objects.NPCS
{
    [NpcInfo(251, "Hanzo The Blacksmith")]
    public class Hanzo : NPCObject
    {
        private const int HammerBoneIndex = 17;
        private const double ForgeTriggerFrame = 5.0;
        private static readonly Vector3 ForgeLocalOffset = new(0f, -70f, 90f);

        public override bool CanRepair => true;

        private static readonly ushort[] Sequence = { 0, 1, 2 };

        private readonly BlacksmithForgeEffect _forgeEffect;
        private int _loopsTarget;
        private float _idleSecondsRemaining;
        private double _lastIdleAnimationFrame = -1.0;
        private bool _forgeTriggeredThisCycle;
        private float _lastAppliedForgeLuminosity = -1f;

        public Hanzo()
        {
            BlendMesh = 4;
            BlendMeshLight = 0f;

            _forgeEffect = new BlacksmithForgeEffect();
            Children.Add(_forgeEffect);
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare("NPC/Smith01.bmd");
            await base.Load();

            ResetSequence();
        }

        protected override void HandleClick()
        {
            var service = MuGame.Network?.GetCharacterService();
            if (service != null)
                _ = service.SendTalkToNpcRequestAsync(NetworkId);
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (!Visible)
                return;

            Vector3 forgePosition = GetForgeWorldPosition();
            _forgeEffect.SetForgeOrigin(forgePosition);
            UpdateForgeTrigger(forgePosition);
            UpdateForgeLighting();

            if (CurrentAction == 0 && !IsOneShotPlaying)
            {
                _idleSecondsRemaining -= (float)gameTime.ElapsedGameTime.TotalSeconds;
                if (_idleSecondsRemaining <= 0f)
                    PlayStepOne();
            }
        }

        private void UpdateForgeTrigger(Vector3 forgePosition)
        {
            if (CurrentAction != 0)
            {
                _lastIdleAnimationFrame = -1.0;
                _forgeTriggeredThisCycle = false;
                return;
            }

            double currentFrame = GetLoopedAnimationTime();
            bool firstObservedFrame = _lastIdleAnimationFrame < 0.0;
            bool wrapped = !firstObservedFrame && currentFrame < _lastIdleAnimationFrame;
            if (wrapped)
                _forgeTriggeredThisCycle = false;

            // Do not rely on observing one exact animation frame. A busy loading/update
            // frame can advance directly from before 5.0 to after it, or the first sampled
            // frame can already be inside the impact window.
            bool crossedTrigger = firstObservedFrame
                ? currentFrame >= ForgeTriggerFrame
                : !wrapped &&
                  _lastIdleAnimationFrame < ForgeTriggerFrame &&
                  currentFrame >= ForgeTriggerFrame;

            if (!_forgeTriggeredThisCycle && crossedTrigger)
            {
                Vector3 strikePosition = GetHammerBoneWorldPosition(forgePosition);
                _forgeEffect.EmitBurst(strikePosition, Angle);
                PlayForgeSound();
                _forgeTriggeredThisCycle = true;
            }

            _lastIdleAnimationFrame = currentFrame;
        }

        private Vector3 GetHammerBoneWorldPosition(Vector3 fallbackPosition)
        {
            Matrix[] bones = GetBoneTransforms();
            if (bones == null || HammerBoneIndex < 0 || HammerBoneIndex >= bones.Length)
                return fallbackPosition;

            Vector3 position = Vector3.Transform(bones[HammerBoneIndex].Translation, WorldPosition);
            return IsFinite(position) ? position : fallbackPosition;
        }

        private Vector3 GetForgeWorldPosition()
        {
            // The original effect used (0, -70, 90) as a child-local position. Transform that
            // point by Hanzo's complete world matrix so the smoke remains over the forge for
            // every NPC rotation and map placement instead of using an arbitrary world-axis
            // offset.
            Vector3 position = Vector3.Transform(ForgeLocalOffset, WorldPosition);
            return IsFinite(position) ? position : WorldPosition.Translation;
        }

        private static bool IsFinite(Vector3 value) =>
            float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

        private void PlayForgeSound()
        {
            if (World is not WalkableWorldControl walkable || walkable.Walker == null)
                return;

            SoundController.Instance.PlayBufferWithAttenuation(
                "Sound/nBlackSmith.wav",
                WorldPosition.Translation,
                walkable.Walker.Position,
                maxDistance: 2000f,
                loop: false);
        }

        private void UpdateForgeLighting()
        {
            float luminosity = _forgeEffect.CurrentLuminosity;
            if (MathF.Abs(luminosity - _lastAppliedForgeLuminosity) <= 0.001f)
                return;

            _lastAppliedForgeLuminosity = luminosity;
            BlendMeshLight = luminosity;
            Light = luminosity * new Vector3(1f, 0.4f, 0f);
        }

        private void ResetSequence()
        {
            _loopsTarget = MuGame.Random.Next(4, 7);

            if (!_animationController.TryGetActionDurationSeconds(Sequence[0], out float secondsPerLoop))
                secondsPerLoop = 1f;

            _idleSecondsRemaining = secondsPerLoop * _loopsTarget;
            PlayAction(Sequence[0]);
        }

        private void PlayStepOne()
        {
            _animationController.PlayOneShot(
                Sequence[1],
                returnActionIndex: Sequence[0],
                onCompleted: PlayStepTwo);
        }

        private void PlayStepTwo()
        {
            _animationController.PlayOneShot(
                Sequence[2],
                returnActionIndex: Sequence[0],
                onCompleted: ResetSequence);
        }
    }
}
