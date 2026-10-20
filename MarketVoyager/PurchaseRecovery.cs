namespace MarketVoyager;

internal static class PurchaseRecovery
{
    public static bool Resolve(BuyRule rule, Reservation pending, int currentCount, bool dialogOpen,
        bool confirmedNotPurchased, bool countUnresolved)
    {
        if (rule.Id != pending.RuleId || rule.ItemId != pending.ItemId) return false;
        var outcome = Rules.Reconcile(pending.InventoryBefore, currentCount, pending.Quantity);
        if (outcome == Rules.InventoryResult.Purchased)
        {
            Sales.Credit(rule, pending.CharacterId, pending.Hq, pending.Quantity);
            return true;
        }
        if (dialogOpen || outcome == Rules.InventoryResult.Inconsistent) return false;
        if (confirmedNotPurchased || pending.Submitted == false && pending.Cancelled) return true;
        if (!countUnresolved) return false;
        // A manual conservative count is not evidence of physical stock for automatic selling.
        rule.Purchased = checked(rule.Purchased + (int)pending.Quantity);
        return true;
    }
}
