using System.Numerics;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using Lumina.Excel.Sheets;
using NativeObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace MarketVoyager;

internal enum Phase { Idle, Travel, Walk, Open, Search, Select, Listings, Confirm, Result, SellWalk, SellOpen, SellPick, SellMenu, SellResult, Done, Stopped, Error }

internal sealed unsafe class Runner(Configuration config, Travel travel, NativeMarket market,
    NativeSales sales, IDataManager data, IObjectTable objects, IPlayerState player, IClientState client,
    IPluginLog log, System.Action save)
{
    private readonly Queue<(uint Id, string Name)> worlds = new();
    private readonly HashSet<ulong> attempted = [];
    private List<BuyRule> rules = [];
    private int index;
    private ShoppingList activeList = new();
    private ulong vendorId;
    private bool skipSaleUntilPurchase;
    private int slotsBeforeSale;
    private (uint Id, string Name) world;
    private ulong character;
    private ulong boardId;
    private bool ownsTravel;
    private bool ownsMovement;
    private Offer offer;
    private int retries;
    private int listingRetries;
    private bool waitingReconnect;
    private DateTime deadline, next;
    public Phase State { get; private set; }
    public bool Running => State is >= Phase.Travel and <= Phase.SellResult;
    public string CurrentWorld => world.Name ?? "";
    public uint CurrentItem => index < rules.Count ? rules[index].ItemId : 0;

    public void Start()
    {
        if (Running) return;
        try
        {
            if (config.Pending != null || config.PendingSale != null || !travel.Ready || travel.Busy || !client.IsLoggedIn || client.TerritoryType != 129 || player.ContentId == 0)
                throw new InvalidOperationException("Start requires Limsa Lower Decks, available dependencies and no unresolved purchase");
            if (market.Visible("SelectYesno") || market.Visible("InputNumeric") || market.Visible("ContextMenu") || market.Visible("Shop") || market.Visible("ItemSearch")) throw new InvalidOperationException("Close existing dialogs first");
            var aether = objects.Where(x => x.ObjectKind == ObjectKind.Aetheryte && x.BaseId == 8).FirstOrDefault();
            if (aether == null || objects.LocalPlayer == null || Vector3.Distance(objects.LocalPlayer.Position, aether.Position) > 25)
                throw new InvalidOperationException("Start beside the Limsa main aetheryte");
            activeList = config.Lists[config.Selected];
            skipSaleUntilPurchase = false;
            rules = activeList.Items.Where(x => x.Purchased < x.QuantityLimit).ToList();
            if (rules.Count == 0 || rules.Any(x => x.UnitLimit < 1 || x.QuantityLimit < 1 || x.Purchased < 0 || !data.GetExcelSheet<Item>().TryGetRow(x.ItemId, out var item) || item.ItemSearchCategory.RowId == 0))
                throw new InvalidOperationException("Invalid shopping list");
            foreach (var rule in rules)
                if (!data.GetExcelSheet<Item>().GetRow(rule.ItemId).CanBeHq) rule.Quality = Quality.Nq;
            for (int i = 0; i < rules.Count; i++)
                for (int j = i + 1; j < rules.Count; j++)
                    if (Rules.Overlaps(rules[i], rules[j], data.GetExcelSheet<Item>().GetRow(rules[i].ItemId).CanBeHq))
                        throw new InvalidOperationException("Overlapping item and quality rules");
            save();
            character = player.ContentId;
            worlds.Clear();
            var current = player.CurrentWorld.RowId;
            // Single bounded world-sheet pass per run; dependency advertises visitability.
            var region = player.CurrentWorld.Value.DataCenter.Value.Region;
            foreach (var w in data.GetExcelSheet<World>().Where(w => w.IsPublic && w.DataCenter.Value.Region.Equals(region))
                .OrderBy(w => w.RowId == current ? 0 : 1).ThenBy(w => w.RowId))
                if (w.RowId == current || travel.CanVisit(w.Name.ToString())) worlds.Enqueue((w.RowId, w.Name.ToString()));
            NextWorld();
        }
        catch (Exception ex) { Fail(ex); }
    }

    private void Set(Phase phase, int seconds = 30)
    {
        State = phase; deadline = DateTime.UtcNow.AddSeconds(seconds); next = DateTime.UtcNow.AddSeconds(1);
    }
    private void NextWorld()
    {
        if (rules.All(x => x.Purchased >= x.QuantityLimit) || !worlds.TryDequeue(out world)) { Stop(Phase.Done); return; }
        index = 0; attempted.Clear();
        market.CloseMarket();
        if (player.CurrentWorld.RowId != world.Id)
        {
            travel.ChangeWorld(world.Name); ownsTravel = true; Set(Phase.Travel, 600);
        }
        else BeginWalk();
    }
    private void BeginWalk()
    {
        ownsTravel = false;
        if (client.TerritoryType != 129) throw new InvalidOperationException("Travel did not return to Limsa");
        var aether = objects.FirstOrDefault(x => x.ObjectKind == ObjectKind.Aetheryte && x.BaseId == 8);
        if (aether == null) throw new InvalidOperationException("Limsa main aetheryte not loaded");
        // IDs verified against the installed EObjName sheet in all four game languages.
        var board = objects.Where(x => x.ObjectKind == ObjectKind.EventObj && x.BaseId is 2000402 or 2000440 or 2000442)
            .OrderBy(x => Vector3.DistanceSquared(x.Position, aether.Position)).FirstOrDefault();
        if (board == null || Vector3.Distance(board.Position, aether.Position) > 150 || !travel.MeshReady)
            throw new InvalidOperationException("Nearest market board or navmesh unavailable");
        boardId = board.GameObjectId;
        if (objects.LocalPlayer == null || !travel.Move(objects.LocalPlayer.Position, board.Position)) throw new InvalidOperationException("Path request rejected");
        ownsMovement = true; Set(Phase.Walk, 120);
    }
    public void Tick()
    {
        if (!Running || DateTime.UtcNow < next) return;
        next = DateTime.UtcNow.AddMilliseconds(250);
        try
        {
            if (config.Pending != null && !client.IsLoggedIn)
            {
                if (!waitingReconnect) { waitingReconnect = true; deadline = DateTime.UtcNow.AddSeconds(120); }
                if (DateTime.UtcNow > deadline) throw new TimeoutException("Reconnect timed out");
                return;
            }
            waitingReconnect = false;
            if (config.Pending != null && ReconcileInventory()) return;
            if (DateTime.UtcNow > deadline)
            {
                if (State is Phase.Search or Phase.Select or Phase.Listings && listingRetries < 3 && !market.Visible("SelectYesno"))
                {
                    listingRetries++;
                    market.CloseResults();
                    if (market.Visible("ItemSearch")) Set(Phase.Search); else BeginWalk();
                    next = DateTime.UtcNow.AddSeconds(3);
                    return;
                }
                if (State == Phase.Result && retries < 2 && !market.Visible("SelectYesno"))
                {
                    retries++;
                    market.CloseResults();
                    if (market.Visible("ItemSearch")) Set(Phase.Search); else BeginWalk();
                    next = DateTime.UtcNow.AddSeconds(3);
                    return;
                }
                throw new TimeoutException(State.ToString());
            }
            if (!travel.Ready) throw new InvalidOperationException("Dependency unloaded");
            if (State == Phase.Travel)
            {
                if (!travel.Busy && client.IsLoggedIn && player.ContentId == character && player.CurrentWorld.RowId == world.Id) BeginWalk();
                return;
            }
            if (!client.IsLoggedIn || player.ContentId != character || player.CurrentWorld.RowId != world.Id || client.TerritoryType != 129)
                throw new InvalidOperationException("Character, territory or world changed unexpectedly");
            if (State == Phase.Walk)
            {
                travel.PollMove();
                var board = objects.FirstOrDefault(x => x.GameObjectId == boardId);
                if (board == null || objects.LocalPlayer == null) throw new InvalidOperationException("Board disappeared");
                if (Vector3.Distance(objects.LocalPlayer.Position, board.Position) > 3) return;
                travel.StopMove(); ownsMovement = false;
                TargetSystem.Instance()->InteractWithObject((NativeObject*)board.Address);
                Set(Phase.Open); return;
            }
            if (State == Phase.Open)
            {
                if (market.Visible("ItemSearch")) NextItem(false);
                return;
            }
            if (State >= Phase.SellWalk) { TickSale(); return; }
            if (State == Phase.Listings && config.Pending == null && CheckSpace()) return;
            if (index >= rules.Count) throw new InvalidOperationException("Invalid item cursor");
            var rule = rules[index];
            var item = data.GetExcelSheet<Item>().GetRow(rule.ItemId);
            switch (State)
            {
                case Phase.Search:
                    if (market.Search(item.Name.ToString())) Set(Phase.Select);
                    break;
                case Phase.Select:
                    if (market.SelectItem(rule.ItemId)) Set(Phase.Listings);
                    break;
                case Phase.Listings:
                    if (!market.ListingsReady(rule.ItemId)) break;
                    if (config.Pending != null)
                    {
                        if (ReconcileInventory()) break;
                        if (!market.Read(rule.ItemId, character).Contains(offer))
                            throw new InvalidOperationException("Original listing unavailable; purchase outcome remains unresolved");
                        if (NativeSales.FreeSlots() == 0 && activeList.AutoSell)
                        {
                            // A fresh listing still exists and inventory is unchanged: no purchase was completed.
                            config.Pending = null; attempted.Remove(offer.Id); save();
                            BeginSale(); break;
                        }
                        if (!market.SelectOffer(offer, character)) throw new InvalidOperationException("Retry listing changed");
                        Set(Phase.Confirm, 15); break;
                    }
                    var options = market.Read(rule.ItemId, character).Where(x => !attempted.Contains(x.Id) && Rules.Eligible(rule, x, item.CanBeHq))
                        .OrderBy(x => x.Price).ThenBy(x => x.Total).ThenBy(x => x.Id).ToList();
                    if (options.Count == 0) { NextItem(true); break; }
                    offer = options[0];
                    if (InventoryManager.Instance() == null || offer.Total > InventoryManager.Instance()->GetGil())
                        throw new InvalidOperationException("Insufficient gil");
                    // Persist the reservation BEFORE any purchase interaction. Never blindly retry.
                    config.Pending = new() { RuleId = rule.Id, ListingId = offer.Id, ItemId = offer.Item,
                        Quantity = offer.Quantity, WorldId = world.Id, CharacterId = character, Hq = offer.Hq,
                        InventoryBefore = InventoryManager.Instance()->GetInventoryItemCount(offer.Item, offer.Hq, false, false) };
                    retries = 0;
                    save(); attempted.Add(offer.Id);
                    if (!market.SelectOffer(offer, character))
                    {
                        // SelectOffer returned before dispatching an event, so nothing was purchased.
                        config.Pending = null; save(); Set(Phase.Listings); break;
                    }
                    Set(Phase.Confirm, 15);
                    break;
                case Phase.Confirm:
                    if (!Rules.Eligible(rule, offer, item.CanBeHq)) throw new InvalidOperationException("Limit changed");
                    if (InventoryManager.Instance() == null || InventoryManager.Instance()->GetGil() < offer.Total)
                        throw new InvalidOperationException("Insufficient gil before confirmation");
                    // Mark Result before the click so a synchronous purchase event cannot be lost.
                    State = Phase.Result;
                    var action = market.Confirm(offer, item.Name.ToString());
                    if (action == 2) { if (State == Phase.Result) Set(Phase.Result, 20); }
                    else { State = Phase.Confirm; next = DateTime.UtcNow.AddSeconds(1); }
                    break;
            }
        }
        catch (Exception ex) { Fail(ex); }
    }
    public void Purchased(uint itemId, uint quantity)
    {
        if (State != Phase.Result || config.Pending == null || player.ContentId != character || player.CurrentWorld.RowId != world.Id ||
            itemId % 1_000_000 != offer.Item || quantity != offer.Quantity || !market.Matches(offer)) return;
        try
        {
            Sales.Credit(rules[index], character, offer.Hq, quantity);
            skipSaleUntilPurchase = false;
            config.Pending = null; save(); Set(Phase.Listings);
            next = DateTime.UtcNow.AddSeconds(2);
        }
        catch (Exception ex) { Fail(ex); }
    }
    private bool ReconcileInventory()
    {
        var pending = config.Pending;
        if (pending == null || player.ContentId != pending.CharacterId || InventoryManager.Instance() == null || !client.IsLoggedIn) return false;
        int count = InventoryManager.Instance()->GetInventoryItemCount(pending.ItemId, pending.Hq, false, false);
        var outcome = Rules.Reconcile(pending.InventoryBefore, count, pending.Quantity);
        if (outcome == Rules.InventoryResult.Unchanged) return false;
        if (outcome == Rules.InventoryResult.Inconsistent) throw new InvalidOperationException("Inventory changed unexpectedly; cannot reconcile purchase");
        Sales.Credit(rules[index], pending.CharacterId, pending.Hq, pending.Quantity);
        skipSaleUntilPurchase = false;
        config.Pending = null; save(); Set(market.Visible("ItemSearchResult") ? Phase.Listings : Phase.Search);
        next = DateTime.UtcNow.AddSeconds(2);
        return true;
    }
    private bool CheckSpace()
    {
        int free = NativeSales.FreeSlots();
        if (free < 0) throw new InvalidOperationException("Inventory not loaded");
        if (Sales.ShouldVisit(activeList.AutoSell, free, activeList.FreeSlotsThreshold) && !skipSaleUntilPurchase)
        { BeginSale(); return true; }
        if (free == 0) throw new InvalidOperationException("Inventory full and no automatic sale can free a slot");
        return false;
    }
    private void BeginSale()
    {
        if (config.Pending != null || config.PendingSale != null || market.Visible("SelectYesno") || market.Visible("InputNumeric"))
            throw new InvalidOperationException("Cannot sell while another transaction is pending");
        if (objects.LocalPlayer == null || !travel.MeshReady) throw new InvalidOperationException("Navigation unavailable");
        // Only ordinary, direct gil shops. Avoid quest, exchange and multi-menu NPCs.
        var npc = objects.Where(x => x.ObjectKind == ObjectKind.EventNpc && x.IsTargetable &&
            Vector3.Distance(x.Position, objects.LocalPlayer.Position) < 150)
            .Select(x => (Npc: x, Shops: data.GetExcelSheet<ENpcBase>().TryGetRow(x.BaseId, out var row)
                ? row.ENpcData.Select(d => d.RowId).Where(id => id != 0).ToArray() : []))
            .Where(x => x.Shops.Length == 1 && (x.Shops[0] >> 16) == 4)
            .OrderBy(x => Vector3.DistanceSquared(x.Npc.Position, objects.LocalPlayer.Position)).FirstOrDefault();
        if (npc.Npc == null) throw new InvalidOperationException("Nearby ordinary vendor unavailable");
        market.CloseMarket();
        vendorId = npc.Npc.GameObjectId; sales.ExpectShop(npc.Shops[0]);
        slotsBeforeSale = NativeSales.FreeSlots();
        if (!travel.Move(objects.LocalPlayer.Position, npc.Npc.Position)) throw new InvalidOperationException("Vendor path rejected");
        ownsMovement = true; Set(Phase.SellWalk, 120);
    }
    private void TickSale()
    {
        switch (State)
        {
            case Phase.SellWalk:
                travel.PollMove();
                var npc = objects.FirstOrDefault(x => x.GameObjectId == vendorId);
                if (npc == null || objects.LocalPlayer == null) throw new InvalidOperationException("Vendor disappeared");
                if (Vector3.Distance(objects.LocalPlayer.Position, npc.Position) > 3) return;
                travel.StopMove(); ownsMovement = false;
                TargetSystem.Instance()->InteractWithObject((NativeObject*)npc.Address);
                Set(Phase.SellOpen); break;
            case Phase.SellOpen:
                if (sales.Ready) Set(Phase.SellPick);
                break;
            case Phase.SellPick:
                if (config.PendingSale != null || market.Visible("SelectYesno") || market.Visible("InputNumeric") || market.Visible("ContextMenu")) throw new InvalidOperationException("Unresolved sale or unrelated dialog");
                var candidate = sales.Pick(activeList, character);
                if (candidate == null)
                {
                    sales.Close(); skipSaleUntilPurchase = true;
                    if (NativeSales.FreeSlots() <= slotsBeforeSale && slotsBeforeSale == 0)
                        throw new InvalidOperationException("No eligible acquired stack could free an inventory slot");
                    BeginWalk(); return;
                }
                config.PendingSale = candidate; save();
                sales.OpenMenu(candidate); Set(Phase.SellMenu, 15); break;
            case Phase.SellMenu:
                if (config.PendingSale == null) throw new InvalidOperationException("Missing sale reservation");
                if (sales.SelectSell(config.PendingSale)) Set(Phase.SellResult, 30);
                break;
            case Phase.SellResult:
                var pending = config.PendingSale ?? throw new InvalidOperationException("Missing sale reservation");
                var manager = InventoryManager.Instance();
                if (manager == null) throw new InvalidOperationException("Inventory unavailable");
                if (pending.ExpectedGil == 0 && sales.ReadPrice(pending)) save();
                var outcome = Sales.Reconcile(pending, manager->GetInventoryItemCount(pending.Item, pending.Hq, false, false), manager->GetGil());
                if (outcome == Sales.Outcome.Inconsistent) throw new InvalidOperationException("Sale result inconsistent");
                if (outcome == Sales.Outcome.Sold)
                {
                    var rule = activeList.Items.Single(x => x.Id == pending.RuleId);
                    Sales.Complete(rule, pending); config.PendingSale = null; save();
                    Set(Phase.SellPick); return;
                }
                if (sales.Confirm(pending, data.GetExcelSheet<Item>().GetRow(pending.Item).Name.ToString()))
                    next = DateTime.UtcNow.AddSeconds(1);
                break;
        }
    }
    private void NextItem(bool advance)
    {
        if (advance) index++;
        while (index < rules.Count && rules[index].Purchased >= rules[index].QuantityLimit) index++;
        if (index >= rules.Count) { NextWorld(); return; }
        listingRetries = 0;
        market.CloseResults(); Set(Phase.Search);
    }
    private void Fail(Exception ex) { log.Error(ex, "Market Voyager stopped after bounded same-listing retries"); Stop(Phase.Error); }
    public void Stop(Phase result = Phase.Stopped)
    {
        State = result;
        try { if (ownsMovement) travel.StopMove(); } catch (Exception ex) { log.Warning(ex, "Stop movement failed"); }
        try { if (ownsTravel) travel.AbortTravel(); } catch (Exception ex) { log.Warning(ex, "Stop travel failed"); }
        ownsMovement = ownsTravel = false;
    }
}

