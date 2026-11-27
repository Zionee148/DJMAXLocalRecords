using System.Security.Cryptography;
using System.Text.Json;
namespace LadderRecords;

// 동명곡 식별용 음향 기준(data\audio\<SourceId>.bin)을 만들고 확인하는 도구(개발용 빌드에만 들어간다).
// 특징 계산과 비교는 앱이 플레이 중에 쓰는 것(AudioIdentity)을 그대로 부른다. 그래서 기준과 플레이 중의 소리가 같은 계산을 거친다.
// 영상에서 소리를 추출하고 작업 목록을 만들어 이 도구를 실행하는 것은 tools\build_audio_references.py 다.
//
//   DJMAXLocalRecords.exe --audio-reference-build <작업 목록.json>
//     {"items":[{"id":"varchive:2","wav":"…\\2.wav","bin":"…\\2.bin"}],"report":"…\\build.json"}
//     wav(11,025Hz 모노 PCM16)마다 특징을 계산해 bin 으로 쓰고, 앱이 읽는 방법(AudioIdentity.Load)으로 다시 읽어 같은지 본다.
//   DJMAXLocalRecords.exe --audio-reference-check <작업 목록.json>
//     {"references":{"varchive:2":"…\\2.bin",…},"pairs":[["varchive:2","varchive:441"],…],
//      "queries":[{"group":"…","expect":"varchive:2","pair":0,"wav":"…","start":12.0,"seconds":8.0}],"report":"…\\check.json"}
//     식별을 요청한 구간(queries 의 항목)마다 wav 의 그 구간으로 특징을 계산해, 그 쌍의 두 기준 가운데 어느 곡으로 확정하는지(AudioIdentity.Decide) 적는다.
//     비교하지 못한 구간은 refused 에 사유를 적고, 비교했지만 확정하지 못한 구간은 decided 를 null 로 적는다(expect 와 맞는지는 이 도구가 판정하지 않는다).
//     references 의 값이 "app" 이면 실행 파일 옆의 data\audio 에서 읽는다(빌드에 복사된 자료를 확인할 때).
static class AudioReferenceChecks
{
 const int Header=0x3146474C;
 static string Hash(string path){using var stream=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();}
 static string Text(JsonElement item,string name)=>item.GetProperty(name).GetString()!;

 public static void Build(string jobPath)
 {
  var job=JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(jobPath));var rows=new List<object>();
  foreach(var item in job.GetProperty("items").EnumerateArray())
  {
   string id=Text(item,"id"),wav=Text(item,"wav"),bin=Text(item,"bin");
   var samples=AudioIdentity.ReadWav(wav);
   var features=AudioIdentity.Features(samples);
   Directory.CreateDirectory(Path.GetDirectoryName(bin)!);
   using(var writer=new BinaryWriter(File.Create(bin)))
   {
    writer.Write(Header);writer.Write(features.Length);writer.Write(AudioIdentity.Dimensions);
    foreach(var frame in features)foreach(float value in frame)writer.Write(value);
   }
   // 앱이 읽는 방법으로 다시 읽어, 쓴 것과 값 하나하나가 같은지 본다
   var loaded=AudioIdentity.Load(bin);
   if(loaded.Length!=features.Length)throw new Exception("다시 읽은 프레임 수가 다르다: "+id);
   for(int i=0;i<loaded.Length;i++)if(!loaded[i].AsSpan().SequenceEqual(features[i]))throw new Exception("다시 읽은 값이 다르다: "+id);
   int silent=features.Count(frame=>frame.All(value=>value==0));
   rows.Add(new{id,wav,bin,samples=samples.Length,seconds=samples.Length/(double)AudioIdentity.Rate,frames=features.Length,silent_frames=silent,bytes=new FileInfo(bin).Length,sha256=Hash(bin),wav_sha256=Hash(wav)});
  }
  File.WriteAllText(Text(job,"report"),JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));
 }

 public static void Check(string jobPath)
 {
  var job=JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(jobPath));
  var references=new Dictionary<string,float[][]>();var files=new Dictionary<string,object>();
  foreach(var entry in job.GetProperty("references").EnumerateObject())
  {
   string path=entry.Value.GetString()=="app"?Path.Combine(AppContext.BaseDirectory,"data","audio",entry.Name.Split(':')[1]+".bin"):entry.Value.GetString()!;
   references[entry.Name]=AudioIdentity.Load(path);files[entry.Name]=new{path,frames=references[entry.Name].Length,sha256=Hash(path)};
  }
  var pairs=job.GetProperty("pairs").EnumerateArray().Select(pair=>pair.EnumerateArray().Select(id=>id.GetString()!).ToArray()).ToArray();
  var sounds=new Dictionary<string,float[]>();var rows=new List<object>();
  foreach(var query in job.GetProperty("queries").EnumerateArray())
  {
   string wav=Text(query,"wav"),group=Text(query,"group");string? expect=query.GetProperty("expect").GetString();
   int pair=query.GetProperty("pair").GetInt32();double start=query.GetProperty("start").GetDouble(),seconds=query.GetProperty("seconds").GetDouble();
   if(!sounds.TryGetValue(wav,out var all))sounds[wav]=all=AudioIdentity.ReadWav(wav);
   int from=(int)Math.Round(start*AudioIdentity.Rate),count=(int)Math.Round(seconds*AudioIdentity.Rate);
   if(from<0||from+count>all.Length){rows.Add(new{group,expect,pair,wav,start,seconds,decided=(string?)null,refused="구간이 소리 밖이다",scores=(object?)null,best=0d,margin=0d});continue;}
   var candidates=pairs[pair].ToDictionary(id=>id,id=>references[id]);
   try
   {
    var features=AudioIdentity.Features(all.AsSpan(from,count).ToArray());
    var decision=AudioIdentity.Decide(features,candidates);
    rows.Add(new{group,expect,pair,wav,start,seconds,decided=decision.SongId,refused=(string?)null,scores=(object?)candidates.ToDictionary(c=>c.Key,c=>Math.Round(AudioIdentity.Compare(features,c.Value),4)),best=Math.Round(decision.Score,4),margin=Math.Round(decision.Margin,4)});
   }
   // 너무 짧거나 무음이면 앱도 비교하지 않고 넘긴다
   catch(InvalidDataException e){rows.Add(new{group,expect,pair,wav,start,seconds,decided=(string?)null,refused=e.Message,scores=(object?)null,best=0d,margin=0d});}
  }
  File.WriteAllText(Text(job,"report"),JsonSerializer.Serialize(new{references=files,rows},new JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));
 }
}
