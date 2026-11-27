using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
namespace LadderRecords;
// 컬렉션 가져오기의 창: 시작 대기 → 첫 곡 찾기·점수 읽는 중 → 중단(또는 끝). 끝까지 읽었고 확인할 것이 없으면 전곡 읽기 완료 → 적용.
// 확인할 것이 있으면 먼저 판독 검수(같은 창의 단계)를 거친다. 기록 저장소를 받지 않은 창(개발용 검사)은 개발용 요약을 보인다.
// 시작은 게임이 앞 창일 때 Ctrl+Enter, 중지는 Backspace(전역 단축키. 시작은 기다릴 때만, 중지는 읽는 동안만 켠다). 읽기는 CollectionScan 이 따로 도는 스레드에서 한다.
// 저장은 완료 화면의 '적용'이 RecordStore.ImportCollection 으로 한 번에 한다(직전 저장 되돌리기 한 번으로 돌아간다). 그 밖에는 아무것도 저장하지 않는다.
// 창이 뜬 뒤에 만든 컨트롤은 크기 단계만큼 커지지 않는다: 단계마다의 화면을 처음에 다 만들어 두고 보이는 것만 바꾼다.

/// <summary>키보드의 키 모양(테두리 있는 작은 상자 안의 굵은 글자)</summary>
sealed class KeyCap:Control
{
 public KeyCap(string text,float points=9f)
 {
  SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.SupportsTransparentBackColor,true);
  Text=text;AccessibleName=text;AccessibleRole=AccessibleRole.StaticText;TabStop=false;Margin=Padding.Empty;Font=Theme.Px(AppFonts.Latin,points,FontStyle.Bold);Fit();
 }
 void Fit(){var size=TextRenderer.MeasureText(Text,Font,Size.Empty,TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix);int pad=Math.Max(6,Font.Height*6/10);Size=new(size.Width+pad*2,Font.Height+pad);}
 protected override void OnFontChanged(EventArgs e){base.OnFontChanged(e);Fit();}
 protected override void OnTextChanged(EventArgs e){base.OnTextChanged(e);AccessibleName=Text;Fit();}
 protected override void OnPaint(PaintEventArgs e)
 {
  var g=e.Graphics;g.Clear(Parent?.BackColor??Theme.Window);g.SmoothingMode=SmoothingMode.AntiAlias;
  using(var shape=BadgeDesign.Rounded(new RectangleF(0.5f,0.5f,Width-2,Height-2),Math.Max(2f,Height/7f))){using var fill=new SolidBrush(Theme.Table);g.FillPath(fill,shape);using var pen=new Pen(Theme.Mix(Theme.Line,Theme.Text,.25));g.DrawPath(pen,shape);}
  TextRenderer.DrawText(g,Text,Font,new Rectangle(0,0,Width-1,Height-1),Theme.Text,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine);
 }
}

/// <summary>옅은 상자 안에 줄마다 이름(왼쪽)과 키 모양(오른쪽 끝). 컬렉션 창의 시작 대기와 단축키 설정 창의 '컬렉션 가져오기' 구역이 쓴다</summary>
static class KeyBoxes
{
 /// <summary>같은 상자를 다른 창(단축키 설정 창의 '컬렉션 가져오기' 구역)에서도 쓴다. 너비는 그 창의 본문 너비</summary>
 public static Control Make(int top,int width,params (string Name,string[] Keys)[] rows)
 {
  const int RowHeight=34;
  var box=new FieldPanel(){Size=new(width,RowHeight*rows.Length+12),Margin=new(0,top,0,0)};
  for(int i=0;i<rows.Length;i++)
  {
   int y=6+RowHeight*i;
   var name=new Label(){Text=rows[i].Name,AutoSize=true,UseMnemonic=false,BackColor=Color.Transparent,Font=Theme.Px(AppFonts.Ui,9.75f,FontStyle.Bold),Location=new(14,y+9)};box.Controls.Add(name);
   int right=width-14;
   for(int k=rows[i].Keys.Length-1;k>=0;k--)
   {
    var cap=new KeyCap(rows[i].Keys[k]);cap.Location=new(right-cap.Width,y+(RowHeight-cap.Height)/2);box.Controls.Add(cap);right=cap.Left;
    if(k>0){var plus=new Label(){Text="+",AutoSize=true,Tag="dim",UseMnemonic=false,BackColor=Color.Transparent,Font=Theme.Px(AppFonts.Ui,9.75f)};plus.Location=new(right-plus.PreferredWidth-4,y+9);box.Controls.Add(plus);right=plus.Left-4;}
   }
  }
  return box;
 }
}

[System.Runtime.Versioning.SupportedOSPlatform("windows10.0.19041")]
sealed class CollectionWindow:CompactDialog
{
 public const int ContentWidth=456,Side=20;
 /// <summary>전곡 읽기 완료 화면의 너비(창 너비 630 에서 좌우 여백을 뺀 값. 100% 때)</summary>
 public const int DoneWidth=590;
 /// <summary>판독 검수의 너비(창 너비 874 에서 좌우 여백을 뺀 값. 100% 때)</summary>
 public const int ReviewWidth=ReviewColumns.Table;
 public const string Title="컬렉션 가져오기";
 /// <summary>Finished 는 개발용 요약(기록 저장소가 없거나 확인할 것이 있을 때), Complete 는 전곡 읽기 완료, Nothing 은 '변경할 기록이 없습니다.'</summary>
 public enum Stage{Ready,Climbing,Reading,Stopped,TooMany,Finished,Complete,Nothing,Review}
 /// <summary>지금의 단계(창이 뜨기 전에도 맞다. 컨트롤의 Visible 은 창이 뜨기 전에 늘 거짓이라 따로 둔다)</summary>
 public Stage Now{get;private set;}=Stage.Ready;
 /// <summary>끝까지 읽었다(Done). 받는 쪽이 결과를 쓴다</summary>
 public event Action<ScanResult>? Finished;
 /// <summary>적용해 저장했다(창은 닫힌다. 메인 창은 상태 줄에 추가·갱신·삭제 건수를 적는다)</summary>
 public event Action<CollectionSaveResult>? Applied;
 /// <summary>읽기가 끝나면 창을 앞으로 가져온다. 거짓이면 앞으로 가져오지 않고 제목 표시줄과 작업 표시줄을 깜빡여 알린다</summary>
 public bool FinishToFront=true;
 /// <summary>개발용: 실제 게임 대신 쓸 것(창 검사가 가짜 게임을 준다)</summary>
 [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
 internal Func<IScanWorld?>? WorldForTest{get;set;}
 /// <summary>검사가 '시작 키를 아직 누르고 있다'를 흉내 낸다(가짜 게임에는 키보드가 없다)</summary>
 [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
 internal Func<bool>? KeysHeldForTest{get;set;}
 /// <summary>개발용: 끝·멈춤·취소 어느 때든 결과를 받는다(Finished 는 끝까지 읽었을 때만)</summary>
 internal event Action<ScanResult>? EndedForTest;
 /// <summary>읽기가 끝나거나 멈출 때마다(끝·중단·취소·허용 수 초과). 메인 창이 진단 기록에 한 줄을 남긴다.
 /// 읽기의 스레드에서 끝나는 자리에서 바로 부른다: 창이 이미 닫혔어도 한 번. 받는 쪽이 제 스레드로 옮긴다</summary>
 public event Action<ScanResult>? ScanEnded;
 /// <summary>개발용: 읽기의 진행이 적는 기록(CollectionScan.Trace)을 받는다</summary>
 [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
 internal Action<string>? TraceForTest{get;set;}
 [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
 internal Action<ScanItem>? ClosingForTest{get;set;}

 readonly Catalog catalog;readonly CollectionData data;
 readonly Dictionary<Stage,(Control Body,Control Footer)> stages=new();
 // 시작 대기
 readonly WordLabel readyReason;
 // 읽는 중
 readonly SectionHeader runHead;readonly Label runTitle;readonly WordLabel climbLine;readonly Control countBox,runNote;readonly Label countValue;
 // 중단
 readonly WordLabel stopHead;readonly FieldPanel stopBox;readonly Label stopName,stopValue;readonly WordLabel stopNext;
 // 허용 수 초과
 readonly WordLabel limitCounts;readonly FoldBar limitFold;readonly Control limitInside;
 // 끝(개발용 요약. 기록 저장소를 받지 않은 창에서만 쓴다. 받은 창은 전곡 읽기 완료 화면으로 간다)
 readonly WordLabel doneLines;
 // 전곡 읽기 완료
 readonly RecordStore? store;readonly NoticeBand doneBand;readonly Label doneSummary,deleteLine;readonly FoldBar addFold;readonly DoneTable addTable;
 readonly Dictionary<DoneKind,DoneHeader> headers=new();readonly Dictionary<DoneKind,DoneTable> tables=new();
 string? startProfile;CollectionOutcome? outcome;CollectionPlan? seen,recheckBefore;CollectionChoices choices=new();IReadOnlyDictionary<string,IReadOnlyList<string>>? charts;
 // 판독 검수
 CollectionReview? review;readonly ReviewHead reviewHead;readonly FlowLayoutPanel reviewRows;readonly WordLabel reviewProtect,reviewNotFound,reviewShaky;readonly Label reviewStatus;readonly Button reviewConfirm;
 readonly List<ReviewLine> lines=new();
 /// <summary>펼친 목록: -1 없음, 0~3 삭제 항목(DoneKind), 4 추가·갱신할 점수</summary>
 int openList=-1;const int AddsList=4;

 ScanHotkeys? hotkeys;CollectionGame? game;Thread? worker;

 public CollectionWindow(Catalog catalog,string profile,RecordStore? store=null):base($"{Title} · {profile}")
 {
  this.catalog=catalog;this.store=store;data=CollectionData.Load();
  Body.Padding=new(0,16,0,0);StartPosition=FormStartPosition.CenterScreen;ShowInTaskbar=true;

  // ── 시작 대기 ──
  {
   var col=Column();col.Controls.Add(Head("시작 대기"));
   readyReason=new WordLabel("",ContentWidth,9.75f,"bad"){Margin=new(0,10,0,0)};col.Controls.Add(readyReason);readyReason.Visible=false;
   col.Controls.Add(Big("게임에서 ALL 목록을 열어 주세요.",10));
   var path=new FlowLayoutPanel(){AutoSize=true,WrapContents=false,Margin=new(0,10,0,0)};
   string[] steps={"COLLECTION","PLAY INFO","MUSIC DATA"};
   foreach(string step in steps){path.Controls.Add(PathWord(step));path.Controls.Add(PathMark());}
   var all=new KeyCap("ALL",9.5f){Margin=new(2,0,0,0)};path.Controls.Add(all);
   col.Controls.Add(path);
   col.Controls.Add(Para("게임 창에서 시작 단축키를 누르면 첫 곡까지 자동으로 올라간 뒤, 마지막 곡까지 패턴별 최고 점수를 읽습니다.","dim",12));
   col.Controls.Add(KeyBox(12,ScanHotkeys.Start,ScanHotkeys.Stop));
   col.Controls.Add(Para("읽는 동안 다른 창으로 전환하면 가져오기가 취소됩니다.","dim",12));
   col.Controls.Add(Para("마우스 커서는 곡명과 점수표 밖에 두어 주세요.","dim",4));
   var close=new Button(){Text="닫기"};close.Click+=(_,_)=>Close();
   Add(Stage.Ready,col,close);
  }
  // ── 첫 곡 찾기·점수 읽는 중 ──
  {
   var col=Column();runHead=Head("점수 읽는 중");col.Controls.Add(runHead);
   climbLine=new WordLabel("ALL 목록의 첫 곡으로 이동 중입니다.",ContentWidth,13f,"strong"){Margin=new(0,10,0,0)};col.Controls.Add(climbLine);
   runTitle=new Label(){AutoSize=false,AutoEllipsis=true,UseMnemonic=false,Size=new(ContentWidth,30),Margin=new(0,8,0,0),Font=Theme.Px(AppFonts.Ui,13f,FontStyle.Bold),TextAlign=ContentAlignment.MiddleLeft};col.Controls.Add(runTitle);
   var box=new FieldPanel(){Size=new(ContentWidth,44),Margin=new(0,8,0,0)};
   var name=new Label(){Text="읽은 곡",AutoSize=true,Tag="dim",UseMnemonic=false,BackColor=Color.Transparent,Font=Theme.Px(AppFonts.Ui,9f),Location=new(14,14)};box.Controls.Add(name);
   countValue=new Label(){Text="0곡",AutoSize=true,UseMnemonic=false,BackColor=Color.Transparent,Font=Theme.Px(AppFonts.Ui,12f,FontStyle.Bold),Location=new(72,10)};box.Controls.Add(countValue);
   countBox=box;col.Controls.Add(box);
   runNote=Para("4B·5B·6B·8B의 패턴별 최고 점수를 읽고 있습니다.","dim",10);col.Controls.Add(runNote);
   col.Controls.Add(KeyBox(12,ScanHotkeys.Stop));
   var stop=new Button(){Text="중지"};stop.Click+=(_,_)=>game?.AskStop();stop.MouseDown+=(_,e)=>{if(e.Button==MouseButtons.Left)game?.AskStop();};   // 누르는 순간 이 창이 앞 창이 된다: 뗄 때(Click)만 알리면 읽는 쪽이 먼저 '창 전환'으로 멈춘다
   Add(Stage.Climbing,col,stop);stages[Stage.Reading]=stages[Stage.Climbing];
  }
  // ── 중단 ──
  {
   var col=Column();col.Controls.Add(Head("가져오기 중단"));
   stopHead=new WordLabel("",ContentWidth,13f,"strong"){Margin=new(0,10,0,0)};col.Controls.Add(stopHead);
   stopBox=new FieldPanel(){Size=new(ContentWidth,40),Margin=new(0,10,0,0)};
   stopName=new Label(){AutoSize=true,Tag="dim",UseMnemonic=false,BackColor=Color.Transparent,Font=Theme.Px(AppFonts.Ui,9f),Location=new(14,12)};stopBox.Controls.Add(stopName);
   stopValue=new Label(){AutoSize=false,AutoEllipsis=true,UseMnemonic=false,BackColor=Color.Transparent,Font=Theme.Px(AppFonts.Ui,10.5f,FontStyle.Bold),Height=22,Location=new(130,9)};stopBox.Controls.Add(stopValue);
   col.Controls.Add(stopBox);
   col.Controls.Add(Para("기존 기록은 그대로입니다.","",12));
   stopNext=new WordLabel("",ContentWidth,9.75f){Margin=new(0,4,0,0)};col.Controls.Add(stopNext);
   var close=new Button(){Text="닫기"};close.Click+=(_,_)=>Close();
   Add(Stage.Stopped,col,close);
  }
  // ── 허용 수 초과 ──
  {
   var col=Column();col.Controls.Add(Head("가져오기 중단"));
   col.Controls.Add(new WordLabel("확인이 필요한 항목이 많아 읽기를 중단했습니다.",ContentWidth,13f,"strong"){Margin=new(0,10,0,0)});
   limitCounts=new WordLabel("",ContentWidth,9.75f){Margin=new(0,10,0,0)};col.Controls.Add(limitCounts);
   col.Controls.Add(Para("마우스 커서나 다른 창이 곡명과 점수표를 가리고 있지 않은지 확인한 뒤 다시 시도해 주세요.","",10));
   col.Controls.Add(new Panel(){Size=new(ContentWidth,1),Margin=new(0,12,0,6),Tag="line"});
   limitFold=new FoldBar("계속 중단될 때"){Plain=true,Size=new(ContentWidth,24)};col.Controls.Add(limitFold);
   limitInside=Para("게임 화면이 1920×1080이 아니면 게임을 1920×1080 창 모드로 바꿔 다시 시도해 주세요. 게임 언어가 한국어인지 확인해 주세요. HDR을 켜 두었다면 끄고 다시 시도해 주세요.","dim",4);limitInside.Visible=false;col.Controls.Add(limitInside);
   limitFold.Toggled+=()=>limitInside.Visible=limitFold.Open;
   col.Controls.Add(Para("기존 기록은 그대로입니다.","dim",12));
   var ok=new Button(){Text="확인",Tag="primary"};ok.Click+=(_,_)=>ShowReady(null);
   Add(Stage.TooMany,col,ok);
  }
  // ── 끝(개발용) ──
  {
   var col=Column();col.Controls.Add(Head("전곡 읽기 완료"));
   doneLines=new WordLabel("",ContentWidth,9.75f){Margin=new(0,10,0,0)};col.Controls.Add(doneLines);
   col.Controls.Add(Para("이 검사에서는 읽기 결과만 보여 줍니다(개발용 화면). 결과는 개발용 폴더에 남습니다.","dim",10));
   var close=new Button(){Text="닫기"};close.Click+=(_,_)=>Close();
   Add(Stage.Finished,col,close);
  }
  // ── 전곡 읽기 완료(재확인도 이 화면) ──
  {
   var col=Column(DoneWidth);col.Controls.Add(Head("전곡 읽기 완료",DoneWidth));
   doneBand=new NoticeBand(){Size=new(DoneWidth,54),Margin=new(0,12,0,0)};doneBand.Visible=false;col.Controls.Add(doneBand);
   doneSummary=new Label(){AutoSize=true,UseMnemonic=false,Font=Theme.Px(AppFonts.Ui,14f,FontStyle.Bold),Margin=new(0,12,0,0)};col.Controls.Add(doneSummary);
   addFold=new FoldBar("추가·갱신할 점수 보기"){Size=new(DoneWidth,36),Margin=new(0,10,0,0)};col.Controls.Add(addFold);
   addTable=new DoneTable("추가·갱신할 점수"){Size=new(DoneWidth,DoneTable.RowHeight*3),Margin=Padding.Empty};addTable.Visible=false;col.Controls.Add(addTable);
   addFold.Toggled+=()=>OpenList(addFold.Open?AddsList:-1);
   deleteLine=new Label(){AutoSize=true,UseMnemonic=false,Font=Theme.Px(AppFonts.Ui,14f,FontStyle.Bold),Margin=new(0,18,0,2)};col.Controls.Add(deleteLine);
   foreach(var kind in CollectionDoneView.Order)
   {
    var header=new DoneHeader(kind,kind is DoneKind.VArchive or DoneKind.OldRate?"삭제":kind==DoneKind.Higher?"전체 선택":null,kind==DoneKind.Auto?"자동 삭제":null){Size=new(DoneWidth,36),Margin=new(0,8,0,0)};
    var table=new DoneTable(CollectionDoneView.Name(kind)){Size=new(DoneWidth,DoneTable.RowHeight*3),Margin=Padding.Empty};table.Visible=false;
    header.FoldToggled+=()=>OpenList(header.Open?(int)kind:-1);header.CheckToggled+=()=>Tick(kind);
    table.IsPicked=p=>choices.Higher.Contains(p);table.Toggled+=Pick;
    headers[kind]=header;tables[kind]=table;col.Controls.Add(header);col.Controls.Add(table);
   }
   var apply=new Button(){Text="적용",Tag="primary"};apply.Click+=(_,_)=>Apply();
   var cancel=new Button(){Text="취소"};cancel.Click+=(_,_)=>CancelDone();
   backFromDone=new Button(){Text="검수로 돌아가기"};backFromDone.Click+=(_,_)=>BackToReview();
   AddWithBack(Stage.Complete,col,DoneWidth,backFromDone,apply,cancel);
  }
  // ── 판독 검수 ──
  {
   var col=Column(ReviewWidth);col.Controls.Add(Head("판독 검수",ReviewWidth));
   col.Controls.Add(new WordLabel("캡처나 게임 화면을 보고 곡과 점수를 확인해 주세요.",ReviewWidth,9.75f,"dim"){Margin=new(0,8,0,0)});
   reviewHead=new ReviewHead(){Size=new(ReviewWidth,36),Margin=new(0,12,0,0)};col.Controls.Add(reviewHead);
   reviewRows=new FlowLayoutPanel(){FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Size=new(ReviewWidth,160),Margin=Padding.Empty};col.Controls.Add(reviewRows);
   reviewProtect=new WordLabel("",ReviewWidth,9.75f,"accent"){Margin=new(0,10,0,0)};reviewProtect.Visible=false;col.Controls.Add(reviewProtect);
   reviewNotFound=new WordLabel(CollectionReview.NotFound,ReviewWidth,9.75f,"accent"){Margin=new(0,6,0,0)};reviewNotFound.Visible=false;col.Controls.Add(reviewNotFound);
   reviewShaky=new WordLabel(CollectionReview.Shaky,ReviewWidth,9.75f,"accent"){Margin=new(0,6,0,0)};reviewShaky.Visible=false;col.Controls.Add(reviewShaky);
   reviewStatus=new Label(){AutoSize=true,Tag="dim",UseMnemonic=false,Margin=new(0,8,0,0)};col.Controls.Add(reviewStatus);
   reviewConfirm=new Button(){Text="확인",Tag="primary"};reviewConfirm.Click+=(_,_)=>ConfirmReview();
   var cancel=new Button(){Text="취소"};cancel.Click+=(_,_)=>CancelReview();
   Add(Stage.Review,col,ReviewWidth,reviewConfirm,cancel);
  }
  // ── 변경할 기록이 없음 ──
  {
   var col=Column();col.Controls.Add(Head("전곡 읽기 완료"));
   col.Controls.Add(Big("변경할 기록이 없습니다.",10));
   var close=new Button(){Text="닫기"};close.Click+=(_,_)=>Close();
   backFromNothing=new Button(){Text="검수로 돌아가기"};backFromNothing.Click+=(_,_)=>BackToReview();
   AddWithBack(Stage.Nothing,col,ContentWidth,backFromNothing,close);
  }
  Show(Stage.Ready);
  FormClosing+=(_,_)=>{game?.AskStop();hotkeys?.Dispose();hotkeys=null;};
 }

 // ── 부품 ──
 static FlowLayoutPanel Column(int width=ContentWidth)=>new(){AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,FlowDirection=FlowDirection.TopDown,WrapContents=false,Margin=new(Side,0,Side,16),MinimumSize=new(width,0)};
 static SectionHeader Head(string text,int width=ContentWidth)=>new(text){Keyed=true,Width=width,Margin=Padding.Empty};
 static WordLabel Big(string text,int top)=>new(text,ContentWidth,13f,"strong"){Margin=new(0,top,0,0)};
 static WordLabel Para(string text,string tone,int top)=>new(text,ContentWidth,9.75f,tone){Margin=new(0,top,0,0)};
 static Label PathWord(string text)=>new(){Text=text,AutoSize=true,UseMnemonic=false,Font=Theme.Px(AppFonts.Latin,9.5f,FontStyle.Bold),Margin=new(0,4,0,0)};
 static Label PathMark()=>new(){Text="›",AutoSize=true,Tag="dim",UseMnemonic=false,Font=Theme.Px(AppFonts.Ui,10f,FontStyle.Bold),Margin=new(6,3,6,0)};
 /// <summary>옅은 상자 안에 줄마다 이름(왼쪽)과 키 모양(오른쪽 끝)</summary>
 static Control KeyBox(int top,params (string Name,string[] Keys)[] rows)=>KeyBoxes.Make(top,ContentWidth,rows);
 void Add(Stage stage,FlowLayoutPanel body,params Button[] buttons)=>Add(stage,body,ContentWidth,buttons);
 void Add(Stage stage,FlowLayoutPanel body,int width,params Button[] buttons)=>AddWithBack(stage,body,width,null,buttons);
 /// <summary>단계의 본문과 아래 단추 줄. back 이 있으면 단추 줄의 왼쪽 끝에 둔다(판독 검수로 돌아가기. 탭 차례는 그것이 먼저).
 /// 이름을 Add 와 따로 둔다: 같은 이름이면 Add(…, 확인, 취소)가 '확인'을 왼쪽 단추로 받는다</summary>
 void AddWithBack(Stage stage,FlowLayoutPanel body,int width,Button? back,params Button[] buttons)
 {
  var band=new FlowLayoutPanel(){AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,FlowDirection=FlowDirection.TopDown,WrapContents=false,Margin=Padding.Empty};
  band.Controls.Add(new Panel(){Size=new(width+Side*2,1),Margin=Padding.Empty,Tag="line"});
  var footer=Parts.Footer(width,buttons);footer.Margin=new(Side,12,Side,12);band.Controls.Add(footer);
  if(back!=null)
  {
   back.AutoSize=false;back.MinimumSize=new(70,27);back.Font=Theme.Px(AppFonts.Ui,9.75f);back.Size=new(Math.Max(70,TextRenderer.MeasureText(back.Text,back.Font).Width+22),27);back.Location=Point.Empty;
   foreach(var button in buttons)button.TabIndex++;
   back.TabIndex=0;footer.Controls.Add(back);
  }
  Body.Controls.Add(body);Body.Controls.Add(band);stages[stage]=(body,band);
 }
 void Show(Stage stage)
 {
  Now=stage;var (body,footer)=stages[stage];
  // 판독 검수를 거쳐 온 완료·변경 0건 화면에만 '검수로 돌아가기'
  if(backFromDone!=null)backFromDone.Visible=fromReview;if(backFromNothing!=null)backFromNothing.Visible=fromReview;
  foreach(var (b,f) in stages.Values.Distinct()){bool on=ReferenceEquals(b,body);b.Visible=on;f.Visible=on;}
  if(IsHandleCreated){PerformLayout();Invalidate(true);}
  Hotkeys();
 }
 // 시작은 기다릴 때만, 중지는 읽는 동안만 듣는다(다른 때에 그 키를 가로채지 않는다)
 void Hotkeys()
 {
  if(hotkeys==null)return;
  bool reading=Now is Stage.Climbing or Stage.Reading;
  hotkeys.SetStop(reading);
  if(!hotkeys.SetStart(Now==Stage.Ready))ShowReason("Ctrl+Enter를 다른 프로그램이 쓰고 있어 시작 단축키를 켜지 못했습니다. 그 프로그램을 끈 뒤 이 창을 다시 열어 주세요.");
 }
 void ShowReason(string? reason){readyReason.Text=reason??"";readyReason.Visible=reason!=null;if(IsHandleCreated)PerformLayout();}
 internal void ShowReady(string? reason){ShowReason(reason);Show(Stage.Ready);}

 protected override void OnShown(EventArgs e)
 {
  base.OnShown(e);
  hotkeys=new ScanHotkeys(Handle);Hotkeys();
 }
 protected override void WndProc(ref Message m)
 {
  if(m.Msg==ScanHotkeys.Message)
  {
   int id=(int)m.WParam;
   if(id==ScanHotkeys.StartId&&Now==Stage.Ready)Start();
   else if(id==ScanHotkeys.StopId)game?.AskStop();
   return;
  }
  base.WndProc(ref m);
 }

 // ── 시작 ──
 /// <summary>게임이 앞 창이고 화면이 읽을 수 있는 크기(CollectionGame.Readable)이고 키를 보낼 수 있을 때만 읽기를 시작한다. 아니면 까닭을 적고 기다린다</summary>
 internal void Start()
 {
  IScanWorld? world;
  if(WorldForTest!=null)world=WorldForTest();
  else
  {
   var window=CollectionGame.Find();
   if(window==IntPtr.Zero){ShowReady("게임 창을 찾지 못했습니다. 게임을 켠 뒤 ALL 목록에서 Ctrl+Enter를 눌러 주세요.");return;}
   var real=new CollectionGame(window);
   if(!real.Active){ShowReady("게임 창이 앞에 있을 때 Ctrl+Enter를 눌러 주세요.");return;}
   if(real.KeysBlocked){ShowReady("게임이 관리자 권한으로 실행 중이라 키를 보낼 수 없습니다. 게임을 보통 권한으로 다시 켜 주세요.");return;}
   if(real.Capture()==null){ShowReady(real.LastSize.IsEmpty?"게임 화면을 읽지 못했습니다. 게임 창이 최소화되지 않았는지 확인해 주세요.":Unreadable(real.LastSize));return;}
   game=real;world=real;LastWorld=real;
  }
  if(world==null)return;
  startProfile=store?.Active.Id;   // 결과는 읽기를 시작한 프로필의 것이다(적용할 때 지금 프로필과 다르면 저장하지 않는다)
  // 중지 단축키를 켤 수 없으면 읽기 시작 전에 알린다(창의 중지 단추와 창 전환은 쓸 수 있지만 안내한 키가 듣지 않는다)
  if(hotkeys!=null&&!hotkeys.SetStop(true)){game=null;ShowReady(StopKeyTaken);return;}
  Climbing();
  int allowed=CollectionScan.Allowed(catalog);var scan=new CollectionScan(new CollectionReader(catalog,data),world,allowed);   // 허용 수는 읽기 전에 멈춘 결과에도 넣는다(진단 기록의 한 줄에 쓴다)
  scan.Progress=(count,title)=>Post(()=>Reading(count,title));scan.Trace=TraceForTest;scan.Closing=ClosingForTest;
  var keys=game;   // 시작 단축키를 아직 누르고 있으면 뗄 때까지 기다린다(누른 채로 보내는 ↑ 는 게임에 Ctrl+↑ 로 들어간다)
  worker=new Thread(()=>{ScanResult result;try{if(keys!=null){double waited=keys.WaitKeysUp();TraceForTest?.Invoke($"시작 키를 뗄 때까지 {waited*1000:0}ms");}result=KeysHeldForTest?.Invoke()??(keys!=null&&CollectionGame.KeysHeld)?new ScanResult{End=ScanEnd.KeysHeld,Detail="시작 키를 뗄 때까지 기다리는 한도가 지났다",Allowed=allowed}:scan.Run();}catch(Exception error){result=new ScanResult{End=ScanEnd.NoScreen,Detail="읽기 도중 오류: "+error.Message,Allowed=allowed};}try{ScanEnded?.Invoke(result);}catch{}Post(()=>Done(result));}){IsBackground=true,Name="컬렉션 읽기"};
  worker.Start();
 }
 /// <summary>읽을 수 없는 크기일 때의 안내. 작으면 해상도를, 크지만 16:9 가 아니면(모니터가 16:9 가 아닌 보더리스·전체 화면) 창 모드를 권한다</summary>
 static string Unreadable(Size size)
 {
  string head=$"게임 화면의 크기({size.Width}×{size.Height})가 맞지 않아 시작하지 않았습니다. ";
  return size.Width<CollectionFrame.Width||size.Height<CollectionFrame.Height?head+"게임 해상도를 1920×1080으로 설정해 주세요.":head+"화면이 16:9가 아닙니다. 게임 해상도를 1920×1080으로 설정하고 창 모드로 바꿔 주세요.";
 }
 /// <summary>MUSIC DATA 화면으로 보이지 않아 시작하지 않았을 때의 안내. large 는 1920×1080 보다 큰 16:9 를 줄여 읽은 화면의 크기다.
 /// 게임 해상도를 모니터 크기(예: 2560×1440)로 두면 MUSIC DATA 화면이어도 줄인 글자가 가늘어 이 검사에서 막힌다(실제 게임에서 본 것): 그때는 해상도도 안내한다</summary>
 internal static string WrongScreen(Size? large)=>large!=null
  ?"MUSIC DATA 화면을 찾지 못해 시작하지 않았습니다. 아래 경로의 ALL 목록을 열어 주세요. 이미 열려 있다면 게임 설정에서 해상도를 1920×1080으로 바꿔 주세요."
  :"MUSIC DATA 화면을 찾지 못해 시작하지 않았습니다. 아래 경로의 ALL 목록을 열어 주세요.";
 internal const string NotAll="ALL 목록이 아니어서 시작하지 않았습니다. 게임에서 필터(Space)를 ALL로 바꾼 뒤 Ctrl+Enter를 눌러 주세요.";
 internal const string FilterChanged="필터가 ALL에서 바뀌어 가져오기를 멈췄습니다. 게임에서 필터(Space)를 ALL로 바꾼 뒤 Ctrl+Enter를 눌러 주세요. 첫 곡부터 다시 읽습니다.";
 internal const string KeysStillHeld="키를 누르고 있어 시작하지 않았습니다. 키를 모두 뗀 뒤 다시 Ctrl+Enter를 눌러 주세요.";
 internal const string StopKeyTaken="Backspace를 다른 프로그램이 쓰고 있어 중지 단축키를 켜지 못했습니다. 그 프로그램을 끈 뒤 다시 Ctrl+Enter를 눌러 주세요.";
 internal void Climbing(){ShowReason(null);runHead.Text="첫 곡 찾는 중";climbLine.Visible=true;runTitle.Visible=false;countBox.Visible=false;runNote.Visible=false;Show(Stage.Climbing);}
 void Post(Action action){try{if(!IsDisposed&&IsHandleCreated)BeginInvoke(action);}catch(InvalidOperationException){}}
 internal void Reading(int count,string title)
 {
  if(Now is not (Stage.Climbing or Stage.Reading))return;
  if(Now==Stage.Climbing){runHead.Text="점수 읽는 중";climbLine.Visible=false;runTitle.Visible=true;countBox.Visible=true;runNote.Visible=true;Show(Stage.Reading);}
  runTitle.Text=title;countValue.Text=$"{count}곡";
 }
 internal void Done(ScanResult result)
 {
  if(IsDisposed)return;
  var real=game;game=null;worker=null;if(real!=null)LastWorld=real;
  EndedForTest?.Invoke(result);
  switch(result.End)
  {
   case ScanEnd.Done:
    if(store!=null){Finished?.Invoke(result);if(result.Questions==0)Begin(result);else OpenReview(result);Notify();break;}   // 확인할 것이 있으면 판독 검수부터
    doneLines.Text=$"{result.Items.Count}곡을 읽었습니다. 확인이 필요한 항목 {result.Questions}건 · 걸린 시간 {TimeSpan.FromSeconds(result.Seconds):m\\:ss}";
    Show(Stage.Finished);Finished?.Invoke(result);Notify();break;
   case ScanEnd.Cancelled:
    ShowReady(result.Detail=="중지"?"읽기를 중지했습니다. 다시 시작하면 첫 곡부터 읽습니다.":"다른 창으로 전환되어 가져오기가 취소되었습니다. 다시 시작하면 첫 곡부터 읽습니다.");Notify();break;
   case ScanEnd.NoScreen:
    ShowReady("게임 화면을 읽지 못해 가져오기를 중단했습니다. 게임 화면이 1920×1080 이상의 16:9인지, 게임 창이 최소화되지 않았는지 확인해 주세요.");Notify();break;
   case ScanEnd.WrongScreen:
    ShowReady(WrongScreen(real is{Shrunk:true}?real.LastSize:null));break;
   case ScanEnd.NotAll:
    ShowReady(NotAll);break;
   case ScanEnd.FilterChanged:
    ShowReady(FilterChanged);Notify();break;
   case ScanEnd.KeysHeld:
    ShowReady(KeysStillHeld);break;
   case ScanEnd.Empty:
    Stopped("게임의 ALL 목록에서 곡을 찾지 못했습니다.",null,null);Notify();break;
   case ScanEnd.UnknownPattern:
    Stopped("프로그램의 곡 목록에 없는 패턴이 있습니다.","중단한 곡",result.LastTitle,"메인 창에서 ‘업데이트 확인’을 눌러 새 버전이 있는지 봐 주세요.");Notify();break;
   case ScanEnd.TooMany:
    limitCounts.Text=$"확인 필요 {result.Questions}건({result.Allowed}건까지만 넘어가며 읽습니다)\n곡 확인 {result.SongQuestions}건 · 점수 확인 {result.CellQuestions}건";
    limitFold.Set(false);limitInside.Visible=false;Show(Stage.TooMany);Notify();break;
   case ScanEnd.NotFirst:   // 첫 곡까지 올라갔는지 모른다: 곡 상자 없이
    Stopped("첫 곡으로 이동했는지 확인할 수 없습니다.",null,null);Notify();break;
   default:   // NotMoved
    Stopped("다음 곡으로 이동했는지 확인할 수 없습니다.","마지막으로 확인한 곡",result.LastTitle);Notify();break;
  }
 }
 /// <summary>마지막으로 읽기를 돌린 게임(개발용 도구가 보낸 키의 수를 적는다)</summary>
 internal CollectionGame? LastWorld{get;private set;}
 void Stopped(string headline,string? name,string? value,string? next=null)
 {
  stopHead.Text=headline;
  bool box=name!=null&&!string.IsNullOrEmpty(value);stopBox.Visible=box;   // 위의 칸이 아직 숨어 있으면 Visible 은 거짓으로 읽힌다: 따로 둔 값으로 본다
  if(box){stopName.Text=name;stopValue.Left=stopName.Left+stopName.PreferredWidth+16;stopValue.Width=stopBox.Width-stopValue.Left-14;stopValue.Text=value;}
  stopNext.Text=next??"다시 시도하려면 이 창을 닫고 ‘컬렉션 가져오기’를 다시 연 뒤, 게임의 ALL 목록에서 Ctrl+Enter를 눌러 주세요. 첫 곡부터 다시 읽습니다.";
  Show(Stage.Stopped);
 }

 // ── 전곡 읽기 완료 ──
 /// <summary>확인할 것이 없는 끝까지 읽은 결과로 완료 화면을 연다(바뀔 것이 없으면 '변경할 기록이 없습니다.')</summary>
 void Begin(ScanResult result)
 {
  try{Prepare(CollectionOutcome.From(result,startProfile??store!.Active.Id));}
  catch(Exception error){Forget();ShowReady(null);SaveFailed(error);}
 }
 void Prepare(CollectionOutcome read)
 {
  outcome=read;charts=CollectionPlan.Charts(catalog);choices=new CollectionChoices();recheckBefore=null;
  if(store!.Active.Id!=read.ProfileId)throw new Exception(RecordStore.CollectionProfileChanged);
  seen=CollectionPlan.Make(store,charts,read);
  if(CollectionDoneView.Nothing(seen,choices)){Forget();Show(Stage.Nothing);return;}
  ShowComplete(null,0);
 }
 /// <summary>개발용 검사와 창 그림: 읽기 없이 이 결과로 완료 화면을 연다</summary>
 internal void ShowDoneForTest(CollectionOutcome read){startProfile=read.ProfileId;Prepare(read);}
 /// <summary>완료 화면의 취소: 읽은 값과 고른 것을 버리고 준비 화면으로</summary>
 internal void CancelDone(){DropReview();Forget();ShowReady(null);}
 /// <summary>검수를 버린다: 검수 줄과 그 줄이 붙잡은 캡처(PNG)도 함께. '검수로 돌아가기'를 쓸 수 있는 동안에는 부르지 않는다</summary>
 void DropReview(){review=null;fromReview=false;foreach(var old in lines){reviewRows.Controls.Remove(old);old.Dispose();}lines.Clear();}
 /// <summary>완료·변경 0건 화면에서 판독 검수로 돌아간다: 검수에 적은 것은 그대로, 셈과 삭제 선택은 버린다(다시 확인하면 새로 셈한다)</summary>
 internal void BackToReview(){if(review==null)return;Forget();Show(Stage.Review);RefreshReview();}
 Button? backFromDone,backFromNothing;bool fromReview;
 internal Button? BackFromDone=>backFromDone;internal Button ReviewConfirm=>reviewConfirm;internal Button? BackFromNothing=>backFromNothing;
 void Forget(){outcome=null;seen=null;recheckBefore=null;choices=new CollectionChoices();}
 /// <summary>완료 화면을 지금의 셈(seen)으로 그린다. before 가 있으면 재확인이다(띠를 보이고, 바뀐 줄이 든 항목을 펼친다)</summary>
 void ShowComplete(CollectionPlan? before,int changed)
 {
  recheckBefore=before;var plan=seen!;
  doneBand.Visible=before!=null;
  doneSummary.Text=$"추가 {plan.AddCount}건   갱신 {plan.UpdateCount}건";
  var adds=CollectionDoneView.Adds(plan,catalog);addTable.Set(adds);addFold.Visible=adds.Count>0;
  var groups=Regroup();
  // 띠의 건수는 바뀐 판의 수다(두 항목에 보이는 판은 한 번, 사라진 판도 든다). 판으로 보이지 않는 변화(더할 것만 달라짐 등)뿐이면 달라진 패턴의 수를 쓴다
  if(before!=null){int rows=CollectionDoneView.ChangedPlays(before,plan,choices);doneBand.Second="바뀐 점수를 확인한 뒤 삭제할 기록을 선택해 주세요.";doneBand.Text=$"검수 중 기록 {(rows>0?rows:changed)}건이 바뀌었습니다.";}
  Show(Stage.Complete);
  int open=-1,at=-1;if(before!=null){var first=groups.FirstOrDefault(g=>g.Rows.Any(r=>r.Was!=null));if(first!=null){open=(int)first.Kind;at=first.Rows.ToList().FindIndex(r=>r.Was!=null);}}
  OpenList(open);
  // 펼친 목록에서 첫 번째 바뀐 줄이 보이게 굴린다. 목록의 높이를 맞춘 뒤에
  if(open>=0&&at>=0&&IsHandleCreated){var table=tables[(DoneKind)open];BeginInvoke(()=>{if(!table.IsDisposed&&at<table.Rows.Count)table.EnsureVisible(at);});}
 }
 /// <summary>항목의 건수·체크·줄과 삭제 예정 건수를 지금의 고른 것으로 다시 적는다(펼친 것은 그대로)</summary>
 IReadOnlyList<DoneGroup> Regroup()
 {
  var plan=seen!;var groups=CollectionDoneView.Groups(plan,choices,catalog,recheckBefore,store?.Active.Plays);
  foreach(var kind in CollectionDoneView.Order)
  {
   var group=groups.FirstOrDefault(g=>g.Kind==kind);var header=headers[kind];header.Visible=group!=null;
   var check=kind switch{DoneKind.VArchive=>choices.DeleteVArchive?CheckState.Checked:CheckState.Unchecked,DoneKind.OldRate=>choices.DeleteOldRate?CheckState.Checked:CheckState.Unchecked,DoneKind.Higher=>CollectionDoneView.Picked(plan,choices),_=>CheckState.Unchecked};
   header.Set(group?.Rows.Count(r=>!r.Gone)??0,check);tables[kind].Set(group?.Rows??Array.Empty<DoneRow>());
   // 열려 있던 항목이 사라지면 표도 숨기고 창 높이를 다시 맞춘다
   if(group==null&&openList==(int)kind){openList=-1;tables[kind].Visible=false;Relayout();}
   if(group==null)header.SetOpen(false);   // 다시 나타나면 접힌 채로(첫 누름에 펴진다)
  }
  deleteLine.Text=$"삭제 예정 {plan.DeleteCount(choices)}건";
  if(openList>=0)Fit(openList);
  return groups;
 }
 /// <summary>목록 하나만 펼친다(같은 것을 다시 누르면 접힌다). 펼친 목록의 높이는 줄 수와 화면의 남은 높이에 맞춘다</summary>
 void OpenList(int which)
 {
  openList=which;
  addFold.Set(which==AddsList);addTable.Visible=which==AddsList&&addTable.Rows.Count>0;
  foreach(var kind in CollectionDoneView.Order){bool on=which==(int)kind;headers[kind].SetOpen(on);tables[kind].Visible=on&&tables[kind].Rows.Count>0;}
  Relayout();   // 창의 높이를 먼저 맞춘다(안쪽 칸이 다시 배치되기 전의 높이로 셈하면 남은 높이가 작게 나온다)
  if(which>=0)Fit(which);
  Relayout();if(IsHandleCreated)KeepOnScreen();
 }
 /// <summary>완료 화면의 칸 → 창의 몸 → 창의 차례로 다시 배치한다(창의 PerformLayout 만으로는 안쪽의 AutoSize 가 늦게 맞춰진다)</summary>
 void Relayout(Stage stage=Stage.Complete){if(!IsHandleCreated)return;stages[stage].Body.PerformLayout();Body.PerformLayout();PerformLayout();}
 /// <summary>펼친 목록의 높이: MaxRows 줄까지 보이고 그 안에서 굴린다. 창이 배치된 뒤(Shrink) 작업 영역을 넘으면 넘지 않을 만큼 줄인다(적어도 세 줄)</summary>
 void Fit(int which)
 {
  var table=which==AddsList?addTable:tables[(DoneKind)which];int count=table.Rows.Count;if(count==0)return;
  table.Height=Math.Min(count,MaxRows)*Theme.S(table.RowTall)+2;
  if(IsHandleCreated)BeginInvoke(Shrink);
 }
 const int MaxRows=8;
 void Shrink()
 {
  if(IsDisposed||openList<0)return;var table=openList==AddsList?addTable:tables[(DoneKind)openList];if(!table.Visible)return;
  // 창이 작업 영역을 넘으면 목록을 줄인다. 창의 높이는 Windows 가 화면 크기로 막아 실제보다 작게 읽힐 수 있다:
  // 내용이 바라는 높이(PreferredSize)로 견주고, 단추까지 들어갈 때까지 한 줄까지 줄인다
  int row=Theme.S(table.RowTall),count=table.Rows.Count;var area=Screen.FromControl(this).WorkingArea;
  for(int i=0;i<3;i++)
  {
   int over=Math.Max(Height,PreferredSize.Height)-area.Height;if(over<=0)break;
   int rows=Math.Max(1,(table.Height-2-over)/row);if(rows*row+2>=table.Height)break;
   table.Height=rows*row+2;Relayout();
  }
  LastFit=$"줄 {count}, 보인 줄 {(table.Height-2)/row}, 창 {Height}, 작업 영역 {area.Height}";
  KeepOnScreen();
 }
 /// <summary>개발용: 마지막으로 펼친 목록의 높이를 셈한 값</summary>
 internal string LastFit="";
 /// <summary>창이 커져 작업 영역 밖으로 나가면 안으로 들인다</summary>
 void KeepOnScreen()
 {
  var area=Screen.FromControl(this).WorkingArea;
  int left=Math.Max(area.Left,Math.Min(Left,area.Right-Width)),top=Math.Max(area.Top,Math.Min(Top,area.Bottom-Height));
  if(left!=Left||top!=Top)Location=new(left,top);
 }
 void Tick(DoneKind kind)
 {
  var plan=seen;if(plan==null)return;
  switch(kind)
  {
   case DoneKind.VArchive:choices.DeleteVArchive=!choices.DeleteVArchive;break;
   case DoneKind.OldRate:choices.DeleteOldRate=!choices.DeleteOldRate;break;
   case DoneKind.Higher:
    var visible=plan.Visible(choices).Select(h=>h.Row).ToList();
    if(CollectionDoneView.Picked(plan,choices)==CheckState.Checked)foreach(var row in visible)choices.Higher.Remove(row);else foreach(var row in visible)choices.Higher.Add(row);
    break;
   default:return;
  }
  Regroup();
 }
 void Pick(Play row){if(seen==null)return;if(!choices.Higher.Remove(row))choices.Higher.Add(row);Regroup();}
 /// <summary>적용: RecordStore.ImportCollection 이 지금 기록으로 다시 셈해 견준다. 같으면 저장하고 창을 닫는다, 다르면 재확인, 바뀔 것이 없으면 '변경할 기록이 없습니다.'</summary>
 internal void Apply()
 {
  if(store==null||outcome==null||seen==null||charts==null)return;
  CollectionSaveResult result;
  try{result=store.ImportCollection(outcome,charts,seen,choices);}
  catch(Exception error){SaveFailed(error);return;}   // 기록과 직전 되돌리기는 그대로. 읽은 값·고른 것도 그대로 두고 완료 화면에 남는다
  switch(result.State)
  {
   case CollectionSave.Saved:Applied?.Invoke(result);Forget();Close();break;
   case CollectionSave.Nothing:Forget();Show(Stage.Nothing);break;
   default:   // 검수 중에 기록이 바뀌었다: 바뀐 줄의 체크만 풀린다(고른 것은 판 전체로 기억한다. 같은 패턴의 다른 줄은 그대로)
    var before=seen;seen=result.Now;CollectionDoneView.Keep(choices,seen);ShowComplete(before,result.Changed.Count);break;
  }
 }
 /// <summary>저장하지 못했다는 안내('저장 실패', '돌아가기'). 까닭과 다음 행동은 저장 실패의 분류를 쓴다</summary>
 void SaveFailed(Exception error)
 {
  var (reason,before)=error.Message==RecordStore.CollectionProfileChanged?(error.Message,(string?)null):SaveTrouble.Of(error);
  var parts=new List<GuidePart>{new GuideText(reason,"strong"),new GuideText("기존 기록은 그대로입니다.","dim")};
  if(before!=null)parts.Add(new GuideText($"{before} ‘적용’을 다시 눌러 주세요."));
  GuideDialog.Show(this,new Guide(Text,"저장 실패",parts,new[]{"돌아가기"}));
 }
 // 개발용 검사가 완료 화면을 다룬다
 internal DoneHeader HeaderFor(DoneKind kind)=>headers[kind];
 internal DoneTable TableFor(DoneKind kind)=>tables[kind];
 internal FoldBar AddFold=>addFold;internal DoneTable AddTable=>addTable;
 internal string DoneText=>$"{doneSummary.Text} | {deleteLine.Text} | {(doneBand.Visible?doneBand.Text+" "+doneBand.Second:"")}";
 internal CollectionChoices DoneChoices=>choices;internal CollectionPlan? DonePlan=>seen;internal int OpenListForTest=>openList;

 // ── 판독 검수 ──
 /// <summary>확인할 것이 있는 끝까지 읽은 결과로 판독 검수를 연다. 표의 줄은 이 창의 배수로 만든다(창이 뜬 뒤라 Enlarge 를 거치지 않는다)</summary>
 void OpenReview(ScanResult result)
 {
  review=CollectionReview.From(result);fromReview=false;
  reviewRows.SuspendLayout();
  foreach(var old in lines){reviewRows.Controls.Remove(old);old.Dispose();}lines.Clear();
  foreach(var group in review.Groups)
  {
   string title=group.Fixed==null?"":OverlayText.Title(catalog,group.Fixed);int shots=Shots(group).Count;
   foreach(var row in group.Rows){var line=new ReviewLine(row,Unit,title,shots,RefreshReview,PickSong,ShowCapture);lines.Add(line);reviewRows.Controls.Add(line);}
  }
  reviewRows.ResumeLayout();
  Theme.Apply(this);   // 새로 만든 줄에 색과 모양을 입힌다
  foreach(var line in lines)line.BackColor=Theme.Table;
  Show(Stage.Review);RefreshReview();
 }
 /// <summary>모델의 지금 값으로 줄·안내·건수·확인 단추를 맞춘다</summary>
 void RefreshReview()
 {
  if(review==null)return;
  reviewRows.SuspendLayout();foreach(var line in lines)line.Sync(review);reviewRows.ResumeLayout();
  var protect=review.ProtectNote;reviewProtect.Text=protect??"";reviewProtect.Visible=protect!=null;
  reviewNotFound.Visible=review.NotFoundNote!=null;reviewShaky.Visible=review.ShakyNote!=null;
  reviewStatus.Text=review.Status;reviewConfirm.Enabled=review.CanConfirm;
  FitRows();
 }
 /// <summary>표의 높이: 줄을 모두 보이되, 창이 배치된 뒤 작업 영역을 넘으면 넘지 않을 만큼 줄이고 그 안에서 굴린다(적어도 가장 낮은 줄 하나의 높이. 완료 화면의 펼친 목록(Shrink)과 같은 규칙)</summary>
 void FitRows()
 {
  int total=lines.Sum(l=>l.Height)+2;if(reviewRows.Height!=total)reviewRows.Height=total;
  Relayout(Stage.Review);
  if(IsHandleCreated)BeginInvoke(()=>
  {
   if(IsDisposed||Now!=Stage.Review)return;
   // 안내 글의 줄 수는 그려질 때에야 정해질 수 있다: 배치된 뒤의 줄 높이로 다시 맞춘다
   int now=lines.Sum(l=>l.Height)+2;if(reviewRows.Height!=now){reviewRows.Height=now;Relayout(Stage.Review);}
   for(int i=0;i<3;i++)
   {
    int over=Math.Max(Height,PreferredSize.Height)-Screen.FromControl(this).WorkingArea.Height;if(over<=0)break;
    int height=Math.Max(Theme.S(52),reviewRows.Height-over);if(height>=reviewRows.Height)break;
    reviewRows.Height=height;Relayout(Stage.Review);
   }
   KeepOnScreen();
  });
 }
 static List<CaptureViewer.Shot> Shots(ReviewGroup group)=>CaptureViewer.For(group);
 void PickSong(ReviewGroup group)
 {
  using var picker=new SongPicker(catalog,group.Candidates,group.Chosen);
  if(PickerForTest!=null){PickerForTest(picker);return;}
  if(picker.ShowDialog(this)!=DialogResult.OK)return;
  if(picker.Cleared)group.Chosen=null;else if(picker.Picked!=null)group.Chosen=picker.Picked;
  RefreshReview();
 }
 void ShowCapture(ReviewRow row)
 {
  var shots=Shots(row.Group);if(shots.Count==0)return;
  string read=row.Kind==ReviewKind.Song&&int.TryParse(row.Read,out int v)?$"{v:N0}":"—";
  using var viewer=new CaptureViewer(row.Pattern,read,shots);
  if(ViewerForTest!=null){ViewerForTest(viewer);return;}
  viewer.ShowDialog(this);
 }
 /// <summary>확인: 검수의 결과로 전곡 읽기 완료 화면을 연다(저장은 완료 화면의 '적용'이 한다)</summary>
 internal void ConfirmReview()
 {
  if(review==null||!review.CanConfirm||store==null)return;
  var read=review.Outcome(startProfile??store.Active.Id);fromReview=true;   // 검수는 버리지 않는다: 완료 화면에서 '검수로 돌아가기'
  try{Prepare(read);}catch(Exception error){DropReview();Forget();ShowReady(null);SaveFailed(error);}
 }
 /// <summary>취소: 읽고 입력한 것을 버리고 준비 화면으로</summary>
 internal void CancelReview(){DropReview();Forget();ShowReady(null);}
 // 개발용 검사가 판독 검수를 다룬다
 /// <summary>개발용 검사와 창 그림: 읽기 없이 이 결과로 판독 검수를 연다</summary>
 internal void ShowReviewForTest(ScanResult result){startProfile=store?.Active.Id;OpenReview(result);}
 internal CollectionReview? ReviewForTest=>review;internal IReadOnlyList<ReviewLine> LinesForTest=>lines;
 internal string ReviewText=>$"{reviewStatus.Text} | {(reviewProtect.Visible?reviewProtect.Text:"")} | {(reviewNotFound.Visible?reviewNotFound.Text:"")} | {(reviewShaky.Visible?reviewShaky.Text:"")} | 확인 {(reviewConfirm.Enabled?"됨":"막힘")}";
 /// <summary>개발용 검사: 곡 선택 창·캡처 보기 창을 띄우는 대신 받는다(창을 다룬 뒤 닫는다)</summary>
 [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
 internal Action<SongPicker>? PickerForTest{get;set;}
 [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
 internal Action<CaptureViewer>? ViewerForTest{get;set;}
 internal void Choose(ReviewGroup group,Song? song){group.Chosen=song;RefreshReview();}

 // ── 끝났을 때 알리기 ──
 [DllImport("user32.dll")]static extern bool FlashWindowEx(ref FlashInfo info);
 [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
 [DllImport("kernel32.dll")]static extern uint GetCurrentThreadId();
 [DllImport("user32.dll")]static extern bool AttachThreadInput(uint from,uint to,bool attach);
 [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr window);
 [StructLayout(LayoutKind.Sequential)]struct FlashInfo{public uint Size;public IntPtr Window;public uint Flags,Count,Timeout;}
 /// <summary>읽기가 끝났거나 멈췄음을 알린다: 창을 앞으로 가져오거나, 작업 표시줄에서 깜빡인다(앞 창이 될 때까지)</summary>
 void Notify()
 {
  try
  {
   if(FinishToFront)
   {
    if(WindowState==FormWindowState.Minimized)WindowState=FormWindowState.Normal;
    uint other=GetWindowThreadProcessId(GetForegroundWindow(),out _),me=GetCurrentThreadId();
    if(other!=0&&other!=me)AttachThreadInput(me,other,true);
    try{SetForegroundWindow(Handle);Activate();}finally{if(other!=0&&other!=me)AttachThreadInput(me,other,false);}
   }
   else{var info=new FlashInfo{Size=(uint)Marshal.SizeOf<FlashInfo>(),Window=Handle,Flags=3|12};FlashWindowEx(ref info);}   // 제목과 작업 표시줄 | 앞 창이 될 때까지
  }
  catch{}
 }
}
