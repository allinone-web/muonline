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
    }

    /// <summary>
    /// 這份客戶端的 UI 與鏡頭都是照 PC 螢幕調的，直接搬到手機上會過小。
    /// 這裡的預設值針對 6–7 吋手機調整過，可在 appsettings.json 覆寫。
    /// </summary>
    public class MobileGraphicsSettings
    {
        /// <summary>
        /// UI 虛擬畫布寬度。UI 以此為基準等比拉伸到實際螢幕，
        /// <b>數值越小，畫面上的 UI 元素越大</b>。桌面為 1280。
        /// </summary>
        public int UiVirtualWidth { get; set; } = 960;

        /// <summary>
        /// UI 虛擬畫布高度。桌面為 720。
        /// </summary>
        public int UiVirtualHeight { get; set; } = 540;

        /// <summary>
        /// 預設鏡頭距離，<b>數值越小角色越大</b>。桌面為 1700，
        /// 可用範圍見 Constants.MIN/MAX_CAMERA_DISTANCE（800–1800）。
        /// </summary>
        public float CameraDistance { get; set; } = 1150f;
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
