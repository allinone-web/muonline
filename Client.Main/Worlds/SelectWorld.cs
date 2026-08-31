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
        // 格子 (210,171) 是使用者在遊戲裡挑定的。x 206–212、y 168–174 完全平坦
        // （高度值都是 113），東邊有隆起可以當背景。
        // 先前用過 (139,84)，也試過 (108,111)——後者不平坦且取景較差。
        private const float StageGroundZ = 169.5f;   // 高度圖 113 × 1.5
        private const float StageTileX = 210f;
        private const float StageTileY = 171f;

        // 鏡頭：接近平視。公式刻意與地圖編輯器的環繞鏡頭一致（EditorCamera.Apply），
        // 這樣 tools/mu golden 拍出來的取景就等於真機看到的取景 ——
        // 兩邊各寫各的相機，比出來的差異會分不清是「渲染不同」還是「根本沒看同一個地方」。
        private const float CameraYawDegrees = -45f;
        // 18 度：剛好讓天空離開畫面，又盡量不俯視。
        //
        // 界線是算得出來的，不必試誤：天空可見 ⟺ 俯角 < 垂直視角的一半。
        // 本機的垂直視角經過寬螢幕補償後是 32.57 度（基準 35 度、長寬比 2.257、
        // SelectSceneZoom 0.85），所以臨界值是 16.28 度。多 2 度當緩衝，
        // 因為遠處地形起伏會讓地平線再冒高一點。
        // 實測對照：12 度看得到天空，20 度看不到但偏俯視。
        private const float CameraPitchDegrees = 18f;
        private const float CameraDistance = 1400f;
        private const float StageFieldOfView = 35f;

        /// <summary>鏡頭焦點抬到胸口高度，角色才不會貼在畫面下緣。</summary>
        private const float CameraFocusHeight = 80f;

        /// <summary>
        /// 角色整排往鏡頭方向前移多少。
        ///
        /// 「角色要更大」與「地圖不要被放大到看得出貼圖模糊」是衝突的，除非把
        /// 兩個距離拆開：鏡頭與地形的距離維持 CameraDistance 不動（草地保持細膩），
        /// 只把角色往鏡頭推近。角色模型的解析度撐得住特寫，地形貼圖撐不住。
        /// </summary>
        private const float CharacterForwardOffset = 300f;

        /// <summary>
        /// 選中的角色往鏡頭方向踏出多少。
        ///
        /// 位移比發光更容易讀懂 —— 玩家一眼就知道「這個被選中了」，
        /// 而且不需要新的 shader（iOS 的 .fx 在 macOS 編不動，要送 CI）。
        /// </summary>
        // 踏步方向是斜的，前移時角色同時往畫面右下跑 ——
        // 幅度太大時最右邊那個會被切出畫面。
        private const float SelectedStepDistance = 150f;

        /// <summary>選中的角色相對於自己站位的位移。</summary>
        public static Vector3 SelectedStepOffset => TowardCamera * SelectedStepDistance;

        /// <summary>鏡頭在世界座標裡的位置。角色要面向它。</summary>
        public static Vector3 CameraWorldPosition
        {
            get
            {
                float yaw = MathHelper.ToRadians(CameraYawDegrees);
                float pitch = MathHelper.ToRadians(CameraPitchDegrees);
                float horizontal = CameraDistance * MathF.Cos(pitch);
                return StageCenter
                     + new Vector3(0f, 0f, CameraFocusHeight)
                     + new Vector3(-MathF.Cos(yaw) * horizontal,
                                   -MathF.Sin(yaw) * horizontal,
                                   CameraDistance * MathF.Sin(pitch));
            }
        }

        /// <summary>
        /// 站在 <paramref name="position"/> 的角色要面向鏡頭時的 Z 軸旋轉（弧度）。
        ///
        /// 五個角色共用同一個角度時，兩端的人是側對鏡頭的，看起來就像「右邊比較近、
        /// 左邊比較遠」。各自朝向鏡頭之後，整排才會對稱。
        /// 加的那 90 度是模型自己的正面偏移，理由見 CharacterFacingDegrees。
        /// </summary>
        public static float FacingAngleFor(Vector3 position)
            => FacingAngleTowards(position, CameraWorldPosition);

        /// <summary>
        /// 從 <paramref name="from"/> 面向 <paramref name="to"/> 的 Z 軸旋轉（弧度）。
        ///
        /// 走路時要用這個面向「移動方向」，不能一直面向鏡頭 ——
        /// 否則角色是側著身平移，像螃蟹在走。
        /// 加的 90 度是模型自己的正面偏移，理由見 CharacterFacingDegrees。
        /// </summary>
        public static float FacingAngleTowards(Vector3 from, Vector3 to)
        {
            var direction = new Vector2(to.X - from.X, to.Y - from.Y);
            if (direction.LengthSquared() < 0.0001f)
                return MathHelper.ToRadians(CharacterFacingDegrees);

            return MathF.Atan2(direction.Y, direction.X) + MathHelper.PiOver2;
        }

        /// <summary>名字標籤與角色腳底之間留多少空隙（螢幕像素）。</summary>
        private const float LabelScreenGap = 10f;

        /// <summary>角色並排的間距（世界單位，一格 = 100）。翅膀很寬，太小會黏在一起。</summary>
        private const float SlotSpacing = 195f;

        /// <summary>
        /// 角色面向。
        ///
        /// 直覺會寫「鏡頭方位 + 180」，但實測是錯的：那樣角色會面向畫面左方。
        /// 模型自己的正面比世界方位少 90 度，所以要再加 90 —— 也就是 +270。
        /// （鏡頭在焦點西北方往東南看，畫面左方是世界方位 45 度；
        /// 設 135 度卻看到 45 度，差的就是這 90 度。）
        /// </summary>
        private const float CharacterFacingDegrees = CameraYawDegrees + 270f;

        /// <summary>舞台中心：鏡頭取景與物件裁切都以它為準。</summary>
        private static readonly Vector3 StageCenter =
            new(StageTileX * Constants.TERRAIN_SCALE + Constants.TERRAIN_SCALE / 2f,
                StageTileY * Constants.TERRAIN_SCALE + Constants.TERRAIN_SCALE / 2f,
                StageGroundZ);

        /// <summary>從舞台中心指向鏡頭的水平單位向量。</summary>
        private static Vector3 TowardCamera
        {
            get
            {
                float yaw = MathHelper.ToRadians(CameraYawDegrees);
                return new Vector3(-MathF.Cos(yaw), -MathF.Sin(yaw), 0f);
            }
        }

        private readonly Vector3 _characterDisplayPosition =
            StageCenter + (TowardCamera * CharacterForwardOffset);
        private readonly Vector3 _characterDisplayAngle =
            new(0, 0, MathHelper.ToRadians(CharacterFacingDegrees));
        private ILogger<SelectWorld> _logger;
        private CharacterSelectionController _controller;

        // 舞台的環境動態：落葉與蝴蝶。
        // 兩者原本都是繞著玩家角色生成的，而這個世界沒有玩家角色 ——
        // 所以建構時要給一個錨點（見它們的 anchor 參數）。
        private Objects.Worlds.Noria.ButterflyManager _butterflyManager;
        private Objects.Worlds.Noria.NoriaLeafAmbientEffect _leafEffect;

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

            // 排成以鏡頭為圓心的弧線，不是直線。
            //
            // 直線排列時中間離鏡頭最近、兩端最遠 —— 實測 1069 對 1152，
            // 差 83 單位（約 7.5%）。透視下兩端就明顯比中間小，
            // 看起來像整排是斜的。沿著等距的弧線擺，五個人大小才一致。
            var centre = StageCenter + (TowardCamera * CharacterForwardOffset);
            var camera = CameraWorldPosition;

            var radial = new Vector2(centre.X - camera.X, centre.Y - camera.Y);
            float radius = radial.Length();
            if (radius < 1f)
                return Vector3.Zero;

            float theta = ((index - (count - 1) / 2f) * SlotSpacing) / radius;
            float cos = MathF.Cos(theta);
            float sin = MathF.Sin(theta);

            var rotated = new Vector2(
                (radial.X * cos) - (radial.Y * sin),
                (radial.X * sin) + (radial.Y * cos));

            return new Vector3(
                camera.X + rotated.X - centre.X,
                camera.Y + rotated.Y - centre.Y,
                0f);
        }

        public SelectWorld() : base(worldIndex: 4)
        {
            // 接近平視的話沒有影子角色會像浮在地上，所以這裡要開。
            EnableShadows = true;

            // 舞台在諾利亞，配樂與環境音就用諾利亞的。原本選角畫面是全靜音的。
            BackgroundMusicPath = "Music/Noria.mp3";
            AmbientSoundPath = "Sound/aForest.wav";
            Name = "Noria";
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

            // 鏡頭位置以「舞台中心」算 —— 地形的取景距離因此不受角色前移影響。
            // 目標則對準角色，角色才會留在畫面中央。
            float yaw = MathHelper.ToRadians(CameraYawDegrees);
            float pitch = MathHelper.ToRadians(CameraPitchDegrees);
            float horizontal = CameraDistance * MathF.Cos(pitch);
            var offset = new Vector3(
                -MathF.Cos(yaw) * horizontal,
                -MathF.Sin(yaw) * horizontal,
                CameraDistance * MathF.Sin(pitch));

            var cameraPosition = StageCenter + new Vector3(0f, 0f, CameraFocusHeight) + offset;
            var target = _characterDisplayPosition + new Vector3(0f, 0f, CameraFocusHeight);

            cameraState = cameraState.With(
                viewFar: MathF.Max(8000f, CameraDistance * 3f),
                fieldOfView: fieldOfView,
                position: cameraPosition,
                target: target);
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
            float dx = mapObj.Position.X - StageCenter.X;
            float dy = mapObj.Position.Y - StageCenter.Y;
            return (dx * dx) + (dy * dy) <= StageObjectRadius * StageObjectRadius;
        }

        public override void Dispose()
        {
            // 舞台的環境動態要跟著世界一起收掉，否則換場景後會留著。
            _butterflyManager?.Clear();
            _butterflyManager = null;

            if (_leafEffect != null)
            {
                Objects.Remove(_leafEffect);
                _leafEffect.Dispose();
                _leafEffect = null;
            }

            base.Dispose();
        }

        public override async System.Threading.Tasks.Task Load()
        {
            var leafSettings = MuGame.AppSettings?.Environment?.NoriaLeaf;
            if (leafSettings?.Enabled != false)
            {
                _leafEffect = new Objects.Worlds.Noria.NoriaLeafAmbientEffect(
                    this,
                    leafSettings ?? new Configuration.NoriaLeafEffectSettings(),
                    () => _characterDisplayPosition);
                Objects.Add(_leafEffect);
            }

            await base.Load();
        }

        public override void AfterLoad()
        {
            base.AfterLoad();

            _butterflyManager = new Objects.Worlds.Noria.ButterflyManager(
                this, () => _characterDisplayPosition);

            // water animation parameters
            Terrain.WaterSpeed = 0.05f;
            Terrain.DistortionAmplitude = 0.2f;
            Terrain.DistortionFrequency = 1.0f;

        }

        public override void Update(GameTime time)
        {
            base.Update(time);
            if (!Visible) return;

            _butterflyManager?.Update(time);

            // Keep the selected cinematic actor published even if a body-part model swap
            // or first-frame recovery temporarily invalidated its visibility.
            if (Status == GameControlStatus.Ready && _controller != null)
            {
                _controller.EnsureActiveCharacterVisible(this);
                _controller.UpdateSelectionMotion((float)time.ElapsedGameTime.TotalSeconds);

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

                    // 排在投影點「下方」而不是上方 —— 錨點本來就在腳底下方一點，
                    // 原本又往上推一個字高，結果標籤幾乎貼著角色的腳。
                    label.Y = (int)(virtualPos.Y + LabelScreenGap);
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
