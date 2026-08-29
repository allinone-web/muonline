using System.Text.Json;
using Client.Data.MAP;

namespace MuAssets.Core;

/// <summary>
/// 每張圖自訂的「貼圖索引 → 檔名」對應，存在 <c>~/.mu-editor/texture-mappings.json</c>。
/// </summary>
/// <remarks>
/// <b>這同時解決兩件事。</b>
///
/// 一是缺貼圖：<c>TerrainLoader</c> 只掛載 ExtTile01–16（索引 14–29），
/// 而這與原版客戶端一致 —— MuMain 的 <c>MapManager.cpp</c> 也是
/// <c>for (int i = 1; i &lt;= 16; i++)</c>，索引 30–32 是草地疊層（<c>BITMAP_MAPGRASS</c>）
/// 而不是 ExtTile17–19。Season 20 的新圖（World139/142/143）用到索引 33 以上，
/// 那是 S20 才擴充的空間，S6 世代的客戶端沒有對應的槽位。缺的部分只能自己指定。
///
/// 二是貼圖替換：要換掉某個索引用的圖，改這裡就好，不必動原始資源。
/// </remarks>
public sealed class TextureMappingStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _path;
    private readonly Dictionary<string, Dictionary<string, string>> _overrides = new(StringComparer.OrdinalIgnoreCase);

    public TextureMappingStore(string path)
    {
        _path = path;
        Load();
    }

    /// <summary>某張圖有幾個自訂對應。</summary>
    public int CountFor(int worldIndex)
        => _overrides.TryGetValue(Key(worldIndex), out var map) ? map.Count : 0;

    /// <summary>取出這張圖的完整索引表：預設值 + ExtTile01–16 + 使用者覆寫。</summary>
    public Dictionary<int, string> BuildFor(int worldIndex)
    {
        var map = TerrainTextureMapping.BuildIndexMap();

        if (_overrides.TryGetValue(Key(worldIndex), out var custom))
        {
            foreach (var (index, file) in custom)
            {
                if (int.TryParse(index, out int parsed))
                    map[parsed] = file;
            }
        }

        return map;
    }

    public string? Get(int worldIndex, int index)
        => _overrides.TryGetValue(Key(worldIndex), out var map) && map.TryGetValue(index.ToString(), out var file)
            ? file
            : null;

    public void Set(int worldIndex, int index, string fileName)
    {
        if (!_overrides.TryGetValue(Key(worldIndex), out var map))
            _overrides[Key(worldIndex)] = map = [];

        map[index.ToString()] = fileName;
        Save();
    }

    public void Clear(int worldIndex, int index)
    {
        if (!_overrides.TryGetValue(Key(worldIndex), out var map))
            return;

        if (map.Remove(index.ToString()))
        {
            if (map.Count == 0)
                _overrides.Remove(Key(worldIndex));

            Save();
        }
    }

    private static string Key(int worldIndex) => $"World{worldIndex}";

    private void Load()
    {
        if (!File.Exists(_path))
            return;

        try
        {
            var stored = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(
                File.ReadAllText(_path), JsonOptions);

            if (stored is null)
                return;

            foreach (var (world, map) in stored)
                _overrides[world] = new Dictionary<string, string>(map);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TextureMappingStore] 讀取 {_path} 失敗：{ex.Message}");
        }
    }

    private void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(_path, JsonSerializer.Serialize(_overrides, JsonOptions));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TextureMappingStore] 寫入 {_path} 失敗：{ex.Message}");
        }
    }
}
