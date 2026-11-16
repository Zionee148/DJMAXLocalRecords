using System.Diagnostics;
using System.Text.Json;
namespace LadderRecords;
[System.Runtime.Versioning.SupportedOSPlatform("windows10.0.19041")]
static class PlayTests
{
 public static async Task Run(Catalog catalog,string folder,string? extra=null){
  using var reader=new Recognizer(catalog);var results=new List<string>();
  var cases=new[]{("4B, SC, 8레벨.jpg","더 파이퍼",4,"SC","맥스",0,3),("5B, SC 9레벨.jpg","Moment (feat. Nauts)",5,"SC","하드",0,1),("6B, SC, 9레벨.jpg","Let's Burn",6,"SC","하드",0,1),("8B, SC, 12레벨.jpg","Hero",8,"SC","하드",0,1),("8B, SC, 15레벨.jpg","ANiMA",8,"SC","하드",0,1),("4B, MAXIMUN, 13레벨.jpg","VORTEX",4,"MAXIMUM","노말",0,1),("5B, MAXIMUM, 15레벨.jpg","Cyaegha",5,"MAXIMUM","노말",0,2),("Center.jpg","Space of Soul",8,"MAXIMUM","노말",2,1),("Right.jpg","Radiance",5,"MAXIMUM","노말",1,1)};
  void Check(bool ok,string name){if(!ok)throw new Exception(name+"\n"+reader.Detail);results.Add("PASS: "+name);}
  foreach(var (file,name,button,pattern,judge,gear,round) in cases){using var b=new Bitmap(Path.Combine(folder,file));var clock=Stopwatch.StartNew();var c=await reader.ReadPlay(b,gear);Check(c!=null&&catalog.Songs.Single(s=>s.Id==c.SongId).Name==name&&c.Button==button&&c.Pattern==pattern&&c.Judge==judge&&c.Round==round,file);results.Add($"  {clock.ElapsedMilliseconds} ms");}
  using var normal=new Bitmap(Path.Combine(folder,"4B, MAXIMUN, 13레벨.jpg"));
  foreach(var (file,region,pattern) in new[]{("HARD.png",new Rectangle(529,232,142,33),"HARD"),("NORMAL.png",new Rectangle(574,219,140,34),"NORMAL")}){
   using var b=new Bitmap(normal);using var sample=new Bitmap(Path.Combine(folder,file));using(var g=Graphics.FromImage(b))g.DrawImage(sample,new Rectangle(1058,266,110,28),region,GraphicsUnit.Pixel);
   // Exercise the supplied real label colors on a full gameplay panel. Chart availability may reject the synthetic combination.
   await reader.ReadPlay(b,0);Check(reader.Detail.Contains("패턴: "+pattern),pattern+" label from supplied cropped image");
  }
  using var empty=new Bitmap(1920,1080);Check(await reader.ReadPlay(empty,0)==null,"Empty frame rejected");Check(await reader.ReadPlay(normal,2)==null,"Wrong gear position rejected");
  Check(Hotkeys.Defaults().Count==5&&Hotkeys.Defaults().Any(k=>k.Name=="감지 시작")&&!Hotkeys.Defaults().Any(k=>k.Name.Contains("해제")||k.Name.Contains("확정")),"Five global actions including detection start");
  if(extra!=null)foreach(var (file,name,button,pattern,gear) in new[]{("20261020073321_1.jpg","O'men",6,"HARD",2),("20261020074431_1.jpg","JBG",8,"NORMAL",0)}){using var b=new Bitmap(Path.Combine(extra,file));var c=await reader.ReadPlay(b,gear);Check(c!=null&&catalog.Songs.Single(s=>s.Id==c.SongId).Name==name&&c.Button==button&&c.Pattern==pattern&&c.Judge=="노말",file+" real full gameplay");}
  using(var b=new Bitmap(Path.Combine(folder,"20261020085734_1.jpg"))){var c=await reader.ReadPlay(b,0);Check(c!=null&&c.SongId=="varchive:512"&&c.Button==8&&c.Pattern=="SC"&&c.Judge=="하드"&&reader.Detail.Contains("곡명+아티스트"),"Fundamental text-first recognition despite ambiguous jacket");}
  Check(Recognizer.ExactTextSong(catalog.Songs,"Fundamental","Bagagee Viphex13")?.Name=="Fundamental"&&Recognizer.ExactTextSong(catalog.Songs,"Fundamental","PIA")==null&&Recognizer.ExactTextSong(catalog.Songs,"Fundamental","")==null,"Text match requires correct artist");
  foreach(var group in catalog.Songs.GroupBy(s=>s.Name).Where(g=>g.Count()>1))foreach(var song in group)Check(Recognizer.ExactTextSong(catalog.Songs,song.Name,song.Artist)?.Id==song.Id,"Duplicate title with artist: "+song.Id);
  using(var font=new Font("맑은 고딕",24,FontStyle.Bold,GraphicsUnit.Pixel)){var shortTitle=OverlayText.Middle("카트라이더, 크레이지아케이드, 버블파이터 Main theme ~CHUCK Remix~",font,240);Check(shortTitle.Contains('…')&&shortTitle.StartsWith("카트")&&shortTitle.EndsWith("Remix~"),"Middle ellipsis preserves beginning and ending");Check(OverlayText.Middle("Fundamental",font,400)=="Fundamental","Short title retained");}
  Check(Hotkeys.Defaults()[4].Key==Keys.F12&&Hotkeys.Defaults()[4].Modifiers==2,"Detection shortcut Ctrl+F12");
  using(var font=new Font("맑은 고딕",25,FontStyle.Bold,GraphicsUnit.Pixel)){Check(OverlayText.DisplayTitle(OverlayText.ReferenceTitle,font)=="Tekken Tag Tournament Piano Intro...","Exact Tekken display title");Check(OverlayText.DisplayTitle(OverlayText.KartTitle,font)=="카트라이더…CHUCK Remix~","Exact Kart display title");Check(catalog.Songs.Count(s=>OverlayText.DisplayTitle(OverlayText.Title(catalog,s),font)!=OverlayText.Title(catalog,s))==2,"Only two current titles shortened");}
  Check(catalog.Songs.All(song=>OverlayText.Title(catalog,song)==(catalog.Songs.Count(s=>s.Name.Equals(song.Name,StringComparison.OrdinalIgnoreCase))>1?$"{song.Name} ({song.Artist})":song.Name)),"All 846 overlay titles; artist shown only for duplicate titles");
  File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"play-validation.txt"),string.Join("\n",results));
 }
}
