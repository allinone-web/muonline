using System.Reflection;
using Client.Data.BMD;
using Microsoft.Xna.Framework;

namespace Client.AssetStudio.Textures;

/// <summary>
/// 技能圖示在圖集裡的位置。轉呼叫 <c>Client.Main</c> 的 <c>SkillIconAtlas</c>。
/// </summary>
/// <remarks>
/// <c>SkillIconAtlas</c> 是 <c>internal</c>，所以只能用反射叫。這是刻意的選擇：
/// 那張表有將近二十條手工調出來的特例（寵物指令、Alice 系列、大師技用另一張
/// 512×512、一列 25 格的圖集…），全部是對照原版 <c>NewUIMuHelper.cpp</c> 逐條試出來的。
/// 在工具裡再抄一份，等於保證兩邊會慢慢漂移 —— 而漂移的症狀是
/// 「工具裡的圖示和遊戲裡不一樣」，非常難察覺。
///
/// 另一個選擇是在 <c>Client.Main</c> 加 <c>InternalsVisibleTo</c>，但那要動別人正在改的檔案。
/// 反射的代價只有「型別名稱寫錯要到執行期才知道」，用一次初始化的檢查就能擋住。
/// </remarks>
public static class SkillIconResolver
{
    private const string AtlasTypeName = "Client.Main.Controls.UI.Game.Skills.SkillIconAtlas";
    private const string FrameTypeName = "Client.Main.Controls.UI.Game.Skills.SkillIconFrame";

    private static readonly MethodInfo? TryResolveMethod;
    private static readonly PropertyInfo? TexturePathProperty;
    private static readonly PropertyInfo? SourceRectangleProperty;

    /// <summary>反射沒接上時的說明，UI 直接顯示出來而不是靜靜地少一排圖示。</summary>
    public static string? Unavailable { get; }

    static SkillIconResolver()
    {
        var assembly = typeof(Client.Main.MuGame).Assembly;
        var atlas = assembly.GetType(AtlasTypeName);
        var frame = assembly.GetType(FrameTypeName);

        if (atlas is null || frame is null)
        {
            Unavailable = $"找不到 {AtlasTypeName}，Client.Main 可能改過命名空間";
            return;
        }

        TryResolveMethod = atlas.GetMethod("TryResolve", BindingFlags.Public | BindingFlags.Static);
        TexturePathProperty = frame.GetProperty("TexturePath");
        SourceRectangleProperty = frame.GetProperty("SourceRectangle");

        if (TryResolveMethod is null || TexturePathProperty is null || SourceRectangleProperty is null)
            Unavailable = "SkillIconAtlas.TryResolve 的簽章與預期不同";
    }

    public sealed record IconFrame(string TexturePath, Rectangle Source);

    public static IconFrame? Resolve(int skillId, SkillBMD? definition)
    {
        if (TryResolveMethod is null || skillId is <= 0 or > ushort.MaxValue)
            return null;

        var arguments = new object?[] { (ushort)skillId, definition, null };

        try
        {
            if (TryResolveMethod.Invoke(null, arguments) is not true)
                return null;
        }
        catch
        {
            return null;
        }

        object? frame = arguments[2];
        if (frame is null)
            return null;

        string? path = TexturePathProperty!.GetValue(frame) as string;
        if (string.IsNullOrEmpty(path))
            return null;

        var source = SourceRectangleProperty!.GetValue(frame);
        return source is Rectangle rectangle ? new IconFrame(path, rectangle) : null;
    }

    /// <summary>
    /// 圖集在 <c>Data/</c> 裡的實際檔案。<c>SkillIconAtlas</c> 寫的是 <c>.jpg</c>，
    /// 磁碟上是 <c>.OZJ</c> —— 這正是 <see cref="TextureResolver"/> 存在的理由。
    /// </summary>
    public static string? ResolveAtlasFile(string dataPath, string texturePath)
    {
        string relativeDirectory = Path.GetDirectoryName(texturePath)?.Replace('\\', '/') ?? string.Empty;
        string directory = Path.Combine(dataPath, relativeDirectory);

        return TextureResolver.Resolve(directory, Path.GetFileName(texturePath)).FullPath;
    }
}
