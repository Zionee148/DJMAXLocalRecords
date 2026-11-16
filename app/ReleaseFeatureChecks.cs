using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace LadderRecords;

// V아카이브 가져오기와, 하드·맥스 기록이 노말에 함께 반영되는 규칙을 저장 계층에서 본다(이름의 release 는 옛 배포 준비 때 붙은 것이다. 배포본에 들어가는 검사가 아니다).
//  V아카이브: 응답 네 개(4·5·6·8버튼)를 읽어 곡 목록의 채보로 옮긴다, 미리 보기와 실제 가져오기가 같고 한 번의 저장이다, 새 기록은 횟수·최근 플레이가 비어 있다,
//             이미 있는 기록의 횟수·날짜와 더 높은 내 기록은 그대로다, 되돌리기 한 번으로 모두 돌아온다.
//  노말 반영: 하드로 올리면 노말도 오르고 횟수·날짜가 따라간다, 같은 점수의 맥스는 노말을 건드리지 않는다, 되돌리기가 함께 돌린다, 노말은 하드를 쓰지 않는다.
// 자료: 폴더에 button-4.json, button-5.json, button-6.json, button-8.json(V아카이브 응답 형식). 합성 표본은 --dev-fixtures 가 dev-fixtures\varchive 에 만든다.
// 실행: DJMAXLocalRecords.exe --release-feature-check <표본 폴더>. 몇 초 걸린다. 결과 release-feature-validation.json
// 실제 V아카이브에 접속하지 않는다. 지금 서비스의 응답이 이 형식과 같은지, 접속·닉네임 오류의 처리는 보지 않는다.
static class ReleaseFeatureChecks
{
 public static void Run(Catalog catalog,string fixtures)
 {
  var notes=new List<string>();
  void Check(bool condition,string name){if(!condition)throw new Exception(name);notes.Add(name);}
  var files=Directory.Exists(fixtures)?Directory.GetFiles(fixtures,"button-*.json"):Array.Empty<string>();
  if(files.Length!=4)throw new CheckUnavailableException($"V아카이브 표본 네 개(button-4·5·6·8.json)가 있어야 하는데 {fixtures} 에 {files.Length}개가 있다. 새 시험 폴더에서 --dev-fixtures 로 합성 표본을 만든 뒤 그 폴더(dev-fixtures\\varchive)를 준다.");
  var responses=files.Select(file=>
   JsonSerializer.Deserialize<VArchiveImport.Response>(File.ReadAllText(file),new JsonSerializerOptions(JsonSerializerDefaults.Web))!).ToList();
  var scores=VArchiveImport.Collect(catalog,responses);
  Check(responses.Count==4&&scores.Count>1000&&scores.Keys.All(c=>c.Judge=="노말"),$"V ARCHIVE fixtures parsed for all four buttons ({scores.Count:N0} valid charts)");
  Check(SteamPresence.NormalizePattern("NM")=="NORMAL"&&SteamPresence.NormalizePattern("HD")=="HARD"
   &&SteamPresence.NormalizePattern("MX")=="MAXIMUM"&&SteamPresence.NormalizePattern("SC")=="SC","Steam difficulty aliases");

  string path=Path.Combine(AppContext.BaseDirectory,"test-data",Guid.NewGuid()+".json");
  Directory.CreateDirectory(Path.GetDirectoryName(path)!);
  var store=new RecordStore(path);
  var (chart,score)=scores.First(pair=>pair.Value is >0 and <100);
  var other=scores.First(pair=>pair.Key!=chart).Key;
  var older=new DateTime(2026,10,19,1,2,3,DateTimeKind.Utc);
  store.SetManual(chart,score-.01m,7,older);
  store.SetManual(other,100,8,older);
  var watch=Stopwatch.StartNew();
  var preview=store.PreviewImport(scores);
  var revision=store.Revision;
  Check(store.Import(scores)==preview.Count&&store.Revision==revision+1,"Import preview matches a single transaction");
  var importMs=watch.Elapsed.TotalMilliseconds;
  var first=preview.First(c=>c.Before==null);
  var reloaded=new RecordStore(path);
  Check(reloaded.Active.Records[first.Key] is {PlayCount:null,LastPlayedAt:null},"New imported rows retain blank metadata on reload");
  Check(reloaded.Get(chart) is {PlayCount:7} imported&&imported.Accuracy==score&&imported.LastPlayedAt==older&&reloaded.Get(other)!.Accuracy==100,"Import keeps count, date and higher local best");
  reloaded.Undo();
  Check(reloaded.Active.Records.Count==2&&reloaded.Get(chart)!.Accuracy==score-.01m,"Import undo restores all rows");
  var hard=chart with{Judge="하드"};
  var max=chart with{Judge="맥스"};
  var played=older.AddDays(1);
  reloaded.Put(hard,score,measuredAt:played);
  var normal=reloaded.Get(chart);
  Check(reloaded.Get(hard) is {PlayCount:1}&&normal is {PlayCount:8}&&normal.Accuracy==score&&normal.LastPlayedAt==played,"Hard play improves Normal and updates its metadata");
  reloaded.Put(max,score,measuredAt:played.AddHours(1));
  Check(reloaded.Get(max) is {PlayCount:1}&&reloaded.Get(chart)==normal,"Equal Max play leaves Normal untouched");
  reloaded.Undo();
  reloaded.Undo();
  Check(reloaded.Get(chart) is {PlayCount:7}&&reloaded.Get(hard)==null&&reloaded.Get(max)==null,"Undo restores Hard and its Normal promotion together");
  reloaded.Put(other with{Judge="맥스"},100,measuredAt:played);
  Check(reloaded.Get(other) is {PlayCount:8}&&reloaded.Get(other)!.LastPlayedAt==older,"Existing equal Normal metadata stays unchanged");
  reloaded.DeleteRecord(other);
  reloaded.Put(other with{Judge="맥스"},100,measuredAt:played);
  Check(reloaded.Get(other) is {Accuracy:100,PlayCount:1}&&reloaded.Get(other)!.LastPlayedAt==played,"Max play creates missing Normal at count one");
  reloaded.Put(chart,100,measuredAt:played);
  Check(reloaded.Get(hard)==null,"Normal play never writes Hard");
  File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"release-feature-validation.json"),
   JsonSerializer.Serialize(new{notes,importMs},new JsonSerializerOptions{WriteIndented=true}));
 }
}
