namespace MarketVoyager;

public enum Quality { Nq, Hq, Any }

public sealed class BuyRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public uint ItemId { get; set; }
    public int UnitLimit { get; set; } = 100;
    public bool IncludeTax { get; set; }
    public int QuantityLimit { get; set; } = 1;
    public Quality Quality { get; set; }
    public int Purchased { get; set; }
    public List<AcquiredStock> Stock { get; set; } = [];
}

public sealed class ShoppingList
{
    public string Name { get; set; } = "List 1";
    public List<BuyRule> Items { get; set; } = [];
    public bool AutoSell { get; set; }
    public int FreeSlotsThreshold { get; set; } = 5;
}

public readonly record struct Offer(ulong Id, uint Item, uint Price, uint Quantity, uint Tax, bool Hq)
{
    public ulong Total => (ulong)Price * Quantity + Tax;
}

public sealed class Reservation
{
    public Guid RuleId { get; set; }
    public ulong ListingId { get; set; }
    public uint ItemId { get; set; }
    public uint Quantity { get; set; }
    public uint WorldId { get; set; }
    public ulong CharacterId { get; set; }
    public bool Hq { get; set; }
    public int InventoryBefore { get; set; }
    // null preserves the uncertain submission state of reservations saved by older versions.
    public bool? Submitted { get; set; }
    public bool Cancelled { get; set; }
}

public static class Rules
{
    public static string ResolveLanguage(string? saved, string? game, string? dalamud) =>
        Normalize(saved) ?? Normalize(game) ?? Normalize(dalamud) ?? "en";
    public enum InventoryResult { Unchanged, Purchased, Inconsistent }
    public static InventoryResult Reconcile(int before, int current, uint quantity)
    {
        if (before < 0 || current < 0 || quantity == 0) return InventoryResult.Inconsistent;
        long delta = (long)current - before;
        return delta == quantity ? InventoryResult.Purchased : delta == 0 ? InventoryResult.Unchanged : InventoryResult.Inconsistent;
    }
    // Remaining quantity counts units, not listings. A market stack cannot be split.
    public static bool Eligible(BuyRule rule, Offer offer, bool canHq) =>
        rule.ItemId != 0 && Enum.IsDefined(rule.Quality) && rule.UnitLimit > 0 && rule.QuantityLimit > 0 && rule.Purchased >= 0 &&
        offer.Id != 0 && offer.Item == rule.ItemId &&
        (rule.IncludeTax ? offer.Total <= (ulong)rule.UnitLimit * offer.Quantity : offer.Price <= (uint)rule.UnitLimit) &&
        offer.Quantity > 0 && offer.Quantity <= (long)rule.QuantityLimit - rule.Purchased &&
        (!canHq ? !offer.Hq : rule.Quality == Quality.Any || offer.Hq == (rule.Quality == Quality.Hq));

    public static bool Overlaps(BuyRule left, BuyRule right, bool canHq) => left.ItemId == right.ItemId &&
        (!canHq || left.Quality == Quality.Any || right.Quality == Quality.Any || left.Quality == right.Quality);

    public static int? NpcLimit(uint baseSellPrice, int margin) => margin >= 1 && baseSellPrice > margin &&
        (long)baseSellPrice - margin <= int.MaxValue ? (int)(baseSellPrice - margin) : null;

    public static string? Normalize(string? code)
    {
        var value = code?.Replace('_', '-').ToLowerInvariant();
        if (value is null) return null;
        if (value == "zh-hans" || value.StartsWith("zh-hans-") || value is "zh-cn" or "zh-sg") return "zh-Hans";
        if (value == "zh-hant" || value.StartsWith("zh-hant-") || value is "zh-tw" or "zh-hk" or "zh-mo") return "zh-Hant";
        var root = value.Split('-')[0];
        return root is "ja" or "en" or "de" or "fr" or "ko" ? root : null;
    }
}
