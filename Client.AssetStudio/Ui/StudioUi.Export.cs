using Client.AssetStudio.Catalog;
using Client.AssetStudio.Export;
using Client.AssetStudio.Textures;
using ImGuiNET;

namespace Client.AssetStudio.Ui;

public sealed partial class StudioUi
{
    private string _exportDirectory;
    private bool _exportTextures = true;
    private bool _exportUseViewerSpeed = true;
    private float _exportFps = GltfExporter.DefaultFramesPerSecond;
    private string _batchFilter = string.Empty;
    private string _batchLog = string.Empty;

    /// <summary>
    /// 匯出。單一模型或整批，輸出 glTF 2.0 + PNG。
    /// </summary>
    /// <remarks>
    /// 這是整個工具最終要通往的地方：<b>把資產搬出 MU 的自訂格式</b>。
    /// 匯出之後就能用 Blender 編輯、做網頁檢視器、建立自己的美術管線，
    /// 而長期方向是讓客戶端讀 glTF（<c>STRATEGY.md</c> 第 4 節），
    /// 所以<b>反方向不做</b> —— 不維護一個自製的 3D 格式轉換器。
    ///
    /// 匯出的檔案是 Webzen 的美術資產，只能用於研究與替換素材的前置作業，不可散布。
    /// </remarks>
    private void DrawExportPanel()
    {
        PlaceWindow("匯出");
        ImGui.Begin("匯出 glTF", ref _showExport);

        ImGui.SetNextItemWidth(-100f);
        ImGui.InputText("輸出資料夾", ref _exportDirectory, 512);

        ImGui.SameLine();
        if (ImGui.Button("開啟"))
        {
            Directory.CreateDirectory(_exportDirectory);
            RevealInFinder(_exportDirectory);
        }

        ImGui.Checkbox("一併匯出貼圖（PNG）", ref _exportTextures);

        ImGui.Checkbox("動畫速率沿用檢視器", ref _exportUseViewerSpeed);
        HelpMarker(".bmd 沒有存播放速度，BMDReader 根本不讀，PlaySpeed 一律是 1。\n"
                 + "遊戲裡的實際速率是 PlaySpeed × AnimationSpeed，未經調整時等於 4 影格／秒。\n"
                 + "勾這個 = 用檢視器目前的速度匯出，所見即所得。");

        if (!_exportUseViewerSpeed)
        {
            ImGui.SetNextItemWidth(200f);
            ImGui.SliderFloat("影格／秒", ref _exportFps, 1f, 60f, "%.1f");
        }

        ImGui.Separator();

        var entry = _session.Selected;

        if (entry?.FullPath is null)
        {
            ImGui.TextColored(Muted, "選一個模型才能匯出");
        }
        else
        {
            ImGui.Text($"目前選取：{entry.Name}");

            if (ImGui.Button("匯出這一個"))
                ExportOne(entry);
        }

        ImGui.Separator();
        ImGui.Text("整批匯出");
        HelpMarker("目前目錄面板的篩選結果會全部匯出，每個模型一個子資料夾。\n"
                 + "六千個模型全匯會花很久而且吃掉不少磁碟，建議先用篩選縮小範圍。");

        ImGui.SetNextItemWidth(-150f);
        ImGui.InputTextWithHint("##batchFilter", "額外的名稱篩選（留空 = 全部）", ref _batchFilter, 96);

        ImGui.SameLine();
        var pending = ResolveBatch();

        if (ImGui.Button($"匯出 {pending.Length} 個"))
            ExportBatch(pending);

        if (!string.IsNullOrEmpty(_batchLog))
        {
            ImGui.Separator();
            ImGui.TextWrapped(_batchLog);
        }

        ImGui.End();
    }

    private EntityEntry[] ResolveBatch()
    {
        var entries = ResolveVisible();

        if (!string.IsNullOrWhiteSpace(_batchFilter))
        {
            entries = entries
                .Where(e => e.Search.Contains(_batchFilter, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        return entries.Where(e => e.FullPath is not null).ToArray();
    }

    private float ResolveExportFps()
        => _exportUseViewerSpeed ? MathF.Max(_session.AnimationSpeed, 0.1f) : _exportFps;

    private void ExportOne(EntityEntry entry)
    {
        try
        {
            string directory = Path.Combine(_exportDirectory, SafeName(entry));

            var result = GltfExporter.Export(entry.FullPath!, directory,
                new GltfExporter.Options(ResolveExportFps(), _exportTextures, entry.Kind));

            _session.Report(
                $"已匯出 {Path.GetFileName(result.GltfPath)}："
              + $"{result.Meshes} 網格、{result.Bones} 骨骼、{result.Animations} 動畫、{result.Textures} 貼圖"
              + (result.Warnings.Length > 0 ? $"　注意：{result.Warnings.Length} 項警告" : string.Empty),
                failed: result.Warnings.Length > 0);

            _batchLog = result.Warnings.Length > 0
                ? string.Join("\n", result.Warnings.Take(20))
                : $"{result.GltfPath}";
        }
        catch (Exception ex)
        {
            _session.Report($"匯出失敗：{ex.GetType().Name} {ex.Message}", failed: true);
        }
    }

    /// <summary>
    /// 整批匯出。同步跑（會凍住畫面）——
    /// 丟到背景執行緒的話貼圖解碼會與主執行緒的 GPU 操作衝突，
    /// 而且進度回報要另外做一套同步；換來的只是「凍住時看得到動畫」。
    /// </summary>
    private void ExportBatch(EntityEntry[] entries)
    {
        int ok = 0;
        int failed = 0;
        var warnings = new List<string>();

        foreach (var entry in entries)
        {
            try
            {
                var result = GltfExporter.Export(
                    entry.FullPath!,
                    Path.Combine(_exportDirectory, SafeName(entry)),
                    new GltfExporter.Options(ResolveExportFps(), _exportTextures, entry.Kind));

                ok++;
                warnings.AddRange(result.Warnings.Select(w => $"{entry.Name}：{w}"));
            }
            catch (Exception ex)
            {
                failed++;
                warnings.Add($"{entry.Name}：{ex.GetType().Name} {ex.Message}");
            }
        }

        _batchLog = warnings.Count == 0
            ? "沒有警告。"
            : string.Join("\n", warnings.Take(40))
              + (warnings.Count > 40 ? $"\n…另有 {warnings.Count - 40} 項" : string.Empty);

        _session.Report($"整批匯出完成：成功 {ok}、失敗 {failed}", failed: failed > 0);
        TextureResolver.InvalidateAll();
    }

    /// <summary>檔名安全化。資源包裡有 <c>!Chrome01</c> 這種名字。</summary>
    private static string SafeName(EntityEntry entry)
    {
        string name = entry.Number >= 0 ? $"{entry.Number:000}_{entry.Name}" : entry.Name;

        foreach (char invalid in Path.GetInvalidFileNameChars())
            name = name.Replace(invalid, '_');

        return name.Replace(' ', '_');
    }
}
