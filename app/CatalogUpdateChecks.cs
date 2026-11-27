using System.Text.Json;

namespace LadderRecords;

// 곡 목록(data\catalog.json)을 게임 업데이트에 맞춰 바꾼 뒤, 늘어난 곡과 패턴을 앱이 실제로 쓰는지 검사한다(개발용 빌드에만 들어간다).
// 바꾸기 전의 곡 목록과 지금 폴더의 곡 목록을 견준다. 곡 목록을 바꾸는 도구는 tools\update_catalog.py 다.
//  1. 늘기만 했다: 전의 곡이 모두 같은 번호·곡명·아티스트·팩으로 있고, 전의 패턴이 모두 남아 있다. 새 곡마다 패턴이 하나 이상 있다.
//  2. 늘어난 패턴마다(새 곡의 패턴과, 기존 곡에 더해진 SC 등의 패턴):
//     - Steam 곡 정보로 찾기: 새 목록에서는 그 곡이 후보에 들고, 전의 목록에서는 들지 않는다(그 패턴을 플레이하면 전에는 곡을 못 찾았다).
//     - V아카이브 가져오기: 새 목록에서는 가져오고, 전의 목록에서는 건너뛴다.
//  3. 기록: 전의 목록의 모든 패턴에 기록이 있는 파일에, 늘어난 패턴의 기록을 더하고 다시 읽어도 전의 기록이 하나도 달라지지 않는다.
//     전의 기록은 모두 새 목록의 곡과 패턴으로 찾아진다.
//  4. 이름이 같은 곡: 새 곡과 이름이 같은 곡이 있으면 오버레이·목록의 제목에 아티스트가 붙고(전에 혼자이던 곡도 바뀐다),
//     그 이름의 곡마다 동명곡 소리 기준(data\audio\<SourceId>.bin)이 있고 앱의 방법으로 읽혀야 한다. 없거나 읽지 못하면 실패로 적는다(그 곡들을 가르지 못한다).
//     소리 기준이 실제 게임 소리로 두 곡을 가르는지는 여기서 보지 않는다(tools\build_audio_references.py 의 확인).
// 실행: DJMAXLocalRecords.exe --catalog-update-check <바꾸기 전의 catalog.json>. 몇 초 걸린다. 결과 catalog-update-validation.json
// 두 목록의 곡과 패턴이 같으면 검사할 것이 없으므로 실행 불가로 끝난다. 사라지거나 바뀐 것이 있으면 늘어난 것이 없어도 실패 내역을 결과 파일에 남긴다.
// 실제 게임의 Steam 곡 이름이 곡 목록의 표기와 같은지는 검사 범위 밖이다(게임에서만 알 수 있다).
static class CatalogUpdateChecks
{
 public static void Run(Catalog catalog,string previousPath)
 {
  var previous=JsonSerializer.Deserialize<Catalog>(File.ReadAllText(previousPath))!;
  var notes=new List<string>();var wrong=new List<string>();
  void Check(bool condition,string name){(condition?notes:wrong).Add((condition?"PASS: ":"FAIL: ")+name);}
  var before=previous.Songs.ToDictionary(s=>s.Id);var after=catalog.Songs.ToDictionary(s=>s.Id);

  // 1. 늘기만 했다
  var missing=before.Keys.Where(id=>!after.ContainsKey(id)).ToList();
  var changed=before.Values.Where(s=>after.TryGetValue(s.Id,out var n)&&(n.SourceId!=s.SourceId||n.Name!=s.Name||n.Artist!=s.Artist||n.Pack!=s.Pack)).Select(s=>s.Id).ToList();
  var lost=before.Values.Where(s=>after.TryGetValue(s.Id,out var n)).SelectMany(s=>s.Charts.Where(c=>!after[s.Id].Charts.Contains(c)).Select(c=>$"{s.Id} {c.Button}B {c.Pattern}")).ToList();
  Check(missing.Count==0,$"전의 곡이 모두 남아 있음(없어진 곡 {missing.Count}: {string.Join(", ",missing.Take(5))})");
  Check(changed.Count==0,$"전의 곡의 곡명·아티스트·팩이 그대로임(바뀐 곡 {changed.Count}: {string.Join(", ",changed.Take(5))})");
  Check(lost.Count==0,$"전의 패턴이 모두 남아 있음(없어진 패턴 {lost.Count}: {string.Join(", ",lost.Take(5))})");

  var newSongs=after.Values.Where(s=>!before.ContainsKey(s.Id)).ToList();
  var added=after.Values.SelectMany(s=>s.Charts.Where(c=>!before.TryGetValue(s.Id,out var old)||!old.Charts.Contains(c)).Select(c=>(Song:s,Chart:c))).ToList();
  var grown=added.Where(a=>before.ContainsKey(a.Song.Id)).ToList();   // 기존 곡에 더해진 패턴
  // 두 목록의 곡과 패턴이 같을 때만 검사할 것이 없다. 사라지거나 바뀐 것이 있으면 늘어난 것이 없어도 끝까지 보고 실패를 남긴다.
  if(missing.Count==0&&changed.Count==0&&lost.Count==0&&added.Count==0&&newSongs.Count==0)
   throw new CheckUnavailableException("두 곡 목록의 곡과 패턴이 같다. 검사할 것이 없다.");
  var bare=newSongs.Where(s=>s.Charts.Count==0).Select(s=>$"{s.Name}({s.SourceId})").ToList();
  Check(bare.Count==0,$"새 곡마다 패턴이 하나 이상 있음(패턴이 없는 새 곡 {bare.Count}: {string.Join(", ",bare.Take(5))})");
  notes.Add($"새 곡 {newSongs.Count}개(패턴 {added.Count-grown.Count}개), 기존 곡에 더해진 패턴 {grown.Count}개: "+string.Join(", ",grown.Select(a=>$"{a.Song.Name} {a.Chart.Button}B {a.Chart.Pattern}")));

  // 2. 늘어난 패턴을 Steam 곡 정보로 찾기, V아카이브에서 가져오기
  foreach(var (song,chart) in added)
  {
   string what=$"{song.Name}({song.SourceId}) {chart.Button}B {chart.Pattern}";
   var steam=new SteamChart("FREESTYLE",song.Name,chart.Button,chart.Pattern);
   var now=SteamSongs.Find(catalog,steam);var then=SteamSongs.Find(previous,steam);
   Check(now.Any(s=>s.Id==song.Id),$"Steam 곡 정보로 찾음(새 목록): {what} → 후보 {now.Count}");
   Check(!then.Any(s=>s.Id==song.Id),$"전의 목록에서는 찾지 못했음: {what}");
   var response=new VArchiveImport.Response(true,chart.Button,new(){new(song.SourceId,chart.Pattern switch{"NORMAL"=>"NM","HARD"=>"HD","MAXIMUM"=>"MX",_=>chart.Pattern},99.12m)});
   var take=new Selection(song.Id,chart.Button,chart.Pattern,"노말",0);
   Check(VArchiveImport.Collect(catalog,new[]{response}).TryGetValue(take,out var value)&&value==99.12m,$"V아카이브에서 가져옴(새 목록): {what}");
   Check(VArchiveImport.Collect(previous,new[]{response}).Count==0,$"전의 목록에서는 건너뜀: {what}");
  }

  // 3. 기록: 전의 패턴마다 기록이 있는 파일에 늘어난 패턴을 더하고 다시 읽는다
  string dir=Path.Combine(AppContext.BaseDirectory,"test-data",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
  string file=Path.Combine(dir,"records.json");
  var store=new RecordStore(file);
  var old=previous.Songs.SelectMany(s=>s.Charts.Select(c=>new Selection(s.Id,c.Button,c.Pattern,"노말",0))).Select((sel,i)=>(sel,score:90m+(i%1000)/100m)).ToDictionary(p=>p.sel,p=>p.score);
  store.Import(old);
  var snapshot=old.Keys.ToDictionary(k=>k,k=>store.Get(k));
  foreach(var (song,chart) in added)store.Put(new Selection(song.Id,chart.Button,chart.Pattern,"하드",0),98.76m);
  var reloaded=new RecordStore(file);
  int same=snapshot.Count(p=>p.Value!=null&&reloaded.Get(p.Key)==p.Value);
  Check(same==snapshot.Count,$"전의 기록 {snapshot.Count:N0}개가 늘어난 패턴의 기록을 더하고 다시 읽은 뒤에도 그대로임({same:N0}개 같음)");
  if(added.Count>0)Check(added.All(a=>reloaded.Get(new Selection(a.Song.Id,a.Chart.Button,a.Chart.Pattern,"하드",0))?.Accuracy==98.76m),$"늘어난 패턴 {added.Count}개의 기록이 저장되고 다시 읽힘");
  Check(reloaded.Keys().All(key=>{var p=key.Split('|');return after.TryGetValue(p[0],out var s)&&s.Charts.Contains(new Chart(int.Parse(p[1]),p[2]));}),"저장된 기록이 모두 새 목록의 곡과 패턴으로 찾아짐");
  try{Directory.Delete(dir,true);}catch(IOException){}

  // 4. 이름이 같은 곡
  foreach(var song in newSongs)
  {
   var group=catalog.Songs.Where(s=>s.Name.Trim().Equals(song.Name.Trim(),StringComparison.OrdinalIgnoreCase)).ToList();
   if(group.Count<2){Check(OverlayText.Title(catalog,song)==song.Name,$"새 곡의 제목: {OverlayText.Title(catalog,song)}");continue;}
   var titles=group.Select(s=>OverlayText.Title(catalog,s)).ToList();
   Check(titles.Distinct().Count()==group.Count&&titles.All(t=>t.EndsWith(")")),"이름이 같은 곡의 제목에 아티스트가 붙음: "+string.Join(" / ",titles));
   // 소리 기준은 앱이 읽는 방법(AudioIdentity.Load)으로 열어 본다. 파일이 있어도 읽지 못하면 그 곡들을 가르지 못한다.
   var badAudio=new List<string>();
   foreach(var s in group){
    string path=Path.Combine(AppContext.BaseDirectory,"data","audio",$"{s.SourceId}.bin");
    if(!File.Exists(path)){badAudio.Add($"{s.Name}({s.SourceId}, {s.Artist}): 파일 없음");continue;}
    try{AudioIdentity.Load(path);}
    catch(Exception e) when(e is IOException or InvalidDataException or UnauthorizedAccessException){badAudio.Add($"{s.Name}({s.SourceId}, {s.Artist}): 읽지 못함({e.GetType().Name}: {e.Message})");}
   }
   Check(badAudio.Count==0,$"이름이 같은 곡의 소리 기준이 모두 있고 읽힘(문제: {(badAudio.Count==0?"없음":string.Join(" / ",badAudio))})");
  }

  File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"catalog-update-validation.json"),
   JsonSerializer.Serialize(new{passed=wrong.Count==0,wrong,notes},new JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));
  if(wrong.Count>0)throw new Exception($"곡 목록 검사 실패 {wrong.Count}건: "+wrong[0]);
 }
}
