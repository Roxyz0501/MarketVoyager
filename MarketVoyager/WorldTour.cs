namespace MarketVoyager;

internal readonly record struct TourWorld(uint Id, string Name, uint DataCenter);

internal static class WorldTour
{
    public static bool Excluded(ShoppingList list, uint world, DateTimeOffset now) =>
        list.SkipRecentWorlds && list.WorldHistory.TryGetValue(world, out var visit) &&
        now - visit.LastCheckedUtc < TimeSpan.FromHours(6);

    public static IEnumerable<TourWorld> Order(IEnumerable<TourWorld> worlds, uint current, uint currentDc) =>
        worlds.OrderBy(w => w.DataCenter == currentDc ? 0 : 1).ThenBy(w => w.DataCenter)
            .ThenBy(w => w.Id == current ? 0 : 1).ThenBy(w => w.Id);

    public static void Record(ShoppingList list, uint world, DateTimeOffset now, bool purchased = false)
    {
        if (world == 0) return;
        if (!list.WorldHistory.TryGetValue(world, out var visit))
        {
            // One record per world, bounded even if old/custom config contains unknown worlds.
            while (list.WorldHistory.Count >= 512)
                list.WorldHistory.Remove(list.WorldHistory.MinBy(x => x.Value.LastCheckedUtc).Key);
            list.WorldHistory[world] = visit = new();
        }
        if (purchased) visit.LastPurchasedUtc = now.ToUniversalTime();
        else visit.LastCheckedUtc = now.ToUniversalTime();
    }

    public static void RecordPurchase(IEnumerable<ShoppingList> lists, Reservation pending, DateTimeOffset now)
    {
        var list = pending.ListId != Guid.Empty
            ? lists.FirstOrDefault(x => x.Id == pending.ListId)
            : lists.FirstOrDefault(x => x.Items.Any(r => r.Id == pending.RuleId));
        if (list != null) Record(list, pending.WorldId, now, true);
    }
}
