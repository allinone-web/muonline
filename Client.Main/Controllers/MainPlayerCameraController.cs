using Client.Main.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System;

namespace Client.Main.Controllers;

internal sealed class MainPlayerCameraController
{
    private float _currentCameraDistance = Constants.DEFAULT_CAMERA_DISTANCE;
    private float _targetCameraDistance = Constants.DEFAULT_CAMERA_DISTANCE;
    private int _previousScrollValue;
    private bool _mouseScrollToZoom = true;
    private float _cameraYaw = Constants.DEFAULT_CAMERA_YAW;
    private float _cameraPitch = Constants.DEFAULT_CAMERA_PITCH;
    private bool _isRotating;
    private bool _wasRotating;

    public bool MouseScrollToZoom
    {
        get => _mouseScrollToZoom;
        set => _mouseScrollToZoom = value;
    }

    public void Initialize()
    {
        _previousScrollValue = MuGame.Instance.Mouse.ScrollWheelValue;
        _cameraYaw = Constants.DEFAULT_CAMERA_YAW;
        _cameraPitch = Constants.DEFAULT_CAMERA_PITCH;
        _currentCameraDistance = Constants.DEFAULT_CAMERA_DISTANCE;
        _targetCameraDistance = Constants.DEFAULT_CAMERA_DISTANCE;
    }

    public void Update(GameTime gameTime)
    {
        float deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _currentCameraDistance = MathHelper.Lerp(
            _currentCameraDistance,
            _targetCameraDistance,
            Constants.ZOOM_SPEED * deltaTime);

        var mouse = MuGame.Instance.Mouse;
        var prevMouse = MuGame.Instance.PrevMouseState;
        var keyboard = MuGame.Instance.Keyboard;

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

        _isRotating = keyboard.IsKeyDown(Keys.LeftAlt) && mouse.RightButton == ButtonState.Pressed;
        if (_isRotating)
        {
            int deltaX = mouse.X - prevMouse.X;
            int deltaY = mouse.Y - prevMouse.Y;
            _cameraYaw -= deltaX * Constants.ROTATION_SENSITIVITY;
            _cameraPitch += deltaY * Constants.ROTATION_SENSITIVITY;
            _cameraPitch = MathHelper.Clamp(_cameraPitch, -1.2f, -0.1f);
        }

        if (_wasRotating && !_isRotating)
        {
            _cameraYaw = Constants.DEFAULT_CAMERA_YAW;
            _cameraPitch = Constants.DEFAULT_CAMERA_PITCH;
        }

        _wasRotating = _isRotating;
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
}
