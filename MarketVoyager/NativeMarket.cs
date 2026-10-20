using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace MarketVoyager;

internal sealed unsafe class NativeMarket(IGameGui gui)
{
    private nint notifiedInput;
    private string notifiedText = "";
    public bool Visible(string name) { var a = gui.GetAddonByName<AtkUnitBase>(name); return a != null && a->IsVisible && a->IsReady; }
    private InfoProxyItemSearch* Proxy => InfoModule.Instance() == null ? null : InfoModule.Instance()->GetInfoProxyItemSearch();
    public void CloseResults() { notifiedInput = 0; var a = gui.GetAddonByName<AtkUnitBase>("ItemSearchResult"); if (a != null && a->IsVisible) a->Close(true); }
    public void CloseMarket() { notifiedInput = 0; notifiedText = ""; CloseResults(); var a = gui.GetAddonByName<AtkUnitBase>("ItemSearch"); if (a != null && a->IsVisible) a->Close(true); }
    public string Diagnostics()
    {
        var a = gui.GetAddonByName<AddonItemSearch>("ItemSearch");
        var result = gui.GetAddonByName<AddonItemSearchResult>("ItemSearchResult");
        var proxy = Proxy;
        return $"searchVisible={Visible("ItemSearch")}, mode={(a == null ? -1 : (int)a->Mode)}, " +
            $"inputPresent={a != null && a->SearchTextInput != null}, " +
            $"inputHasText={a != null && a->SearchTextInput != null && a->SearchTextInput->EvaluatedString.StringLength > 0}, " +
            $"resultVisible={Visible("ItemSearchResult")}, rows={(result == null || result->Results == null ? -1 : result->Results->ListLength)}, " +
            $"listingCount={(proxy == null ? 0 : proxy->ListingCount)}, waiting={proxy != null && proxy->WaitingForListings}, " +
            $"searchItem={(proxy == null ? 0 : proxy->SearchItemId)}, " +
            $"updatePending={result != null && result->Results != null && result->Results->IsUpdatePending}, " +
            $"scrollPending={result != null && result->Results != null && result->Results->IsScrollRefreshPending}";
    }

    // The input widget and its actual registered button event are used; no request packet API.
    public bool Search(string name)
    {
        var a = gui.GetAddonByName<AddonItemSearch>("ItemSearch");
        if (!CanEnterSearch(a)) return false;
        if (!FocusInput(&a->AtkUnitBase, a->SearchTextInput)) return false;
        // Mode describes the previous result/category, not whether the name input is usable.
        // The normal search button owns the transition into name-search mode.
        if (a->SearchTextInput->EvaluatedString.ToString() != name)
        {
            notifiedInput = 0;
            a->SearchTextInput->SetText(name);
        }
        if (notifiedInput != (nint)a->SearchTextInput || notifiedText != name)
        {
            if (!NotifyTextChanged(&a->AtkUnitBase, a->SearchTextInput)) return false;
            notifiedInput = (nint)a->SearchTextInput; notifiedText = name;
            return false; // Observe the callback's ordinary button update on the next tick.
        }
        return Click(&a->AtkUnitBase, a->SearchButton);
    }
    internal static bool FocusInput(AtkUnitBase* owner, AtkComponentTextInput* input)
    {
        if (owner == null || input == null || input->OwnerAddon != owner ||
            input->OwnerNode == null || !input->Enabled || input->CollisionNode == null) return false;
        if (input->IsActive) return true;
        owner->SetFocusNode(&input->CollisionNode->AtkResNode, true);
        // Allow the ordinary focus event and widget activation to finish before typing.
        return false;
    }
    internal static bool NotifyTextChanged(AtkUnitBase* owner, AtkComponentTextInput* input)
    {
        if (owner == null || input == null || input->OwnerAddon != owner || input->Callback == null) return false;
        input->Callback(owner, InputCallbackType.TextChanged, input->RawString.StringPtr,
            input->EvaluatedString.StringPtr, input->CallbackEventKind);
        return true;
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
    public bool ResultsOpenFor(uint item)
    {
        if (!Visible("ItemSearchResult")) return false;
        return ResultsMatch(AgentItemSearch.Instance(), Proxy, item);
    }
    internal static bool ResultsMatch(AgentItemSearch* agent, InfoProxyItemSearch* proxy, uint item) =>
        item != 0 && agent != null && proxy != null && agent->ResultItemId == item && proxy->SearchItemId == item;
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
    public bool SelectOffer(Offer offer, ulong owner, Action? beforeSelection = null)
    {
        if (Visible("SelectYesno") || !Read(offer.Item, owner).Contains(offer)) return false;
        var a = gui.GetAddonByName<AddonItemSearchResult>("ItemSearchResult");
        if (a == null || a->Results == null) return false;
        for (int i = 0; i < Math.Min(Proxy->ListingCount, 100); i++)
            if (Proxy->Listings[i].ListingId == offer.Id && i < a->Results->GetItemCount())
            {
                beforeSelection?.Invoke();
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
    public bool SelectedMatches(Offer offer)
    {
        if (!Visible("ItemSearchResult")) return false;
        return SelectedMatches(AgentItemSearch.Instance(), Proxy, offer);
    }
    internal static bool SelectedMatches(AgentItemSearch* agent, InfoProxyItemSearch* proxy, Offer offer)
    {
        if (agent == null || proxy == null || agent->ResultItemId != offer.Item || proxy->SearchItemId != offer.Item ||
            proxy->ListingCount > 100 || agent->ResultSelectedIndex >= proxy->ListingCount) return false;
        var row = proxy->Listings[(int)agent->ResultSelectedIndex];
        return row.ListingId == offer.Id && row.ItemId == offer.Item && row.Quantity == offer.Quantity &&
            row.UnitPrice == offer.Price && row.TotalTax == offer.Tax && row.IsHqItem == offer.Hq &&
            !row.IsMannequin && !row.IsSellingAsSet;
    }
    public bool CancelConfirmation(Offer offer)
    {
        var a = gui.GetAddonByName<AddonSelectYesno>("SelectYesno");
        return a != null && Visible("SelectYesno") && SelectedMatches(offer) &&
            a->PromptText != null && PurchasePrompt(a->PromptText->NodeText.ToString()) &&
            Click(&a->AtkUnitBase, a->NoButton);
    }
    internal static bool PurchasePrompt(string prompt) =>
        new[] { "購入", "買います", "装備でき", "purchase", "buy", "cannot equip", "unable to equip",
            "acheter", "achat", "équiper", "kaufen", "kauf", "ausrüsten", "구매", "구입", "购买", "購買" }
            .Any(marker => prompt.Contains(marker, StringComparison.OrdinalIgnoreCase));
    // Caller already owns the purchase dialog, validates selected listing and waits between actions.
    public int Confirm(Offer offer, string itemName, Action beforePurchase)
    {
        var a = gui.GetAddonByName<AddonSelectYesno>("SelectYesno");
        // LastPurchasedMarketboardItem is populated by the game's accept handler,
        // after this dialog. Before acceptance validate the selected row instead.
        if (a == null || !Visible("SelectYesno") || a->PromptText == null || !SelectedMatches(offer)) return 0;
        var prompt = a->PromptText->NodeText.ToString();
        bool itemPrompt = prompt.Contains(itemName, StringComparison.Ordinal);
        var check = a->ConfirmCheckBox;
        bool hasCheckbox = check != null && check->OwnerNode != null && check->OwnerNode->AtkResNode.IsVisible();
        bool equipmentWarning = hasCheckbox && new[] { "装備でき", "装備することができ", "cannot equip", "unable to equip", "équiper", "equiper", "ausrüsten", "ausruesten" }
            .Any(marker => prompt.Contains(marker, StringComparison.OrdinalIgnoreCase));
        if (!itemPrompt && !equipmentWarning && !PurchasePrompt(prompt)) return 0;
        if (check != null && check->OwnerNode != null && check->OwnerNode->AtkResNode.IsVisible() && !check->IsChecked)
        {
            if (!check->IsEnabled) return 0;
            var ev = FindButtonClick(&a->AtkUnitBase, (AtkEvent*)check->OwnerNode->AtkResNode.AtkEventManager.Event);
            if (ev == null) return 0;
            AtkEventData data = default;
            a->AtkUnitBase.ReceiveEvent(ev->State.EventType, (int)ev->Param, ev, &data);
            return 1;
        }
        return Click(&a->AtkUnitBase, a->YesButton, beforePurchase) ? 2 : 0;
    }
    internal static bool Click(AtkUnitBase* addon, AtkComponentButton* button, Action? beforeClick = null)
    {
        if (addon == null || button == null || button->OwnerNode == null || !button->IsEnabled || !button->OwnerNode->AtkResNode.IsVisible()) return false;
        var ev = FindButtonClick(addon, (AtkEvent*)button->OwnerNode->AtkResNode.AtkEventManager.Event);
        if (ev == null) return false;
        AtkEventData data = default;
        beforeClick?.Invoke();
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
