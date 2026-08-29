using System.Text.Json;
using System.Text.Json.Serialization;

namespace Client.AssetStudio.Catalog;

/// <summary>一個資源在「換成自己的美術」這件事上的狀態。</summary>
public enum AssetTag
{
    None = 0,
    Keep,
    ToReplace,
    Replaced,
    Unused,
}

/// <summary>
/// 人工標註：這個資源要不要換、換了沒有。
/// </summary>
/// <remarks>
/// <b>這是整個工具唯一「使用者自己產生的資料」，也是它真正的長期價值。</b>
/// 專案的目標是「完全成為自己的遊戲」，而那件事的形狀就是
/// 「4739 個資源，一個一個換掉」——
/// 沒有進度追蹤的話，換到第三百個就不知道哪些換過了。
///
/// 存在使用者目錄（<c>~/.mu-studio/asset-tags.json</c>）而不是資源目錄：
/// 資源是 Webzen 的版權素材、而且會整包重灌，標註不該跟著一起被覆蓋。
/// 鍵用「相對於 Data 的模型路徑」，換一份資源包也還對得上。
///
/// 與地圖編輯器的 <c>~/.mu-editor/object-catalog.json</c> 是同一個作法，
/// 但刻意分開存：那邊記的是「這個地圖物件是樹還是石頭」（分類），
/// 這邊記的是「這個素材換掉了沒有」（進度），兩件事的生命週期不同。
/// </remarks>
public sealed class AssetTagStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _path;
    private Dictionary<string, AssetNote> _notes = new(StringComparer.OrdinalIgnoreCase);

    public sealed class AssetNote
    {
        public AssetTag Tag { get; set; }
        public string? Note { get; set; }
    }

    public string Path => _path;

    /// <summary>最近一次存檔失敗的訊息。UI 顯示出來，不要讓標註靜靜地掉。</summary>
    public string? LastError { get; private set; }

    public AssetTagStore(string? path = null)
    {
        _path = path ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".mu-studio",
            "asset-tags.json");

        Load();
    }

    public int Count => _notes.Count(n => n.Value.Tag != AssetTag.None);

    public AssetTag TagOf(string modelPath)
        => _notes.TryGetValue(modelPath, out var note) ? note.Tag : AssetTag.None;

    public string NoteOf(string modelPath)
        => _notes.TryGetValue(modelPath, out var note) ? note.Note ?? string.Empty : string.Empty;

    public void SetTag(string modelPath, AssetTag tag)
    {
        var note = Ensure(modelPath);
        note.Tag = tag;
        Prune(modelPath, note);
        Save();
    }

    public void SetNote(string modelPath, string text)
    {
        var note = Ensure(modelPath);
        note.Note = string.IsNullOrWhiteSpace(text) ? null : text;
        Prune(modelPath, note);
        Save();
    }

    public int CountOf(AssetTag tag) => _notes.Count(n => n.Value.Tag == tag);

    private AssetNote Ensure(string modelPath)
    {
        if (!_notes.TryGetValue(modelPath, out var note))
            _notes[modelPath] = note = new AssetNote();

        return note;
    }

    /// <summary>標註被清空的話就把整筆移掉，檔案才不會慢慢長出幾千個空物件。</summary>
    private void Prune(string modelPath, AssetNote note)
    {
        if (note.Tag == AssetTag.None && string.IsNullOrEmpty(note.Note))
            _notes.Remove(modelPath);
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path))
                return;

            var loaded = JsonSerializer.Deserialize<Dictionary<string, AssetNote>>(
                File.ReadAllText(_path), SerializerOptions);

            if (loaded is not null)
                _notes = new Dictionary<string, AssetNote>(loaded, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            // 讀不到就當成空的。標註是輔助資料，不該擋住工具啟動。
            LastError = $"讀取標註失敗（將以空白開始）：{ex.Message}";
        }
    }

    private void Save()
    {
        try
        {
            string directory = System.IO.Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(directory);

            // 與貼圖寫入同樣的理由：先寫暫存檔再換上去。
            // 這份檔案是使用者累積了幾百筆的手工成果，寫壞的代價比重畫一張貼圖大得多。
            string temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(_notes, SerializerOptions));
            File.Move(temporary, _path, overwrite: true);

            LastError = null;
        }
        catch (Exception ex)
        {
            LastError = $"標註存檔失敗：{ex.Message}";
        }
    }
}

public static class AssetTagNames
{
    private static readonly Dictionary<AssetTag, string> Names = new()
    {
        [AssetTag.None] = "未標註",
        [AssetTag.Keep] = "保留原樣",
        [AssetTag.ToReplace] = "待替換",
        [AssetTag.Replaced] = "已替換",
        [AssetTag.Unused] = "不使用",
    };

    public static string Of(AssetTag tag) => Names.GetValueOrDefault(tag, tag.ToString());

    public static AssetTag[] All { get; } = Enum.GetValues<AssetTag>();
}
