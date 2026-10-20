using System.Numerics;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using Lumina.Excel.Sheets;
using NativeObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace MarketVoyager;

internal enum Phase { Idle, WalkToAetheryte, Travel, Walk, Open, Search, Select, Listings, Confirm, Result, SellWalk, SellOpen, SellPick, SellMenu, SellResult, Done, Stopped, Error }

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
    private const float BoardInteractionDistance = 2f;
    private int boardOpenAttempts;
    private DateTime nextBoardInteraction;
    private uint sourceWorld;
    private bool evaluated;
    public int? OfferCount { get; private set; }
    public int CandidateCount { get; private set; }
    public uint EvaluatedItem { get; private set; }
    private bool ownsTravel;
    private bool ownsMovement;
    private Offer offer;
    private int retries;
    private int listingRetries;
    private bool waitingReconnect;
    private DateTime deadline, next;
    private readonly RunPause pause = new();
    private bool restartAfterResume;
    public bool PauseRequested => pause.Requested;
    public bool IsPaused => pause.Paused;
    public void TogglePause()
    {
        if (!Running) return;
        if (pause.Paused)
        {
            try
            {
                if (config.Pending != null || config.PendingSale != null || ownsMovement || ownsTravel)
                    throw new InvalidOperationException("Cannot apply paused edits during an unresolved action");
                LoadRules();
                index = 0; retries = listingRetries = 0; attempted.Clear();
                evaluated = false; OfferCount = null; CandidateCount = 0; EvaluatedItem = 0;
                skipSaleUntilPurchase = false;
                if (rules.Count == 0) { Stop(Phase.Done); return; }
                restartAfterResume = true;
            }
            catch (Exception ex) { Fail(ex); return; }
        }
        deadline += pause.Toggle(DateTime.UtcNow);
        if (restartAfterResume) deadline = DateTime.UtcNow.AddSeconds(State == Phase.WalkToAetheryte ? 120 : 30);
        next = DateTime.UtcNow;
    }
    public Phase State { get; private set; }
    public bool Running => State is >= Phase.WalkToAetheryte and <= Phase.SellResult;
    public string CurrentWorld => world.Name ?? "";
    public uint CurrentItem => index < rules.Count ? rules[index].ItemId : 0;

    public void Start()
    {
        if (Running) return;
        pause.Reset();
        OfferCount = null; CandidateCount = 0; EvaluatedItem = 0;
        try
        {
            if (config.Pending != null || config.PendingSale != null || !travel.Ready || travel.Busy || !client.IsLoggedIn || client.TerritoryType != 129 || player.ContentId == 0)
                throw new InvalidOperationException("Start requires Limsa Lower Decks, available dependencies and no unresolved purchase");
            if (market.Visible("SelectYesno") || market.Visible("InputNumeric") || market.Visible("ContextMenu") || market.Visible("Shop") || market.Visible("ItemSearch")) throw new InvalidOperationException("Close existing dialogs first");
            var aether = objects.Where(x => x.ObjectKind == ObjectKind.Aetheryte && x.BaseId == 8).FirstOrDefault();
            if (aether == null || objects.LocalPlayer == null || Vector3.Distance(objects.LocalPlayer.Position, aether.Position) > 25)
                throw new InvalidOperationException("Start beside the Limsa main aetheryte");
            skipSaleUntilPurchase = false;
            LoadRules();
            if (rules.Count == 0) throw new InvalidOperationException("Empty shopping list");
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

    private void LoadRules()
    {
        if (config.Selected < 0 || config.Selected >= config.Lists.Count) throw new InvalidOperationException("Invalid list selection");
        var selected = config.Lists[config.Selected];
        var updated = selected.Items.Where(x => x.Purchased < x.QuantityLimit).ToList();
        var sheet = data.GetExcelSheet<Item>();
        Dictionary<uint, bool> qualities = [];
        foreach (var rule in updated)
        {
            if (rule.UnitLimit < 1 || rule.QuantityLimit < 1 || rule.Purchased < 0 || !Enum.IsDefined(rule.Quality) ||
                !sheet.TryGetRow(rule.ItemId, out var item) || item.ItemSearchCategory.RowId == 0)
                throw new InvalidOperationException("Invalid shopping list");
            qualities[rule.ItemId] = item.CanBeHq;
        }
        foreach (var group in updated.GroupBy(x => x.ItemId))
        {
            var rows = group.ToArray();
            for (int i = 0; i < rows.Length; i++)
                for (int j = i + 1; j < rows.Length; j++)
                    if (Rules.Overlaps(rows[i], rows[j], qualities[group.Key]))
                        throw new InvalidOperationException("Overlapping item and quality rules");
        }
        bool normalized = false;
        foreach (var rule in updated)
            if (!qualities[rule.ItemId] && rule.Quality != Quality.Nq) { rule.Quality = Quality.Nq; normalized = true; }
        activeList = selected; rules = updated;
        if (normalized) save();
    }

    private void Set(Phase phase, int seconds = 30)
    {
        State = phase; deadline = DateTime.UtcNow.AddSeconds(seconds);
        next = DateTime.UtcNow.AddMilliseconds(phase is >= Phase.Search and <= Phase.Result ? config.PurchaseDelayMs : 1000);
    }
    private void NextWorld()
    {
        if (rules.All(x => x.Purchased >= x.QuantityLimit) || !worlds.TryDequeue(out world)) { Stop(Phase.Done); return; }
        index = 0; listingRetries = 0; attempted.Clear();
        market.CloseMarket();
        if (player.CurrentWorld.RowId != world.Id)
        {
            var aether = objects.FirstOrDefault(x => x.ObjectKind == ObjectKind.Aetheryte && x.BaseId == 8);
            if (client.TerritoryType != 129 || objects.LocalPlayer == null || aether == null || !travel.MeshReady)
                throw new InvalidOperationException("Limsa aetheryte walking route unavailable");
            sourceWorld = player.CurrentWorld.RowId;
            if (!travel.Move(objects.LocalPlayer.Position, aether.Position)) throw new InvalidOperationException("Aetheryte path rejected");
            ownsMovement = true; Set(Phase.WalkToAetheryte, 120);
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
        if (DateTime.UtcNow < next) return;
        if (!Running)
        {
            if (config.Pending == null) return;
            next = DateTime.UtcNow.AddSeconds(1);
            try { ResolveStoppedPurchase(false, false); }
            catch (Exception ex) { log.Warning(ex, "Stopped purchase reconciliation failed"); next = DateTime.UtcNow.AddSeconds(30); }
            return;
        }
        // Faster local observation while buying; UI readiness still gates every action.
        next = DateTime.UtcNow.AddMilliseconds(State is >= Phase.Search and <= Phase.Result ? config.PurchaseDelayMs : 250);
        try
        {
            if (pause.Enter(State is Phase.Search or Phase.Select or Phase.Listings or Phase.SellPick,
                config.Pending != null || config.PendingSale != null, ownsMovement || ownsTravel, DateTime.UtcNow)) return;
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
                    log.Warning("Market search retry {Retry}/3 at {Phase}, item {Item}: {Details}",
                        listingRetries, State, CurrentItem, market.Diagnostics());
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
                throw new TimeoutException($"{State}: {market.Diagnostics()}");
            }
            if (!travel.Ready) throw new InvalidOperationException("Dependency unloaded");
            if (restartAfterResume)
            {
                if (!client.IsLoggedIn || player.ContentId != character || client.TerritoryType != 129 ||
                    player.CurrentWorld.RowId != (State == Phase.WalkToAetheryte ? sourceWorld : world.Id))
                    throw new InvalidOperationException("Character or location changed while paused");
                restartAfterResume = false;
                if (State == Phase.WalkToAetheryte) { /* Keep the already queued next world. */ }
                else if (State == Phase.SellPick && activeList.AutoSell) { /* Resume selling against the edited list. */ }
                else
                {
                    if (State == Phase.SellPick) sales.Close();
                    market.CloseResults();
                    if (market.Visible("ItemSearch")) Set(Phase.Search); else BeginWalk();
                    return;
                }
            }
            if (State == Phase.WalkToAetheryte)
            {
                if (!client.IsLoggedIn || player.ContentId != character || player.CurrentWorld.RowId != sourceWorld || client.TerritoryType != 129)
                    throw new InvalidOperationException("Character or world changed while walking to aetheryte");
                var aether = objects.FirstOrDefault(x => x.ObjectKind == ObjectKind.Aetheryte && x.BaseId == 8 && x.IsTargetable);
                if (aether == null || objects.LocalPlayer == null) throw new InvalidOperationException("Aetheryte disappeared");
                if (ownsMovement) travel.PollMove();
                // Lifestream must recognize the main aetheryte before receiving any travel request.
                if (Vector3.Distance(objects.LocalPlayer.Position, aether.Position) > 12 || !travel.AtLimsaAetheryte) return;
                if (ownsMovement) { travel.StopMove(); ownsMovement = false; }
                if (pause.Enter(true, false, false, DateTime.UtcNow)) return;
                travel.ChangeWorld(world.Name); ownsTravel = true; Set(Phase.Travel, 600);
                return;
            }
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
                if (Vector3.Distance(objects.LocalPlayer.Position, board.Position) > BoardInteractionDistance) return;
                travel.StopMove(); ownsMovement = false;
                boardOpenAttempts = 0;
                nextBoardInteraction = DateTime.UtcNow.AddSeconds(1);
                Set(Phase.Open); return;
            }
            if (State == Phase.Open)
            {
                if (market.Visible("ItemSearch")) { NextItem(false); return; }
                // Let walking settle, then retry only this board at a bounded rate.
                // Keep the original Open deadline; an inaccessible board cannot loop forever.
                if (boardOpenAttempts >= 3 || DateTime.UtcNow < nextBoardInteraction) return;
                var board = objects.FirstOrDefault(x => x.GameObjectId == boardId);
                if (board == null || objects.LocalPlayer == null) throw new InvalidOperationException("Board disappeared before opening");
                if (!board.IsTargetable || Vector3.Distance(objects.LocalPlayer.Position, board.Position) > BoardInteractionDistance) return;
                boardOpenAttempts++;
                nextBoardInteraction = DateTime.UtcNow.AddSeconds(2);
                TargetSystem.Instance()->InteractWithObject((NativeObject*)board.Address);
                return;
            }
            if (State >= Phase.SellWalk) { TickSale(); return; }
            if (State == Phase.Result) return; // Inventory reconciliation above owns completion.
            if (State == Phase.Listings && market.Visible("SelectYesno")) return;
            if (State == Phase.Listings && config.Pending == null && CheckSpace()) return;
            if (index >= rules.Count) throw new InvalidOperationException("Invalid item cursor");
            var rule = rules[index];
            var item = data.GetExcelSheet<Item>().GetRow(rule.ItemId);
            switch (State)
            {
                case Phase.Search:
                    if (market.ResultsOpenFor(rule.ItemId)) Set(Phase.Listings);
                    else if (market.Search(item.Name.ToString())) Set(Phase.Select);
                    break;
                case Phase.Select:
                    if (market.ResultsOpenFor(rule.ItemId) || market.SelectItem(rule.ItemId)) Set(Phase.Listings);
                    break;
                case Phase.Listings:
                    if (!market.ListingsReady(rule.ItemId)) break;
                    if (config.Pending != null)
                    {
                        if (ReconcileInventory()) break;
                        if (!market.Read(rule.ItemId, character).Contains(offer))
                            throw new InvalidOperationException("Original listing unavailable; purchase outcome remains unresolved");
                        if (pause.Requested)
                        {
                            // Refreshed unchanged listing and inventory establish that this attempt did not complete.
                            // Pause before retrying the purchase.
                            config.Pending = null; attempted.Remove(offer.Id); save(); Set(Phase.Search); break;
                        }
                        if (NativeSales.FreeSlots() == 0 && activeList.AutoSell)
                        {
                            // A fresh listing still exists and inventory is unchanged: no purchase was completed.
                            config.Pending = null; attempted.Remove(offer.Id); save();
                            BeginSale(); break;
                        }
                        if (!market.SelectOffer(offer, character)) throw new InvalidOperationException("Retry listing changed");
                        Set(Phase.Confirm, 15); break;
                    }
                    var available = market.Read(rule.ItemId, character);
                    var options = available.Where(x => !attempted.Contains(x.Id) && Rules.Eligible(rule, x, item.CanBeHq))
                        .OrderBy(x => x.Price).ThenBy(x => x.Total).ThenBy(x => x.Id).ToList();
                    OfferCount = available.Count; CandidateCount = options.Count; EvaluatedItem = rule.ItemId;
                    if (!evaluated)
                    {
                        evaluated = true;
                        log.Information("Market eligibility item {Item}: {Offers} offers, {Candidates} candidates; cap {Cap}, tax {Tax}, quality {Quality}, remaining {Remaining}",
                            rule.ItemId, available.Count, options.Count, rule.UnitLimit, rule.IncludeTax, rule.Quality, (long)rule.QuantityLimit - rule.Purchased);
                    }
                    if (options.Count == 0) { NextItem(true); break; }
                    offer = options[0];
                    if (InventoryManager.Instance() == null || offer.Total > InventoryManager.Instance()->GetGil())
                        throw new InvalidOperationException("Insufficient gil");
                    // Persist the reservation BEFORE any purchase interaction. Never blindly retry.
                    config.Pending = new() { RuleId = rule.Id, ListingId = offer.Id, ItemId = offer.Item,
                        Quantity = offer.Quantity, WorldId = world.Id, CharacterId = character, Hq = offer.Hq,
                        InventoryBefore = InventoryManager.Instance()->GetInventoryItemCount(offer.Item, offer.Hq, false, false), Submitted = false };
                    retries = 0;
                    if (!market.SelectOffer(offer, character, save))
                    {
                        // SelectOffer returned before dispatching an event, so nothing was purchased.
                        config.Pending = null; next = DateTime.UtcNow.AddSeconds(1); break;
                    }
                    attempted.Add(offer.Id);
                    Set(Phase.Confirm, 15);
                    break;
                case Phase.Confirm:
                    if (!Rules.Eligible(rule, offer, item.CanBeHq)) throw new InvalidOperationException("Limit changed");
                    if (InventoryManager.Instance() == null || InventoryManager.Instance()->GetGil() < offer.Total)
                        throw new InvalidOperationException("Insufficient gil before confirmation");
                    // Mark Result before the click so a synchronous purchase event cannot be lost.
                    State = Phase.Result;
                    var action = market.Confirm(offer, item.Name.ToString(), () => {
                        if (config.Pending == null) throw new InvalidOperationException("Missing purchase reservation");
                        config.Pending.Submitted = true; save();
                    });
                    if (action == 2) { if (State == Phase.Result) Set(Phase.Result, 20); }
                    else { State = Phase.Confirm; next = DateTime.UtcNow.AddMilliseconds(action == 1 ? Math.Max(250, config.PurchaseDelayMs) : config.PurchaseDelayMs); }
                    break;
            }
        }
        catch (Exception ex) { Fail(ex); }
    }
    public void Purchased(uint itemId, uint quantity)
    {
        if (State != Phase.Result || config.Pending == null || player.ContentId != character || player.CurrentWorld.RowId != world.Id ||
            itemId % 1_000_000 != offer.Item || quantity != offer.Quantity) return;
        // A receipt can arrive before the inventory update. Wake the existing reconciliation
        // instead of taking the next purchase's baseline from stale inventory.
        next = DateTime.UtcNow;
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
        return true;
    }
    public bool ResolveStoppedPurchase(bool confirmedNotPurchased, bool countUnresolved)
    {
        var pending = config.Pending;
        if (Running || pending == null || !client.IsLoggedIn || player.ContentId != pending.CharacterId ||
            InventoryManager.Instance() == null) return false;
        var rule = config.FindRule(pending.RuleId);
        if (rule == null) return false;
        int count = InventoryManager.Instance()->GetInventoryItemCount(pending.ItemId, pending.Hq, false, false);
        if (!PurchaseRecovery.Resolve(rule, pending, count, market.Visible("SelectYesno"), confirmedNotPurchased, countUnresolved)) return false;
        config.Pending = null; config.PruneDetached(); save(); return true;
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
        if (advance) listingRetries = 0;
        evaluated = false;
        market.CloseResults(); Set(Phase.Search);
    }
    private void Fail(Exception ex) { log.Error(ex, "Market Voyager stopped after bounded same-listing retries"); Stop(Phase.Error); }
    public void Stop(Phase result = Phase.Stopped)
    {
        pause.Reset();
        restartAfterResume = false;
        bool cancelUnsent = Running && config.Pending is { Submitted: false } && State == Phase.Confirm;
        State = result;
        if (cancelUnsent)
        {
            try
            {
                if (market.CancelConfirmation(offer) && config.Pending is { } pending)
                { pending.Cancelled = true; save(); }
            }
            catch (Exception ex) { log.Warning(ex, "Could not cancel unsubmitted purchase dialog"); }
        }
        try { if (ownsMovement) travel.StopMove(); } catch (Exception ex) { log.Warning(ex, "Stop movement failed"); }
        try { if (ownsTravel) travel.AbortTravel(); } catch (Exception ex) { log.Warning(ex, "Stop travel failed"); }
        ownsMovement = ownsTravel = false;
    }
}

