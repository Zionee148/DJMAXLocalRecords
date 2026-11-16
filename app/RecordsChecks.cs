using System.ComponentModel;
using System.Reflection;
namespace LadderRecords;
static class RecordsChecks
{
 public static void Run(Catalog catalog){
  void Check(bool value,string name){if(!value)throw new Exception(name);}
  var path=Path.Combine(AppContext.BaseDirectory,"test-data",Guid.NewGuid()+".records.json");Directory.CreateDirectory(Path.GetDirectoryName(path)!);var store=new RecordStore(path);var fundamental=catalog.Songs.Single(s=>s.Name=="Fundamental");var hero=catalog.Songs.Single(s=>s.Name=="Hero");store.Put(new(fundamental.Id,8,"SC","하드",1),99.48m);store.Put(new(hero.Id,8,"SC","하드",1),100m);
  foreach(var song in catalog.Songs.GroupBy(s=>s.Name).First(g=>g.Count()>1)){var c=song.Charts[0];store.Put(new(song.Id,c.Button,c.Pattern,"노말",1),98.12m);}
  for(int i=0;i<9;i++)store.Put(new(fundamental.Id,8,"SC","하드",1),98m);store.Put(new(hero.Id,8,"SC","하드",1),99m,measuredAt:new DateTime(2031,1,1,0,0,0,DateTimeKind.Utc));
  store.SetManual(new(fundamental.Id,8,"SC","노말",1),0m,1);
  var rows=RecordRows.Read(catalog,store.Active);Check(rows.Count==6&&rows.Any(r=>r.Accuracy==0),"All registered records including zero and promoted Normal");Check(RecordRows.Filter(rows,"FUNDAMENTAL viphex",null,"전체","전체").Count()==2,"Case-insensitive title and artist token search");Check(RecordRows.Filter(rows,"",8,"SC","하드").Count()==2,"Combined filters");Check(RecordRows.Filter(rows,"not a song",null,"전체","전체").Count()==0,"No search match");
  string first=store.Active.Id;store.Create("분리",false);Check(RecordRows.Read(catalog,store.Active).Count==0,"Profile isolation");store.Select(first);
  using var dialog=new RecordsDialog(catalog,store);dialog.Show();Application.DoEvents();
  T Field<T>(string name)=>(T)typeof(RecordsDialog).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(dialog)!;
  var grid=Field<RecordsView>("grid");Check(grid.RowCount==rows.Count,"Grid displays all rows");Check(grid.SortedColumn?.Name=="LastPlayedAt"&&grid.SortOrder==SortOrder.Descending&&grid.ItemAt(0)?.SongName=="Hero","Default recent play descending sort includes lower score registration");grid.Sort("Accuracy",ListSortDirection.Descending);Check(grid.ItemAt(0)!.Accuracy == 100m,"Numerical score sort 100 before 99");
  grid.Sort("PlayCount",ListSortDirection.Descending);Check(grid.ItemAt(0)!.PlayCount == 10,"Numerical play count sort 10 before 2");
  // 칸은 번호가 아니라 이름으로 찾는다("최고 스코어" 칸이 생기면서 번호가 밀렸다). 줄마다 그 기록의 점수(%), 스코어, 횟수가 보여야 한다.
  string Cell(int row,string column)=>grid.Items[row].SubItems[grid.Columns[column]!.Index].Text;
  Check(Enumerable.Range(0,grid.RowCount).All(i=>grid.ItemAt(i) is RegisteredRecord row&&Cell(i,"Accuracy")==$"{row.Accuracy:F2}%"&&Cell(i,"RawScore")==$"{row.RawScore:N0}"&&Cell(i,"PlayCount")==$"{row.PlayCount}회"),"Sorted virtual rows preserve record identity and display");
  var search=Field<TextBox>("search");search.Text="Fundamental";Check(grid.RowCount==2,"Live grid search");Field<ComboBox>("judge").SelectedItem="하드";Check(grid.RowCount==1,"Live judge filter");Field<ComboBox>("pattern").SelectedItem="NORMAL";Check(grid.RowCount==0,"Empty grid result");Field<ComboBox>("pattern").SelectedItem="전체";Field<ComboBox>("judge").SelectedItem="전체";search.Clear();Check(grid.RowCount==rows.Count,"Reset view");
  using(var shot=new Bitmap(dialog.Width,dialog.Height)){dialog.DrawToBitmap(shot,new Rectangle(Point.Empty,shot.Size));shot.Save(Path.Combine(AppContext.BaseDirectory,"records-list-preview.png"));}
  // 삭제 확인 창은 곡 이름을 글로, 버튼·난이도·판정을 꼬리표(Chip) 셋으로 보여 준다. 고른 기록의 것과 같아야 한다.
  IEnumerable<Control> Inside(Control parent)=>parent.Controls.Cast<Control>().SelectMany(c=>new[]{c}.Concat(Inside(c)));
  var selected=grid.SelectedRecord!;using(var deleteDialog=new DeleteRecordDialog(selected)){deleteDialog.Show();Application.DoEvents();var parts=Inside(deleteDialog).ToArray();Check(parts.OfType<Label>().Any(l=>l.Text.Contains(selected.SongName))&&parts.OfType<Chip>().Select(c=>c.Text).SequenceEqual(new[]{$"{selected.Chart.Button}B",selected.Chart.Pattern,selected.Chart.Judge+"판정"}),"Delete confirmation identifies exact selected chart");Check(deleteDialog.ActiveControl==deleteDialog.CancelButton&&deleteDialog.AcceptButton==null,"Delete confirmation defaults to cancel");using var shot=new Bitmap(deleteDialog.Width,deleteDialog.Height);deleteDialog.DrawToBitmap(shot,new Rectangle(Point.Empty,shot.Size));shot.Save(Path.Combine(AppContext.BaseDirectory,"delete-confirm-preview.png"));deleteDialog.Close();}
  var before=store.Get(selected.Chart);Check(!dialog.DeleteSelected(e=>false)&&store.Get(selected.Chart)==before&&grid.RowCount==rows.Count,"Cancel deletion changes nothing");Check(dialog.DeleteSelected(e=>e.Chart.Key==selected.Chart.Key)&&store.Get(selected.Chart)==null&&grid.RowCount==rows.Count-1,"Delete sorted selected row and refresh list");store.Undo();Check(store.Get(selected.Chart)==before,"Undo deletion restores complete score");
  dialog.Close();File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"records-validation.txt"),"PASS: all records including 0%; profile isolation; title+artist search; combined button/pattern/judge filters; numerical score and play count sorting; sorted record identity; live search/filter/reset; records list rendering.");
 }
}


