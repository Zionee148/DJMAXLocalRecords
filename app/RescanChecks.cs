using System.Reflection;
namespace LadderRecords;
[System.Runtime.Versioning.SupportedOSPlatform("windows10.0.19041")]
static class RescanChecks
{
 public static void Run(MainForm app,Catalog catalog){
  var notes=new List<string>();void Check(bool value,string label){if(!value)throw new Exception(label);notes.Add("PASS: "+label);}
  T Field<T>(string name)=>(T)typeof(MainForm).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(app)!;
  void Call(string name,params object[] values)=>typeof(MainForm).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(app,values);
  void Press()=>Call("WndProc",Message.Create(app.Handle,0x0312,(IntPtr)103,IntPtr.Zero));
  var s=Field<Session>("session");var timer=Field<System.Windows.Forms.Timer>("timer");var song=catalog.Songs.Single(song=>song.Name=="Fundamental");var chart=new Selection(song.Id,8,"SC","하드",1);
  Call("SyncSelection",chart);foreach(var name in new[]{"buttonChip","patternChip","judgeChip"})Check(Field<Chip>(name).Text.Length>0,"Main song information shown: "+name);string displayed=Field<Label>("songTitle").Text;Check(displayed==OverlayText.Title(catalog,song),"Main song title shown");
  s.Observe(chart,0);s.Lock();s.Current=98;s.Running=true;typeof(MainForm).GetField("finishedKey",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(app,chart.Key);
  Press();Check(s.Running&&s.Locked==null&&s.Candidate==null&&timer.Enabled,"One global rescan message resets and starts recognition");Check(Field<string?>("finishedKey")==null,"Manual rescan clears same-song exclusion");timer.Stop();
  s.Observe(chart,100);s.Observe(chart,101);s.Observe(chart,102);s.Observe(chart,103);Check(s.Locked?.Key==chart.Key,"Same song can lock again without a second keypress");
  s.Running=false;Press();Check(s.Running&&timer.Enabled&&s.Locked==null,"One rescan keypress also resumes stopped detection");timer.Stop();Check(Field<Label>("songTitle").Text==displayed,"Previous displayed information remains until next recognition");
  s.Next();Call("RefreshState");Check(Field<Label>("songTitle").Text==displayed,"Clearing recognition does not erase last displayed chart");s.Running=false;
  File.WriteAllLines(Path.Combine(AppContext.BaseDirectory,"rescan-validation.txt"),notes);
 }
}
