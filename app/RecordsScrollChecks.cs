using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
namespace LadderRecords;
static class RecordsScrollChecks
{
 [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr h,int msg,IntPtr w,IntPtr l);
 public static void Run(Catalog catalog,string recordsPath){var store=new RecordStore(recordsPath);store.Data.ActiveId=store.Data.Profiles.Single(p=>p.Name=="기록측정").Id;using var dialog=new RecordsDialog(catalog,store);dialog.Show();Application.DoEvents();var grid=(RecordsView)typeof(RecordsDialog).GetField("grid",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(dialog)!;using var canvas=new Bitmap(grid.Width,grid.Height);var samples=new List<double>();int maximumTop=0;for(int pass=0;pass<2;pass++){for(int i=0;i<200;i++){var watch=Stopwatch.StartNew();SendMessage(grid.Handle,0x1014,IntPtr.Zero,new IntPtr(pass==0?64:-64));grid.DrawToBitmap(canvas,new Rectangle(Point.Empty,canvas.Size));Application.DoEvents();samples.Add(watch.Elapsed.TotalMilliseconds);maximumTop=Math.Max(maximumTop,grid.TopItem?.Index??0);}}samples.Sort();if(maximumTop<300||grid.RowCount<3000)throw new Exception("Scroll check did not traverse enough records");using(var shot=new Bitmap(dialog.Width,dialog.Height)){dialog.DrawToBitmap(shot,new Rectangle(Point.Empty,shot.Size));shot.Save(Path.Combine(AppContext.BaseDirectory,"records-scroll-preview.png"));}File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"records-scroll-validation.json"),JsonSerializer.Serialize(new{records=grid.RowCount,scroll_steps=samples.Count,maximum_top_row=maximumTop,includes_forced_paint=true,mean_ms=samples.Average(),p95_ms=samples[(int)(samples.Count*.95)],maximum_ms=samples[^1]}));dialog.Close();}
}
