using Client.Main.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Input.Touch;
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

    // --- 觸控雙指手勢 ---
    // 桌面靠滾輪縮放、中鍵拖曳旋轉，兩者在手機上都沒有對應輸入。
    // 這裡以雙指補上：兩指距離變化 = 縮放，兩指中點位移 = 旋轉。
    private bool _pinchActive;
    private float _previousPinchDistance;
    private Vector2 _previousPinchCenter;

    /// <summary>雙指縮放靈敏度，把兩指距離的像素變化換算成鏡頭距離。</summary>
    private const float PinchZoomSensitivity = 2.2f;

    /// <summary>雙指旋轉靈敏度。刻意低於滑鼠 —— 手指的位移量比游標大得多。</summary>
    private const float TouchRotationSensitivity = Constants.ROTATION_SENSITIVITY * 0.6f;

    /// <summary>兩指距離小於此值視為誤觸，不進行縮放。</summary>
    private const float MinPinchDistance = 24f;

    private static readonly bool IsMobile = OperatingSystem.IsIOS() || OperatingSystem.IsAndroid();

    private static Configuration.MobileGraphicsSettings MobileSettings
        => MuGame.AppSettings?.Graphics?.Mobile;

    /// <summary>縮放下限。手機用自己的設定 —— 桌面的 500 在手機上還不夠近。</summary>
    private static float ZoomMinDistance
    {
        get
        {
            if (!IsMobile) return Constants.MIN_CAMERA_DISTANCE;
            float v = MobileSettings?.CameraMinDistance ?? 350f;
            return MathHelper.Clamp(v, 200f, Constants.MAX_CAMERA_DISTANCE);
        }
    }

    /// <summary>縮放上限。手機收窄 —— 拉到桌面的 1800 時角色小到不能玩。</summary>
    private static float ZoomMaxDistance
    {
        get
        {
            if (!IsMobile) return Constants.MAX_CAMERA_DISTANCE;
            float v = MobileSettings?.CameraMaxDistance ?? 1100f;
            return MathHelper.Clamp(v, ZoomMinDistance, Constants.MAX_CAMERA_DISTANCE);
        }
    }

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

        UpdateTouchGestures();
    }

    /// <summary>
    /// 雙指手勢：張合縮放、平移旋轉。
    /// 只在剛好兩指時作用 —— 單指保留給點擊移動，三指以上視為誤觸。
    /// </summary>
    private void UpdateTouchGestures()
    {
        TouchCollection touches = MuGame.Instance.Touch;

        if (touches.Count != 2)
        {
            _pinchActive = false;
            return;
        }

        Vector2 a = touches[0].Position;
        Vector2 b = touches[1].Position;
        float distance = Vector2.Distance(a, b);
        Vector2 center = (a + b) * 0.5f;

        // 手指剛放上的那一幀只記錄基準，否則會拿沒有意義的差值去動鏡頭
        if (!_pinchActive)
        {
            _pinchActive = true;
            _previousPinchDistance = distance;
            _previousPinchCenter = center;
            return;
        }

        // 縮放：兩指張開拉近鏡頭（角色變大），收合則拉遠
        float distanceDelta = distance - _previousPinchDistance;
        if (MathF.Abs(distanceDelta) > 0.5f && distance > MinPinchDistance)
        {
            _targetCameraDistance = MathHelper.Clamp(
                _targetCameraDistance - distanceDelta * PinchZoomSensitivity,
                ZoomMinDistance,
                ZoomMaxDistance);
        }

        // 旋轉：兩指一起平移
        Vector2 centerDelta = center - _previousPinchCenter;
        if (centerDelta.LengthSquared() > 0f)
        {
            _cameraYaw = MathHelper.WrapAngle(
                _cameraYaw - centerDelta.X * TouchRotationSensitivity);
            _cameraPitch = MathHelper.Clamp(
                _cameraPitch - centerDelta.Y * TouchRotationSensitivity,
                Constants.MIN_PITCH,
                Constants.MAX_PITCH);
        }

        _previousPinchDistance = distance;
        _previousPinchCenter = center;
    }

    public void Apply(Vector3 target)
    {
        // 角色模型的原點在腳底，因此鏡頭原本正對腳底 —— 身體整個往畫面中心以上長，
        // 拉近時頭部會頂出螢幕上緣。手機是橫向螢幕，垂直空間本來就窄。
        // 把注視點沿世界 Z 抬高，角色整體下移，腳落在畫面中心略下方。
        if (IsMobile)
        {
            float lift = MathHelper.Clamp(MobileSettings?.CameraTargetLift ?? 0.12f, 0f, 0.5f);
            target.Z += _currentCameraDistance * lift;
        }

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
