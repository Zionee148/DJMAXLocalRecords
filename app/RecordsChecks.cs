using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
namespace LadderRecords;
// 기록 목록과 기록 히스토리를 창을 띄워 본다. 창 검사(--dialogs-check)에 들어 있다.
// 목록: 줄과 칸의 글, 검색·필터, 정렬(최고 기록, 최근 플레이를 모르는 줄은 아래), 예전 설정의 이름 옮기기, 곡명 칸이 남는 폭을 쓰는 것.
// 히스토리: 표의 줄과 PB, 한 판 수정·삭제(확인하는 동안 바뀐 판·이미 지워진 판), 노말에 보이는 반영만 삭제, 마지막 판을 지운 뒤의 빈 창, 일시 미상 기록.
// 수정·삭제 확인 창과 안내창: 문구, 기본 선택(취소), SCORE·RATE 입력의 연동. 그림을 실행 파일 옆에 남긴다
static class RecordsChecks
{
 [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr window,int message,IntPtr w,IntPtr l);
 public static void Run(Catalog catalog){
  var passed=new List<string>();void Check(bool value,string name){if(!value)throw new Exception(name);passed.Add("PASS: "+name);}
  var path=Path.Combine(AppContext.BaseDirectory,"test-data",Guid.NewGuid()+".records.json");Directory.CreateDirectory(Path.GetDirectoryName(path)!);var store=new RecordStore(path);
  var fundamental=catalog.Songs.Single(s=>s.Name=="Fundamental");var hero=catalog.Songs.Single(s=>s.Name=="Hero");
  var fundHard=new Selection(fundamental.Id,8,"SC","하드",1);var fundNormal=fundHard with{Judge="노말"};var heroHard=new Selection(hero.Id,8,"SC","하드",1);var heroNormal=heroHard with{Judge="노말"};
  var t=new DateTime(2026,11,20,10,0,0,DateTimeKind.Utc);
  store.Add(fundHard,994800,t);store.Add(heroHard,1000000,t.AddMinutes(1));
  foreach(var song in catalog.Songs.GroupBy(s=>s.Name).First(g=>g.Count()>1)){var c=song.Charts[0];store.Add(new(song.Id,c.Button,c.Pattern,"노말",1),981200,t.AddMinutes(2));}
  for(int i=0;i<9;i++)store.Add(fundHard,980000+i,t.AddMinutes(10+i));
  store.Add(heroHard,990000,new DateTime(2031,1,1,0,0,0,DateTimeKind.Utc));
  // Fundamental 의 노말: 하드에서 올라온 반영을 끊고 0점의 한 판을 넣는다(0점도 기록이다)
  store.CancelNormal(store.History(fundNormal.Key).Single().Seq);store.Add(fundNormal,0,t.AddMinutes(30),PlaySource.Manual);
  // V아카이브에서만 온 기록: 최근 플레이를 모르고, 점수는 환산이다
  var other=fundamental.Charts.First(c=>!(c.Button==8&&c.Pattern=="SC"));var imported=new Selection(fundamental.Id,other.Button,other.Pattern,"노말",1);
  store.Import(new Dictionary<Selection,decimal>{[imported]=99.54m});
  var rows=RecordRows.Read(catalog,store,store.Active);
  Check(rows.Count==7&&rows.Any(r=>r.Accuracy==0)&&rows.Single(r=>r.Chart.Key==heroNormal.Key).Best.Key==heroHard.Key,"All registered records including zero and the hard play shown on normal");
  Check(RecordRows.Filter(rows,"FUNDAMENTAL viphex",null,"전체","전체").Count()==3,"Case-insensitive title and artist token search");
  Check(RecordRows.Filter(rows,"",8,"SC","하드판정").Count()==2&&RecordRows.Filter(rows,"",8,"SC","하드").Count()==2&&RecordRows.Filter(rows,"",null,"전체","하드·맥스판정").Count()==2,"Combined filters, with the judge named as shown or as older settings kept it");
  Check(RecordRows.Filter(rows,"not a song",null,"전체","전체").Count()==0,"No search match");
  Check(RecordOrdering.Column("Accuracy")=="Best"&&RecordOrdering.Column("RawScore")=="Best"&&RecordOrdering.Column("PlayCount")=="LastPlayedAt"&&RecordOrdering.Column(null)=="LastPlayedAt"&&RecordRows.JudgeFilter("하드·맥스")=="하드·맥스판정"&&RecordRows.JudgeFilter("없는 이름")=="전체","Names kept by older settings map to the present columns and filters");
  string first=store.Active.Id;store.Create("분리",false);Check(RecordRows.Read(catalog,store,store.Active).Count==0,"Profile isolation");store.Select(first);

  var notices=new List<(string Title,string Headline,string Text)>();PlayNotice.Hook=(title,headline,text)=>notices.Add((title,headline,text));
  try{
  using var dialog=new RecordsDialog(catalog,store);dialog.Show();Application.DoEvents();
  T Field<T>(string name)=>(T)typeof(RecordsDialog).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(dialog)!;
  var grid=Field<RecordsView>("grid");Check(grid.RowCount==rows.Count,"Grid displays all rows");
  Check(grid.SortedColumn?.Name=="LastPlayedAt"&&grid.SortOrder==SortOrder.Descending&&grid.ItemAt(0)?.SongName=="Hero"&&grid.ItemAt(grid.RowCount-1)!.Chart.Key==imported.Key,"Default sort is the most recent play first; the row with no known play time is last");
  grid.Sort("LastPlayedAt",ListSortDirection.Ascending);Check(grid.ItemAt(grid.RowCount-1)!.Chart.Key==imported.Key&&grid.ItemAt(0)!.LastPlayedAt<grid.ItemAt(1)!.LastPlayedAt,"Oldest first still keeps the unknown play time at the bottom");
  grid.Sort("Best",ListSortDirection.Descending);Check(grid.ItemAt(0)!.RawScore==1000000&&grid.ItemAt(grid.RowCount-1)!.RawScore==0,"Best record sorts by score: 1,000,000 first, 0 last");
  // 칸은 번호가 아니라 이름으로 찾는다. 줄마다 그 기록의 판정, 최고 기록(SCORE 와 RATE 한 칸), 최근 플레이가 보여야 한다
  string Cell(int row,string column)=>grid.Items[row].SubItems[grid.Columns[column]!.Index].Text;
  Check(Enumerable.Range(0,grid.RowCount).All(i=>grid.ItemAt(i) is RegisteredRecord row&&Cell(i,"Song")==row.SongName&&Cell(i,"Judge")==row.Judge+"판정"&&Cell(i,"Best")==ScoreText.Full(row.Best)&&Cell(i,"LastPlayedAt")==(row.LastPlayedAt?.ToString("yyyy-MM-dd HH:mm:ss")??"—")),"Sorted virtual rows preserve record identity and display");
  Check(grid.Columns.Cast<ColumnHeader>().Select(c=>c.Name).SequenceEqual(new[]{"Song","Artist","Button","Pattern","Judge","Best","LastPlayedAt"})&&ScoreText.Full(grid.ItemAt(0)!.Best)=="1,000,000점 (100.00%)"&&ScoreText.Full(rows.Single(r=>r.Chart.Key==imported.Key).Best)=="995,400점 (99.54% · 환산)","Seven columns; the best record is one cell, and a RATE-only record is marked as converted");
  Check(grid.Columns.Cast<ColumnHeader>().Sum(c=>c.Width)==grid.ClientSize.Width,$"The song column takes the spare width ({grid.Columns.Cast<ColumnHeader>().Sum(c=>c.Width)} / {grid.ClientSize.Width})");
  var search=Field<InputBox>("search").Box;var historyButton=Field<Button>("historyButton");
  search.Text="Fundamental";Check(grid.RowCount==3,"Live grid search");Field<ComboBox>("judge").SelectedItem="하드판정";Check(grid.RowCount==1,"Live judge filter");
  Field<ComboBox>("pattern").SelectedItem="NORMAL";Check(grid.RowCount==0&&!historyButton.Enabled,"Empty grid result; the history button is off with nothing selected");
  Field<ComboBox>("pattern").SelectedItem="전체";Field<ComboBox>("judge").SelectedItem="전체";search.Clear();Check(grid.RowCount==rows.Count&&historyButton.Enabled,"Reset view");
  void Save(Form form,string name){Application.DoEvents();using var shot=new Bitmap(form.Width,form.Height);form.DrawToBitmap(shot,new Rectangle(Point.Empty,shot.Size));shot.Save(Path.Combine(AppContext.BaseDirectory,name+".png"));}
  grid.Sort("LastPlayedAt",ListSortDirection.Descending);Save(dialog,"records-list-preview");
  // 삭제 확인 창이 목록에서 고른 곡·버튼·난이도·판정을 보이는가, 안내 줄이 창 안에 다 들어오는가
  // notes 는 그 창에 보여야 하는 안내 줄 전부다(없어야 하면 비운다). 안내가 통째로 사라진 것도 여기서 잡힌다
  void Matches(DeletePlayDialog confirm,Selection chart,string song,params string[] notes)
  {
   Application.DoEvents();var line=Inside(confirm).OfType<SongLine>().Single();
   Check(line.Title==song&&line.Chips.SequenceEqual(new[]{$"{chart.Button}B",chart.Pattern,chart.Judge+"판정"}),$"The confirmation shows the chart picked in the list ({chart.Judge})");
   Check(confirm.NotesShown==string.Join("\n",notes),$"The confirmation shows exactly the notes it should ({chart.Judge}: {notes.Length})");
   if(notes.Length==0)return;
   var note=Inside(confirm).OfType<Label>().Single(l=>l.Visible&&l.Text==confirm.NotesShown);var seen=note.Parent!.RectangleToScreen(note.Bounds);var inside=confirm.RectangleToScreen(confirm.ClientRectangle);
   Check(seen.Left>=inside.Left&&seen.Right<=inside.Right-8&&seen.Bottom<inside.Bottom,$"The whole note fits inside the confirmation ({chart.Judge}: {seen} in {inside})");
  }
  // 표의 줄 아래 빈 곳을 누른다(실제 마우스 메시지)
  void ClickBelow(HistoryTable rows){var last=rows.GetItemRect(rows.Rows.Count-1);var spot=(IntPtr)(((last.Bottom+14)<<16)|24);SendMessage(rows.Handle,0x0201,(IntPtr)1,spot);SendMessage(rows.Handle,0x0202,IntPtr.Zero,spot);Application.DoEvents();}
  void Reread(HistoryDialog window)=>typeof(HistoryDialog).GetMethod("Reload",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);

  // ── 히스토리: 하드 열 판(994,800 하나와 980,000 ~ 980,008)
  void Open(Selection chart){int index=Enumerable.Range(0,grid.RowCount).First(i=>grid.ItemAt(i)!.Chart.Key==chart.Key);grid.SelectIndex(index);dialog.OpenHistory();Application.DoEvents();}
  Open(fundHard);var history=dialog.History!;
  Check(history.Visible&&dialog.Enabled&&history.Text=="기록 히스토리 · "+store.Active.Name&&history.Chart.Key==fundHard.Key,"The history opens beside the list without blocking it");
  var table=history.Table;
  Check(table.Rows.Count==10&&table.Rows[0].Play.Score==980008&&table.Rows[^1].Play.Score==994800&&history.PickedSeq==table.Rows[0].Play.Seq,"Ten plays, latest first, with the latest play picked");
  Check(table.Rows.Count(r=>r.Tie)==1&&table.Rows.Single(r=>r.Pb).Play.Score==994800&&history.Model.Best!.Score==994800,"The best play carries the PB mark");
  Check(table.Items[0].SubItems[0].Text=="980,008점 (98.00%)"&&table.Items[0].SubItems[1].Text==ScoreText.Time(table.Rows[0].Play),"Table rows read as score and play time");
  Save(history,"history-preview");
  // ── 그래프: 열 판은 한 화면에 다 보인다. 점을 누르면 그 판을 고르고 표가 그 줄을 고른다. 점과 PB 기준선에 마우스를 올리면 안내가 뜬다
  var graph=history.Graph;
  Check(graph.Visible&&history.Fold.Expanded&&graph.Total==10&&graph.Points.Count==10&&graph.Points.Select(p=>p.Number).SequenceEqual(Enumerable.Range(1,10))&&graph.Points.Single(p=>p.Pb).Number==1&&history.RangeBox.Visible&&history.PbLineBox.Checked&&graph.PbLine&&table.Height<history.Graph.Top,"The graph shows the ten plays oldest first, with the PB mark on the best play");
  Check(graph.Press(graph.SpotOf(4))&&history.PickedSeq==graph.Points[4].Play.Seq&&table.Picked?.Play.Seq==history.PickedSeq&&!graph.Press(new Point(3,3)),"Clicking a point picks that play, and the table selects its row");
  graph.Hover(graph.SpotOf(4));Check(graph.TipText==GraphMath.PointTip(graph.Points[4])&&graph.TipText!.StartsWith("5회 · "),"Hovering a point names the play");
  graph.Hover(graph.LineSpot());Check(graph.TipText==GraphMath.PbTip(history.Model)&&graph.TipText!.StartsWith("PB · 994,800점 · "),"Hovering the PB line names the PB");
  graph.Hover(new Point(3,3));Check(graph.TipText==null,"Away from both there is no tip");
  // PB 기준선을 끄면 선의 안내도 없다. PB 근처 확대는 PB 에서 5,000 안의 판만 점으로 그린다(회차는 그대로다)
  history.PbLineBox.Checked=false;graph.Hover(graph.LineSpot());Check(!graph.PbLine&&graph.TipText==null&&Field<HistoryPrefs>("graphPrefs").PbLine==false,"Turning the PB line off removes it, and the choice is kept");history.PbLineBox.Checked=true;
  history.RangeBox.SelectedIndex=1;Check(graph.Zoom&&graph.Axis is {Near:true,Bottom:989800,Top:994800}&&graph.Points.Count==10&&graph.Points.Count(p=>p.Visible)==1,"The zoom keeps the play numbers and draws only the plays near the PB");
  store.Add(fundHard,994000,t.AddMinutes(41));Reread(history);Check(graph.Zoom&&history.RangeBox.SelectedIndex==1&&graph.Total==11&&graph.Points.Count(p=>p.Visible)==2,"A new play on the same chart keeps the zoom");store.Undo();Reread(history);
  // 접으면 그래프가 사라지고 그 자리를 표가 쓴다. 창의 크기와 아래 단추의 자리는 그대로다
  {
   int tall=table.Height,buttons=Part<Button>(history,"edit").Parent!.Top;var size=history.Size;history.Fold.Flip();Application.DoEvents();
   Check(!graph.Visible&&!history.RangeBox.Visible&&!history.PbLineBox.Visible&&table.Height>tall+Theme.S(200)&&history.Size==size&&Part<Button>(history,"edit").Parent!.Top==buttons&&Field<HistoryPrefs>("graphPrefs").Folded,"Folding the graph gives its room to the table; the window and the buttons stay where they are");
   Save(history,"history-folded-preview");history.Fold.Flip();Application.DoEvents();Check(graph.Visible&&table.Height==tall&&graph.Zoom,"Unfolding brings it back as it was");
  }
  // 다른 판정을 열면 전체 그래프로 시작한다
  Open(heroHard);Check(!graph.Zoom&&history.RangeBox.SelectedIndex==0&&graph.Total==2,"Opening another chart starts with the whole graph");Open(fundHard);
  // 열두 판을 넘으면 가장 최근 구간부터 보이고, 막대(와 화살표 키)로 앞선 회차를 본다. 회차의 번호는 다시 매기지 않는다
  for(int i=0;i<4;i++)store.Add(fundHard,981000+i,t.AddMinutes(60+i));Reread(history);
  Check(graph.Total==14&&graph.Start==2&&graph.Points.Select(p=>p.Number).SequenceEqual(Enumerable.Range(3,12)),"With fourteen plays the latest twelve show first");
  Check(graph.Key(Keys.Left)&&graph.Start==1&&graph.Key(Keys.Home)&&graph.Start==0&&graph.Points[0].Number==1&&graph.Points[0].Pb&&graph.Key(Keys.End)&&graph.Start==2&&!graph.Key(Keys.Up),"The arrow, Home and End keys move through the plays");
  graph.Start=0;store.Add(fundHard,981100,t.AddMinutes(70));Reread(history);Check(graph.Start==0&&graph.Total==15,"While looking at earlier plays a new play does not move the view");
  graph.Start=99;store.Add(fundHard,981200,t.AddMinutes(71));Reread(history);Check(graph.Start==4&&graph.Points[^1].Number==16,"At the end the view follows the new play");
  for(int i=0;i<6;i++)store.Undo();Reread(history);Check(graph.Total==10&&graph.Start==0,"Back to ten plays");
  history.Pick(table.Rows[0].Play.Seq);
  // 표의 빈 곳을 눌러 선택을 풀면 수정·삭제의 대상도 없어진다
  ClickBelow(table);
  Check(table.Picked==null&&history.PickedSeq==null&&!Part<Button>(history,"edit").Enabled&&!Part<Button>(history,"delete").Enabled&&history.Request()==null&&!history.EditSelected(_=>1)&&!history.DeleteSelected(r=>r.Candidates[0])&&store.History(fundHard.Key).Count==10,"Clicking the empty part of the table clears the selection, and nothing is left to edit or delete");
  // 정렬: 같은 히스토리를 다시 읽을 때는 고른 차례를 지키고, 다른 판정을 열면 최신순으로 시작한다
  table.Order(true,true);Check(table.ByScore&&table.Rows[0].Play.Score==994800,"Sorted by score, the best play is first");
  store.Add(fundHard,970000,t.AddMinutes(40));Reread(history);Check(table.ByScore&&table.Rows.Count==11&&table.Rows[0].Play.Score==994800,"Reloading the same history keeps the chosen order");
  store.Undo();Open(heroHard);Check(!table.ByScore&&table.Descending&&table.Rows[0].Play.Score==990000&&history.PickedSeq==table.Rows[0].Play.Seq,"Opening another chart starts latest first");
  Open(fundHard);Check(table.Rows.Count==10&&table.Rows[0].Play.Score==980008,"Back on the first chart, latest first");
  // 한 판 수정: 원래 시각은 그대로, 되돌리면 전의 점수
  var low=table.Rows[3].Play;history.Pick(low.Seq);
  Check(history.EditSelected(_=>985000)&&store.History(fundHard.Key).Single(p=>p.Seq==low.Seq) is {Score:985000} edited&&edited.At==low.At&&history.Table.Rows.Single(r=>r.Play.Seq==low.Seq).Play.Score==985000&&history.PickedSeq==low.Seq,"Editing one play changes its score only; the window shows it and keeps it picked");
  store.Undo();Check(store.History(fundHard.Key).Single(p=>p.Seq==low.Seq)==low,"Undo restores the play as it was");
  Check(!history.EditSelected(_=>null)&&!history.EditSelected(p=>p.Score)&&store.Active.Undo.Count>0&&store.History(fundHard.Key).Single(p=>p.Seq==low.Seq)==low,"Cancelling, or saving the same score, changes nothing");
  // 수정을 확인하는 동안 그 판이 바뀐다
  notices.Clear();int steps=store.Active.Undo.Count;
  Check(!history.EditSelected(p=>{store.Edit(p.Seq,970000);return 985000;})&&notices.Single().Headline=="기록이 바뀌어 수정을 멈췄습니다."&&store.History(fundHard.Key).Single(p=>p.Seq==low.Seq).Score==970000&&store.Active.Undo.Count==steps+1,"If the play changes while the edit is open, the edit stops with a notice");
  store.Undo();
  // 한 판 삭제와 되돌리기
  history.Pick(low.Seq);
  using(var confirm=new DeletePlayDialog(history.Request()!)){confirm.Show();Matches(confirm,fundHard,fundamental.Name);Check(confirm.NotesShown==""&&confirm.Chosen==low,"A plain play: no note, and the play to delete is the one picked");confirm.Close();}
  Check(!history.DeleteSelected(_=>null)&&store.History(fundHard.Key).Count==10,"Cancel deletion changes nothing");
  Check(history.DeleteSelected(request=>request.Candidates.Count==1&&!request.Reflected?request.Candidates[0]:null)&&store.History(fundHard.Key).Count==9&&history.Table.Rows.Count==9&&history.PickedSeq==null,"Deleting removes that one play");
  store.Undo();Check(store.History(fundHard.Key).Count==10,"Undo brings the deleted play back");
  // 기록 파일에 쓰지 못할 때(파일이 잠겨 있다): 수정도 삭제도 까닭과 다음 행동을 알리고 기록은 그대로다. 수동 저장과 같은 글이고, Windows 의 영문 오류를 그대로 보이지 않는다
  {
   const string locked="기록 파일을 다른 프로그램이 사용하고 있습니다.\n해당 프로그램을 닫은 뒤 다시 시도해 주세요.";
   var kept=store.History(fundHard.Key).Single(p=>p.Seq==low.Seq);history.Pick(low.Seq);notices.Clear();bool lockedEdit,lockedDelete;
   using(new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.None)){lockedEdit=history.EditSelected(_=>981000);history.Pick(low.Seq);lockedDelete=history.DeleteSelected(request=>request.Candidates[0]);}
   Check(!lockedEdit&&!lockedDelete&&notices.Count==2&&notices[0]==("기록 수정","기록을 수정하지 못했습니다.",locked)&&notices[1]==("기록 삭제","기록을 삭제하지 못했습니다.",locked)
    &&store.History(fundHard.Key).Count==10&&store.History(fundHard.Key).Single(p=>p.Seq==low.Seq)==kept,"An edit or a delete that cannot be written says why and what to do, and leaves the play as it was");
  }
  // 삭제를 확인하는 동안: 다른 판이 더해지는 것은 고른 판과 상관없다. 고른 판이 바뀌면 멈춘다. 고른 판이 먼저 지워지면 알리고 목록으로 돌아간다
  history.Pick(low.Seq);notices.Clear();
  Check(!history.DeleteSelected(request=>{store.Edit(low.Seq,970000);return request.Candidates[0];})&&notices.Single().Headline=="기록이 바뀌어 삭제를 멈췄습니다."&&store.History(fundHard.Key).Count==10,"If the play changes while the confirmation is open, nothing is deleted");
  store.Undo();history.Pick(low.Seq);notices.Clear();
  Check(!history.DeleteSelected(request=>{store.Delete(low.Seq);return request.Candidates[0];})&&notices.Single() is {Title:"기록 삭제",Headline:"선택한 기록이 이미 삭제되었습니다."}&&history.ReturnedToList&&dialog.History==null,"If the play is already gone, the notice returns to the list");
  store.Undo();

  // ── 노말에 보이는 하드의 판: 고칠 수 없고, 지우면 반영만 끊긴다. 마지막 판이면 빈 히스토리가 남는다
  Open(heroNormal);history=dialog.History!;var shown=history.Table.Rows.Single();
  Check(shown.Reflected&&!shown.Editable&&shown.Pb&&history.Table.Items[0].SubItems[1].Text.StartsWith("2026-11-20")&&!Part<Button>(history,"edit").Enabled&&Part<Button>(history,"delete").Enabled,"A hard play shown on normal cannot be edited there");
  var request=history.Request()!;
  Check(request.Reflected&&DeletePlayDialog.Question(true)=="이 플레이의 노말판정 반영만 삭제할까요?"&&DeletePlayDialog.Notes(request,request.Candidates[0]).SequenceEqual(new[]{"원래 하드판정 기록은 유지됩니다.","이 반영을 삭제하면 이 패턴의 노말판정 기록은 미등록이 됩니다."}),"The confirmation says only the reflection goes, and that normal becomes empty");
  using(var confirm=new DeletePlayDialog(request)){confirm.Show();Save(confirm,"delete-confirm-normal-preview");Check(confirm.ActiveControl==confirm.CancelButton&&confirm.AcceptButton==confirm.CancelButton&&Inside(confirm).OfType<Button>().Any(b=>b.Text=="반영만 삭제"),"Delete confirmation defaults to cancel; the button names what it does");
   Matches(confirm,heroNormal,hero.Name,"원래 하드판정 기록은 유지됩니다.","이 반영을 삭제하면 이 패턴의 노말판정 기록은 미등록이 됩니다.");
   // 확인 창이 떠 있는 동안 같은 판정에 새 판이 저장되면 '미등록이 됩니다'는 맞지 않게 된다: 안내 줄만 다시 적는다
   store.Add(heroNormal,900000,t.AddMinutes(50));confirm.RefreshNotes();Check(confirm.NotesShown=="원래 하드판정 기록은 유지됩니다.","When another play arrives while the confirmation is open, the note about becoming empty goes away");
   store.Undo();confirm.RefreshNotes();Check(confirm.NotesShown.EndsWith("미등록이 됩니다."),"and comes back when that play is gone again");
   confirm.Close();}
  Check(history.DeleteSelected(r=>r.Candidates[0])&&store.History(heroNormal.Key).Count==0&&store.History(heroHard.Key).Count==2&&history.Visible&&history.Model.Best==null&&history.Table.Rows.Count==0,"Removing the last play leaves the history open and empty; the hard plays stay");
  Save(history,"history-empty-preview");
  store.Undo();typeof(HistoryDialog).GetMethod("Reload",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(history,null);
  Check(history.Table.Rows.Count==1&&history.Model.Best!.Score==1000000,"Undo shows the play again in the open history");
  // 하드의 원래 판: 노말에도 반영되어 있으면 함께 지워진다고 적는다
  Open(heroHard);history=dialog.History!;history.Pick(history.Model.All.Single(p=>p.Normal==NormalLink.Linked).Seq);request=history.Request()!;
  Check(!request.Reflected&&DeletePlayDialog.Notes(request,request.Candidates[0]).SequenceEqual(new[]{"같은 플레이의 노말판정 기록(하드판정에서 반영)도 함께 삭제됩니다.","이 플레이를 삭제하면 이 패턴의 노말판정 기록은 미등록이 됩니다."}),"Deleting the hard original says the normal record goes with it");
  using(var confirm=new DeletePlayDialog(request)){confirm.Show();Save(confirm,"delete-confirm-linked-preview");Matches(confirm,heroHard,hero.Name,"같은 플레이의 노말판정 기록(하드판정에서 반영)도 함께 삭제됩니다.","이 플레이를 삭제하면 이 패턴의 노말판정 기록은 미등록이 됩니다.");confirm.Close();}
  var windows=CultureInfo.CurrentCulture;
  try{
  CultureInfo.CurrentCulture=new CultureInfo("ko-KR");
  using(var editing=new EditPlayDialog(hero.Name,"8B","SC","하드",request.Candidates[0],true))
  {
   editing.Show();Save(editing,"edit-play-preview");var input=editing.Input;
   Check(editing.Value==1000000&&Inside(editing).OfType<Label>().Any(l=>l.Text=="같은 플레이의 노말판정 기록도 함께 수정됩니다."),"The edit window opens with the play's score and says the normal record follows");
   input.ScoreBox.Text="999511";Check(input.RateBox.Text=="99.95"&&editing.Value==999511,"Typing a score fills the rate");
   input.RateBox.Text="99.54";Check(input.ScoreBox.Text=="995,400"&&editing.Value==995400,"Typing a rate fills the score in hundreds");
   input.ScoreBox.Text="998156";input.RateBox.Text="99.8";input.RateBox.Text="99.81";Check(editing.Value==998156&&input.ScoreBox.Text=="998,156","Typing the same rate again brings the exact score back");
   input.ScoreBox.Text="1000001";Check(editing.Value==null&&!((Button)editing.AcceptButton!).Enabled,"Out of range cannot be saved");
   input.ScoreBox.Text="0";Check(editing.Value==0&&input.RateBox.Text=="0.00","Zero is a score");
   editing.Close();
  }
  // Windows 의 숫자 형식이 독일어(천 단위 마침표, 소수 쉼표)일 때: 칸이 스스로 적은 값을 다시 읽을 수 있어야 한다
  CultureInfo.CurrentCulture=new CultureInfo("de-DE");
  using(var german=new EditPlayDialog(hero.Name,"8B","SC","하드",new Play(77,heroHard.Key,995421,t,PlaySource.Play),false))
  {
   german.Show();var input=german.Input;
   Check(input.ScoreBox.Text=="995.421"&&input.RateBox.Text=="99,54"&&german.Value==995421,"With a dot as the thousands separator the window opens with a value it can read back");
   input.RateBox.Text="99,55";Check(input.ScoreBox.Text=="995.500"&&german.Value==995500&&((Button)german.AcceptButton!).Enabled,"and a score filled in from the rate can be saved");
   input.RateBox.Text="99,54";Check(german.Value==995421&&input.ScoreBox.Text=="995.421","and the exact score still comes back with the same rate");
   input.ScoreBox.Text="998156";Check(input.RateBox.Text=="99,81"&&german.Value==998156,"and a typed score fills the rate in that format");
   german.Close();
  }
  }finally{CultureInfo.CurrentCulture=windows;}

  // ── 일시 미상 기록만 있는 판정: 표에는 'PB · 일시 미상' 한 줄, 그래프 아래의 줄에서 출처를 고른다
  Open(imported);history=dialog.History!;var only=history.Table.Rows.Single();
  Check(only.Group!=null&&only.Pb&&history.Model.Pb==null&&history.Model.Dated.Count==0&&history.Model.Undated.Single().Plays.Single().Source==PlaySource.VArchive&&history.Table.Items[0].SubItems[1].Text=="일시 미상","A record with no play time is the PB line at the foot of the table");
  Check(history.Graph.Total==0&&history.Graph.Points.Count==0&&!history.RangeBox.Visible&&history.Graph.Axis.Pb==995400,"With only undated records the graph has no point and no zoom");
  history.Graph.Hover(history.Graph.LineSpot());Check(history.Graph.TipText=="PB · 995,400점 · 일시 미상 · V아카이브 (RATE 환산)","The PB line names the undated source");
  history.Pick(history.Model.Undated[0].Plays[0].Seq);request=history.Request()!;
  Check(history.Table.Picked==only&&!Part<Button>(history,"edit").Enabled&&request.Candidates.Single().Source==PlaySource.VArchive&&DeletePlayDialog.Notes(request,request.Candidates[0]).Single()=="이 플레이를 삭제하면 이 패턴의 노말판정 기록은 미등록이 됩니다.","Picking its source selects that line; it can be deleted but not edited");
  Save(history,"history-undated-preview");
  // 같은 SCORE 에 출처가 여럿이면 확인 창에서 지울 출처를 고른다(자료는 지어낸 것이다. 실제로는 옛 버전에서 올라온 RATE 기록과 V아카이브 기록이 같은 점수일 때다)
  var two=new DeleteRequest(fundamental.Name,"8B","SC","노말",new[]{new Play(901,imported.Key,997200,null,PlaySource.VArchive),new Play(902,imported.Key,997200,null,PlaySource.OldRate)},false,_=>Array.Empty<string>());
  using(var confirm=new DeletePlayDialog(two))
  {
   confirm.Show();var sources=Inside(confirm).OfType<ComboBox>().Single();
   Check(sources.Items.Cast<string>().SequenceEqual(new[]{"V아카이브",PlaySource.FirstVersion+" 이전 기록 (RATE 환산)"})&&confirm.Chosen.Seq==901&&Inside(confirm).OfType<Label>().Any(l=>l.Text=="선택한 출처의 기록 1건만 삭제됩니다."),"With several sources at one score the confirmation lists them");
   sources.SelectedIndex=1;Check(confirm.Chosen.Seq==902,"The chosen source is the one deleted");Save(confirm,"delete-confirm-source-preview");confirm.Close();
  }
  using(var plain=new DeletePlayDialog(new(fundamental.Name,"8B","SC","하드",new[]{low},false,_=>Array.Empty<string>()))){plain.Show();Save(plain,"delete-confirm-preview");Check(Inside(plain).OfType<SongLine>().Single().AccessibleName==fundamental.Name&&!Inside(plain).OfType<Label>().Any(l=>l.Text.Contains("미등록")||l.Text.Contains("노말판정")),"A plain play is confirmed with no extra notes");plain.Close();}
  // 기록 수정·삭제가 멈췄을 때의 안내창 네 가지: 기록이 바뀜, 이미 삭제됨, 곡 목록에 없는 패턴, 다른 프로필
  notices.Clear();PlayNotice.Changed(dialog,false);PlayNotice.Gone(dialog,false);PlayNotice.NoPattern(dialog);PlayNotice.OtherProfile(dialog,true);
  Check(notices.Select(n=>n.Title+"|"+n.Headline+"|"+n.Text).SequenceEqual(new[]{
   "기록 삭제|기록이 바뀌어 삭제를 멈췄습니다.|기록 히스토리에서 변경된 내용을 확인한 뒤 삭제할 기록을 다시 선택해 주세요.",
   "기록 삭제|선택한 기록이 이미 삭제되었습니다.|‘확인’을 누르면 최신 기록 목록으로 돌아갑니다.",
   "기록 수정|현재 곡 목록에서 이 패턴을 찾을 수 없습니다.|메인 창에서 ‘업데이트 확인’을 눌러 주세요. 최신 버전에서도 같은 안내가 뜨면 곡명과 패턴을 함께 제보해 주세요.",
   "기록 수정|‘등록한 기록 보기’ 창을 열었을 때와 현재 프로필이 다릅니다.|‘등록한 기록 보기’ 창을 닫고 메인 창에서 기록을 수정할 프로필을 선택한 뒤 ‘등록한 기록 보기’를 다시 열어 주세요."}),"The four notices");
  using(var notice=new PlayNotice("기록 삭제","기록이 바뀌어 삭제를 멈췄습니다.","기록 히스토리에서 변경된 내용을 확인한 뒤 삭제할 기록을 다시 선택해 주세요.")){notice.Show();Save(notice,"play-notice-preview");notice.Close();}
  // 최고점보다 낮은 일시 미상 기록: 표에는 없고 아래의 줄에만 있다. 환산이라고 적히고, 키보드로 고를 수 있다
  store.Add(imported,999000,t.AddHours(3));Open(imported);history=dialog.History!;var strip=history.Undated;
  Check(history.Table.Rows.Count==1&&history.Table.Rows[0].Group==null&&strip.TabStop&&strip.Sources.Single().Source==PlaySource.VArchive&&ScoreText.Source(strip.Sources[0])=="V아카이브 (RATE 환산)","A lower undated record is only under the table, marked as converted beside its source");
  Check(strip.Key(Keys.Right)&&strip.Key(Keys.Space)&&history.PickedSeq==strip.Sources[0].Seq&&history.Table.Picked==null&&!Part<Button>(history,"edit").Enabled&&Part<Button>(history,"delete").Enabled&&history.Request()!.Candidates.Single().Source==PlaySource.VArchive,"It can be picked with the keyboard and then deleted with the button");
  ClickBelow(history.Table);Check(history.PickedSeq==strip.Sources[0].Seq&&Part<Button>(history,"delete").Enabled,"Clicking the empty part of the table does not drop a source picked below");
  Save(history,"history-lower-undated-preview");
  {
   using var lone=new UndatedStrip();
   UndatedGroup Two(int seq)=>new(997200,new[]{new Play(seq,imported.Key,997200,null,PlaySource.VArchive),new Play(seq+1,imported.Key,997200,null,PlaySource.OldRate)});
   lone.Set(new[]{Two(901)},null);lone.Arrive();Check(lone.Keyed!.Seq==901&&lone.Key(Keys.Right)&&lone.Keyed.Seq==902,"In a row of two sources the arrow moves the keyboard focus to the second");
   lone.Set(new[]{Two(911)},null);lone.Arrive();Check(lone.Keyed!.Seq==911,"Entering the sources of another chart starts at its first source, not where the last chart was left");
   lone.Set(new[]{Two(921)},922);lone.Arrive();Check(lone.Keyed!.Seq==922,"With a source already picked the focus starts there");
  }
  // 긴 곡명: 머리가 줄여 그리고, 도움말은 지금 보이는 곡의 것이다(앞에 본 곡의 것이 남지 않는다)
  var longs=catalog.Songs.Where(s=>s.Name.Length>=40).Take(2).ToArray();
  if(longs.Length==2)
  {
   foreach(var song in longs){var c=song.Charts[0];store.Add(new(song.Id,c.Button,c.Pattern,"노말",1),950000,t.AddHours(4));}
   typeof(RecordsDialog).GetMethod("RefreshRows",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(dialog,null);var tips=new List<string?>();
   foreach(var song in longs){var c=song.Charts[0];Open(new(song.Id,c.Button,c.Pattern,"노말",1));history=dialog.History!;history.Refresh();Application.DoEvents();tips.Add(Part<HistoryHead>(history,"head").TipText);}
   Check(tips[0]!=null&&tips[0]!.StartsWith(longs[0].Name)&&tips[1]!=null&&tips[1]!.StartsWith(longs[1].Name),$"The tip of a shortened title is that of the song shown now ({tips[0]} / {tips[1]})");
   Open(fundHard);history=dialog.History!;history.Refresh();Application.DoEvents();Check(Part<HistoryHead>(history,"head").TipText==null,"A title that fits has no tip");
  }
  else passed.Add("SKIP: no two long titles in the catalog");
  // 좁혀서 가로로 스크롤한 목록: 마우스 아래의 칸은 그 줄의 실제 왼쪽 끝에서부터 센다
  dialog.Size=dialog.MinimumSize;Application.DoEvents();SendMessage(grid.Handle,0x1014,(IntPtr)120,IntPtr.Zero);Application.DoEvents();
  {
   var top=grid.GetItemRect(0);int song=grid.Columns["Song"]!.Width,y=top.Top+top.Height/2;
   Check(top.Left<0&&grid.CellAt(new Point(top.Left+song+10,y))==(0,1)&&grid.CellAt(new Point(top.Left+song-10,y))==(0,0)&&grid.CellAt(new Point(top.Left+song+grid.Columns["Artist"]!.Width+5,y))==(0,2),$"After scrolling sideways the cell under the mouse is found from the row's real left edge ({top.Left})");
  }
  // 목록을 닫으면 히스토리도 닫힌다
  Open(fundHard);var last=dialog.History!;dialog.Close();Application.DoEvents();Check(last.IsDisposed||!last.Visible,"Closing the list closes the history");
  }finally{PlayNotice.Hook=null;}
  File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"records-validation.txt"),string.Join("\n",passed)+"\n");
 }
 static IEnumerable<Control> Inside(Control parent)=>parent.Controls.Cast<Control>().SelectMany(c=>new[]{c}.Concat(Inside(c)));
 static T Part<T>(HistoryDialog history,string name)=>(T)typeof(HistoryDialog).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(history)!;
}
