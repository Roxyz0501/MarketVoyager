namespace MarketVoyager;

public sealed class AcquiredStock
{
    public ulong Character { get; set; }
    public bool Hq { get; set; }
    public int Remaining { get; set; }
}

public sealed class SaleReservation
{
    public Guid RuleId { get; set; }
    public ulong Character { get; set; }
    public uint Item { get; set; }
    public bool Hq { get; set; }
    public int Quantity { get; set; }
    public int Inventory { get; set; }
    public int Slot { get; set; }
    public int Before { get; set; }
    public uint GilBefore { get; set; }
    public ulong ExpectedGil { get; set; }
}

public static class Sales
{
    public enum Outcome { Waiting, Sold, Inconsistent }
    public static bool ShouldVisit(bool enabled, int freeSlots, int threshold) =>
        enabled && freeSlots >= 0 && freeSlots <= Math.Clamp(threshold, 0, 139);
    public static void Credit(BuyRule rule, ulong character, bool hq, uint quantity)
    {
        if (character == 0 || quantity == 0 || quantity > int.MaxValue) throw new InvalidOperationException("Invalid receipt");
        var stock = rule.Stock.SingleOrDefault(x => x.Character == character && x.Hq == hq);
        int purchased = checked(rule.Purchased + (int)quantity);
        int remaining = checked((stock?.Remaining ?? 0) + (int)quantity);
        if (stock == null) { stock = new() { Character = character, Hq = hq }; rule.Stock.Add(stock); }
        rule.Purchased = purchased; stock.Remaining = remaining;
    }
    public static Outcome Reconcile(SaleReservation sale, int current, uint gil)
    {
        if (sale.Quantity <= 0 || sale.Before < sale.Quantity || current < 0) return Outcome.Inconsistent;
        long removed = (long)sale.Before - current;
        long proceeds = (long)gil - sale.GilBefore;
        if (removed == 0 && proceeds == 0) return Outcome.Waiting;
        // Inventory and wallet updates may arrive in different frames. Neither permits another sale.
        if ((removed == 0 || removed == sale.Quantity) && (proceeds == 0 || (ulong)Math.Max(0, proceeds) == sale.ExpectedGil) && proceeds >= 0)
            return removed == sale.Quantity && sale.ExpectedGil > 0 && (ulong)proceeds == sale.ExpectedGil ? Outcome.Sold : Outcome.Waiting;
        return Outcome.Inconsistent;
    }
    public static void Complete(BuyRule rule, SaleReservation sale)
    {
        var stock = rule.Stock.SingleOrDefault(x => x.Character == sale.Character && x.Hq == sale.Hq);
        if (rule.Id != sale.RuleId || rule.ItemId != sale.Item || stock == null || sale.Quantity <= 0 || stock.Remaining < sale.Quantity)
            throw new InvalidOperationException("Sale ledger mismatch");
        stock.Remaining -= sale.Quantity;
        rule.Stock.RemoveAll(x => x.Remaining == 0);
        // Purchased is cumulative and deliberately independent of physical inventory and sales.
    }
}
