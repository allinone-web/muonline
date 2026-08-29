using System.Text.Json;

namespace MuAssets.Core;

/// <summary>
/// 跨執行階段保留的設定，存在 <c>~/.mu-editor/settings.json</c>。
/// </summary>
public sealed class EditorSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>匯出的客戶端資源放這裡。<b>預設不是遊戲的 Data 目錄</b> —— 見 <see cref="MapExporter"/>。</summary>
    public string OutputRoot { get; set; } = DefaultOutputRoot;

    /// <summary>專案（map.json + PNG）放這裡。</summary>
    public string ProjectRoot { get; set; } = DefaultProjectRoot;

    /// <summary>部署目標：要把匯出結果複製進去的遊戲 Data 目錄。空字串表示還沒設定。</summary>
    public string DeployDataPath { get; set; } = string.Empty;

    /// <summary>
    /// 介面字型大小。macOS 上視窗不是 HiDPI，系統會把畫面放大約 1.7 倍，
    /// 調大字型是最直接的補償方式。
    /// </summary>
    /// <summary>
    /// <c>Client.Main/Worlds</c> 的路徑。填了的話，新建地圖會順便產生
    /// <c>World{N}.cs</c>（帶 <c>[WorldInfo]</c>）—— 客戶端靠那個屬性認得這張圖。
    /// 留空就只建資料，類別自己補。
    /// </summary>
    public string WorldsSourcePath { get; set; } = string.Empty;

    public float FontSize { get; set; } = 17f;

    /// <summary>
    /// 只顯示相機焦點這個半徑（世界單位）內的物件；0 = 全部顯示。
    /// </summary>
    /// <remarks>
    /// 俯視整張圖時遊戲的視錐裁切等於沒裁：勒瑞西亞 2833 個物件各一次 draw call，
    /// 場景就吃掉 18ms。畫地形時把這個值調到 8000 左右（80 格）會明顯變順，
    /// 代價是遠處的建築不顯示。預設 0（全部顯示），因為「看得到全貌」通常更重要。
    /// </remarks>
    public float ObjectDrawDistance { get; set; }

    public static string ConfigDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".mu-editor");

    private static string DefaultOutputRoot => Path.Combine(ConfigDirectory, "output");
    private static string DefaultProjectRoot => Path.Combine(ConfigDirectory, "projects");

    private static string SettingsPath => Path.Combine(ConfigDirectory, "settings.json");

    public static EditorSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<EditorSettings>(File.ReadAllText(SettingsPath), JsonOptions) ?? new();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EditorSettings] 讀取失敗，改用預設值：{ex.Message}");
        }

        return new EditorSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(ConfigDirectory);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EditorSettings] 寫入失敗：{ex.Message}");
        }
    }

    public string OutputDirectoryFor(int worldIndex) => Path.Combine(OutputRoot, $"World{worldIndex}");

    public string ProjectDirectoryFor(int worldIndex) => Path.Combine(ProjectRoot, $"World{worldIndex}");
}
