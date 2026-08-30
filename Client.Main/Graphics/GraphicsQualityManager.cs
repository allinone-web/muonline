using System;
using System.Reflection;
using Client.Main.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Graphics
{
    public enum GraphicsQualityPreset
    {
        Auto,
        Low,
        Medium,
        High
    }

    public readonly struct GraphicsAdapterInfo
    {
        public string Description { get; }
        public string DeviceName { get; }
        public int? VendorId { get; }
        public int? DeviceId { get; }
        public bool IsIntegrated { get; }
        public bool IsDiscrete { get; }
        public bool IsSoftware { get; }

        public GraphicsAdapterInfo(
            string description,
            string deviceName,
            int? vendorId,
            int? deviceId,
            bool isIntegrated,
            bool isDiscrete,
            bool isSoftware)
        {
            Description = description ?? string.Empty;
            DeviceName = deviceName ?? string.Empty;
            VendorId = vendorId;
            DeviceId = deviceId;
            IsIntegrated = isIntegrated;
            IsDiscrete = isDiscrete;
            IsSoftware = isSoftware;
        }

        public override string ToString()
        {
            string vendor = VendorId.HasValue ? $"0x{VendorId.Value:X4}" : "unknown";
            return $"{Description} ({DeviceName}) Vendor={vendor}, Integrated={IsIntegrated}, Discrete={IsDiscrete}, Software={IsSoftware}";
        }
    }

    public static class GraphicsQualityManager
    {
        public static GraphicsQualityPreset UserPreset { get; private set; } = GraphicsQualityPreset.Auto;
        public static GraphicsQualityPreset ActivePreset { get; private set; } = GraphicsQualityPreset.High;
        public static GraphicsAdapterInfo LastAdapterInfo { get; private set; } = new GraphicsAdapterInfo(string.Empty, string.Empty, null, null, false, false, false);

        public static GraphicsQualityPreset ParsePreset(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return GraphicsQualityPreset.Auto;

            if (Enum.TryParse(value, true, out GraphicsQualityPreset preset))
                return preset;

            return GraphicsQualityPreset.Auto;
        }

        public static void ApplyFromSettings(GraphicsSettings settings, GraphicsAdapter adapter, ILogger logger)
        {
            var preset = ParsePreset(settings?.QualityPreset);
            ApplyPreset(preset, adapter, logger);
        }

        public static void ApplyPreset(GraphicsQualityPreset preset, GraphicsAdapter adapter, ILogger logger)
        {
            UserPreset = preset;
            var resolved = ResolvePreset(preset, adapter);
            ActivePreset = resolved;
            ApplyProfile(resolved);

            if (logger != null)
            {
                if (preset == GraphicsQualityPreset.Auto)
                {
                    logger.LogInformation("Graphics preset: {UserPreset} -> {ResolvedPreset}. Adapter: {Adapter}",
                        preset, resolved, LastAdapterInfo.ToString());
                }
                else
                {
                    logger.LogInformation("Graphics preset: {UserPreset} applied.", preset);
                }
            }
        }

        private static GraphicsQualityPreset ResolvePreset(GraphicsQualityPreset preset, GraphicsAdapter adapter)
        {
            if (preset != GraphicsQualityPreset.Auto)
                return preset;

            if (OperatingSystem.IsAndroid())
                return GraphicsQualityPreset.Low;

            // iOS 先前沒有分支，會掉到下面的桌面顯示卡判斷，最後落在
            // 「Unknown adapters default to Medium」—— 結果是對的，但並非刻意選擇。
            // 這裡把它寫明：iOS 以 Medium 為基準（RENDER_SCALE 0.9、關草地與
            // 地形 GPU 光照），涵蓋較舊機型；高階裝置可在 appsettings.json 用
            // MuOnlineSettings:Graphics:QualityPreset = "High" 覆寫。
            // 實測 iPhone Air（iOS 26.1）在登入場景 draw 僅 5.1 ms，每幀尚餘
            // 約 11 ms，確實有拉到 High 的空間。
            // 先前選 Medium 是因為當時 iOS 的材質 shader 是錯的（沿用了 DesktopGL 版），
            // Medium 剛好避開部分問題。shader 已由 Windows CI 正確編譯後，這個顧慮消失。
            //
            // 改用 High 的關鍵理由是畫質：Medium 會把 HIGH_QUALITY_TEXTURES 設為 false，
            // 而那會讓貼圖取樣退回 SamplerState.PointClamp（最近鄰、完全無濾波）——
            // 盔甲與裝備的細節因此糊掉、邊緣鋸齒，明顯不如 Windows 版。
            // High 用 AnisotropicClamp（各向異性濾波），也是 Windows 版的預設。
            //
            // 效能上負擔得起：iPhone Air 實機每幀 draw 僅 5.1 ms，尚有約 11 ms 餘裕。
            if (OperatingSystem.IsIOS())
                return GraphicsQualityPreset.High;

            LastAdapterInfo = GetAdapterInfo(adapter);

            if (LastAdapterInfo.IsSoftware || LastAdapterInfo.IsIntegrated)
                return GraphicsQualityPreset.Medium;

            if (LastAdapterInfo.IsDiscrete)
                return GraphicsQualityPreset.High;

            // Unknown adapters default to Medium for safety.
            return GraphicsQualityPreset.Medium;
        }

        /// <summary>草地品質等級可選的值。數字＝每格的立牌數。</summary>
        public static readonly int[] GrassQualityLevels = [1, 4, 8];

        /// <summary>
        /// 密度大於 1 時的 alpha 測試門檻。
        /// </summary>
        /// <remarks>
        /// 原版 0.01 幾乎不丟任何像素，而草是「混合 ＋ 深度寫入」——
        /// 立牌重疊之後，看不見的邊緣像素會寫深度並擋掉後面的草，
        /// 畫面上就是一塊一塊的矩形色塊。0.35 讓它接近 cutout：
        /// 只有真的被草蓋住的像素才寫深度，順便省掉大量混合的填充率。
        /// </remarks>
        private const float DenseAlphaReference = 0.35f;

        /// <summary>
        /// 套用草地品質。<paramref name="level"/> 是每格的立牌數（1／4／8）。
        /// </summary>
        /// <remarks>
        /// 交叉片數（planes）不影響三角形數 —— 它只決定那些立牌是各自獨立擺，
        /// 還是共用圓心夾角散開。所以往上一級同時給更多片與更好的排列，成本只跟立牌數走。
        ///
        /// 繪製距離只在密度大於原版時才設限：原版一格一片，遠處的成本本來就低，
        /// 加距離限制只會讓地平線出現一條草消失的界線。
        /// </remarks>
        public static void ApplyGrassQuality(int level)
        {
            switch (level)
            {
                case >= 8:
                    Constants.GRASS_TUFTS_PER_TILE = 8;
                    Constants.GRASS_CLUSTER_PLANES = 3;   // 三角，Lineage W 的做法
                    Constants.GRASS_DRAW_DISTANCE = 8000f;
                    Constants.GRASS_ALPHA_REFERENCE = DenseAlphaReference;
                    break;

                case >= 4:
                    Constants.GRASS_TUFTS_PER_TILE = 4;
                    Constants.GRASS_CLUSTER_PLANES = 2;   // 十字
                    Constants.GRASS_DRAW_DISTANCE = 8000f;
                    Constants.GRASS_ALPHA_REFERENCE = DenseAlphaReference;
                    break;

                default:
                    Constants.GRASS_TUFTS_PER_TILE = 1;
                    Constants.GRASS_CLUSTER_PLANES = 1;
                    Constants.GRASS_DRAW_DISTANCE = 0f;
                    Constants.GRASS_ALPHA_REFERENCE = 0.01f;   // 原版
                    break;
            }
        }

        private static void ApplyProfile(GraphicsQualityPreset preset)
        {
            switch (preset)
            {
                case GraphicsQualityPreset.Low:
                    Constants.RENDER_SCALE = 0.75f;
                    Constants.MSAA_ENABLED = false;
                    Constants.ENABLE_DYNAMIC_LIGHTS = false;
                    Constants.ENABLE_DYNAMIC_LIGHTING_SHADER = false;
                    Constants.ENABLE_TERRAIN_GPU_LIGHTING = false;
                    Constants.OPTIMIZE_FOR_INTEGRATED_GPU = true;
                    Constants.HIGH_QUALITY_TEXTURES = false;
                    Constants.DRAW_GRASS = false;
                    Constants.ENABLE_ITEM_MATERIAL_SHADER = false;
                    Constants.ENABLE_MONSTER_MATERIAL_SHADER = false;
                    Constants.ENABLE_WEAPON_TRAIL = false;
                    break;

                case GraphicsQualityPreset.Medium:
                    Constants.RENDER_SCALE = 0.9f;
                    Constants.MSAA_ENABLED = false;
                    Constants.ENABLE_DYNAMIC_LIGHTS = true;
                    Constants.ENABLE_DYNAMIC_LIGHTING_SHADER = true;
                    Constants.ENABLE_TERRAIN_GPU_LIGHTING = false;
                    Constants.OPTIMIZE_FOR_INTEGRATED_GPU = true;
                    Constants.HIGH_QUALITY_TEXTURES = false;
                    Constants.DRAW_GRASS = false;
                    Constants.ENABLE_ITEM_MATERIAL_SHADER = true;
                    Constants.ENABLE_MONSTER_MATERIAL_SHADER = true;
                    Constants.ENABLE_WEAPON_TRAIL = true;
                    break;

                case GraphicsQualityPreset.High:
                default:
                    Constants.RENDER_SCALE = 1.0f;
                    Constants.MSAA_ENABLED = false;
                    Constants.ENABLE_DYNAMIC_LIGHTS = true;
                    Constants.ENABLE_DYNAMIC_LIGHTING_SHADER = true;
                    Constants.ENABLE_TERRAIN_GPU_LIGHTING = true;
                    Constants.OPTIMIZE_FOR_INTEGRATED_GPU = false;
                    Constants.HIGH_QUALITY_TEXTURES = true;
                    Constants.DRAW_GRASS = true;
                    Constants.ENABLE_ITEM_MATERIAL_SHADER = true;
                    Constants.ENABLE_MONSTER_MATERIAL_SHADER = true;
                    Constants.ENABLE_WEAPON_TRAIL = true;
                    break;
            }

            // Keep terrain GPU lighting consistent with shader usage.
            if (!Constants.ENABLE_DYNAMIC_LIGHTING_SHADER)
            {
                Constants.ENABLE_TERRAIN_GPU_LIGHTING = false;
            }

#if WINDOWS_DX12 || DESKTOP_VK
            // Native terrain and dynamic-lighting effects still need their fallback path.
            Constants.ENABLE_DYNAMIC_LIGHTING_SHADER = false;
            Constants.ENABLE_TERRAIN_GPU_LIGHTING = false;
#endif
        }

        private static GraphicsAdapterInfo GetAdapterInfo(GraphicsAdapter adapter)
        {
            string description = adapter?.Description ?? string.Empty;
            string deviceName = TryGetAdapterString(adapter, "DeviceName");

            int? vendorId = TryGetAdapterInt(adapter, "VendorId");
            int? deviceId = TryGetAdapterInt(adapter, "DeviceId");

            string name = $"{description} {deviceName}".ToLowerInvariant();

            bool isSoftware = name.Contains("microsoft basic render") ||
                              name.Contains("swiftshader") ||
                              vendorId == 0x1414;

            bool isIntel = vendorId == 0x8086 || name.Contains("intel");
            bool isNvidia = vendorId == 0x10DE || name.Contains("nvidia") || name.Contains("geforce");
            bool isAmd = vendorId == 0x1002 || vendorId == 0x1022 || name.Contains("amd") || name.Contains("radeon");

            bool isAmdIntegrated = isAmd &&
                                   (name.Contains("apu") ||
                                    name.Contains("ryzen") ||
                                    name.Contains("athlon") ||
                                    name.Contains("radeon(tm) graphics") ||
                                    name.Contains("vega") ||
                                    name.Contains("embedded"));

            bool isIntegrated = isIntel ||
                                isAmdIntegrated ||
                                name.Contains("uhd") ||
                                name.Contains("iris") ||
                                name.Contains("xe graphics") ||
                                name.Contains("integrated");

            bool isDiscrete = isNvidia || (isAmd && !isAmdIntegrated);

            return new GraphicsAdapterInfo(description, deviceName, vendorId, deviceId, isIntegrated, isDiscrete, isSoftware);
        }

        private static int? TryGetAdapterInt(GraphicsAdapter adapter, string propertyName)
        {
            if (adapter == null)
                return null;

            try
            {
                var prop = adapter.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
                if (prop == null || prop.PropertyType != typeof(int))
                    return null;

                return (int)prop.GetValue(adapter);
            }
            catch
            {
                return null;
            }
        }

        private static string TryGetAdapterString(GraphicsAdapter adapter, string propertyName)
        {
            if (adapter == null)
                return string.Empty;

            try
            {
                var prop = adapter.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
                if (prop == null || prop.PropertyType != typeof(string))
                    return string.Empty;

                return (string)prop.GetValue(adapter) ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
