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
}
