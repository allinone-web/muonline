using Client.Main.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System;

namespace Client.Main.Controllers;

internal sealed class MainPlayerCameraController
{
    private const int MiddleMouseDragThresholdPixels = 3;

    private float _currentCameraDistance = Constants.DEFAULT_CAMERA_DISTANCE;
    private float _targetCameraDistance = Constants.DEFAULT_CAMERA_DISTANCE;
    private int _previousScrollValue;
    private bool _mouseScrollToZoom = true;
    private float _cameraYaw = Constants.DEFAULT_CAMERA_YAW;
    private float _cameraPitch = Constants.DEFAULT_CAMERA_PITCH;
    private int _middleMouseTravel;

    public bool MouseScrollToZoom
    {
        get => _mouseScrollToZoom;
        set => _mouseScrollToZoom = value;
    }

    public void Initialize()
    {
        _previousScrollValue = MuGame.Instance.Mouse.ScrollWheelValue;
        ResetToDefault(immediateDistance: true);
        _middleMouseTravel = 0;
    }

    public void Update(GameTime gameTime)
    {
        float deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _currentCameraDistance = MathHelper.Lerp(
            _currentCameraDistance,
            _targetCameraDistance,
            Constants.ZOOM_SPEED * deltaTime);

        var mouse = MuGame.Instance.Mouse;
        var previousMouse = MuGame.Instance.PrevMouseState;

        if (MouseScrollToZoom)
        {
            int scrollDelta = mouse.ScrollWheelValue - _previousScrollValue;
            if (scrollDelta != 0)
            {
                float zoomChange = -scrollDelta * Constants.ZOOM_SPEED;
                _targetCameraDistance = MathHelper.Clamp(
                    _targetCameraDistance + zoomChange,
                    Constants.MIN_CAMERA_DISTANCE,
                    Constants.MAX_CAMERA_DISTANCE);
            }
        }

        _previousScrollValue = mouse.ScrollWheelValue;

        bool middlePressed = mouse.MiddleButton == ButtonState.Pressed;
        bool middleWasPressed = previousMouse.MiddleButton == ButtonState.Pressed;

        if (middlePressed && !middleWasPressed)
        {
            _middleMouseTravel = 0;
        }

        if (middlePressed)
        {
            int deltaX = mouse.X - previousMouse.X;
            int deltaY = mouse.Y - previousMouse.Y;

            if (deltaX != 0 || deltaY != 0)
            {
                _middleMouseTravel += Math.Abs(deltaX) + Math.Abs(deltaY);
                _cameraYaw = MathHelper.WrapAngle(
                    _cameraYaw - deltaX * Constants.ROTATION_SENSITIVITY);
                _cameraPitch = MathHelper.Clamp(
                    _cameraPitch - deltaY * Constants.ROTATION_SENSITIVITY,
                    Constants.MIN_PITCH,
                    Constants.MAX_PITCH);
            }
        }

        if (!middlePressed && middleWasPressed)
        {
            // A click resets the complete view. A drag keeps the new yaw, pitch and zoom.
            if (_middleMouseTravel < MiddleMouseDragThresholdPixels)
                ResetToDefault(immediateDistance: true);

            _middleMouseTravel = 0;
        }
    }

    public void Apply(Vector3 target)
    {
        float x = _currentCameraDistance * MathF.Cos(_cameraPitch) * MathF.Sin(_cameraYaw);
        float y = _currentCameraDistance * MathF.Cos(_cameraPitch) * MathF.Cos(_cameraYaw);
        float z = _currentCameraDistance * MathF.Sin(_cameraPitch);
        var cameraPosition = target + new Vector3(x, y, z);

        Camera.Instance.FOV = 35 * Constants.FOV_SCALE;
        Camera.Instance.Position = cameraPosition;
        Camera.Instance.Target = target;
    }

    private void ResetToDefault(bool immediateDistance)
    {
        _cameraYaw = Constants.DEFAULT_CAMERA_YAW;
        _cameraPitch = Constants.DEFAULT_CAMERA_PITCH;
        _targetCameraDistance = Constants.DEFAULT_CAMERA_DISTANCE;

        if (immediateDistance)
            _currentCameraDistance = Constants.DEFAULT_CAMERA_DISTANCE;
    }
}
