using System.Drawing.Drawing2D;
using System.Text.RegularExpressions;
using Windows.Media.Ocr;
namespace LadderRecords;
partial class Recognizer
{
 public bool TrialEnabled {get;set;}=true;
 public string LastMethod {get;private set;}="";
 record TextReading(string Title,string Artist,bool TitleClipped,bool ArtistClipped,string Origin);
 static int EditDistance(string a,string b){var d=Enumerable.Range(0,b.Length+1).ToArray();for(int i=1;i<=a.Length;i++){int before=d[0];d[0]=i;for(int j=1;j<=b.Length;j++){int old=d[j];d[j]=Math.Min(Math.Min(d[j]+1,d[j-1]+1),before+(a[i-1]==b[j-1]?0:1));before=old;}}return d[^1];}
 internal static bool PrefixMatches(string read,string full,bool clipped,int minimum=8){
  string a=Identity(read),b=Identity(full);if(!clipped||a.Length<minimum||b.Length<=a.Length)return false;
  // Only a small OCR error budget is allowed in the visible beginning; the missing suffix is not an error.
  int budget=Math.Max(1,(int)Math.Floor(a.Length*.10));
  for(int trim=0;trim<=2&&a.Length-trim>=minimum;trim++){string visible=a[..(a.Length-trim)];for(int n=Math.Max(minimum,visible.Length-budget);n<=Math.Min(b.Length-1,visible.Length+budget);n++)if(EditDistance(visible,b[..n])<=budget)return true;}return false;
 }
 static bool ArtistSupports(TextReading r,Song song){var a=Identity(r.Artist);var b=Identity(song.Artist);return a.Length>0&&a==b||PrefixMatches(r.Artist,song.Artist,r.ArtistClipped,12);}
 bool TextConflict(IEnumerable<TextReading> readings,Song expected)=>readings.Any(r=>references.Any(t=>Identity(t.Song.Name)!=Identity(expected.Name)&&Identity(r.Title).Length>=3&&Identity(r.Title)==Identity(t.Song.Name)));
 bool AmbiguousPrefix(IEnumerable<TextReading> readings)=>readings.Any(r=>references.Count(t=>ArtistSupports(r,t.Song)&&PrefixMatches(r.Title,t.Song.Name,r.TitleClipped))>1);
 Song? PrefixSong(IEnumerable<TextReading> readings,Match cover){
  if(cover.Distance>=.15||cover.Margin<=.018)return null;
  var list=readings.ToArray();if(TextConflict(list,cover.Song)||AmbiguousPrefix(list))return null;
  return list.Any(r=>PrefixMatches(r.Title,cover.Song.Name,r.TitleClipped)&&ArtistSupports(r,cover.Song))?cover.Song:null;
 }
 async Task<TextReading> ReadNatural(Bitmap frame,int panel,int scale,OcrEngine engine){
  async Task<(string Text,bool Clipped)> Region(Rectangle area){using var crop=new Bitmap(area.Width*scale+40,area.Height*scale+40);using(var g=Graphics.FromImage(crop)){g.Clear(Color.Black);g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.DrawImage(frame,new Rectangle(20,20,area.Width*scale,area.Height*scale),area,GraphicsUnit.Pixel);}var words=await Words(crop,engine);string text=string.Join(" ",words.OrderBy(w=>w.Y/Math.Max(1,area.Height*scale)).ThenBy(w=>w.X).Select(w=>w.Text));return(text,words.Any(w=>w.X+w.Width>=20+(area.Width-28)*scale));}
  var title=await Region(new(panel+100,165,335,35));var artist=await Region(new(panel+100,199,335,25));return new(title.Text,artist.Text,title.Clipped,artist.Clipped,$"원본 {scale}배 {engine.RecognizerLanguage.LanguageTag}");
 }
 async Task<(Song? Song,string Title,string Method)> ResolveFallback(Bitmap frame,int panel,Match cover,List<TextReading> readings){
  var prefixed=PrefixSong(readings,cover);if(prefixed!=null)return(prefixed,readings[0].Title,"1단계: 잘린 제목+아티스트+자켓");
  var engines=new[]{ocr,korean}.Where(e=>e!=null).Cast<OcrEngine>().DistinctBy(e=>e.RecognizerLanguage.LanguageTag).ToArray();
  foreach(int scale in new[]{2,3})foreach(var engine in engines){
   var r=await ReadNatural(frame,panel,scale,engine);readings.Add(r);Detail+=$" | {r.Origin}: {r.Title} / {r.Artist}";
   var exact=ExactTextSong(references.Select(t=>t.Song),r.Title,r.Artist);
   if(exact!=null&&!TextConflict(readings,exact)&&(cover.Song.Id==exact.Id||cover.Distance>=.10||cover.Margin<=.04))return(exact,r.Title,$"2단계: {r.Origin} 곡명+아티스트");
   prefixed=PrefixSong(new[]{r},cover);if(prefixed!=null&&!TextConflict(readings,prefixed))return(prefixed,r.Title,$"2단계: {r.Origin} 잘린 제목+아티스트+자켓");
  }
  // A title failure alone cannot identify a song. Require a strong global jacket winner and matching artist.
  if(cover.Distance<.10&&cover.Margin>.04&&readings.Any(r=>ArtistSupports(r,cover.Song))&&!TextConflict(readings,cover.Song)&&!AmbiguousPrefix(readings))return(cover.Song,readings[^1].Title,"3단계: 아티스트+확실한 자켓");
  return(null,readings[0].Title,"보완 인식 보류");
 }
 public void CheckFallbackGuards(string output){
  var checks=new List<string>();void Check(bool value,string label){if(!value)throw new Exception(label);checks.Add("PASS: "+label);}
  Check(PrefixMatches("Tekken Tag Tournament Pian","Tekken Tag Tournament Piano Intro -Massive True Mix-",true),"long visible prefix accepted");
  Check(!PrefixMatches("Tekken","Tekken Tag Tournament Piano Intro -Massive True Mix-",true),"short prefix rejected");
  Check(!PrefixMatches("Tekken Tag Tournament Pian","Tekken Tag Tournament Piano Intro -Massive True Mix-",false),"unclipped text rejected by prefix stage");
  Check(!PrefixMatches("Unrelated Completely Different","Tekken Tag Tournament Piano Intro -Massive True Mix-",true),"unrelated title rejected");
  var song=references.Single(t=>t.Song.Name=="脳天直撃").Song;var strong=new Match(song,.025,.12);
  Check(PrefixSong(new[]{new TextReading("Tekken Tag Tournament Pian",song.Artist,true,false,"test")},strong)==null,"wrong title prefix cannot follow a strong unrelated jacket");
  Check(TextConflict(new[]{new TextReading("Fundamental",song.Artist,false,false,"test")},song),"recognizable conflicting title blocks artist fallback");
  Check(!ArtistSupports(new TextReading("", "Wrong Artist",false,false,"test"),song),"wrong artist rejected");
  Check(PrefixSong(new[]{new TextReading("Permutation Part", "XeoN",true,false,"test")},new Match(references.Single(t=>t.Song.Name=="Permutation Part.1").Song,.02,.10))==null,"shared title prefix and same artist remain uncertain");
  Check(PrefixSong(new[]{new TextReading("Tekken Tag Tournament Pian","Akitaka Tohyama (Bandai Namco Entertainment Inc.)",true,false,"test")},new Match(references.Single(t=>t.Song.Name.StartsWith("Tekken Tag Tournament")).Song,.02,.01))==null,"small jacket lead rejected");
  Check(new[]{ocr,korean}.Where(e=>e!=null).All(e=>!e!.RecognizerLanguage.LanguageTag.StartsWith("ja")),"Japanese OCR is never selected");
  var chart=new Selection(song.Id,4,"SC","하드",1);var session=new Session();session.Observe(chart,0);session.Observe(chart,2.99);Check(session.Locked==null,"processing gap cannot count as continuous recognition");session.Observe(chart,3.5);session.Observe(null,4);Check(session.Candidate==null&&session.Locked==null,"failed observation resets stability");session.Observe(chart,5);session.Observe(chart,6);session.Observe(chart,7);session.Observe(chart,7.99);Check(session.Locked==null,"no early lock");session.Observe(chart,8);Check(session.Locked!=null,"three continuous seconds lock");
  File.WriteAllLines(Path.Combine(output,"safety-checks.txt"),checks);
 }
}
