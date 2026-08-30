using System.Collections.Generic;
using Client.Main.Core.Client;

namespace Client.Main.Configuration
{
    public class PacketLoggingSettings
    {
        public bool ShowWeather { get; set; } = true;
        public bool ShowDamage { get; set; } = true;
        public bool LogPacketsHex { get; set; } = false;
        public int LogPacketsHexMaxBytes { get; set; } = 64;
    }

    public class GraphicsSettings
    {
        public int Width { get; set; } = 1280;
        public int Height { get; set; } = 720;
        public bool IsFullScreen { get; set; }
        public int UiVirtualWidth { get; set; } = 1280;
        public int UiVirtualHeight { get; set; } = 720;
        public string QualityPreset { get; set; } = "Auto";
        public bool ForceMonsterMeshShadows { get; set; }
        public int DynamicLightUpdateFps { get; set; } = 23;
        public int AnimationUpdateFps { get; set; } = 23;
        public bool EnableCrowdSpatialCulling { get; set; } = true;
        public bool EnableWalkerCrowdInstancing { get; set; } = true;
        public bool EnableAnimationThrottling { get; set; } = true;
        public bool EnableSharedAnimationPalettes { get; set; } = true;
        public bool EnableBmdMeshBatching { get; set; } = true;
        public bool EnableEffectPooling { get; set; } = true;
        public int RenderMetricsLevel { get; set; } = 1;

        /// <summary>
        /// 手機（iOS / Android）專用的呈現設定。桌面平台不受影響。
        /// </summary>
        public MobileGraphicsSettings Mobile { get; set; } = new();

        /// <summary>
        /// 設定選單中各項開關的使用者選擇。
        ///
        /// 選單裡多數開關（動態光源、各材質 shader、草地、高品質貼圖、音效等）
        /// 原本只寫入 Constants 的記憶體欄位，重開遊戲就全部回到預設值。
        /// 這裡以「開關名稱 -> 值」保存，啟動時於套用畫質預設之後再覆寫回去。
        /// </summary>
        public Dictionary<string, bool> RenderToggles { get; set; } = new();

        /// <summary>
        /// 使用者在設定選單選擇的畫面縮放比例。null 表示沿用畫質預設的值。
        /// </summary>
        public float? RenderScale { get; set; }

        /// <summary>
        /// 草地品質：一格地面長幾片草。1 = 原版（一格一片），4 = 中，8 = 高。
        /// null 表示沒選過，維持原版。
        /// </summary>
        /// <remarks>
        /// 存的是「每格的立牌數」而不是 low/medium/high，因為這個數字就是成本本身 ——
        /// 三角形數與它成正比，看到 8 就知道是原版的八倍。
        /// </remarks>
        public int? GrassQuality { get; set; }
    }

    /// <summary>
    /// 這份客戶端的 UI 與鏡頭都是照 PC 螢幕調的，直接搬到手機上會過小。
    /// 這裡的預設值針對 6–7 吋手機調整過，可在 appsettings.json 覆寫。
    /// </summary>
    public class MobileGraphicsSettings
    {
        /// <summary>
        /// UI 虛擬畫布寬度。UI 以此為基準拉伸到實際螢幕，
        /// <b>數值越小，畫面上的 UI 元素越大</b>。
        ///
        /// ⚠ 實測過 960x540：UI 確實變大且好按，但現有視窗版面是照 1280x720 畫的，
        /// 最大的視窗達 560x700 —— 高度一旦低於 720 就會超出畫面，關閉鈕點不到、
        /// 底部導覽列被遮住。要真正放大 UI 必須重排版面，不能只縮虛擬畫布。
        /// 因此維持 1280x720，放大效果改由 CameraDistance 提供。
        /// </summary>
        public int UiVirtualWidth { get; set; } = 1280;

        /// <summary>
        /// UI 虛擬畫布高度。不可低於 720，見上方說明。
        /// </summary>
        public int UiVirtualHeight { get; set; } = 720;

        /// <summary>
        /// 預設鏡頭距離，<b>數值越小角色越大</b>。桌面為 1700。
        /// 680 約為桌面的 2.5 倍大；下限 500（約 3.4 倍）。
        /// </summary>
        public float CameraDistance { get; set; } = 680f;

        /// <summary>
        /// 手機的畫面縮放比例。
        ///
        /// 畫質預設 Medium 會把 RENDER_SCALE 設為 0.9，而縮放渲染路徑在 iOS 上
        /// 會讓 3D 畫面被壓扁（角色與建築變形）—— 實測必須手動點 Render Scale 100%
        /// 才恢復正常比例。實機有約 11 ms 的每幀餘裕，直接用 1.0 即可。
        /// </summary>
        public float RenderScale { get; set; } = 1.0f;

        /// <summary>
        /// 手機的雙指縮放下限（越小角色越大）。
        ///
        /// 桌面的 MIN_CAMERA_DISTANCE 是 500，但在 6.5 吋的橫向螢幕上放到最大仍嫌小。
        /// </summary>
        public float CameraMinDistance { get; set; } = 450f;

        /// <summary>
        /// 手機的雙指縮放上限（越大視野越廣，但角色與怪物會小到失去意義）。
        ///
        /// 桌面允許到 1800，實測在手機上拉到那麼遠時建築與角色都太小，
        /// 畫面雖然寬廣卻不能玩，因此收窄。
        /// </summary>
        public float CameraMaxDistance { get; set; } = 950f;

        /// <summary>
        /// 注視點沿世界 Z 抬高的比例（相對於當前鏡頭距離）。
        ///
        /// 鏡頭正對角色腳底時，身體整個往畫面中心以上延伸，拉近後頭部會頂出螢幕。
        /// 把注視點抬高，角色隨之下移，腳落在畫面中心略下方 —— 橫向手機螢幕垂直
        /// 空間很窄，這個偏移相當有感。0 表示維持原本對準腳底的行為。
        /// </summary>
        public float CameraTargetLift { get; set; } = 0.12f;

        /// <summary>
        /// 地面等貼圖的各向異性過濾倍數。
        ///
        /// MonoGame 內建的取樣器只有 4x，掠角地面會糊；16x 最銳利但在行動 GPU 上
        /// 要多吃記憶體頻寬。8x 是實測上的折衷。想比較畫質與幀率就改這個值。
        /// </summary>
        public int MaxAnisotropy { get; set; } = 8;

        /// <summary>
        /// 玩家角色的顯示倍率。
        ///
        /// 放大角色而不是拉近鏡頭 —— 拉近鏡頭會把只有 128x128 的地磚一起放大
        /// 而變糊，放大角色則讓地面維持在清晰的取樣距離。
        /// 上限受限於與建築的比例：MU 的門與房子是照原比例做的，
        /// 超過約 1.4 角色就會開始像進錯場景。
        /// </summary>
        public float PlayerScale { get; set; } = 1.25f;

        /// <summary>
        /// 怪物的顯示倍率，預設 1.0 —— 不放大。
        ///
        /// 原本跟著放大 1.1 倍，實機看下來不需要：MU 的怪物本來就是巨人與
        /// 野獸，尺寸已經夠。放大玩家的用意是讓角色在畫面上夠大而不必拉近
        /// 鏡頭，怪物並沒有這個問題，一起放大反而讓畫面顯得擁擠。
        /// 保留這個設定是為了還能調。
        /// </summary>
        public float MonsterScale { get; set; } = 1.0f;

        /// <summary>
        /// NPC 的顯示倍率。人形 NPC 與玩家相當，但 NPCObject 底下也掛著
        /// 櫻花樹之類的場景物件，因此保守一點。
        /// </summary>
        public float NpcScale { get; set; } = 1.1f;




        /// <summary>
        /// UI 是否維持等比縮放。
        ///
        /// 原本用 Stretch 把 1280x720 硬拉滿螢幕，在 2868x1320 上橫向 2.241 倍、
        /// 縱向 1.833 倍 —— UI 被橫向拉寬 22%。改為等比後比例正確，
        /// 左右留白也順帶避開 iPhone 的圓角。
        /// </summary>
        public bool UniformUiScale { get; set; } = true;

        /// <summary>
        /// 虛擬畫布的寬度是否跟著螢幕長寬比走。
        ///
        /// 手機是超寬螢幕（iPhone Air 的可用區域接近 21:9），用固定的 1280x720
        /// 等比縮放後左右各留約 260 px 空白，UI 全被擠在中央，四個角落用不到。
        /// 開啟後高度仍固定為 <see cref="UiVirtualHeight"/>（既有視窗版面依賴 720），
        /// 寬度由長寬比推算，虛擬畫布正好鋪滿安全區域。
        /// 關閉則沿用 <see cref="UiVirtualWidth"/>。
        /// </summary>
        public bool MatchScreenAspect { get; set; } = true;

        /// <summary>
        /// 登入與選角畫面的額外拉近倍率，<b>數值越大主體越大</b>。
        ///
        /// 這兩個畫面是固定機位的展示鏡頭，在 21:9 的手機上主體偏小。
        /// 1.0 表示只做長寬比補償（見 <see cref="Graphics.WideScreenFraming"/>），
        /// 已經約放大 1.2 倍；1.12 再多拉近一點。過大會裁掉畫面上下的景物。
        /// 只影響登入與選角，遊戲中的鏡頭由 <see cref="CameraDistance"/> 控制。
        /// </summary>
        public float MenuSceneZoom { get; set; } = 1.12f;

        /// <summary>
        /// 選角畫面的拉近倍率。與 <see cref="MenuSceneZoom"/> 分開，因為這兩個畫面
        /// 想要的東西相反：登入是「整片海景拉近」，選角則希望<b>角色大、背景小</b>。
        /// 背景由這個值控制（越小背景越小），角色由 <see cref="SelectCharacterScale"/> 控制。
        /// </summary>
        public float SelectSceneZoom { get; set; } = 1.0f;

        /// <summary>
        /// 選角畫面的角色模型放大倍率。只影響選角的展示用角色，不影響遊戲中的角色。
        /// </summary>
        public float SelectCharacterScale { get; set; } = 1.15f;
    }

    public abstract class LeafEffectSettingsBase
    {
        public bool Enabled { get; set; } = true;
        public string TexturePath { get; set; } = "World1/leaf01.tga";
        public string[] TexturePaths { get; set; }
        public int MaxParticles { get; set; } = 140;
        public float SpawnRate { get; set; } = 12f;
        public float MinLifetime { get; set; } = 10f;
        public float MaxLifetime { get; set; } = 20f;
        public float FadeInDuration { get; set; } = 0.8f;
        public float FadeOutDuration { get; set; } = 2f;
        public float MinHorizontalSpeed { get; set; } = 12f;
        public float MaxHorizontalSpeed { get; set; } = 28f;
        public float VerticalSpeedRange { get; set; } = 4f;
        public float DriftStrength { get; set; } = 3.5f;
        public float MaxDistance { get; set; } = 2000f;
        public float BaseScale { get; set; } = 36f;
        public float ScaleVariance { get; set; } = 14f;
        public float TiltStrength { get; set; } = 0.45f;
        public float SwayStrength { get; set; } = 18f;
    }

    public class LorenciaLeafEffectSettings : LeafEffectSettingsBase
    {
        public float WindDirectionX { get; set; } = 6f;
        public float WindDirectionY { get; set; } = 14f;
        public float WindSpeedMultiplier { get; set; } = 1.0f;
        public float WindVariance { get; set; } = 0.35f;
        public float WindAlignment { get; set; } = 0.45f;
        public float SpawnPaddingX { get; set; } = 900f;
        public float SpawnPaddingBack { get; set; } = 700f;
        public float SpawnPaddingForward { get; set; } = 1600f;
        public float SpawnHeightMin { get; set; } = 50f;
        public float SpawnHeightMax { get; set; } = 320f;
        public float UpwindSpawnDistance { get; set; } = 1100f;
        public float InitialFillRatio { get; set; } = 0.65f;
    }

    public class NoriaLeafEffectSettings : LeafEffectSettingsBase
    {
        public float Gravity { get; set; } = 45f;
        public float GroundFadeTime { get; set; } = 1.5f;

        public NoriaLeafEffectSettings()
        {
            TexturePath = "World4/leaf01.tga";
            SpawnRate = 20f;
            MinLifetime = 8f;
            MaxLifetime = 18f;
            FadeOutDuration = 2.5f;
            MinHorizontalSpeed = 80f;
            MaxHorizontalSpeed = 220f;
            VerticalSpeedRange = 180f;
            DriftStrength = 2.5f;
            MaxDistance = 2800f;
            BaseScale = 7f;
            ScaleVariance = 2.5f;
            TiltStrength = 0.35f;
            SwayStrength = 7f;
        }
    }

    public class DeviasSnowEffectSettings : LeafEffectSettingsBase
    {
        public float Gravity { get; set; } = 60f;
        public float GroundFadeTime { get; set; } = 1.2f;
        public float HorizontalBiasX { get; set; } = 8f;
        public float HorizontalBiasY { get; set; } = -12f;

        public DeviasSnowEffectSettings()
        {
            TexturePath = "World3/leaf01.ozj";
            TexturePaths = new[] { "World3/leaf01.ozj", "World3/leaf02.ozj" };
            SpawnRate = 28f;
            MinLifetime = 6f;
            MaxLifetime = 16f;
            FadeOutDuration = 2.2f;
            MinHorizontalSpeed = 90f;
            MaxHorizontalSpeed = 260f;
            VerticalSpeedRange = 220f;
            DriftStrength = 3.5f;
            MaxDistance = 3200f;
            BaseScale = 9f;
            ScaleVariance = 3.5f;
            TiltStrength = 0.4f;
            SwayStrength = 6.5f;
        }
    }

    public class EnvironmentSettings
    {
        public LorenciaLeafEffectSettings LorenciaLeaf { get; set; } = new();
        public NoriaLeafEffectSettings NoriaLeaf { get; set; } = new();
        public DeviasSnowEffectSettings DeviasSnow { get; set; } = new();
    }

    public class MuOnlineSettings
    {
        /// <summary>
        /// 每件道具是否只佔背包一格（取代 MU 原本的俄羅斯方塊式格子）。
        /// <b>必須與伺服器的 OPENMU_SINGLE_SLOT_ITEMS 一致</b> —— 尺寸不走網路協議，
        /// 只改一邊會讓道具移動被伺服器拒絕、彈回原位。
        /// </summary>
        public bool SingleSlotItems { get; set; } = true;

        // Connect Server Settings
        public string ConnectServerHost { get; set; } = "127.0.0.1";
        public int ConnectServerPort { get; set; } = 44405;

        // Client/Protocol Settings
        public string ProtocolVersion { get; set; } = nameof(TargetProtocolVersion.Season6); // Use nameof for safety
        public string ClientVersion { get; set; } = "2.04d"; // Matches SourceMain5.2 default
        public string ClientSerial { get; set; } = "k1Pk2jcET48mxL3b"; // Matches SourceMain5.2 default
        public Dictionary<byte, byte> DirectionMap { get; set; } = new(); // Direction mapping for walk packets
        public PacketLoggingSettings PacketLogging { get; set; } = new();
        public GraphicsSettings Graphics { get; set; } = new();
        public EnvironmentSettings Environment { get; set; } = new();
    }
}
