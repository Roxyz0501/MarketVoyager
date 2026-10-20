using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using MarketVoyager;
using System.Runtime.InteropServices;
using InteropGenerator.Runtime;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;

unsafe class Program
{
    static int changes, clicks;
    static nint callbackOwner, callbackRaw, callbackEvaluated;
    static int callbackKind;
    static bool savedBeforeClick, correctInput;
    static int focuses;
    static nint focusedNode;
    [UnmanagedCallersOnly]
    static byte Focus(AtkUnitBase* owner, AtkResNode* node, byte cursor, uint param)
    { focuses++; focusedNode = (nint)node; return 1; }
    [UnmanagedCallersOnly]
    static byte Visible(AtkResNode* node) => (byte)(node != null && node->NodeFlags.HasFlag(NodeFlags.Visible) ? 1 : 0);
    [UnmanagedCallersOnly]
    static InputCallbackResult Changed(AtkUnitBase* owner, InputCallbackType type, CStringPointer raw, CStringPointer evaluated, int kind)
    {
        if (type == InputCallbackType.TextChanged) changes++;
        callbackOwner = (nint)owner; callbackRaw = (nint)raw.Value; callbackEvaluated = (nint)evaluated.Value; callbackKind = kind;
        return InputCallbackResult.None;
    }
    [UnmanagedCallersOnly]
    static void Receive(AtkEventListener* owner, AtkEventType type, int param, AtkEvent* evt, AtkEventData* data)
    {
        clicks++;
        correctInput = savedBeforeClick && data != null && type == AtkEventType.ButtonClick && param == 42;
    }
    static void Main()
    {
        int checks = 0;
        AtkResNode.Addresses.IsVisible.Value = (nint)(delegate* unmanaged<AtkResNode*, byte>)&Visible;
        AtkUnitBase.Addresses.SetFocusNode.Value = (nint)(delegate* unmanaged<AtkUnitBase*, AtkResNode*, byte, uint, byte>)&Focus;
        void Check(bool value, string reason) { checks++; if (!value) throw new Exception(reason); }
        AddonItemSearch addon = default;
        AtkComponentTextInput input = default;
        addon.SearchTextInput = &input;
        addon.IsVisible = true;
        addon.Flags1A1 = 1;
        // Reproduce a freshly opened market and all category/result modes.
        foreach (var mode in Enum.GetValues<AddonItemSearch.SearchMode>())
        {
            addon.Mode = mode;
            Check(NativeMarket.CanEnterSearch(&addon), $"Name input must work in {mode}");
        }
        addon.IsVisible = false;
        Check(!NativeMarket.CanEnterSearch(&addon), "Hidden market must not accept input");
        addon.IsVisible = true; addon.Flags1A1 = 0;
        Check(!NativeMarket.CanEnterSearch(&addon), "Wait for setup");
        addon.Flags1A1 = 1; addon.SearchTextInput = null;
        Check(!NativeMarket.CanEnterSearch(&addon), "Wait for input widget");
        Check(!NativeMarket.CanEnterSearch(null), "Missing market");

        AtkUnitBase owner = default, other = default;
        AtkEvent hover = default, foreign = default, click = default;
        hover.Listener = (AtkEventListener*)&owner;
        hover.State.EventType = AtkEventType.MouseOver; hover.NextEvent = &foreign;
        foreign.Listener = (AtkEventListener*)&other;
        foreign.State.EventType = AtkEventType.ButtonClick; foreign.NextEvent = &click;
        click.Listener = (AtkEventListener*)&owner;
        click.State.EventType = AtkEventType.ButtonClick; click.Param = 42;
        Check(NativeMarket.FindButtonClick(&owner, &hover) == &click, "Skip hover and other listeners, preserve registered click");
        click.State.EventType = AtkEventType.ButtonPress;
        Check(NativeMarket.FindButtonClick(&owner, &hover) == null, "Press cannot substitute for click");
        click.NextEvent = &hover;
        Check(NativeMarket.FindButtonClick(&owner, &hover) == null, "Malformed cycle is bounded");
        Check(NativeMarket.FindButtonClick(&owner, null) == null, "Missing registration");

        byte* raw = stackalloc byte[] { 65, 0 }, evaluated = stackalloc byte[] { 66, 0 };
        input.OwnerAddon = &owner; input.Callback = &Changed; input.CallbackEventKind = 71;
        input.RawString.StringPtr = raw; input.EvaluatedString.StringPtr = evaluated;
        Check(NativeMarket.NotifyTextChanged(&owner, &input) && changes == 1, "Actual input callback executes TextChanged");
        Check(callbackOwner == (nint)(&owner) && callbackRaw == (nint)raw && callbackEvaluated == (nint)evaluated && callbackKind == 71, "Registered owner, raw/evaluated text and event kind passed unchanged");
        Check(!NativeMarket.NotifyTextChanged(&other, &input) && changes == 1, "Foreign input callback not invoked");
        input.Callback = null;
        Check(!NativeMarket.NotifyTextChanged(&owner, &input), "Missing input callback rejected");
        AtkCollisionNode inputCollision = default;
        AtkComponentNode inputNode = default;
        input.OwnerNode = &inputNode; input.CollisionNode = &inputCollision; input.Enabled = true;
        Check(!NativeMarket.FocusInput(&owner, &input) && focuses == 1 && focusedNode == (nint)(&inputCollision),
            "Search uses the normal collision-node focus route and waits before typing");
        input.IsActive = true;
        Check(NativeMarket.FocusInput(&owner, &input) && focuses == 1, "Activated search input proceeds without repeated focus");
        input.Enabled = false;
        Check(!NativeMarket.FocusInput(&owner, &input) && focuses == 1, "Disabled search input is not forced active");
        input.Enabled = true;
        Check(!NativeMarket.FocusInput(&other, &input), "Foreign search input cannot be focused");
        input.CollisionNode = null;
        Check(!NativeMarket.FocusInput(&owner, &input), "Missing focus target rejected");

        var vtable = stackalloc nint[3];
        vtable[2] = (nint)(delegate* unmanaged<AtkEventListener*, AtkEventType, int, AtkEvent*, AtkEventData*, void>)&Receive;
        *(nint*)&owner = (nint)vtable;
        AtkComponentNode node = default;
        node.NodeFlags = NodeFlags.Visible | NodeFlags.Enabled;
        AtkComponentButton button = default; button.OwnerNode = &node;
        node.AtkEventManager.Event = &click; click.NextEvent = null; click.State.EventType = AtkEventType.ButtonClick;
        Check(NativeMarket.Click(&owner, &button, () => savedBeforeClick = true) && clicks == 1 && correctInput, "Purchase persistence precedes actual native ABI click with non-null input");
        savedBeforeClick = false; node.NodeFlags = NodeFlags.Visible;
        Check(!NativeMarket.Click(&owner, &button, () => savedBeforeClick = true) && !savedBeforeClick && clicks == 1, "Disabled button neither submits nor writes a reservation");
        button.OwnerNode = null;
        Check(!NativeMarket.Click(&owner, &button), "Missing button owner rejected before IsEnabled access");

        AgentItemSearch agent = default;
        InfoProxyItemSearch proxy = default;
        var offer = new Offer(123, 42, 100, 5, 25, true);
        agent.ResultItemId = 42; agent.ResultSelectedIndex = 1;
        proxy.SearchItemId = 42; proxy.ListingCount = 2;
        Check(NativeMarket.ResultsMatch(&agent, &proxy, 42), "An already opened exact-item listing window can advance directly to eligibility");
        Check(!NativeMarket.ResultsMatch(&agent, &proxy, 43), "Another item's listing window cannot bypass search");
        agent.ResultItemId = 43;
        Check(!NativeMarket.ResultsMatch(&agent, &proxy, 42), "Stale agent/proxy combination cannot bypass search");
        agent.ResultItemId = 42;
        ref var row = ref proxy.Listings[1];
        row.ListingId = 123; row.ItemId = 42; row.UnitPrice = 100; row.Quantity = 5; row.TotalTax = 25; row.IsHqItem = true;
        Check(NativeMarket.SelectedMatches(&agent, &proxy, offer) && proxy.LastPurchasedMarketboardItem.ListingId == 0, "Pre-OK selected row matches with an empty last-purchase record");
        proxy.LastPurchasedMarketboardItem.ListingId = 999;
        Check(NativeMarket.SelectedMatches(&agent, &proxy, offer), "Previous purchase cannot block a new confirmation");
        row.UnitPrice++;
        Check(!NativeMarket.SelectedMatches(&agent, &proxy, offer), "Changed selected price rejected");
        row.UnitPrice--; agent.ResultSelectedIndex = 0;
        Check(!NativeMarket.SelectedMatches(&agent, &proxy, offer), "Different selected row rejected");
        agent.ResultSelectedIndex = 100;
        Check(!NativeMarket.SelectedMatches(&agent, &proxy, offer), "Selected index bounds checked");
        Check(NativeMarket.PurchasePrompt("このアイテムを購入します。よろしいですか？"), "Purchase prompt without item name accepted after row identity check");
        Check(!NativeMarket.PurchasePrompt("テレポしますか？"), "Unrelated prompt rejected");

        for (uint rows = 1; rows <= 100; rows++)
        {
            Check(MarketReadiness.Ready(rows, (int)rows, false, false, true), "Populated UI with stale waiting flag");
            Check(MarketReadiness.Ready(rows, (int)rows, false, false, false), "Populated UI");
            Check(!MarketReadiness.Ready(rows, (int)rows - 1, false, false, true), "Incomplete list");
            Check(!MarketReadiness.Ready(rows, (int)rows, true, false, true), "Pending update");
            Check(!MarketReadiness.Ready(rows, (int)rows, false, true, true), "Pending scroll");
        }
        Check(!MarketReadiness.Ready(0, 0, false, false, true), "Loading must not be treated as no offers");
        Check(MarketReadiness.Ready(0, 0, false, false, false), "Completed empty search");
        Check(!MarketReadiness.Ready(101, 101, false, false, false), "Native buffer bound");
        Check(!MarketReadiness.Ready(1, -1, false, false, false), "Invalid UI count");
        Console.WriteLine($"PASS {checks} native search/event/readiness regressions; no game process, input or packets.");
    }
}
