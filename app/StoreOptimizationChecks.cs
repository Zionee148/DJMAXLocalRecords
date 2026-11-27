using System.Diagnostics;
using System.Text.Json;
namespace LadderRecords;
// 저장 계층의 두 가지를 본다. ① 저장이 실패하면 메모리·기록 파일·저장 횟수가 그 전 그대로다(임시 파일 자리를 폴더로 막아 실패시킨다).
// ② 되돌리기용 복사본(Snapshot)이 원본과 이어져 있지 않다(복사한 뒤 원본을 바꿔도 복사본은 그대로다). 많은 기록에서 복사에 걸리는 시간과 할당량도 적어 둔다(기준은 없고 적기만 한다).
// 자료: ②는 ‘UserData’의 records.json 에 기록이 3,000개 넘게 있어야 한다. 합성 기록은 --dev-fixtures 가 만든다. 그 파일은 읽기만 한다.
// 실행: 새 시험 폴더에 복사한 Debug 빌드에서 DJMAXLocalRecords.exe --dev-fixtures 뒤에 --store-optimization-check. 몇 초 걸린다. 결과 store-optimization-validation.json
// 밖에서 바뀐 기록 파일의 보호와 본 파일 없이 남은 백업은 StoreGuardTests(--self-test)가 본다. 여기서는 보지 않는다.
static class StoreOptimizationChecks
{
 public static void Run(Catalog catalog){void Check(bool condition,string name){if(!condition)throw new Exception(name);}var root=Path.Combine(AppContext.BaseDirectory,"test-data",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);string path=Path.Combine(root,"records.json");var store=new RecordStore(path);var song=catalog.Songs.First(s=>s.Charts.Count>0);var chart=song.Charts[0];var selection=new Selection(song.Id,chart.Button,chart.Pattern,"하드",0);store.SetManual(selection,98.76m,3);string before=JsonSerializer.Serialize(store.Data);var fileBefore=File.ReadAllBytes(path);long revision=store.Revision;Directory.CreateDirectory(path+".tmp");try{store.SetManual(selection,99m,4);throw new Exception("Failed save was accepted");}catch(IOException){}catch(UnauthorizedAccessException){}Check(JsonSerializer.Serialize(store.Data)==before&&File.ReadAllBytes(path).SequenceEqual(fileBefore)&&store.Revision==revision,"Save failure did not roll back complete state");
  string beside=UserFiles.At("records.json");
  if(!File.Exists(beside))throw new CheckUnavailableException("‘UserData’에 records.json 이 없다. 새 시험 폴더에서 --dev-fixtures 로 합성 기록을 만든 뒤 돌린다.");
  byte[] besideBefore=File.ReadAllBytes(beside);
  var large=new RecordStore(beside);int total=large.Data.Profiles.Sum(p=>p.Plays.Count);
  if(total<=3000)throw new CheckUnavailableException($"기록이 3,000개 넘게 있어야 하는 검사인데 ‘UserData’의 기록은 {total}개다. 새 시험 폴더에서 --dev-fixtures 로 합성 기록을 만든 뒤 돌린다.");
  var snapshot=large.Snapshot();var profile=large.Active;string oldName=profile.Name;int oldRecords=profile.Plays.Count,oldUndo=profile.Undo.Count;profile.Name="mutated";profile.Plays.Clear();profile.Undo.Clear();
  var copied=snapshot.Profiles.Single(p=>p.Id==profile.Id);
  Check(copied.Name==oldName&&copied.Plays.Count==oldRecords&&copied.Undo.Count==oldUndo&&snapshot.Profiles.Sum(p=>p.Plays.Count)==total,"Snapshot shared mutable data");large=new RecordStore(UserFiles.At("records.json"));long oldBytes=0,newBytes=0;double oldMs=0,newMs=0;for(int i=0;i<12;i++){long allocation=GC.GetAllocatedBytesForCurrentThread();var watch=Stopwatch.StartNew();_ =JsonSerializer.Serialize(large.Data);if(i>=2){oldBytes+=GC.GetAllocatedBytesForCurrentThread()-allocation;oldMs+=watch.Elapsed.TotalMilliseconds;}allocation=GC.GetAllocatedBytesForCurrentThread();watch.Restart();_ =large.Snapshot();if(i>=2){newBytes+=GC.GetAllocatedBytesForCurrentThread()-allocation;newMs+=watch.Elapsed.TotalMilliseconds;}}Check(File.ReadAllBytes(beside).SequenceEqual(besideBefore),"Large record file was modified by a read-only check");
  File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"store-optimization-validation.json"),JsonSerializer.Serialize(new{rollback_verified=true,snapshot_isolated=true,records=large.Data.Profiles.Sum(p=>p.Plays.Count),legacy_snapshot_mean_ms=oldMs/10,snapshot_mean_ms=newMs/10,legacy_snapshot_allocated_bytes=oldBytes/10,snapshot_allocated_bytes=newBytes/10}));
 }
}
