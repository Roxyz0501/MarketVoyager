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
  Dummy.StorageDirectory=Path.Combine(output,"persistence-"+Guid.NewGuid().ToString("N"));
  ImGui.CreateContext(); var io=ImGui.GetIO(); io.DisplaySize=new(1440,900); io.DeltaTime=1f/60; io.IniFilename=null;
  int checks=0; void Check(bool ok,string label) { checks++; if(!ok) throw new Exception(label); }
  using(var plugin = new Plugin(Stub<IDalamudPluginInterface>(),Stub<ICommandManager>(),Stub<IFramework>(),Stub<IMarketBoard>(),new DataStub(game),Stub<IGameGui>(),Stub<IObjectTable>(),Stub<IPlayerState>(),Stub<IClientState>(),Stub<IPluginLog>()))
  {
   var type=typeof(Plugin); Dummy.Storage=type.GetField("persistence",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(plugin); void Invoke(string name)=>type.GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(plugin,null);
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
   var setPhase=runnerType.GetMethod("Set",BindingFlags.Instance|BindingFlags.NonPublic)!;
   var nextField=runnerType.GetField("next",BindingFlags.Instance|BindingFlags.NonPublic)!;
   Check(new Configuration().PurchaseDelayMs==100,"new configurations retain 100ms default");
   Check(System.Text.Json.JsonSerializer.Deserialize<Configuration>("{}")!.PurchaseDelayMs==100,"old configs migrate to 100ms without a setting");
   var delayConfig=new Configuration {PurchaseDelayMs=750};
   Check(System.Text.Json.JsonSerializer.Deserialize<Configuration>(System.Text.Json.JsonSerializer.Serialize(delayConfig))!.PurchaseDelayMs==750,"custom purchase delay survives restart");
   delayConfig.PurchaseDelayMs=int.MinValue; Check(delayConfig.PurchaseDelayMs==50,"invalid low delay is bounded");
   delayConfig.PurchaseDelayMs=int.MaxValue; Check(delayConfig.PurchaseDelayMs==2000,"invalid high delay is bounded");
   foreach(int configured in new[]{50,100,500,2000})
   foreach(var phase in new[]{"Search","Select","Listings","Confirm","Result"}) {
    Dummy.Config.PurchaseDelayMs=configured;
    var began=DateTime.UtcNow;
    setPhase.Invoke(runner,[Enum.Parse(state.PropertyType,phase),20]);
    var scheduled=(DateTime)nextField.GetValue(runner)!;
    Check(scheduled>=began.AddMilliseconds(configured) && scheduled<=DateTime.UtcNow.AddMilliseconds(configured+10),"purchase phases use the current configured delay");
    var limit=(DateTime)runnerType.GetField("deadline",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(runner)!;
    Check(limit>=began.AddSeconds(20),"faster checks preserve transaction timeout");
   }
   Dummy.Config.PurchaseDelayMs=100;
   var receiptRule=new BuyRule {ItemId=42,QuantityLimit=10,Purchased=2};
   var reservation=new Reservation {RuleId=receiptRule.Id,ItemId=42,Quantity=5,InventoryBefore=10,Submitted=true};
   Dummy.Config.Pending=reservation;
   runnerType.GetField("offer",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(runner,new Offer(123,42,100,5,25,false));
   int receiptSaves=Dummy.Saves;
   nextField.SetValue(runner,DateTime.UtcNow.AddHours(1));
   runnerType.GetMethod("Purchased")!.Invoke(runner,[42u,5u]);
   Check(ReferenceEquals(Dummy.Config.Pending,reservation) && receiptRule.Purchased==2 && Dummy.Saves==receiptSaves,
       "early receipt preserves pending transaction until inventory changes, without credit or save");
   Check(state.GetValue(runner)!.ToString()=="Result" && (DateTime)nextField.GetValue(runner)!<=DateTime.UtcNow,
       "receipt wakes inventory check without advancing to another purchase");
   Dummy.Config.Pending=null;
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
   var deleteList=type.GetMethod("DeleteList",BindingFlags.Instance|BindingFlags.NonPublic)!;
   deleteList.Invoke(plugin,[Dummy.Config.Lists[0]]);
   Check(Dummy.Config.Lists.Count==1 && Dummy.Config.Lists[0].Items.Count==countBefore,"whole-list deletion is also blocked while running");
   runnerType.GetMethod("Stop")!.Invoke(runner,[Enum.Parse(state.PropertyType,"Stopped")]);
   Dummy.Config.Language="ja";
   var anchorField=type.GetField("listOptionsAnchor",BindingFlags.Instance|BindingFlags.NonPublic)!;
   var positionField=type.GetField("listOptionsPosition",BindingFlags.Instance|BindingFlags.NonPublic)!;
   anchorField.SetValue(plugin,new System.Numerics.Vector2(1100,140));
   System.Numerics.Vector2? stable=null;
   for(int i=0;i<60;i++) {
    ImGui.NewFrame(); ImGui.SetNextWindowPos(new(20,20)); ImGui.Begin("Popup stability QA");
    if(i==0) ImGui.OpenPopup("ListOptionsPopup");
    if(i==30) type.GetField("confirmListDelete",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(plugin,true);
    using(var font=Dummy.Push()) type.GetMethod("DrawListOptionsPopup",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(plugin,[Dummy.Config.Lists[0]]);
    var pos=(System.Numerics.Vector2)positionField.GetValue(plugin)!;
    if(i==3) stable=pos;
    if(i>3) Check(pos==stable,"list settings popup remains fixed on every frame");
    ImGui.End(); ImGui.Render();
   }
   Check(stable is { X: > 0, Y: > 0 },"popup was actually laid out");
   Renderer.Render(ImGui.GetDrawData(),textures,Path.Combine(output,"ja-list-options-stable.bmp"));
   var doomed=Dummy.Config.Lists[0]; var keep=new ShoppingList{Name="Keep",Items=[new(){ItemId=4,Purchased=7}]};
   var buy=doomed.Items[0]; var sell=doomed.Items[1];
   Dummy.Config.Pending=new(){RuleId=buy.Id,ItemId=buy.ItemId};
   Dummy.Config.PendingSale=new(){RuleId=sell.Id};
   Dummy.Config.Lists.Add(keep); Dummy.Config.Selected=0;
   var keepJson=System.Text.Json.JsonSerializer.Serialize(keep);
   deleteList.Invoke(plugin,[doomed]);
   Check(Dummy.Config.Lists.Count==1 && ReferenceEquals(Dummy.Config.Lists[0],keep) && Dummy.Config.Selected==0,"deleting selected list selects the remaining list");
   Check(keepJson==System.Text.Json.JsonSerializer.Serialize(keep),"deleting a list preserves other list settings and counters");
   Check(Dummy.Config.DetachedRules.Count==2 && Dummy.Config.FindRule(buy.Id)==buy && Dummy.Config.FindRule(sell.Id)==sell,"whole-list deletion retains both unresolved ledgers");
   var savedDeletion=System.Text.Json.JsonSerializer.Deserialize<Configuration>(System.Text.Json.JsonSerializer.Serialize(Dummy.Config))!;
   Check(savedDeletion.Lists.Count==1 && savedDeletion.FindRule(buy.Id)!=null && savedDeletion.FindRule(sell.Id)!=null,"whole-list deletion and pending ledgers survive restart");
   deleteList.Invoke(plugin,[keep]);
   Check(Dummy.Config.Lists.Count==1 && Dummy.Config.Lists[0].Items.Count==0 && Dummy.Config.Selected==0,"deleting the last list creates an empty selectable list");
   Dummy.Config.Pending=null; Dummy.Config.PendingSale=null; Dummy.Config.PruneDetached();
   var travel=type.GetField("travel",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(plugin)!;
   var change=travel.GetType().GetMethod("ChangeWorld")!;
   bool refused=false;
   try { change.Invoke(travel,["TestWorld"]); } catch(TargetInvocationException ex) when(ex.InnerException is InvalidOperationException) { refused=true; }
   Check(refused && Dummy.TravelRequests==0,"world request rejected until Lifestream recognizes Limsa aetheryte");
   Dummy.ActiveAetheryte=8; change.Invoke(travel,["TestWorld"]);
   Check(Dummy.TravelRequests==1 && Dummy.TravelArgs is {Length:7} && Equals(Dummy.TravelArgs[1],false) && Equals(Dummy.TravelArgs[6],false),
       "arrival permits only aetheryte world route, with no return teleport");
   var travelType=travel.GetType();
   var teleport=travelType.GetMethod("TeleportToLimsa")!;
   Check((bool)teleport.Invoke(travel,null)! && Dummy.TeleportArgs is {Length:2} && Equals(Dummy.TeleportArgs[0],(uint)8) &&
       Equals(Dummy.TeleportArgs[1],(byte)0),"startup teleport uses public Lifestream destination 8, sub-index 0");
   Dummy.TravelBusy=true;
   Check(!(bool)teleport.Invoke(travel,null)! && Dummy.TeleportRequests==1,"busy dependency prevents startup teleport");
   Dummy.TravelBusy=false; Dummy.TeleportAccepted=false;
   Check(!(bool)teleport.Invoke(travel,null)!,"teleport rejection is propagated");
   Dummy.TeleportAccepted=true;
   var move=travelType.GetMethod("Move")!; var pollMove=travelType.GetMethod("PollMove")!; var stopMove=travelType.GetMethod("StopMove")!;
   bool MoveCamera()=> (bool)move.Invoke(travel,[new System.Numerics.Vector3(),new System.Numerics.Vector3(1,0,0),true])!;
   Check(MoveCamera() && Dummy.AlignCamera && Dummy.CameraWrites==1,"owned market movement enables camera alignment once");
   Check(!MoveCamera() && Dummy.CameraWrites==1,"pending owned path is not overwritten");
   Check((bool)pollMove.Invoke(travel,null)! && Dummy.MoveRequests==1,"completed path is handed to navigation provider");
   stopMove.Invoke(travel,null);
   Check(!Dummy.AlignCamera && Dummy.CameraWrites==2,"stopping owned movement restores prior camera configuration");
   Dummy.AlignCamera=true;
   Check(MoveCamera(),"already aligned movement accepted"); pollMove.Invoke(travel,null); stopMove.Invoke(travel,null);
   Check(Dummy.AlignCamera && Dummy.CameraWrites==2,"user's enabled camera option is left enabled without writes");
   Dummy.AlignCamera=false; Dummy.NavBusy=true;
   Check(!MoveCamera() && Dummy.CameraWrites==2 && !Dummy.AlignCamera,"another plugin's active movement is not changed");
   Dummy.NavBusy=false;
   foreach(int failure in new[]{1,2,3}) {
    Dummy.PathFailure=failure; bool pathFailed=false;
    try { MoveCamera(); pollMove.Invoke(travel,null); } catch(TargetInvocationException ex) when(ex.InnerException is InvalidOperationException) { pathFailed=true; }
    Check(pathFailed && !Dummy.AlignCamera,"path error restores camera setting "+failure);
   }
   Dummy.PathFailure=0; MoveCamera(); pollMove.Invoke(travel,null); Dummy.StopFailure=true;
   bool stopFailed=false;
   try { stopMove.Invoke(travel,null); } catch(TargetInvocationException ex) when(ex.InnerException is InvalidOperationException) { stopFailed=true; }
   Check(stopFailed && !Dummy.AlignCamera,"camera restored even if movement stop IPC throws");
   Dummy.StopFailure=false;
   var resumeRows=game.GetExcelSheet<Lumina.Excel.Sheets.Item>()!.Where(x=>x.ItemSearchCategory.RowId!=0 && x.CanBeHq).Take(4).ToArray();
   var edited=new BuyRule {ItemId=resumeRows[0].RowId,UnitLimit=1000,QuantityLimit=20,Purchased=3};
   var removed=new BuyRule {ItemId=resumeRows[1].RowId,QuantityLimit=10};
   var capped=new BuyRule {ItemId=resumeRows[2].RowId,QuantityLimit=10,Purchased=4};
   var editingList=new ShoppingList {Items=[edited,removed,capped]};
   var replacement=new ShoppingList {Items=[new(){ItemId=resumeRows[3].RowId,QuantityLimit=10}]};
   Dummy.Config.Lists=[editingList,replacement]; Dummy.Config.Selected=0;
   var loadRules=runnerType.GetMethod("LoadRules",BindingFlags.Instance|BindingFlags.NonPublic)!;
   var ruleField=runnerType.GetField("rules",BindingFlags.Instance|BindingFlags.NonPublic)!;
   var indexField=runnerType.GetField("index",BindingFlags.Instance|BindingFlags.NonPublic)!;
   var editingLock=type.GetProperty("EditingLocked",BindingFlags.Instance|BindingFlags.NonPublic)!;
   void PauseAt(string phase) {
    runnerType.GetMethod("Stop")!.Invoke(runner,[Enum.Parse(state.PropertyType,"Stopped")]);
    state.SetValue(runner,Enum.Parse(state.PropertyType,phase));
    runnerType.GetMethod("TogglePause")!.Invoke(runner,null);
    Check((bool)editingLock.GetValue(plugin)!,"requesting pause alone cannot unlock editing");
    runnerType.GetMethod("Tick")!.Invoke(runner,null);
    Check((bool)runnerType.GetProperty("IsPaused")!.GetValue(runner)! && !(bool)editingLock.GetValue(plugin)!,"safe pause unlocks editing");
   }
   loadRules.Invoke(runner,null); indexField.SetValue(runner,2); PauseAt("Listings");
   edited.UnitLimit=200; edited.QuantityLimit=30; edited.Quality=Quality.Hq; edited.IncludeTax=true;
   capped.QuantityLimit=4;
   remove.Invoke(plugin,[editingList,new HashSet<Guid>{removed.Id}]);
   var added=new BuyRule {ItemId=resumeRows[3].RowId,QuantityLimit=10};
   editingList.Items.Add(added);
   runnerType.GetMethod("TogglePause")!.Invoke(runner,null);
   var resumed=(List<BuyRule>)ruleField.GetValue(runner)!;
   Check(resumed.SequenceEqual(new[]{edited,added}) && (int)indexField.GetValue(runner)! == 0,"resume rebuilds added/removed/capped rules and starts from beginning");
   Check(edited.Purchased==3 && capped.Purchased==4,"resume preserves purchased counts");
   Check(Rules.Eligible(resumed[0],new Offer(1,edited.ItemId,190,2,20,true),true) && !Rules.Eligible(resumed[0],new Offer(2,edited.ItemId,190,2,21,true),true) && !Rules.Eligible(resumed[0],new Offer(3,edited.ItemId,190,2,0,false),true),"edited tax-inclusive cap and quality govern resumed eligibility");
   Check((bool)runnerType.GetField("restartAfterResume",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(runner)! && (bool)editingLock.GetValue(plugin)!,"resume schedules a fresh search and relocks editing");
   PauseAt("Search"); deleteList.Invoke(plugin,[editingList]);
   runnerType.GetMethod("TogglePause")!.Invoke(runner,null);
   Check(ReferenceEquals(runnerType.GetField("activeList",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(runner),replacement) && ((List<BuyRule>)ruleField.GetValue(runner)!).SequenceEqual(replacement.Items),"deleting the paused active list resumes the newly selected list");
   PauseAt("Select"); deleteList.Invoke(plugin,[replacement]);
   runnerType.GetMethod("TogglePause")!.Invoke(runner,null);
   Check(state.GetValue(runner)!.ToString()=="Done" && !(bool)runnerType.GetProperty("Running")!.GetValue(runner)!,"empty replacement ends without buying stale rules");
   Dummy.Config.Lists[0].Items=[new(){ItemId=resumeRows[0].RowId,QuantityLimit=10}];
   PauseAt("Search");
   Dummy.Config.Lists[0].Items.Add(new(){ItemId=resumeRows[0].RowId,QuantityLimit=10});
   runnerType.GetMethod("TogglePause")!.Invoke(runner,null);
   Check(state.GetValue(runner)!.ToString()=="Error","resume rejects overlapping edited rules before any purchase");
   Dummy.Config.Lists=[new(){Name="履歴テスト",SkipRecentWorlds=false}]; Dummy.Config.Selected=0;
   var history=Dummy.Config.Lists[0]; var time=DateTimeOffset.UtcNow.AddMinutes(-5);
   var realWorld=game.GetExcelSheet<Lumina.Excel.Sheets.World>()!.First(x=>x.IsPublic);
   runnerType.GetField("activeList",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(runner,history);
   runnerType.GetField("world",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(runner,(realWorld.RowId,realWorld.Name.ToString()));
   var recordCheck=runnerType.GetMethod("RecordPriceCheck",BindingFlags.Instance|BindingFlags.NonPublic)!;
   int historySaves=Dummy.Saves;
   recordCheck.Invoke(runner,null);
   Check(Dummy.Saves==historySaves && history.WorldHistory.ContainsKey(realWorld.RowId),"price check marks dirty without immediate write");
   for(int i=0;i<20;i++) recordCheck.Invoke(runner,null);
   Check(Dummy.Saves==historySaves,"subsequent price checks coalesce without per-item disk writes");
   runnerType.GetMethod("Stop")!.Invoke(runner,[Enum.Parse(state.PropertyType,"Stopped")]);
   Check(Dummy.Saves==historySaves,"tour stop does not bypass normal 60-second save cadence");
   type.GetMethod("OnLogout",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(plugin,null);
   Check(Dummy.Saves>historySaves,"logout flushes latest history and settings");
   historySaves=Dummy.Saves;
   runnerType.GetMethod("Stop")!.Invoke(runner,[Enum.Parse(state.PropertyType,"Stopped")]);
   Check(Dummy.Saves==historySaves,"unchanged stop does not save history again");
   history.WorldHistory[realWorld.RowId]=new(){LastCheckedUtc=time,LastPurchasedUtc=time.AddMinutes(1)};
   var historyRoundtrip=System.Text.Json.JsonSerializer.Deserialize<Configuration>(System.Text.Json.JsonSerializer.Serialize(Dummy.Config))!;
   Check(!historyRoundtrip.Lists[0].SkipRecentWorlds && historyRoundtrip.Lists[0].WorldHistory[realWorld.RowId].LastCheckedUtc==time,
       "per-list exclusion off and price history persist");
   foreach(var code in codes) {
    Dummy.Config.Language=code;
    for(int frame=0;frame<2;frame++) {
     ImGui.NewFrame(); ImGui.SetNextWindowSize(new(520,730)); ImGui.Begin("World history QA");
     using(var font=Dummy.Push()) {
      // Open the production history section with the same stable ID.
      ImGui.GetStateStorage().SetInt(ImGui.GetID((string)texts.GetMethod("Get")!.Invoke(null,["WorldHistory",code])!+"###WorldHistory"),1);
      type.GetMethod("DrawListOptions",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(plugin,[history]);
     }
     ImGui.End(); ImGui.Render();
    }
    Renderer.Render(ImGui.GetDrawData(),textures,Path.Combine(output,code+"-world-history.bmp"));
    Check(ImGui.GetDrawData().TotalVtxCount>0,"world history renders "+code);
   }
   var storage=Dummy.Storage!; var storageType=storage.GetType();
   storageType.GetProperty("Blocked")!.SetValue(storage,true);
   storageType.GetProperty("Status")!.SetValue(storage,"SaveFailedFallback");
   var openField=type.GetField("open",BindingFlags.Instance|BindingFlags.NonPublic)!;
   openField.SetValue(plugin,false);
   type.GetMethod("Update",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(plugin,[Stub<IFramework>()]);
   Check((bool)openField.GetValue(plugin)!,"storage fault opens notification once");
   openField.SetValue(plugin,false);
   type.GetMethod("Update",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(plugin,[Stub<IFramework>()]);
   Check(!(bool)openField.GetValue(plugin)!,"fault notification does not reopen every frame");
   foreach(var code in codes) {
    Dummy.Config.Language=code;
    for(int frame=0;frame<2;frame++) {
     ImGui.NewFrame();ImGui.SetNextWindowSize(new(850,500));ImGui.Begin("Persistence recovery QA");
     using(var font=Dummy.Push()) Invoke("DrawPersistence");
     ImGui.End();ImGui.Render();
    }
    Renderer.Render(ImGui.GetDrawData(),textures,Path.Combine(output,code+"-save-recovery.bmp"));
    Check(ImGui.GetDrawData().TotalVtxCount>0,"save recovery renders "+code);
   }
   storageType.GetProperty("Blocked")!.SetValue(storage,false);
   storageType.GetProperty("Status")!.SetValue(storage,"");
  }
  Check(Dummy.Subscriptions==0,"Unsubscribes events"); Check(Dummy.Commands.Count==0,"Removes command");
  ImGui.DestroyContext(); foreach(var pin in Dummy.Pins) pin.Free();
  Console.WriteLine($"PASS {checks} UI/glyph/lifecycle checks; 53 background renders.");
 }
}
public unsafe class Dummy:DispatchProxy
{
 public static GameData Game=null!;
 public static Configuration Config=new(){Language="ja",Lists=[new(){Name="材料の購入",AutoSell=true,Items=[new(){ItemId=5333,UnitLimit=5000,QuantityLimit=20,Quality=Quality.Any},new(){ItemId=4,UnitLimit=1000,QuantityLimit=5,Quality=Quality.Hq}]}]};
 public static int Subscriptions;
 public static string StorageDirectory="";
 public static object? Storage;
 public static int Saves=>Storage==null?0:(int)(long)Storage.GetType().GetProperty("Writes")!.GetValue(Storage)!;
 public static readonly Dictionary<string,CommandInfo> Commands=[];
 public static readonly List<ImFontPtr> Fonts=[];
 public static readonly List<GCHandle> Pins=[];
 private ImFontPtr font;
 private string ipcName="";
 public static uint ActiveAetheryte;
 public static int TravelRequests;
 public static object?[]? TravelArgs;
 public static bool TravelBusy, TeleportAccepted=true, AlignCamera, NavBusy, StopFailure;
 public static int TeleportRequests, CameraWrites, MoveRequests, PathFailure;
 public static object?[]? TeleportArgs;
 public static IDisposable Push() { ImGui.PushFont(Fonts[0]); return new Pop(); }
 protected override object? Invoke(MethodInfo? method,object?[]? args)
 {
  if(method==null) return null;
  if(method.Name.StartsWith("add_")) { Subscriptions++; return null; }
  if(method.Name.StartsWith("remove_")) { Subscriptions--; return null; }
  switch(method.Name) {
   case "GetPluginConfig": return Config;
   case "SavePluginConfig": throw new Exception("Legacy immediate saving is forbidden");
   case "GetPluginConfigDirectory": return StorageDirectory;
   case "get_UiLanguage": return "en";
   case "GetOrCreateData": return new HashSet<string>();
   case "GetIpcSubscriber":
    var ipc=DispatchProxy.Create(method.ReturnType,typeof(Dummy)); ((Dummy)ipc).ipcName=(string)args![0]!; return ipc;
   case "InvokeFunc":
    if(ipcName=="Lifestream.IsBusy") return TravelBusy;
    if(ipcName=="Lifestream.GetActiveAetheryte") return ActiveAetheryte;
    if(ipcName=="Lifestream.GetWorldChangeAetheryteByTerritoryType") return (int?)1;
    if(ipcName=="Lifestream.Teleport") { TeleportRequests++; TeleportArgs=args; return TeleportAccepted; }
    if(ipcName=="vnavmesh.Path.IsRunning") return NavBusy;
    if(ipcName=="vnavmesh.Path.GetAlignCamera") return AlignCamera;
    if(ipcName=="vnavmesh.Nav.PathfindCancelable") {
     if(PathFailure==1) throw new InvalidOperationException("Path refused");
     if(PathFailure==2) return Task.FromException<List<System.Numerics.Vector3>>(new InvalidOperationException("Path failed"));
     return Task.FromResult(PathFailure==3 ? new List<System.Numerics.Vector3>() : new List<System.Numerics.Vector3>{new(1,0,0)});
    }
    break;
   case "InvokeAction":
    if(ipcName=="Lifestream.TPAndChangeWorld") { TravelRequests++; TravelArgs=args; }
    if(ipcName=="vnavmesh.Path.SetAlignCamera") { AlignCamera=(bool)args![0]!; CameraWrites++; }
    if(ipcName=="vnavmesh.Path.MoveTo") MoveRequests++;
    if(ipcName=="vnavmesh.Path.Stop" && StopFailure) throw new InvalidOperationException("Stop failed");
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

