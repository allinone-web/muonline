using Client.AssetStudio.Catalog;

namespace Client.AssetStudio.Cli;

/// <summary>道具分類盤點：每個群組有幾筆定義、對到幾個模型、有幾個模型沒人用。</summary>
public static class ItemReport
{
    public static int Print(EntityCatalog catalog, ItemCatalog items, string? filter)
    {
        if (items.Error is string error)
        {
            Console.Error.WriteLine(error);
            return 2;
        }

        var models = catalog.OfKind(EntityKind.Item);

        Console.WriteLine();
        Console.WriteLine($"item.bmd 有 {items.BoundItems} 筆帶模型的道具定義，Data/Item 有 {models.Length} 個模型");
        Console.WriteLine();
        Console.WriteLine("群組                定義數  對到的模型");

        foreach (var (group, name) in Enumerable.Range(0, 16)
                     .Select(g => ((byte)g, ItemCatalog.GroupName((byte)g))))
        {
            var bound = models.Where(m => items.For(m.ModelPath).Any(b => b.Group == group)).ToArray();
            int definitions = models.Sum(m => items.For(m.ModelPath).Count(b => b.Group == group));

            if (definitions == 0 && bound.Length == 0)
                continue;

            Console.WriteLine($"{name,-18}{definitions,8}{bound.Length,12}");
        }

        int unbound = models.Count(m => items.For(m.ModelPath).Count == 0);
        Console.WriteLine();
        Console.WriteLine($"沒有對到任何道具定義的模型：{unbound} / {models.Length}");

        if (filter is null)
            return 0;

        Console.WriteLine();
        Console.WriteLine("模型                                群組              道具");

        foreach (var model in models)
        {
            var bindings = items.For(model.ModelPath);
            if (bindings.Count == 0)
                continue;

            if (!bindings.Any(b => b.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                && !model.ModelPath.Contains(filter, StringComparison.OrdinalIgnoreCase)
                && !bindings[0].GroupName.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Console.WriteLine($"{model.ModelPath,-36}{bindings[0].GroupName,-18}"
                            + string.Join("、", bindings.Take(4).Select(b => $"{b.Name}({b.Number})")));
        }

        return 0;
    }
}
