using System.ComponentModel;
using System.Drawing.Drawing2D;
namespace LadderRecords;

// 기록 히스토리: 한 판정(프로필·곡·버튼·난이도·판정)에 남은 판을 하나씩 보고 고치고 지운다.
// 위에서부터: 머리(곡·꼬리표·아티스트와 최고 기록) → 표(SCORE | 날짜·시각) → 점수 그래프(접을 수 있다. app\HistoryGraph.cs) → 일시 미상 기록 → 선택 기록 수정·삭제·닫기.
// 창의 크기는 고정이다(820×650. 100% 때의 값). 그래프를 접으면 창과 아래 단추의 자리는 그대로이고 그 자리를 표가 쓴다.
// 접힘과 PB 기준선은 기억한다(records-view.json. 목록 창이 함께 적는다). 전체 / PB 근처 확대와 보던 회차의 구간은 같은 판정을 보는 동안만 지키고, 다른 판정을 열면 전체 그래프의 가장 최근 구간으로 시작한다.
// 기록 목록이 주인인 떠 있는 창이다: 열어 둔 채 주 화면에서 등록·저장·취소를 할 수 있고, 보고 있는 판정에 새 판이 저장되면 스스로 다시 그린다.

/// <summary>히스토리의 머리: 곡 이름과 꼬리표, 아티스트, 오른쪽에 최고 기록</summary>
sealed class HistoryHead:Control
{
 string title="",artist="";string[] chips=Array.Empty<string>();Play? best;ToolTip? tip;
 /// <summary>곡명이나 아티스트를 줄여 그렸을 때 마우스를 올리면 보이는 전체 글(줄이지 않았으면 없다)</summary>
 internal string? TipText{get;private set;}
 public HistoryHead(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);TabStop=false;}
 public void Set(string title,string artist,string[] chips,Play? best){this.title=title;this.artist=artist;this.chips=chips;this.best=best;AccessibleName=title;Invalidate();}
 protected override void OnPaint(PaintEventArgs e)
 {
  var g=e.Graphics;g.Clear(Parent?.BackColor??Theme.Window);int side=Theme.S(18);
  // 오른쪽: '최고 기록'과 그 값. 기록이 없으면 '미등록'
  var caption=new List<InkPart>{new("최고 기록",Ink.Body(12f,true),Theme.Dim)};
  var value=best!=null?Ink.Score(best,24f,13f,Theme.Pb,true):new List<InkPart>{new("미등록",Ink.Body(20f,true),Theme.Dim)};
  float right=Width-side,valueWide=Ink.Width(value);
  Ink.DrawRight(g,caption,right,Ink.Baseline(Theme.S(24),Ink.Body(12f,true)));
  Ink.DrawRight(g,value,right,Ink.Baseline(Theme.S(47),Ink.Number(24f)));
  // 왼쪽: 곡 이름은 꼬리표와 최고 기록의 자리를 뺀 폭에 한 줄로 맞춘다
  int chipsWide=chips.Sum(Chip.Measure)+Theme.S(4)*Math.Max(0,chips.Length-1),gap=Theme.S(9);
  var chain=Ink.Title(25f);float middle=Theme.S(31);
  var line=MixedLine.Clip(title,chain,Math.Max(Theme.S(60),Width-side*2-chipsWide-gap-valueWide-Theme.S(16)));
  var hint=g.TextRenderingHint;g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAlias;
  float x;using(var ink=new SolidBrush(Theme.Text))x=line.Draw(g,ink,side,Ink.Baseline(middle,chain));
  g.TextRenderingHint=hint;
  x+=gap;foreach(string chip in chips)x+=Chip.Draw(g,chip,x,middle)+Theme.S(4);
  bool cut=line.Cut|Ink.DrawClipped(g,artist,Ink.Artist(13f),Theme.Dim,side,Ink.Baseline(Theme.S(58),Ink.Artist(13f)),Width-side*2-valueWide-Theme.S(16));
  // 보이는 곡이 바뀌면 도움말도 그 곡의 것으로 바꾼다(줄이지 않았으면 없앤다)
  string? full=cut?(artist.Length>0?$"{title}\n{artist}":title):null;
  if(full!=TipText){TipText=full;tip??=new ToolTip();tip.SetToolTip(this,full);}
 }
 protected override void Dispose(bool disposing){if(disposing)tip?.Dispose();base.Dispose(disposing);}
}

/// <summary>히스토리의 표: SCORE | 날짜·시각. 줄을 직접 그리는 가상 목록이다(기록 목록과 같은 방식)</summary>
sealed class HistoryTable:OwnerTable
{
 IReadOnlyList<HistoryRow> rows=Array.Empty<HistoryRow>();readonly ImageList spacing=new();
 public bool ByScore{get;private set;}
 public bool Descending{get;private set;}=true;
 public event Action? OrderChanged;
 public IReadOnlyList<HistoryRow> Rows=>rows;
 public HistoryTable()
 {
  spacing.ImageSize=new(1,Theme.S(29));spacing.Images.Add(new Bitmap(1,1));SmallImageList=spacing;
  Font=Theme.Px(AppFonts.Ui,13f);   // 머리글의 높이는 이 글꼴이 정한다(글자는 직접 그린다)
  Columns.Add(new ColumnHeader(){Name="Score",Text="SCORE",Width=330});Columns.Add(new ColumnHeader(){Name="Time",Text="날짜·시각",Width=440});
  RetrieveVirtualItem+=(_,e)=>{var row=e.ItemIndex<rows.Count?rows[e.ItemIndex]:null;e.Item=new ListViewItem(new[]{row==null?"":ScoreText.Full(row.Play),row==null?"":row.Group!=null?"일시 미상":ScoreText.Time(row.Play)}){ImageIndex=0};};
  HeaderPainter=(g,area,index)=>
  {
   bool sorted=index==(ByScore?0:1);float middle=area.Top+area.Height/2f;
   float x=Ink.Draw(g,new[]{new InkPart(Columns[index].Text,Ink.Body(12f,true),sorted?Theme.Accent:Theme.Dim)},area.Left+Theme.S(12),Ink.Baseline(middle,Ink.Body(12f,true)));
   if(sorted)Ink.Arrow(g,x+Theme.S(5),middle,Descending,Theme.Accent);
  };
  DrawSubItem+=(_,e)=>
  {
   if(e.Item==null||e.ItemIndex>=rows.Count)return;var row=rows[e.ItemIndex];var g=e.Graphics;var cell=e.Bounds;
   bool picked=e.Item.Selected;float middle=cell.Top+cell.Height/2f;
   using(var back=new SolidBrush(picked?Theme.RowPick:row.Tie?Theme.PbWash:Theme.Table))g.FillRectangle(back,cell);
   using(var line=new Pen(Theme.RowLine))g.DrawLine(line,cell.Left,cell.Bottom-1,cell.Right,cell.Bottom-1);
   if(e.ColumnIndex==0)
   {
    if(picked)using(var bar=new SolidBrush(Theme.Stripe))g.FillRectangle(bar,cell.Left,cell.Top,Theme.S(3),cell.Height-1);
    // 최고 SCORE 와 같은 점수만 굵게 그린다(파란색과 함께). 그 밖의 점수는 보통 굵기다
    var parts=Ink.Score(row.Play,14f,12f,row.Tie?Theme.Pb:Theme.Score,false,row.Tie);
    float x=Ink.Draw(g,parts,cell.Left+Theme.S(12),Ink.Baseline(middle,Ink.Number(14f,row.Tie)));
    if(row.Pb)
    {
     // PB 표식: 파란 테두리의 작은 네모 안에 'PB'
     var mark=new[]{new InkPart("PB",Ink.Number(10f),Theme.Pb)};float wide=Ink.Width(mark)+Theme.S(8),tall=Theme.S(15);
     var box=new RectangleF(x+Theme.S(7),middle-tall/2f,wide,tall);
     var smoothing=g.SmoothingMode;g.SmoothingMode=SmoothingMode.AntiAlias;
     using(var shape=BadgeDesign.Rounded(box,Theme.S(2f)))using(var pen=new Pen(Theme.Pb))g.DrawPath(pen,shape);
     g.SmoothingMode=smoothing;
     Ink.Draw(g,mark,box.Left+Theme.S(4),Ink.Baseline(middle,Ink.Number(10f)));
    }
   }
   else
   {
    string time=row.Group!=null?"일시 미상":ScoreText.Time(row.Play);
    string? note=row.Group!=null?string.Join(", ",row.Group.Select(p=>PlaySource.Label(p.Source))):row.Reflected?ScoreText.Reflected(row.Play):null;
    var parts=new List<InkPart>{new(time,Ink.Number(12f,false),Theme.Dim)};if(note!=null)parts.Add(new(note,Ink.Body(11f),Theme.Dim,9));
    Ink.Draw(g,parts,cell.Left+Theme.S(12),Ink.Baseline(middle,Ink.Number(12f,false)));
   }
  };
  ColumnClick+=(_,e)=>{bool score=e.Column==0;Order(score,score==ByScore?!Descending:true);};
  // 칸의 너비는 표가 정한다(43% : 나머지). 사용자가 끌어 바꾸지 못한다
  ColumnWidthChanging+=(_,e)=>{if(sizing)return;e.Cancel=true;e.NewWidth=Columns[e.ColumnIndex].Width;};
 }
 // 창이 뜰 때의 키우기에서는 자리와 크기만 따른다(글꼴과 줄 높이는 이미 단계에 맞췄다). 칸의 너비는 크기가 바뀔 때 다시 나눈다
 protected override void OnResize(EventArgs e){base.OnResize(e);if(Columns.Count!=2)return;sizing=true;try{int wide=ClientSize.Width;Columns[0].Width=(int)(wide*.43);Columns[1].Width=Math.Max(10,wide-Columns[0].Width);}finally{sizing=false;}}
 bool sizing;
 /// <summary>처음의 차례(날짜·시각 최신순)로 돌린다. 다른 판정의 히스토리를 열 때 쓴다</summary>
 public void ResetOrder(){ByScore=false;Descending=true;}
 /// <summary>머리글을 눌렀을 때의 정렬(같은 칸을 다시 누르면 방향이 바뀐다)</summary>
 internal void Order(bool byScore,bool descending){ByScore=byScore;Descending=descending;OrderChanged?.Invoke();}
 public void Display(IReadOnlyList<HistoryRow> shown){BeginUpdate();try{SelectedIndices.Clear();rows=shown;VirtualListSize=shown.Count;}finally{EndUpdate();}Invalidate();}
 public HistoryRow? Picked=>SelectedIndices.Count==1&&SelectedIndices[0]<rows.Count?rows[SelectedIndices[0]]:null;
 public void Pick(int index){if(index<0||index>=rows.Count){SelectedIndices.Clear();return;}if(SelectedIndices.Count==1&&SelectedIndices[0]==index)return;SelectedIndices.Clear();SelectedIndices.Add(index);EnsureVisible(index);}
 // 줄이 하나도 없을 때의 글. 목록이 스스로 그린 뒤에 덧그린다
 protected override void WndProc(ref Message m)
 {
  base.WndProc(ref m);
  if(m.Msg!=0x000F||VirtualListSize>0||!IsHandleCreated)return;
  using var g=Graphics.FromHwnd(Handle);var words=new[]{new InkPart("등록한 기록이 없습니다.",Ink.Body(12f),Theme.Dim)};
  Ink.Draw(g,words,Theme.S(12),Ink.Baseline(RowsEnd()+Theme.S(20),Ink.Body(12f)));
 }
 /// <summary>창을 없애기 직전에 줄을 비운다(기록 목록과 같은 까닭: 줄이 남은 채 없애면 접근성 정보를 끊느라 오래 걸릴 수 있다)</summary>
 internal void ReleaseRows(){if(IsDisposed||!IsHandleCreated||VirtualListSize==0)return;Display(Array.Empty<HistoryRow>());}
 protected override void Dispose(bool disposing){if(disposing)spacing.Dispose();base.Dispose(disposing);}
}

/// <summary>그래프 아래의 '일시 미상 기록': 같은 SCORE 는 한 줄에 출처마다 단추 하나. 단추를 누르면 그 기록을 고른다(삭제할 수 있다)</summary>
sealed class UndatedStrip:Control
{
 IReadOnlyList<UndatedGroup> groups=Array.Empty<UndatedGroup>();int? picked;readonly List<(RectangleF Area,Play Play)> hits=new();int hot=-1,at;
 public event Action<Play>? Chosen;
 public const int RowPitch=27,TopGap=9;
 public UndatedStrip(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.Selectable,true);TabStop=false;AccessibleName="일시 미상 기록";}
 /// <summary>줄 수에 맞는 높이(100% 때의 값). 줄이 없으면 0</summary>
 public static int Tall(int count)=>count==0?0:TopGap+count*RowPitch-3;
 // 출처가 있을 때만 Tab 으로 들어올 수 있다
 public void Set(IReadOnlyList<UndatedGroup> shown,int? pickedSeq){groups=shown;picked=pickedSeq;TabStop=shown.Count>0;at=Math.Clamp(at,0,Math.Max(0,Sources.Count-1));Invalidate();}
 public IReadOnlyList<(RectangleF Area,Play Play)> Hits=>hits;
 /// <summary>출처 단추들(그리는 차례: 위의 줄부터, 한 줄 안에서는 왼쪽부터)</summary>
 internal IReadOnlyList<Play> Sources=>groups.SelectMany(g=>g.Plays).ToList();
 /// <summary>키보드의 초점이 놓인 출처</summary>
 internal Play? Keyed=>Sources is {Count:>0} sources?sources[Math.Clamp(at,0,sources.Count-1)]:null;
 // 키보드: Tab 으로 들어오면 고른 출처(없으면 첫 출처)에 초점이 놓인다. 화살표로 옮기고 스페이스·Enter 로 고른다. 고른 뒤에는 아래의 '선택 기록 삭제'를 쓴다
 protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);Arrive();}
 /// <summary>초점이 들어올 때 놓일 자리: 고른 출처가 있으면 거기, 없으면 첫 출처(앞에 보던 판정에서의 자리를 이어 쓰지 않는다)</summary>
 internal void Arrive(){int now=Sources.ToList().FindIndex(p=>p.Seq==picked);at=Math.Max(0,now);Invalidate();}
 protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);Invalidate();}
 protected override bool IsInputKey(Keys key)=>key is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Enter or Keys.Space||base.IsInputKey(key);
 protected override void OnKeyDown(KeyEventArgs e){base.OnKeyDown(e);if(Key(e.KeyCode))e.Handled=true;}
 /// <summary>키 하나를 받는다(받았으면 true)</summary>
 internal bool Key(Keys key)
 {
  var sources=Sources;if(sources.Count==0)return false;
  switch(key)
  {
   case Keys.Left or Keys.Up:at=Math.Max(0,at-1);Invalidate();return true;
   case Keys.Right or Keys.Down:at=Math.Min(sources.Count-1,at+1);Invalidate();return true;
   case Keys.Space or Keys.Enter:Chosen?.Invoke(sources[Math.Clamp(at,0,sources.Count-1)]);return true;
   default:return false;
  }
 }
 protected override void OnPaint(PaintEventArgs e)
 {
  var g=e.Graphics;g.Clear(Parent?.BackColor??Theme.Window);hits.Clear();if(groups.Count==0)return;
  using(var line=new Pen(Theme.Line))g.DrawLine(line,0,0,Width,0);
  int pitch=Theme.S(RowPitch),tall=Theme.S(22);float top=Theme.S(TopGap);
  Ink.Draw(g,new[]{new InkPart("일시 미상 기록",Ink.Head(12f),Theme.Dim)},0,Ink.Baseline(top+tall/2f,Ink.Body(12f,true)));
  var smoothing=g.SmoothingMode;
  for(int i=0;i<groups.Count;i++)
  {
   float middle=top+i*pitch+tall/2f,x=Theme.S(86);
   Ink.Draw(g,new[]{new InkPart(ScoreText.Points(groups[i].Score),Ink.Number(13f),Theme.Score)},x,Ink.Baseline(middle,Ink.Number(13f)));
   x+=Theme.S(85);
   foreach(var play in groups[i].Plays)
   {
    // 환산한 점수의 출처에는 환산이라고 적는다(같은 SCORE 에 세부 SCORE 를 아는 출처와 함께 묶여도 그 출처에만)
    bool on=play.Seq==picked,over=hits.Count==hot,keyed=Focused&&hits.Count==at;var words=new[]{new InkPart(ScoreText.Source(play),Ink.Body(12f),on?Theme.Accent:Theme.Dim)};
    var area=new RectangleF(x,middle-tall/2f,Ink.Width(words)+Theme.S(14),tall);
    g.SmoothingMode=SmoothingMode.AntiAlias;
    using(var shape=BadgeDesign.Rounded(new RectangleF(area.X,area.Y,area.Width-1,area.Height-1),Theme.S(3f)))
    {
     using(var fill=new SolidBrush(over?Theme.HoverBack:Theme.Table))g.FillPath(fill,shape);
     using var pen=new Pen(on?Theme.Accent:over?Theme.HoverEdge:Theme.Line);g.DrawPath(pen,shape);
    }
    // 키보드의 초점: 단추 바깥에 강조색 외곽선(다른 단추들과 같은 표시)
    if(keyed)using(var around=BadgeDesign.Rounded(new RectangleF(area.X-2,area.Y-2,area.Width+3,area.Height+3),Theme.S(4f)))using(var ring=new Pen(Theme.Accent,Theme.S(1.5f)))g.DrawPath(ring,around);
    g.SmoothingMode=smoothing;
    Ink.Draw(g,words,area.X+Theme.S(7),Ink.Baseline(middle,Ink.Body(12f)));
    hits.Add((area,play));x=area.Right+Theme.S(6);
   }
  }
 }
 int At(Point point){for(int i=0;i<hits.Count;i++)if(hits[i].Area.Contains(point))return i;return -1;}
 protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);int now=At(e.Location);if(now!=hot){hot=now;Cursor=now>=0?Cursors.Hand:Cursors.Default;Invalidate();}}
 protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);if(hot!=-1){hot=-1;Invalidate();}}
 protected override void OnMouseClick(MouseEventArgs e){base.OnMouseClick(e);int now=At(e.Location);if(now<0)return;at=now;Focus();Chosen?.Invoke(hits[now].Play);}
}

/// <summary>히스토리 창의 기억하는 설정(목록 창이 records-view.json 에 함께 적는다)</summary>
sealed class HistoryPrefs{public bool Folded;public bool PbLine=true;}

class HistoryDialog:AppWindow
{
 readonly Catalog catalog;readonly RecordStore store;readonly string profileId;
 Selection chart;string songName="",artist="";HistoryModel model;long loadedRevision=-1;int? picked;bool syncing;
 readonly System.Windows.Forms.Timer refreshTimer=new(){Interval=250};
 readonly Panel root=new(){Dock=DockStyle.Fill};readonly HistoryHead head=new();readonly Panel tableFrame=new(){Tag="line",Padding=new(1)};readonly HistoryTable table=new(){Dock=DockStyle.Fill};readonly UndatedStrip undated=new();
 readonly Button edit=new(){Text="선택 기록 수정"},delete=new(){Text="선택 기록 삭제"},close=new(){Text="닫기",DialogResult=DialogResult.Cancel};readonly Control footer;
 readonly GraphFold fold=new();readonly HistoryGraph graph=new();readonly HistoryPrefs prefs;readonly Action? prefsChanged;bool freshChart=true;
 readonly ComboBox range=new ThemedCombo(){DropDownStyle=ComboBoxStyle.DropDownList,Width=112,Tallness=25,Roomy=true,AccessibleName="그래프 범위"};
 readonly CheckBox pbLine=new(){Text="PB 기준선",AutoSize=true,Checked=true};
 const int ContentWidth=782,Side=18,HeadTall=84,FooterGap=12,FooterBottom=13,GraphHeadTall=43,GraphTall=215;
 internal HistoryGraph Graph=>graph;
 internal GraphFold Fold=>fold;
 internal ComboBox RangeBox=>range;
 internal CheckBox PbLineBox=>pbLine;
 public string ProfileId=>profileId;
 public Selection Chart=>chart;
 internal HistoryModel Model=>model;
 internal HistoryTable Table=>table;
 internal UndatedStrip Undated=>undated;
 /// <summary>고른 판의 순번(표의 줄이든 일시 미상 기록이든)</summary>
 internal int? PickedSeq=>picked;
 /// <summary>'선택한 기록이 이미 삭제되었습니다.'를 알린 뒤 닫혔는가(목록으로 돌아간다)</summary>
 internal bool ReturnedToList{get;private set;}

 public HistoryDialog(Catalog catalog,RecordStore store,RegisteredRecord record,HistoryPrefs? prefs=null,Action? prefsChanged=null)
 {
  this.catalog=catalog;this.store=store;profileId=store.Active.Id;chart=record.Chart;model=new(chart.Key,Array.Empty<Play>());this.prefs=prefs??new();this.prefsChanged=prefsChanged;
  Font=Theme.Px(AppFonts.Ui,9.75f);FormBorderStyle=FormBorderStyle.FixedSingle;MaximizeBox=false;MinimizeBox=false;ShowInTaskbar=false;StartPosition=FormStartPosition.CenterParent;ClientSize=new(818,615);
  tableFrame.Controls.Add(table);footer=Parts.Footer(ContentWidth,edit,delete,close);
  root.Controls.AddRange(new Control[]{head,tableFrame,fold,range,pbLine,graph,undated,footer});Controls.Add(root);CancelButton=close;
  // 그래프: 접기, 전체 / PB 근처 확대, PB 기준선. 점을 누르면 그 판을 고르고 표가 그 줄로 간다
  range.Items.AddRange(new object[]{"전체 그래프","PB 근처 확대"});range.SelectedIndex=0;pbLine.Font=Theme.Px(AppFonts.Ui,9f);
  fold.Expanded=!this.prefs.Folded;pbLine.Checked=this.prefs.PbLine;graph.PbLine=this.prefs.PbLine;
  fold.Toggled+=()=>{this.prefs.Folded=!fold.Expanded;this.prefsChanged?.Invoke();Arrange();};
  pbLine.CheckedChanged+=(_,_)=>{graph.PbLine=pbLine.Checked;this.prefs.PbLine=pbLine.Checked;this.prefsChanged?.Invoke();};
  range.SelectedIndexChanged+=(_,_)=>{if(!syncing)graph.Zoom=range.SelectedIndex==1;};
  graph.Chosen+=play=>{picked=play.Seq;ShowPick(true);};
  close.Click+=(_,_)=>Close();edit.Click+=(_,_)=>EditSelected();delete.Click+=(_,_)=>DeleteSelected();
  // 표에서 고른 줄이 곧 수정·삭제의 대상이다. 표의 빈 곳을 눌러 선택을 풀면 대상도 없어진다(단추가 꺼진다).
  // 아래의 일시 미상 출처를 골라 표의 선택이 비는 것은 코드가 한 일이라(syncing) 여기로 오지 않는다: 그 출처가 대상으로 남는다
  table.SelectedIndexChanged+=(_,_)=>{if(syncing)return;picked=table.Picked?.Play.Seq;ShowPick(false);};
  table.MouseUp+=(_,_)=>{if(!syncing&&table.Picked==null&&picked is int seq&&table.Rows.Any(r=>r.Group!=null?r.Group.Any(p=>p.Seq==seq):r.Play.Seq==seq)){picked=null;ShowPick(false);}};
  tableFrame.TabIndex=0;fold.TabIndex=1;range.TabIndex=2;pbLine.TabIndex=3;graph.TabIndex=4;undated.TabIndex=5;footer.TabIndex=6;
  table.DoubleClick+=(_,_)=>{if(table.Picked is {Editable:true})EditSelected();};   // 고칠 수 없는 줄을 두 번 눌러도 아무 일도 하지 않는다
  table.OrderChanged+=()=>ShowRows();
  undated.Chosen+=play=>{picked=play.Seq;ShowPick(true);};
  refreshTimer.Tick+=(_,_)=>{if(loadedRevision!=store.Revision)Reload();};VisibleChanged+=(_,_)=>refreshTimer.Enabled=Visible;
  root.Resize+=(_,_)=>Arrange();
  Open(record);
 }
 /// <summary>목록에서 다른 줄을 열었다: 같은 창이 그 판정으로 바뀐다</summary>
 public void Open(RegisteredRecord record)
 {
  // 다른 판정을 열면 표는 처음의 차례(최신순)로 시작한다. 같은 히스토리를 다시 읽을 때는 사용자가 고른 차례를 그대로 둔다(Reload)
  if(record.Chart.Key!=chart.Key||loadedRevision<0){table.ResetOrder();freshChart=true;}
  chart=record.Chart;songName=record.SongName;artist=record.Artist;picked=null;loadedRevision=-1;Reload();
  // 처음에는 가장 최근 판을 고른다
  if(model.Dated.Count>0){picked=model.Dated[^1].Seq;ShowPick(true);}
 }
 protected override void Dispose(bool disposing){if(disposing){refreshTimer.Dispose();table.ReleaseRows();}base.Dispose(disposing);}
 // 창이 뜨기 전에 고른 줄은 목록이 만들어질 때 사라진다(가상 목록). 뜬 뒤에 다시 고른다
 protected override void OnShown(EventArgs e){base.OnShown(e);ShowPick(true);}

 int U(int value)=>(int)Math.Round(value*Unit);
 // 고정된 창 안의 자리. 일시 미상 기록의 줄 수만큼 표가 줄어든다(창의 크기와 아래 단추의 자리는 그대로다)
 void Arrange()
 {
  int wide=root.ClientSize.Width,tall=root.ClientSize.Height,left=U(Side),inner=wide-left*2;if(inner<=0||tall<=0)return;
  head.Bounds=new(0,0,wide,U(HeadTall));
  footer.Location=new(wide-left-footer.Width,tall-U(FooterBottom)-footer.Height);
  int under=U(UndatedStrip.Tall(model.Undated.Count)),bottom=footer.Top-U(FooterGap);
  undated.Visible=under>0;undated.Bounds=new(left,bottom-under,inner,under);
  // 그래프는 일시 미상 기록 위에 놓인다. 접으면 제목 줄만 남고 그 자리를 표가 쓴다
  int graphBottom=under>0?undated.Top-U(9):bottom;bool open=fold.Expanded;int graphTall=open?U(GraphTall):0,foldTop=graphBottom-graphTall-U(GraphHeadTall),row=foldTop+U(11);
  graph.Visible=open;graph.Bounds=new(left,graphBottom-graphTall,inner,graphTall);
  fold.Location=new(left,row);
  pbLine.Visible=open;pbLine.Location=new(left+inner-pbLine.Width,row+(fold.Height-pbLine.Height)/2);
  range.Visible=open&&graph.ZoomOffered;range.Location=new(pbLine.Left-U(12)-range.Width,row+(fold.Height-range.Height)/2);
  tableFrame.Bounds=new(left,head.Bottom,inner,Math.Max(U(60),foldTop-head.Bottom));
 }
 /// <summary>저장에서 다시 읽어 모두 다시 그린다. 고른 판이 남아 있으면 그대로 고른 채다</summary>
 void Reload()
 {
  var profile=store.Data.Profiles.FirstOrDefault(p=>p.Id==profileId);
  model=profile==null?new(chart.Key,Array.Empty<Play>()):HistoryModel.Read(store,chart.Key,profile);loadedRevision=store.Revision;
  Text="기록 히스토리 · "+(profile?.Name??"");
  head.Set(songName,artist,new[]{$"{chart.Button}B",chart.Pattern,chart.Judge+"판정"},model.Best);
  if(picked is int seq&&model.All.All(p=>p.Seq!=seq))picked=null;
  graph.Display(model,picked,freshChart);freshChart=false;
  syncing=true;try{range.SelectedIndex=graph.Zoom?1:0;}finally{syncing=false;}
  Arrange();ShowRows();
 }
 void ShowRows(){syncing=true;try{table.Display(model.Rows(table.ByScore,table.Descending));}finally{syncing=false;}table.Invalidate();ShowPick(true);}
 // 고른 판을 표와 일시 미상 기록에 나타내고 단추를 맞춘다. 일시 미상 기록을 골랐고 그 SCORE 가 'PB · 일시 미상' 줄의 것이면 그 줄도 고른 줄로 보인다
 void ShowPick(bool scroll)
 {
  int index=-1;
  if(picked is int seq)for(int i=0;i<table.Rows.Count;i++){var row=table.Rows[i];if(row.Group!=null?row.Group.Any(p=>p.Seq==seq):row.Play.Seq==seq){index=i;break;}}
  syncing=true;try{if(scroll||index<0)table.Pick(index);}finally{syncing=false;}
  undated.Set(model.Undated,picked);graph.Pick(picked);
  var play=Picked();edit.Enabled=play!=null&&model.Editable(play);delete.Enabled=play!=null;
 }
 Play? Picked()=>picked is int seq?model.All.FirstOrDefault(p=>p.Seq==seq):null;
 internal void Pick(int? seq){picked=seq;ShowPick(true);}
 bool Known()=>catalog.Songs.Any(s=>s.Id==chart.SongId&&s.Charts.Contains(new(chart.Button,chart.Pattern)));
 // 창이 보여 준 것이 저장보다 늦었으면(방금 다른 저장이 있었다) 먼저 다시 읽는다. 고른 판이 그사이 사라졌으면 고른 것이 없어진다
 Play? Current(){if(loadedRevision!=store.Revision)Reload();return Picked();}
 void BackToList(bool edit){PlayNotice.Gone(this,edit);ReturnedToList=true;Close();}

 /// <summary>
 /// 선택 기록 수정. 창이 보여 준 판(순번과 내용)이 저장하는 순간에도 그대로일 때만 고친다.
 /// ask 는 개발용 검사가 수정 창 대신 답하는 자리다(새 SCORE 를 돌려주거나, 취소면 null)
 /// </summary>
 internal bool EditSelected(Func<Play,int?>? ask=null)
 {
  if(Current() is not {} shown||!model.Editable(shown))return false;
  if(store.Active.Id!=profileId){PlayNotice.OtherProfile(this,true);return false;}
  if(!Known()){PlayNotice.NoPattern(this);return false;}
  int? score;
  if(ask!=null)score=ask(shown);
  else{using var dialog=new EditPlayDialog(songName,$"{chart.Button}B",chart.Pattern,chart.Judge,shown,shown.Normal==NormalLink.Linked);score=dialog.ShowDialog(this)==DialogResult.OK?dialog.Value:null;}
  if(score is not int wanted||wanted==shown.Score)return false;
  try
  {
   if(store.Active.Id!=profileId){PlayNotice.OtherProfile(this,true);return false;}
   var now=store.Active.Plays.FirstOrDefault(p=>p.Seq==shown.Seq);
   if(now==null){Reload();BackToList(true);return false;}
   if(now!=shown){Reload();PlayNotice.Changed(this,true);return false;}
   store.Edit(shown.Seq,wanted);Reload();return true;
  }
  catch(Exception error){Reload();PlayNotice.Failed(this,true,error);return false;}
 }
 /// <summary>지금 고른 것을 지우려 할 때 확인 창이 보일 것. 'PB · 일시 미상' 줄이나 출처가 여럿인 일시 미상 SCORE 는 그 출처들이 후보다(고른 출처가 맨 앞)</summary>
 internal DeleteRequest? Request()
 {
  if(Current() is not {} play)return null;
  IReadOnlyList<Play> candidates=new[]{play};
  if(play.At==null&&model.Undated.FirstOrDefault(g=>g.Score==play.Score) is {Plays.Count:>1} group)candidates=group.Plays.OrderBy(p=>p.Seq==play.Seq?0:1).ToList();
  bool reflected=model.Reflected(play);
  return new(songName,$"{chart.Button}B",chart.Pattern,chart.Judge,candidates,reflected,target=>HistoryModel.Emptied(store,target,reflected));
 }
 /// <summary>
 /// 선택 기록 삭제. 노말에 보이는 하드·맥스의 판은 노말 반영만 끊는다(원래 판은 남는다). 확인 창이 보여 준 판이 승인하는 순간에도 그대로일 때만 한다.
 /// ask 는 개발용 검사가 확인 창 대신 답하는 자리다(지울 판을 돌려주거나, 취소면 null)
 /// </summary>
 internal bool DeleteSelected(Func<DeleteRequest,Play?>? ask=null)
 {
  if(Request() is not {} request)return false;
  if(store.Active.Id!=profileId){PlayNotice.OtherProfile(this,false);return false;}
  Play? target;
  if(ask!=null)target=ask(request);
  else{using var dialog=new DeletePlayDialog(request);target=dialog.ShowDialog(this)==DialogResult.Yes?dialog.Chosen:null;}
  if(target==null)return false;
  try
  {
   if(store.Active.Id!=profileId){PlayNotice.OtherProfile(this,false);return false;}
   var now=store.Active.Plays.FirstOrDefault(p=>p.Seq==target.Seq);
   if(now==null){Reload();BackToList(false);return false;}
   if(now!=target){Reload();PlayNotice.Changed(this,false);return false;}
   if(request.Reflected)store.CancelNormal(target.Seq);else store.Delete(target.Seq);
   Reload();return true;
  }
  catch(Exception error){Reload();PlayNotice.Failed(this,false,error);return false;}
 }
}
