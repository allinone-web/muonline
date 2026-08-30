using Client.Main.Controls;
using Client.Main.Controls.UI;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects.Worlds.SelectWrold;
using Client.Main.Scenes.SelectCharacter;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Client.Main.Worlds
{
    public class SelectWorld : WorldControl
    {
        // === 選角舞台：諾利亞的開闊草地 ===
        // 格子 (139,84) 是使用者在遊戲裡站過、挑定的位置。那一帶 7×6 格完全平坦
        // （地形高度值 113 × 1.5 = 170），五個角色並排站上去不會高低不齊。
        private const float StageGroundZ = 170f;
        private const float StageTileX = 139f;
        private const float StageTileY = 84f;

        // 鏡頭：接近平視。公式刻意與地圖編輯器的環繞鏡頭一致（EditorCamera.Apply），
        // 這樣 tools/mu golden 拍出來的取景就等於真機看到的取景 ——
        // 兩邊各寫各的相機，比出來的差異會分不清是「渲染不同」還是「根本沒看同一個地方」。
        private const float CameraYawDegrees = -45f;
        private const float CameraPitchDegrees = 13f;
        private const float CameraDistance = 1400f;
        private const float StageFieldOfView = 35f;

        /// <summary>鏡頭焦點抬到胸口高度，角色才不會貼在畫面下緣。</summary>
        private const float CameraFocusHeight = 80f;

        /// <summary>角色並排的間距（世界單位，一格 = 100）。翅膀很寬，太小會黏在一起。</summary>
        private const float SlotSpacing = 240f;

        /// <summary>
        /// 角色面向。
        ///
        /// 直覺會寫「鏡頭方位 + 180」，但實測是錯的：那樣角色會面向畫面左方。
        /// 模型自己的正面比世界方位少 90 度，所以要再加 90 —— 也就是 +270。
        /// （鏡頭在焦點西北方往東南看，畫面左方是世界方位 45 度；
        /// 設 135 度卻看到 45 度，差的就是這 90 度。）
        /// </summary>
        private const float CharacterFacingDegrees = CameraYawDegrees + 270f;

        private readonly Vector3 _characterDisplayPosition =
            new(StageTileX * Constants.TERRAIN_SCALE + Constants.TERRAIN_SCALE / 2f,
                StageTileY * Constants.TERRAIN_SCALE + Constants.TERRAIN_SCALE / 2f,
                StageGroundZ);
        private readonly Vector3 _characterDisplayAngle =
            new(0, 0, MathHelper.ToRadians(CharacterFacingDegrees));
        private ILogger<SelectWorld> _logger;
        private CharacterSelectionController _controller;

        public Vector3 CharacterDisplayPosition => _characterDisplayPosition;
        public Vector3 CharacterDisplayAngle => _characterDisplayAngle;

        /// <summary>
        /// 第 <paramref name="index"/> 個角色相對於舞台中心的位移。
        /// 排列方向與鏡頭視線垂直，所以在畫面上就是水平的一排。
        /// </summary>
        public static Vector3 SlotOffset(int index, int count)
        {
            if (count <= 1)
                return Vector3.Zero;

            float yaw = MathHelper.ToRadians(CameraYawDegrees);
            var sideways = new Vector3(-MathF.Sin(yaw), MathF.Cos(yaw), 0f);
            return sideways * ((index - (count - 1) / 2f) * SlotSpacing);
        }

        public SelectWorld() : base(worldIndex: 4)
        {
            // 接近平視的話沒有影子角色會像浮在地上，所以這裡要開。
            EnableShadows = true;
            Terrain.PreferIndexBatching = true;
            _logger = MuGame.AppLoggerFactory?.CreateLogger<SelectWorld>() ?? throw new System.InvalidOperationException("LoggerFactory not initialized in MuGame");
        }

        protected override bool DeferCameraActivation => true;

        protected override void ConfigureCameraState(ref CameraState cameraState)
        {
            float fieldOfView = StageFieldOfView * Constants.FOV_SCALE;

            // 手機是超寬螢幕：垂直視角固定時，水平方向會多看到很多，角色因此顯得小。
            // 壓縮垂直視角，把水平取景拉回 16:9 的設計值（見 WideScreenFraming）。
            if (Client.Main.Controls.UI.MobileUi.IsMobile)
            {
                fieldOfView = WideScreenFraming.CompensateVerticalFov(
                    fieldOfView,
                    cameraState.AspectRatio,
                    MuGame.AppSettings?.Graphics?.Mobile?.SelectSceneZoom ?? 1f);
            }

            var focus = _characterDisplayPosition + new Vector3(0f, 0f, CameraFocusHeight);

            float yaw = MathHelper.ToRadians(CameraYawDegrees);
            float pitch = MathHelper.ToRadians(CameraPitchDegrees);
            float horizontal = CameraDistance * MathF.Cos(pitch);
            var offset = new Vector3(
                -MathF.Cos(yaw) * horizontal,
                -MathF.Sin(yaw) * horizontal,
                CameraDistance * MathF.Sin(pitch));

            cameraState = cameraState.With(
                viewFar: MathF.Max(8000f, CameraDistance * 3f),
                fieldOfView: fieldOfView,
                position: focus + offset,
                target: focus);
        }

        public void SetController(CharacterSelectionController controller)
        {
            _controller = controller;
        }

        protected override void CreateMapTileObjects()
        {
            base.CreateMapTileObjects();

            // 舞台換成諾利亞（World4）之後，物件索引的意義跟著換 ——
            // 這份對照必須跟 NoriaWorld 一致，否則樹會被當成瀑布之類。
            MapTileObjects[39] = typeof(Objects.Worlds.Noria.ChaosMachineObject);
            MapTileObjects[1] = typeof(Objects.Worlds.Noria.NoriaObject);
            MapTileObjects[9] = typeof(Objects.Worlds.Noria.NoriaObject);
            MapTileObjects[17] = typeof(Objects.Worlds.Noria.NoriaObject);
            MapTileObjects[19] = typeof(Objects.Worlds.Noria.NoriaObject);
            MapTileObjects[35] = typeof(Objects.Worlds.Noria.NoriaObject);
            MapTileObjects[41] = typeof(Objects.Worlds.Noria.NoriaObject);
            MapTileObjects[42] = typeof(Objects.Worlds.Noria.NoriaObject);
            MapTileObjects[43] = typeof(Objects.Worlds.Noria.NoriaObject);
            MapTileObjects[38] = typeof(Objects.Worlds.Noria.RestPlaceObject);
            MapTileObjects[8] = typeof(Objects.Worlds.Noria.SitPlaceObject);
            MapTileObjects[6] = typeof(Objects.Worlds.Noria.ClimberObject);
            MapTileObjects[37] = typeof(Objects.Worlds.Noria.LightBeamObject);
            MapTileObjects[18] = typeof(Objects.Worlds.Noria.EoTheCraftsmanPlaceObject);
        }

        /// <summary>
        /// 舞台附近多遠以內的地圖物件才載入。
        ///
        /// 諾利亞整張圖有約 2800 個物件，全部載入會讓選角畫面多花二十秒才出來
        /// —— 實測 log 有一段 19.7 秒完全沒有輸出，使用者以為登入失敗而再按一次，
        /// 於是拿到 AccountAlreadyConnected。畫面上只看得到舞台附近，其餘不必載。
        /// </summary>
        private const float StageObjectRadius = 4000f;

        protected override bool ShouldCreateMapObject(Client.Data.OBJS.IMapObject mapObj)
        {
            float dx = mapObj.Position.X - _characterDisplayPosition.X;
            float dy = mapObj.Position.Y - _characterDisplayPosition.Y;
            return (dx * dx) + (dy * dy) <= StageObjectRadius * StageObjectRadius;
        }

        public override void AfterLoad()
        {
            base.AfterLoad();

            // water animation parameters
            Terrain.WaterSpeed = 0.05f;
            Terrain.DistortionAmplitude = 0.2f;
            Terrain.DistortionFrequency = 1.0f;

        }

        public override void Update(GameTime time)
        {
            base.Update(time);
            if (!Visible) return;

            // Keep the selected cinematic actor published even if a body-part model swap
            // or first-frame recovery temporarily invalidated its visibility.
            if (Status == GameControlStatus.Ready && _controller != null)
            {
                _controller.EnsureActiveCharacterVisible(this);

                foreach (var (player, label) in _controller.Labels)
                {
                    if (player.Status != GameControlStatus.Ready || player.Hidden)
                    {
                        label.Visible = false;
                        continue;
                    }

                    var head = new Vector3(
                        player.WorldPosition.Translation.X,
                        player.WorldPosition.Translation.Y,
                        player.BoundingBoxWorld.Min.Z - 20);

                    var sp = GraphicsDevice.Viewport.Project(
                                 head,
                                 Camera.Instance.Projection,
                                 Camera.Instance.View,
                                 Matrix.Identity);

                    if (sp.Z is < 0 or > 1)
                    {
                        label.Visible = false;
                        continue;
                    }

                    var font = GraphicsManager.Instance.Font;
                    float k = label.FontSize / Constants.BASE_FONT_SIZE;
                    Vector2 s = font.MeasureString(label.Text) * k;

                    var virtualPos = UiScaler.ToVirtual(new Point((int)sp.X, (int)sp.Y));

                    label.X = (int)(virtualPos.X - s.X / 2f);
                    label.Y = (int)(virtualPos.Y - s.Y - 4);
                    label.ControlSize = new Point((int)s.X, (int)s.Y);
                    label.Visible = true;
                }
            }

            // Debug key handling
            if (MuGame.Instance.PrevKeyboard.IsKeyDown(Keys.Delete) && MuGame.Instance.Keyboard.IsKeyUp(Keys.Delete))
            {
                if (Objects.Count > 0)
                {
                    var obj = Objects[0];
                    _logger?.LogDebug($"Removing obj: {obj.Type} -> {obj.ObjectName}");
                    Objects.RemoveAt(0);
                }
            }
            else if (MuGame.Instance.Keyboard.IsKeyDown(Keys.Add))
            {
                Camera.Instance.ViewFar += 10;
            }
            else if (MuGame.Instance.Keyboard.IsKeyDown(Keys.Subtract))
            {
                Camera.Instance.ViewFar -= 10;
            }
        }

        public override void Draw(GameTime gameTime)
        {
            var gd = GraphicsManager.Instance.GraphicsDevice;
            gd.BlendState = BlendState.AlphaBlend;
            gd.DepthStencilState = DepthStencilState.Default;
            gd.SamplerStates[0] = SamplerState.LinearClamp;

            base.Draw(gameTime);
        }

    }
}
