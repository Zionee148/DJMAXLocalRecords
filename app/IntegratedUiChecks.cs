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
   NumericUpDown Field(string name)=>(NumericUpDown)typeof(RecordDialog).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(dialog)!;
   var raw=Field("rawScore");var rate=Field("value");raw.Value=996869;if(rate.Value!=99.68m)throw new Exception("Raw->RATE synchronization");
   rate.Value=99.50m;if(raw.Value!=995000)throw new Exception("RATE->raw truncation");raw.Value=995034;rate.Value=99.50m;if(raw.Value!=995034)throw new Exception("Unchanged RATE discarded raw tail");
   dialog.Show();Application.DoEvents();using var image=new Bitmap(dialog.Width,dialog.Height);dialog.DrawToBitmap(image,new(Point.Empty,image.Size));image.Save(Path.Combine(output,"manual-dialog.png"));
   report.Add("manual raw/RATE synchronization; last changed input wins; unchanged RATE preserves raw tail");
  }
  var a=BadgeDesign.Defaults;
  foreach(int mode in Enumerable.Range(0,10)){
   var titles=OverlayText.Titles(catalog);int width=OverlayLayout.Width(a,titles),height=OverlayLayout.Height(a,true);
   if(width!=540||height!=276)throw new Exception("Overlay canvas must stay 540 × 276");
   using var preview=new StylePreview(()=>a,titles,true){Width=width,Height=height,Mode=mode};preview.CreateControl();preview.Restart();using var image=new Bitmap(width,height);preview.DrawToBitmap(image,new(Point.Empty,image.Size));image.Save(Path.Combine(output,$"preview-{mode}.png"));
   if(mode>=7){typeof(StylePreview).GetField("started",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(preview,Environment.TickCount64-1550);preview.DrawToBitmap(image,new(Point.Empty,image.Size));image.Save(Path.Combine(output,$"preview-{mode}-numbers.png"));}
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
