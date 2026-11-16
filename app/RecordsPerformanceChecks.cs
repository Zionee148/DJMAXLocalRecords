using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
namespace LadderRecords;
static class RecordsPerformanceChecks
{
 [System.Runtime.Versioning.SupportedOSPlatform("windows10.0.19041")]
 public static void Responsiveness(Catalog catalog,string recordsPath){var store=new RecordStore(recordsPath);store.Data.ActiveId=store.Data.Profiles.Single(p=>p.Name=="기록측정").Id;using var dialog=new RecordsDialog(catalog,store);using var context=new ApplicationContext();using var reader=new Recognizer(catalog);using var blank=new Bitmap(1920,1080);using var heartbeat=new System.Windows.Forms.Timer(){Interval=20};using var start=new System.Windows.Forms.Timer(){Interval=50};var watch=Stopwatch.StartNew();double previous=0,maxGap=0;int pulses=0;Exception? error=null;
  heartbeat.Tick+=(_,_)=>{double now=watch.Elapsed.TotalMilliseconds;if(previous>0)maxGap=Math.Max(maxGap,now-previous);previous=now;pulses++;};
  start.Tick+=async(_,_)=>{start.Stop();try{for(int i=0;i<3;i++){await reader.Read(blank,"FREESTYLE",0);var search=(TextBox)typeof(RecordsDialog).GetField("search",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(dialog)!;search.Text=i%2==0?"Alone":"";}}catch(Exception e){error=e;}finally{heartbeat.Stop();dialog.Close();context.ExitThread();}};
  dialog.Show();heartbeat.Start();start.Start();Application.Run(context);if(error!=null)throw error;if(pulses<5||maxGap>500)throw new Exception($"UI responsiveness failed: {pulses} pulses, max gap {maxGap:F0}ms");File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"records-responsiveness.json"),JsonSerializer.Serialize(new{records=store.Active.Records.Count,pulses,max_ui_gap_ms=maxGap,ocr_passes=3,includes_filtering=true}));
 }
 public static void Run(Catalog catalog,string recordsPath){
  var store=new RecordStore(recordsPath);var profile=store.Data.Profiles.Single(p=>p.Name=="기록측정");store.Data.ActiveId=profile.Id;
  var watch=Stopwatch.StartNew();using var dialog=new RecordsDialog(catalog,store);double create=watch.Elapsed.TotalMilliseconds;dialog.Show();Application.DoEvents();double shown=watch.Elapsed.TotalMilliseconds;
  T Field<T>(string name)=>(T)typeof(RecordsDialog).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(dialog)!;
  var grid=Field<RecordsView>("grid");int count=grid.RowCount;
  watch.Restart();grid.Sort("Accuracy",ListSortDirection.Descending);Application.DoEvents();double sort=watch.Elapsed.TotalMilliseconds;
  watch.Restart();Field<TextBox>("search").Text="Alone";Application.DoEvents();double search=watch.Elapsed.TotalMilliseconds;int filtered=grid.RowCount;
  watch.Restart();Field<TextBox>("search").Clear();Application.DoEvents();double reset=watch.Elapsed.TotalMilliseconds;
  if(count!=profile.Records.Count||grid.RowCount!=count||filtered==0||filtered>=count)throw new Exception("Record display/filter count mismatch");
  using(var shot=new Bitmap(dialog.Width,dialog.Height)){dialog.DrawToBitmap(shot,new Rectangle(Point.Empty,shot.Size));shot.Save(Path.Combine(AppContext.BaseDirectory,"records-large-preview.png"));}
  File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"records-performance.json"),JsonSerializer.Serialize(new{records=count,filtered,construct_ms=create,open_ms=shown,sort_ms=sort,search_ms=search,reset_ms=reset},new JsonSerializerOptions{WriteIndented=true}));dialog.Close();
 }
}
