#if DEVELOPMENT_CHECKS
using System.Reflection;
using System.Text.Json;
namespace LadderRecords;
[System.Runtime.Versioning.SupportedOSPlatform("windows10.0.19041")]
static class IntegratedUiChecks
{
 sealed class ManualPreview(Catalog catalog,RecordStore store,Selection selection):RecordDialog(catalog,store,selection){protected override bool ShowWithoutActivation=>true;}

 internal static void Run(Catalog catalog,string output)
 {
  Directory.CreateDirectory(output);var report=new List<string>();
  var store=new RecordStore(Path.Combine(output,"manual-test.json"));var song=catalog.Songs.First(s=>s.Charts.Count>0);var chart=song.Charts[0];var selection=new Selection(song.Id,chart.Button,chart.Pattern,"노말",0);
  using(var dialog=new ManualPreview(catalog,store,selection){ShowInTaskbar=false,Opacity=0}){
   // SCORE 와 RATE 의 칸은 서로 따라간다. 숫자는 Windows 의 숫자 형식으로 적고 읽는다
   var culture=System.Globalization.CultureInfo.CurrentCulture;var input=dialog.Input;
   input.ScoreBox.Text=996869.ToString("N0",culture);if(input.RateBox.Text!=99.68m.ToString("F2",culture))throw new Exception("Raw->RATE synchronization");
   input.RateBox.Text=99.50m.ToString("F2",culture);if(input.Value!=995000)throw new Exception("RATE->raw truncation");
   input.ScoreBox.Text=995034.ToString("N0",culture);input.RateBox.Text=99.51m.ToString("F2",culture);input.RateBox.Text=99.50m.ToString("F2",culture);if(input.Value!=995034)throw new Exception("Unchanged RATE discarded raw tail");
   dialog.Show();Application.DoEvents();using var image=new Bitmap(dialog.Width,dialog.Height);dialog.DrawToBitmap(image,new(Point.Empty,image.Size));image.Save(Path.Combine(output,"manual-dialog.png"));
   report.Add("manual raw/RATE synchronization; last changed input wins; unchanged RATE preserves raw tail");
  }
  // 디자인 설정 창의 미리보기를 OBS 크기(1080×552)로 그린다: 열 가지 상태의 완성된 모습과 연출 도중의 모습. 오버레이 페이지를 찍은 그림과 견주는 데 쓴다(흰 바탕 위에 놓는다)
  var a=BadgeDesign.Defaults;
  if(OverlayLayout.Width(a,Array.Empty<string>())!=540||OverlayLayout.Height(a,true)!=276)throw new Exception("Overlay canvas must stay 540 × 276");
  void Shot(StylePreview preview,string name,double? clock){using var image=new Bitmap(OverlayLayout.ObsWidth,OverlayLayout.ObsHeight);using(var g=Graphics.FromImage(image)){g.Clear(Color.White);preview.DrawOverlay(g,new RectangleF(0,0,image.Width,image.Height),clock);}image.Save(Path.Combine(output,name+".png"));}
  (int Mode,int[] At)[] moments={(0,new[]{90,330,700}),(3,new[]{60}),(4,new[]{200,400,700}),(5,new[]{200,3200}),(6,new[]{200,330,500,800}),(7,new[]{120,300,1000,1600,1750,2100,7600}),(8,new[]{120,300,450,700,1000,1600,1900,2100,7600}),(9,new[]{300,1000,1700,2100,7600})};
  foreach(int mode in Enumerable.Range(0,10)){
   using var preview=new StylePreview(()=>a,true){Mode=mode};
   Shot(preview,$"preview-{mode}",null);
   foreach(int at in moments.FirstOrDefault(m=>m.Mode==mode).At??Array.Empty<int>())Shot(preview,$"preview-{mode}-at{at:0000}",at);
  }
  using(var hidden=new StylePreview(()=>a,false){Mode=1})Shot(hidden,"preview-1-current-hidden",null);
  // 예시 곡은 곡 목록에서 뽑힌다: 끝이 줄어드는 긴 이름과 한자가 든 이름도 오버레이와 같게 그려져야 한다
  using(var longest=new StylePreview(()=>a,true){Mode=2,SongText=OverlayText.ReferenceTitle,ButtonText="4B",PatternText="MAXIMUM",JudgeText="맥스"})Shot(longest,"preview-2-long",null);
  using(var longest=new StylePreview(()=>a,true){Mode=9,SongText=OverlayText.ReferenceTitle})Shot(longest,"preview-9-long",null);
  using(var kanji=new StylePreview(()=>a,true){Mode=0,SongText="泣き虫 O'clock"})Shot(kanji,"preview-0-kanji",null);
  using(var kart=new StylePreview(()=>a,true){Mode=8,SongText=OverlayText.KartTitle,ButtonText="5B",PatternText="HARD",JudgeText="노말"})Shot(kart,"preview-8-kart",null);
  var other=JsonSerializer.SerializeToElement(new{Above="#1B7F3B",Below="#C0263B",Text="#202830",Background="#F4F6FA",TextTransparency=40,BackgroundTransparency=60});
  using(var light=new StylePreview(()=>other,true){Mode=6,ButtonText="4B",PatternText="MAXIMUM",JudgeText="맥스"})Shot(light,"preview-6-light-40-60",null);
  // 움직임을 줄인 PC(Windows 의 '애니메이션 효과' 끔): 연출 도중의 때에도 그 장면의 완성된 모습이어야 한다. 완성된 모습의 그림과 화소까지 같은지 본다
  {
   static bool Same(Bitmap x,Bitmap y){for(int row=0;row<x.Height;row+=2)for(int column=0;column<x.Width;column+=2)if(x.GetPixel(column,row)!=y.GetPixel(column,row))return false;return true;}
   Bitmap Draw(int mode,double? clock,bool reduced){using var preview=new StylePreview(()=>a,true){Mode=mode};typeof(StylePreview).GetField("reduced",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(preview,reduced);var image=new Bitmap(OverlayLayout.ObsWidth,OverlayLayout.ObsHeight);using var g=Graphics.FromImage(image);g.Clear(Color.White);preview.DrawOverlay(g,new RectangleF(0,0,image.Width,image.Height),clock);return image;}
   // (상태, 때, 같아야 할 완성된 모습의 상태): 곡 정보·점수 확인·등록 확인은 곧바로 그 모습, 실패는 3.14초 뒤 플레이 화면, 저장 결과의 점수 상세는 숫자가 다 올라간 모습
   foreach(var (mode,at,still) in new[]{(0,100,0),(4,200,4),(6,200,6),(5,200,5),(5,3200,0),(8,1600,8),(9,1600,9)})
   {
    using var moving=Draw(mode,at,true);using var settled=Draw(still,null,false);
    if(!Same(moving,settled))throw new Exception($"Reduced-motion preview differs: mode {mode} at {at}ms");
    using var lively=Draw(mode,at,false);
    if(mode!=5&&Same(lively,settled))throw new Exception($"Preview effect missing: mode {mode} at {at}ms");   // 같은 때에 움직임을 줄이지 않으면 연출 도중이라 달라야 한다
   }
   // 저장 결과는 7.5초 뒤에 저장이 반영된 플레이 화면으로 곧바로 바뀐다(움직임을 줄이지 않았을 때 0.2초의 전환이 끝난 모습과 같다)
   using(var back=Draw(7,7600,true))using(var done=Draw(7,9000,false))if(!Same(back,done))throw new Exception("Reduced-motion preview differs: return to play");
   report.Add("reduced-motion previews equal the settled scenes");
  }
  report.Add("GDI design previews rendered: all ten states");File.WriteAllLines(Path.Combine(output,"ui-validation.txt"),report);
 }
 internal static async Task Positions(Catalog catalog,string directory,string output)
 {
  using var tile=new Bitmap(Path.Combine(directory,"1-01/f001.png"));var reports=new List<object>();
  for(int gear=0;gear<3;gear++)foreach(int dy in new[]{-150,300,650}){
   using var reader=new Recognizer(catalog);using var frame=new Bitmap(1920,1080);int dx=gear==1?1280:gear==2?640:0;using(var g=Graphics.FromImage(frame))g.DrawImageUnscaled(tile,190+dx,200+dy);
   var read=await reader.ReadRate(frame,gear);if(read!=99.28m)throw new Exception($"HUD placement gear={gear} dy={dy}: {read}");
   var locked=reader.HudArea(gear);using var blank=new Bitmap(1920,1080);var hidden=await reader.ReadRate(blank,gear);if(hidden!=null||reader.HudArea(gear)!=locked)throw new Exception("Hidden RATE lost lock or invented digits");
   if(await reader.ReadRate(frame,gear)!=99.28m)throw new Exception("RATE lock recovery failed");reports.Add(new{gear,dy,read,locked});
  }
  using var range=JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,"range-fixtures/manifest.json")));using var rangeReader=new Recognizer(catalog);
  using(var first=new Bitmap(1920,1080)){using(var g=Graphics.FromImage(first))g.DrawImageUnscaled(tile,190,200);await rangeReader.ReadRate(first,0);}
  foreach(var item in range.RootElement.EnumerateArray()){
   using var cropped=new Bitmap(Path.Combine(directory,"range-fixtures",item.GetProperty("file").GetString()!));using var frame=new Bitmap(1920,1080);using(var g=Graphics.FromImage(frame))g.DrawImageUnscaled(cropped,190,200);
   var expected=item.GetProperty("rate").GetDecimal();var actual=await rangeReader.ReadRate(frame,0);reports.Add(new{synthetic=true,expected,actual});if(actual!=expected)throw new Exception($"Numeric range {expected}: {actual}");
  }
  File.WriteAllText(output,JsonSerializer.Serialize(new{passed=true,reports},new JsonSerializerOptions{WriteIndented=true}));
 }
}
#endif
