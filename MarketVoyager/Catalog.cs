namespace MarketVoyager;

public sealed record CatalogItem(uint Id, string Name, uint CategoryId, string CategoryName, uint NpcPrice, uint ItemLevel, bool CanHq);
public enum CatalogSort { NpcDescending, NpcAscending, Name, Category, LevelDescending, Id }

public sealed record BatchOptions(int Price, int Quantity, Quality Quality, bool IncludeTax, bool NpcMode, int Margin,
    bool SetPrice = true, bool SetQuantity = true, bool SetQuality = true);
public readonly record struct BatchResult(int Changed, int Skipped, string? Error = null);

public static class Catalog
{
    public const int ListCapacity = 1000;
    public static CatalogItem[] Filter(IEnumerable<CatalogItem> items, string query, uint category, int minNpc, CatalogSort sort)
    {
        var filtered = items.Where(x => (category == 0 || x.CategoryId == category) && x.NpcPrice >= Math.Max(0, minNpc) &&
            (string.IsNullOrWhiteSpace(query) || x.Name.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase) || x.Id.ToString() == query.Trim()));
        var ordered = sort switch
        {
            CatalogSort.NpcDescending => filtered.OrderByDescending(x => x.NpcPrice),
            CatalogSort.NpcAscending => filtered.OrderBy(x => x.NpcPrice),
            CatalogSort.Name => filtered.OrderBy(x => x.Name, StringComparer.CurrentCulture),
            CatalogSort.Category => filtered.OrderBy(x => x.CategoryName, StringComparer.CurrentCulture),
            CatalogSort.LevelDescending => filtered.OrderByDescending(x => x.ItemLevel),
            _ => filtered.OrderBy(x => x.Id)
        };
        return ordered.ThenBy(x => x.Id).ToArray();
    }
    private static BuyRule? Make(CatalogItem item, BatchOptions options, BuyRule? source = null)
    {
        var rule = new BuyRule { Id = source?.Id ?? Guid.NewGuid(), ItemId = item.Id,
            UnitLimit = source?.UnitLimit ?? options.Price, IncludeTax = source?.IncludeTax ?? options.IncludeTax,
            QuantityLimit = source?.QuantityLimit ?? options.Quantity, Quality = source?.Quality ?? options.Quality,
            Purchased = source?.Purchased ?? 0 };
        if (options.SetPrice)
        {
            int? limit = options.NpcMode ? Rules.NpcLimit(item.NpcPrice, options.Margin) : options.Price;
            if (limit is not > 0) return null;
            rule.UnitLimit = limit.Value; rule.IncludeTax = options.NpcMode || options.IncludeTax;
        }
        if (options.SetQuantity) rule.QuantityLimit = options.Quantity;
        if (options.SetQuality) rule.Quality = options.Quality;
        if (!item.CanHq) rule.Quality = Quality.Nq;
        return rule.QuantityLimit > 0 && Enum.IsDefined(rule.Quality) ? rule : null;
    }
    public static BatchResult Add(ShoppingList list, IEnumerable<CatalogItem> items, BatchOptions options)
    {
        List<BuyRule> additions = []; int skipped = 0;
        foreach (var item in items.DistinctBy(x => x.Id))
        {
            var rule = Make(item, options);
            if (rule == null || list.Items.Concat(additions).Any(x => Rules.Overlaps(x, rule, item.CanHq))) { skipped++; continue; }
            additions.Add(rule);
            if (list.Items.Count + additions.Count > ListCapacity) return new(0, 0, "Capacity");
        }
        list.Items.AddRange(additions); return new(additions.Count, skipped);
    }
    public static BatchResult Edit(ShoppingList list, IReadOnlySet<Guid> targets, IReadOnlyDictionary<uint, CatalogItem> catalogue, BatchOptions options)
    {
        var replacements = new Dictionary<Guid, BuyRule>();
        foreach (var old in list.Items.Where(x => targets.Contains(x.Id)))
        {
            if (!catalogue.TryGetValue(old.ItemId, out var item)) return new(0, 0, "InvalidBatch");
            var updated = Make(item, options, old);
            if (updated == null) return new(0, 0, "InvalidBatch");
            replacements.Add(old.Id, updated);
        }
        var preview = list.Items.Select(x => replacements.GetValueOrDefault(x.Id, x)).ToList();
        foreach (var group in preview.GroupBy(x => x.ItemId))
        {
            var same = group.ToArray();
            for (int i = 0; i < same.Length; i++) for (int j = i + 1; j < same.Length; j++)
                if (Rules.Overlaps(same[i], same[j], catalogue.TryGetValue(group.Key, out var item) && item.CanHq)) return new(0, 0, "Overlap");
        }
        int changed = 0;
        foreach (var old in list.Items)
            if (replacements.TryGetValue(old.Id, out var updated) &&
                (old.UnitLimit != updated.UnitLimit || old.IncludeTax != updated.IncludeTax || old.QuantityLimit != updated.QuantityLimit || old.Quality != updated.Quality))
            {
                old.UnitLimit = updated.UnitLimit; old.IncludeTax = updated.IncludeTax;
                old.QuantityLimit = updated.QuantityLimit; old.Quality = updated.Quality; changed++;
            }
        return new(changed, 0);
    }
    public static int Reset(ShoppingList list, IReadOnlySet<Guid> targets)
    {
        int changed = 0;
        foreach (var row in list.Items.Where(x => targets.Contains(x.Id) && x.Purchased != 0)) { row.Purchased = 0; changed++; }
        return changed;
    }
}
