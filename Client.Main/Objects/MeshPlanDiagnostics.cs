using System;
using System.Collections.Concurrent;

namespace Client.Main.Objects
{
    /// <summary>
    /// 追查「戰士看不到身體」用的臨時診斷。
    ///
    /// 已排除的可能：模型能解析（BMDLoader 沒有 MODEL NOT FOUND）、貼圖檔案存在
    /// （離線工具逐一確認過）、網格沒有被隱藏（傾印顯示 hiddenMesh=-1）。
    /// 剩下的只可能發生在建立繪製計畫的階段 —— 這裡逐網格記錄它被排入或被跳過，
    /// 以及被跳過的原因。
    ///
    /// 只對角色身體部位輸出，且同一物件同一網格只記一次，避免每幀刷屏。
    /// </summary>
    internal static class MeshPlanDiagnostics
    {
        private static readonly ConcurrentDictionary<string, byte> _reported = new();

        public static bool ShouldDiagnose(ModelObject obj)
        {
            string name = obj?.GetType().Name;
            return name != null
                && name.StartsWith("Player", StringComparison.Ordinal)
                && name.EndsWith("Object", StringComparison.Ordinal);
        }

        public static void Report(ModelObject obj, int meshIndex, string texturePath, string outcome, string detail)
        {
            string partName = obj.GetType().Name;
            string key = $"{partName}|{meshIndex}|{texturePath}|{outcome}";

            if (_reported.TryAdd(key, 0))
            {
                Console.WriteLine($"[MeshPlan] {partName} mesh[{meshIndex}] '{texturePath}' {outcome} ({detail})");
            }
        }
    }
}
