using System.ComponentModel;
using System.Drawing.Drawing2D;
namespace LadderRecords;

// 등록한 기록 보기: 현재 프로필의 곡·버튼·난이도·판정마다 한 줄.
// 위에서부터 검색·필터 → 표(곡명 | 아티스트 | 버튼 | 난이도 | 판정 | 최고 기록 | 최근 플레이) → 표시 건수와 '기록 히스토리'·'닫기'.
// 한 판씩 보고 고치고 지우는 일은 히스토리에서 한다(HistoryDialog). 이 창은 크기를 바꾸고 최대화할 수 있는 하나뿐인 창이다. 창의 크기, 칸의 너비, 정렬과 필터를 기억한다(records-view.json).

/// <summary>목록의 한 줄. 최고 기록은 그 판정에 남은 모든 출처 가운데 최고 SCORE 이고, 최근 플레이는 확인되는 마지막 플레이 시각이다(모르면 없다)</summary>
record RegisteredRecord(Selection Chart,string SongName,string Artist,Play Best,DateTime? LastPlayedAt)
{
 public int Button=>Chart.Button;public string Pattern=>Chart.Pattern;public string Judge=>Chart.Judge;
 public int RawScore=>Best.Score;public decimal Accuracy=>ScoreValue.Rate(Best.Score);
}
static class RecordRows
{
 public static List<RegisteredRecord> Read(Catalog catalog,RecordStore store,PlayProfile profile)
 {
  var songs=catalog.Songs.ToDictionary(s=>s.Id);var rows=new List<RegisteredRecord>();
  foreach(string key in store.Keys(profile))
  {
   var parts=key.Split('|');if(parts.Length!=4||!int.TryParse(parts[1],out int button)||store.Best(key,profile) is not {} best)continue;
   songs.TryGetValue(parts[0],out var song);
   rows.Add(new(new(parts[0],button,parts[2],parts[3],1),song?.Name??ScoreText.MissingSong(parts[0]),song?.Artist??"",best,store.Recent(key,profile)?.ToLocalTime()));
  }
  return rows;
 }
 /// <summary>판정 필터의 항목. 예전 판이 기억해 둔 이름('노말' 등)도 받는다</summary>
 public static readonly string[] Judges={"전체","노말판정","하드판정","맥스판정","하드·맥스판정"};
 public static string JudgeFilter(string? kept)=>kept==null?"전체":Judges.Contains(kept)?kept:Judges.Contains(kept+"판정")?kept+"판정":"전체";
 public static IEnumerable<RegisteredRecord> Filter(IEnumerable<RegisteredRecord> rows,string query,int? button,string pattern,string judge)
 {
  var words=query.Split(' ',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
  string wanted=judge.EndsWith("판정")?judge[..^2]:judge;
  return rows.Where(r=>(button==null||r.Chart.Button==button)&&(pattern=="전체"||r.Chart.Pattern==pattern)
   &&(wanted=="전체"||(wanted=="하드·맥스"?r.Chart.Judge is "하드" or "맥스":r.Chart.Judge==wanted))
   &&words.All(word=>(r.SongName+" "+r.Artist).Contains(word,StringComparison.OrdinalIgnoreCase)));
 }
}
static class RecordOrdering
{
 /// <summary>예전 판이 기억해 둔 정렬 칸을 지금의 칸으로 옮긴다: 최고 점수(%)·최고 스코어는 '최고 기록'으로, 없어진 칸(플레이 카운트)과 모르는 이름은 기본(최근 플레이)으로</summary>
 public static string Column(string? kept)=>kept switch{"Song" or "Artist" or "Button" or "Pattern" or "Judge" or "Best" or "LastPlayedAt"=>kept,"Accuracy" or "RawScore"=>"Best",_=>"LastPlayedAt"};
 public static void Apply(List<RegisteredRecord> list,string column,ListSortDirection order)
 {
  list.Sort((a,b)=>
  {
   // 최근 플레이를 모르는 줄은 어느 방향으로 정렬하든 아래에 둔다
   if(column=="LastPlayedAt"&&(a.LastPlayedAt==null)!=(b.LastPlayedAt==null))return a.LastPlayedAt==null?1:-1;
   int value=column switch
   {
    "Song"=>StringComparer.CurrentCultureIgnoreCase.Compare(a.SongName,b.SongName),"Artist"=>StringComparer.CurrentCultureIgnoreCase.Compare(a.Artist,b.Artist),
    "Button"=>a.Button.CompareTo(b.Button),"Pattern"=>string.CompareOrdinal(a.Pattern,b.Pattern),"Judge"=>string.CompareOrdinal(a.Judge,b.Judge),
    "Best"=>a.RawScore.CompareTo(b.RawScore),"LastPlayedAt"=>Nullable.Compare(a.LastPlayedAt,b.LastPlayedAt),_=>0,
   };
   if(value!=0)return order==ListSortDirection.Ascending?value:-value;
   int name=StringComparer.CurrentCultureIgnoreCase.Compare(a.SongName,b.SongName);if(name!=0)return name;
   return string.CompareOrdinal(a.Chart.Key,b.Chart.Key);
  });
 }
}

/// <summary>목록의 표. 줄을 직접 그리는 가상 목록이다(줄이 수천이어도 보이는 줄만 만든다)</summary>
class RecordsView:OwnerTable
{
 // 창이 뜰 때의 키우기: 칸의 너비도 같이 키운다(Windows 의 목록이 스스로 키웠으면 그대로 둔다)
 protected override void ScaleControl(SizeF factor,BoundsSpecified specified)
 {
  var before=Columns.Cast<ColumnHeader>().Select(c=>c.Width).ToArray();
  base.ScaleControl(factor,specified);
  if(factor.Width!=1f)for(int i=0;i<before.Length;i++)if(Columns[i].Width==before[i])Columns[i].Width=(int)Math.Round(before[i]*factor.Width);
  FitSong();
 }
 IReadOnlyList<RegisteredRecord> records=Array.Empty<RegisteredRecord>();readonly Dictionary<int,ListViewItem> cache=new();readonly ImageList spacing=new();readonly ToolTip tip=new();(int Row,int Column) tipped=(-1,-1);bool fitting;
 public ColumnHeader? SortedColumn{get;private set;}
 public SortOrder SortOrder{get;private set;}
 public int RowCount=>VirtualListSize;
 public event Action<ColumnHeader,ListSortDirection>? OrderChanged;
 /// <summary>칸의 이름, 머리글, 기본 너비(100% 때의 값). 곡명은 남는 폭을 쓴다</summary>
 public static readonly (string Name,string Text,int Width)[] Plan={("Song","곡명",302),("Artist","아티스트",165),("Button","버튼",64),("Pattern","난이도",96),("Judge","판정",96),("Best","최고 기록",210),("LastPlayedAt","최근 플레이",195)};
 static bool Centered(string name)=>name is "Button" or "Pattern" or "Judge";
 static bool Ended(string name)=>name is "Best" or "LastPlayedAt";
 /// <summary>머리글의 높이를 정하는 글꼴 크기(포인트)</summary>
 const float HeaderFont=15.5f;
 public static string Cell(RegisteredRecord row,string column)=>column switch
 {
  "Song"=>row.SongName,"Artist"=>row.Artist,"Button"=>$"{row.Button}B","Pattern"=>row.Pattern,"Judge"=>row.Judge+"판정","Best"=>ScoreText.Full(row.Best),
  "LastPlayedAt"=>row.LastPlayedAt?.ToString("yyyy-MM-dd HH:mm:ss")??"—",_=>"",
 };
 public RecordsView()
 {
  spacing.ImageSize=new(1,Theme.S(35));spacing.Images.Add(new Bitmap(1,1));SmallImageList=spacing;
  Font=Theme.Px(AppFonts.Ui,HeaderFont);   // 머리글의 높이는 이 글꼴이 정한다(글자는 직접 그린다)
  foreach(var (name,text,width) in Plan)Columns.Add(new ColumnHeader(){Name=name,Text=text,Width=width});
  // 줄의 글은 화면을 읽어 주는 도구와 검사가 읽는다(그리는 것은 아래의 DrawSubItem 이다)
  RetrieveVirtualItem+=(_,e)=>{if(!cache.TryGetValue(e.ItemIndex,out var item)){var row=ItemAt(e.ItemIndex);item=row==null?new ListViewItem(""):new ListViewItem(Plan.Select(c=>Cell(row,c.Name)).ToArray()){ImageIndex=0};cache[e.ItemIndex]=item;}e.Item=item;};
  HeaderPainter=(g,area,index)=>
  {
   var head=Columns[index];string name=head.Name??"";bool sorted=head==SortedColumn;int pad=Theme.S(12);
   var words=new[]{new InkPart(head.Text,Ink.Body(12f,true),sorted?Theme.Accent:Theme.Dim)};
   float wide=Ink.Width(words)+(sorted?Theme.S(13):0),middle=area.Top+area.Height/2f;
   float x=Centered(name)?area.Left+(area.Width-wide)/2f:Ended(name)?area.Right-pad-wide:area.Left+pad;
   var state=g.Save();g.SetClip(area);
   x=Ink.Draw(g,words,x,Ink.Baseline(middle,Ink.Body(12f,true)));
   if(sorted)Ink.Arrow(g,x+Theme.S(5),middle,SortOrder!=SortOrder.Ascending,Theme.Accent);
   g.Restore(state);
  };
  DrawSubItem+=(_,e)=>
  {
   if(e.Item==null||ItemAt(e.ItemIndex) is not {} row)return;
   var g=e.Graphics;var cell=e.Bounds;string name=Columns[e.ColumnIndex].Name??"";bool picked=e.Item.Selected;float middle=cell.Top+cell.Height/2f;int pad=Theme.S(12);
   using(var back=new SolidBrush(picked?Theme.RowPick:Theme.Table))g.FillRectangle(back,cell);
   using(var line=new Pen(Theme.RowLine))g.DrawLine(line,cell.Left,cell.Bottom-1,cell.Right,cell.Bottom-1);
   var state=g.Save();g.SetClip(cell);
   switch(name)
   {
    case "Song":
     if(picked)using(var bar=new SolidBrush(Theme.Stripe))g.FillRectangle(bar,cell.Left,cell.Top,Theme.S(3),cell.Height-1);
     Ink.DrawClipped(g,row.SongName,Ink.Title(13f),Theme.Text,cell.Left+pad,Ink.Baseline(middle,Ink.Title(13f)),cell.Width-pad*2);break;
    case "Artist":Ink.DrawClipped(g,row.Artist,Ink.Artist(13f),Theme.Dim,cell.Left+pad,Ink.Baseline(middle,Ink.Artist(13f)),cell.Width-pad*2);break;
    case "Best":Ink.DrawRight(g,Ink.Score(row.Best,15f,12f,Theme.Score,true),cell.Right-pad,Ink.Baseline(middle,Ink.Number(15f)));break;
    case "LastPlayedAt":Ink.DrawRight(g,new[]{new InkPart(Cell(row,name),Ink.Number(12f,false),Theme.Dim)},cell.Right-pad,Ink.Baseline(middle,Ink.Number(12f,false)));break;
    default:string text=Cell(row,name);Chip.Draw(g,text,cell.Left+(cell.Width-Chip.Measure(text))/2f,middle);break;
   }
   g.Restore(state);
  };
  ColumnClick+=(_,e)=>{var column=Columns[e.Column];Sort(column.Name??"Song",SortedColumn==column&&SortOrder==SortOrder.Ascending?ListSortDirection.Descending:ListSortDirection.Ascending);};
  // 줄여 그린 곡명·아티스트는 마우스를 올리면 전체를 보인다
  MouseMove+=(_,e)=>
  {
   var (row,column)=CellAt(e.Location);
   if((row,column)==tipped)return;tipped=(row,column);string? full=null;
   if(row>=0&&column is 0 or 1&&ItemAt(row) is {} record)
   {
    string text=column==0?record.SongName:record.Artist;var chain=column==0?Ink.Title(13f):Ink.Artist(13f);
    if(MixedLine.Clip(text,chain,Columns[column].Width-Theme.S(24)).Cut)full=text;
   }
   tip.SetToolTip(this,full);
  };
  MouseLeave+=(_,_)=>{tipped=(-1,-1);tip.SetToolTip(this,null);};
 }
 /// <summary>그 자리의 줄과 칸(없으면 -1). 가로로 스크롤했으면 칸의 자리도 그만큼 밀려 있다: 그 줄의 왼쪽 끝에서부터 센다</summary>
 internal (int Row,int Column) CellAt(Point point)
 {
  int row=HitTest(point).Item?.Index??-1;if(row<0)return(-1,-1);
  int left=GetItemRect(row).Left;for(int i=0;i<Columns.Count;i++){left+=Columns[i].Width;if(point.X<left)return(row,i);}
  return(row,-1);
 }
 // 곡명 칸은 남는 폭을 쓴다(다른 칸의 너비를 바꾸거나 창의 크기를 바꾸면 따라간다)
 public void FitSong()
 {
  if(fitting||Columns.Count==0)return;fitting=true;
  try{int others=0;foreach(ColumnHeader column in Columns)if(column.Name!="Song")others+=column.Width;int wide=Math.Max(Theme.S(160),ClientSize.Width-others);var song=Columns["Song"]!;if(song.Width!=wide)song.Width=wide;}
  finally{fitting=false;}
 }
 protected override void OnClientSizeChanged(EventArgs e){base.OnClientSizeChanged(e);FitSong();}
 protected override void OnColumnWidthChanged(ColumnWidthChangedEventArgs e){base.OnColumnWidthChanged(e);if(!fitting){FitSong();Invalidate();}}
 public RegisteredRecord? ItemAt(int index)=>index>=0&&index<records.Count?records[index]:null;
 public RegisteredRecord? SelectedRecord=>SelectedIndices.Count==1?ItemAt(SelectedIndices[0]):null;
 public void SelectIndex(int index){if(index<0||index>=RowCount)return;SelectedIndices.Clear();SelectedIndices.Add(index);EnsureVisible(index);}
 // 창을 없애기 직전에 목록을 비운다. 접근성 클라이언트(화면 읽기, 화면 조작 도구 등)가 줄을 읽어 간 적이 있으면 WinForms 가 목록을 없앨 때
 // 줄마다 접근성 정보를 끊는데, 3,200줄에 83 ~ 115초가 걸려 프로그램이 그만큼 늦게 끝났다. 줄이 없으면 끊을 것도 없다(0.14초).
 internal void ReleaseRows(){if(IsDisposed||!IsHandleCreated||VirtualListSize==0)return;Display(Array.Empty<RegisteredRecord>());}
 public void Display(IReadOnlyList<RegisteredRecord> rows){BeginUpdate();try{SelectedIndices.Clear();records=rows;cache.Clear();VirtualListSize=rows.Count;}finally{EndUpdate();}tipped=(-1,-1);FitSong();Invalidate();}
 public void Sort(string name,ListSortDirection order){SortedColumn=Columns[name];SortOrder=order==ListSortDirection.Ascending?SortOrder.Ascending:SortOrder.Descending;OrderChanged?.Invoke(SortedColumn!,order);Invalidate();}
 protected override void Dispose(bool disposing){if(disposing){spacing.Dispose();tip.Dispose();}base.Dispose(disposing);}
}

class RecordsDialog:AppWindow
{
 readonly Catalog catalog;readonly RecordStore store;readonly string profileId;readonly string? settingsPath;bool settingsReady;List<RegisteredRecord> displayedRows=new(),allRows=new();long loadedRevision=-1;readonly System.Windows.Forms.Timer refreshTimer=new(){Interval=250};
 HistoryDialog? history;readonly HistoryPrefs graphPrefs=new();
 public string ProfileId=>profileId;
 [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]internal bool KeepAlive{get;set;}
 protected override void OnFormClosing(FormClosingEventArgs e){base.OnFormClosing(e);if(KeepAlive&&e.CloseReason==CloseReason.UserClosing&&!e.Cancel){e.Cancel=true;DialogResult=DialogResult.None;Hide();}}
 public void PrepareForShow(){DialogResult=DialogResult.None;if(loadedRevision!=store.Revision)RefreshRows();}
 // 창이 뜨기 전에 고른 줄은 목록이 만들어질 때 사라진다(가상 목록). 뜬 뒤에 첫 줄을 고른다
 protected override void OnShown(EventArgs e){base.OnShown(e);if(grid.SelectedRecord==null&&grid.RowCount>0)grid.SelectIndex(0);historyButton.Enabled=grid.SelectedRecord!=null;}
 protected override void Dispose(bool disposing){if(disposing){SaveViewSettings();settingsReady=false;refreshTimer.Dispose();history?.Dispose();grid.ReleaseRows();}base.Dispose(disposing);}
 readonly InputBox search=new(330,"",0,30);readonly ComboBox button=new ThemedCombo(){Tallness=30,Roomy=true},pattern=new ThemedCombo(){Tallness=30,Roomy=true},judge=new ThemedCombo(){Tallness=30,Roomy=true};
 readonly RecordsView grid=new(){Dock=DockStyle.Fill};readonly Label summary=new(){AutoSize=true,Anchor=AnchorStyles.Left,Margin=Padding.Empty,Tag="dim"};
 readonly Button historyButton=new(){Text="기록 히스토리",Tag="primary"},close=new(){Text="닫기",DialogResult=DialogResult.Cancel};
 /// <summary>열려 있는 히스토리 창(없으면 null)</summary>
 internal HistoryDialog? History=>history is {IsDisposed:false,Visible:true}?history:null;
 public RecordsDialog(Catalog catalog,RecordStore store,string? settingsPath=null)
 {
  this.catalog=catalog;this.store=store;this.settingsPath=settingsPath;profileId=store.Active.Id;Text="등록한 기록 보기 · "+store.Active.Name;Font=Theme.Px(AppFonts.Ui,9.75f);ClientSize=new(1158,531);MinimumSize=new(920,400);StartPosition=FormStartPosition.CenterParent;
  var root=new TableLayoutPanel(){Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new(16,14,16,13)};root.RowStyles.Add(new(SizeType.AutoSize));root.RowStyles.Add(new(SizeType.Percent,100));root.RowStyles.Add(new(SizeType.AutoSize));Controls.Add(root);
  var filters=new FlowLayoutPanel(){AutoSize=true,Dock=DockStyle.Fill,WrapContents=true,Margin=Padding.Empty};
  void Add(string name,Control control,int after=12)
  {
   var field=new FlowLayoutPanel(){AutoSize=true,FlowDirection=FlowDirection.TopDown,WrapContents=false,Margin=new(0,0,after,0)};
   var label=Boxes.Text(name,"dim",9f);label.Margin=new(1,0,0,5);control.Margin=Padding.Empty;field.Controls.Add(label);field.Controls.Add(control);filters.Controls.Add(field);
  }
  search.Box.Hint="곡명 또는 아티스트";search.Box.AccessibleName="검색";
  foreach(var c in new[]{button,pattern,judge}){c.DropDownStyle=ComboBoxStyle.DropDownList;c.Width=c==pattern?108:c==judge?116:76;}
  button.Items.AddRange(new object[]{"전체","4B","5B","6B","8B"});pattern.Items.AddRange(new object[]{"전체","NORMAL","HARD","MAXIMUM","SC"});judge.Items.AddRange(RecordRows.Judges);foreach(var c in new[]{button,pattern,judge})c.SelectedIndex=0;
  button.AccessibleName="버튼";pattern.AccessibleName="난이도";judge.AccessibleName="판정";
  var reset=new Button(){Text="초기화",Size=new(62,30)};reset.Click+=(_,_)=>{search.Box.Clear();foreach(var c in new[]{button,pattern,judge})c.SelectedIndex=0;};
  Add("검색",search);Add("버튼",button);Add("난이도",pattern);Add("판정",judge);Add(" ",reset,0);root.Controls.Add(filters,0,0);
  var frame=new Panel(){Dock=DockStyle.Fill,Padding=new(1),Margin=new(0,13,0,12),Tag="line"};frame.Controls.Add(grid);root.Controls.Add(frame,0,1);
  grid.DoubleClick+=(_,_)=>OpenHistory();grid.SelectedIndexChanged+=(_,_)=>historyButton.Enabled=grid.SelectedRecord!=null;
  grid.OrderChanged+=(column,direction)=>{var selected=grid.SelectedRecord?.Chart.Key;RecordOrdering.Apply(displayedRows,column.Name??"Song",direction);grid.Display(displayedRows);int index=selected==null?-1:displayedRows.FindIndex(r=>r.Chart.Key==selected);if(index>=0)grid.SelectIndex(index);else if(grid.RowCount>0)grid.SelectIndex(0);historyButton.Enabled=grid.SelectedRecord!=null;};
  var footer=new TableLayoutPanel(){AutoSize=true,Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};footer.ColumnStyles.Add(new(SizeType.Percent,100));footer.ColumnStyles.Add(new(SizeType.AutoSize));
  var buttons=Parts.Footer(190,historyButton,close);buttons.Margin=Padding.Empty;buttons.Anchor=AnchorStyles.Right;
  footer.Controls.Add(summary,0,0);footer.Controls.Add(buttons,1,0);root.Controls.Add(footer,0,2);CancelButton=close;
  historyButton.Click+=(_,_)=>OpenHistory();close.Click+=(_,_)=>Close();
  LoadViewSettings();settingsReady=true;FormClosing+=(_,_)=>SaveViewSettings();
  search.Box.TextChanged+=(_,_)=>RefreshRows();foreach(var c in new[]{button,pattern,judge})c.SelectedIndexChanged+=(_,_)=>RefreshRows();refreshTimer.Tick+=(_,_)=>{if(loadedRevision!=store.Revision)RefreshRows();};
  // 목록을 닫으면(숨기면) 히스토리도 함께 닫는다
  VisibleChanged+=(_,_)=>{refreshTimer.Enabled=Visible;if(!Visible&&history is {IsDisposed:false})history.Close();};RefreshRows();
 }
 void LoadViewSettings()
 {
  var settings=RecordsViewSettings.Load(settingsPath);search.Box.Text=settings.Search??"";graphPrefs.Folded=settings.GraphFolded;graphPrefs.PbLine=settings.PbLine;
  void Select(ComboBox combo,string? value){combo.SelectedItem=combo.Items.Contains(value??"")?value:"전체";}
  Select(button,settings.Button);Select(pattern,settings.Pattern);Select(judge,RecordRows.JudgeFilter(settings.Judge));
  foreach(ColumnHeader column in grid.Columns)if(column.Name!="Song"&&settings.ColumnWidths?.TryGetValue(column.Name!,out int width)==true)column.Width=Math.Clamp(width,40,2000);
  if(settings.Width>0&&settings.Height>0){var area=Screen.FromControl(this).WorkingArea;Size=new(Math.Clamp(settings.Width,MinimumSize.Width,Math.Max(MinimumSize.Width,area.Width)),Math.Clamp(settings.Height,MinimumSize.Height,Math.Max(MinimumSize.Height,area.Height)));}
  // 예전 판이 기억해 둔 정렬 칸이 없어졌으면 기본(최근 플레이 최신순)으로 연다
  string sort=RecordOrdering.Column(settings.SortColumn);bool kept=sort==settings.SortColumn||settings.SortColumn is "Accuracy" or "RawScore";
  grid.Sort(sort,kept&&!settings.Descending?ListSortDirection.Ascending:ListSortDirection.Descending);
 }
 void SaveViewSettings(){if(!settingsReady||settingsPath==null)return;try{var size=Unscaled(WindowState==FormWindowState.Normal?Size:RestoreBounds.Size);   // 크기와 칸 너비는 100% 때의 값으로 적는다(창의 크기 단계가 달라도 같은 값이게)
  new RecordsViewSettings(){Search=search.Box.Text,Button=button.Text,Pattern=pattern.Text,Judge=judge.Text,SortColumn=grid.SortedColumn?.Name??"LastPlayedAt",Descending=grid.SortOrder!=SortOrder.Ascending,Width=size.Width,Height=size.Height,ColumnWidths=grid.Columns.Cast<ColumnHeader>().Where(c=>c.Name!="Song").ToDictionary(c=>c.Name!,c=>Unscaled(c.Width)),GraphFolded=graphPrefs.Folded,PbLine=graphPrefs.PbLine}.Save(settingsPath);}catch(Exception){summary.Text="목록 설정을 저장하지 못했습니다.";}}
 void RefreshRows()
 {
  var active=store.Data.Profiles.Single(p=>p.Id==profileId);
  if(loadedRevision!=store.Revision){allRows=RecordRows.Read(catalog,store,active);loadedRevision=store.Revision;}
  Text="등록한 기록 보기 · "+active.Name;int? selectedButton=button.SelectedIndex==0?null:int.Parse(button.Text.TrimEnd('B'));
  string sort=grid.SortedColumn?.Name??"LastPlayedAt";var direction=grid.SortedColumn==null||grid.SortOrder==SortOrder.Descending?ListSortDirection.Descending:ListSortDirection.Ascending;
  displayedRows=RecordRows.Filter(allRows,search.Box.Text,selectedButton,pattern.Text,judge.Text).ToList();grid.Sort(sort,direction);
  summary.Text=$"표시 {displayedRows.Count:N0} / {allRows.Count:N0}개";
 }
 /// <summary>고른 줄의 히스토리를 연다(더블클릭과 아래의 단추가 같다). 히스토리는 한 번에 하나다: 이미 열려 있으면 같은 창이 고른 판정으로 바뀐다</summary>
 internal void OpenHistory()
 {
  if(grid.SelectedRecord is not RegisteredRecord entry)return;
  if(store.Active.Id!=profileId){PlayNotice.Show(this,"기록 히스토리","‘등록한 기록 보기’ 창을 열었을 때와 현재 프로필이 다릅니다.","‘등록한 기록 보기’ 창을 닫고 메인 창에서 프로필을 선택한 뒤 ‘등록한 기록 보기’를 다시 열어 주세요.");return;}
  if(History is {} open){open.Open(entry);open.Activate();return;}
  var opened=new HistoryDialog(catalog,store,entry,graphPrefs,SaveViewSettings);history=opened;   // 그래프의 접힘과 PB 기준선은 바꿀 때마다 목록의 설정 파일에 적는다
  // 히스토리가 '선택한 기록이 이미 삭제되었습니다.'를 알리고 닫혔으면 최신 목록으로 돌아온다
  opened.FormClosed+=(_,_)=>{if(opened.ReturnedToList&&!IsDisposed&&Visible){RefreshRows();Activate();}};
  opened.Show(this);
 }
}
