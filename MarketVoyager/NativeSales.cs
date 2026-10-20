using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;

namespace MarketVoyager;

internal sealed unsafe class NativeSales(IGameGui gui, IDataManager data)
{
    private static readonly InventoryType[] Bags = [InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4];
    private nint shopAddress;
    private uint shopId;
    private bool numericSubmitted, yesSubmitted;
    public void ExpectShop(uint id) { shopId = id; shopAddress = 0; }
    private AtkUnitBase* Shop => gui.GetAddonByName<AtkUnitBase>("Shop");
    private ShopEventHandler* Handler
    {
        get
        {
            var a = Shop;
            var proxy = ShopEventHandler.AgentProxy.Instance();
            if (a == null || !a->IsVisible || !a->IsReady || proxy == null || proxy->AddonId != a->Id || proxy->Handler == null) return null;
            var h = proxy->Handler;
            if (h->EventHandler.Info.EventId.Id != shopId || h->IsTradingWithRetainer || h->CurrentMode != 1) return null;
            if (shopAddress != 0 && shopAddress != (nint)a) return null;
            return h;
        }
    }
    public bool Ready { get { if (Handler == null) return false; shopAddress = (nint)Shop; return true; } }
    public void Close() { if (Shop != null && (nint)Shop == shopAddress) Shop->Close(true); }
    public static int FreeSlots()
    {
        var manager = InventoryManager.Instance(); if (manager == null) return -1;
        int free = 0;
        foreach (var type in Bags)
        {
            var bag = manager->GetInventoryContainer(type);
            if (bag == null || !bag->IsLoaded || bag->Items == null || bag->Size != 35) return -1;
            for (int i = 0; i < bag->Size; i++)
            {
                var slot = bag->GetInventorySlot(i);
                if (slot == null || slot->IsSymbolic) return -1;
                if (slot->ItemId == 0) free++;
            }
        }
        return free;
    }
    public SaleReservation? Pick(ShoppingList list, ulong character)
    {
        if (!Ready || FreeSlots() < 0) return null;
        var manager = InventoryManager.Instance();
        foreach (var rule in list.Items)
        {
            if (!data.GetExcelSheet<Item>().TryGetRow(rule.ItemId, out var row) || row.PriceLow == 0) continue;
            foreach (var stock in rule.Stock.Where(x => x.Character == character && x.Remaining > 0))
            {
                int count = manager->GetInventoryItemCount(rule.ItemId, stock.Hq, false, false);
                if (count < stock.Remaining) throw new InvalidOperationException("Acquired inventory changed outside this tour");
                foreach (var type in Bags)
                {
                    var bag = manager->GetInventoryContainer(type);
                    for (int i = 0; i < bag->Size; i++)
                    {
                        var slot = bag->GetInventorySlot(i);
                        if (!SafeSlot(slot, rule.ItemId, stock.Hq) || slot->Quantity > stock.Remaining) continue;
                        if ((ulong)manager->GetGil() + (ulong)row.PriceLow * (uint)slot->Quantity * 2 > 999_999_999)
                            throw new InvalidOperationException("Insufficient wallet capacity for automatic sale");
                        // Only a fully accounted stack can be sold. Never sell pre-existing excess in a merged stack.
                        return new() { RuleId = rule.Id, Character = character, Item = rule.ItemId, Hq = stock.Hq,
                            Quantity = slot->Quantity, Inventory = (int)type, Slot = i, Before = count, GilBefore = manager->GetGil() };
                    }
                }
            }
        }
        return null;
    }
    private static bool SafeSlot(InventoryItem* slot, uint item, bool hq)
    {
        if (slot == null || slot->IsSymbolic || slot->ItemId != item || slot->Quantity <= 0 ||
            slot->Flags != (hq ? InventoryItem.ItemFlags.HighQuality : InventoryItem.ItemFlags.None) ||
            slot->SpiritbondOrCollectability != 0 || slot->GlamourId != 0) return false;
        for (int i = 0; i < 5; i++) if (slot->Materia[i] != 0) return false;
        return true;
    }
    private void Validate(SaleReservation sale)
    {
        var manager = InventoryManager.Instance();
        if (!Ready || manager == null || !Bags.Contains((InventoryType)sale.Inventory)) throw new InvalidOperationException("Shop closed");
        var bag = manager->GetInventoryContainer((InventoryType)sale.Inventory);
        if (bag == null || !bag->IsLoaded || sale.Slot < 0 || sale.Slot >= bag->Size) throw new InvalidOperationException("Inventory unavailable");
        var slot = bag->GetInventorySlot(sale.Slot);
        if (!SafeSlot(slot, sale.Item, sale.Hq) || slot->Quantity != sale.Quantity) throw new InvalidOperationException("Sale slot changed");
    }
    public void OpenMenu(SaleReservation sale)
    {
        Validate(sale);
        numericSubmitted = yesSubmitted = false;
        AgentInventoryContext.Instance()->OpenForItemSlot((InventoryType)sale.Inventory, sale.Slot, 0, Shop->Id);
    }
    public bool SelectSell(SaleReservation sale)
    {
        Validate(sale);
        var menu = gui.GetAddonByName<AddonContextMenu>("ContextMenu");
        var context = AgentInventoryContext.Instance();
        if (menu == null || !menu->IsReady || !menu->IsVisible) return false;
        if (context == null || context->TargetInventoryId != (InventoryType)sale.Inventory || context->TargetInventorySlotId != sale.Slot || context->OwnerAddonId != Shop->Id)
            throw new InvalidOperationException("Unrelated item context menu");
        if (menu->AtkValuesCount < 9) return false;
        int count = (int)menu->AtkValues[0].UInt;
        if (count is < 1 or > 32 || menu->AtkValuesCount < 8 + count) return false;
        var label = data.GetExcelSheet<Addon>().GetRow(93).Text.ExtractText();
        for (int i = 0; i < count; i++)
        {
            var value = &menu->AtkValues[8 + i];
            if (value->Type != AtkValueType.ManagedString || value->String.Value == null || context->IsContextItemDisabled(i)) continue;
            if (new ReadOnlySeStringSpan(value->String.Value).ExtractText() != label) continue;
            menu->OnMenuSelected(i, 0); return true;
        }
        throw new InvalidOperationException("Ordinary shop Sell action unavailable");
    }
    public bool ReadPrice(SaleReservation sale)
    {
        var h = Handler;
        if (h == null || h->TransactionType != 2 || h->TransactionItemId % 1_000_000 != sale.Item ||
            h->SellInventorySlot != sale.Slot || (int)h->SellInventoryType != sale.Inventory || h->SellPrice <= 0) return false;
        sale.ExpectedGil = (ulong)h->SellPrice * (uint)sale.Quantity;
        if (sale.GilBefore + sale.ExpectedGil > 999_999_999) throw new InvalidOperationException("Sale would exceed gil capacity");
        return true;
    }
    public bool Confirm(SaleReservation sale, string itemName)
    {
        if (!ReadPrice(sale)) return false;
        var h = Handler;
        var numeric = gui.GetAddonByName<AddonInputNumeric>("InputNumeric");
        if (numeric != null && numeric->IsReady && numeric->IsVisible && !numericSubmitted)
        {
            Validate(sale);
            if (numeric->NumericInput == null || numeric->NumericInput->Data.Max < sale.Quantity) return false;
            numeric->NumericInput->SetValue(sale.Quantity);
            numericSubmitted = NativeMarket.Click(&numeric->AtkUnitBase, numeric->OkButton);
            return numericSubmitted;
        }
        var yesno = gui.GetAddonByName<AddonSelectYesno>("SelectYesno");
        if (yesno != null && yesno->IsReady && yesno->IsVisible && !yesSubmitted && h->WaitingForSellConfirm &&
            h->TransactionItemCount == sale.Quantity && yesno->PromptText != null &&
            yesno->PromptText->NodeText.ToString().Contains(itemName, StringComparison.Ordinal))
        {
            Validate(sale);
            yesSubmitted = NativeMarket.Click(&yesno->AtkUnitBase, yesno->YesButton);
            return yesSubmitted;
        }
        return false;
    }
}
