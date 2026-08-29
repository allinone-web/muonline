using System;

namespace Client.Main.Objects
{
    /// <summary>
    /// 手機上把「會動的角色」放大的倍率。
    ///
    /// 目的不是把鏡頭拉近。拉近鏡頭時地磚會跟著放大 —— 地形貼圖只有
    /// 128x128 到 256x256，放大就是把不存在的細節攤開，只會更糊。
    /// 這裡改為只放大角色、怪物與 NPC，地形與建築維持原尺寸：角色在畫面上
    /// 夠大，地面卻仍留在 mipmap 取樣得漂亮的距離。兩件事就此解耦。
    ///
    /// 分三類是因為 MU 的美術本來就不是同一個尺度 —— 玩家是人類、偏小；
    /// 怪物多半是巨人與野獸、本來就大；人形 NPC 則與玩家相當。
    /// 三者共用一個倍率會讓怪物大得誇張。
    ///
    /// 桌面完全不受影響（一律 1.0）。
    /// </summary>
    internal static class ActorScale
    {
        private const float MinScale = 0.5f;
        private const float MaxScale = 2.0f;

        private static volatile bool _loaded;
        private static float _player = 1f;
        private static float _monster = 1f;
        private static float _npc = 1f;

        public static float Player { get { EnsureLoaded(); return _player; } }
        public static float Monster { get { EnsureLoaded(); return _monster; } }
        public static float Npc { get { EnsureLoaded(); return _npc; } }

        private static void EnsureLoaded()
        {
            if (_loaded)
                return;

            if (!OperatingSystem.IsIOS() && !OperatingSystem.IsAndroid())
            {
                _loaded = true;
                return;
            }

            var graphics = MuGame.AppSettings?.Graphics;
            if (graphics == null)
            {
                // 設定還沒載入。這一次先用 1.0，不要把預設值鎖進快取 ——
                // 下次存取時設定就緒了會重新讀。
                return;
            }

            var mobile = graphics.Mobile;
            if (mobile != null)
            {
                _player = Sanitize(mobile.PlayerScale);
                _monster = Sanitize(mobile.MonsterScale);
                _npc = Sanitize(mobile.NpcScale);
            }

            _loaded = true;
        }

        private static float Sanitize(float value)
            => value <= 0f ? 1f : Math.Clamp(value, MinScale, MaxScale);
    }
}
