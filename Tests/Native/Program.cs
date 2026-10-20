using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using MarketVoyager;

unsafe class Program
{
    static void Main()
    {
        int checks = 0;
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
