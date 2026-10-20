using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace MarketVoyager;

internal sealed unsafe class NativeMarket(IGameGui gui)
{
    public bool Visible(string name) { var a = gui.GetAddonByName<AtkUnitBase>(name); return a != null && a->IsVisible && a->IsReady; }
    private InfoProxyItemSearch* Proxy => InfoModule.Instance() == null ? null : InfoModule.Instance()->GetInfoProxyItemSearch();
    public void CloseResults() { var a = gui.GetAddonByName<AtkUnitBase>("ItemSearchResult"); if (a != null && a->IsVisible) a->Close(true); }
    public void CloseMarket() { CloseResults(); var a = gui.GetAddonByName<AtkUnitBase>("ItemSearch"); if (a != null && a->IsVisible) a->Close(true); }
    public string Diagnostics()
    {
        var a = gui.GetAddonByName<AddonItemSearch>("ItemSearch");
        var result = gui.GetAddonByName<AddonItemSearchResult>("ItemSearchResult");
        var proxy = Proxy;
        return $"searchVisible={Visible("ItemSearch")}, mode={(a == null ? -1 : (int)a->Mode)}, " +
            $"inputPresent={a != null && a->SearchTextInput != null}, " +
            $"inputHasText={a != null && a->SearchTextInput != null && a->SearchTextInput->EvaluatedString.StringLength > 0}, " +
            $"resultVisible={Visible("ItemSearchResult")}, rows={(result == null || result->Results == null ? -1 : result->Results->ListLength)}, " +
            $"listingCount={(proxy == null ? 0 : proxy->ListingCount)}, waiting={proxy != null && proxy->WaitingForListings}";
    }

    // The input widget and its actual registered button event are used; no request packet API.
    public bool Search(string name)
    {
        var a = gui.GetAddonByName<AddonItemSearch>("ItemSearch");
        if (!CanEnterSearch(a)) return false;
        // Mode describes the previous result/category, not whether the name input is usable.
        // The normal search button owns the transition into name-search mode.
        if (a->SearchTextInput->EvaluatedString.ToString() != name)
        {
            a->SearchTextInput->SetText(name);
            return false; // Let the ordinary input-change callback update the button first.
        }
        return Click(&a->AtkUnitBase, a->SearchButton);
    }
    internal static bool CanEnterSearch(AddonItemSearch* a) =>
        a != null && a->IsVisible && a->IsReady && a->SearchTextInput != null;
    public bool SelectItem(uint item)
    {
        var a = gui.GetAddonByName<AddonItemSearch>("ItemSearch");
        var agent = AgentItemSearch.Instance();
        if (a == null || !Visible("ItemSearch") || a->ResultsList == null || agent == null || !agent->ListingPageLoaded) return false;
        for (int i = 0; i < Math.Min(agent->ListingPageItemCount, 100); i++)
            if (agent->ListingPageItems[i].ItemId == item && i < a->ResultsList->GetItemCount())
            {
                a->ResultsList->DispatchItemEvent(i, AtkEventType.ListItemClick);
                return true;
            }
        return false;
    }
    public bool ListingsReady(uint item)
    {
        var a = gui.GetAddonByName<AddonItemSearchResult>("ItemSearchResult");
        var proxy = Proxy;
        if (a == null || !Visible("ItemSearchResult") || a->Results == null || proxy == null || proxy->SearchItemId != item) return false;
        // WaitingForListings can remain set after the visible rows are fully populated.
        // Validate the UI rows instead; a still-empty, waiting window is not complete.
        if (!MarketReadiness.Ready(proxy->ListingCount, a->Results->ListLength,
            a->Results->IsUpdatePending, a->Results->IsScrollRefreshPending, proxy->WaitingForListings)) return false;
        for (int i = 0; i < proxy->ListingCount; i++)
            if (proxy->Listings[i].ItemId != item) return false;
        return true;
    }
    public List<Offer> Read(uint item, ulong owner)
    {
        List<Offer> offers = [];
        if (!ListingsReady(item)) return offers;
        for (int i = 0; i < Math.Min(Proxy->ListingCount, 100); i++)
        {
            var row = Proxy->Listings[i];
            if (row.ItemId != item || row.ContentId == owner || row.IsMannequin || row.IsSellingAsSet) continue;
            offers.Add(new(row.ListingId, row.ItemId, row.UnitPrice, row.Quantity, row.TotalTax, row.IsHqItem));
        }
        return offers;
    }
    public bool SelectOffer(Offer offer, ulong owner)
    {
        if (Visible("SelectYesno") || !Read(offer.Item, owner).Contains(offer)) return false;
        var a = gui.GetAddonByName<AddonItemSearchResult>("ItemSearchResult");
        if (a == null || a->Results == null) return false;
        for (int i = 0; i < Math.Min(Proxy->ListingCount, 100); i++)
            if (Proxy->Listings[i].ListingId == offer.Id && i < a->Results->GetItemCount())
            {
                a->Results->DispatchItemEvent(i, AtkEventType.ListItemClick);
                return true;
            }
        return false;
    }
    public bool Matches(Offer offer)
    {
        if (!Visible("ItemSearchResult") || Proxy == null || Proxy->SearchItemId != offer.Item) return false;
        var p = Proxy->LastPurchasedMarketboardItem;
        return p.ListingId == offer.Id && p.ItemId == offer.Item && p.Quantity == offer.Quantity && p.UnitPrice == offer.Price && p.TotalTax == offer.Tax && p.IsHqItem == offer.Hq;
    }
    // Caller already owns the purchase dialog, validates selected listing and waits between actions.
    public int Confirm(Offer offer, string itemName)
    {
        var a = gui.GetAddonByName<AddonSelectYesno>("SelectYesno");
        if (a == null || !Visible("SelectYesno") || a->PromptText == null || !Matches(offer)) return 0;
        // Unknown warnings are never blanket-accepted. Require the exact game item name.
        var prompt = a->PromptText->NodeText.ToString();
        bool itemPrompt = prompt.Contains(itemName, StringComparison.Ordinal);
        var check = a->ConfirmCheckBox;
        bool hasCheckbox = check != null && check->OwnerNode != null && check->OwnerNode->AtkResNode.IsVisible();
        bool equipmentWarning = hasCheckbox && new[] { "装備でき", "装備することができ", "cannot equip", "unable to equip", "équiper", "equiper", "ausrüsten", "ausruesten" }
            .Any(marker => prompt.Contains(marker, StringComparison.OrdinalIgnoreCase));
        if (!itemPrompt && !equipmentWarning) return 0;
        if (check != null && check->OwnerNode != null && check->OwnerNode->AtkResNode.IsVisible() && !check->IsChecked)
        {
            if (!check->IsEnabled) return 0;
            var ev = FindButtonClick(&a->AtkUnitBase, (AtkEvent*)check->OwnerNode->AtkResNode.AtkEventManager.Event);
            if (ev == null) return 0;
            AtkEventData data = default;
            a->AtkUnitBase.ReceiveEvent(ev->State.EventType, (int)ev->Param, ev, &data);
            return 1;
        }
        return Click(&a->AtkUnitBase, a->YesButton) ? 2 : 0;
    }
    internal static bool Click(AtkUnitBase* addon, AtkComponentButton* button)
    {
        if (button == null || !button->IsEnabled || button->OwnerNode == null || !button->OwnerNode->AtkResNode.IsVisible()) return false;
        var ev = FindButtonClick(addon, (AtkEvent*)button->OwnerNode->AtkResNode.AtkEventManager.Event);
        if (ev == null) return false;
        AtkEventData data = default;
        addon->ReceiveEvent(ev->State.EventType, (int)ev->Param, ev, &data);
        return true;
    }
    internal static AtkEvent* FindButtonClick(AtkUnitBase* addon, AtkEvent* first)
    {
        // The first registered event may be hover/focus/press, not a click.
        // Follow the registration and dispatch only the addon-owned click.
        for (int count = 0; first != null && count < 32; count++, first = first->NextEvent)
            if (first->State.EventType == AtkEventType.ButtonClick && first->Listener == (AtkEventListener*)addon)
                return first;
        return null;
    }
}
