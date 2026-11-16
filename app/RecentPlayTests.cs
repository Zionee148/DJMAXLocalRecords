namespace LadderRecords;
static class RecentPlayTests
{
 public static void Run(Catalog catalog){
 var notes=new List<string>();void Check(bool value,string name){if(!value)throw new Exception(name);notes.Add("PASS: "+name);}
 string dir=Path.Combine(AppContext.BaseDirectory,"test-data",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);string path=Path.Combine(dir,"recent.json");var store=new RecordStore(path);var c=new Selection(catalog.Songs[0].Id,4,"SC","하드",1);DateTime t=new(2026,10,20,1,0,0,DateTimeKind.Utc);
 store.Put(c,99.5m,measuredAt:t);var best=store.Get(c)!;Check(best.LastPlayedAt==t,"First measured time saved");
 store.Put(c,98m,measuredAt:t.AddMinutes(1));Check(store.Get(c)!.Accuracy==99.5m&&store.Get(c)!.At==best.At&&store.Get(c)!.LastPlayedAt==t.AddMinutes(1),"Lower score updates recent time while best and best date stay intact");
 store.Put(c,99.5m,measuredAt:t.AddMinutes(2));Check(store.Get(c)!.LastPlayedAt==t.AddMinutes(2),"Equal score updates recent time");
 store.Put(c,100m,measuredAt:t.AddMinutes(3));Check(store.Get(c)!.LastPlayedAt==t.AddMinutes(3)&&store.Get(c)!.Accuracy==100,"Higher score updates both");
 Check(new RecordStore(path).Get(c)!.LastPlayedAt==t.AddMinutes(3),"Recent time survives restart");store.Put(c,97m,true);Check(store.Get(c)!.LastPlayedAt==t.AddMinutes(3),"Manual edit preserves actual play time");store.Undo();store.Undo();Check(store.Get(c)!.LastPlayedAt==t.AddMinutes(2)&&store.Get(c)!.Accuracy==99.5m,"Undo restores time and best together");
 var session=SessionFixture.For(c);session.Lock();session.Current=98;var pending=session.Begin(store.Active.Id);Check(pending.MeasuredAt.HasValue,"Measurement timestamp captured with pending score");session.Confirm(store);Check(store.Get(c)!.LastPlayedAt==pending.MeasuredAt,"Confirmation saves measurement time rather than later confirmation time");
 var saved=store.Get(c);session.Observe(c,0);session.Lock();session.Current=99;session.Begin(store.Active.Id);session.Cancel();Check(store.Get(c)==saved,"Cancellation leaves time intact");
 var manual=c with{Judge="맥스"};store.Put(manual,99m,true);Check(store.Get(manual)!.LastPlayedAt==null,"New manual record has no fabricated play time");
 var legacy=System.Text.Json.JsonSerializer.Deserialize<Score>("{\"Accuracy\":99.00,\"At\":\"2026-10-19T01:00:00Z\",\"PlayCount\":3}")!;Check(legacy.LastPlayedAt==null&&legacy.PlayCount==3,"Legacy records remain compatible without fabricated recent time");
 File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"recent-play-validation.txt"),string.Join("\n",notes));
 }
}
