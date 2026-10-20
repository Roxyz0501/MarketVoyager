using Dalamud.Configuration;
namespace MarketVoyager;
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public string? Language { get; set; }
    public List<ShoppingList> Lists { get; set; } = [new()];
    public int Selected { get; set; }
    public Reservation? Pending { get; set; }
    public SaleReservation? PendingSale { get; set; }
    // Only unresolved deleted rows remain here; they are never purchase/sale targets.
    public List<BuyRule> DetachedRules { get; set; } = [];
    public BuyRule? FindRule(Guid id) => Lists.SelectMany(x => x.Items).Concat(DetachedRules).SingleOrDefault(x => x.Id == id);
    public bool HasPending(Guid id) => Pending?.RuleId == id || PendingSale?.RuleId == id;
    public void PruneDetached() => DetachedRules.RemoveAll(x => !HasPending(x.Id));
    public int RemoveRules(ShoppingList list, IReadOnlySet<Guid> ids)
    {
        PruneDetached();
        foreach (var rule in list.Items.Where(x => ids.Contains(x.Id) && HasPending(x.Id)))
            if (DetachedRules.All(x => x.Id != rule.Id)) DetachedRules.Add(rule);
        return list.Items.RemoveAll(x => ids.Contains(x.Id));
    }
}
