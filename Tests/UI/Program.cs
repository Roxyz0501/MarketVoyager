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
   int saves=Dummy.Saves; for(int i=0;i<20;i++) Frame(); Check(Dummy.Saves==saves,"Unchanged rendering writes zero config files");
  }
  Check(Dummy.Subscriptions==0,"Unsubscribes events"); Check(Dummy.Commands.Count==0,"Removes command");
  ImGui.DestroyContext(); foreach(var pin in Dummy.Pins) pin.Free();
  Console.WriteLine($"PASS {checks} UI/glyph/lifecycle checks; 28 background renders.");
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

