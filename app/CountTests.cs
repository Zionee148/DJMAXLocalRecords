using System.Text.Json;
namespace LadderRecords;
static class CountTests
{
 public static void Run(Catalog catalog){
  var results=new List<string>();void Check(bool value,string name){if(!value)throw new Exception(name);results.Add("PASS: "+name);}
  string dir=Path.Combine(AppContext.BaseDirectory,"test-data",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);string path=Path.Combine(dir,"counts.json");var store=new RecordStore(path);var song=catalog.Songs.Single(s=>s.Name=="Fundamental");var chart=new Selection(song.Id,8,"SC","하드",1);
  void Confirm(RecordStore target,decimal accuracy){var session=SessionFixture.For(chart);session.Lock();session.Current=accuracy;session.Begin(target.Active.Id);session.Current=0;session.Confirm(target);}
  Confirm(store,99.25m);Check(store.Get(chart) is {Accuracy:99.25m,PlayCount:1},"First confirmed registration counts once");
  Confirm(store,98);Check(store.Get(chart) is {Accuracy:99.25m,PlayCount:2},"Lower score counts, best retained");
  Confirm(store,99.25m);Check(store.Get(chart) is {Accuracy:99.25m,PlayCount:3},"Equal score counts, best retained");
  Confirm(store,99.8m);Check(store.Get(chart) is {Accuracy:99.8m,PlayCount:4},"Higher score updates best and count");
  Check(new RecordStore(path).Get(chart) is {Accuracy:99.8m,PlayCount:4},"Count survives reopening");
  var cancelled=SessionFixture.For(chart);cancelled.Lock();cancelled.Current=100;cancelled.Begin(store.Active.Id);cancelled.Cancel();Check(store.Get(chart)!.PlayCount==4,"Cancellation excluded");
  try{store.Put(chart,101);throw new Exception("Invalid accuracy accepted");}catch(Exception e)when(e.Message.Contains("정확도")){}Check(store.Get(chart)!.PlayCount==4,"Invalid input excluded");
  store.Put(chart,97,true);Check(store.Get(chart) is {Accuracy:97m,PlayCount:4},"Manual edit preserves count");store.Undo();Check(store.Get(chart) is {Accuracy:99.8m,PlayCount:4},"Undo manual edit preserves count");
  string first=store.Active.Id;store.Create("복사",true);Confirm(store,90);Check(store.Get(chart)!.PlayCount==5,"Profile copy carries count");store.Select(first);Check(store.Get(chart)!.PlayCount==4,"Copied profile increments independently");
  store.Undo();Check(store.Get(chart) is {Accuracy:99.25m,PlayCount:3},"Undo higher registration restores score and count");store.Undo();Check(store.Get(chart)!.PlayCount==2,"Undo equal registration restores count");store.Undo();Check(store.Get(chart)!.PlayCount==1,"Undo lower registration restores count");store.Undo();Check(store.Get(chart)==null,"Undo first registration removes record");
  store.Put(chart,99,true);Check(store.Get(chart) is {PlayCount:0},"New manual record starts at zero");Confirm(store,98);Check(store.Get(chart) is {Accuracy:99m,PlayCount:1},"Confirmed play after manual record starts count");
  var other=chart with{Judge="노말"};store.Put(other,98);Check(store.Get(other)!.PlayCount==2&&store.Get(chart)!.PlayCount==1,"Normal play increases only its own count");
  string legacyPath=Path.Combine(dir,"legacy.json");File.WriteAllText(legacyPath,JsonSerializer.Serialize(new{ActiveId="legacy",Profiles=new[]{new{Id="legacy",Name="기본",Records=new Dictionary<string,object>{{chart.Key,new{Accuracy=99.1m,At=DateTime.UtcNow}}},Undo=Array.Empty<object>()}}}));var legacy=new RecordStore(legacyPath);Check(legacy.Get(chart) is {Accuracy:99.1m,PlayCount:0},"Older records load at zero without score loss");Confirm(legacy,98);Check(legacy.Get(chart) is {Accuracy:99.1m,PlayCount:1},"Older record counts forward from first new confirmation");
  string blockedPath=Path.Combine(dir,"blocked");var blocked=new RecordStore(blockedPath);Directory.CreateDirectory(blockedPath);var pending=SessionFixture.For(chart);pending.Lock();pending.Current=99;pending.Begin(blocked.Active.Id);try{pending.Confirm(blocked);throw new Exception("Blocked write succeeded");}catch(IOException){}catch(UnauthorizedAccessException){}Check(blocked.Get(chart)==null&&pending.Pending!=null,"Failed write rolls back count and retains pending snapshot");
  File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"count-validation.txt"),string.Join("\n",results));
 }
}
