using System;
using Microsoft.Xna.Framework;

namespace Client.Main.Graphics
{
    /// <summary>
    /// 超寬螢幕的取景修正。
    ///
    /// <see cref="Matrix.CreatePerspectiveFieldOfView"/> 吃的是<b>垂直</b>視角，水平視角
    /// 由長寬比推導。手機是 21:9 左右，用照 16:9 調好的垂直視角，水平方向會多看到將近
    /// 四分之一 —— 登入畫面因此連海面貼圖的邊緣都露出來了，角色與船也顯得很小。
    ///
    /// 這裡反過來壓縮垂直視角，讓<b>水平</b>取景維持設計時的樣子。畫面上下會被裁掉一些，
    /// 但主體會回到應有的大小，也不會看到地圖邊界。
    ///
    /// 只用在登入與選角這種「固定機位的展示畫面」。遊戲中的鏡頭維持原樣 ——
    /// 那裡「螢幕越寬看得越多」是優點。
    /// </summary>
    public static class WideScreenFraming
    {
        /// <summary>這個客戶端的 UI 與鏡頭都是照 16:9 調的。</summary>
        public const float ReferenceAspect = 16f / 9f;

        /// <param name="verticalFovDegrees">原本的垂直視角。</param>
        /// <param name="aspectRatio">實際的畫面長寬比。</param>
        /// <param name="extraZoom">額外拉近的倍率，1 表示只做長寬比補償。</param>
        public static float CompensateVerticalFov(float verticalFovDegrees, float aspectRatio, float extraZoom = 1f)
        {
            if (verticalFovDegrees <= 0f || aspectRatio <= 0f)
                return verticalFovDegrees;

            // 比參考長寬比窄的螢幕不做補償 —— 否則會變成把畫面往外拉。
            float aspectScale = MathF.Min(ReferenceAspect / aspectRatio, 1f);
            float scale = aspectScale / MathF.Max(extraZoom, 0.05f);

            float tangent = MathF.Tan(MathHelper.ToRadians(verticalFovDegrees) * 0.5f) * scale;
            return MathHelper.ToDegrees(2f * MathF.Atan(tangent));
        }
    }
}
