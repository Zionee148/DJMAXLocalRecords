#if DEVELOPMENT_CHECKS
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
namespace LadderRecords;
[System.Runtime.Versioning.SupportedOSPlatform("windows10.0.19041")]
static class RateResponsivenessChecks
{
 const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
 sealed class Probe(Catalog catalog,Bitmap fixture):MainForm(catalog,18786,false)
 {
  protected override bool ShowWithoutActivation=>true;
  protected override Bitmap CaptureGameFrame(IntPtr handle)=>(Bitmap)fixture.Clone();
 }
 static Bitmap Full(string path){var frame=new Bitmap(1920,1080);using var tile=new Bitmap(path);using var graphics=Graphics.FromImage(frame);graphics.DrawImageUnscaled(tile,190,200);return frame;}
 internal static async Task Digits(Catalog catalog,string directory,string output)
 {
  using var reader=new Recognizer(catalog);using var perfect=Full(Path.Combine(directory,"range-fixtures/100.00.png"));
  var first=await reader.ReadRate(perfect,0);if(first!=100m)throw new Exception("Initial 100.00 detection failed");
  using var lost=(Bitmap)perfect.Clone();using(var graphics=Graphics.FromImage(lost))graphics.FillRectangle(Brushes.Black,310,232,10,16);
  var hidden=await reader.ReadRate(lost,0);if(hidden!=null)throw new Exception("Dropped leading 1 became "+hidden);
  var restored=await reader.ReadRate(perfect,0);if(restored!=100m)throw new Exception("100.00 recovery failed");
  using var zero=Full(Path.Combine(directory,"range-fixtures/0.00.png"));var actualZero=await reader.ReadRate(zero,0);if(actualZero!=0m)throw new Exception("Genuine 0.00 was blocked");
  if(Recognizer.ParseRate("00.00%")!=null)throw new Exception("Leading zero grammar accepted");
  File.WriteAllText(output,JsonSerializer.Serialize(new{passed=true,first,leadingOneMissing=hidden,restored,actualZero}));
 }
 internal static void Responsiveness(Catalog catalog,string directory,string output)
 {
  using var frame=Full(Path.Combine(directory,"1-01/f001.png"));using var app=new Probe(catalog,frame){ShowInTaskbar=false,Opacity=0};
  var windows=(ComboBox)typeof(MainForm).GetField("windows",Flags)!.GetValue(app)!;
  var item=Activator.CreateInstance(typeof(MainForm).GetNestedType("WindowItem",BindingFlags.NonPublic)!,new IntPtr(1),"fixture")!;windows.Items.Add(item);windows.SelectedItem=item;
  typeof(MainForm).GetField("steamChart",Flags)!.SetValue(app,new SteamChart("FREESTYLE","Fundamental",8,"SC"));
  Exception? failure=null;
  app.Shown+=async(_,_)=>{try{
   var work=new TaskCompletionSource();((ScoreRunner)typeof(MainForm).GetField("scoreRunner",Flags)!.GetValue(app)!).Run(()=>Thread.Sleep(2500),()=>work.SetResult());
   var watch=Stopwatch.StartNew();var read=await(Task<ScreenReading>)typeof(MainForm).GetMethod("ReadScreen",Flags)!.Invoke(app,new object[]{"FREESTYLE"})!;
   if(read.Rate!=99.28m||work.Task.IsCompleted)throw new Exception("RATE waited for SCORE worker or read incorrectly");
   File.WriteAllText(output,JsonSerializer.Serialize(new{passed=true,rate=read.Rate,elapsedMs=watch.Elapsed.TotalMilliseconds,scoreWorkStillRunning=!work.Task.IsCompleted}));
  }catch(Exception e){failure=e;}finally{app.Close();}};
  Application.Run(app);if(failure!=null)throw failure;
 }
}
#endif
