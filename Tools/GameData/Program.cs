using Lumina;
using Lumina.Excel.Sheets;
using Lumina.Data;
using var game = new GameData(Path.Combine(args[0], "sqpack"));
foreach(var language in new[]{Language.Japanese, Language.English, Language.German, Language.French}) {
 var items=game.GetExcelSheet<EObjName>(language);
 foreach(var row in items.Where(x=>x.Singular.ToString().Contains("Market", StringComparison.OrdinalIgnoreCase) || x.Singular.ToString().Contains("マーケット") || x.Singular.ToString().Contains("Marktbrett") || x.Singular.ToString().Contains("ventes")))
  Console.WriteLine($"{language}: EObjName {row.RowId}: {row.Singular}");
 var aether=game.GetExcelSheet<Aetheryte>(language).GetRow(8);
 Console.WriteLine($"{language}: Aetheryte 8: {aether.PlaceName.Value.Name}, territory {aether.Territory.RowId}");
}
Console.WriteLine("ItemSearchCategory: " + string.Join(", ", typeof(ItemSearchCategory).GetProperties().Select(x=>x.Name+":"+x.PropertyType.Name)));
foreach (var c in game.GetExcelSheet<ItemSearchCategory>(Language.Japanese).Where(c=>c.RowId>0).Take(18)) Console.WriteLine($"Category {c.RowId}: {c.Name}");
Console.WriteLine("Example base NPC sell price: " + game.GetExcelSheet<Item>(Language.Japanese).GetRow(5333).PriceLow);
foreach (var c in game.GetExcelSheet<ItemSearchCategory>(Language.Japanese).Where(c=>c.RowId>0).Take(36)) Console.WriteLine($"{c.RowId}: {c.Name} parent={c.Category}, order={c.Order}");
Console.WriteLine("ENpcBase: " + string.Join(", ", typeof(ENpcBase).GetProperties().Select(x=>x.Name+":"+x.PropertyType.Name)));
foreach(var a in game.GetExcelSheet<Addon>(Language.English).Where(x=>x.Text.ToString() is "Sell" or "Sell Item" or "Sell items")) Console.WriteLine($"SELL LABEL {a.RowId}: {a.Text}");
foreach(var npc in game.GetExcelSheet<ENpcResident>(Language.English).Where(x=>x.Singular.ToString() is "Unsynrael" or "Syneyhil")) {
 Console.WriteLine($"VENDOR {npc.RowId}: {npc.Singular}");
 var b=game.GetExcelSheet<ENpcBase>().GetRow(npc.RowId);
 foreach(var d in b.ENpcData) Console.WriteLine($" data {d.RowId:X}");
}
