using System.Runtime.InteropServices;
namespace LadderRecords;

// 공통 안내창.
// 업데이트·복구·실행의 안내와 확인이 함께 쓰는 한 창이다. 창마다 다른 것은 "무엇을 차례로 놓는가"뿐이라, 놓을 것을 값(Guide)으로 받아 그린다.
//  · 너비는 고정(100% 때 442)이고 높이는 내용에 맞춘다. 펼치는 줄을 펴면 창이 함께 늘어난다. 작업 영역에 다 들어가지 않으면 첫 줄과 아래 단추는 그대로 두고 본문만 굴린다
//  · 위에서부터: 사선이 붙은 첫 줄 → 놓을 것들(글, 경로 상자, 번호 붙은 절차, 주소, 펼치는 줄) → 선 → 오른쪽 아래의 단추
//  · 단추가 하나면 '확인'이고 확인·Enter·Esc·닫기가 모두 같다. 둘 이상이면 기본 선택이 오른쪽의 채운 단추다. 처음 초점이 그 단추라 연 직후의 Enter 는 기본 선택을 누르고(Tab 으로 초점을 옮기면 Enter 는 그 단추를 누른다), Esc 와 닫기는 언제나 기본 선택이다
//  · 창이 뜬 뒤에 만든 컨트롤은 창의 크기 단계만큼 커지지 않는다. 펼치는 내용도 처음에 다 만들어 두고 보이는 것만 바꾼다
//  · 시험 도구(tools\update_test.py)는 자식 창의 글자를 읽어 창을 찾는다. 이 창의 글은 모두 글자를 가진 보통의 컨트롤(Label, TextBox, Button)에 적는다

/// <summary>안내창에 차례로 놓는 것 하나</summary>
abstract record GuidePart;
/// <summary>글 한 문단. Tone: 보통("")·굵게("strong". 지금의 상태)·옅게("dim". 보존 안내, 다음 행동의 보조)</summary>
sealed record GuideText(string Text,string Tone=""):GuidePart;
/// <summary>경로 상자: 작은 항목 이름과 그 아래의 경로(줄을 바꿔 끝까지 보이고 골라 복사할 수 있다). 왼쪽에 테마색 띠</summary>
sealed record GuidePaths(params (string Name,string Path)[] Items):GuidePart;
/// <summary>번호 붙은 절차</summary>
sealed record GuideSteps(params string[] Steps):GuidePart;
/// <summary>옅은 이름과 그 아래의 주소(골라 복사할 수 있다)</summary>
sealed record GuideLink(string Name,string Url):GuidePart;
/// <summary>펼치는 줄('오류 내용' 등). 펴면 안의 것들이 보이고 창이 그만큼 늘어난다. 위에 선을 긋는다</summary>
sealed record GuideFold(string Title,params GuidePart[] Inside):GuidePart;
/// <summary>테두리 상자 안의 두 버전과 그 사이의 화살표(뒤의 것을 강조색으로), 선 아래에 이름과 값 한 줄(복구 확인의 '현재 버전 → 복구할 버전'과 '백업 시각')</summary>
sealed record GuideVersions(string FromName,string From,string ToName,string To,string? RowName=null,string? RowValue=null):GuidePart;

/// <summary>
/// 안내창 하나에 보일 것. Buttons 는 왼쪽부터의 단추 글자이고 Default 는 기본 선택의 번호다(창이 그 단추를 맨 오른쪽의 채운 단추로 놓는다).
/// 단추를 주지 않으면 '확인' 하나다
/// </summary>
sealed record Guide(string Title,string Headline,IReadOnlyList<GuidePart> Parts,IReadOnlyList<string>? Buttons=null,int Default=0)
{
 public IReadOnlyList<string> ButtonTexts=>Buttons is {Count:>0}?Buttons:new[]{"확인"};
 /// <summary>창에 보이는 글을 한 덩이로(개발용 검사와, 이 창을 띄우지 못했을 때 Windows 의 알림 창에 적는 글). 펼치는 줄의 안쪽도 든다</summary>
 public string Plain(){var lines=new List<string>{Headline};void Add(IEnumerable<GuidePart> parts){foreach(var part in parts)switch(part){
  case GuideText text:lines.Add(text.Text);break;
  case GuidePaths paths:foreach(var (name,path) in paths.Items){lines.Add(name);lines.Add(path);}break;
  case GuideSteps steps:for(int i=0;i<steps.Steps.Length;i++)lines.Add($"{i+1}. {steps.Steps[i]}");break;
  case GuideLink link:lines.Add(link.Name);lines.Add(link.Url);break;
  case GuideFold fold:lines.Add(fold.Title);Add(fold.Inside);break;
  case GuideVersions versions:lines.Add($"{versions.FromName} {versions.From} → {versions.ToName} {versions.To}");if(versions.RowName!=null)lines.Add($"{versions.RowName} {versions.RowValue}");break;}}
  Add(Parts);return string.Join("\n",lines);}
}

/// <summary>줄을 바꿔 끝까지 보이는 읽기 전용 글(경로, 긴 낱말이 든 문장). 마우스로 골라 복사할 수 있다. 높이는 줄 수에 맞춰 스스로 정한다</summary>
sealed class WrapBox:TextBox
{
 [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr window,int message,IntPtr w,IntPtr l);
 /// <summary>옅은 상자(경로 상자) 안에 놓였는가, 옅은 글자인가. 테마가 바탕과 글자의 색을 이것으로 정한다(Theme.Apply)</summary>
 public bool OnField,Dimmed;
 public WrapBox(string text,int width)
 {
  Multiline=true;ReadOnly=true;BorderStyle=BorderStyle.None;WordWrap=true;TabStop=false;ScrollBars=ScrollBars.None;Margin=Padding.Empty;Cursor=Cursors.IBeam;
  Width=width;Text=text.Replace("\r\n","\n").Replace("\n","\r\n");Fit();
 }
 // 줄 수는 화면에 뜬 뒤에야 Windows 에 물을 수 있다. 그 전에는 글의 길이로 어림한다(뜨면 다시 맞춘다)
 void Fit()
 {
  int lines=IsHandleCreated?Math.Max(1,(int)SendMessage(Handle,0x00BA,IntPtr.Zero,IntPtr.Zero)):Math.Max(1,(int)Math.Ceiling(TextRenderer.MeasureText(Text,Font,Size.Empty,TextFormatFlags.NoPadding|TextFormatFlags.SingleLine).Width/(double)Math.Max(1,Width)));
  int tall=lines*Font.Height+2;if(Height!=tall)Height=tall;
 }
 protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);Fit();}
 protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);Fit();}
 protected override void OnFontChanged(EventArgs e){base.OnFontChanged(e);Fit();}
 protected override void OnTextChanged(EventArgs e){base.OnTextChanged(e);Fit();}
}

/// <summary>경로 상자의 바탕: 옅은 바탕에 왼쪽의 테마색 띠</summary>
sealed class PathBlock:FlowLayoutPanel
{
 public PathBlock(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);AutoSize=true;AutoSizeMode=AutoSizeMode.GrowAndShrink;FlowDirection=FlowDirection.TopDown;WrapContents=false;Margin=Padding.Empty;Padding=new(15,9,12,10);}
 protected override void OnPaint(PaintEventArgs e)
 {
  if(BackColor!=Theme.Field)BackColor=Theme.Field;e.Graphics.Clear(Theme.Field);
  using var band=new SolidBrush(Theme.Stripe);e.Graphics.FillRectangle(band,0,0,Theme.S(3),Height);
 }
}

class GuideDialog:CompactDialog
{
 public const int ContentWidth=404,Side=18;
 /// <summary>개발용 검사가 창 대신 답하는 자리(보일 것을 받고, 누른 단추의 번호를 돌려준다)</summary>
 internal static Func<Guide,int>? Hook{get;set;}
 readonly Guide guide;readonly List<Button> buttons=new();readonly List<(FoldBar Bar,Control Inside)> folds=new();
 /// <summary>누른 단추의 번호(닫기·Esc 는 기본 선택)</summary>
 public int Chosen{get;private set;}
 /// <summary>펼치는 줄들(개발용 검사와 화면 검사가 펴 본다)</summary>
 internal IReadOnlyList<FoldBar> Folds=>folds.Select(fold=>fold.Bar).ToList();

 // 첫 줄 아래의 본문. 창이 작업 영역보다 길어지면 첫 줄과 아래 단추는 그대로 두고 이 칸만 굴린다
 readonly FlowLayoutPanel main,content;readonly VerticalScroller scroller=new();
 bool fitting;
 /// <summary>본문이 화면에 다 들어가지 않아 굴리는 중인가(개발용 검사)</summary>
 internal bool Scrolled=>scroller.Scrolling;
 internal VerticalScroller BodyArea=>scroller;

 public GuideDialog(Guide guide):base(guide.Title)
 {
  this.guide=guide;Chosen=guide.Default;
  Body.Padding=new(0,16,0,0);
  main=Column(ContentWidth);main.Margin=new(Side,0,Side,14);Body.Controls.Add(main);
  main.Controls.Add(Parts.TitleLine(guide.Headline,ContentWidth));
  content=Column(ContentWidth);Place(content,guide.Parts,ContentWidth,10);
  scroller.Controls.Add(content);content.Location=Point.Empty;scroller.Size=new(ContentWidth,content.GetPreferredSize(Size.Empty).Height);main.Controls.Add(scroller);
  // 펼치는 줄을 펴거나 접으면 본문의 높이가 바뀐다: 굴릴지와 칸의 높이를 다시 정한다
  content.SizeChanged+=(_,_)=>Fit();
  // 선과 단추 줄(창의 너비를 다 쓴다)
  Body.Controls.Add(new Panel(){Size=new(ContentWidth+Side*2,1),Margin=Padding.Empty,Tag="line"});
  var texts=guide.ButtonTexts;int chosen=Math.Clamp(guide.Default,0,texts.Count-1);
  // 기본 선택을 맨 오른쪽에 채운 단추로 놓고 나머지는 그 왼쪽에 차례대로 둔다
  var order=Enumerable.Range(0,texts.Count).Where(index=>index!=chosen).Append(chosen).ToList();
  foreach(int index in order){var button=new Button(){Text=texts[index],Tag=index==chosen?"primary":null};int picked=index;button.Click+=(_,_)=>{Chosen=picked;DialogResult=DialogResult.OK;Close();};buttons.Add(button);}
  var footer=Parts.Footer(ContentWidth,buttons.ToArray());var band=new Panel(){Size=new(ContentWidth+Side*2,27+24),Margin=Padding.Empty};band.Controls.Add(footer);footer.Location=new(Side,12);Body.Controls.Add(band);
  var basic=buttons[^1];AcceptButton=basic;CancelButton=null;Shown+=(_,_)=>ActiveControl=basic;
  // Esc 와 닫기는 기본 선택이다(단추를 누르지 않고 닫히면 Chosen 은 처음 값 그대로다)
  KeyPreview=true;KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Escape){e.Handled=true;Chosen=guide.Default;DialogResult=DialogResult.Cancel;Close();}};
 }
 static FlowLayoutPanel Column(int width)=>new(){AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,FlowDirection=FlowDirection.TopDown,WrapContents=false,Margin=Padding.Empty,MinimumSize=new(width,0)};

 // 창이 놓이는 화면의 작업 영역
 Rectangle Area()=>Screen.FromRectangle(Bounds).WorkingArea;
 /// <summary>
 /// 본문의 높이를 정한다: 창 전체가 작업 영역에 들어가면 본문을 다 보이고, 넘치면 넘치는 만큼 본문 칸을 줄여 그 안에서 굴린다(첫 줄과 단추는 늘 보인다).
 /// 굴릴 때는 세로 막대만큼 본문 칸을 오른쪽 여백 쪽으로 넓혀 본문의 너비와 창의 너비를 그대로 둔다(막대가 여백보다 넓으면 그만큼 창이 넓어진다).
 /// 창이 뜬 뒤(펼치는 줄을 폈을 때)에는 창이 화면 아래로 나가지 않게 올린다
 /// </summary>
 void Fit()
 {
  if(fitting||IsDisposed||!IsHandleCreated)return;
  fitting=true;
  try
  {
   int side=main.Margin.Left,full=content.Height;
   var area=Area();
   // 본문 칸을 뺀 창의 높이(틀, 제목 띠, 첫 줄, 선과 단추 줄). 창의 실제 높이는 쓰지 않는다: Windows 가 창의 높이를 화면 크기쯤에서 막아 본문이 아주 길면 내용보다 작게 나온다
   int others=Height-ClientSize.Height+Padding.Vertical+Body.Margin.Vertical+Body.GetPreferredSize(Size.Empty).Height-scroller.Height,room=area.Height-others;
   bool scroll=full>room&&room>Theme.S(60);
   int bar=scroll?SystemInformation.GetVerticalScrollBarWidthForDpi(DeviceDpi):0;
   scroller.Scrolling=scroll;
   var size=new Size(content.Width+bar,scroll?room:full);if(scroller.Size!=size)scroller.Size=size;
   var margin=new Padding(side,0,Math.Max(0,side-bar),main.Margin.Bottom);if(main.Margin!=margin)main.Margin=margin;
   PerformLayout();
   if(Visible&&!area.IsEmpty)
   {
    if(Bottom>area.Bottom)Top=Math.Max(area.Top,area.Bottom-Height);
    if(Top<area.Top)Top=area.Top;
   }
  }
  finally{fitting=false;}
 }
 // 창이 가운데에 놓인 뒤, 보이기 전(가려 둔 동안)에 높이를 정한다. 줄였으면 작업 영역 안에 다시 놓는다
 protected override void OnShown(EventArgs e)
 {
  try{Fit();var area=Area();if(Height>=area.Height||Top<area.Top||Bottom>area.Bottom)Top=Math.Max(area.Top,area.Top+(area.Height-Height)/2);}
  catch{}   // 높이를 정하지 못해도 창은 보여야 한다
  base.OnShown(e);
  // 창이 뜰 때 Windows 가 본문의 첫 항목(펼치는 줄)에 잠깐 초점을 주면 본문이 그 자리까지 굴러간다. 초점이 단추로 옮겨 간 뒤 본문의 맨 위를 보인다
  if(scroller.Scrolling)scroller.AutoScrollPosition=Point.Empty;
 }
 // 한 문단에 빈칸 없이 긴 낱말(경로, 주소)이 있으면 Label 은 그 낱말을 줄바꿈하지 못해 잘린다. 그런 문단은 어느 글자에서나 줄을 바꾸는 글 칸으로 적는다
 static bool LongWord(string text)=>text.Split(' ','\n').Any(word=>word.Length>34);
 void Place(FlowLayoutPanel panel,IEnumerable<GuidePart> parts,int width,int first)
 {
  int gap=first;
  foreach(var part in parts)
  {
   switch(part)
   {
    case GuideText text:
    {
     bool strong=text.Tone=="strong",dim=text.Tone=="dim";
     // 문단을 줄로 나눠, 빈칸 없는 긴 낱말(주소, 경로)이 든 줄은 어느 글자에서나 꺾는 글 칸으로, 나머지 줄은 낱말 사이에서만 꺾는 글로 적는다
     // (문단 전체를 글 칸으로 적으면 같은 문단의 한국어도 낱말 가운데에서 끊긴다). 이어진 보통 줄은 한 덩이다
     var pieces=new List<Control>();var plain=new List<string>();
     void FlushPlain(){if(plain.Count>0){pieces.Add(new WordLabel(string.Join("\n",plain),width,9.75f,text.Tone));plain.Clear();}}
     foreach(string row in text.Text.Replace("\r\n","\n").Split('\n'))
     {
      if(!LongWord(row)){plain.Add(row);continue;}
      FlushPlain();pieces.Add(new WrapBox(row,width){Font=Theme.Px(AppFonts.Ui,9.75f,strong?FontStyle.Bold:FontStyle.Regular),Dimmed=dim});
     }
     FlushPlain();
     Control line=pieces.Count==1?pieces[0]:Column(width);
     if(pieces.Count>1)foreach(var piece in pieces){piece.Margin=Padding.Empty;line.Controls.Add(piece);}
     line.Margin=new(0,gap,0,0);panel.Controls.Add(line);break;
    }
    case GuidePaths paths:
    {
     var block=new PathBlock(){Margin=new(0,gap,0,0),MinimumSize=new(width,0)};int inner=width-27;bool top=true;
     foreach(var (name,path) in paths.Items)
     {
      // 이름이 없으면(업데이트용 임시 파일 안내의 파일 이름) 경로만 놓는다
      if(name.Length>0){var label=Boxes.Text(name,"dim",9f,false,inner);label.BackColor=Color.Transparent;label.Margin=new(0,top?0:8,0,2);block.Controls.Add(label);}
      block.Controls.Add(new WrapBox(path,inner){Font=Theme.Px(AppFonts.Ui,9.75f,FontStyle.Bold),OnField=true,AccessibleName=name.Length>0?name:path,Margin=new(0,name.Length>0||top?0:8,0,0)});top=false;
     }
     panel.Controls.Add(block);break;
    }
    case GuideSteps steps:
    {
     var list=Column(width);list.Margin=new(0,gap,0,0);
     for(int i=0;i<steps.Steps.Length;i++)
     {
      var row=new Panel(){Width=width,Margin=new(0,i==0?0:4,0,0)};
      var number=Boxes.Text($"{i+1}.",null,9.75f,true);number.Location=new(8,0);row.Controls.Add(number);
      var words=new WordLabel(steps.Steps[i],width-28,9.75f,"strong");words.Location=new(28,0);row.Controls.Add(words);
      row.Height=words.Height;words.SizeChanged+=(_,_)=>{if(row.Height!=words.Height)row.Height=words.Height;};
      list.Controls.Add(row);
     }
     panel.Controls.Add(list);break;
    }
    case GuideLink link:
    {
     var name=Boxes.Text(link.Name,"dim",9f,false,width);name.Margin=new(0,gap,0,2);panel.Controls.Add(name);
     panel.Controls.Add(new WrapBox(link.Url,width){Font=Theme.Px(AppFonts.Ui,9.75f),AccessibleName=link.Name});break;
    }
    case GuideFold fold:
    {
     panel.Controls.Add(new Panel(){Size=new(width,1),Margin=new(0,gap+2,0,6),Tag="line"});
     var bar=new FoldBar(fold.Title){Plain=true,Size=new(width,24)};panel.Controls.Add(bar);
     var inside=Column(width);inside.Visible=false;Place(inside,fold.Inside,width,4);panel.Controls.Add(inside);
     bar.Toggled+=()=>inside.Visible=bar.Open;folds.Add((bar,inside));break;
    }
    case GuideVersions versions:
    {
     // 왼쪽에 지금의 것, 가운데 화살표, 오른쪽에 강조색으로 갈 것. 선 아래에 이름(왼쪽)과 값(오른쪽 끝)
     var box=new FieldPanel(){Width=width,Margin=new(0,gap,0,0)};int right=width/2+14,top=12;
     Label Put(string text,string? tag,Font font,int x,int y){var label=new Label(){Text=text,AutoSize=true,UseMnemonic=false,Tag=tag,Font=font,BackColor=Color.Transparent,Margin=Padding.Empty,Location=new(x,y)};box.Controls.Add(label);return label;}
     var small=Theme.Px(AppFonts.Ui,9f);var big=Theme.Px(AppFonts.Latin,15f,FontStyle.Bold);
     Put(versions.FromName,"dim",small,16,top);Put(versions.From,null,big,15,top+19);
     Put("→","dim",Theme.Px(AppFonts.Ui,11f),right-36,top+24);
     Put(versions.ToName,"dim",small,right,top);Put(versions.To,"accent",big,right-1,top+19);
     int bottom=top+19+30+10;
     if(versions.RowName!=null)
     {
      box.Controls.Add(new Panel(){Location=new(16,bottom),Size=new(width-32,1),Tag="line"});
      Put(versions.RowName,"dim",Theme.Px(AppFonts.Ui,9.5f),16,bottom+11);
      var value=Put(versions.RowValue??"",null,Theme.Px(AppFonts.Latin,10.5f),0,bottom+10);value.Left=width-16-value.PreferredWidth;
      bottom+=11+20+9;
     }
     box.Height=bottom;panel.Controls.Add(box);break;
    }
   }
   gap=10;
  }
 }

 /// <summary>주인 창 위에 띄운다. 누른 단추의 번호를 돌려준다(닫기·Esc 는 기본 선택)</summary>
 public static int Show(IWin32Window? owner,Guide guide)
 {
  if(Hook!=null)return Hook(guide);
  using var dialog=new GuideDialog(guide);if(owner==null){dialog.StartPosition=FormStartPosition.CenterScreen;dialog.ShowInTaskbar=true;dialog.ShowDialog();}else dialog.ShowDialog(owner);
  return dialog.Chosen;
 }
 /// <summary>
 /// 주 화면이 없을 때 띄운다(시작 오류, 이미 켜져 있음, 임시 파일·백업을 직접 열었을 때): 화면 가운데에 뜨고 작업 표시줄에도 보인다.
 /// 테마와 창의 크기 단계는 부르는 쪽이 이미 정해 둔 것을 쓴다. **이 창을 띄우지 못하면 Windows 의 알림 창으로라도 알린다**(알림이 사라지면 안 된다. 그때는 기본 선택으로 답한 것으로 본다)
 /// </summary>
 public static int ShowAlone(Guide guide)=>ShowSafe(null,guide);
 /// <summary>
 /// 이 창을 띄우지 못해도 알림이 사라지지 않게 띄운다(교체·복구를 맡은 실행 파일이 쓴다. 그 실행 파일은 글꼴·테마를 읽지 못해도 알려야 한다).
 /// 실패하면 Windows 의 알림 창으로 알린다. 단추가 둘이면 '예'가 기본이 아닌 쪽, '아니요'가 기본 선택이다(처음 초점도 '아니요')
 /// </summary>
 public static int ShowSafe(IWin32Window? owner,Guide guide)
 {
  if(Hook!=null)return Hook(guide);
  try{return Show(owner,guide);}
  catch
  {
   try
   {
    var texts=guide.ButtonTexts;
    if(texts.Count<2){if(owner!=null)MessageBox.Show(owner,guide.Plain(),guide.Title);else MessageBox.Show(guide.Plain(),guide.Title);return guide.Default;}
    int other=guide.Default==0?1:0;
    string text=guide.Plain()+$"\n\n[예] {texts[other]}   [아니요] {texts[guide.Default]}";
    var answer=owner!=null?MessageBox.Show(owner,text,guide.Title,MessageBoxButtons.YesNo,MessageBoxIcon.None,MessageBoxDefaultButton.Button2):MessageBox.Show(text,guide.Title,MessageBoxButtons.YesNo,MessageBoxIcon.None,MessageBoxDefaultButton.Button2);
    return answer==DialogResult.Yes?other:guide.Default;
   }
   catch{return guide.Default;}
  }
 }
}

/// <summary>켤 때 만나는 안내의 글. 창을 띄우는 자리(Program, SingleInstance)와 떼어 두어 검사가 그대로 본다</summary>
static class StartGuides
{
 public const string ErrorTitle="실행 오류",ErrorHead="프로그램을 시작할 수 없습니다.";
 /// <summary>두 번째로 켰는데 먼저 켜진 창을 찾지 못했을 때</summary>
 public static Guide AlreadyRunning=>new("디맥 개인 기록 저장 오버레이","프로그램이 이미 실행 중입니다.",new GuidePart[]{new GuideText("잠시 후 작업 표시줄에서 프로그램 창을 확인해 주세요.")});
 /// <summary>
 /// 프로그램을 시작하다 난 오류. OBS 연결용 포트가 이미 쓰이고 있으면 전용 글, 그 밖에는 받은 글과 제보 안내.
 /// 개인 파일과 기록 파일의 글(UserFilesException, InvalidDataException)은 까닭과 다음 행동, 폴더의 자리까지 스스로 적혀 있어 제보 안내를 붙이지 않는다
 /// </summary>
 public static Guide Error(Exception error)
 {
  if(error is System.Net.Sockets.SocketException{SocketErrorCode:System.Net.Sockets.SocketError.AddressAlreadyInUse})
   return new(ErrorTitle,ErrorHead,new GuidePart[]{new GuideText("다른 프로그램이 OBS 연결용 포트(18765)를 사용하고 있습니다."),new GuideText("이전 버전의 기록 프로그램이 켜져 있다면 종료한 뒤 다시 실행해 주세요.","dim")});
  var parts=Lines(error.Message);
  if(parts.Count==0)parts.Add(new GuideText("알 수 없는 오류가 났습니다."));   // 글이 빈 예외: 영어 형식 이름을 보이지 않는다
  if(error is not (UserFilesException or InvalidDataException))parts.Add(new GuideText("다시 실행해도 같은 오류가 뜨면 위 내용을 함께 제보해 주세요.","dim"));
  return new(ErrorTitle,ErrorHead,parts);
 }
 /// <summary>
 /// 여러 줄의 글을 안내창에 놓을 것들로 나눈다. 글은 그대로 두고 놓는 모양만 정한다:
 ///  · '이름: 경로'인 줄(경로가 드라이브 글자나 \\ 로 시작한다)은 경로 상자에 넣는다. 이어진 줄들은 한 상자에 모은다
 ///  · '이름: https://…'인 줄은 이름과 주소로 놓는다(앞의 가운뎃점은 뗀다)
 ///  · 그 밖의 줄은 글이다. 빈 줄이 문단을 가른다(가운뎃점으로 시작하는 줄은 줄마다 한 문단이다)
 /// </summary>
 internal static List<GuidePart> Lines(string message)
 {
  var parts=new List<GuidePart>();var paths=new List<(string,string)>();var text=new List<string>();
  void FlushText(){if(text.Count>0){parts.Add(new GuideText(string.Join("\n",text)));text.Clear();}}
  void FlushPaths(){if(paths.Count>0){parts.Add(new GuidePaths(paths.ToArray()));paths.Clear();}}
  foreach(string raw in message.Replace("\r\n","\n").Split('\n'))
  {
   string line=raw.TrimEnd();int colon=line.IndexOf(": ",StringComparison.Ordinal);string value=colon>0?line[(colon+2)..].Trim():"";
   bool path=value.Length>2&&(char.IsAsciiLetter(value[0])&&value[1]==':'&&value[2]=='\\'||value.StartsWith("\\\\",StringComparison.Ordinal));
   if(colon>0&&path){FlushText();paths.Add((line[..colon].TrimStart('·',' '),value));continue;}
   FlushPaths();
   if(colon>0&&value.StartsWith("https://",StringComparison.Ordinal)&&!value.Contains(' ')){FlushText();parts.Add(new GuideLink(line[..colon].TrimStart('·',' '),value));continue;}
   if(line.Length==0){FlushText();continue;}
   if(line.StartsWith("·",StringComparison.Ordinal))FlushText();
   text.Add(line);
  }
  FlushText();FlushPaths();return parts;
 }
 /// <summary>기록 안내(첫 전환이 알릴 것): 첫 문장이 첫 줄이고 나머지가 본문이다</summary>
 public static Guide Records(string notice)
 {
  int end=notice.IndexOf(". ",StringComparison.Ordinal);
  return end<0?new("기록 안내",notice,Array.Empty<GuidePart>()):new("기록 안내",notice[..(end+1)],new GuidePart[]{new GuideText(notice[(end+2)..])});
 }
}
