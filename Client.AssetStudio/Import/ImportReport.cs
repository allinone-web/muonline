namespace Client.AssetStudio.Import;

public enum ImportSeverity
{
    /// <summary>只是資訊，不影響結果。</summary>
    Info,

    /// <summary>能匯入，但遊戲裡會與原檔不一樣。</summary>
    Warning,

    /// <summary>匯不進來。</summary>
    Error,
}

public sealed record ImportIssue(ImportSeverity Severity, string Title, string Detail);

/// <summary>
/// 匯入一個外部模型的結果報告。
/// </summary>
/// <remarks>
/// <b>這份報告才是匯入器真正的產出。</b>
/// 轉檔本身不難，難的是誠實地說出「你的模型有哪些東西這個遊戲表達不了」——
/// 多骨權重會被壓成單骨、縮放會被烘進頂點、morph target 直接消失。
/// 靜默降級的匯入器最糟：模型進去了、看起來「差不多」，
/// 等到動起來才發現關節扭曲，而那時候已經不知道是哪一步弄壞的。
/// </remarks>
public sealed class ImportReport
{
    private readonly List<ImportIssue> _issues = [];

    public IReadOnlyList<ImportIssue> Issues => _issues;

    public bool HasErrors => _issues.Any(i => i.Severity == ImportSeverity.Error);

    public int WarningCount => _issues.Count(i => i.Severity == ImportSeverity.Warning);

    // ── 統計 ─────────────────────────────────────────────────────

    public int Meshes { get; set; }
    public int Triangles { get; set; }
    public int Vertices { get; set; }
    public int Bones { get; set; }
    public int Animations { get; set; }
    public int Textures { get; set; }

    /// <summary>模型在 MU 世界單位下的高度（套用匯入縮放之後）。</summary>
    public float Height { get; set; }

    /// <summary>建議的匯入縮放：讓模型高度接近 MU 的角色高度。</summary>
    public float SuggestedScale { get; set; } = 1f;

    public void Info(string title, string detail = "") => _issues.Add(new(ImportSeverity.Info, title, detail));

    public void Warn(string title, string detail = "") => _issues.Add(new(ImportSeverity.Warning, title, detail));

    public void Error(string title, string detail = "") => _issues.Add(new(ImportSeverity.Error, title, detail));

    public string Summary => HasErrors
        ? $"匯入失敗：{_issues.Count(i => i.Severity == ImportSeverity.Error)} 個錯誤"
        : $"{Meshes} 網格、{Triangles:N0} 三角形、{Bones} 骨骼、{Animations} 個動作、{Textures} 張貼圖"
          + (WarningCount > 0 ? $"，{WarningCount} 項要注意" : string.Empty);
}
