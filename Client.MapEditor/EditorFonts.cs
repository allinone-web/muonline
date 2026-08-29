using ImGuiNET;
using MuAssets.Core;

namespace Client.MapEditor;

/// <summary>
/// ImGui 內建字型只有 ASCII，中文會全部變成 <c>????</c>。這裡從系統字型裡挑一個含 CJK 的載入。
/// </summary>
/// <remarks>
/// <b>拉丁字與中文分兩次載入、用不同的 oversampling。</b>
///
/// stb_truetype 完全不做 hinting，16px 的字沒有加強取樣就會糊成一片 ——
/// 這是介面看起來「模模糊糊」的主因之一。ImGui 預設 <c>OversampleH = 3</c>，
/// 但 CJK 全範圍有兩萬多個字，開 3 倍圖集會爆掉。
///
/// 所以：拉丁字（約 200 個字）用 OversampleH = 3 換清晰度，
/// CJK 用 1 換圖集大小，再用 MergeMode 併成同一份字型。
/// </remarks>
public static class EditorFonts
{
    /// <summary>
    /// macOS 內建的 CJK 字型，依偏好排序。都是 .ttc（字型集合），
    /// ImGui 的 stb_truetype 後端靠 <c>ImFontConfig.FontNo</c> 選集合裡的第幾個字型，0 即可。
    /// </summary>
    private static readonly string[] CandidateFonts =
    [
        "/System/Library/Fonts/Hiragino Sans GB.ttc",
        "/System/Library/Fonts/STHeiti Medium.ttc",
        "/System/Library/Fonts/Supplemental/Songti.ttc",
        "/System/Library/Fonts/Supplemental/Arial Unicode.ttf",
    ];

    /// <summary>
    /// 載入含 CJK 的字型。找不到就沿用 ImGui 內建字型（介面仍可用，但中文會是問號）。
    /// </summary>
    /// <param name="sizePixels">字型大小。視窗被系統放大時（Retina 非 HiDPI）調大這個值最有感。</param>
    /// <returns>實際載入的字型路徑，沒載到則為 null。</returns>
    public static unsafe string? LoadCjkFont(float sizePixels = 17f)
    {
        var io = ImGui.GetIO();

        foreach (var path in CandidateFonts)
        {
            if (!File.Exists(path))
                continue;

            // 拉丁字：字數少，加強水平取樣換清晰度。
            var latin = ImGuiNative.ImFontConfig_ImFontConfig();
            latin->FontNo = 0;
            latin->OversampleH = 3;
            latin->OversampleV = 1;
            latin->PixelSnapH = 1;

            var font = io.Fonts.AddFontFromFileTTF(path, sizePixels, latin, io.Fonts.GetGlyphRangesDefault());
            ImGuiNative.ImFontConfig_destroy(latin);

            if (font.NativePtr is null)
                continue;

            // CJK：兩萬多個字，只能用 1 倍取樣，否則圖集會大到上不了 GPU。
            var cjk = ImGuiNative.ImFontConfig_ImFontConfig();
            cjk->FontNo = 0;
            cjk->OversampleH = 1;
            cjk->OversampleV = 1;
            cjk->PixelSnapH = 1;
            cjk->MergeMode = 1;

            io.Fonts.AddFontFromFileTTF(path, sizePixels, cjk, io.Fonts.GetGlyphRangesChineseFull());
            ImGuiNative.ImFontConfig_destroy(cjk);

            return path;
        }

        return null;
    }
}
