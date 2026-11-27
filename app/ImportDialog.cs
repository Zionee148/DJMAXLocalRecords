using System.Drawing.Drawing2D;
namespace LadderRecords;

// V아카이브 가져오기. 닉네임을 받고, 불러오고, 바뀔 것을 보이고, 가져오는 것까지 한 창이 한다.
// 창 너비: 닉네임을 받을 때 430, 결과를 보일 때 580(100% 때의 값. 본문 좌우 여백 18).
// 창 안의 것은 모두 처음에 만들어 두고 단계마다 보이는 것만 바꾼다(창이 뜬 뒤에 만든 것은 창의 크기 단계만큼 커지지 않는다).

/// <summary>가져오면 바뀔 한 줄: 어느 패턴에, 더하는지 고치는지, 어떤 값으로</summary>
record ImportRow(string Title,string Pattern,bool Added,int Score);

/// <summary>'추가·갱신할 점수 보기'의 표: 곡명 | 패턴 | RATE | 환산 SCORE. 곡명은 한 줄로 그리고 넘치면 끝을 줄인다(풍선 도움말은 띄우지 않는다)</summary>
sealed class ImportTable:OwnerTable
{
 IReadOnlyList<ImportRow> rows=Array.Empty<ImportRow>();readonly ImageList spacing=new();
 public const int RowHeight=49,HeadHeight=30;
 public IReadOnlyList<ImportRow> Rows=>rows;
 public ImportTable()
 {
  spacing.ImageSize=new(1,Theme.S(RowHeight));spacing.Images.Add(new Bitmap(1,1));SmallImageList=spacing;
  Font=Theme.Px(AppFonts.Ui,13f);HeaderStyle=ColumnHeaderStyle.Nonclickable;TabStop=true;AccessibleName="추가·갱신할 점수";
  Columns.Add(new ColumnHeader(){Text="곡명",Width=280});Columns.Add(new ColumnHeader(){Text="패턴",Width=80});Columns.Add(new ColumnHeader(){Text="RATE",Width=84});Columns.Add(new ColumnHeader(){Text="환산 SCORE",Width=98});
  RetrieveVirtualItem+=(_,e)=>{var row=e.ItemIndex<rows.Count?rows[e.ItemIndex]:null;e.Item=new ListViewItem(row==null?new[]{"","","",""}:new[]{row.Title+(row.Added?" (추가)":" (갱신)"),row.Pattern,ScoreText.Rate(row.Score),$"{row.Score:N0}"}){ImageIndex=0};};
  HeaderPainter=(g,area,index)=>
  {
   var chain=index>=2?Ink.Number(12f):Ink.Body(12f,true);var part=new[]{new InkPart(Columns[index].Text,chain,Theme.Dim)};float baseline=Ink.Baseline(area.Top+area.Height/2f,chain);
   if(index>=2)Ink.DrawRight(g,part,area.Right-Theme.S(10),baseline);else Ink.Draw(g,part,area.Left+Theme.S(10),baseline);
  };
  DrawSubItem+=(_,e)=>
  {
   if(e.Item==null||e.ItemIndex>=rows.Count)return;var row=rows[e.ItemIndex];var g=e.Graphics;var cell=e.Bounds;float middle=cell.Top+cell.Height/2f;
   using(var back=new SolidBrush(Theme.Table))g.FillRectangle(back,cell);
   using(var line=new Pen(Theme.RowLine))g.DrawLine(line,cell.Left,cell.Bottom-1,cell.Right,cell.Bottom-1);
   switch(e.ColumnIndex)
   {
    case 0:
     Ink.DrawClipped(g,row.Title,Ink.Title(14f),Theme.Text,cell.Left+Theme.S(10),Ink.Baseline(cell.Top+cell.Height*.34f,Ink.Title(14f)),cell.Width-Theme.S(18));
     Ink.Draw(g,new[]{new InkPart(row.Added?"추가":"갱신",Ink.Body(12f),Theme.Dim)},cell.Left+Theme.S(10),Ink.Baseline(cell.Top+cell.Height*.72f,Ink.Body(12f)));break;
    case 1:Ink.Draw(g,new[]{new InkPart(row.Pattern,Ink.Number(12f),Theme.Dim)},cell.Left+Theme.S(10),Ink.Baseline(middle,Ink.Number(12f)));break;
    case 2:Ink.DrawRight(g,new[]{new InkPart(ScoreText.Rate(row.Score),Ink.Number(13f),Theme.Text)},cell.Right-Theme.S(10),Ink.Baseline(middle,Ink.Number(13f)));break;
    default:Ink.DrawRight(g,new[]{new InkPart($"{row.Score:N0}",Ink.Number(13f),Theme.Text)},cell.Right-Theme.S(10),Ink.Baseline(middle,Ink.Number(13f)));break;
   }
  };
  // 고른 줄을 두지 않는다(보기만 하는 표다)
  SelectedIndexChanged+=(_,_)=>{if(SelectedIndices.Count>0)SelectedIndices.Clear();};
  ClientSizeChanged+=(_,_)=>Fit();
 }
 // 패턴·RATE·환산 SCORE 의 자리를 먼저 잡고 남는 너비를 곡명에 준다(세로 스크롤 막대가 생기면 그만큼 곡명이 준다)
 bool fitting;
 void Fit()
 {
  if(fitting||Columns.Count<4||ClientSize.Width<=0)return;fitting=true;
  try{float unit=ClientSize.Width/542f;int pattern=(int)(80*Math.Max(1f,unit)),rate=(int)(84*Math.Max(1f,unit)),score=(int)(98*Math.Max(1f,unit));int title=Math.Max(60,ClientSize.Width-pattern-rate-score);
   if(Columns[0].Width!=title)Columns[0].Width=title;if(Columns[1].Width!=pattern)Columns[1].Width=pattern;if(Columns[2].Width!=rate)Columns[2].Width=rate;if(Columns[3].Width!=score)Columns[3].Width=score;}
  finally{fitting=false;}
 }
 public void Set(IReadOnlyList<ImportRow> list){rows=list;VirtualListSize=list.Count;Fit();Invalidate();}
 /// <summary>줄 count 개가 굴리지 않고 보이는 높이. 표가 화면에 있으면 실제 머리글과 줄의 높이로 셈하고, 아니면 100% 때의 값에 unit 을 곱한다</summary>
 public int HeightFor(int count,float unit)
 {
  // 첫 줄의 위쪽 좌표는 굴린 만큼 음수가 된다. 그것을 머리글의 높이로 쓰면 끝까지 내린 뒤 다시 폈을 때 높이가 0 이 된다. 머리글의 높이는 머리글에서 읽는다
  if(IsHandleCreated&&VirtualListSize>0)return HeadTall()+GetItemRect(0).Height*count;
  return (int)Math.Round((HeadHeight+count*RowHeight)*unit);
 }
 protected override void Dispose(bool disposing){if(disposing)spacing.Dispose();base.Dispose(disposing);}
}

/// <summary>접었다 펴는 줄('▶ 추가·갱신할 점수 보기'). 누르거나 Space·Enter 로 바꾼다. 펼치면 테두리가 강조색이 된다</summary>
sealed class FoldBar:Control
{
 bool open,hot;
 /// <summary>상자 없이 세모와 글만 그린다(공통 안내창의 '오류 내용')</summary>
 public bool Plain;
 public event Action? Toggled;
 public bool Open=>open;
 public FoldBar(string text)
 {
  SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.Selectable,true);
  Text=text;Cursor=Cursors.Hand;TabStop=true;Margin=Padding.Empty;AccessibleName=text;AccessibleRole=AccessibleRole.PushButton;AccessibleDescription="접힘";
 }
 /// <summary>접거나 편다(누른 것과 같다)</summary>
 internal void Flip(){Set(!open);Toggled?.Invoke();}
 public void Set(bool value){if(open==value)return;open=value;AccessibleDescription=value?"펼침":"접힘";Invalidate();}
 protected override void OnPaint(PaintEventArgs e)
 {
  var g=e.Graphics;g.Clear(Parent?.BackColor??Theme.Window);g.SmoothingMode=SmoothingMode.AntiAlias;
  if(!Plain)using(var shape=BadgeDesign.Rounded(new RectangleF(0,0,Width-1,Height-1),Theme.S(3f))){using var fill=new SolidBrush(Theme.Field);g.FillPath(fill,shape);using var pen=new Pen(open||Focused&&ShowFocusCues?Theme.Accent:Theme.Line);g.DrawPath(pen,shape);}
  else if(Focused&&ShowFocusCues){using var pen=new Pen(Theme.Accent);g.DrawRectangle(pen,0,0,Width-1,Height-1);}
  float middle=Height/2f,x=Plain?Theme.S(2f):Theme.S(12f),size=Theme.S(4.5f);
  // 접었을 때 ▶, 펼쳤을 때 ▼ (글꼴에 그 글자가 없을 수 있어 직접 그린다)
  using(var ink=new SolidBrush(hot?Theme.Accent:Theme.Text))g.FillPolygon(ink,open?new[]{new PointF(x,middle-size*.7f),new PointF(x+size*2,middle-size*.7f),new PointF(x+size,middle+size*.9f)}:new[]{new PointF(x+size*.2f,middle-size),new PointF(x+size*.2f,middle+size),new PointF(x+size*1.8f,middle)});
  g.SmoothingMode=SmoothingMode.Default;
  var chain=Ink.Body(13f,true);Ink.Draw(g,new[]{new InkPart(Text,chain,hot?Theme.Accent:Theme.Text)},x+size*2+Theme.S(8),Ink.Baseline(middle,chain));
 }
 protected override void OnMouseEnter(EventArgs e){base.OnMouseEnter(e);hot=true;Invalidate();}
 protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hot=false;Invalidate();}
 protected override void OnClick(EventArgs e){base.OnClick(e);Focus();Flip();}
 protected override void OnKeyDown(KeyEventArgs e){base.OnKeyDown(e);if(e.KeyCode is Keys.Space or Keys.Enter){Flip();e.Handled=true;}}
 protected override bool IsInputKey(Keys keyData)=>keyData==Keys.Enter||base.IsInputKey(keyData);
 protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);Invalidate();}
 protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);Invalidate();}
}

/// <summary>V아카이브 가져오기가 멈추고 띄우는 안내들</summary>
static class ImportNotice
{
 public const string Title="V아카이브 가져오기";
 public static void Profile(IWin32Window owner,string started,string now)=>PlayNotice.Show(owner,Title,"기록을 가져오는 동안 프로필이 바뀌었습니다.",
  "불러온 기록은 저장되지 않았습니다.\n메인 창에서 기록을 받을 프로필을 선택한 뒤 ‘V아카이브 가져오기’를 다시 눌러 주세요.",new[]{NoticeRow.Word("가져오기를 시작한 프로필",started),NoticeRow.Word("현재 프로필",now)});
 /// <summary>저장하지 못한 까닭과 다음 행동. 공간 부족, 다른 프로그램이 사용 중인 파일 등 까닭마다 그에 맞게 적는다(SaveTrouble)</summary>
 public static (string Reason,string? Next) Cause(Exception error){var (reason,before)=SaveTrouble.Of(error);return (reason,before==null?null:before+" ‘V아카이브 가져오기’를 다시 눌러 주세요.");}
 public static void Failed(IWin32Window owner,Exception error){var (reason,next)=Cause(error);PlayNotice.Show(owner,Title,"불러온 기록을 저장하지 못했습니다.",next==null?reason:reason+"\n"+next);}
}

class ImportDialog:CompactDialog
{
 public const int AskWidth=392,ResultWidth=542,MaxRows=5;
 public const string Scope="4B·5B·6B·8B의 노말판정 기록을 불러옵니다.",Loading="4B·5B·6B·8B의 노말판정 기록을 불러오는 중입니다.",
  ConvertNote="V아카이브의 RATE를 100점 단위의 SCORE로 환산합니다. 예를 들어 99.54%는 995,400점으로 저장됩니다.",
  Different="가져올 기록이 달라졌습니다. 변경 내용을 확인한 뒤 ‘가져오기’를 다시 눌러 주세요.",
  Nothing="변경할 기록이 없습니다.",FetchFailed="기록을 불러오지 못했습니다.",FetchAdvice="닉네임과 네트워크 연결을 확인해 주세요.";
 /// <summary>개발용 검사: V아카이브에 묻는 대신 이 답을 쓴다</summary>
 internal static Func<Catalog,string,Task<Dictionary<Selection,decimal>>>? Fetcher{get;set;}

 readonly Catalog catalog;readonly RecordStore store;readonly string profileId,profileName;
 readonly FlowLayoutPanel ask=Column(),result=Column(),message=Column();
 readonly InputBox nickname=new(AskWidth,"",40,42);readonly Label askNote,shownName,added,changed,different,messageText,messageDetail;readonly SectionHeader messageHead;
 readonly Button fetch=new(){Text="기록 불러오기",Tag="primary"},askCancel=new(){Text="취소"},apply=new(){Text="가져오기",Tag="primary"},resultCancel=new(){Text="취소"},retry=new(){Text="닉네임 다시 입력"},close=new(){Text="닫기",Tag="primary"};
 readonly FoldBar fold=new("추가·갱신할 점수 보기");readonly ImportTable table=new();
 Dictionary<Selection,decimal>? scores;List<PlayChange> shown=new();int asked;bool busy,warned;
 static FlowLayoutPanel Column()=>new(){AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,FlowDirection=FlowDirection.TopDown,WrapContents=false,Margin=Padding.Empty};
 /// <summary>가져왔으면 반영한 줄 수</summary>
 public int? Imported{get;private set;}
 /// <summary>지금 보이는 단계: ask(닉네임), loading(불러오는 중), result(바뀔 것), empty(바뀔 것 없음), failed(불러오지 못함)</summary>
 internal string Stage{get;private set;}="ask";
 internal TextBox NicknameBox=>nickname.Box;
 internal Button FetchButton=>fetch;
 internal Button ApplyButton=>apply;
 internal Button RetryButton=>retry;
 internal FoldBar Fold=>fold;
 internal ImportTable Table=>table;
 /// <summary>결과 단계의 건수 줄</summary>
 internal string Counts=>$"{added.Text} | {changed.Text}";
 /// <summary>'가져올 기록이 달라졌습니다 …'가 보이면 그 글</summary>
 internal string Warning=>warned?different.Text:"";
 /// <summary>바뀔 것이 없거나 불러오지 못했을 때 보이는 첫 줄과 안내</summary>
 internal string Message=>Stage is "empty" or "failed"?(messageHead.Text+"\n"+messageText.Text).Trim():"";
 /// <summary>불러오기가 끝날 때까지 기다린다(개발용 검사)</summary>
 internal Task Pending{get;private set;}=Task.CompletedTask;

 public ImportDialog(Catalog catalog,RecordStore store):base(ImportNotice.Title+" · "+store.Active.Name)
 {
  this.catalog=catalog;this.store=store;profileId=store.Active.Id;profileName=store.Active.Name;
  Body.Padding=new(18,16,18,16);

  // 1. 닉네임
  ask.Controls.Add(new SectionHeader("기록 불러오기"){Width=AskWidth,Margin=new(0,0,0,12)});
  var nameLabel=Boxes.Text("V아카이브 닉네임",null,9.75f,true);nameLabel.Margin=new(0,0,0,8);ask.Controls.Add(nameLabel);
  nickname.Box.Font=Theme.Px(AppFonts.Ui,11.25f,FontStyle.Bold);nickname.Box.AccessibleName="V아카이브 닉네임";ask.Controls.Add(nickname);
  askNote=Boxes.Text(Scope,"dim",9.75f,false,AskWidth);askNote.Margin=new(0,8,0,16);ask.Controls.Add(askNote);
  ask.Controls.Add(Parts.Footer(AskWidth,fetch,askCancel));

  // 2. 바뀔 것
  result.Controls.Add(new SectionHeader("노말판정 기록 가져오기"){Width=ResultWidth,Margin=new(0,0,0,10)});
  var who=new Panel(){Size=new(ResultWidth,22),Margin=Padding.Empty};var whoName=Boxes.Text("V아카이브 닉네임","dim",9f,true);who.Controls.Add(whoName);whoName.Location=new(0,(22-whoName.PreferredHeight)/2);
  shownName=new Label(){AutoSize=false,AutoEllipsis=true,UseMnemonic=false,Size=new(ResultWidth-whoName.PreferredWidth-8,22),TextAlign=ContentAlignment.MiddleLeft,Font=Theme.Px(AppFonts.Latin,11.25f,FontStyle.Bold),Location=new(whoName.PreferredWidth+8,0)};who.Controls.Add(shownName);
  result.Controls.Add(who);
  var counts=new FlowLayoutPanel(){AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,WrapContents=false,Margin=new(0,6,0,8)};
  added=new Label(){Text="추가 0건",AutoSize=true,UseMnemonic=false,Margin=new(0,0,10,0),Font=Theme.Px(AppFonts.Ui,12f,FontStyle.Bold)};
  changed=new Label(){Text="갱신 0건",AutoSize=true,UseMnemonic=false,Margin=new(10,0,0,0),Font=Theme.Px(AppFonts.Ui,12f,FontStyle.Bold)};
  counts.Controls.Add(added);counts.Controls.Add(new Panel(){Size=new(1,14),Margin=new(0,5,0,0),Tag="line"});counts.Controls.Add(changed);
  result.Controls.Add(counts);
  fold.Size=new(ResultWidth,34);result.Controls.Add(fold);
  table.Size=new(ResultWidth,ImportTable.HeadHeight+ImportTable.RowHeight);table.Margin=Padding.Empty;table.Visible=false;result.Controls.Add(table);
  var note=Boxes.Text(ConvertNote,null,9.75f,false,ResultWidth);note.Margin=new(0,10,0,0);result.Controls.Add(note);
  different=Boxes.Text(Different,"warn",9.75f,true,ResultWidth);different.Margin=new(0,8,0,0);different.Visible=false;result.Controls.Add(different);
  var resultFooter=Parts.Footer(ResultWidth,apply,resultCancel);resultFooter.Margin=new(0,16,0,0);result.Controls.Add(resultFooter);
  result.Visible=false;

  // 3. 바뀔 것이 없거나 불러오지 못함
  var head=Parts.TitleLine(Nothing,AskWidth);messageHead=(SectionHeader)head.Controls[0];message.Controls.Add(head);
  messageText=Boxes.Text(FetchAdvice,null,9.75f,false,AskWidth);messageText.Margin=new(0,10,0,0);message.Controls.Add(messageText);
  messageDetail=Boxes.Text("","dim",9f,false,AskWidth);messageDetail.Margin=new(0,4,0,0);messageDetail.Visible=false;message.Controls.Add(messageDetail);
  var messageFooter=Parts.Footer(AskWidth,retry,close);messageFooter.Margin=new(0,16,0,0);message.Controls.Add(messageFooter);
  message.Visible=false;

  Body.Controls.Add(ask);Body.Controls.Add(result);Body.Controls.Add(message);
  AcceptButton=fetch;CancelButton=askCancel;
  nickname.Box.TextChanged+=(_,_)=>fetch.Enabled=!busy&&nickname.Box.Text.Trim().Length>0;fetch.Enabled=false;
  fetch.Click+=(_,_)=>Pending=Fetch();
  apply.Click+=(_,_)=>{if(Apply()){DialogResult=DialogResult.OK;Close();}};
  retry.Click+=(_,_)=>Turn("ask");
  foreach(var button in new[]{askCancel,resultCancel,close})button.Click+=(_,_)=>{DialogResult=DialogResult.Cancel;Close();};
  fold.Toggled+=ShowTable;
  Shown+=(_,_)=>nickname.Box.Focus();
  FormClosed+=(_,_)=>asked++;   // 닫힌 뒤에 도착한 응답은 버린다
 }

 // 단계마다 보이는 것만 바꾼다. 창의 크기는 내용에 맞춰 따라가고(430 ↔ 580), 주인 창의 가운데에 다시 놓는다
 void Turn(string stage)
 {
  Stage=stage;
  ask.Visible=stage is "ask" or "loading";result.Visible=stage=="result";message.Visible=stage is "empty" or "failed";
  nickname.Enabled=stage=="ask";busy=stage=="loading";fetch.Enabled=!busy&&nickname.Box.Text.Trim().Length>0;fetch.Text=busy?"불러오는 중":"기록 불러오기";
  askNote.Text=busy?Loading:Scope;
  retry.Visible=stage=="failed";
  AcceptButton=stage switch{"result"=>apply,"empty" or "failed"=>close,_=>fetch};CancelButton=stage switch{"result"=>resultCancel,"empty" or "failed"=>close,_=>askCancel};
  if(!IsHandleCreated||!Visible)return;
  if(Owner is {} owner)Location=new(owner.Left+(owner.Width-Width)/2,owner.Top+(owner.Height-Height)/2);
  if(stage=="ask"){nickname.Box.Focus();nickname.Box.SelectAll();}else if(stage=="result")apply.Focus();else if(stage=="failed")retry.Focus();else if(stage=="empty")close.Focus();
 }
 void Say(string stage,string head,string text,string detail="")
 {
  messageHead.Text=head;messageHead.Width=messageHead.TextWidth+4;messageText.Text=text;   // 첫 줄의 너비는 글에 맞춘다(아직 화면에 뜬 적이 없으면 스스로 맞추지 않는다)
  messageText.Visible=text.Length>0;messageDetail.Text=detail;messageDetail.Visible=detail.Length>0;Turn(stage);
 }

 /// <summary>
 /// 불러오지 못한 까닭을 안내 아래에 옅게 적는 한 줄. 받은 오류의 영문 문장을 그대로 보이지 않고 짧게 줄인다:
 /// 다음 행동은 위의 안내가 말하고, 이 줄은 제보할 때 쓸 번호나 종류만 남긴다('서버 응답 코드 404')
 /// </summary>
 internal static string FetchDetail(Exception error)=>error switch
 {
  HttpRequestException{StatusCode:{} code}=>$"서버 응답 코드 {(int)code}",
  // 연결은 됐는데 서버가 응답을 마치기 전에 닫았다. 머리글을 받기 전이면 HttpRequestException, 자료를 받는 도중이면 HttpIOException 으로 온다(둘 다 .NET 이 ResponseEnded 로 가른다).
  // '연결하지 못함'이나 오류의 이름만으로는 맞지 않는 글이 된다
  HttpRequestException{HttpRequestError:HttpRequestError.ResponseEnded} or HttpIOException{HttpRequestError:HttpRequestError.ResponseEnded}=>"서버 응답을 끝까지 받지 못함 (ResponseEnded)",
  HttpRequestException http=>$"서버에 연결하지 못함 ({http.HttpRequestError})",
  OperationCanceledException or TimeoutException=>"응답 시간 초과",
  System.Text.Json.JsonException=>"응답을 읽지 못함",
  InvalidDataException=>error.Message,   // 프로그램이 스스로 적은 글이다('8B 기록을 받지 못함'. 다른 줄과 같은 '~함' 꼴)
  _=>$"오류 종류 {error.GetType().Name}",
 };
 /// <summary>불러오지 못했을 때 안내 아래에 보이는 까닭</summary>
 internal string Detail=>Stage=="failed"?messageDetail.Text:"";
 /// <summary>닉네임의 기록을 불러와 지금 프로필과 견준다. 저장하지 않는다</summary>
 internal async Task Fetch()
 {
  string name=nickname.Box.Text.Trim();if(busy||name.Length==0)return;
  int turn=++asked;Turn("loading");
  Dictionary<Selection,decimal> got;
  try{got=await (Fetcher??VArchiveImport.Fetch)(catalog,name);}
  catch(Exception error){if(turn==asked&&!IsDisposed)Say("failed",FetchFailed,FetchAdvice,FetchDetail(error));return;}
  if(turn!=asked||IsDisposed)return;
  // 조회 직후: 그사이 프로필이 바뀌었으면 조회한 것을 버리고 알린 뒤 닫는다(주 화면에서 다시 시작한다)
  if(store.Active.Id!=profileId){ImportNotice.Profile(this,profileName,store.Active.Name);DialogResult=DialogResult.Cancel;Close();return;}
  scores=got;shownName.Text=name;Warn(false);fold.Set(false);
  if(!Fill(store.PreviewImport(got))){Say("empty",Nothing,"");return;}
  Turn("result");
 }
 void Warn(bool on){warned=on;different.Visible=on;}
 // 바뀔 것을 창에 적는다. 바뀔 것이 없으면 false
 bool Fill(List<PlayChange> changes)
 {
  shown=changes;if(changes.Count==0)return false;
  var songs=catalog.Songs.ToDictionary(s=>s.Id);
  var rows=changes.Select(change=>{var parts=change.After!.Key.Split('|');return new ImportRow(songs.TryGetValue(parts[0],out var song)?OverlayText.Title(catalog,song):parts[0],$"{parts[1]}B · {parts[2]}",change.Before==null,change.After.Score);}).ToList();
  int fresh=changes.Count(c=>c.Before==null);added.Text=$"추가 {fresh:N0}건";changed.Text=$"갱신 {changes.Count-fresh:N0}건";
  table.Set(rows);ShowTable();return true;
 }
 // 목록을 펴거나 접는다. 펼친 표는 다섯 줄까지 보이고 그 뒤는 표 안에서 굴린다
 void ShowTable()
 {
  table.Visible=fold.Open;if(!fold.Open)return;
  int tall=table.HeightFor(Math.Min(MaxRows,Math.Max(1,table.Rows.Count)),Unit);if(table.Height!=tall)table.Height=tall;
 }

 /// <summary>
 /// 가져온다. 가져왔으면 true(창을 닫는다). 누른 순간에 이미 불러온 응답을 지금의 기록과 다시 견준다: 대상 패턴, 추가·갱신의 구분, 바뀔 값이 창에 보인 것과 같을 때만 저장한다.
 /// 달라졌으면 이번에는 저장하지 않고 건수와 목록을 고쳐 적은 뒤 다시 승인을 받는다. 바뀔 것이 없어졌으면 저장하지 않고(되돌리기 대상도 그대로다) '변경할 기록이 없습니다.'를 보인다.
 /// 다른 곡의 저장으로 변경 번호만 달라진 것은 다시 묻지 않는다
 /// </summary>
 internal bool Apply()
 {
  if(scores==null)return false;
  if(store.Active.Id!=profileId){ImportNotice.Profile(this,profileName,store.Active.Name);DialogResult=DialogResult.Cancel;Close();return false;}
  var now=store.PreviewImport(scores);
  if(!now.SequenceEqual(shown))
  {
   if(!Fill(now)){Say("empty",Nothing,"");return false;}
   Warn(true);return false;
  }
  try{Imported=store.Import(scores);return true;}
  catch(Exception error){ImportNotice.Failed(this,error);DialogResult=DialogResult.Cancel;Close();return false;}
 }
}
