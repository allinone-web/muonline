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
        /// 預設鏡頭距離，<b>數值越小角色越大</b>。桌面為 1700，
        /// 可用範圍見 Constants.MIN/MAX_CAMERA_DISTANCE（800–1800）。
        /// </summary>
        public float CameraDistance { get; set; } = 850f;

        /// <summary>
        /// 手機的畫面縮放比例。
        ///
        /// 畫質預設 Medium 會把 RENDER_SCALE 設為 0.9，而縮放渲染路徑在 iOS 上
        /// 會讓 3D 畫面被壓扁（角色與建築變形）—— 實測必須手動點 Render Scale 100%
        /// 才恢復正常比例。實機有約 11 ms 的每幀餘裕，直接用 1.0 即可。
        /// </summary>
        public float RenderScale { get; set; } = 1.0f;

        /// <summary>
        /// UI 是否維持等比縮放。
        ///
        /// 原本用 Stretch 把 1280x720 硬拉滿螢幕，在 2868x1320 上橫向 2.241 倍、
        /// 縱向 1.833 倍 —— UI 被橫向拉寬 22%。改為等比後比例正確，
        /// 左右留白也順帶避開 iPhone 的圓角。
        /// </summary>
        public bool UniformUiScale { get; set; } = true;
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
