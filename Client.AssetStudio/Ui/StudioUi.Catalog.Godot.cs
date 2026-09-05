using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ImGuiNET;

namespace Client.AssetStudio.Ui;

public sealed partial class StudioUi
{
    /// <summary>
    /// 綜合資源目錄的匯出面板：把 MU 與天堂兩邊的資產一起送去 Godot。
    /// </summary>
    /// <remarks>
    /// <b>這個面板不自己做事，它只是 <c>tools/mu catalog</c> 的介面。</b>
    /// 索引與轉檔的規則只有一份（Python 那邊），介面重寫一份的話兩邊一定會分岔 ——
    /// 而分岔的那天不會有人發現，只會有人抱怨「介面匯出的跟命令列不一樣」。
    ///
    /// 所以這裡做三件事：讀 <c>assets.json</c> 顯示現況、讓人勾分類、把指令跑起來。
    /// </remarks>
    private string _catalogIndexPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".mu-studio", "catalog", "assets.json");

    private string _catalogExportDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Documents", "mu-godot-pack");

    private readonly Dictionary<string, bool> _catalogSelection = new();
    private (string Category, int Count)[] _catalogCounts = [];
    private string _catalogSummary = string.Empty;
    private string _catalogLog = string.Empty;
    private bool _catalogWithModels;
    private bool _catalogRunning;

    private void DrawCatalogGodotPanel()
    {
        PlaceWindow("綜合目錄");
        ImGui.Begin("綜合資源目錄 → Godot", ref _showCatalogGodot);

        ImGui.TextWrapped(
            "MU 的 Data/ 與天堂（梦想与征程）的解析成果，合成同一份索引、同一套分類，"
          + "再匯出成 Godot 吃得下的來源檔（glTF／PNG／WAV／JSON）。");

        ImGui.Separator();

        ImGui.SetNextItemWidth(-140f);
        ImGui.InputText("索引檔", ref _catalogIndexPath, 512);
        ImGui.SameLine();
        if (ImGui.Button("重新載入"))
            LoadCatalogIndex();

        if (_catalogCounts.Length == 0)
        {
            ImGui.TextWrapped(string.IsNullOrEmpty(_catalogSummary)
                ? "還沒有索引。先在終端機跑一次：tools/mu catalog"
                : _catalogSummary);
            ImGui.End();
            return;
        }

        ImGui.TextUnformatted(_catalogSummary);
        ImGui.Separator();

        if (ImGui.Button("全選"))
            SetAllCatalogSelection(true);
        ImGui.SameLine();
        if (ImGui.Button("全不選"))
            SetAllCatalogSelection(false);

        if (ImGui.BeginTable("catalogCounts", 2,
                ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("分類");
            ImGui.TableSetupColumn("筆數", ImGuiTableColumnFlags.WidthFixed, 90f);
            ImGui.TableHeadersRow();

            foreach (var (category, count) in _catalogCounts)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();

                bool selected = _catalogSelection.GetValueOrDefault(category);
                if (ImGui.Checkbox(category, ref selected))
                    _catalogSelection[category] = selected;

                ImGui.TableNextColumn();
                ImGui.TextUnformatted($"{count:N0}");
            }

            ImGui.EndTable();
        }

        ImGui.Separator();
        ImGui.SetNextItemWidth(-140f);
        ImGui.InputText("輸出資料夾", ref _catalogExportDirectory, 512);
        ImGui.SameLine();
        if (ImGui.Button("開啟##catalogOut"))
        {
            Directory.CreateDirectory(_catalogExportDirectory);
            RevealInFinder(_catalogExportDirectory);
        }

        ImGui.Checkbox("一併把 BMD 轉成 glTF", ref _catalogWithModels);
        HelpMarker("勾了會逐個呼叫 C# 的匯出器，數千個模型會跑很久。\n"
                 + "不勾的話模型只會列在清單裡、不轉檔。");

        ImGui.BeginDisabled(_catalogRunning);
        if (ImGui.Button(_catalogRunning ? "匯出中…" : "開始匯出"))
            RunCatalogExport();
        ImGui.EndDisabled();

        if (!string.IsNullOrEmpty(_catalogLog))
        {
            ImGui.Separator();
            ImGui.TextWrapped(_catalogLog);
        }

        ImGui.End();
    }

    private void SetAllCatalogSelection(bool value)
    {
        foreach (var (category, _) in _catalogCounts)
            _catalogSelection[category] = value;
    }

    private void LoadCatalogIndex()
    {
        _catalogCounts = [];
        _catalogSummary = string.Empty;

        if (!File.Exists(_catalogIndexPath))
        {
            _catalogSummary = $"找不到 {_catalogIndexPath}\n先在終端機跑一次：tools/mu catalog";
            return;
        }

        try
        {
            using var stream = File.OpenRead(_catalogIndexPath);
            using var document = JsonDocument.Parse(stream);
            var counts = document.RootElement.GetProperty("counts");

            var byCategory = counts.GetProperty("byCategory");
            var list = new List<(string, int)>();
            foreach (var property in byCategory.EnumerateObject())
                list.Add((property.Name, property.Value.GetInt32()));

            // 照筆數排序 —— 「哪一類最多」是打開這個面板時第一個想知道的事
            _catalogCounts = list.OrderByDescending(item => item.Item2).ToArray();

            foreach (var (category, _) in _catalogCounts)
                _catalogSelection.TryAdd(category, false);

            int total = counts.GetProperty("total").GetInt32();
            var sources = counts.GetProperty("bySource");
            var parts = new StringBuilder($"共 {total:N0} 筆　");
            foreach (var property in sources.EnumerateObject())
                parts.Append($"{property.Name} {property.Value.GetInt32():N0}　");

            _catalogSummary = parts.ToString().TrimEnd('　');
        }
        catch (Exception ex)
        {
            _catalogSummary = $"讀不開索引：{ex.GetType().Name}: {ex.Message}";
        }
    }

    private void RunCatalogExport()
    {
        var chosen = _catalogSelection.Where(pair => pair.Value).Select(pair => pair.Key).ToArray();
        if (chosen.Length == 0)
        {
            _catalogLog = "沒有勾任何分類。";
            return;
        }

        string? toolsRoot = FindToolsEntry();
        if (toolsRoot is null)
        {
            // 找不到就把指令印出來讓人自己貼 —— 比默默失敗有用。
            _catalogLog = "找不到 tools/mu。請自己在終端機跑：\n"
                        + $"  tools/mu catalog --godot {_catalogExportDirectory} "
                        + string.Join(" ", chosen.Select(c => $"--category {c}"));
            return;
        }

        var arguments = new List<string> { "catalog", "--skip-scan", "--godot", _catalogExportDirectory };
        foreach (string category in chosen)
        {
            arguments.Add("--category");
            arguments.Add(category);
        }
        if (_catalogWithModels)
            arguments.Add("--with-models");

        _catalogRunning = true;
        _catalogLog = "匯出中…";

        Task.Run(() =>
        {
            try
            {
                var info = new ProcessStartInfo(toolsRoot)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = Path.GetDirectoryName(Path.GetDirectoryName(toolsRoot)) ?? ".",
                };
                foreach (string argument in arguments)
                    info.ArgumentList.Add(argument);

                using var process = Process.Start(info);
                if (process is null)
                {
                    _catalogLog = "起不動 tools/mu";
                    return;
                }

                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                // 只留最後幾行 —— 整份輸出塞進面板會把版面撐爆
                _catalogLog = Tail(string.IsNullOrWhiteSpace(output) ? error : output, 12);
            }
            catch (Exception ex)
            {
                _catalogLog = $"{ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                _catalogRunning = false;
            }
        });
    }

    private static string Tail(string text, int lines)
    {
        var all = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return string.Join('\n', all.TakeLast(lines));
    }

    /// <summary>從執行檔往上找 <c>tools/mu</c>。</summary>
    private static string? FindToolsEntry()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (int depth = 0; depth < 8 && directory is not null; depth++, directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "tools", "mu");
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }
}
