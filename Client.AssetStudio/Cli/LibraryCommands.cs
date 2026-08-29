using Client.AssetStudio.Catalog;
using Client.AssetStudio.Project;

namespace Client.AssetStudio.Cli;

/// <summary>自有資產的資源庫，命令列版。</summary>
public static class LibraryCommands
{
    public static int List(AssetLibrary library)
    {
        Console.WriteLine();
        Console.WriteLine($"資源庫：{library.Root}");

        if (library.LastError is string error)
            Console.Error.WriteLine(error);

        if (library.Assets.Count == 0)
        {
            Console.WriteLine("（還沒有任何自有資產。用 --library-add <gltf|glb> 加入。）");
            return 0;
        }

        Console.WriteLine();
        Console.WriteLine("id                    分類    綁定           動作對映  名稱");

        foreach (var asset in library.Assets)
        {
            string bind = asset.BindNumber >= 0
                ? $"#{asset.BindNumber}"
                : asset.BindModelPath ?? "－";

            Console.WriteLine($"{asset.Id,-22}{EntityKindNames.Of(asset.Kind),-8}{bind,-15}"
                            + $"{asset.Actions.Count,8}  {asset.Name}");
        }

        return 0;
    }

    public static int Add(AssetLibrary library, string path, string? name, string? kindName)
    {
        var kind = ResolveKind(kindName);

        var asset = library.Add(path, name, kind, out var imported);

        if (imported is not null)
            ImportCommands.PrintReport(Path.GetFileName(path), imported);

        if (asset is null)
        {
            Console.Error.WriteLine(library.LastError ?? "匯入失敗");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine($"已加入資源庫：{asset.Id}（{library.DirectoryOf(asset)}）");
        Console.WriteLine($"建議縮放 ×{asset.Scale:F3} 已記在 asset 上。");
        Console.WriteLine();
        Console.WriteLine("下一步：把外部的動作對到遊戲的動作編號");
        Console.WriteLine($"  MuAssetStudio --library-map {asset.Id} --action 0 --clip <動作名稱>");

        return 0;
    }

    /// <summary>把外部模型的一個動作對到遊戲的動作編號。</summary>
    public static int Map(AssetLibrary library, string id, int action, string? clip)
    {
        var asset = library.Find(id);

        if (asset is null)
        {
            Console.Error.WriteLine($"資源庫裡沒有「{id}」");
            return 2;
        }

        library.MapAction(asset, action, clip);

        Console.WriteLine(clip is null
            ? $"{asset.Id}：動作 {ActionNames.Of(asset.Kind, action)} 的對映已清除"
            : $"{asset.Id}：動作 {ActionNames.Of(asset.Kind, action)} → 「{clip}」");

        return 0;
    }

    /// <summary>顯示一筆資產目前的動作對映，未對映的也列出來。</summary>
    public static int Show(AssetLibrary library, string id)
    {
        var asset = library.Find(id);

        if (asset is null)
        {
            Console.Error.WriteLine($"資源庫裡沒有「{id}」");
            return 2;
        }

        var imported = Import.GltfImporter.Import(
            library.SourcePathOf(asset),
            new Import.GltfImporter.Options(Scale: asset.Scale, AutoScale: false));

        ImportCommands.PrintReport(asset.Name, imported);

        Console.WriteLine();
        Console.WriteLine("動作對映（遊戲的動作編號 ← 外部的動作名稱）");

        // 怪物只有 11 個具名動作；角色那一套有 380 個，全列出來沒有意義。
        int count = asset.Kind == EntityKind.Monster ? 11 : 16;

        for (int action = 0; action < count; action++)
        {
            string clip = library.ClipFor(asset, action) ?? "－";
            Console.WriteLine($"  {ActionNames.Of(asset.Kind, action),-24} ← {clip}");
        }

        if (asset.Actions.Keys.Select(int.Parse).Any(a => a >= count))
        {
            Console.WriteLine("  （還有編號更大的對映，未列出）");
        }

        return 0;
    }

    private static EntityKind ResolveKind(string? name)
    {
        if (name is null)
            return EntityKind.Monster;

        foreach (var kind in EntityKindNames.All)
        {
            if (EntityKindNames.Of(kind).Equals(name, StringComparison.OrdinalIgnoreCase)
                || kind.ToString().Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return kind;
            }
        }

        return EntityKind.Monster;
    }
}
