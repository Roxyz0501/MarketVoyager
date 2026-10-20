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
    private string query = "";
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
    private void DrawLists()
    {
        bool locked = runner.Running || config.Pending != null || config.PendingSale != null;
        ImGui.BeginDisabled(locked);
        ImGui.PushItemWidth(220);
        if (ImGui.BeginCombo("###List", config.Lists[config.Selected].Name))
        {
            for (int i = 0; i < config.Lists.Count; i++)
                if (ImGui.Selectable(config.Lists[i].Name + "###list" + i, i == config.Selected))
                { config.Selected = i; selectedRules.Clear(); rulePage = 0; batchMessage = null; Save(); }
            ImGui.EndCombo();
        }
        if (config.Lists.Count < 50 && ImGui.Button(T("AddList")))
        { config.Lists.Add(new() { Name = $"{T("Lists")} {config.Lists.Count + 1}" }); config.Selected = config.Lists.Count - 1; selectedRules.Clear(); rulePage = 0; Save(); }
        var list = config.Lists[config.Selected];
        string name = list.Name;
        if (ImGui.InputText(T("Name") + "###Name", ref name, 100)) { list.Name = name; ScheduleSave(); }
        bool sell = list.AutoSell;
        if (ImGui.Checkbox(T("AutoSell") + "###AutoSell", ref sell)) { list.AutoSell = sell; Save(); }
        if (sell)
        {
            int threshold = list.FreeSlotsThreshold;
            ImGui.TextWrapped(T("FreeSlots"));
            if (ImGui.InputInt("###FreeSlots", ref threshold))
            { list.FreeSlotsThreshold = Math.Clamp(threshold, 0, 139); ScheduleSave(); }
            ImGui.TextWrapped(T("SaleHelp"));
        }
        if (ImGui.Button(T("Start") + "###Start")) runner.Start();
        ImGui.SameLine(); ImGui.TextUnformatted($"{list.Items.Count} / {Catalog.ListCapacity}");
        if (batchMessage is { } message)
            ImGui.TextWrapped(message.Error != null ? T(message.Error) : $"{T("Changed")}: {message.Changed} / {T("Skipped")}: {message.Skipped}");
        if (ImGui.CollapsingHeader(T("BatchSettings") + "###BatchSettings", ImGuiTreeNodeFlags.DefaultOpen)) DrawBatchSettings();
        if (ImGui.CollapsingHeader(T("Catalogue") + "###Catalogue")) DrawCatalogue(list);
        if (ImGui.CollapsingHeader(T("Registered") + "###Registered", ImGuiTreeNodeFlags.DefaultOpen)) DrawRegistered(list);
        ImGui.PopItemWidth();
        ImGui.EndDisabled();
    }
    private void DrawBatchSettings()
    {
        if (ImGui.InputInt(T("Unit") + "###BatchPrice", ref batchPrice)) batchPrice = Math.Max(1, batchPrice);
        ImGui.Checkbox(T("Tax") + "###BatchTax", ref batchTax);
        if (ImGui.InputInt(T("Quantity") + "###BatchQuantity", ref batchQuantity)) batchQuantity = Math.Max(1, batchQuantity);
        ImGui.Combo(T("Quality") + "###BatchQuality", ref batchQuality, new[] { T("Nq"), T("Hq"), T("Any") }, 3);
        ImGui.Checkbox(T("NpcMode") + "###NpcMode", ref npcMode);
        if (ImGui.InputInt(T("Margin") + "###Margin", ref margin)) margin = Math.Max(1, margin);
        ImGui.TextWrapped(T("NpcHelp"));
    }
    private void DrawCatalogue(ShoppingList list)
    {
        bool refresh = ImGui.InputText(T("Find") + "###Find", ref query, 100);
        refresh |= ImGui.InputInt(T("MinimumNpc") + "###MinimumNpc", ref minimumNpc);
        minimumNpc = Math.Max(0, minimumNpc);
        if (ImGui.BeginCombo(T("Category") + "###Category", category == 0 ? T("All") : categories.First(x => x.Id == category).Name))
        {
            if (ImGui.Selectable(T("All"), category == 0)) { category = 0; refresh = true; }
            foreach (var c in categories)
                if (ImGui.Selectable(c.Name + "###cat" + c.Id, category == c.Id)) { category = c.Id; refresh = true; }
            ImGui.EndCombo();
        }
        refresh |= ImGui.Combo(T("Sort") + "###Sort", ref sort,
            new[] { T("NpcDescending"), T("NpcAscending"), T("ByName"), T("Category"), T("LevelDescending"), "ID" }, 6);
        if (refresh) RefreshCatalogue();
        if (ImGui.Button(T("SelectResults") + "###SelectResults")) { selectedItems.Clear(); selectedItems.UnionWith(filtered.Select(x => x.Id)); }
        ImGui.SameLine(); if (ImGui.Button(T("ClearSelection") + "###ClearItems")) selectedItems.Clear();
        if (ImGui.Button(T("AddSelected") + "###AddSelected")) ApplyResult(Catalog.Add(list, catalogue.Where(x => selectedItems.Contains(x.Id)), Options()));
        ImGui.SameLine();
        if (ImGui.Button(T("AddResults") + "###AddResults")) ApplyResult(Catalog.Add(list, filtered, Options()));
        PageControls("CataloguePages", ref cataloguePage, filtered.Length);
        if (ImGui.BeginTable("CatalogueTable", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.Resizable))
        {
            ImGui.TableSetupColumn(T("ByName")); ImGui.TableSetupColumn(T("Category")); ImGui.TableSetupColumn(T("NpcPrice")); ImGui.TableHeadersRow();
            foreach (var item in filtered.Skip(cataloguePage * PageSize).Take(PageSize))
            {
                ImGui.TableNextRow(); ImGui.TableNextColumn(); bool selected = selectedItems.Contains(item.Id);
                if (ImGui.Checkbox(item.Name + "###pick" + item.Id, ref selected)) { if (selected) selectedItems.Add(item.Id); else selectedItems.Remove(item.Id); }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(item.Name);
                ImGui.TableNextColumn(); ImGui.TextUnformatted(item.CategoryName);
                ImGui.TableNextColumn(); ImGui.TextUnformatted(item.NpcPrice.ToString("N0"));
            }
            ImGui.EndTable();
        }
    }
    private void DrawRegistered(ShoppingList list)
    {
        if (ImGui.Button(T("SelectAll") + "###SelectRules")) selectedRules.UnionWith(list.Items.Select(x => x.Id));
        ImGui.SameLine(); if (ImGui.Button(T("ClearSelection") + "###ClearRules")) selectedRules.Clear();
        ImGui.Checkbox(T("SetPrice") + "###SetPrice", ref setPrice); ImGui.SameLine();
        ImGui.Checkbox(T("SetQuantity") + "###SetQuantity", ref setQuantity); ImGui.SameLine();
        ImGui.Checkbox(T("SetQuality") + "###SetQuality", ref setQuality);
        if (ImGui.Button(T("EditSelected") + "###EditSelected")) ApplyResult(Catalog.Edit(list, selectedRules, catalogueById, Options(true)));
        ImGui.SameLine();
        if (ImGui.Button(T("EditAll") + "###EditAll")) ApplyResult(Catalog.Edit(list, list.Items.Select(x => x.Id).ToHashSet(), catalogueById, Options(true)));
        if (ImGui.Button(T("ResetSelected") + "###ResetSelected")) { if (Catalog.Reset(list, selectedRules) > 0) Save(); }
        ImGui.SameLine();
        if (ImGui.Button(T("Reset") + "###ResetAll")) { if (Catalog.Reset(list, list.Items.Select(x => x.Id).ToHashSet()) > 0) Save(); }
        PageControls("RulePages", ref rulePage, list.Items.Count);
        Guid? remove = null;
        foreach (var rule in list.Items.Skip(rulePage * PageSize).Take(PageSize))
        {
            catalogueById.TryGetValue(rule.ItemId, out var item);
            ImGui.PushID(rule.Id.ToString()); ImGui.Separator();
            bool selected = selectedRules.Contains(rule.Id);
            if (ImGui.Checkbox((item?.Name ?? rule.ItemId.ToString()) + "###Selected", ref selected))
            { if (selected) selectedRules.Add(rule.Id); else selectedRules.Remove(rule.Id); }
            int price = rule.UnitLimit, quantity = rule.QuantityLimit; bool tax = rule.IncludeTax;
            ImGui.SetNextItemWidth(180);
            if (ImGui.InputInt(T("Unit") + "###Unit", ref price)) { rule.UnitLimit = Math.Max(1, price); ScheduleSave(); }
            if (ImGui.Checkbox(T("Tax") + "###Tax", ref tax)) { rule.IncludeTax = tax; Save(); }
            if (item != null)
            {
                ImGui.TextUnformatted($"{T("NpcPrice")}: {item.NpcPrice:N0}"); ImGui.SameLine();
                if (ImGui.Button(T("SetNpc") + "###SetNpc"))
                {
                    var limit = Rules.NpcLimit(item.NpcPrice, margin);
                    if (limit is { } value) { rule.UnitLimit = value; rule.IncludeTax = true; Save(); }
                    else batchMessage = new(0, 0, "InvalidBatch");
                }
            }
            ImGui.SetNextItemWidth(180);
            if (ImGui.InputInt(T("Quantity") + "###Quantity", ref quantity)) { rule.QuantityLimit = Math.Max(1, quantity); ScheduleSave(); }
            int quality = (int)rule.Quality;
            ImGui.BeginDisabled(item?.CanHq != true);
            if (ImGui.Combo(T("Quality") + "###Quality", ref quality, new[] { T("Nq"), T("Hq"), T("Any") }, 3))
            {
                var options = new BatchOptions(rule.UnitLimit, rule.QuantityLimit, (Quality)quality, rule.IncludeTax, false, margin, false, false);
                ApplyResult(Catalog.Edit(list, new HashSet<Guid> { rule.Id }, catalogueById, options));
            }
            ImGui.EndDisabled();
            ImGui.TextUnformatted($"{T("Bought")}: {rule.Purchased}"); ImGui.SameLine();
            if (ImGui.Button(T("Remove") + "###Remove")) remove = rule.Id;
            ImGui.PopID();
        }
        if (remove is { } id) { list.Items.RemoveAll(x => x.Id == id); selectedRules.Remove(id); Save(); }
    }
    private void PageControls(string id, ref int page, int count)
    {
        ImGui.PushID(id); int last = Math.Max(0, (count - 1) / PageSize); page = Math.Clamp(page, 0, last);
        ImGui.BeginDisabled(page == 0); if (ImGui.Button("<")) page--; ImGui.EndDisabled();
        ImGui.SameLine(); ImGui.TextUnformatted($"{page + 1} / {last + 1} ({count})"); ImGui.SameLine();
        ImGui.BeginDisabled(page == last); if (ImGui.Button(">")) page++; ImGui.EndDisabled(); ImGui.PopID();
    }
}
