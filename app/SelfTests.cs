using System.Text.Json;
namespace LadderRecords;
[System.Runtime.Versioning.SupportedOSPlatform("windows10.0.19041")]
static class SelfTests
{
 public static void Run(Catalog catalog)
 {
  CountTests.Run(catalog);RecentPlayTests.Run(catalog);DeleteTests.Run(catalog);StoreGuardTests.Run();UpdateTests.Run();
  void Check(bool ok,string name){if(!ok)throw new Exception(name);}
  Check(catalog.Songs.Count==846,"Catalog count");Check(catalog.Songs.Select(s=>s.Id).Distinct().Count()==846,"Unique song IDs");
  Check(catalog.Songs.GroupBy(s=>s.Name).Any(g=>g.Count()>1),"Duplicate title coverage");
  var chart=new Selection(catalog.Songs[0].Id,4,"SC","하드",1);var session=new Session();session.Observe(chart,0);session.Observe(chart,1);session.Observe(chart,2);session.Observe(chart,2.99);Check(session.Locked==null,"Premature lock before 3 seconds");session.Observe(null,3);session.Observe(chart,4);session.Observe(chart,5);session.Observe(chart,6);session.Observe(chart,6.99);Check(session.Locked==null,"Noncontinuous lock");session.Observe(chart,7);Check(session.Locked==chart,"Three second lock");session.Observe(chart with{Button=8},30);Check(session.Locked==chart,"Locked overwrite");
  session.Next();Check(session.Candidate==null&&session.Locked==null,"Detection reset");
  string dir=Path.Combine(AppContext.BaseDirectory,"test-data",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);var store=new RecordStore(Path.Combine(dir,"records.json"));
  Check(store.Get(chart)==null,"Fresh unregistered");Check(store.Put(chart,0)=="최초 등록","Zero first record");store.Put(chart,98);Check(store.Put(chart,97)=="기존 기록 유지"&&store.Get(chart)!.Accuracy==98,"Lower record changed");store.Undo();Check(store.Get(chart) is {Accuracy:98m,PlayCount:2},"Undo lower registration count");store.Undo();Check(store.Get(chart)!.Accuracy==0,"Undo update");store.Undo();Check(store.Get(chart)==null,"Undo first registration");
  var snap=new Session();snap.Observe(chart,0);snap.Lock();snap.Current=99.48m;snap.Begin(store.Active.Id);snap.Current=1;snap.Confirm(store);Check(store.Get(chart)!.Accuracy==99.48m,"Snapshot changed while pending");Check(snap.Locked==null,"Confirm did not advance");
  snap.Observe(chart,0);snap.Lock();snap.Current=100;snap.Begin(store.Active.Id);snap.Cancel();Check(snap.Locked==chart&&store.Get(chart)!.Accuracy==99.48m,"Cancel changed data or lock");
  string first=store.Active.Id;store.Create("복사",true);store.Put(chart,100);store.Select(first);Check(store.Get(chart)!.Accuracy==99.48m,"Copy shared records");store.Rename("원본");Check(new RecordStore(Path.Combine(dir,"records.json")).Active.Name=="원본","Profile persistence");

  File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"test-result.txt"),"PASS: 846 unique songs; duplicate titles; continuous 3-second lock and interruption reset; immutable locked selection; manual reset; unregistered vs 0%; higher-only update; undo first/update; immutable confirmation snapshot; cancellation lock retention; advance after confirmation; profile copy isolation and persistence.");
 }
}




