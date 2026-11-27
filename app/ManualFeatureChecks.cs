using System.Globalization;
namespace LadderRecords;
static class SessionFixture
{
 public static Session For(Selection c){var s=new Session();s.Observe(c,0);return s;}
}
[System.Runtime.Versioning.SupportedOSPlatform("windows10.0.19041")]
// 수동으로 더하는 판(저장층)과 '수동 기록 설정' 창을 본다.
// 창은 한 판을 더하거나(새 플레이: 저장 시각 또는 직접 적은 시각, 하드·맥스의 연습·무효) 직접 저장한 가장 최근의 한 판의 점수를 바꾼다(교체).
// 저장하는 순간에 프로필·패턴·보여 준 최고 기록·교체할 판이 그대로인지 다시 보고, 달라졌으면 저장하지 않고 알린다. 바닥줄의 'V아카이브 삭제'는 지금 프로필의 그 출처만 한 번에 지운다.
// 알림과 일괄 삭제 확인은 창을 띄우지 않고 받는다(PlayNotice.Hook, RecordDialog.BulkHook). 지금 시각은 정해 준다(RecordDialog.Clock)
static class ManualFeatureChecks
{
 public static void Run(Catalog catalog){
  var notes=new List<string>();void Check(bool value,string text){if(!value)throw new Exception(text);notes.Add("PASS: "+text);}
  string folder=Path.Combine(AppContext.BaseDirectory,"test-data",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);string path=Path.Combine(folder,"manual.json");var store=new RecordStore(path);var song=catalog.Songs.Single(s=>s.Name=="Fundamental");var chart=new Selection(song.Id,8,"SC","하드",1);var t=new DateTime(2026,10,22,0,0,0,DateTimeKind.Utc);
  // ── 저장층: 수동으로 더한 판은 한 줄이다(검사 전용 옛 이름으로 읽는다. OldNamesChecks.cs)
  store.Put(chart,99,measuredAt:t);var old=store.Get(chart)!;store.SetManual(chart,99,12,t.AddMinutes(1));Check(store.Get(chart) is {Accuracy:99m,PlayCount:2}&&store.Get(chart)!.LastPlayedAt==t.AddMinutes(1)&&store.Get(chart)!.At==t.AddMinutes(1),"A manual play of the same score is one more row; the later play carries the best time");store.Undo();Check(store.Get(chart)==old,"Undo restores score count and time");
  store.SetManual(chart,97,1,t.AddMinutes(2));Check(store.Get(chart) is {Accuracy:99m,PlayCount:2}&&store.Get(chart)!.LastPlayedAt==t.AddMinutes(2)&&store.Get(chart)!.At==old.At&&store.Active.Plays[^1] is {Score:970000,Source:PlaySource.Manual}&&store.Active.Plays[^1].Key==chart.Key&&store.Active.Plays[^1].At==t.AddMinutes(2),"A lower manual play is kept as a row with the score that was entered; the best stays");store.Undo();
  var absent=chart with{Judge="맥스"};store.SetManual(absent,0,0,t);Check(store.Get(absent) is {Accuracy:0m,PlayCount:1}&&store.Get(absent)!.LastPlayedAt==t,"A zero score on an empty chart is a valid record of one row");store.Undo();Check(store.Get(absent)==null,"Undo removes newly created manual record");
  store.Put(chart,98,measuredAt:t.AddMinutes(3));Check(store.Get(chart) is {Accuracy:99m,PlayCount:2}&&store.Get(chart)!.LastPlayedAt==t.AddMinutes(3)&&store.Active.Plays[^1] is {Score:980000,Source:PlaySource.Play}&&store.Active.Plays[^1].Key==chart.Key,"Automatic lower score is kept as its own row and preserves best");

  // ── 창
  var normal=chart with{Judge="노말"};var culture=CultureInfo.CurrentCulture;
  string N(int score)=>score.ToString("N0",culture);string Local(DateTime utc)=>utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
  var others=catalog.Songs.Where(s=>s.Id!=song.Id&&s.Charts.Count>0).Take(4).Select(s=>new Selection(s.Id,s.Charts[0].Button,s.Charts[0].Pattern,"노말",1)).ToList();
  var lone=others[0];var late=others[1];var single=others[2];var spare=others[3];
  var told=new List<(string Title,string Head,string Text)>();
  // 기록 파일이 잠겨 쓰지 못했을 때의 안내(까닭과 다음 행동. Windows 의 영문 오류를 그대로 보이지 않는다)
  const string Locked="기록 파일을 다른 프로그램이 사용하고 있습니다.\n해당 프로그램을 닫은 뒤 다시 시도해 주세요.";
  IEnumerable<Control> All(Control parent)=>parent.Controls.Cast<Control>().SelectMany(child=>new[]{child}.Concat(All(child)));
  RecordDialog Open(Selection c){var dialog=new RecordDialog(catalog,store,c);dialog.Show();Application.DoEvents();return dialog;}
  void Type(RecordDialog dialog,int score)=>dialog.Input.ScoreBox.Text=N(score);
  try
  {
   PlayNotice.Hook=(title,head,text)=>told.Add((title,head,text));
   // 1. 처음 모습과 곡 고르기. 닫아도 쓰지 않는다
   using(var dialog=Open(chart))
   {
    Check(dialog.Text=="수동 기록 설정"&&dialog.Chart.Key==chart.Key&&dialog.BestShown=="990,000점 (99.00%)"&&dialog.Input.Blank&&!dialog.SaveButton.Enabled&&!dialog.DeleteArchiveButton.Enabled,"the manual window opens on the chart with its best shown and empty score boxes; save is locked until a score is typed, and the bulk delete while nothing was fetched");
    Check(dialog.UseNow.Checked&&dialog.FixedTime==null&&!dialog.ReplaceOption.Checked&&dialog.ReplaceOption.Enabled&&dialog.TargetShown=="980,000점 · "+Local(t.AddMinutes(3))&&dialog.PracticeShown&&!dialog.PracticeOption.Checked,"it starts as a new play at the saving time, shows the play a replace would change (the latest directly saved one), and offers the practice option on hard");
    int rows=store.Active.Plays.Count;long revision=store.Revision;
    Type(dialog,997654);
    Check(dialog.Input.RateBox.Text==99.76m.ToString("F2",culture)&&dialog.Input.Value==997654&&dialog.SaveButton.Enabled&&store.Active.Plays.Count==rows&&store.Revision==revision,"typing a score fills the rate and unlocks save, and nothing is written yet");
    dialog.SearchBox.Text="ZZZZ nonexistent song";Check(!dialog.SaveButton.Enabled&&dialog.BestShown=="곡을 고르면 표시됩니다","Empty search cannot save");
    dialog.SearchBox.Text="Fundamental Bagagee";Check(dialog.Songs.Items.Count==1,"Search covers title and artist");
    dialog.ButtonChoice.SelectedItem=8;dialog.Patterns.SelectedItem="SC";dialog.Judges.SelectedItem="하드판정";
    Check(dialog.Chart.Key==chart.Key&&dialog.BestShown=="990,000점 (99.00%)"&&dialog.Input.Value==997654&&dialog.SaveButton.Enabled,"Choosing the combination again reloads its best and keeps the typed score");
    dialog.Judges.SelectedItem="노말판정";
    Check(dialog.Chart.Key==normal.Key&&!dialog.PracticeShown&&dialog.BestShown=="990,000점 (99.00%)"&&dialog.TargetShown==ReplaceTargetView.None&&!dialog.ReplaceOption.Enabled,"normal has no practice option; a hard play that is only shown there is not a replace target");
    using var shot=new Bitmap(dialog.Width,dialog.Height);dialog.DrawToBitmap(shot,new Rectangle(Point.Empty,shot.Size));shot.Save(Path.Combine(AppContext.BaseDirectory,"records-preview.png"));
    dialog.Close();Check(store.Active.Plays.Count==rows&&store.Revision==revision,"Closing manual dialog does not write data");
   }
   // 2. 새 플레이: 최고점 이상·동점·미만 모두 한 판으로 남는다. 저장 시각은 저장을 누른 순간이다
   using(var dialog=Open(chart))
   {
    Type(dialog,997654);var before=DateTime.UtcNow;bool saved=dialog.TrySave();var row=store.Active.Plays[^1];
    Check(saved&&dialog.Saved&&row is {Score:997654,Source:PlaySource.Manual,Practice:false,Normal:NormalLink.Linked}&&row.Key==chart.Key&&row.At>=before&&row.At<=DateTime.UtcNow&&store.Active.Undo[^1].Kind==PlayOps.Add&&told.Count==0,"saving adds one manual play at the saving time; a hard play above the normal best is shown on normal too");
   }
   using(var dialog=Open(chart)){Type(dialog,997654);Check(dialog.TrySave()&&store.History(chart.Key).Count(p=>p.Score==997654)==2&&store.Active.Plays[^1].Normal==null,"an equal score is saved as one more play (equal does not beat the normal best)");}
   using(var dialog=Open(chart)){dialog.Input.RateBox.Text=95m.ToString("F2",culture);Check(dialog.Input.Value==950000&&dialog.TrySave()&&store.Active.Plays[^1] is {Score:950000,Source:PlaySource.Manual}&&store.Best(chart.Key)!.Score==997654,"a lower score typed as a rate is saved as its own play of the converted score; the best stays");}
   // 3. 하드·맥스의 연습·무효: 그 판정에만 남는다
   using(var dialog=Open(chart)){Type(dialog,999000);dialog.PracticeOption.Checked=true;Check(dialog.TrySave()&&store.Active.Plays[^1] is {Score:999000,Practice:true,Normal:null}&&store.Best(normal.Key)!.Score==997654,"a practice play is saved on hard only and is not shown on normal");}
   // 4. 플레이 시각을 직접 적는다: 지금 시각이 채워지고, 저장하는 순간의 시각과 초 단위로 견준다. 없는 날짜와 미래의 시각은 그 칸에서 알리고 적은 것을 지킨다
   var now=new DateTime(2026,11,23,21,35,8,DateTimeKind.Local);RecordDialog.Clock=()=>now.AddMilliseconds(400);
   using(var dialog=Open(normal))
   {
    Type(dialog,900000);dialog.UseNow.Checked=false;
    Check(dialog.TimeBox.Text=="2026-11-23 21:35:08"&&dialog.TimeProblem=="","unchecking 'use the saving time' fills the box with the local time now, to the second");
    int rows=store.Active.Plays.Count;
    dialog.TimeBox.Text="2026-11-23 21:35:09";
    Check(!dialog.TrySave()&&dialog.TimeProblem==RecordDialog.LateTime&&store.Active.Plays.Count==rows&&dialog.Input.Value==900000&&dialog.TimeBox.Text=="2026-11-23 21:35:09","a time one second in the future is refused at the box; the score and the typed time stay");
    dialog.TimeBox.Text="2026-11-31 10:00:00";
    Check(dialog.TimeProblem==""&&!dialog.TrySave()&&dialog.TimeProblem==RecordDialog.BadTime&&store.Active.Plays.Count==rows&&dialog.TimeBox.Text=="2026-11-31 10:00:00","a date that does not exist is refused at the box and kept as typed (the earlier message goes away when the box is edited)");
    dialog.TimeBox.Text="어제 저녁";Check(!dialog.TrySave()&&dialog.TimeProblem==RecordDialog.BadTime&&store.Active.Plays.Count==rows,"so is text that is not a date and time");
    dialog.TimeBox.Text="2026-11-23 21:35:08";
    Check(dialog.TrySave()&&store.Active.Plays[^1] is {Score:900000,Source:PlaySource.Manual}&&store.Active.Plays[^1].At==now.ToUniversalTime()&&store.Active.Plays.Count==rows+1&&told.Count==0,"the same second as now is accepted and saved as that time");
   }
   using(var dialog=Open(normal))
   {
    Type(dialog,910000);dialog.UseNow.Checked=false;dialog.TimeBox.Text="1999-03-04 05:06:07";
    Check(dialog.TrySave()&&store.Active.Plays[^1].At==new DateTime(1999,3,4,5,6,7,DateTimeKind.Local).ToUniversalTime()&&store.Best(normal.Key)!.Score==997654,"a time long before the program existed is accepted (there is no lower bound)");
   }
   using(var dialog=Open(normal))
   {
    Type(dialog,920000);dialog.UseNow.Checked=false;dialog.TimeBox.Text="2026-11-23 21:36:00";bool refused=!dialog.TrySave();now=now.AddMinutes(1);
    Check(refused&&dialog.TrySave()&&store.Active.Plays[^1] is {Score:920000}&&store.Active.Plays[^1].At==new DateTime(2026,11,23,21,36,0,DateTimeKind.Local).ToUniversalTime(),"the same typed time is accepted once that moment has passed: each save compares with the time of that save");
   }
   RecordDialog.Clock=null;
   // 5. 최근 플레이 1건 교체: 새 줄을 만들지 않고 그 판의 점수만 바꾼다. 원래 시각과 연습 여부는 그대로이고 창에서 잠긴다
   using(var dialog=Open(normal))
   {
    var target=store.ReplaceTarget(normal.Key)!;int rows=store.Active.Plays.Count;
    Type(dialog,930000);dialog.UseNow.Checked=false;dialog.ReplaceOption.Checked=true;
    Check(target.Score==920000&&dialog.TargetShown=="920,000점 · 2026-11-23 21:36:00"&&dialog.FixedTime=="2026-11-23 21:36:00"&&dialog.Input.Value==930000,"choosing replace shows the play's own time in place of the time input and keeps the typed score");
    Check(dialog.TrySave()&&store.Active.Plays.Count==rows&&store.Active.Plays.Single(p=>p.Seq==target.Seq)==target with{Score=930000}&&store.Active.Undo[^1].Kind==PlayOps.Replace&&told.Count==0,"saving changes only that play's score: no new row, the same time");
   }
   using(var dialog=Open(chart))
   {
    var target=store.ReplaceTarget(chart.Key)!;Type(dialog,998000);dialog.ReplaceOption.Checked=true;
    Check(target is {Score:999000,Practice:true}&&dialog.PracticeOption.Checked&&!dialog.PracticeOption.Enabled,"replace shows the play's original practice state and locks it");
    dialog.ReplaceOption.Checked=false;Check(!dialog.PracticeOption.Checked&&dialog.PracticeOption.Enabled&&dialog.FixedTime==null&&dialog.UseNow.Checked,"turning replace off gives the new-play options back");
    dialog.ReplaceOption.Checked=true;
    Check(dialog.TrySave()&&store.Active.Plays.Single(p=>p.Seq==target.Seq)==target with{Score=998000},"a replaced practice play stays practice with its own time, and is still not shown on normal");
   }
   // 6. 교체할 판이 없는 패턴(가져온 기록뿐): 최고 기록은 환산이라고 적고, 교체는 잠기고, 새 플레이는 더할 수 있다
   store.Import(new Dictionary<Selection,decimal>{[lone]=99.54m});
   using(var dialog=Open(lone))
   {
    Check(dialog.BestShown=="995,400점 (99.54% · 환산)"&&!dialog.ReplaceOption.Enabled&&!dialog.ReplaceOption.Checked&&dialog.TargetShown=="교체할 플레이가 없습니다."&&dialog.DeleteArchiveButton.Enabled,"with only a fetched record the best is shown as converted and there is nothing to replace");
    Type(dialog,996800);Check(dialog.TrySave()&&store.History(lone.Key).Count==2&&store.Active.Plays[^1] is {Score:996800,Source:PlaySource.Manual},"a new play can still be added there");
   }
   // 7. 입력하는 동안 최고 기록이 바뀌었다: 저장하지 않고 두 값을 보인다. 창의 최고 기록이 고쳐지고, 다시 누르면 저장한다. RATE 만 남은 기록은 RATE 만, 지워졌으면 '미등록'
   using(var dialog=Open(lone))
   {
    Type(dialog,990000);store.Add(lone,997000,DateTime.UtcNow);int rows=store.Active.Plays.Count;
    Check(!dialog.TrySave()&&store.Active.Plays.Count==rows&&told.Count==1&&told[0]==("수동 기록 설정","입력하는 동안 기록이 바뀌었습니다.","현재 최고 기록: 997,000점 (99.70%)\n입력한 점수: 990,000점 (99.00%)\n입력한 점수를 저장하려면 수동 기록 설정에서 ‘저장’을 다시 눌러 주세요.")
     &&dialog.BestShown=="997,000점 (99.70%)"&&dialog.Input.Value==990000&&!dialog.Saved,"when the record changed while typing, the first save stops with both values shown and the window shows the new best; the typed score stays");
    Check(dialog.TrySave()&&store.Active.Plays[^1] is {Score:990000}&&told.Count==1,"saving again adds the play");
   }
   told.Clear();
   using(var dialog=Open(late))
   {
    Type(dialog,991000);store.Import(new Dictionary<Selection,decimal>{[late]=99.54m});
    Check(dialog.BestShown=="미등록"&&!dialog.TrySave()&&told.Count==1&&told[0].Text.StartsWith("현재 최고 기록: 99.54%\n입력한 점수: 991,000점 (99.10%)\n")&&dialog.BestShown=="995,400점 (99.54% · 환산)","a rate-only record is named in the notice with the rate that is really kept");
    store.Delete(store.History(late.Key).Single().Seq);
    Check(!dialog.TrySave()&&told.Count==2&&told[1].Text.StartsWith("현재 최고 기록: 미등록\n")&&dialog.BestShown=="미등록","a record that was deleted meanwhile is named as unregistered");
    Check(dialog.TrySave()&&store.History(late.Key).Single() is {Score:991000,Source:PlaySource.Manual},"and the next save goes through");
   }
   told.Clear();
   // 8. 입력하는 동안 교체할 판이 바뀌었다: 같은 판의 점수가 달라졌을 때, 점수·시각이 같은 다른 판이 대상이 됐을 때, 대상이 사라졌을 때
   using(var dialog=Open(normal))
   {
    var target=store.ReplaceTarget(normal.Key)!;Type(dialog,940000);dialog.ReplaceOption.Checked=true;store.Edit(target.Seq,931000);
    Check(!dialog.TrySave()&&told.Count==1&&told[0]==("수동 기록 설정","입력하는 동안 교체할 플레이가 바뀌었습니다.","현재 교체할 플레이: 2026-11-23 21:36:00 · 931,000점 (93.10%)\n입력한 점수: 940,000점 (94.00%)\n교체할 플레이를 확인한 뒤 ‘저장’을 다시 눌러 주세요.")
     &&store.Active.Plays.Single(p=>p.Seq==target.Seq).Score==931000&&dialog.ReplaceOption.Checked&&dialog.Input.Value==940000&&dialog.TargetShown=="931,000점 · 2026-11-23 21:36:00","when the play to replace was edited meanwhile, the save stops and shows the play as it is now with the typed score");
    var same=store.ReplaceTarget(normal.Key)!;store.Delete(same.Seq);var twin=store.Add(normal,same.Score,same.At!.Value,PlaySource.Manual);
    Check(twin.Seq!=same.Seq&&twin.Score==same.Score&&twin.At==same.At&&!dialog.TrySave()&&told.Count==2&&told[1].Head=="입력하는 동안 교체할 플레이가 바뀌었습니다."&&store.Active.Plays.Single(p=>p.Seq==twin.Seq).Score==931000,"a different play with the same score and time is still a different play: the save stops again");
    Check(dialog.TrySave()&&store.Active.Plays.Single(p=>p.Seq==twin.Seq).Score==940000&&told.Count==2,"saving again replaces the play that is shown now");
   }
   told.Clear();store.Add(single,900000,t,PlaySource.Manual);
   using(var dialog=Open(single))
   {
    Type(dialog,910000);dialog.ReplaceOption.Checked=true;store.Delete(store.History(single.Key).Single().Seq);int rows=store.Active.Plays.Count;
    Check(!dialog.TrySave()&&told.Count==1&&told[0].Title=="수동 기록 설정"&&told[0].Head=="교체할 플레이가 없습니다."&&store.Active.Plays.Count==rows&&!dialog.ReplaceOption.Checked&&!dialog.ReplaceOption.Enabled&&dialog.Input.Value==910000&&dialog.FixedTime==null,"when the play to replace is gone, the save stops, the replace option is turned off and locked, and the typed score stays");
    Check(dialog.TrySave()&&store.History(single.Key).Single() is {Score:910000,Source:PlaySource.Manual}&&told.Count==1,"saving again adds it as a new play");
   }
   told.Clear();
   // 9. 입력하는 동안 프로필이 바뀌었다: 어느 프로필에도 저장하지 않는다
   string home=store.Active.Id;
   using(var dialog=Open(normal))
   {
    Type(dialog,960000);int rows=store.Active.Plays.Count;store.Create("연습",false);store.Import(new Dictionary<Selection,decimal>{[lone]=90m});
    Check(!dialog.TrySave()&&told.Count==1&&told[0]==("수동 기록 설정","입력하는 동안 프로필이 바뀌었습니다.","입력을 시작한 프로필: 기본\n현재 프로필: 연습\n점수는 저장되지 않았습니다. 수동 기록 설정을 닫고, 기록을 남길 프로필에서 다시 열어 주세요.")
     &&store.Active.Plays.Count==1&&store.Data.Profiles.Single(p=>p.Id==home).Plays.Count==rows&&!dialog.Saved,"when the profile changed while typing, nothing is saved to either profile and both names are shown");
    store.Select(home);
   }
   told.Clear();
   // 10. 저장하는 순간에 고른 패턴이 곡 목록에 없다
   using(var dialog=Open(normal))
   {
    Type(dialog,960000);int rows=store.Active.Plays.Count;var listed=new Chart(8,"SC");int at=song.Charts.IndexOf(listed);song.Charts.RemoveAt(at);
    try{Check(!dialog.TrySave()&&told.Count==1&&told[0]==("수동 기록 설정","선택한 패턴을 확인할 수 없습니다.","곡과 버튼·난이도를 다시 선택한 뒤 저장해 주세요.")&&store.Active.Plays.Count==rows,"a pattern that is not in the song list at the moment of saving is refused");}
    finally{song.Charts.Insert(at,listed);}
   }
   told.Clear();
   // 11. 기록 파일에 쓰지 못했다: 알리고 창에 남는다. 쓸 수 있게 되면 같은 저장이 된다
   using(var dialog=Open(normal))
   {
    Type(dialog,960000);int rows=store.Active.Plays.Count;
    using(new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.None))Check(!dialog.TrySave()&&told.Count==1&&told[0].Title=="수동 기록 설정"&&told[0].Head=="점수를 저장하지 못했습니다."&&told[0].Text==Locked&&store.Active.Plays.Count==rows&&!dialog.Saved,"a save that cannot be written says why and what to do, and leaves the records as they were");
    Check(dialog.TrySave()&&store.Active.Plays[^1].Score==960000,"the same save goes through once the file is free");
   }
   told.Clear();
   // 12. 바닥줄의 'V아카이브 삭제': 지금 프로필의 그 출처만, 한 작업으로. 확인이 보여 준 건수가 승인하는 순간에도 그대로일 때만 지운다
   store.Import(new Dictionary<Selection,decimal>{[late]=98m,[single]=97m});int fetched=store.Count(PlaySource.VArchive);
   using(var dialog=Open(normal))
   {
    (string Profile,int Count)? asked=null;int rows=store.Active.Plays.Count,steps=store.Active.Undo.Count;
    RecordDialog.BulkHook=(profile,count)=>{asked=(profile,count);return false;};dialog.DeleteImported();
    Check(fetched==3&&dialog.DeleteArchiveButton.Enabled&&asked==("기본",3)&&store.Count(PlaySource.VArchive)==3&&store.Active.Plays.Count==rows&&store.Active.Undo.Count==steps,"the bulk delete asks with the profile name and the number of fetched rows; cancel deletes nothing");
    RecordDialog.BulkHook=(_,_)=>true;dialog.DeleteImported();
    Check(store.Count(PlaySource.VArchive)==0&&store.Active.Plays.Count==rows-3&&store.Active.Undo[^1] is {Kind:PlayOps.DeleteVArchive,Plays.Count:3}&&!dialog.DeleteArchiveButton.Enabled&&!dialog.Saved&&told.Count==0
     &&store.Data.Profiles.Single(p=>p.Name=="연습").Plays.Count(p=>p.Source==PlaySource.VArchive)==1,"approving removes exactly the fetched rows of this profile as one step; own plays and the other profile stay");
    store.Undo();Check(store.Count(PlaySource.VArchive)==3&&store.Active.Plays.Count==rows,"one undo brings them all back");
    RecordDialog.BulkHook=(_,_)=>{store.Import(new Dictionary<Selection,decimal>{[spare]=91m});return true;};dialog.DeleteImported();
    Check(store.Count(PlaySource.VArchive)==4&&told.Count==1&&told[0]==("V아카이브 일괄 삭제","기록이 바뀌어 삭제를 멈췄습니다.","‘V아카이브 삭제’를 다시 눌러 주세요.")&&dialog.DeleteArchiveButton.Enabled,"when the number of rows changed while the question was open, nothing is deleted");
    told.Clear();RecordDialog.BulkHook=(_,_)=>true;int kept=store.Count(PlaySource.VArchive),rowsNow=store.Active.Plays.Count;
    using(new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.None))dialog.DeleteImported();
    Check(store.Count(PlaySource.VArchive)==kept&&store.Active.Plays.Count==rowsNow&&told.Count==1&&told[0]==("V아카이브 일괄 삭제","기록을 삭제하지 못했습니다.",Locked),"a bulk delete that cannot be written says why and what to do, and deletes nothing");
   }
   // 12b. 바닥줄의 '컬렉션 삭제': V아카이브와 같은 규칙이고, 확인 창의 제목·질문과 멈춤 안내는 컬렉션의 글이다
   {
    void Seed(Selection chart,int score){var profile=store.Active;profile.Plays.Add(new Play(profile.NextSeq++,chart.Key,score,null,PlaySource.Collection));store.Save();}
    told.Clear();Seed(lone,950000);Seed(late,960000);int archived=store.Count(PlaySource.VArchive);   // 12 의 마지막 검사(쓸 수 없을 때)가 남긴 알림을 비우고 시작한다
    using var dialog=Open(normal);int rows=store.Active.Plays.Count,steps=store.Active.Undo.Count;
    (string Profile,int Count)? asked=null;RecordDialog.BulkHook=(profile,count)=>{asked=(profile,count);return false;};dialog.DeleteImported(PlaySource.Collection);
    Check(dialog.DeleteCollectionButton.Enabled&&dialog.DeleteCollectionButton.Text=="컬렉션 삭제"&&dialog.DeleteArchiveButton.Right<dialog.DeleteCollectionButton.Left&&asked==("기본",2)&&store.Count(PlaySource.Collection)==2&&store.Active.Undo.Count==steps,
     "the collection bulk delete sits right of the V-ARCHIVE one, asks with the profile and the number of collection rows; cancel deletes nothing");
    RecordDialog.BulkHook=(_,_)=>true;dialog.DeleteImported(PlaySource.Collection);
    Check(store.Count(PlaySource.Collection)==0&&store.Count(PlaySource.VArchive)==archived&&store.Active.Plays.Count==rows-2&&store.Active.Undo[^1] is {Kind:PlayOps.DeleteCollection,Plays.Count:2}&&!dialog.DeleteCollectionButton.Enabled&&told.Count==0,
     "approving removes only the collection rows of this profile as one step; V-ARCHIVE rows stay");
    store.Undo();Check(store.Count(PlaySource.Collection)==2,"one undo brings the collection rows back");
    RecordDialog.BulkHook=(_,_)=>{Seed(single,970000);return true;};dialog.DeleteImported(PlaySource.Collection);
    Check(store.Count(PlaySource.Collection)==3&&told.Count==1&&told[0]==("컬렉션 일괄 삭제","기록이 바뀌어 삭제를 멈췄습니다.","‘컬렉션 삭제’를 다시 눌러 주세요."),"when the collection rows changed while the question was open, nothing is deleted and the notice names the collection button");
    told.Clear();RecordDialog.BulkHook=null;
    using(var confirm=new BulkDeleteDialog("기본",3,PlaySource.Collection))
     Check(confirm.Text=="컬렉션 일괄 삭제"&&All(confirm).Any(c=>c.Text=="컬렉션에서 가져온 기록을 모두 삭제할까요?")&&All(confirm).Any(c=>c.AccessibleName=="삭제할 기록 3건"),"the collection confirmation has its own title and question, and the count");
    store.DeleteSource(PlaySource.Collection);
    // 창이 열린 동안 다른 길로 마지막 컬렉션 줄이 사라지면 '컬렉션 삭제'가 꺼진다. 입력 중인 점수는 그대로다
    Seed(lone,950000);using(var open=Open(normal))
    {
     Type(open,912345);bool before=open.DeleteCollectionButton.Enabled;store.DeleteSource(PlaySource.Collection);
     for(int i=0;i<40&&open.DeleteCollectionButton.Enabled;i++){Application.DoEvents();Thread.Sleep(20);}
     Check(before&&!open.DeleteCollectionButton.Enabled&&open.Input.ScoreBox.Text==N(912345),"when the last collection row disappears while the window is open, the collection button turns off and the typed score stays");
     store.Undo();for(int i=0;i<40&&!open.DeleteCollectionButton.Enabled;i++){Application.DoEvents();Thread.Sleep(20);}
     Check(open.DeleteCollectionButton.Enabled,"and turns on again when it comes back");
    }
    store.DeleteSource(PlaySource.Collection);
   }
   // 13. 연습·무효의 물음표: 키보드로도 갈 수 있고 Space·Enter 로 설명을 펴고 접는다
   using(var dialog=Open(chart))
   {
    var mark=dialog.HelpMark;
    Check(mark.TabStop&&mark.CanSelect&&dialog.GetNextControl(dialog.PracticeOption,true)==mark&&!dialog.HelpShown,"the help mark is the next Tab stop after the practice option");
    mark.Press(Keys.Space);bool opened=dialog.HelpShown;mark.Press(Keys.Enter);
    Check(opened&&!dialog.HelpShown,"Space opens the explanation and Enter folds it again");
   }
   // 14. 되돌리기 확인의 곡 이름: 이름이 같은 곡은 아티스트를 붙여 가른다
   {
    var alones=catalog.Songs.Where(s=>s.Name=="Alone").ToList();
    string Shown(Song which){var play=new Play(1,$"{which.Id}|4|HARD|하드",990000,t,PlaySource.Play);using var dialog=new UndoDialog(UndoView.Of(new Operation(PlayOps.Add,t,2,new(){new(null,play)}),"기본"),catalog);return All(dialog).OfType<SongLine>().Single().Title;}
    Check(alones.Count==2&&alones[0].Artist!=alones[1].Artist&&Shown(alones[0])==$"Alone ({alones[0].Artist})"&&Shown(alones[1])==$"Alone ({alones[1].Artist})"&&Shown(song)=="Fundamental"&&UndoDialog.SongTitle(catalog,"없는곡|4|SC|노말")=="곡 정보 없음 (곡 번호 없는곡)",
     "the undo question names same-titled songs with their artist, and other songs by title alone");
   }
  }
  finally{PlayNotice.Hook=null;RecordDialog.Clock=null;RecordDialog.BulkHook=null;}
  File.WriteAllLines(Path.Combine(AppContext.BaseDirectory,"manual-validation.txt"),notes);
 }
}
