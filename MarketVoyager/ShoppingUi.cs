using Dalamud.Bindings.ImGui;

namespace MarketVoyager;

public sealed partial class Plugin
{
    private readonly CatalogItem[] catalogue;
    private readonly Dictionary<uint, CatalogItem> catalogueById;
    private readonly (uint Id, string Name)[] categories;
    private CatalogItem[] filtered = [];
    private readonly HashSet<uint> selectedItems = [];
    private readonly HashSet<Guid> selectedRules = [];
    private string query = "", registeredQuery = "";
    private uint category;
    private int minimumNpc, sort, cataloguePage, rulePage;
    private int batchPrice = 100, batchQuantity = 1, batchQuality, margin = 1;
    private bool batchTax, npcMode, setPrice = true, setQuantity = true, setQuality = true;
    private BatchResult? batchMessage;
    private const int PageSize = 30;
    private void RefreshCatalogue()
    {
        filtered = Catalog.Filter(catalogue, query, category, minimumNpc, (CatalogSort)sort);
        cataloguePage = 0;
    }
    private BatchOptions Options(bool editing = false) => new(batchPrice, batchQuantity, (Quality)batchQuality,
        batchTax, npcMode, margin, !editing || setPrice, !editing || setQuantity, !editing || setQuality);
    private void ApplyResult(BatchResult result) { batchMessage = result; if (result.Changed > 0) Save(); }
    private bool EditingLocked => runner.Running || config.Pending != null || config.PendingSale != null;
    private void DrawTransport()
    {
        ImGui.BeginDisabled(EditingLocked || !travel.Ready || config.Lists[config.Selected].Items.Count == 0);
        if (ImGui.Button("\u25B6 " + T("Run") + "###Run")) runner.Start();
        ImGui.EndDisabled(); ImGui.SameLine();
        ImGui.BeginDisabled(!runner.Running);
        if (ImGui.Button((runner.PauseRequested ? "\u25B6 " + T("Resume") : "\u2016 " + T("Pause")) + "###Pause")) runner.TogglePause();
        ImGui.SameLine(); if (ImGui.Button("\u25A0 " + T("Stop") + "###Stop")) runner.Stop();
        ImGui.EndDisabled(); ImGui.SameLine();
        ImGui.TextWrapped(T(runner.IsPaused ? "Paused" : runner.PauseRequested ? "Pausing" : runner.State.ToString()));
        if (runner.PauseRequested && ImGui.IsItemHovered()) ImGui.SetTooltip(T("PauseHelp"));
        if (runner.CurrentWorld.Length > 0) { ImGui.SameLine(); ImGui.TextUnformatted(runner.CurrentWorld); }
        ImGui.Separator();
    }
    private void DrawLists()
    {
        ImGui.BeginDisabled(EditingLocked);
        ImGui.SetNextItemWidth(240);
        if (ImGui.BeginCombo("###List", config.Lists[config.Selected].Name))
        {
            for (int i = 0; i < config.Lists.Count; i++)
                if (ImGui.Selectable(config.Lists[i].Name + "###list" + i, i == config.Selected))
                { config.Selected = i; selectedRules.Clear(); rulePage = 0; batchMessage = null; Save(); }
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        if (config.Lists.Count < 50 && ImGui.Button(T("AddList") + "###AddList"))
        { config.Lists.Add(new() { Name = $"{T("Lists")} {config.Lists.Count + 1}" }); config.Selected = config.Lists.Count - 1; selectedRules.Clear(); rulePage = 0; Save(); }
        ImGui.SameLine(); if (ImGui.Button(T("ListOptions") + "###ListOptions")) ImGui.OpenPopup("ListOptionsPopup");
        var list = config.Lists[config.Selected];
        ImGui.SameLine(); ImGui.TextDisabled($"{list.Items.Count} / {Catalog.ListCapacity}");
        ImGui.SetNextWindowSize(new(520, 0), ImGuiCond.Appearing);
        if (ImGui.BeginPopup("ListOptionsPopup")) { DrawListOptions(list); ImGui.EndPopup(); }
        if (batchMessage is { } message)
            ImGui.TextWrapped(message.Error != null ? T(message.Error) : $"{T("Changed")}: {message.Changed} / {T("Skipped")}: {message.Skipped}");
        if (ImGui.BeginTabBar("ListViews"))
        {
            if (ImGui.BeginTabItem(T("Registered") + "###Registered")) { DrawRegistered(list); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem(T("AddItems") + "###AddItems")) { DrawCatalogue(list); ImGui.EndTabItem(); }
            ImGui.EndTabBar();
        }
        ImGui.EndDisabled();
    }
    private void DrawListOptions(ShoppingList list)
    {
        string name = list.Name;
        ImGui.TextWrapped(T("Name")); ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("###Name", ref name, 100)) { list.Name = name; ScheduleSave(); }
        ImGui.Separator(); bool sell = list.AutoSell;
        if (ImGui.Checkbox(T("AutoSell") + "###AutoSell", ref sell)) { list.AutoSell = sell; Save(); }
        if (sell)
        {
            int threshold = list.FreeSlotsThreshold;
            ImGui.TextWrapped(T("FreeSlots")); ImGui.SetNextItemWidth(120);
            if (ImGui.InputInt("###FreeSlots", ref threshold)) { list.FreeSlotsThreshold = Math.Clamp(threshold, 0, 139); ScheduleSave(); }
            ImGui.TextWrapped(T("SaleHelp"));
        }
    }
    // Labels above controls keep translated text out of neighbouring columns.
    private void Field(string key) { ImGui.TableNextColumn(); ImGui.TextWrapped(T(key)); ImGui.SetNextItemWidth(-1); }
    private void DrawBatchSettings()
    {
        if (ImGui.BeginTable("InputDefaults", 3, ImGuiTableFlags.SizingStretchSame))
        {
            Field("UnitShort");
            ImGui.BeginDisabled(npcMode);
            if (ImGui.InputInt("###BatchPrice", ref batchPrice, 0, 0)) batchPrice = Math.Max(1, batchPrice);
            ImGui.EndDisabled();
            Field("QuantityShort"); if (ImGui.InputInt("###BatchQuantity", ref batchQuantity, 0, 0)) batchQuantity = Math.Max(1, batchQuantity);
            Field("Quality"); ImGui.Combo("###BatchQuality", ref batchQuality, new[] { T("Nq"), T("Hq"), T("Any") }, 3);
            ImGui.EndTable();
        }
        ImGui.BeginDisabled(npcMode); ImGui.Checkbox(T("Tax") + "###BatchTax", ref batchTax); ImGui.EndDisabled();
        if (ImGui.CollapsingHeader(T("NpcOptions") + "###NpcOptions"))
        {
            ImGui.Checkbox(T("NpcMode") + "###NpcMode", ref npcMode);
            ImGui.TextWrapped(T("Margin")); ImGui.SetNextItemWidth(140);
            if (ImGui.InputInt("###Margin", ref margin)) margin = Math.Max(1, margin);
            ImGui.TextWrapped(T("NpcHelp"));
        }
        else if (npcMode) ImGui.TextDisabled($"{T("NpcOptions")}: -{margin:N0}");
    }
    private void DrawCatalogue(ShoppingList list)
    {
        bool refresh = false;
        if (ImGui.BeginTable("CatalogueFilters", 4, ImGuiTableFlags.SizingStretchSame))
        {
            Field("Find"); refresh |= ImGui.InputText("###Find", ref query, 100);
            Field("Category");
            if (ImGui.BeginCombo("###Category", category == 0 ? T("All") : categories.First(x => x.Id == category).Name))
            {
                if (ImGui.Selectable(T("All"), category == 0)) { category = 0; refresh = true; }
                foreach (var c in categories)
                    if (ImGui.Selectable(c.Name + "###cat" + c.Id, category == c.Id)) { category = c.Id; refresh = true; }
                ImGui.EndCombo();
            }
            Field("MinimumNpcShort"); refresh |= ImGui.InputInt("###MinimumNpc", ref minimumNpc, 0, 0); minimumNpc = Math.Max(0, minimumNpc);
            Field("Sort"); refresh |= ImGui.Combo("###Sort", ref sort, new[] { T("NpcDescending"), T("NpcAscending"), T("ByName"), T("Category"), T("LevelDescending"), "ID" }, 6);
            ImGui.EndTable();
        }
        if (refresh) RefreshCatalogue();
        DrawBatchSettings();
        if (ImGui.Button(T("SelectResults") + "###SelectResults")) { selectedItems.Clear(); selectedItems.UnionWith(filtered.Select(x => x.Id)); }
        ImGui.SameLine(); if (ImGui.Button(T("ClearSelection") + "###ClearItems")) selectedItems.Clear();
        ImGui.SameLine(); ImGui.BeginDisabled(selectedItems.Count == 0);
        if (ImGui.Button($"{T("AddSelected")} ({selectedItems.Count})###AddSelected"))
            ApplyResult(Catalog.Add(list, catalogue.Where(x => selectedItems.Contains(x.Id)), Options()));
        ImGui.EndDisabled();
        PageControls("CataloguePages", ref cataloguePage, filtered.Length);
        if (ImGui.BeginTable("CatalogueTableV2", 4, TableFlags, new(0, Math.Max(100, ImGui.GetContentRegionAvail().Y))))
        {
            Column("", 32); Column(T("ByName"), 370); Column(T("Category"), 200); Column(T("NpcPrice"), 180);
            ImGui.TableSetupScrollFreeze(2, 1); ImGui.TableHeadersRow();
            foreach (var item in filtered.Skip(cataloguePage * PageSize).Take(PageSize))
            {
                ImGui.TableNextRow(); ImGui.TableNextColumn(); bool selected = selectedItems.Contains(item.Id);
                if (ImGui.Checkbox("###pick" + item.Id, ref selected)) { if (selected) selectedItems.Add(item.Id); else selectedItems.Remove(item.Id); }
                Cell(item.Name); Cell(item.CategoryName); Cell(item.NpcPrice.ToString("N0"));
            }
            ImGui.EndTable();
        }
    }
    private const ImGuiTableFlags TableFlags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.ScrollY | ImGuiTableFlags.ScrollX | ImGuiTableFlags.Resizable;
    private static void Column(string name, float width) => ImGui.TableSetupColumn(name, ImGuiTableColumnFlags.WidthFixed, Math.Max(width, ImGui.CalcTextSize(name).X + 16));
    private static void Cell(string value) { ImGui.TableNextColumn(); ImGui.TextUnformatted(value); if (ImGui.IsItemHovered()) ImGui.SetTooltip(value); }
    private void DrawRegistered(ShoppingList list)
    {
        ImGui.SetNextItemWidth(240);
        if (ImGui.InputTextWithHint("###RegisteredQuery", T("Find"), ref registeredQuery, 100)) rulePage = 0;
        ImGui.SameLine(); if (ImGui.Button(T("SelectAll") + "###SelectRules")) selectedRules.UnionWith(list.Items.Select(x => x.Id));
        ImGui.SameLine(); if (ImGui.Button(T("ClearSelection") + "###ClearRules")) selectedRules.Clear();
        ImGui.SameLine(); if (ImGui.Button($"{T("Actions")} ({selectedRules.Count})###Actions")) ImGui.OpenPopup("RuleActions");
        if (ImGui.BeginPopup("RuleActions"))
        {
            ImGui.BeginDisabled(selectedRules.Count == 0);
            if (ImGui.MenuItem(T("BatchEdit"))) { ImGui.CloseCurrentPopup(); openBatch = true; }
            if (ImGui.MenuItem(T("SetNpc"))) { ImGui.CloseCurrentPopup(); openNpcBatch = true; }
            if (ImGui.MenuItem(T("ResetSelected"))) { if (Catalog.Reset(list, selectedRules) > 0) Save(); }
            if (ImGui.MenuItem(T("RemoveSelected"))) { list.Items.RemoveAll(x => selectedRules.Contains(x.Id)); selectedRules.Clear(); Save(); }
            ImGui.EndDisabled(); ImGui.Separator();
            if (ImGui.MenuItem(T("Reset"))) { if (Catalog.Reset(list, list.Items.Select(x => x.Id).ToHashSet()) > 0) Save(); }
            ImGui.EndPopup();
        }
        if (openBatch) { ImGui.OpenPopup("BatchPopup"); openBatch = false; }
        if (openNpcBatch) { ImGui.OpenPopup("NpcBatchPopup"); openNpcBatch = false; }
        ImGui.SetNextWindowSize(new(560, 0), ImGuiCond.Appearing);
        if (ImGui.BeginPopup("NpcBatchPopup"))
        {
            ImGui.TextWrapped($"{T("SetNpc")} ({selectedRules.Count})");
            ImGui.TextWrapped(T("Margin")); ImGui.SetNextItemWidth(160);
            if (ImGui.InputInt("###NpcBatchMargin", ref margin)) margin = Math.Max(1, margin);
            ImGui.TextWrapped(T("NpcHelp"));
            if (ImGui.Button(T("EditSelected") + "###ApplyNpcBatch"))
            {
                ApplyResult(Catalog.Edit(list, selectedRules, catalogueById,
                    new(batchPrice, batchQuantity, (Quality)batchQuality, true, true, margin, true, false, false)));
                if (batchMessage?.Error == null) ImGui.CloseCurrentPopup();
            }
            if (batchMessage?.Error is { } error) ImGui.TextWrapped(T(error));
            ImGui.EndPopup();
        }
        ImGui.SetNextWindowSize(new(620, 0), ImGuiCond.Appearing);
        if (ImGui.BeginPopup("BatchPopup"))
        {
            ImGui.TextUnformatted($"{T("BatchEdit")} ({selectedRules.Count})");
            ImGui.Checkbox(T("SetPrice") + "###SetPrice", ref setPrice); ImGui.SameLine();
            ImGui.Checkbox(T("SetQuantity") + "###SetQuantity", ref setQuantity); ImGui.SameLine();
            ImGui.Checkbox(T("SetQuality") + "###SetQuality", ref setQuality);
            DrawBatchSettings();
            if (ImGui.Button(T("EditSelected") + "###EditSelected"))
            { ApplyResult(Catalog.Edit(list, selectedRules, catalogueById, Options(true))); if (batchMessage?.Error == null) ImGui.CloseCurrentPopup(); }
            ImGui.EndPopup();
        }
        var rows = string.IsNullOrWhiteSpace(registeredQuery) ? list.Items : list.Items.Where(x =>
            catalogueById.TryGetValue(x.ItemId, out var item) && item.Name.Contains(registeredQuery, StringComparison.OrdinalIgnoreCase) || x.ItemId.ToString() == registeredQuery).ToList();
        PageControls("RulePages", ref rulePage, rows.Count);
        Guid? remove = null;
        if (ImGui.BeginTable("RulesTableV2", 9, TableFlags, new(0, Math.Max(100, ImGui.GetContentRegionAvail().Y))))
        {
            Column("", 30); Column(T("ByName"), 275); Column(T("Quality"), 100); Column(T("UnitShort"), 115);
            Column(T("TaxShort"), 65); Column(T("QuantityShort"), 100); Column(T("Bought"), 90); Column(T("NpcPrice"), 150); Column("...", 40);
            ImGui.TableSetupScrollFreeze(2, 1); ImGui.TableHeadersRow();
            foreach (var rule in rows.Skip(rulePage * PageSize).Take(PageSize))
            {
                catalogueById.TryGetValue(rule.ItemId, out var item); ImGui.PushID(rule.Id.ToString());
                ImGui.TableNextRow(); ImGui.TableNextColumn(); bool selected = selectedRules.Contains(rule.Id);
                if (ImGui.Checkbox("###Selected", ref selected)) { if (selected) selectedRules.Add(rule.Id); else selectedRules.Remove(rule.Id); }
                Cell(item?.Name ?? rule.ItemId.ToString());
                ImGui.TableNextColumn(); int quality = (int)rule.Quality; ImGui.SetNextItemWidth(-1); ImGui.BeginDisabled(item?.CanHq != true);
                if (ImGui.Combo("###Quality", ref quality, new[] { "NQ", "HQ", T("AnyShort") }, 3))
                    ApplyResult(Catalog.Edit(list, new HashSet<Guid> { rule.Id }, catalogueById,
                        new(rule.UnitLimit, rule.QuantityLimit, (Quality)quality, rule.IncludeTax, false, margin, false, false)));
                ImGui.EndDisabled();
                ImGui.TableNextColumn(); int price = rule.UnitLimit; ImGui.SetNextItemWidth(-1);
                if (ImGui.InputInt("###Unit", ref price, 0, 0)) { rule.UnitLimit = Math.Max(1, price); ScheduleSave(); }
                ImGui.TableNextColumn(); bool tax = rule.IncludeTax;
                if (ImGui.Checkbox("###Tax", ref tax)) { rule.IncludeTax = tax; Save(); }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(T("Tax"));
                ImGui.TableNextColumn(); int quantity = rule.QuantityLimit; ImGui.SetNextItemWidth(-1);
                if (ImGui.InputInt("###Quantity", ref quantity, 0, 0)) { rule.QuantityLimit = Math.Max(1, quantity); ScheduleSave(); }
                Cell(rule.Purchased.ToString("N0")); Cell(item?.NpcPrice.ToString("N0") ?? "-");
                ImGui.TableNextColumn(); if (ImGui.SmallButton("...")) ImGui.OpenPopup("RowOptions");
                if (ImGui.BeginPopup("RowOptions"))
                {
                    ImGui.TextWrapped(item?.Name ?? rule.ItemId.ToString());
                    ImGui.TextWrapped(T("Margin")); ImGui.SetNextItemWidth(140);
                    if (ImGui.InputInt("###RowMargin", ref margin)) margin = Math.Max(1, margin);
                    if (item != null && ImGui.Button(T("SetNpc")))
                    {
                        var limit = Rules.NpcLimit(item.NpcPrice, margin);
                        if (limit is { } value) { rule.UnitLimit = value; rule.IncludeTax = true; Save(); ImGui.CloseCurrentPopup(); }
                        else batchMessage = new(0, 0, "InvalidBatch");
                    }
                    ImGui.Separator();
                    if (ImGui.MenuItem(T("Reset"))) { if (Catalog.Reset(list, new HashSet<Guid> { rule.Id }) > 0) Save(); }
                    if (ImGui.MenuItem(T("Remove"))) remove = rule.Id;
                    ImGui.EndPopup();
                }
                ImGui.PopID();
            }
            ImGui.EndTable();
        }
        if (remove is { } id) { list.Items.RemoveAll(x => x.Id == id); selectedRules.Remove(id); Save(); }
    }
    private bool openBatch, openNpcBatch;
    private void PageControls(string id, ref int page, int count)
    {
        ImGui.PushID(id); int last = Math.Max(0, (count - 1) / PageSize); page = Math.Clamp(page, 0, last);
        ImGui.BeginDisabled(page == 0); if (ImGui.Button("<")) page--; ImGui.EndDisabled();
        ImGui.SameLine(); ImGui.TextUnformatted($"{page + 1} / {last + 1} ({count})"); ImGui.SameLine();
        ImGui.BeginDisabled(page == last); if (ImGui.Button(">")) page++; ImGui.EndDisabled(); ImGui.PopID();
    }
}
