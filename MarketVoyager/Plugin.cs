using Dalamud;
using System.Collections.Concurrent;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Command;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using Roxyz0501.Dalamud.CommonUi;

namespace MarketVoyager;
public sealed partial class Plugin : IDalamudPlugin
{
    private readonly IDalamudPluginInterface pi;
    private readonly ICommandManager commands;
    private readonly IFramework framework;
    private readonly IMarketBoard board;
    private readonly IDataManager data;
    private readonly IPlayerState player;
    private readonly Configuration config;
    private readonly Runner runner;
    private readonly Travel travel;
    private readonly HashSet<string> yesAlready;
    private readonly IFontHandle font;
    private readonly Item[] items;
    private readonly Dictionary<uint, Item> itemById;
    private readonly Dictionary<uint, string> worldNames;
    private readonly IMarketBoard.ItemPurchasedDelegate purchased;
    private bool open;
    private readonly Persistence persistence;
    private readonly IClientState client;
    private readonly IClientState.LogoutDelegate logout;
    private volatile bool disposed;
    private readonly object receiptGate = new();
    private bool storageFailureNotified;
    private readonly ConcurrentQueue<(uint Item, uint Quantity)> receipts = new();

    public Plugin(IDalamudPluginInterface pi, ICommandManager commands, IFramework framework,
        IMarketBoard board, IDataManager data, IGameGui gui, IObjectTable objects,
        IPlayerState player, IClientState client, IPluginLog log)
    {
        this.pi = pi; this.commands = commands; this.framework = framework;
        this.board = board; this.data = data; this.player = player;
        this.client = client;
        persistence = new(pi.GetPluginConfigDirectory(), () => pi.GetPluginConfig() as Configuration ?? new(),
            ex => log.Error(ex, "Market Voyager persistence stopped; explicit recovery required"));
        config = persistence.Config;
        Texts.Validate();
        if (config.Lists.Count == 0) config.Lists.Add(new());
        config.Selected = Math.Clamp(config.Selected, 0, config.Lists.Count - 1);
        string? game = null;
        try { game = client.ClientLanguage.ToString() switch { "Japanese" => "ja", "English" => "en", "German" => "de", "French" => "fr", _ => null }; }
        catch (Exception ex) { log.Debug(ex, "Game language unavailable"); }
        var resolved = Rules.ResolveLanguage(config.Language, game, pi.UiLanguage);
        if (config.Language != resolved) { config.Language = resolved; Save(); }
        travel = new(pi);
        runner = new(config, travel, new NativeMarket(gui), new NativeSales(gui, data), data, objects, player, client, log, SaveSafety, Save);
        yesAlready = pi.GetOrCreateData<HashSet<string>>("YesAlready.StopRequests", () => []);
        items = data.GetExcelSheet<Item>().Where(x => x.ItemSearchCategory.RowId != 0).ToArray();
        itemById = items.ToDictionary(x => x.RowId);
        worldNames = data.GetExcelSheet<World>().Where(x => x.IsPublic).ToDictionary(x => x.RowId, x => x.Name.ToString());
        catalogue = items.Select(x => new CatalogItem(x.RowId, x.Name.ToString(), x.ItemSearchCategory.RowId,
            x.ItemSearchCategory.Value.Name.ToString(), x.PriceLow, x.LevelItem.RowId, x.CanBeHq)).ToArray();
        catalogueById = catalogue.ToDictionary(x => x.Id);
        categories = catalogue.Select(x => (x.CategoryId, x.CategoryName)).Distinct().OrderBy(x => x.CategoryName).ToArray();
        RefreshCatalogue();
        bool normalized = false;
        foreach (var rule in config.Lists.SelectMany(x => x.Items))
            if (itemById.TryGetValue(rule.ItemId, out var item) && !item.CanBeHq && rule.Quality != Quality.Nq)
            { rule.Quality = Quality.Nq; normalized = true; }
        if (normalized) Save();
        var ranges = (Texts.FontText + string.Concat(items.Select(x => x.Name.ToString())) + string.Concat(categories.Select(x => x.Name)) +
            string.Concat(StandardPluginTabs.Languages.Select(x => x.SelfName)) + "HQNQ0123456789Market VoyagerLifestreamvnavmeshKo-fiRoxyz0501").ToGlyphRange();
        font = pi.UiBuilder.FontAtlas.NewDelegateFontHandle(step => step.OnPreBuild(tk =>
            tk.AddDalamudAssetFont(DalamudAsset.NotoSansCjkRegular, new SafeFontConfig { SizePx = 17, GlyphRanges = ranges })));
        commands.AddHandler("/marketvoyager", new CommandInfo((_, args) => { if (args.Trim() == "stop") runner.Stop(); else open = true; }) { HelpMessage = "Market Voyager" });
        purchased = x => { lock (receiptGate) { if (!disposed) receipts.Enqueue((x.CatalogId, x.ItemQuantity)); } };
        board.ItemPurchased += purchased;
        logout = (_, _) => OnLogout();
        client.Logout += logout;
        framework.Update += Update;
        pi.UiBuilder.Draw += Draw;
        pi.UiBuilder.OpenMainUi += Open;
        pi.UiBuilder.OpenConfigUi += Open;
    }
    private string T(string key) => Texts.Get(key, config.Language);
    private void Save() => persistence.Changed();
    private void ScheduleSave() => Save();
    private void SaveSafety() => persistence.Durable();
    private void StartRun()
    {
        // Persist rule identities once before a tour can create reservations for them.
        if (persistence.Flush()) runner.Start();
    }
    private void ToggleRunPause()
    {
        if (runner.IsPaused && !persistence.Flush()) return;
        runner.TogglePause();
    }
    private void OnLogout()
    {
        if (disposed) return;
        runner.Stop(); persistence.Flush();
    }
    private void Open() => open = true;
    private void Update(IFramework frameworkUnused)
    {
        if (disposed) return;
        persistence.Tick();
        if (persistence.Blocked)
        {
            if (runner.Running) runner.Stop();
            while (receipts.TryDequeue(out _)) { }
            if (config.Pending != null || config.PendingSale != null) yesAlready.Add("MarketVoyager"); else yesAlready.Remove("MarketVoyager");
            if (!storageFailureNotified) { open = true; storageFailureNotified = true; }
            return;
        }
        storageFailureNotified = false;
        while (receipts.TryDequeue(out var receipt)) runner.Purchased(receipt.Item, receipt.Quantity);
        if (runner.Running || config.Pending != null || config.PendingSale != null) yesAlready.Add("MarketVoyager"); else yesAlready.Remove("MarketVoyager");
        runner.Tick();
    }
    private void Draw()
    {
        if (!open) return;
        using var pushed = font.Push();
        ImGui.SetNextWindowSize(new(1180, 720), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new(850, 500), new(float.MaxValue, float.MaxValue));
        if (ImGui.Begin("Market Voyager###MarketVoyager", ref open))
        {
            DrawPersistence();
            DrawTransport();
            if (config.Pending is { } pending)
            {
                ImGui.TextWrapped(T("Pending"));
                ImGui.TextUnformatted($"{pending.ItemId} / {(pending.Hq ? "HQ" : "NQ")} / {pending.Quantity}");
                if (!persistence.Blocked && !runner.Running && player.ContentId == pending.CharacterId)
                {
                    ImGui.TextWrapped(T("RecoveryHelp"));
                    if (ImGui.Button(T("NotPurchased"))) runner.ResolveStoppedPurchase(true, false);
                    if (ImGui.Button(T("CountPending"))) runner.ResolveStoppedPurchase(false, true);
                }
            }
            if (config.PendingSale is { } sale)
            {
                ImGui.TextWrapped(T("SalePending"));
                if (!persistence.Blocked && !runner.Running && player.ContentId == sale.Character && ImGui.Button(T("CountSale")))
                {
                    var rule = config.FindRule(sale.RuleId);
                    if (rule != null) Sales.Complete(rule, sale);
                    config.PendingSale = null; config.PruneDetached(); TrySaveSafety();
                }
            }
            if (ImGui.BeginTabBar("Tabs"))
            {
                if (ImGui.BeginTabItem(T("Lists") + "###Lists")) { DrawLists(); ImGui.EndTabItem(); }
                StandardPluginTabs.DrawStandardTrailingTabs(T("Settings"), "Settings", DrawSettings,
                    T("Language"), () => {
                        var changed = StandardPluginTabs.DrawLanguageSelector(T("Language"), T("LanguageHelp"), config.Language!);
                        if (changed != null) { config.Language = changed; Save(); }
                    }, T("Support"), () => StandardPluginTabs.DrawSupportContent(T("Support"), T("SupportHelp"), T("Recipient"), T("Donate")));
                ImGui.EndTabBar();
            }
        }
        ImGui.End();
    }
    private void DrawSettings()
    {
        ImGui.TextUnformatted(T("PurchaseDelay"));
        int delay = config.PurchaseDelayMs;
        ImGui.SetNextItemWidth(180);
        if (ImGui.InputInt("###PurchaseDelayMs", ref delay, 50, 100) && Math.Clamp(delay, 50, 2000) != config.PurchaseDelayMs)
        { config.PurchaseDelayMs = delay; ScheduleSave(); }
        ImGui.SameLine();
        if (ImGui.Button(T("DefaultDelay") + "###DefaultPurchaseDelay") && config.PurchaseDelayMs != 100)
        { config.PurchaseDelayMs = 100; ScheduleSave(); }
        ImGui.TextWrapped(T("PurchaseDelayHelp"));
        ImGui.Separator();
        foreach (var dependency in new[] { "Lifestream", "vnavmesh" })
            ImGui.TextUnformatted($"{dependency}: {T(travel.Loaded(dependency) ? "Loaded" : "Missing")}");
        ImGui.TextWrapped(T("Help")); ImGui.TextWrapped(T("RetryHelp"));
    }
    private void TrySaveSafety()
    {
        try { SaveSafety(); } catch (IOException) { runner.Stop(); }
    }
    private void DrawPersistence()
    {
        if (persistence.Status.Length == 0) return;
        ImGui.TextWrapped(T(persistence.Status));
        if (persistence.Blocked) ImGui.TextWrapped(persistence.DirectoryPath);
        if (persistence.RecoveryPath != null) ImGui.TextWrapped(persistence.RecoveryPath);
        if (persistence.CanRetry && ImGui.Button(T("RetrySave") + "###RetrySave")) persistence.Retry();
        if (persistence.Status == "RecoveryAvailable" && !persistence.Blocked && !runner.Running && persistence.RecoveryPath != null &&
            ImGui.Button(T("RestoreCandidate") + "###RestoreCandidate"))
        {
            if (persistence.RestoreCandidate()) { selectedRules.Clear(); rulePage = 0; }
        }
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        runner.Stop(); yesAlready.Remove("MarketVoyager");
        pi.UiBuilder.Draw -= Draw; pi.UiBuilder.OpenMainUi -= Open; pi.UiBuilder.OpenConfigUi -= Open;
        framework.Update -= Update; board.ItemPurchased -= purchased; client.Logout -= logout;
        commands.RemoveHandler("/marketvoyager");
        lock (receiptGate) { while (receipts.TryDequeue(out _)) { } }
        persistence.Dispose(); font.Dispose();
    }
}
