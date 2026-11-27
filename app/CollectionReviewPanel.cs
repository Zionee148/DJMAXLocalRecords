using System.Drawing.Drawing2D;
namespace LadderRecords;
// 컬렉션 가져오기의 판독 검수 화면: 표의 머리(ReviewHead), 한 줄(ReviewLine), 곡 선택 창(SongPicker), 캡처 보기 창(CaptureViewer).
// 표의 줄은 읽은 결과마다 수가 달라 창이 뜬 뒤에 만든다. 창이 뜰 때 키우는 단계(AppWindow.Enlarge)를 거치지 않으므로 자리·크기·글꼴을 처음부터 그 창의 배수(unit)로 만든다.

/// <summary>표의 열(100% 때의 자리와 너비). 줄의 너비는 표 834 에서 세로 스크롤 막대의 자리를 뺀 816</summary>
static class ReviewColumns
{
 public const int Table=834,Line=816;
 public static readonly (string Name,int X,int W)[] All={("검수할 내용",12,96),("곡",112,170),("버튼·패턴",290,110),("스코어",404,176),("캡처",584,146),("제외하기",736,80)};
}

/// <summary>표의 머리글(옅은 바탕, 아래 선). 창이 뜨기 전에 만들어 창과 함께 커진다</summary>
sealed class ReviewHead:Control
{
 public ReviewHead(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);TabStop=false;Margin=Padding.Empty;}
 protected override void OnPaint(PaintEventArgs e)
 {
  var g=e.Graphics;g.Clear(Theme.Field);float k=Width/(float)ReviewColumns.Table;
  using(var line=new Pen(Theme.Line)){g.DrawLine(line,0,Height-1,Width,Height-1);g.DrawRectangle(line,0,0,Width-1,Height-1);}
  var font=Ink.Body(12f,true);float baseline=Ink.Baseline(Height/2f,font);
  foreach(var (name,x,w) in ReviewColumns.All)
  {
   var part=new[]{new InkPart(name,font,Theme.Text)};
   if(name=="제외하기")Ink.Draw(g,part,(x+w/2f)*k-Ink.Width(part)/2,baseline);else Ink.Draw(g,part,x*k,baseline);
  }
 }
}

/// <summary>
/// 검수 표의 한 줄. 곡 칸(곡 선택 단추 또는 정해진 곡명, 그 아래 안내), 버튼·패턴, 스코어 칸(읽은 점수 또는 점수 입력 칸·'기록 없음 (-)'·안내), 캡처 보기, 제외하기.
/// 제외하면 곡 단추·입력 칸·기록 없음을 막는다(값은 남는다). 캡처 보기와 제외 체크는 그대로 쓸 수 있다
/// </summary>
sealed class ReviewLine:Panel
{
 public readonly ReviewRow Row;readonly float u;
 public readonly Label Kind,PatternText;public readonly Button? Pick;public readonly Label? Fixed;public readonly WordLabel SongNote;
 public readonly Label? Read;public readonly InputBox? Input;public readonly CheckBox? None;public readonly WordLabel? ScoreNote;
 public readonly Button CaptureButton;public readonly CheckBox Exclude;
 bool syncing;
 int U(float px)=>(int)Math.Round(px*u);
 Font F(float points,FontStyle style=FontStyle.Regular)=>u==1f?Theme.Px(AppFonts.Ui,points,style):Theme.PxS(AppFonts.Ui,points,style);
 public ReviewLine(ReviewRow row,float unit,string songTitle,int shots,Action changed,Action<ReviewGroup> pick,Action<ReviewRow> capture)
 {
  Row=row;u=unit;SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
  Margin=Padding.Empty;Width=U(ReviewColumns.Line);BackColor=Theme.Table;Font=F(9.75f);
  int Col(int i)=>U(ReviewColumns.All[i].X);int W(int i)=>U(ReviewColumns.All[i].W);
  Kind=new Label(){Text=row.Label,AutoSize=true,UseMnemonic=false,BackColor=Color.Transparent,Location=new(Col(0),U(16))};Controls.Add(Kind);
  if(row.Group.NeedsSong){Pick=new Button(){Text="곡 선택…",AutoEllipsis=true,Size=new(U(150),U(32)),Location=new(Col(1),U(9)),UseMnemonic=false};Pick.Click+=(_,_)=>pick(row.Group);Controls.Add(Pick);}
  else{Fixed=new Label(){Text=songTitle,AutoSize=false,AutoEllipsis=true,UseMnemonic=false,BackColor=Color.Transparent,Size=new(W(1),U(22)),Location=new(Col(1),U(15))};Controls.Add(Fixed);}
  SongNote=new WordLabel("",W(1),9f,"accent"){Location=new(Col(1),U(46))};SongNote.Font=F(9f);SongNote.Width=W(1);SongNote.Visible=false;Controls.Add(SongNote);
  PatternText=new Label(){Text=row.Pattern,AutoSize=true,UseMnemonic=false,BackColor=Color.Transparent,Location=new(Col(2),U(16))};Controls.Add(PatternText);
  if(!row.NeedsScore){Read=new Label(){Text=row.Read=="-"?"-":int.TryParse(row.Read,out int v)?$"{v:N0}":row.Read,AutoSize=true,UseMnemonic=false,BackColor=Color.Transparent,Location=new(Col(3),U(16))};Controls.Add(Read);}
  else
  {
   Input=new InputBox(U(110),"",7,U(32)){Location=new(Col(3),U(9))};Input.Box.Font=F(9f);Input.Box.Width=U(88);Input.Box.Left=U(11);Input.Box.Hint="점수 입력";Input.Box.AccessibleName=$"{row.Pattern} 점수 입력";Controls.Add(Input);
   Input.Box.TextChanged+=(_,_)=>{if(syncing)return;row.Typed=Input.Box.Text;changed();};
   // 체크 상자는 투명 바탕을 주지 않는다: 직접 그리는 것(Theme.PaintCheck)이 바탕을 칠해 Windows 가 먼저 그린 글자를 덮어야 한 번만 보인다
   None=new CheckBox(){Text="기록 없음 (-)",AutoSize=true,UseMnemonic=false,BackColor=BackColor,Font=F(9f),Location=new(Col(3),U(46))};Controls.Add(None);
   None.CheckedChanged+=(_,_)=>{if(syncing)return;row.None=None.Checked;changed();};
   ScoreNote=new WordLabel(CollectionReview.BadScore,W(3),9f,"accent"){Location=new(Col(3),U(72))};ScoreNote.Font=F(9f);ScoreNote.Width=W(3);ScoreNote.Visible=false;Controls.Add(ScoreNote);
  }
  CaptureButton=new Button(){Text=$"캡처 보기 ({shots}장)",Size=new(U(130),U(32)),Location=new(Col(4),U(9)),UseMnemonic=false};CaptureButton.Click+=(_,_)=>capture(row);Controls.Add(CaptureButton);
  Exclude=new CheckBox(){Text="",AutoSize=false,Size=new(U(20),U(20)),Location=new(Col(5)+W(5)/2-U(8),U(15)),BackColor=BackColor,AccessibleName=$"{row.Pattern} 제외하기"};Controls.Add(Exclude);
  Exclude.CheckedChanged+=(_,_)=>{if(syncing)return;row.Excluded=Exclude.Checked;changed();Exclude.Focus();};
  Height=U(row.NeedsScore?80:52);
 }
 /// <summary>모델의 지금 값으로 막힘·글·안내와 줄의 높이를 맞춘다</summary>
 public void Sync(CollectionReview review)
 {
  syncing=true;
  try
  {
   bool off=Row.Excluded;var group=Row.Group;
   // 곡은 묶음 전체가 함께 쓴다: 묶음의 줄을 모두 제외했어도 곡을 고르거나 바꾸거나 풀 수 있다(겹친 곡을 풀거나, 제목이 흔들린 곡을 고르게)
   bool songOpen=!off||group.AllExcluded;
   if(Pick!=null){Pick.Text=group.Chosen==null?"곡 선택…":$"{group.Chosen.Name} · {group.Chosen.Artist}";Pick.Enabled=songOpen;}
   var notes=review.SongNotes(group);SongNote.Text=string.Join(" ",notes);bool songNote=notes.Count>0&&songOpen;SongNote.Visible=songNote;
   if(Input!=null)
   {
    string shown=Row.None?"":Row.Typed;if(Input.Box.Text!=shown)Input.Box.Text=shown;   // 기록 없음을 고르면 적은 값을 숨기고, 풀면 되살린다
    Input.Enabled=!off&&!Row.None;None!.Checked=Row.None;None.Enabled=!off;
    ScoreNote!.Visible=Row.ScoreBad;
   }
   Exclude.Checked=off;
   int bottom=U(Row.NeedsScore?80:52);
   if(songNote)bottom=Math.Max(bottom,SongNote.Bottom+U(10));
   if(ScoreNote!=null&&ScoreNote.Visible)bottom=Math.Max(bottom,ScoreNote.Bottom+U(10));
   if(Height!=bottom)Height=bottom;
  }
  finally{syncing=false;}
  Invalidate();
 }
 protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);using var line=new Pen(Theme.RowLine);e.Graphics.DrawLine(line,0,Height-1,Width,Height-1);}
 /// <summary>줄의 바탕이 바뀌면(테마) 체크 상자의 바탕도 같게 둔다</summary>
 protected override void OnBackColorChanged(EventArgs e){base.OnBackColorChanged(e);if(None!=null)None.BackColor=BackColor;if(Exclude!=null)Exclude.BackColor=BackColor;}
}

/// <summary>곡 선택의 목록(곡명 뒤에 옅은 아티스트). 누르거나 Enter 로 고른다</summary>
sealed class SongList:OwnerTable
{
 IReadOnlyList<Song> songs=Array.Empty<Song>();readonly ImageList spacing=new();
 public const int RowHeight=40;
 public event Action<Song>? Picked;
 public IReadOnlyList<Song> Songs=>songs;
 public SongList()
 {
  HeaderStyle=ColumnHeaderStyle.None;TabStop=true;AccessibleName="곡 목록";Font=Theme.Px(AppFonts.Ui,13f);
  spacing.ImageSize=new(1,Theme.S(RowHeight));spacing.Images.Add(new Bitmap(1,1));SmallImageList=spacing;
  Columns.Add(new ColumnHeader(){Width=400});
  RetrieveVirtualItem+=(_,e)=>{var song=e.ItemIndex<songs.Count?songs[e.ItemIndex]:null;e.Item=new ListViewItem(song==null?"":$"{song.Name} {song.Artist}"){ImageIndex=0};};
  DrawSubItem+=(_,e)=>
  {
   if(e.ItemIndex>=songs.Count)return;var song=songs[e.ItemIndex];var g=e.Graphics;var cell=e.Bounds;bool hot=e.Item!=null&&(e.Item.Selected||e.Item.Focused&&Focused);
   using(var back=new SolidBrush(hot?Theme.Field:Theme.Table))g.FillRectangle(back,cell);
   using(var line=new Pen(Theme.RowLine))g.DrawLine(line,cell.Left,cell.Bottom-1,cell.Right,cell.Bottom-1);
   var title=Ink.Title(13f);float baseline=Ink.Baseline(cell.Top+cell.Height/2f,title);float x=cell.Left+Theme.S(12);
   var artist=new[]{new InkPart(song.Artist,Ink.Body(12f),Theme.Dim)};float artistWidth=Math.Min(Ink.Width(artist),cell.Width*.45f);
   Ink.DrawClipped(g,song.Name,title,Theme.Text,x,baseline,cell.Width-Theme.S(36)-artistWidth);
   float used=Math.Min(Ink.Width(new[]{new InkPart(song.Name,title,Theme.Text)}),cell.Width-Theme.S(36)-artistWidth);
   Ink.DrawClipped(g,song.Artist,Ink.Body(12f),Theme.Dim,x+used+Theme.S(10),baseline,artistWidth);
  };
  MouseClick+=(_,e)=>{var hit=HitTest(e.Location);if(hit.Item!=null&&hit.Item.Index<songs.Count)Picked?.Invoke(songs[hit.Item.Index]);};
  KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Enter&&FocusedItem is {} item&&item.Index<songs.Count){Picked?.Invoke(songs[item.Index]);e.Handled=true;}};
  ClientSizeChanged+=(_,_)=>{if(Columns.Count>0&&ClientSize.Width>0&&Columns[0].Width!=ClientSize.Width)Columns[0].Width=ClientSize.Width;};
 }
 public void Set(IReadOnlyList<Song> list){songs=list;VirtualListSize=list.Count;Invalidate();}
 protected override void Dispose(bool disposing){if(disposing)spacing.Dispose();base.Dispose(disposing);}
}

/// <summary>
/// 곡 선택 창. 처음에는 후보(화면에서 확실히 읽은 패턴과 맞는 곡)를 보이고, '전체 곡에서 찾기…'를 누르면 같은 창이 검색으로 바뀐다(곡명·아티스트로 거른다.
/// 패턴이 맞지 않는 곡도 고를 수 있다: 고른 뒤 검수 표에 패턴 경고가 뜬다). 곡이 골라져 있으면 '곡 선택 해제'가 보인다
/// </summary>
sealed class SongPicker:CompactDialog
{
 public const int ContentWidth=580,MaxRows=6;
 public Song? Picked{get;private set;}
 public bool Cleared{get;private set;}
 readonly Catalog catalog;readonly SongList list=new();readonly InputBox search;readonly Label empty;readonly Control actions;
 /// <summary>개발용 검사: 지금 검색 화면인가</summary>
 internal bool Searching{get;private set;}
 internal SongList List=>list;internal HintBox SearchBox=>search.Box;
 public SongPicker(Catalog catalog,IReadOnlyList<Song> candidates,Song? chosen):base("곡 선택")
 {
  this.catalog=catalog;StartPosition=FormStartPosition.CenterParent;
  Body.Controls.Add(new SectionHeader("곡 선택"){Keyed=true,Width=ContentWidth,Margin=Padding.Empty});
  search=new InputBox(ContentWidth,"",60){Margin=new(0,12,0,0)};search.Box.Hint="곡명 또는 아티스트 검색";search.Box.AccessibleName="곡명 또는 아티스트 검색";search.Visible=false;Body.Controls.Add(search);
  list.Size=new(ContentWidth,SongList.RowHeight*Math.Clamp(candidates.Count,1,MaxRows)+2);list.Margin=new(0,10,0,0);Body.Controls.Add(list);
  empty=new Label(){Text="검색 결과가 없습니다.",AutoSize=true,Tag="dim",UseMnemonic=false,Margin=new(0,10,0,0)};Body.Controls.Add(empty);
  var row=new FlowLayoutPanel(){AutoSize=true,WrapContents=false,Margin=new(0,12,0,0)};
  var clear=new Button(){Text="곡 선택 해제",AutoSize=true,Margin=new(0,0,8,0)};clear.Click+=(_,_)=>{Cleared=true;DialogResult=DialogResult.OK;};clear.Visible=chosen!=null;
  var find=new Button(){Text="전체 곡에서 찾기…",AutoSize=true,Margin=Padding.Empty};find.Click+=(_,_)=>Search();
  row.Controls.Add(clear);row.Controls.Add(find);actions=row;Body.Controls.Add(row);
  var close=new Button(){Text="닫기",DialogResult=DialogResult.Cancel};CancelButton=close;
  var footer=Parts.Footer(ContentWidth,close);footer.Margin=new(0,14,0,0);Body.Controls.Add(footer);
  list.Picked+=song=>{Picked=song;DialogResult=DialogResult.OK;};
  search.Box.TextChanged+=(_,_)=>Filter();
  list.Set(candidates);empty.Visible=candidates.Count==0;
 }
 /// <summary>검색으로 바꾼다(빈 검색어면 곡 목록 전체)</summary>
 internal void Search()
 {
  Searching=true;actions.Visible=false;search.Visible=true;list.Height=Theme.S(SongList.RowHeight)*MaxRows+2;Filter();search.Box.Focus();
  if(IsHandleCreated)PerformLayout();
 }
 void Filter()
 {
  string q=search.Box.Text.Trim().ToLowerInvariant();
  var found=catalog.Songs.Where(s=>q.Length==0||($"{s.Name} {s.Artist}").ToLowerInvariant().Contains(q)).ToList();
  list.Set(found);empty.Visible=found.Count==0;
 }
}

/// <summary>
/// 캡처 보기. 게임 화면 전체를 원래 비율로 맞춰 보이고, 그림을 누르면 누른 자리를 가운데로 원본 크기(그림의 1화소 = 화면의 1화소)로 키운다. 다시 누르거나 Esc 로 전체로 돌아온다
/// (전체일 때 Esc 는 창을 닫는다). 이전·다음으로 장면을 넘기고, 장을 바꾸면 전체 보기로 돌아온다
/// </summary>
sealed class CaptureViewer:CompactDialog
{
 public sealed record Shot(byte[] Png,string Scene,string? Note);
 /// <summary>
 /// 그 묶음의 캡처: 점수를 읽은 화면, 그 곡을 다시 잡은 화면들(마지막 화면과 다른 것만 3장까지), 이름이 같은 곡이면 아티스트를 읽은 화면.
 /// 마지막 것의 점수표는 다른 곡이다
 /// </summary>
 public static List<Shot> For(ReviewGroup group)
 {
  var shots=new List<Shot>();var item=group.Item;
  if(item.Screen!=null)shots.Add(new(item.Screen,"점수표",null));
  for(int i=0;i<item.TriedScreens.Count;i++)shots.Add(new(item.TriedScreens[i],$"점수표(다시 잡은 {i+1}번째)",null));
  if(item.ArtistScreen!=null)shots.Add(new(item.ArtistScreen,(item.ArtistAfterUp?"앞 곡으로 이동 후":"다음 곡으로 이동 후")+" · 오른쪽 곡 목록","이 장면의 점수표는 다른 곡입니다."));
  return shots;
 }
 readonly IReadOnlyList<Shot> shots;readonly string pattern;int at;
 readonly Label scene,hint,page,read;readonly WordLabel note;readonly Button prev,next;readonly CaptureView view;
 const string FitHint="사진을 누르면 해당 위치를 확대합니다.",ZoomHint="스크롤로 이동할 수 있습니다. 다시 누르면 전체 화면이 보입니다.";
 internal bool Zoomed=>view.Zoomed;internal int At=>at;internal string SceneText=>scene.Text;internal string NoteText=>noteShown??"";string? noteShown;   // 창이 뜨기 전에는 Visible 이 늘 거짓이라 따로 둔다
 public CaptureViewer(string pattern,string readScore,IReadOnlyList<Shot> shots):base("캡처 보기")
 {
  this.shots=shots;this.pattern=pattern;StartPosition=FormStartPosition.CenterParent;
  // 그림 칸의 크기(100% 때): 1080p 작업 영역이면 약 1286×723. 화면이 작으면 작업 영역에 맞춘다
  var work=Screen.FromPoint(Cursor.Position).WorkingArea;float k=Theme.Scale;
  int width=(int)Math.Min(1286,Math.Min(work.Width*.94f/k-40,(work.Height*.94f/k-230)*16/9));width=Math.Max(320,width);int height=width*9/16;
  Body.Controls.Add(new SectionHeader("캡처 보기"){Keyed=true,Width=width,Margin=Padding.Empty});
  scene=new Label(){AutoSize=true,UseMnemonic=false,Font=Theme.Px(AppFonts.Ui,10.5f,FontStyle.Bold),Margin=new(0,10,0,0)};Body.Controls.Add(scene);
  note=new WordLabel("",width,9.75f,"accent"){Margin=new(0,4,0,0)};note.Visible=false;Body.Controls.Add(note);
  view=new CaptureView(){Size=new(width,height),Margin=new(0,8,0,0)};Body.Controls.Add(view);
  hint=new Label(){Text=FitHint,AutoSize=true,Tag="dim",UseMnemonic=false,Margin=new(0,8,0,0)};Body.Controls.Add(hint);
  read=new Label(){Text=$"프로그램이 읽은 점수 {readScore}",AutoSize=true,UseMnemonic=false,Margin=new(0,4,0,0)};Body.Controls.Add(read);
  var bottom=new TableLayoutPanel(){ColumnCount=2,RowCount=1,AutoSize=false,Size=new(width,36),Margin=new(0,12,0,0)};
  bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
  var left=new FlowLayoutPanel(){AutoSize=true,WrapContents=false,Margin=Padding.Empty};
  prev=new Button(){Text="이전",Margin=new(0,0,8,0)};next=new Button(){Text="다음",Margin=new(8,0,0,0)};
  page=new Label(){AutoSize=true,UseMnemonic=false,Margin=new(0,7,0,0)};
  left.Controls.Add(prev);left.Controls.Add(page);left.Controls.Add(next);
  var close=new Button(){Text="닫기",DialogResult=DialogResult.Cancel,Anchor=AnchorStyles.Right};CancelButton=close;   // 확대가 아닐 때의 Esc 는 닫기(확대일 때는 ProcessCmdKey 가 먼저 받는다)
  bottom.Controls.Add(left,0,0);bottom.Controls.Add(close,1,0);Body.Controls.Add(bottom);
  prev.Click+=(_,_)=>Go(at-1);next.Click+=(_,_)=>Go(at+1);
  view.Toggled+=()=>hint.Text=view.Zoomed?ZoomHint:FitHint;
  Go(0);
 }
 internal void Go(int index)
 {
  if(index<0||index>=shots.Count)return;at=index;var shot=shots[at];
  view.Set(shot.Png);hint.Text=FitHint;
  scene.Text=$"{pattern} · {shot.Scene}";note.Text=shot.Note??"";note.Visible=shot.Note!=null;noteShown=shot.Note;
  page.Text=$"{at+1} / {shots.Count}";prev.Enabled=at>0;next.Enabled=at<shots.Count-1;
  if(IsHandleCreated)PerformLayout();
 }
 /// <summary>개발용 검사: 그림의 그 비율 자리를 누른 것과 같다</summary>
 internal void ClickAt(float fx,float fy)=>view.Toggle(fx,fy);
 /// <summary>개발용 검사: Esc 를 누른 것과 같다(확대를 풀었으면 참. 아니면 창의 취소 단추가 받는다)</summary>
 internal bool PressEscape(){var message=new Message();return ProcessCmdKey(ref message,Keys.Escape);}
 /// <summary>개발용 검사: 그림 칸이 Tab 으로 초점을 받고, Enter 를 누른 것처럼 하면 확대가 바뀐다</summary>
 internal bool PictureTakesFocus=>view.TakesFocus;
 internal void PressPicture(Keys key)=>view.Press(key);
 protected override bool ProcessCmdKey(ref Message msg,Keys keyData)
 {
  if(keyData==Keys.Escape&&view.Zoomed){view.Toggle(.5f,.5f);hint.Text=FitHint;return true;}
  return base.ProcessCmdKey(ref msg,keyData);
 }
 protected override void Dispose(bool disposing){if(disposing)view.Release();base.Dispose(disposing);}
}

/// <summary>캡처 그림 칸. 전체 보기에서는 그림을 칸에 맞춰 그리고, 확대하면 원본 크기의 그림을 칸 안에서 굴린다</summary>
sealed class CaptureView:Panel
{
 Bitmap? image;PictureBox? full;
 public bool Zoomed=>full!=null;
 public event Action? Toggled;
 // 패널은 기본으로 초점을 받지 않는다: Tab 으로 그림 칸에 와서 Enter·Space 로 확대하게 초점을 받는 것으로 둔다
 public CaptureView(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.Selectable,true);Cursor=Cursors.Hand;TabStop=true;AccessibleName="캡처 확대";AccessibleRole=AccessibleRole.PushButton;}
 /// <summary>개발용 검사: Tab 으로 초점을 받는가</summary>
 internal bool TakesFocus=>TabStop&&GetStyle(ControlStyles.Selectable);
 protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);Invalidate();}
 protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);Invalidate();}
 public void Set(byte[] png){Unzoom();image?.Dispose();using(var stream=new MemoryStream(png))image=new Bitmap(stream);Invalidate();}
 public void Release(){Unzoom();image?.Dispose();image=null;}
 Rectangle Fit(){if(image==null)return ClientRectangle;float k=Math.Min(ClientSize.Width/(float)image.Width,ClientSize.Height/(float)image.Height);int w=(int)(image.Width*k),h=(int)(image.Height*k);return new((ClientSize.Width-w)/2,(ClientSize.Height-h)/2,w,h);}
 protected override void OnPaint(PaintEventArgs e)
 {
  var g=e.Graphics;g.Clear(Theme.Table);
  if(image!=null&&full==null){g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.DrawImage(image,Fit());}
  if(Focused&&ShowFocusCues){using var pen=new Pen(Theme.Accent,2);g.DrawRectangle(pen,1,1,Width-3,Height-3);}
 }
 protected override void OnMouseClick(MouseEventArgs e){base.OnMouseClick(e);if(e.Button!=MouseButtons.Left||image==null)return;Focus();var r=Fit();Toggle((e.X-r.Left)/(float)Math.Max(1,r.Width),(e.Y-r.Top)/(float)Math.Max(1,r.Height));}
 protected override void OnKeyDown(KeyEventArgs e){base.OnKeyDown(e);if(e.KeyCode is Keys.Enter or Keys.Space){Toggle(.5f,.5f);e.Handled=true;}}
 internal void Press(Keys key)=>OnKeyDown(new KeyEventArgs(key));
 /// <summary>전체 ↔ 확대. 확대할 때는 그 비율 자리(fx, fy)를 칸의 가운데로 옮긴다(가장자리면 보이는 데까지만)</summary>
 public void Toggle(float fx,float fy)
 {
  if(image==null)return;
  if(full!=null){Unzoom();Toggled?.Invoke();return;}
  full=new PictureBox(){Image=image,SizeMode=PictureBoxSizeMode.Normal,Size=image.Size,Location=Point.Empty,Cursor=Cursors.Hand};
  full.MouseClick+=(_,e)=>{if(e.Button==MouseButtons.Left){Unzoom();Toggled?.Invoke();}};
  AutoScroll=true;Controls.Add(full);AccessibleName="캡처 전체 보기";
  if(FindForm() is Form form)Theme.Apply(form);   // 스크롤 막대를 테마에 맞춘다
  int x=(int)(Math.Clamp(fx,0,1)*image.Width-ClientSize.Width/2f),y=(int)(Math.Clamp(fy,0,1)*image.Height-ClientSize.Height/2f);
  AutoScrollPosition=new(Math.Max(0,x),Math.Max(0,y));
  Invalidate();Toggled?.Invoke();
 }
 void Unzoom(){if(full==null)return;Controls.Remove(full);full.Image=null;full.Dispose();full=null;AutoScroll=false;AccessibleName="캡처 확대";Invalidate();}
}
