using System.Drawing.Drawing2D;
namespace LadderRecords;
// 컬렉션 가져오기의 전곡 읽기 완료 화면에 쓰는 줄과 부품.
// 셈은 CollectionPlan 이 한다. 여기는 그 셈을 화면의 글로 옮기고(CollectionDoneView, 창 없이 검사한다) 그린다(DoneTable, DoneHeader).

/// <summary>완료 화면 목록의 한 줄. Pick 이 있으면 체크하는 줄이다(컬렉션보다 높은 노말 기록). Was 가 있으면 재확인에서 바뀐 줄이다(검수 당시의 값).
/// Gone 이면 검수 당시에는 후보였는데 지금은 삭제 대상에서 빠진 판이다(재확인에서 한 번 보인다. 체크가 없고 건수에 들지 않는다)</summary>
sealed record DoneRow(string Title,string Pattern,string Note,string Main,string Sub,Play? Pick=null,string? Was=null,bool Gone=false);
/// <summary>삭제 항목 넷(화면에 놓이는 차례)</summary>
enum DoneKind{VArchive,OldRate,Higher,Auto}
sealed record DoneGroup(DoneKind Kind,string Name,IReadOnlyList<DoneRow> Rows);

static class CollectionDoneView
{
 public static readonly DoneKind[] Order={DoneKind.VArchive,DoneKind.OldRate,DoneKind.Higher,DoneKind.Auto};
 public static string Name(DoneKind kind)=>kind switch{DoneKind.VArchive=>"V아카이브",DoneKind.OldRate=>"이전 버전 노말 RATE 기록",DoneKind.Higher=>"컬렉션보다 높은 노말 기록",_=>"예전 컬렉션 기록"};
 /// <summary>버튼·패턴('8B · SC'). 키는 곡|버튼|패턴|판정</summary>
 public static string Pattern(string key){var p=key.Split('|');return p.Length>=3?$"{p[1]}B · {p[2]}":key;}
 static string Title(Catalog catalog,IReadOnlyDictionary<string,Song> songs,string key){string id=key.Split('|')[0];return songs.TryGetValue(id,out var song)?OverlayText.Title(catalog,song):id;}
 /// <summary>그 줄의 값: 세부 SCORE 를 아는 출처는 점수, RATE 를 환산한 출처(V아카이브·옛 RATE)는 RATE(기록 창들과 같은 규칙)</summary>
 public static string Value(Play row)=>PlaySource.Exact(row.Source)?ScoreText.Points(row.Score):ScoreText.Rate(row.Score);
 public static string Compare(int? collection)=>collection is int c?$"컬렉션 {c:N0}점":"컬렉션에 기록 없음";
 /// <summary>컬렉션보다 높은 노말 기록의 출처. 노말에 반영된 하드·맥스 판은 반영한 판정과 그 판정의 원본이 남는다는 것을 적는다</summary>
 public static string Source(Play row,string normalKey)
 {
  if(row.Key!=normalKey&&row.Normal==NormalLink.Linked){string judge=row.Key[(row.Key.LastIndexOf('|')+1)..];return $"{judge}판정에서 반영 · {judge}판정 원본 유지";}
  return row.Source switch{PlaySource.Play=>"직접 저장",PlaySource.Manual=>"수동 등록",_=>PlaySource.Label(row.Source)};
 }
 public static string Reason(CollectionDrop why,int? collection)=>why switch
 {
  CollectionDrop.NoRecord=>"이 패턴의 HIGH SCORE가 ‘-’로 표시되었습니다.",
  CollectionDrop.NotInList=>"전곡을 읽은 목록에 이 곡이 없습니다.",
  _=>$"날짜가 있는 {collection:N0}점 기록을 유지합니다.",
 };
 static Dictionary<string,Song> Songs(Catalog catalog)=>catalog.Songs.ToDictionary(s=>s.Id,StringComparer.Ordinal);
 static int? Read(CollectionPlan plan,string key)=>plan.Read.TryGetValue(key,out var v)?v:null;

 /// <summary>'추가·갱신할 점수 보기'의 줄들: 더할 것 다음 고칠 것</summary>
 public static IReadOnlyList<DoneRow> Adds(CollectionPlan plan,Catalog catalog)
 {
  var songs=Songs(catalog);var rows=new List<DoneRow>();
  foreach(var add in plan.Adds)rows.Add(new(Title(catalog,songs,add.Key),Pattern(add.Key),"추가",ScoreText.Points(add.Score),""));
  foreach(var (before,after) in plan.Updates)rows.Add(new(Title(catalog,songs,after!.Key),Pattern(after.Key),"갱신",ScoreText.Points(after.Score),$"지금 컬렉션 {before!.Score:N0}점"));
  return rows;
 }

 /// <summary>
 /// 삭제 항목 넷과 그 줄들(대상이 0건인 항목은 뺀다). before 가 있으면 재확인이다: 지금 셈의 줄 가운데 검수 당시 셈에 같은 판이 없는 줄에 Was 를 단다
 /// (같은 판의 점수·반영이 바뀌었으면 그 값, 아니면 '검수 당시 없음'). 같은 판은 순번·판정 키·시각·출처·연습이 모두 같은 판이다: 되돌린 뒤 같은 순번을 받은 다른 판에
 /// 옛 값을 붙이지 않는다. 검수 당시 후보였는데 지금 빠진 판은 그 항목에 Gone 줄로 한 번 보인다(current 는 지금 프로필의 판들)
 /// </summary>
 public static IReadOnlyList<DoneGroup> Groups(CollectionPlan plan,CollectionChoices choices,Catalog catalog,CollectionPlan? before=null,IEnumerable<Play>? current=null)
 {
  var songs=Songs(catalog);var groups=new List<DoneGroup>();
  var old=before==null?null:Rows(before,before.Visible(choices).Select(h=>h.Row));
  string? Was(Play row)
  {
   if(old==null||old.Contains(row))return null;
   var same=old.FirstOrDefault(p=>Same(p,row));return same==null?"검수 당시 없음":$"검수 당시 {Value(same)}";
  }
  var varchive=plan.VArchive.Select(r=>new DoneRow(Title(catalog,songs,r.Key),Pattern(r.Key),"",Value(r),Compare(Read(plan,r.Key)),null,Was(r))).ToList();
  var oldRate=plan.OldRate.Select(r=>new DoneRow(Title(catalog,songs,r.Key),Pattern(r.Key),"",Value(r),Compare(Read(plan,r.Key)),null,Was(r))).ToList();
  var higher=plan.Visible(choices).Select(h=>new DoneRow(Title(catalog,songs,h.NormalKey),Pattern(h.NormalKey),Source(h.Row,h.NormalKey),Value(h.Row),Compare(h.Collection),h.Row,Was(h.Row))).ToList();
  if(before!=null)
  {
   var now=plan.Visible(choices).Select(h=>h.Row).ToList();var plays=current?.ToList()??new List<Play>();
   foreach(var h in before.Visible(choices).Where(h=>!now.Any(r=>Same(r,h.Row))))
   {
    var still=plays.FirstOrDefault(p=>Same(p,h.Row));
    higher.Add(new DoneRow(Title(catalog,songs,h.NormalKey),Pattern(h.NormalKey),Origin(h.Row,h.NormalKey)+" · 삭제 대상에서 빠짐",still==null?"기록 없음":Value(still),Compare(Read(plan,h.NormalKey)),null,$"검수 당시 {Value(h.Row)}",true));
   }
  }
  var auto=plan.Drops.Select(d=>{int? read=Read(plan,d.Row.Key);return new DoneRow(Title(catalog,songs,d.Row.Key),Pattern(d.Row.Key),Reason(d.Why,read),ScoreText.Points(d.Row.Score),Compare(d.Why==CollectionDrop.SameDated?read:null),null,Was(d.Row));}).ToList();
  foreach(var (kind,rows) in new[]{(DoneKind.VArchive,varchive),(DoneKind.OldRate,oldRate),(DoneKind.Higher,higher),(DoneKind.Auto,auto)})
   if(rows.Count>0)groups.Add(new(kind,Name(kind),rows));
  return groups;
 }
 /// <summary>후보에서 빠진 판의 출처: 반영된 하드·맥스 판은 '{판정}판정에서 반영'까지만(원본이 지워졌을 수 있어 '원본 유지'를 붙이지 않는다)</summary>
 static string Origin(Play row,string normalKey)=>row.Key!=normalKey&&row.Normal==NormalLink.Linked?$"{row.Key[(row.Key.LastIndexOf('|')+1)..]}판정에서 반영":Source(row,normalKey);
 /// <summary>
 /// 검수 당시와 지금 사이에 바뀐 판의 수(재확인 띠의 'N건'): 두 셈에 나오는 판(일괄 삭제 둘, 보이는 후보, 저절로 없어지는 줄)을 견줘
 /// 새로 생겼거나 값이 바뀌었거나 사라진 판을 같은 판(Same)마다 한 번 센다. 한 판이 두 항목에 보여도(일괄 삭제를 푼 V아카이브 판) 한 번이다
 /// </summary>
 public static int ChangedPlays(CollectionPlan before,CollectionPlan now,CollectionChoices choices)
 {
  var old=Rows(before,before.Visible(choices).Select(h=>h.Row));var cur=Rows(now,now.Visible(choices).Select(h=>h.Row));
  var changed=new HashSet<(int,string,DateTime?,string,bool)>();
  foreach(var p in cur)if(!old.Contains(p))changed.Add((p.Seq,p.Key,p.At,p.Source,p.Practice));
  foreach(var p in old)if(!cur.Contains(p))changed.Add((p.Seq,p.Key,p.At,p.Source,p.Practice));
  return changed.Count;
 }
 /// <summary>같은 판인가(점수와 노말 반영만 다를 수 있다)</summary>
 static bool Same(Play a,Play b)=>a.Seq==b.Seq&&a.Key==b.Key&&a.At==b.At&&a.Source==b.Source&&a.Practice==b.Practice;
 /// <summary>한 셈에 나오는 판 모두(일괄 삭제 둘, 보이는 후보, 저절로 없어지는 줄)</summary>
 static HashSet<Play> Rows(CollectionPlan plan,IEnumerable<Play> visible)=>new(plan.VArchive.Concat(plan.OldRate).Concat(visible).Concat(plan.Drops.Select(d=>d.Row)));
 /// <summary>재확인 뒤에도 고른 채로 둘 것: 지금 셈에 같은 판으로 남은 고른 줄만(바뀐 줄의 체크만 풀린다. 같은 패턴의 다른 줄은 그대로다)</summary>
 public static void Keep(CollectionChoices choices,CollectionPlan now){var still=new HashSet<Play>(now.Higher.Select(h=>h.Row));choices.Higher.RemoveWhere(p=>!still.Contains(p));}
 /// <summary>적용한 뒤 메인 창의 상태 줄(V아카이브 가져오기의 글과 같은 꼴, 0건도 적는다)</summary>
 public static string AppliedText(CollectionSaveResult result)=>$"컬렉션 기록을 가져왔습니다(추가 {result.Added:N0}건, 갱신 {result.Updated:N0}건, 삭제 {result.Deleted:N0}건).";
 /// <summary>바뀔 것이 하나도 없다(더함·고침·없어짐·일괄 삭제가 없고 고를 수 있는 후보도 없다): '변경할 기록이 없습니다.'</summary>
 public static bool Nothing(CollectionPlan plan,CollectionChoices choices)=>plan.Changes(choices).Count==0&&!plan.Visible(choices).Any();
 /// <summary>전체 선택 상자의 상태: 모두·일부·없음</summary>
 public static CheckState Picked(CollectionPlan plan,CollectionChoices choices)
 {
  var visible=plan.Visible(choices).Select(h=>h.Row).ToList();int on=visible.Count(choices.Higher.Contains);
  return on==0?CheckState.Unchecked:on==visible.Count?CheckState.Checked:CheckState.Indeterminate;
 }
}

/// <summary>
/// 완료 화면의 목록(한 열, 머리글 없음). 줄마다 왼쪽에 곡·버튼·패턴과 그 아래의 출처나 까닭, 오른쪽에 값과 그 아래의 컬렉션 값.
/// 체크하는 줄은 맨 왼쪽에 체크 상자를 그리고, 누르거나 Space 로 바꾼다. 재확인에서 바뀐 줄은 오른쪽에 검수 당시 값·현재 값(강조색)·컬렉션 값 세 줄을 쓴다
/// </summary>
sealed class DoneTable:OwnerTable
{
 IReadOnlyList<DoneRow> rows=Array.Empty<DoneRow>();ListViewItem[] items=Array.Empty<ListViewItem>();readonly ImageList spacing=new();int rowHeight;
 public const int RowHeight=52,TallRow=66,CheckLeft=14,CheckSize=16;
 /// <summary>그 판을 골랐는가(체크하는 줄)</summary>
 public Func<Play,bool> IsPicked=_=>false;
 public event Action<Play>? Toggled;
 public IReadOnlyList<DoneRow> Rows=>rows;
 public DoneTable(string name)
 {
  HeaderStyle=ColumnHeaderStyle.None;TabStop=true;AccessibleName=name;Font=Theme.Px(AppFonts.Ui,13f);
  Columns.Add(new ColumnHeader(){Width=400});SetRowHeight(RowHeight);
  // 화면 읽기 프로그램이 읽는 줄의 글: 곡·패턴·출처·값과 함께 삭제 선택 상태와 재확인의 검수 당시 → 현재
  // 줄마다 같은 항목을 돌려준다: 화면 읽기 프로그램이 이미 잡고 있는 줄도 글이 바뀌면 새 글을 읽는다
  RetrieveVirtualItem+=(_,e)=>{e.Item=e.ItemIndex<items.Length?items[e.ItemIndex]:new ListViewItem(""){ImageIndex=0};};
  DrawSubItem+=(_,e)=>{if(e.ItemIndex<rows.Count)PaintRow(e.Graphics,e.Bounds,rows[e.ItemIndex],Focused&&e.Item!=null&&e.Item.Focused);};
  SelectedIndexChanged+=(_,_)=>{if(SelectedIndices.Count>0)SelectedIndices.Clear();};   // 고른 줄을 두지 않는다(체크로 고른다)
  MouseDown+=(_,e)=>{if(e.Button!=MouseButtons.Left)return;var hit=HitTest(e.Location);if(hit.Item!=null&&hit.Item.Index<rows.Count&&rows[hit.Item.Index].Pick is {} pick&&e.X<Theme.S(CheckLeft+CheckSize+10))Toggled?.Invoke(pick);};
  KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Space&&FocusedItem is {} item&&item.Index<rows.Count&&rows[item.Index].Pick is {} pick){Toggled?.Invoke(pick);e.Handled=true;}};
  GotFocus+=(_,_)=>Invalidate();LostFocus+=(_,_)=>Invalidate();
  ClientSizeChanged+=(_,_)=>{if(Columns.Count>0&&ClientSize.Width>0&&Columns[0].Width!=ClientSize.Width)Columns[0].Width=ClientSize.Width;};
 }
 void SetRowHeight(int height){if(rowHeight==height)return;rowHeight=height;spacing.ImageSize=new(1,Theme.S(height));spacing.Images.Clear();spacing.Images.Add(new Bitmap(1,1));SmallImageList=null;SmallImageList=spacing;}
 /// <summary>개발용 검사: 그 줄을 누른 것과 같다</summary>
 internal void ToggleAt(int index){if(index<rows.Count&&rows[index].Pick is {} pick)Toggled?.Invoke(pick);}
 public void Set(IReadOnlyList<DoneRow> list)
 {
  rows=list;SetRowHeight(list.Any(r=>r.Was!=null)?TallRow:RowHeight);
  if(items.Length!=list.Count){items=list.Select(r=>new ListViewItem(Spoken(r)){ImageIndex=0}).ToArray();VirtualListSize=list.Count;}
  else for(int i=0;i<list.Count;i++)
  {
   string text=Spoken(list[i]);if(items[i].Text==text)continue;
   items[i].Text=text;if(IsHandleCreated){AccessibilityNotifyClients(AccessibleEvents.NameChange,i);AccessibilityNotifyClients(AccessibleEvents.StateChange,i);}
  }
  Invalidate();
 }
 /// <summary>개발용 검사: 그 줄의 항목(화면 읽기 프로그램이 받는 것)</summary>
 internal ListViewItem ItemAt(int index)=>items[index];
 /// <summary>줄 count 개가 굴리지 않고 보이는 높이(100% 때의 값)</summary>
 public int HeightFor(int count)=>count*rowHeight;
 public int RowTall=>rowHeight;
 /// <summary>줄을 글로(화면 읽기 프로그램과 개발용 검사)</summary>
 internal string Spoken(DoneRow row)
 {
  string text=$"{row.Title} {row.Pattern} {row.Note} {(row.Was!=null?row.Was+" 현재 ":"")}{row.Main} {row.Sub}".Trim();
  return row.Pick is {} pick?text+(IsPicked(pick)?", 삭제 선택됨":", 삭제 선택 안 됨"):text;
 }
 void PaintRow(Graphics g,Rectangle cell,DoneRow row,bool focus)
 {
  using(var back=new SolidBrush(Theme.Table))g.FillRectangle(back,cell);
  using(var line=new Pen(Theme.RowLine))g.DrawLine(line,cell.Left,cell.Bottom-1,cell.Right,cell.Bottom-1);
  // 키보드 초점이 있는 줄: 강조색 윤곽선
  if(focus){using var ring=new Pen(Theme.Accent,Math.Max(1,Theme.S(2)));int w=(int)ring.Width;g.DrawRectangle(ring,cell.Left+w/2,cell.Top+w/2,cell.Width-w-1,cell.Height-w-1);}
  float left=cell.Left+Theme.S(14);
  if(row.Pick is {} pick)
  {
   int size=Theme.S(CheckSize);var box=new Rectangle(cell.Left+Theme.S(CheckLeft),cell.Top+(cell.Height-size)/2,size,size);
   DoneHeader.PaintBox(g,box,IsPicked(pick)?CheckState.Checked:CheckState.Unchecked);
   left=box.Right+Theme.S(12);
  }
  // 오른쪽: 값(굵게)과 컬렉션 값. 바뀐 줄은 검수 당시 값 · 현재 값(강조색) · 컬렉션 값
  float right=cell.Right-Theme.S(14);var number=Ink.Number(13f);var small=Ink.Body(12f);float rightWidth;
  if(row.Was!=null)
  {
   var was=new[]{new InkPart(row.Was,small,Theme.Dim)};var now=new[]{new InkPart("현재 "+row.Main,number,row.Gone?Theme.Text:Theme.Accent)};var sub=new[]{new InkPart(row.Sub,small,Theme.Dim)};
   Ink.DrawRight(g,was,right,Ink.Baseline(cell.Top+cell.Height*.24f,small));Ink.DrawRight(g,now,right,Ink.Baseline(cell.Top+cell.Height*.52f,number));Ink.DrawRight(g,sub,right,Ink.Baseline(cell.Top+cell.Height*.79f,small));
   rightWidth=Math.Max(Ink.Width(was),Math.Max(Ink.Width(now),Ink.Width(sub)));
  }
  else
  {
   var main=new[]{new InkPart(row.Main,number,Theme.Text)};var sub=new[]{new InkPart(row.Sub,small,Theme.Dim)};
   Ink.DrawRight(g,main,right,Ink.Baseline(cell.Top+cell.Height*.36f,number));if(row.Sub.Length>0)Ink.DrawRight(g,sub,right,Ink.Baseline(cell.Top+cell.Height*.72f,small));
   rightWidth=Math.Max(Ink.Width(main),Ink.Width(sub));
  }
  // 왼쪽: 곡명(넘치면 끝을 줄인다) 뒤에 버튼·패턴, 그 아래 출처나 까닭
  float room=right-rightWidth-Theme.S(16)-left;var title=Ink.Title(14f);var pattern=new[]{new InkPart(" "+row.Pattern,Ink.Number(12f,false),Theme.Dim)};
  float patternWidth=Ink.Width(pattern),top=row.Was!=null?.36f:.36f;
  float titleBase=Ink.Baseline(cell.Top+cell.Height*(row.Note.Length>0?top:.5f),title);
  Ink.DrawClipped(g,row.Title,title,Theme.Text,left,titleBase,Math.Max(Theme.S(40),room-patternWidth));
  float drawn=Math.Min(Ink.Width(new[]{new InkPart(row.Title,title,Theme.Text)}),Math.Max(Theme.S(40),room-patternWidth));
  Ink.Draw(g,pattern,left+drawn,titleBase);
  if(row.Note.Length>0)Ink.DrawClipped(g,row.Note,small,Theme.Dim,left,Ink.Baseline(cell.Top+cell.Height*.72f,small),room);
 }
 protected override void Dispose(bool disposing){if(disposing)spacing.Dispose();base.Dispose(disposing);}
}

/// <summary>
/// 삭제 항목 하나의 머리 줄('> V아카이브 4건 ··· 삭제 [v]'). 누르면 그 아래 목록을 펴고 접는다(Enter 도 같다). 오른쪽의 선택은 따로 누른다(Space 도 같다).
/// 오른쪽은 체크 상자(일괄 삭제 '삭제', 전체 선택 '전체 선택'. 일부만 고르면 가운데 줄) 또는 글('자동 삭제')이다. 펼치면 테두리가 강조색이다
/// </summary>
sealed class DoneHeader:Control
{
 bool open,hot;CheckState check;int count;
 public readonly DoneKind Kind;
 /// <summary>오른쪽 체크 상자의 글. 없으면 글만(Label) 보인다</summary>
 public readonly string? CheckText;public readonly string? Label;
 public event Action? FoldToggled,CheckToggled;
 public bool Open=>open;public CheckState Check=>check;public int Count=>count;
 public DoneHeader(DoneKind kind,string? checkText,string? label)
 {
  SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.Selectable,true);
  Kind=kind;CheckText=checkText;Label=label;Text=CollectionDoneView.Name(kind);Cursor=Cursors.Hand;TabStop=true;Margin=Padding.Empty;AccessibleRole=AccessibleRole.PushButton;Describe();
 }
 public void Set(int count,CheckState check){this.count=count;this.check=check;Describe();Invalidate();}
 public void SetOpen(bool value){if(open==value)return;open=value;Describe();Invalidate();}
 void Describe(){AccessibleName=$"{Text} {count}건";AccessibleDescription=(open?"펼침":"접힘")+(CheckText!=null?$", {CheckText} {(check==CheckState.Checked?"선택됨":check==CheckState.Indeterminate?"일부 선택됨":"선택 안 됨")}":Label!=null?", "+Label:"");}
 /// <summary>오른쪽 선택의 자리(누르면 체크가 바뀐다)</summary>
 Rectangle CheckArea(){if(CheckText==null)return Rectangle.Empty;int width=(int)(Ink.Width(new[]{new InkPart(CheckText,Ink.Body(12f,true),Theme.Text)})+Theme.S(16+10+14));return new(Width-width,0,width,Height);}
 internal void Flip(){SetOpen(!open);FoldToggled?.Invoke();}
 internal void Tick(){if(CheckText!=null)CheckToggled?.Invoke();}
 protected override void OnPaint(PaintEventArgs e)
 {
  var g=e.Graphics;g.Clear(Parent?.BackColor??Theme.Window);g.SmoothingMode=SmoothingMode.AntiAlias;
  using(var shape=BadgeDesign.Rounded(new RectangleF(0,0,Width-1,Height-1),Theme.S(3f))){using var fill=new SolidBrush(Theme.Field);g.FillPath(fill,shape);using var pen=new Pen(open||Focused&&ShowFocusCues?Theme.Accent:Theme.Line);g.DrawPath(pen,shape);}
  float middle=Height/2f,x=Theme.S(12f),size=Theme.S(4.5f);
  using(var ink=new SolidBrush(hot?Theme.Accent:Theme.Text))g.FillPolygon(ink,open?new[]{new PointF(x,middle-size*.7f),new PointF(x+size*2,middle-size*.7f),new PointF(x+size,middle+size*.9f)}:new[]{new PointF(x+size*.2f,middle-size),new PointF(x+size*.2f,middle+size),new PointF(x+size*1.9f,middle)});
  g.SmoothingMode=SmoothingMode.Default;
  var name=Ink.Body(13f,true);
  Ink.Draw(g,new[]{new InkPart(Text,name,hot?Theme.Accent:Theme.Text),new InkPart($"  {count}건",Ink.Body(13f),hot?Theme.Accent:Theme.Text)},x+size*2+Theme.S(8),Ink.Baseline(middle,name));
  float right=Width-Theme.S(14);var label=Ink.Body(12f,true);
  if(CheckText!=null)
  {
   int box=Theme.S(16);var r=new Rectangle((int)right-box,(int)(middle-box/2f),box,box);PaintBox(g,r,check);
   Ink.DrawRight(g,new[]{new InkPart(CheckText,label,Theme.Text)},r.Left-Theme.S(8),Ink.Baseline(middle,label));
  }
  else if(Label!=null)Ink.DrawRight(g,new[]{new InkPart(Label,Ink.Body(12f),Theme.Dim)},right,Ink.Baseline(middle,label));
 }
 /// <summary>체크 상자 하나(바탕·테두리·체크 표시. 일부만 골랐으면 가운데 줄). 앱의 체크 상자(Theme.PaintCheck)와 같은 색</summary>
 internal static void PaintBox(Graphics g,Rectangle r,CheckState state)
 {
  bool on=state!=CheckState.Unchecked;var edge=on?Theme.Fill:Theme.Mix(Theme.Dim,Theme.Window,.4);float k=r.Width/13f;
  using(var fill=new SolidBrush(on?edge:Theme.Field))g.FillRectangle(fill,r);
  using(var pen=new Pen(edge))g.DrawRectangle(pen,r.X,r.Y,r.Width-1,r.Height-1);
  if(!on)return;
  var smoothing=g.SmoothingMode;g.SmoothingMode=SmoothingMode.AntiAlias;using var tick=new Pen(Theme.OnAccent,1.7f*k);
  if(state==CheckState.Indeterminate)g.DrawLine(tick,r.X+3f*k,r.Y+r.Height/2f,r.Right-3f*k,r.Y+r.Height/2f);
  else g.DrawLines(tick,new[]{new PointF(r.X+3f*k,r.Y+r.Height/2f),new PointF(r.X+r.Width/2f-1f*k,r.Bottom-4f*k),new PointF(r.Right-3f*k,r.Y+3.5f*k)});
  g.SmoothingMode=smoothing;
 }
 protected override void OnMouseEnter(EventArgs e){base.OnMouseEnter(e);hot=true;Invalidate();}
 protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hot=false;Invalidate();}
 protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);if(e.Button!=MouseButtons.Left)return;Focus();if(CheckArea().Contains(e.Location))Tick();else Flip();}
 protected override void OnKeyDown(KeyEventArgs e){base.OnKeyDown(e);if(e.KeyCode==Keys.Enter){Flip();e.Handled=true;}else if(e.KeyCode==Keys.Space){if(CheckText!=null)Tick();else Flip();e.Handled=true;}}
 protected override bool IsInputKey(Keys keyData)=>keyData==Keys.Enter||base.IsInputKey(keyData);
 protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);Invalidate();}
 protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);Invalidate();}
}

/// <summary>재확인의 띠: 왼쪽에 강조색 막대, 굵은 첫 줄과 옅은 둘째 줄</summary>
sealed class NoticeBand:Control
{
 public string Second="";
 public NoticeBand(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);TabStop=false;Margin=Padding.Empty;AccessibleRole=AccessibleRole.StaticText;}
 protected override void OnTextChanged(EventArgs e){base.OnTextChanged(e);AccessibleName=Text+" "+Second;Invalidate();}
 protected override void OnPaint(PaintEventArgs e)
 {
  var g=e.Graphics;g.Clear(Theme.Field);
  using(var bar=new SolidBrush(Theme.Accent))g.FillRectangle(bar,0,0,Theme.S(3),Height);
  var first=Ink.Body(13f,true);var second=Ink.Body(12f);float x=Theme.S(16);
  Ink.DrawClipped(g,Text,first,Theme.Text,x,Ink.Baseline(Height*.34f,first),Width-x-Theme.S(10));
  Ink.DrawClipped(g,Second,second,Theme.Dim,x,Ink.Baseline(Height*.70f,second),Width-x-Theme.S(10));
 }
}
