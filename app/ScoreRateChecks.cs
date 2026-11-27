#if DEVELOPMENT_CHECKS
using System.Diagnostics;
using System.Text.Json;
namespace LadderRecords;
[System.Runtime.Versioning.SupportedOSPlatform("windows10.0.19041")]
static class ScoreRateChecks
{
 static void Check(bool value,string name){if(!value)throw new Exception(name);}
 internal static void Domain(Catalog catalog,string output)
 {
  string path=Path.Combine(Path.GetDirectoryName(output)!,"check-records-"+Guid.NewGuid().ToString("N")+".json");
  var store=new RecordStore(path);var c=new Selection(catalog.Songs[0].Id,4,"SC","하드",0);
  store.Put(c,99.28m,rawScore:992800,propagateNormal:false);
  Check(store.Get(c with{Judge="노말"})==null,"Practice created normal record");
  store.Put(c,99.28m,rawScore:992869,propagateNormal:false);
  Check(store.Get(c)!.Value==992869&&store.Get(c)!.PlayCount==2,"Same RATE SCORE improvement");
  store.Put(c,99.28m,rawScore:992840);Check(store.Get(c)!.Value==992869,"Lower raw score overwrote best");
  Check(store.Get(c with{Judge="노말"})!.Value==992840,"Normal propagation must use this measured score");
  store.Undo();Check(store.Get(c with{Judge="노말"})==null&&store.Get(c)!.PlayCount==2,"Undo linked propagation");
  var manual=c with{Button=5};store.SetManual(manual,99.20m,0,rawScore:992034);Check(store.Get(manual)!.Value==992034,"Manual raw tail lost");   // 수동으로 넣는 것은 한 판을 더하는 것이라 빈 판정에서 본다
  manual=c with{Button=6};store.SetManual(manual,99.20m,0);Check(store.Get(manual)!.Value==992000,"Manual RATE conversion");
  Check(ScoreValue.Rate(999999)==99.99m&&ScoreValue.Rate(1000000)==100m&&ScoreValue.FromAccuracy(9.11m)==91100,"Conversion/truncation");
  var normal=c with{Judge="노말"};store.Put(normal,99.99m,rawScore:999999);
  int? count=store.Get(normal)!.PlayCount;store.Import(new Dictionary<Selection,decimal>{{normal,99.99m}});
  Check(store.Get(normal)!.Value==999999&&store.Get(normal)!.PlayCount==count+1,"V ARCHIVE equal RATE discarded true raw score");   // 가져온 기록은 따로 한 줄로 선다
  var session=new Session();session.Detect(c);session.Current=1;
  session.Begin(store.Active.Id,measuredAccuracy:99.28m,rawScore:992899,propagateNormal:false,chart:c,steamKey:"frozen");
  session.Current=2;session.Confirm(store);Check(store.Get(c)!.Value==992899,"Frozen record changed");
  Check(store.Get(normal)!.Value==999999,"Practice modified normal best");
  var findings=new List<object>();int charts=0;
  foreach(var song in catalog.Songs)foreach(var chart in song.Charts){
   var found=SteamSongs.Find(catalog,new("FREESTYLE",song.Name,chart.Button,chart.Pattern));
   Check(found.Any(s=>s.Id==song.Id),"Catalog self-match failed: "+song.Name);charts++;
  }
  var greg=catalog.Songs.Single(s=>s.SourceId==690);
  foreach(string name in new[]{"Gregorius | Symphony","GREGORIUS SYMPHONY","Gregorius I Symphony","Gregorius Ⅰ Symphony","Gregorius ｜ Symphony"})foreach(var chart in greg.Charts){
   var matches=SteamSongs.Find(catalog,new("LADDERMATCH",name,chart.Button,chart.Pattern));Check(matches.Count==1&&matches[0].Id==greg.Id,"Gregorius variant: "+name);
  }
  // Steam 이 feat. 의 가수 이름을 다른 글자로 줄 때: 그 부분을 뺀 제목이 곡 목록에서 하나뿐인 곡만 맞춘다.
  var cata=catalog.Songs.Single(s=>s.SourceId==657);
  foreach(var chart in cata.Charts){var matches=SteamSongs.Find(catalog,new("FREESTYLE","Cata (feat. 앤씨아)",chart.Button,chart.Pattern));Check(matches.Count==1&&matches[0].Id==cata.Id,"Cata with the singer's name in Korean");}
  Check(SteamSongs.Find(catalog,new("FREESTYLE","Cata (feat. 앤씨아)",7,"SC")).Count==0,"Featuring fallback ignored the chart");
  Check(SteamSongs.Find(catalog,new("FREESTYLE","missing song (feat. someone)",4,"NORMAL")).Count==0,"Unknown featuring title was guessed");
  int featuring=0,featuringUnique=0;var featuringShared=new List<object>();
  foreach(var group in catalog.Songs.GroupBy(s=>SteamSongs.Title(s.Name))){
   var with=group.Where(s=>SteamSongs.Title(s.Name)!=SteamSongs.Clean(s.Name)).ToList();if(with.Count==0)continue;featuring+=with.Count;
   if(group.Count()>1){featuringShared.Add(new{title=group.Key,songs=group.Select(s=>new{s.Id,s.Name,s.Artist})});continue;}
   // 가수 이름이 다른 글자로 와도 그 곡으로, 그 곡의 채보로만 맞는다
   var song=with[0];var c0=song.Charts.First();var renamed=SteamSongs.Find(catalog,new("FREESTYLE",System.Text.RegularExpressions.Regex.Replace(song.Name,@"\bfeat\b.*$","feat. 다른표기)",System.Text.RegularExpressions.RegexOptions.IgnoreCase),c0.Button,c0.Pattern));
   Check(renamed.Count==1&&renamed[0].Id==song.Id,"Featuring fallback failed: "+song.Name);featuringUnique++;
  }
  foreach(var group in catalog.Songs.GroupBy(s=>SteamSongs.Alias(s.Name)).Where(g=>g.Count()>1))findings.Add(new{key=group.Key,songs=group.Select(s=>new{s.Id,s.Name,s.Artist})});
  File.WriteAllText(output,JsonSerializer.Serialize(new{passed=true,songs=catalog.Songs.Count,charts,gregoriusVariants=80,ambiguousGroups=findings,featuringSongs=featuring,featuringUnique,featuringShared,actualSteamGregoriusObserved=false},new JsonSerializerOptions{WriteIndented=true}));
 }
 internal static async Task Rate(Catalog catalog,string directory,string output)
 {
  using var labels=JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,"labels.json")));
  var results=new List<object>();int correct=0,total=0;
  foreach(var gear in labels.RootElement.EnumerateObject()){
   using var reader=new Recognizer(catalog);
   foreach(var item in gear.Value.EnumerateObject()){
    using var tile=new Bitmap(Path.Combine(directory,gear.Name,$"f{int.Parse(item.Name):000}.png"));
    using var frame=new Bitmap(1920,1080);using(var graphics=Graphics.FromImage(frame))graphics.DrawImageUnscaled(tile,190,200);
    var watch=Stopwatch.StartNew();var actual=await reader.ReadRate(frame,0);var expected=item.Value.GetDecimal();
    correct+=actual==expected?1:0;total++;results.Add(new{id=gear.Name,index=item.Name,expected,actual,ms=watch.Elapsed.TotalMilliseconds,area=reader.HudArea(0)});
   }
  }
  File.WriteAllText(output,JsonSerializer.Serialize(new{correct,total,results},new JsonSerializerOptions{WriteIndented=true}));
 }
}
#endif
