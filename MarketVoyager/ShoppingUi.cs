using Dalamud.Bindings.ImGui;
using System.Numerics;

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
    private Vector2 listOptionsAnchor;
    private Vector2 listOptionsPosition;
    private bool confirmListDelete;
    private void RefreshCatalogue()
    {
        filtered = Catalog.Filter(catalogue, query, category, minimumNpc, (CatalogSort)sort);
        cataloguePage = 0;
    }
    private BatchOptions Options(bool editing = false) => new(batchPrice, batchQuantity, (Quality)batchQuality,
        batchTax, npcMode, margin, !editing || setPrice, !editing || setQuantity, !editing || setQuality);
    private void ApplyResult(BatchResult result) { batchMessage = result; if (result.Changed > 0) Save(); }
    private bool EditingLocked => runner.Running && !runner.IsPaused;
    private bool LedgerLocked(Guid id) => config.HasPending(id);
    private void RemoveRows(ShoppingList list, IReadOnlySet<Guid> ids)
    {
        if (EditingLocked) return;
        if (config.RemoveRules(list, ids) > 0) Save();
        selectedRules.ExceptWith(ids);
    }
    private void DeleteList(ShoppingList list)
    {
        if (EditingLocked) return;
        int index = config.Lists.IndexOf(list);
        if (index < 0) return;
        config.RemoveRules(list, list.Items.Select(x => x.Id).ToHashSet());
        config.Lists.RemoveAt(index);
        if (config.Lists.Count == 0) config.Lists.Add(new() { Name = $"{T("Lists")} 1" });
        config.Selected = Math.Clamp(config.Selected > index ? config.Selected - 1 : config.Selected, 0, config.Lists.Count - 1);
        selectedRules.Clear(); rulePage = 0; batchMessage = null;
        confirmListDelete = openBatch = openNpcBatch = false;
        saveAt = null; Save();
    }
    private void DrawTransport()
    {
        ImGui.BeginDisabled(runner.Running || config.Pending != null || config.PendingSale != null || !travel.Ready || config.Lists[config.Selected].Items.Count == 0);
        if (ImGui.Button("\u25B6 " + T("Run") + "###Run")) runner.Start();
        ImGui.EndDisabled(); ImGui.SameLine();
        ImGui.BeginDisabled(!runner.Running);
        if (ImGui.Button((runner.PauseRequested ? "\u25B6 " + T("Resume") : "\u2016 " + T("Pause")) + "###Pause")) runner.TogglePause();
        ImGui.SameLine(); if (ImGui.Button("\u25A0 " + T("Stop") + "###Stop")) runner.Stop();
        ImGui.EndDisabled(); ImGui.SameLine();
        ImGui.TextWrapped(T(runner.IsPaused ? "Paused" : runner.PauseRequested ? "Pausing" : runner.State.ToString()));
        if (runner.PauseRequested && ImGui.IsItemHovered()) ImGui.SetTooltip(T("PauseHelp"));
        if (runner.CurrentWorld.Length > 0) { ImGui.SameLine(); ImGui.TextUnformatted(runner.CurrentWorld); }
        if (runner.OfferCount is { } count)
        {
            var name = catalogueById.TryGetValue(runner.EvaluatedItem, out var item) ? item.Name : runner.EvaluatedItem.ToString();
            ImGui.TextWrapped($"{T("LastEvaluation")} — {name}: {T("Offers")} {count} / {T("Candidates")} {runner.CandidateCount}");
        }
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
        ImGui.SameLine(); if (ImGui.Button(T("ListOptions") + "###ListOptions"))
        {
            confirmListDelete = false;
            listOptionsAnchor = new(ImGui.GetItemRectMin().X, ImGui.GetItemRectMax().Y + ImGui.GetStyle().ItemSpacing.Y);
            ImGui.OpenPopup("ListOptionsPopup");
        }
        var list = config.Lists[config.Selected];
        ImGui.SameLine(); ImGui.TextDisabled($"{list.Items.Count} / {Catalog.ListCapacity}");
        DrawListOptionsPopup(list);
        list = config.Lists[config.Selected];
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
    private void DrawListOptionsPopup(ShoppingList list)
    {
        if (!ImGui.IsPopupOpen("ListOptionsPopup")) return;
        var viewport = ImGui.GetMainViewport();
        var width = Math.Min(520, viewport.WorkSize.X - 16);
        var anchor = Vector2.Clamp(listOptionsAnchor, viewport.WorkPos + new Vector2(8),
            viewport.WorkPos + new Vector2(Math.Max(8, viewport.WorkSize.X - width - 8), Math.Max(8, viewport.WorkSize.Y - 320)));
        ImGui.SetNextWindowPos(anchor, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new(width, 0), ImGuiCond.Always);
        if (ImGui.BeginPopup("ListOptionsPopup", ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings))
        {
            listOptionsPosition = ImGui.GetWindowPos();
            DrawListOptions(list); ImGui.EndPopup();
        }
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
        ImGui.Separator();
        if (!confirmListDelete)
        {
            if (ImGui.Button(T("DeleteList") + "###DeleteList")) confirmListDelete = true;
        }
        else
        {
            ImGui.TextWrapped(T("DeleteListHelp"));
            if (ImGui.Button(T("DeleteList") + "###ConfirmDeleteList")) { DeleteList(list); ImGui.CloseCurrentPopup(); }
            ImGui.SameLine();
            if (ImGui.Button(T("Cancel") + "###CancelDeleteList")) confirmListDelete = false;
        }
    }
    // Labels above controls keep translated text out of neighbouring columns.
    private void Field(string key) { ImGui.TableNextColumn(); ImGui.TextWrapped(T(key)); ImGui.SetNextItemWidth(-1); }
    private void DrawBatchSettings(bool editing = false)
    {
        if (ImGui.BeginTable("InputDefaults", 3, ImGuiTableFlags.SizingStretchSame))
        {
            Field("UnitShort");
            ImGui.BeginDisabled(npcMode || editing && !setPrice);
            if (ImGui.InputInt("###BatchPrice", ref batchPrice, 0, 0)) batchPrice = Math.Max(1, batchPrice);
            ImGui.EndDisabled();
            Field("QuantityShort"); ImGui.BeginDisabled(editing && !setQuantity);
            if (ImGui.InputInt("###BatchQuantity", ref batchQuantity, 0, 0)) batchQuantity = Math.Max(1, batchQuantity);
            ImGui.EndDisabled();
            Field("Quality"); ImGui.BeginDisabled(editing && !setQuality);
            ImGui.Combo("###BatchQuality", ref batchQuality, new[] { T("Nq"), T("Hq"), T("Any") }, 3);
            ImGui.EndDisabled();
            ImGui.EndTable();
        }
        ImGui.BeginDisabled(npcMode || editing && !setPrice); ImGui.Checkbox(T("Tax") + "###BatchTax", ref batchTax); ImGui.EndDisabled();
        ImGui.BeginDisabled(editing && !setPrice);
        if (ImGui.CollapsingHeader(T("NpcOptions") + "###NpcOptions"))
        {
            ImGui.Checkbox(T("NpcMode") + "###NpcMode", ref npcMode);
            ImGui.TextWrapped(T("Margin")); ImGui.SetNextItemWidth(140);
            if (ImGui.InputInt("###Margin", ref margin)) margin = Math.Max(1, margin);
            ImGui.TextWrapped(T("NpcHelp"));
        }
        else if (npcMode) ImGui.TextDisabled($"{T("NpcOptions")}: -{margin:N0}");
        ImGui.EndDisabled();
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
        ImGui.SameLine(); ImGui.BeginDisabled(list.Items.Count == 0);
        if (ImGui.Button(T("BatchEdit") + "###BatchEdit")) openBatch = true;
        ImGui.EndDisabled();
        ImGui.SameLine(); if (ImGui.Button(T("SelectAll") + "###SelectRules")) selectedRules.UnionWith(list.Items.Select(x => x.Id));
        ImGui.SameLine(); if (ImGui.Button(T("ClearSelection") + "###ClearRules")) selectedRules.Clear();
        ImGui.SameLine(); if (ImGui.Button($"{T("Actions")} ({selectedRules.Count})###Actions")) ImGui.OpenPopup("RuleActions");
        if (ImGui.BeginPopup("RuleActions"))
        {
            ImGui.BeginDisabled(selectedRules.Count == 0);
            if (ImGui.MenuItem(T("BatchEdit"))) { ImGui.CloseCurrentPopup(); openBatch = true; }
            if (ImGui.MenuItem(T("SetNpc"))) { ImGui.CloseCurrentPopup(); openNpcBatch = true; }
            ImGui.BeginDisabled(selectedRules.Any(LedgerLocked));
            if (ImGui.MenuItem(T("ResetSelected"))) { if (Catalog.Reset(list, selectedRules) > 0) Save(); }
            ImGui.EndDisabled();
            if (ImGui.MenuItem(T("RemoveSelected"))) RemoveRows(list, selectedRules.ToHashSet());
            ImGui.EndDisabled(); ImGui.Separator();
            ImGui.BeginDisabled(list.Items.Any(x => LedgerLocked(x.Id)));
            if (ImGui.MenuItem(T("Reset"))) { if (Catalog.Reset(list, list.Items.Select(x => x.Id).ToHashSet()) > 0) Save(); }
            ImGui.EndDisabled();
            ImGui.EndPopup();
        }
        if (openBatch)
        {
            batchAll = selectedRules.Count == 0;
            setPrice = setQuantity = setQuality = false;
            batchMessage = null;
            ImGui.OpenPopup("BatchPopup"); openBatch = false;
        }
        if (openNpcBatch)
        {
            batchAll = selectedRules.Count == 0; batchMessage = null;
            ImGui.OpenPopup("NpcBatchPopup"); openNpcBatch = false;
        }
        ImGui.SetNextWindowSize(new(560, 0), ImGuiCond.Appearing);
        if (ImGui.BeginPopup("NpcBatchPopup"))
        {
            ImGui.TextWrapped(T("SetNpc"));
            DrawBatchTargets(list);
            ImGui.TextWrapped(T("Margin")); ImGui.SetNextItemWidth(160);
            if (ImGui.InputInt("###NpcBatchMargin", ref margin)) margin = Math.Max(1, margin);
            ImGui.TextWrapped(T("NpcHelp"));
            if (ImGui.Button(T(batchAll ? "EditAll" : "EditSelected") + "###ApplyNpcBatch"))
            {
                ApplyResult(Catalog.Edit(list, BatchTargets(list), catalogueById,
                    new(batchPrice, batchQuantity, (Quality)batchQuality, true, true, margin, true, false, false)));
                if (batchMessage?.Error == null) ImGui.CloseCurrentPopup();
            }
            if (batchMessage?.Error is { } error) ImGui.TextWrapped(T(error));
            ImGui.EndPopup();
        }
        ImGui.SetNextWindowSize(new(620, 0), ImGuiCond.Appearing);
        if (ImGui.BeginPopup("BatchPopup"))
        {
            ImGui.TextUnformatted(T("BatchEdit"));
            DrawBatchTargets(list);
            ImGui.TextWrapped(T("BatchHelp"));
            ImGui.Checkbox(T("SetPrice") + "###SetPrice", ref setPrice); ImGui.SameLine();
            ImGui.Checkbox(T("SetQuantity") + "###SetQuantity", ref setQuantity); ImGui.SameLine();
            ImGui.Checkbox(T("SetQuality") + "###SetQuality", ref setQuality);
            DrawBatchSettings(true);
            ImGui.BeginDisabled(!setPrice && !setQuantity && !setQuality);
            if (ImGui.Button(T(batchAll ? "EditAll" : "EditSelected") + "###EditSelected"))
            { ApplyResult(Catalog.Edit(list, BatchTargets(list), catalogueById, Options(true))); if (batchMessage?.Error == null) ImGui.CloseCurrentPopup(); }
            ImGui.EndDisabled();
            if (batchMessage?.Error is { } error) ImGui.TextWrapped(T(error));
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
                    ImGui.BeginDisabled(LedgerLocked(rule.Id));
                    if (ImGui.MenuItem(T("Reset"))) { if (Catalog.Reset(list, new HashSet<Guid> { rule.Id }) > 0) Save(); }
                    ImGui.EndDisabled();
                    if (ImGui.MenuItem(T("Remove"))) remove = rule.Id;
                    ImGui.EndPopup();
                }
                ImGui.PopID();
            }
            ImGui.EndTable();
        }
        if (remove is { } id) RemoveRows(list, new HashSet<Guid> { id });
    }
    private bool openBatch, openNpcBatch, batchAll;
    private IReadOnlySet<Guid> BatchTargets(ShoppingList list) =>
        batchAll ? list.Items.Select(x => x.Id).ToHashSet() : selectedRules;
    private void DrawBatchTargets(ShoppingList list)
    {
        ImGui.BeginDisabled(selectedRules.Count == 0);
        if (ImGui.RadioButton($"{T("SelectedRows")} ({selectedRules.Count})###SelectedRows", !batchAll)) batchAll = false;
        ImGui.EndDisabled();
        if (ImGui.RadioButton($"{T("AllRows")} ({list.Items.Count})###AllRows", batchAll)) batchAll = true;
        ImGui.Separator();
    }
    private void PageControls(string id, ref int page, int count)
    {
        ImGui.PushID(id); int last = Math.Max(0, (count - 1) / PageSize); page = Math.Clamp(page, 0, last);
        ImGui.BeginDisabled(page == 0); if (ImGui.Button("<")) page--; ImGui.EndDisabled();
        ImGui.SameLine(); ImGui.TextUnformatted($"{page + 1} / {last + 1} ({count})"); ImGui.SameLine();
        ImGui.BeginDisabled(page == last); if (ImGui.Button(">")) page++; ImGui.EndDisabled(); ImGui.PopID();
    }
}
