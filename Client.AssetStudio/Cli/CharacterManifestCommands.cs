using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Client.AssetStudio.Catalog;
using Client.Data.BMD;
using Client.Main.Models;

namespace Client.AssetStudio.Cli;

/// <summary>把 MU 玩家角色的原始資產事實編譯成引擎中立、可重現的 JSON。</summary>
/// <remarks>只做離線抽取，不重排、不壓縮、也不替未知動作猜名稱。</remarks>
public static class CharacterManifestCommands
{
    public const int SchemaVersion = 1;
    public const string GeneratorVersion = "1.0.0";
    private const string PlayerModelPath = "Player/Player.bmd";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static int Compile(EntityCatalog catalog, string dataPath, string outputPath)
    {
        try
        {
            var manifest = Build(catalog, dataPath);
            string? directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(outputPath, JsonSerializer.Serialize(manifest, JsonOptions) + "\n", new UTF8Encoding(false));

            Console.WriteLine($"角色 manifest：{outputPath}");
            Console.WriteLine($"來源 SHA-256：{manifest.SourceHash}");
            Console.WriteLine($"Player.bmd：{manifest.Player.Meshes} 網格、{manifest.Player.Skeleton.Count} 骨骼、{manifest.Player.Actions.Count} 動作槽");
            Console.WriteLine($"具名 {manifest.Player.Actions.Count(a => a.Status == "Named")}、Unknown {manifest.Player.Actions.Count(a => a.Status == "Unknown")}");
            Console.WriteLine($"職業外觀：{manifest.PlayerClasses.Count} 套，每套固定五部位");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"角色 manifest 失敗：{ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    public static int SelfTest(EntityCatalog catalog, string dataPath)
    {
        string temporary = Path.Combine(Path.GetTempPath(), $"mu-character-manifest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporary);

        try
        {
            var manifest = Build(catalog, dataPath);
            byte[] first = Serialize(manifest);
            byte[] second = Serialize(Build(catalog, dataPath));
            var problems = new List<string>();

            if (!first.AsSpan().SequenceEqual(second))
                problems.Add("相同來源編譯兩次的 JSON 不相同");
            if (manifest.Player.Actions.Count != 380)
                problems.Add($"Player.bmd 動作槽應為 380，實際 {manifest.Player.Actions.Count}");
            if (manifest.Player.Actions.Where((action, index) => action.Index != index).Any())
                problems.Add("動作槽 index 不是 BMD 原始連續順序");
            if (manifest.Player.Skeleton.Count != 60)
                problems.Add($"Player.bmd 骨骼應為 60，實際 {manifest.Player.Skeleton.Count}");
            if (manifest.Player.Meshes != 0)
                problems.Add($"Player.bmd 應是純骨架（0 網格），實際 {manifest.Player.Meshes}");
            if (manifest.Player.Actions.Where(a => a.Index < 284).Any(a => a.Status != "Named"))
                problems.Add("0–283 應全部由 PlayerAction 命名");
            if (manifest.Player.Actions.Where(a => a.Index >= 284).Any(a => a.Status != "Unknown" || a.Name != "Unknown"))
                problems.Add("284–379 必須明確標為 Unknown，不可猜名");
            if (manifest.PlayerClasses.Any(c => c.Parts.Count != 5 || c.Parts.Select(p => p.Slot).Distinct().Count() != 5))
                problems.Add("職業外觀不是恰好五個不同部位");
            if (manifest.PlayerClasses.Count != 56 || manifest.PlayerClasses.Sum(c => c.Parts.Count) != 280)
                problems.Add($"現行真值應為 56 套、280 個部位，實際 {manifest.PlayerClasses.Count} 套、{manifest.PlayerClasses.Sum(c => c.Parts.Count)} 個");

            string path = Path.Combine(temporary, "character-manifest.json");
            File.WriteAllBytes(path, first);
            var roundTrip = JsonSerializer.Deserialize<CharacterManifest>(File.ReadAllBytes(path), JsonOptions);
            if (roundTrip?.SourceHash != manifest.SourceHash)
                problems.Add("JSON 寫出再讀回不一致");

            Console.WriteLine();
            Console.WriteLine("── MU 角色 manifest 自測 ──");
            Console.WriteLine($"來源 {manifest.SourceHash}");
            Console.WriteLine($"動作 {manifest.Player.Actions.Count}（具名 {manifest.Player.Actions.Count(a => a.Status == "Named")}、Unknown {manifest.Player.Actions.Count(a => a.Status == "Unknown")}）");
            Console.WriteLine($"骨骼 {manifest.Player.Skeleton.Count}、職業外觀 {manifest.PlayerClasses.Count} 套");

            foreach (string problem in problems)
                Console.Error.WriteLine("[FAIL] " + problem);

            Console.WriteLine(problems.Count == 0 ? "全部通過。離開碼 0" : $"{problems.Count} 項失敗。離開碼 1");
            return problems.Count == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"角色 manifest 自測失敗：{ex.GetType().Name}: {ex.Message}");
            return 1;
        }
        finally
        {
            try { Directory.Delete(temporary, recursive: true); } catch { }
        }
    }

    private static byte[] Serialize(CharacterManifest manifest)
        => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest, JsonOptions) + "\n");

    private static CharacterManifest Build(EntityCatalog catalog, string dataPath)
    {
        string playerPath = Path.Combine(dataPath, PlayerModelPath);
        if (!File.Exists(playerPath))
            throw new FileNotFoundException("找不到玩家共用骨架", playerPath);

        var reader = new BMDReader();
        var player = reader.Load(playerPath).GetAwaiter().GetResult();
        var sourceFiles = new SortedDictionary<string, string>(StringComparer.Ordinal);
        string playerHash = AddSource(sourceFiles, PlayerModelPath, playerPath);

        var actions = (player.Actions ?? []).Select((action, index) =>
        {
            bool named = index < (int)PlayerAction.MaxPlayerAction && Enum.IsDefined(typeof(PlayerAction), index);
            return new CharacterAction(
                index,
                named ? Enum.GetName(typeof(PlayerAction), index)! : "Unknown",
                named ? "Named" : "Unknown",
                action?.NumAnimationKeys ?? 0,
                action?.LockPositions ?? false);
        }).ToList();

        var skeleton = (player.Bones ?? []).Select((bone, index) => new CharacterBone(
            index,
            bone is null || bone == BMDTextureBone.Dummy || string.IsNullOrWhiteSpace(bone.Name) ? $"Bone{index:000}" : bone.Name,
            bone is null || bone == BMDTextureBone.Dummy ? -1 : bone.Parent,
            bone is null || bone == BMDTextureBone.Dummy)).ToList();

        var classes = new List<CharacterClassAppearance>();
        foreach (var entry in catalog.Entries.Where(e => e.Kind == EntityKind.Player && e.BodyParts.Length == 5)
                                                     .OrderBy(e => e.Number).ThenBy(e => e.Name, StringComparer.Ordinal))
        {
            var parts = new List<CharacterPart>();
            foreach (string relative in entry.BodyParts.OrderBy(SlotOrder))
            {
                string full = Path.Combine(dataPath, relative);
                if (!File.Exists(full))
                    throw new FileNotFoundException($"職業 {entry.Name} 缺部位", full);

                var model = reader.Load(full).GetAwaiter().GetResult();
                string hash = AddSource(sourceFiles, relative, full);
                parts.Add(new CharacterPart(
                    SlotName(relative), relative.Replace('\\', '/'), hash,
                    model.Meshes?.Length ?? 0, model.Bones?.Length ?? 0));
            }

            classes.Add(new CharacterClassAppearance(entry.Number, entry.Name, entry.Group, parts));
        }

        if (classes.Count == 0)
            throw new InvalidDataException("目錄沒有找到任何完整的五部位職業外觀");

        string aggregate = string.Concat(sourceFiles.Select(pair => $"{pair.Key}\0{pair.Value}\n"));
        string sourceHash = Hex(SHA256.HashData(Encoding.UTF8.GetBytes(aggregate)));

        return new CharacterManifest(
            SchemaVersion,
            new ManifestGenerator("MuAssetStudio.CharacterManifest", GeneratorVersion),
            sourceHash,
            sourceFiles.Select(pair => new ManifestSource(pair.Key, pair.Value)).ToList(),
            new PlayerManifest(PlayerModelPath, playerHash, player.Meshes?.Length ?? 0, skeleton, actions),
            classes);
    }

    private static string AddSource(IDictionary<string, string> sources, string relative, string full)
    {
        relative = relative.Replace('\\', '/');
        string hash = Hex(SHA256.HashData(File.ReadAllBytes(full)));
        sources[relative] = hash;
        return hash;
    }

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

    private static int SlotOrder(string path) => SlotName(path) switch
    {
        "Helm" => 0, "Armor" => 1, "Pant" => 2, "Glove" => 3, "Boot" => 4, _ => 99,
    };

    private static string SlotName(string path)
    {
        string name = Path.GetFileName(path);
        foreach (string slot in new[] { "Helm", "Armor", "Pant", "Glove", "Boot" })
            if (name.Contains(slot, StringComparison.OrdinalIgnoreCase))
                return slot;
        return "Unknown";
    }
}

public sealed record CharacterManifest(
    int SchemaVersion,
    ManifestGenerator Generator,
    string SourceHash,
    List<ManifestSource> Sources,
    PlayerManifest Player,
    List<CharacterClassAppearance> PlayerClasses);

public sealed record ManifestGenerator(string Name, string Version);
public sealed record ManifestSource(string Path, string Sha256);
public sealed record PlayerManifest(
    string ModelPath,
    string Sha256,
    int Meshes,
    List<CharacterBone> Skeleton,
    List<CharacterAction> Actions);
public sealed record CharacterBone(int Index, string Name, int Parent, bool Dummy);
public sealed record CharacterAction(int Index, string Name, string Status, int Frames, bool LockPositions);
public sealed record CharacterClassAppearance(int ClassId, string Name, string Group, List<CharacterPart> Parts);
public sealed record CharacterPart(string Slot, string ModelPath, string Sha256, int Meshes, int Bones);
