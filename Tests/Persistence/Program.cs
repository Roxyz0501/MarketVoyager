using MarketVoyager;
using System.Text.Json;

int checks=0;
void Check(bool ok,string message) { checks++; if(!ok) throw new Exception(message); }
var root=Path.GetFullPath(args.Length>0?args[0]:"artifacts/persistence-tests");
root=Path.Combine(root,Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
var now=new DateTimeOffset(2026,10,22,23,59,30,TimeSpan.FromHours(9));
var cfg=new Configuration {Language="ja"};
cfg.Lists[0].Items.Add(new(){ItemId=42,QuantityLimit=20,Purchased=3,Stock=[new(){Character=7,Remaining=3}]});
int errors=0;
var dir=Path.Combine(root,"normal");
using(var p=new Persistence(dir,()=>cfg,_=>errors++,()=>now))
{
 p.Flush(); Check(p.Writes==2,"initial migration writes separate settings/safety once");
 long writes=p.Writes,bytes=p.LogicalBytes;
 for(int i=0;i<10;i++){now=now.AddSeconds(60);p.Tick();}
 Check(p.Writes==writes && p.LogicalBytes==bytes,"unchanged ten periods: zero writes and bytes");
 for(int i=0;i<600;i++){cfg.PurchaseDelayMs=100+i%100;p.Changed();p.Tick();}
 Check(p.Writes==writes,"600 edits do not write within period");
 now=now.AddSeconds(60);p.Tick();
 Check(p.Writes==writes+1,"600 edits coalesce to one settings write, safety unchanged");
 cfg.PurchaseDelayMs=50;p.Changed();cfg.PurchaseDelayMs=199;p.Changed();
 now=now.AddSeconds(60);p.Tick();Check(p.Writes==writes+1,"reverted edit has zero writes");
 for(int i=0;i<5;i++){cfg.Language=i%2==0?"en":"ja";p.Changed();p.Flush();}
 Check(Directory.GetFiles(dir,"settings-backup-*.json").Length==3,"exactly three previous valid generations");
 var rule=cfg.Lists[0].Items[0];
 cfg.Pending=new(){RuleId=rule.Id,ListId=cfg.Lists[0].Id,ItemId=42,WorldId=1,CharacterId=7,Quantity=2,InventoryBefore=3,Submitted=false};
 writes=p.Writes;p.Durable();Check(p.Writes==writes+1,"reservation writes only safety checkpoint");
 cfg.PurchaseDelayMs=350;p.Changed();
 var normalBefore=File.ReadAllBytes(Path.Combine(dir,"settings-v2.json"));
 cfg.Pending.Submitted=true;p.Durable();
 Check(File.ReadAllBytes(Path.Combine(dir,"settings-v2.json")).SequenceEqual(normalBefore),"purchase checkpoint does not save ordinary edits before 60 seconds");
 using(var reload=new Persistence(dir,()=>throw new Exception("must not read legacy"),_=>errors++,()=>now))
  Check(reload.Config.Pending?.Submitted==true && reload.Config.Lists[0].Items[0].Purchased==3,"pending reservation and count survive restart");
 cfg.Pending=null;rule.Purchased=5;rule.Stock[0].Remaining=5;p.Durable();
 cfg.Lists[0].WorldHistory[1]=new(){LastCheckedUtc=now};p.Changed();
 now=now.AddDays(1);p.Tick();
 cfg.Lists[0].WorldHistory[2]=new(){LastCheckedUtc=now.AddDays(-1)};p.Changed();p.Flush();
 using(var reload=new Persistence(dir,()=>new(),_=>errors++,()=>now))
  Check(reload.Config.Lists[0].WorldHistory.Count==2 && reload.Config.Lists[0].Items[0].Purchased==5,"midnight and delayed timestamp preserved in bounded latest-state history");
 cfg.Language="fr";p.Changed();writes=p.Writes;p.Dispose();
 Check(p.Writes==writes+1,"normal exit flushes settings once");
 p.Changed();now=now.AddDays(1);p.Tick();p.Flush();p.Dispose();Check(p.Writes==writes+1,"no writes after shutdown");
 Console.WriteLine($"normal: {p.Writes} committed files, {p.LogicalBytes} logical payload bytes");
}
// Corrupt primary settings recovers newest valid generation; independent current counts remain.
File.WriteAllText(Path.Combine(dir,"settings-v2.json"),"{broken");
using(var recovered=new Persistence(dir,()=>throw new Exception("no legacy rollback"),_=>errors++,()=>now))
{
 Check(recovered.Status=="BackupRecovered" && !recovered.Blocked,"corrupt primary loads a valid backup with notification");
 Check(recovered.Config.Lists[0].Items[0].Purchased==5 && recovered.Config.Pending==null,"backup cannot roll back transaction state");
 recovered.Flush();Check(Directory.GetFiles(dir,"settings-corrupt-*.json").Length==1,"corrupt primary retained outside normal generations");
}
foreach(var stage in new[]{"write","replace"})
{
 int attempts=0;bool fail=false;var failureDir=Path.Combine(root,"fallback-"+stage);var c=new Configuration{Language="ja"};
 using(var p=new Persistence(failureDir,()=>c,_=>errors++,()=>now,(path,phase)=>{
  if(fail && path.EndsWith("settings-v2.json") && phase==stage){attempts++;throw new IOException("injected "+stage);}
 }))
 {
  p.Flush();fail=true;c.Language="fr";p.Changed();now=now.AddSeconds(60);p.Tick();
  Check(p.Blocked && p.Status=="SaveFailedFallback" && p.RecoveryPath!=null && File.Exists(p.RecoveryPath),"single alternate snapshot succeeds after "+stage+" failure");
  long writes=p.Writes;
  for(int i=0;i<10000;i++){now=now.AddMilliseconds(17);p.Tick();}
  p.Flush();Check(attempts==1 && p.Writes==writes,"10,000 frames and flush do not retry "+stage);
  fail=false;Check(p.Retry() && !p.Blocked,"explicit retry commits latest snapshot");
  using var reread=new Persistence(failureDir,()=>new(),_=>errors++,()=>now);
  Check(reread.Config.Language=="fr" && reread.Status=="RecoveryAvailable","recovery candidate remains available after restart");
 }
}
{
 bool fail=false;int attempts=0;var c=new Configuration();
 using var p=new Persistence(Path.Combine(root,"both-fail"),()=>c,_=>errors++,()=>now,(path,phase)=>{
  if(fail && phase=="write"){attempts++;throw new IOException("disk full");}
 });
 p.Flush();fail=true;c.Language="de";p.Changed();now=now.AddSeconds(60);p.Tick();
 Check(p.Blocked && p.Status=="SaveFailedBoth" && attempts==2,"primary plus exactly one alternate failure");
 p.Dispose();p.Dispose();Check(attempts==2,"blocked exit does not retry");
}
{
 bool fail=false;var c=new Configuration();var r=new BuyRule{ItemId=44,QuantityLimit=10};c.Lists[0].Items.Add(r);
 var safetyDir=Path.Combine(root,"safety-fail");
 using var p=new Persistence(safetyDir,()=>c,_=>errors++,()=>now,(path,phase)=>{
  if(fail && path.EndsWith("safety-v2.json") && phase=="replace")throw new IOException("locked safety");
 });
 p.Flush();fail=true;c.Pending=new(){RuleId=r.Id,ItemId=44,Quantity=2,Submitted=false};
 bool sent=false;try{p.Durable();sent=true;}catch(IOException){}
 Check(!sent && p.Blocked && c.Pending!=null,"failed reservation durability blocks purchase dispatch and retains pending");
 Check(!p.RestoreCandidate(),"in-process fallback cannot discard unresolved in-memory state");
 fail=false;Check(p.Retry(),"explicit durable retry can save pending without sending purchase");
 File.WriteAllText(Path.Combine(safetyDir,"safety-v2.json"),"{}");
 using var broken=new Persistence(safetyDir,()=>new(),_=>errors++,()=>now);
 Check(broken.Blocked && broken.Status=="SafetyUnreadable" && !broken.Retry(),"invalid safety state cannot silently reset counters or enable auto-buy");
}
{
 var c=new Configuration();Persistence? p=null;bool inject=false;
 p=new Persistence(Path.Combine(root,"reentrant"),()=>c,_=>errors++,()=>now,(path,stage)=>{
  if(inject && path.EndsWith("settings-v2.json") && stage=="replace"){
   inject=false;c.Language="ko";p!.Changed();Check(!p.Flush(),"nested save cannot start a second writer");
  }
 });
 using(p){p.Flush();c.Language="fr";p.Changed();inject=true;p.Flush();long writes=p.Writes;p.Flush();Check(p.Writes==writes+1,"edit during save is not lost");}
}
{
 bool fail=false;var c=new Configuration{Language="ja"};var candidateDir=Path.Combine(root,"candidate");
 using(var p=new Persistence(candidateDir,()=>c,_=>errors++,()=>now,(path,stage)=>{
  if(fail && path.EndsWith("settings-v2.json") && stage=="replace")throw new IOException("rename denied");
 }))
 {
  p.Flush();fail=true;c.Language="fr";p.Changed();p.Flush();
  Check(p.PayloadBytesWritten>p.LogicalBytes,"failed temporary payload counted separately from committed bytes");
 }
 using(var p=new Persistence(candidateDir,()=>new(),_=>errors++,()=>now))
 {
  Check(p.Config.Language=="ja" && p.Status=="RecoveryAvailable","valid main not silently overwritten by alternate snapshot");
  Check(p.RestoreCandidate() && p.Config.Language=="fr","next-start explicit recovery validates and applies alternate settings");
 }
}
{
 var c=new Configuration();var lockDir=Path.Combine(root,"file-lock");
 using var p=new Persistence(lockDir,()=>c,_=>errors++,()=>now);p.Flush();
 using(var held=new FileStream(Path.Combine(lockDir,"settings-v2.json"),FileMode.Open,FileAccess.ReadWrite,FileShare.None))
 {
  c.Language="de";p.Changed();p.Flush();
  Check(p.Blocked && p.Status=="SaveFailedFallback","real sharing lock preserves primary and writes one alternate");
 }
 Check(p.Retry(),"explicit save succeeds after real lock released");
}
{
 var c=new Configuration();using var p=new Persistence(Path.Combine(root,"concurrent"),()=>c,_=>errors++,()=>now);
 p.Flush();c.Language="zh-Hant";p.Changed();long before=p.Writes;
 Task.WaitAll(Enumerable.Range(0,10).Select(_=>Task.Run(()=>p.Flush())).ToArray());
 Check(p.Writes==before+1,"ten concurrent flush requests use one writer and one changed write");
}
{
 var c=new Configuration();c.Lists[0].Items.Add(new(){ItemId=77,Purchased=4,QuantityLimit=8});
 var deletionDir=Path.Combine(root,"deletion");
 using(var p=new Persistence(deletionDir,()=>c,_=>errors++,()=>now)){
  p.Flush();c.Lists[0].Items.Clear();p.Changed();p.Flush();
 }
 File.WriteAllText(Path.Combine(deletionDir,"settings-v2.json"),"invalid");
 using var restored=new Persistence(deletionDir,()=>new(),_=>errors++,()=>now);
 Check(!restored.Blocked && restored.Config.Lists[0].Items.Count==0,"backup settings cannot resurrect a deleted purchase rule");
}
Console.WriteLine($"PASS {checks} persistence checks; {errors} expected fault reports; synthetic files only: {root}");
