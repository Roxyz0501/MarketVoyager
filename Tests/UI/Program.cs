using System.Reflection;
using System.Runtime.InteropServices;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Command;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Lumina;
using MarketVoyager;
unsafe class Program
{
 static T Stub<T>() where T:class => DispatchProxy.Create<T,Dummy>();
 static void Main(string[] args)
 {
  using var game = new GameData(Path.Combine(args[0],"sqpack")); Dummy.Game=game;
  string output=Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
  ImGui.CreateContext(); var io=ImGui.GetIO(); io.DisplaySize=new(1440,900); io.DeltaTime=1f/60; io.IniFilename=null;
  int checks=0; void Check(bool ok,string label) { checks++; if(!ok) throw new Exception(label); }
  using(var plugin = new Plugin(Stub<IDalamudPluginInterface>(),Stub<ICommandManager>(),Stub<IFramework>(),Stub<IMarketBoard>(),new DataStub(game),Stub<IGameGui>(),Stub<IObjectTable>(),Stub<IPlayerState>(),Stub<IClientState>(),Stub<IPluginLog>()))
  {
   var type=typeof(Plugin); void Invoke(string name)=>type.GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(plugin,null);
   type.GetField("open",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(plugin,true);
   io.Fonts.Build();
   var texts=type.Assembly.GetType("MarketVoyager.Texts")!;
   string text=(string)texts.GetProperty("FontText",BindingFlags.Public|BindingFlags.Static)!.GetValue(null)!;
   foreach(char c in text.Distinct().Where(c=>!char.IsControl(c))) Check(Dummy.Fonts[0].FindGlyphNoFallback(c)!=null,$"Missing glyph U+{(int)c:x4}");
   var textures=new Dictionary<ulong,(nint,int,int)>();
   for(int i=0;i<io.Fonts.Textures.Size;i++) { byte* pixels; int w=0,h=0,bpp=0; io.Fonts.GetTexDataAsRGBA32(i,&pixels,&w,&h,&bpp); io.Fonts.SetTexID(i,new ImTextureID((ulong)i+1)); textures[(ulong)i+1]=((nint)pixels,w,h); }
   void Frame() { ImGui.NewFrame(); Invoke("Draw"); ImGui.Render(); }
   Dummy.Config.Lists[0].Items = game.GetExcelSheet<Lumina.Excel.Sheets.Item>()!.Where(x => x.ItemSearchCategory.RowId != 0 && x.PriceLow >= 10).Take(45)
    .Select((x,i) => new BuyRule { ItemId=x.RowId, UnitLimit=5000+i*100, QuantityLimit=20, Purchased=i%5, Quality=x.CanBeHq ? Quality.Any : Quality.Nq, IncludeTax=i%2==0 }).ToList();
   var codes=new[]{"ja","en","de","fr","ko","zh-Hans","zh-Hant"};
   foreach(var code in codes) {
    Dummy.Config.Language=code; Frame(); Frame(); Check(ImGui.GetDrawData().TotalVtxCount>0,"Draw "+code);
    Renderer.Render(ImGui.GetDrawData(),textures,Path.Combine(output,code+"-lists.bmp"));
    ImGui.NewFrame(); ImGui.SetNextWindowSize(new(850,650)); ImGui.Begin("Settings QA"); using(var font=Dummy.Push()) Invoke("DrawSettings"); ImGui.End(); ImGui.Render();
    Renderer.Render(ImGui.GetDrawData(),textures,Path.Combine(output,code+"-settings.bmp"));
    foreach(var panel in new[]{"DrawCatalogue","DrawRegistered"}) {
     for(int frame=0;frame<2;frame++) {
      ImGui.NewFrame(); ImGui.SetNextWindowSize(new(1300,850)); ImGui.Begin(panel+" QA");
      using(var font=Dummy.Push()) type.GetMethod(panel,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(plugin,[Dummy.Config.Lists[0]]);
      ImGui.End(); ImGui.Render();
     }
     Check(ImGui.GetDrawData().TotalVtxCount>0,panel+" "+code);
     Renderer.Render(ImGui.GetDrawData(),textures,Path.Combine(output,code+"-"+panel+".bmp"));
    }

   }
   Dummy.Config.Language="ja";
   var runner=type.GetField("runner",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(plugin)!;
   var runnerType=runner.GetType(); var state=runnerType.GetProperty("State")!;
   state.SetValue(runner,Enum.Parse(state.PropertyType,"Search"));
   runnerType.GetField("deadline",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(runner,DateTime.UtcNow.AddSeconds(30));
   runnerType.GetMethod("TogglePause")!.Invoke(runner,null);
   runnerType.GetMethod("Tick")!.Invoke(runner,null);
   Check((bool)runnerType.GetProperty("IsPaused")!.GetValue(runner)!,"production runner pauses at a safe boundary before native calls");
   Frame(); Frame(); Renderer.Render(ImGui.GetDrawData(),textures,Path.Combine(output,"ja-paused.bmp"));
   runnerType.GetMethod("TogglePause")!.Invoke(runner,null);
   Check(!(bool)runnerType.GetProperty("IsPaused")!.GetValue(runner)!,"production runner resume clears pause");
   runnerType.GetMethod("Stop")!.Invoke(runner,[Enum.Parse(state.PropertyType,"Stopped")]);
   runnerType.GetField("rules",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(runner,new List<BuyRule>{new(){ItemId=5333,QuantityLimit=10},new(){ItemId=4,QuantityLimit=10}});
   var retryField=runnerType.GetField("listingRetries",BindingFlags.Instance|BindingFlags.NonPublic)!;
   retryField.SetValue(runner,3);
   runnerType.GetMethod("NextItem",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(runner,[false]);
   Check((int)retryField.GetValue(runner)! == 3,"reopening the same item does not reset retry budget");
   runnerType.GetMethod("NextItem",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(runner,[true]);
   Check((int)retryField.GetValue(runner)! == 0,"advancing to the next item resets retry budget");
   runnerType.GetMethod("Stop")!.Invoke(runner,[Enum.Parse(state.PropertyType,"Stopped")]);
   var selected=(HashSet<Guid>)type.GetField("selectedRules",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(plugin)!;
   selected.UnionWith(Dummy.Config.Lists[0].Items.Take(2).Select(x=>x.Id));
   type.GetField("openNpcBatch",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(plugin,true);
   Frame(); Frame(); Renderer.Render(ImGui.GetDrawData(),textures,Path.Combine(output,"ja-npc-batch.bmp"));
   io.AddKeyEvent(ImGuiKey.Escape,true); Frame(); io.AddKeyEvent(ImGuiKey.Escape,false); Frame();
   selected.Clear(); type.GetField("openBatch",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(plugin,true);
   var beforeBatch=System.Text.Json.JsonSerializer.Serialize(Dummy.Config.Lists[0]);
   Frame(); Frame(); Renderer.Render(ImGui.GetDrawData(),textures,Path.Combine(output,"ja-registered-batch.bmp"));
   Check((bool)type.GetField("batchAll",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(plugin)!,"direct bulk edit defaults to all registered rows when none selected");
   var targets=(IReadOnlySet<Guid>)type.GetMethod("BatchTargets",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(plugin,[Dummy.Config.Lists[0]])!;
   Check(targets.Count==45,"all registered rows includes off-page items");
   Check(beforeBatch==System.Text.Json.JsonSerializer.Serialize(Dummy.Config.Lists[0]),"opening bulk editor preserves saved values and counters");
   Check(!(bool)type.GetField("setPrice",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(plugin)! && !(bool)type.GetField("setQuantity",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(plugin)! && !(bool)type.GetField("setQuality",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(plugin)!,"bulk edit initially changes no fields");
   int saves=Dummy.Saves; for(int i=0;i<20;i++) Frame(); Check(Dummy.Saves==saves,"Unchanged rendering writes zero config files");
   io.AddKeyEvent(ImGuiKey.Escape,true); Frame(); io.AddKeyEvent(ImGuiKey.Escape,false); Frame();
   io.AddMousePosEvent(1100,100); Frame(); io.AddMouseButtonEvent(0,true); Frame(); io.AddMouseButtonEvent(0,false); Frame();
   var pendingRule=Dummy.Config.Lists[0].Items[0];
   Dummy.Config.Pending=new(){RuleId=pendingRule.Id,ItemId=pendingRule.ItemId,Quantity=5,InventoryBefore=10};
   Check(!(bool)type.GetProperty("EditingLocked",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(plugin)!,"stopped pending purchase does not lock settings");
   Check((bool)type.GetMethod("LedgerLocked",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(plugin,[pendingRule.Id])!,"pending row counter reset remains protected");
   Check(!(bool)type.GetMethod("LedgerLocked",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(plugin,[Dummy.Config.Lists[0].Items[1].Id])!,"unrelated row can be deleted/reset");
   foreach(var language in new[]{"ja","en","de","fr","ko","zh-Hans","zh-Hant"}) {
    Dummy.Config.Language=language; Frame(); Frame();
    Renderer.Render(ImGui.GetDrawData(),textures,Path.Combine(output,language+"-stopped-pending.bmp"));
   }
   Check(Dummy.Saves==saves,"pending recovery UI does not save without action");
   var remove = type.GetMethod("RemoveRows",BindingFlags.Instance|BindingFlags.NonPublic)!;
   var ids = new HashSet<Guid> { pendingRule.Id, Dummy.Config.Lists[0].Items[1].Id };
   remove.Invoke(plugin,[Dummy.Config.Lists[0], ids]);
   Check(Dummy.Config.Lists[0].Items.Count==43 && Dummy.Config.DetachedRules.Count==1,"bulk removal deletes visible rows and retains only unresolved ledger");
   Check(ReferenceEquals(Dummy.Config.FindRule(pendingRule.Id),pendingRule) && Dummy.Config.Pending!=null,"deleted pending rule remains resolvable without discarding reservation");
   var restored=System.Text.Json.JsonSerializer.Deserialize<Configuration>(System.Text.Json.JsonSerializer.Serialize(Dummy.Config))!;
   Check(restored.FindRule(pendingRule.Id)!=null && restored.Pending?.RuleId==pendingRule.Id,"deleted pending record survives restart");
   Dummy.Config.PendingSale=new(){RuleId=pendingRule.Id};
   Dummy.Config.Pending=null; Dummy.Config.PruneDetached();
   Check(Dummy.Config.DetachedRules.Count==1,"sale reservation also retains deleted ledger");
   Dummy.Config.PendingSale=null; Dummy.Config.PruneDetached();
   Check(Dummy.Config.DetachedRules.Count==0,"resolved deleted records do not accumulate");
   state.SetValue(runner,Enum.Parse(state.PropertyType,"Listings"));
   int countBefore=Dummy.Config.Lists[0].Items.Count;
   remove.Invoke(plugin,[Dummy.Config.Lists[0],new HashSet<Guid>{Dummy.Config.Lists[0].Items[0].Id}]);
   Check(Dummy.Config.Lists[0].Items.Count==countBefore,"running list cannot be deleted");
   runnerType.GetMethod("Stop")!.Invoke(runner,[Enum.Parse(state.PropertyType,"Stopped")]);
   Dummy.Config.Language="ja";
   var anchorField=type.GetField("listOptionsAnchor",BindingFlags.Instance|BindingFlags.NonPublic)!;
   var positionField=type.GetField("listOptionsPosition",BindingFlags.Instance|BindingFlags.NonPublic)!;
   anchorField.SetValue(plugin,new System.Numerics.Vector2(1100,140));
   System.Numerics.Vector2? stable=null;
   for(int i=0;i<60;i++) {
    ImGui.NewFrame(); ImGui.SetNextWindowPos(new(20,20)); ImGui.Begin("Popup stability QA");
    if(i==0) ImGui.OpenPopup("ListOptionsPopup");
    using(var font=Dummy.Push()) type.GetMethod("DrawListOptionsPopup",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(plugin,[Dummy.Config.Lists[0]]);
    var pos=(System.Numerics.Vector2)positionField.GetValue(plugin)!;
    if(i==3) stable=pos;
    if(i>3) Check(pos==stable,"list settings popup remains fixed on every frame");
    ImGui.End(); ImGui.Render();
   }
   Check(stable is { X: > 0, Y: > 0 },"popup was actually laid out");
   Renderer.Render(ImGui.GetDrawData(),textures,Path.Combine(output,"ja-list-options-stable.bmp"));
   var travel=type.GetField("travel",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(plugin)!;
   var change=travel.GetType().GetMethod("ChangeWorld")!;
   bool refused=false;
   try { change.Invoke(travel,["TestWorld"]); } catch(TargetInvocationException ex) when(ex.InnerException is InvalidOperationException) { refused=true; }
   Check(refused && Dummy.TravelRequests==0,"world request rejected until Lifestream recognizes Limsa aetheryte");
   Dummy.ActiveAetheryte=8; change.Invoke(travel,["TestWorld"]);
   Check(Dummy.TravelRequests==1 && Dummy.TravelArgs is {Length:7} && Equals(Dummy.TravelArgs[1],false) && Equals(Dummy.TravelArgs[6],false),
       "arrival permits only aetheryte world route, with no return teleport");
  }
  Check(Dummy.Subscriptions==0,"Unsubscribes events"); Check(Dummy.Commands.Count==0,"Removes command");
  ImGui.DestroyContext(); foreach(var pin in Dummy.Pins) pin.Free();
  Console.WriteLine($"PASS {checks} UI/glyph/lifecycle checks; 39 background renders.");
 }
}
public unsafe class Dummy:DispatchProxy
{
 public static GameData Game=null!;
 public static Configuration Config=new(){Language="ja",Lists=[new(){Name="材料の購入",AutoSell=true,Items=[new(){ItemId=5333,UnitLimit=5000,QuantityLimit=20,Quality=Quality.Any},new(){ItemId=4,UnitLimit=1000,QuantityLimit=5,Quality=Quality.Hq}]}]};
 public static int Saves,Subscriptions;
 public static readonly Dictionary<string,CommandInfo> Commands=[];
 public static readonly List<ImFontPtr> Fonts=[];
 public static readonly List<GCHandle> Pins=[];
 private ImFontPtr font;
 private string ipcName="";
 public static uint ActiveAetheryte;
 public static int TravelRequests;
 public static object?[]? TravelArgs;
 public static IDisposable Push() { ImGui.PushFont(Fonts[0]); return new Pop(); }
 protected override object? Invoke(MethodInfo? method,object?[]? args)
 {
  if(method==null) return null;
  if(method.Name.StartsWith("add_")) { Subscriptions++; return null; }
  if(method.Name.StartsWith("remove_")) { Subscriptions--; return null; }
  switch(method.Name) {
   case "GetPluginConfig": return Config;
   case "SavePluginConfig": Saves++; return null;
   case "get_UiLanguage": return "en";
   case "GetOrCreateData": return new HashSet<string>();
   case "GetIpcSubscriber":
    var ipc=DispatchProxy.Create(method.ReturnType,typeof(Dummy)); ((Dummy)ipc).ipcName=(string)args![0]!; return ipc;
   case "InvokeFunc":
    if(ipcName=="Lifestream.IsBusy") return false;
    if(ipcName=="Lifestream.GetActiveAetheryte") return ActiveAetheryte;
    if(ipcName=="Lifestream.GetWorldChangeAetheryteByTerritoryType") return (int?)1;
    break;
   case "InvokeAction":
    if(ipcName=="Lifestream.TPAndChangeWorld") { TravelRequests++; TravelArgs=args; }
    return null;
   case "GetExcelSheet": return typeof(GameData).GetMethods().First(x=>x.Name=="GetExcelSheet" && x.IsGenericMethod).MakeGenericMethod(method.GetGenericArguments()).Invoke(Game,[null,null]);
   case "get_InstalledPlugins": return Array.CreateInstance(method.ReturnType.GetGenericArguments()[0],0);
   case "AddHandler": return Commands.TryAdd((string)args![0]!, (CommandInfo)args[1]!);
   case "RemoveHandler": return Commands.Remove((string)args![0]!);
   case "get_UiBuilder": case "get_FontAtlas": return DispatchProxy.Create(method.ReturnType,typeof(Dummy));
   case "get_BuildStep": return FontAtlasBuildStep.PreBuild;
   case "NewDelegateFontHandle":
    var toolkit=DispatchProxy.Create<IFontAtlasBuildToolkitPreBuild,Dummy>(); ((FontAtlasBuildStepDelegate)args![0]!)(toolkit);
    var handle=DispatchProxy.Create<IFontHandle,Dummy>(); ((Dummy)(object)handle).font=Fonts.Last(); return handle;
   case "AddDalamudAssetFont":
    var safe=(SafeFontConfig)args![1]!; var pin=GCHandle.Alloc(safe.GlyphRanges,GCHandleType.Pinned); Pins.Add(pin);
    var native=ImGui.ImFontConfig(); native.FontDataOwnedByAtlas=true; native.OversampleH=1; native.OversampleV=1; native.RasterizerMultiply=1; native.GlyphMaxAdvanceX=float.MaxValue; native.FontNo=safe.FontNo;
    string path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"XIVLauncher/dalamudAssets/dev/UIRes/NotoSansCJK-Regular.ttc");
    var loaded=ImGui.GetIO().Fonts.AddFontFromFileTTF(path,safe.SizePx,native,(ushort*)pin.AddrOfPinnedObject()); native.Destroy(); Fonts.Add(loaded); return loaded;
   case "Push": ImGui.PushFont(font); return new Pop();
  }
  return method.ReturnType==typeof(void)?null:method.ReturnType.IsValueType?Activator.CreateInstance(method.ReturnType):null;
 }
 private sealed class Pop:IDisposable { public void Dispose()=>ImGui.PopFont(); }
}

