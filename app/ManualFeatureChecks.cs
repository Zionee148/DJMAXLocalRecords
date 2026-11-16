using System.Reflection;
namespace LadderRecords;
static class SessionFixture
{
 public static Session For(Selection c){var s=new Session();s.Observe(c,0);return s;}
}
[System.Runtime.Versioning.SupportedOSPlatform("windows10.0.19041")]
static class ManualFeatureChecks
{
 public static void Run(Catalog catalog){
  var notes=new List<string>();void Check(bool value,string text){if(!value)throw new Exception(text);notes.Add("PASS: "+text);}
  string folder=Path.Combine(AppContext.BaseDirectory,"test-data",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);var store=new RecordStore(Path.Combine(folder,"manual.json"));var song=catalog.Songs.Single(s=>s.Name=="Fundamental");var chart=new Selection(song.Id,8,"SC","하드",1);var t=new DateTime(2026,10,22,0,0,0,DateTimeKind.Utc);
  store.Put(chart,99,measuredAt:t);var old=store.Get(chart)!;store.SetManual(chart,99,12,t.AddMinutes(1));Check(store.Get(chart) is {Accuracy:99m,PlayCount:12}&&store.Get(chart)!.LastPlayedAt==t.AddMinutes(1)&&store.Get(chart)!.At==old.At,"Count-only manual edit preserves best score and best timestamp");store.Undo();Check(store.Get(chart)==old,"Undo restores score count and time");
  store.SetManual(chart,97,1,t.AddMinutes(2));Check(store.Get(chart) is {Accuracy:97m,PlayCount:1}&&store.Get(chart)!.LastPlayedAt==t.AddMinutes(2),"Manual score correction allows lower value with explicit cumulative count");store.Undo();
  var absent=chart with{Judge="맥스"};store.SetManual(absent,0,0,t);Check(store.Get(absent) is {Accuracy:0m,PlayCount:0}&&store.Get(absent)!.LastPlayedAt==t,"New zero record and zero count remain valid");store.Undo();Check(store.Get(absent)==null,"Undo removes newly created manual record");
  var beforeInvalid=store.Get(chart);try{store.SetManual(chart,99,-1);throw new Exception("Invalid count accepted");}catch(Exception e)when(e.Message.Contains("0 이상")){}Check(store.Get(chart)==beforeInvalid,"Negative count does not modify record");store.Put(chart,98,measuredAt:t.AddMinutes(3));Check(store.Get(chart) is {Accuracy:99m,PlayCount:2}&&store.Get(chart)!.LastPlayedAt==t.AddMinutes(3),"Automatic lower score still increments once and preserves best");
  store.SetManual(chart,99.25m,12,t);using(var dialog=new RecordDialog(catalog,store,chart)){
   dialog.Show();Application.DoEvents();Check(dialog.Chart.Key==chart.Key&&dialog.Value==99.25m&&dialog.PlayCount==12,"Manual dialog prefills chart score and count");
   T Field<T>(string name)=>(T)typeof(RecordDialog).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(dialog)!;
   Field<System.Windows.Forms.NumericUpDown>("count").Value=13;Check(dialog.Value==99.25m&&dialog.PlayCount==13&&store.Get(chart)!.PlayCount==12,"Editing controls does not save until confirmation");
   Field<System.Windows.Forms.TextBox>("search").Text="ZZZZ nonexistent song";Check(!((System.Windows.Forms.Button)dialog.AcceptButton!).Enabled,"Empty search cannot save");Field<System.Windows.Forms.TextBox>("search").Text="Fundamental Bagagee";Check(Field<System.Windows.Forms.ComboBox>("songs").Items.Count==1,"Search covers title and artist");Field<System.Windows.Forms.ComboBox>("buttons").SelectedItem=8;Field<System.Windows.Forms.ComboBox>("patterns").SelectedItem="SC";Field<System.Windows.Forms.ComboBox>("judges").SelectedItem="하드";Check(dialog.PlayCount==12&&dialog.Value==99.25m,"Changing combination reloads stored score and count");
   using var shot=new Bitmap(dialog.Width,dialog.Height);dialog.DrawToBitmap(shot,new Rectangle(Point.Empty,shot.Size));shot.Save(Path.Combine(AppContext.BaseDirectory,"records-preview.png"));dialog.Close();Check(store.Get(chart)!.PlayCount==12,"Closing manual dialog does not write data");
  }
  File.WriteAllLines(Path.Combine(AppContext.BaseDirectory,"manual-validation.txt"),notes);
 }
}
