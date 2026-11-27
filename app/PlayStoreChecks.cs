using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace LadderRecords;
// 형식 2 의 기록 파일을 줄 수를 늘려 가며 저장하고 읽는 데 걸리는 시간과 크기를 잰다(--play-store-measure).
// 저장은 프로그램과 같은 길(전체를 임시 파일에 쓰고, 본 파일과 견주고, 백업을 남기고, 바꾼다)로 한다. 들여 쓴 것과 붙여 쓴 것을 함께 잰다.
// write_ms 는 그 파일 쓰기만이다. add_save_ms 가 프로그램이 한 판을 더해 저장하는 길 전체다(작업 전의 사본, 되돌리기 내역, 쓰기 전의 검사, 파일 쓰기. 붙여 쓴 쪽에서만 잰다).
// 합성 기록만 쓰고 실행 파일 옆의 test-data 아래에만 쓴다. 결과는 play-store-measure.tsv (시간은 다섯 번의 가운데 값, 이 PC 에서의 값이다)
static class PlayStoreChecks
{
 /// <summary>
 /// 실제 형식 1 파일 하나를 **사본으로** 올려 본다(--play-format-dry <기록 파일>). 원본은 읽기만 한다(사본을 test-data 아래에 만들어 거기서 열고 저장한다. 끝나면 사본을 지운다).
 /// 줄 수와 출처별 수, 올린 것이 쓸 수 있는 모양인지, 첫 저장이 남긴 원본 사본이 원본과 같은지, 다시 열면 같은지, 크기와 걸린 시간을 play-format-dry.txt 에 적는다(기록의 내용은 적지 않는다)
 /// </summary>
 public static void Dry(string source)
 {
  var notes=new List<string>();string root=Path.Combine(AppContext.BaseDirectory,"test-data","play-format-dry-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  try
  {
   byte[] bytes=File.ReadAllBytes(source);string path=Path.Combine(root,"records.json");File.WriteAllBytes(path,bytes);
   var watch=Stopwatch.StartNew();var store=new RecordStore(path);double open=watch.Elapsed.TotalMilliseconds;
   notes.Add($"원본 {bytes.Length:N0}바이트, 올려 읽기 {open:0}ms, 이전 형식에서 올림 = {store.Upgraded}, 프로필 {store.Data.Profiles.Count}개");
   foreach(var profile in store.Data.Profiles)
    notes.Add($"  프로필: 줄 {profile.Plays.Count:N0} ("+string.Join(", ",profile.Plays.GroupBy(p=>p.Source).OrderBy(g=>g.Key).Select(g=>$"{g.Key} {g.Count():N0}"))+$"), 시각 있는 줄 {profile.Plays.Count(p=>p.At!=null):N0}, 넘겨받은 최근 플레이 {profile.Recent.Count:N0}, 다음 순번 {profile.NextSeq:N0}");
   notes.Add("쓸 수 있는 모양인가: "+(PlayFormat.Problem(store.Data)??"예"));
   watch.Restart();store.Save();double save=watch.Elapsed.TotalMilliseconds;
   bool kept=File.ReadAllBytes(Path.Combine(root,RecordStore.OriginalName)).SequenceEqual(bytes)&&File.ReadAllBytes(path+".bak").SequenceEqual(bytes);
   var again=new RecordStore(path);
   notes.Add($"첫 저장 {save:0}ms, 새 파일 {new FileInfo(path).Length:N0}바이트, 원본 사본과 백업이 원본과 같음 = {kept}, 다시 열면 같음 = {!again.Upgraded&&JsonSerializer.Serialize(again.Data)==JsonSerializer.Serialize(store.Data)}");
   notes.Add("원본을 다시 읽으면 처음과 같음 = "+File.ReadAllBytes(source).SequenceEqual(bytes));
  }
  catch(Exception e){notes.Add("실패: "+e.Message);}
  finally{try{Directory.Delete(root,true);}catch(IOException){}catch(UnauthorizedAccessException){}}
  File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"play-format-dry.txt"),string.Join("\n",notes)+"\n");
 }
 public static void Run()
 {
  var lines=new List<string>{"rows\tlayout\tbytes\twrite_ms\tload_ms\tadd_save_ms"};
  string root=Path.Combine(AppContext.BaseDirectory,"test-data","play-store-measure-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  var start=new DateTime(2026,1,1,0,0,0,DateTimeKind.Utc);string[] patterns={"NM","HD","MX","SC"},judges={"노말","하드","맥스"};
  double Middle(Func<double> once){var times=Enumerable.Range(0,5).Select(_=>once()).OrderBy(t=>t).ToList();return times[2];}
  foreach(int rows in new[]{10000,30000,100000})
  {
   var profile=new PlayProfile{Name="합성 기록(측정)"};
   for(int i=0;i<rows;i++)
   {
    // 곡 800개 × 버튼 넷 × 패턴 넷(12,800 패턴)에 고루 퍼진 판. 처음 한 바퀴의 열에 하나는 V아카이브의 최고 기록(노말, 100점 단위, 일시 미상, 패턴마다 한 줄),
    // 그 밖의 스물에 하나는 노말에 반영된 하드 판이다(형식의 규칙에 맞는 줄만 만든다)
    bool imported=i<12800&&i%10==3,linked=!imported&&i%20==7;
    string judge=imported?"노말":linked?"하드":judges[i%7==0?1:i%11==0?2:0];
    string key=$"synthetic-{i%800:0000}|{new[]{4,5,6,8}[i/800%4]}|{patterns[i/3200%4]}|{judge}";
    int score=900000+i*37%100000;
    profile.Plays.Add(new(profile.NextSeq++,key,imported?score/100*100:score,imported?null:start.AddMinutes(i*7),imported?PlaySource.VArchive:i%5==0?PlaySource.Manual:PlaySource.Play,linked?NormalLink.Linked:null));
   }
   var data=new PlayData{ActiveId=profile.Id,Profiles=new(){profile}};
   if(PlayFormat.Problem(data) is {} wrong)throw new Exception("측정용 기록이 형식에 맞지 않는다: "+wrong);
   foreach(bool indented in new[]{true,false})
   {
    var options=new JsonSerializerOptions{WriteIndented=indented,DefaultIgnoreCondition=JsonIgnoreCondition.WhenWritingNull};
    string path=Path.Combine(root,$"{rows}-{(indented?"indented":"compact")}","records.json");Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    var file=new GuardedFile(path);file.Write(stream=>JsonSerializer.Serialize(stream,data,options));   // 첫 저장(백업이 아직 없다)은 재지 않는다
    double save=Middle(()=>{var watch=Stopwatch.StartNew();file.Write(stream=>JsonSerializer.Serialize(stream,data,options));return watch.Elapsed.TotalMilliseconds;});
    double load=Middle(()=>{var watch=Stopwatch.StartNew();var store=new RecordStore(path);double ms=watch.Elapsed.TotalMilliseconds;if(store.Active.Plays.Count!=rows)throw new Exception("읽은 줄 수가 다르다");return ms;});
    long bytes=new FileInfo(path).Length;string add="-";
    if(!indented)
    {
     // 프로그램이 쓰는 저장으로 한 판을 더한다(잴 때마다 한 줄이 늘어난다). 처음 한 번(백업이 바뀐다)은 재지 않는다
     var live=new RecordStore(path);var chart=new Selection("synthetic-0000",4,"NM","하드",0);int n=0;live.Add(chart,950000,start.AddYears(2));
     add=Middle(()=>{n++;var watch=Stopwatch.StartNew();live.Add(chart,950000+n,start.AddYears(2).AddMinutes(n));return watch.Elapsed.TotalMilliseconds;}).ToString("0.0");
    }
    lines.Add($"{rows}\t{(indented?"indented":"compact")}\t{bytes}\t{save:0.0}\t{load:0.0}\t{add}");
   }
  }
  File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"play-store-measure.tsv"),string.Join("\n",lines)+"\n");
  try{Directory.Delete(root,true);}catch(IOException){}catch(UnauthorizedAccessException){}
 }
}
