using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Microsoft.Xna.Framework;
using System;
using System.Threading.Tasks;

namespace Client.Main.Objects.Worlds.Devias
{
    /// <summary>
    /// Devias door movement from SourceMain5.2 ZzzObject.cpp.
    /// The original does not pair doors or animate a fixed 90 degree swing:
    /// it evaluates the hero distance to each door every frame and moves the
    /// door toward its stored target position/angle.
    /// </summary>
    public sealed class SteelDoorObject : MapTileObject
    {
        private const float SourceDoorRange = 200f;
        private const float SourcePositionFollow = 0.2f;
        private const float SourceAngleStepDegrees = 10f;

        private Vector3 _closedPosition;
        private float _closedAngle;
        private bool _wasInRange;

        private bool IsSlidingDoor => Type == 86;

        public override async Task Load()
        {
            LightEnabled = true;
            _closedPosition = Position;
            _closedAngle = Angle.Z;
            await base.Load();
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (World is not WalkableWorldControl world || world.Walker == null)
                return;

            Vector2 heroPosition = new(world.Walker.Position.X, world.Walker.Position.Y);
            Vector2 targetPosition = new(_closedPosition.X, _closedPosition.Y);
            float distance = Vector2.Distance(heroPosition, targetPosition);
            bool inRange = distance < SourceDoorRange;

            if (inRange && !_wasInRange)
            {
                SoundController.Instance.PlayBuffer(
                    IsSlidingDoor ? "Sound/aCastleDoor.wav" : "Sound/aDoor.wav");
            }
            _wasInRange = inRange;

            float frameFactor = MathHelper.Clamp(
                (float)gameTime.ElapsedGameTime.TotalSeconds * 25f,
                0f,
                4f);

            if (inRange)
            {
                if (IsSlidingDoor)
                    ApplySourceSlidingPosition(distance);
                else
                    ApplySourceOpenAngle(distance);
            }
            else
            {
                Position = new Vector3(
                    MathHelper.Lerp(Position.X, _closedPosition.X, SourcePositionFollow * frameFactor),
                    MathHelper.Lerp(Position.Y, _closedPosition.Y, SourcePositionFollow * frameFactor),
                    _closedPosition.Z);
                Angle = new Vector3(
                    Angle.X,
                    Angle.Y,
                    TurnAngle(Angle.Z, _closedAngle, MathHelper.ToRadians(SourceAngleStepDegrees * frameFactor)));
            }
        }

        private void ApplySourceSlidingPosition(float distance)
        {
            float offset = (SourceDoorRange - distance) * 2f;
            float direction = GetCardinalDirectionDegrees();
            Vector3 position = _closedPosition;

            if (direction == 90f)
                position.Y += offset;
            else if (direction == 270f)
                position.Y -= offset;
            else if (direction == 0f)
                position.X += offset;
            else if (direction == 180f)
                position.X -= offset;

            Position = position;
        }

        private void ApplySourceOpenAngle(float distance)
        {
            float openDistance = SourceDoorRange - distance;
            float direction = GetCardinalDirectionDegrees();
            float angleDegrees = direction switch
            {
                90f => 30f - openDistance * 0.5f,
                270f => 330f + openDistance * 0.5f,
                0f => 300f - openDistance * 0.5f,
                180f => 240f + openDistance * 0.5f,
                _ => MathHelper.ToDegrees(_closedAngle)
            };

            Angle = new Vector3(
                Angle.X,
                Angle.Y,
                MathHelper.ToRadians(angleDegrees));
        }

        private float GetCardinalDirectionDegrees()
        {
            float degrees = MathHelper.ToDegrees(_closedAngle) % 360f;
            if (degrees < 0f)
                degrees += 360f;

            float cardinal = MathF.Round(degrees / 90f) * 90f;
            return cardinal >= 360f ? 0f : cardinal;
        }

        private static float TurnAngle(float current, float target, float maxStep)
        {
            float delta = MathHelper.WrapAngle(target - current);
            return current + MathHelper.Clamp(delta, -maxStep, maxStep);
        }
    }
}
