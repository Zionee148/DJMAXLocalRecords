using System.Text.Json;
namespace LadderRecords;
// 판의 수를 검사한다. 옛 '플레이 카운트'는 형식 2 에서 없어졌고, 옛 창의 그 칸에는 그 판정에 보이는 판의 수가 나온다(RecordStore.View).
// 수동으로 넣는 것은 기록을 통째로 바꾸는 것이 아니라 한 판을 더하는 것이다. 형식 1 의 카운트는 넘기지 않는다(옛 기록은 한 줄이다)
static class CountTests
{
 public static void Run(Catalog catalog){
  var results=new List<string>();void Check(bool value,string name){if(!value)throw new Exception(name);results.Add("PASS: "+name);}
  string dir=Path.Combine(AppContext.BaseDirectory,"test-data",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);string path=Path.Combine(dir,"counts.json");var store=new RecordStore(path);var song=catalog.Songs.Single(s=>s.Name=="Fundamental");var chart=new Selection(song.Id,8,"SC","하드",1);
  void Confirm(RecordStore target,decimal accuracy){var session=SessionFixture.For(chart);session.Lock();session.Current=accuracy;session.Begin(target.Active.Id);session.Current=0;session.Confirm(target);}
  Confirm(store,99.25m);Check(store.Get(chart) is {Accuracy:99.25m,PlayCount:1},"First confirmed registration counts once");
  Confirm(store,98);Check(store.Get(chart) is {Accuracy:99.25m,PlayCount:2}&&store.Active.Plays[^1] is {Score:980000,Source:PlaySource.Play}&&store.Active.Plays[^1].Key==chart.Key,"Lower score counts and is kept with its own score, best retained");
  Confirm(store,99.25m);Check(store.Get(chart) is {Accuracy:99.25m,PlayCount:3},"Equal score counts, best retained");
  Confirm(store,99.8m);Check(store.Get(chart) is {Accuracy:99.8m,PlayCount:4},"Higher score updates best and count");
  Check(new RecordStore(path).Get(chart) is {Accuracy:99.8m,PlayCount:4},"Count survives reopening");
  var cancelled=SessionFixture.For(chart);cancelled.Lock();cancelled.Current=100;cancelled.Begin(store.Active.Id);cancelled.Cancel();Check(store.Get(chart)!.PlayCount==4,"Cancellation excluded");
  try{store.Put(chart,101);throw new Exception("Invalid accuracy accepted");}catch(Exception e)when(e.Message.Contains("정확도")){}Check(store.Get(chart)!.PlayCount==4,"Invalid input excluded");
  store.Put(chart,97,true);Check(store.Get(chart) is {Accuracy:99.8m,PlayCount:5}&&store.Active.Plays[^1] is {Score:970000,Source:PlaySource.Manual}&&store.Active.Plays[^1].Key==chart.Key,"A manual play is one more row with the score that was entered; the best stays");store.Undo();Check(store.Get(chart) is {Accuracy:99.8m,PlayCount:4},"Undo of the manual play removes that row");
  string first=store.Active.Id;store.Create("복사",true);Confirm(store,90);Check(store.Get(chart)!.PlayCount==5,"Profile copy carries count");store.Select(first);Check(store.Get(chart)!.PlayCount==4,"Copied profile increments independently");
  store.Undo();Check(store.Get(chart) is {Accuracy:99.25m,PlayCount:3},"Undo higher registration restores score and count");store.Undo();Check(store.Get(chart)!.PlayCount==2,"Undo equal registration restores count");store.Undo();Check(store.Get(chart)!.PlayCount==1,"Undo lower registration restores count");store.Undo();Check(store.Get(chart)==null,"Undo first registration removes record");
  store.Put(chart,99,true);Check(store.Get(chart) is {PlayCount:1},"A manual play on an empty chart is one row");Confirm(store,98);Check(store.Get(chart) is {Accuracy:99m,PlayCount:2},"A confirmed play after it is the second row");
  var other=chart with{Judge="노말"};store.Put(other,98);Check(store.Get(other) is {PlayCount:2,Accuracy:99m}&&store.Get(chart)!.PlayCount==2,"Normal shows its own play and the hard play that carried over; hard shows only its own");
  string legacyPath=Path.Combine(dir,"legacy.json");File.WriteAllText(legacyPath,JsonSerializer.Serialize(new{ActiveId="legacy",Profiles=new[]{new{Id="legacy",Name="기본",Records=new Dictionary<string,object>{{chart.Key,new{Accuracy=99.1m,At=DateTime.UtcNow}}},Undo=Array.Empty<object>()}}}));var legacy=new RecordStore(legacyPath);Check(legacy.Get(chart) is {Accuracy:99.1m,PlayCount:1},"An older record loads as one row without score loss");Confirm(legacy,98);Check(legacy.Get(chart) is {Accuracy:99.1m,PlayCount:2},"A new confirmation is a second row beside it");
  string blockedPath=Path.Combine(dir,"blocked");var blocked=new RecordStore(blockedPath);Directory.CreateDirectory(blockedPath);var pending=SessionFixture.For(chart);pending.Lock();pending.Current=99;pending.Begin(blocked.Active.Id);try{pending.Confirm(blocked);throw new Exception("Blocked write succeeded");}catch(IOException){}catch(UnauthorizedAccessException){}Check(blocked.Get(chart)==null&&pending.Pending!=null,"Failed write rolls back count and retains pending snapshot");
  File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"count-validation.txt"),string.Join("\n",results));
 }
}
