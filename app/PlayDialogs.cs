using System.Globalization;
namespace LadderRecords;

// 기록 히스토리에서 여는 창들: 선택 기록 수정, 선택 기록 삭제 확인, 안내창. 그리고 이 창들이 함께 쓰는 부품(곡 이름 한 줄, 점수 글, SCORE·RATE 입력).
// 안내창의 두 값을 위아래로 보이는 줄(NoticeRow)과 가져온 기록의 일괄 삭제 확인(BulkDeleteDialog)도 여기에 둔다.
// 창 너비는 수정·삭제 424, 안내 442(100% 때의 값. 본문 좌우 여백 18).

/// <summary>곡 이름 한 줄과 그 뒤의 꼬리표들. 꼬리표의 자리를 먼저 잡고, 곡 이름은 기본 글자 크기로 한 줄에 맞춘다(넘치면 끝을 줄이고, 마우스를 올리면 전체를 보인다)</summary>
sealed class SongLine:Control
{
 readonly string title;readonly string[] chips;readonly float px;ToolTip? tip;
 public string Title=>title;
 /// <summary>곡 이름 뒤에 그리는 꼬리표들(버튼, 난이도, 판정)</summary>
 public IReadOnlyList<string> Chips=>chips;
 public SongLine(string title,int width,float px,params string[] chips)
 {
  SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.SupportsTransparentBackColor,true);
  this.title=title;this.chips=chips;this.px=px;Size=new(width,Math.Max(24,(int)Math.Ceiling(px*1.3f)+2));Margin=Padding.Empty;TabStop=false;AccessibleName=title;
 }
 protected override void OnPaint(PaintEventArgs e)
 {
  var g=e.Graphics;g.Clear(Parent?.BackColor??Theme.Window);
  int chipsWide=chips.Sum(Chip.Measure)+Theme.S(4)*Math.Max(0,chips.Length-1),gap=Theme.S(9);
  var chain=Ink.Title(px);float middle=Height/2f;
  var line=MixedLine.Clip(title,chain,Math.Max(Theme.S(40),Width-chipsWide-gap));
  var hint=g.TextRenderingHint;g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAlias;
  float x;using(var ink=new SolidBrush(Theme.Text))x=line.Draw(g,ink,0,Ink.Baseline(middle,chain));
  g.TextRenderingHint=hint;
  x+=gap;foreach(string chip in chips)x+=Chip.Draw(g,chip,x,middle)+Theme.S(4);
  if(line.Cut&&tip==null){tip=new ToolTip();tip.SetToolTip(this,title);}
 }
 protected override void Dispose(bool disposing){if(disposing)tip?.Dispose();base.Dispose(disposing);}
}

/// <summary>글 조각들을 한 줄에 그리는 표시(점수와 RATE 처럼 크기·색이 다른 글). 크기는 글에 맞춰 스스로 정한다</summary>
sealed class InkLabel:Control
{
 readonly Func<List<InkPart>> parts;readonly Font[] tall;
 public InkLabel(Func<List<InkPart>> parts,Font[] tall,int height)
 {
  SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
  this.parts=parts;this.tall=tall;Margin=Padding.Empty;TabStop=false;Size=new((int)Math.Ceiling(Ink.Width(parts()))+2,Theme.S(height));
 }
 // 크기는 이미 창의 크기 단계에 맞춰 정했다. 창이 뜰 때의 키우기에서는 자리만 따른다
 protected override void ScaleControl(SizeF factor,BoundsSpecified specified)=>base.ScaleControl(factor,specified&BoundsSpecified.Location);
 public void Refit(){Width=(int)Math.Ceiling(Ink.Width(parts()))+2;Invalidate();}
 protected override void OnPaint(PaintEventArgs e){e.Graphics.Clear(Parent?.BackColor??Theme.Window);Ink.Draw(e.Graphics,parts(),0,Ink.Baseline(Height/2f,tall));}
}

/// <summary>SCORE 와 RATE 를 나란히 받는 입력. 한쪽을 고치면 다른 쪽이 따라간다. RATE 를 지금의 SCORE 와 같은 값으로 넣으면(칸에 들어갔다 나오기만 해도) 세부 SCORE 를 지킨다</summary>
sealed class ScoreRateInput:Panel
{
 readonly InputBox score,rate;bool syncing;int? held,exact;
 public event Action? Changed;
 public TextBox ScoreBox=>score.Box;
 public TextBox RateBox=>rate.Box;
 /// <param name="scoreWidth">SCORE 칸의 너비(주지 않으면 두 칸이 같다)</param>
 /// <param name="hints">빈 칸에 옅은 안내 글을 보인다(새 플레이를 적는 수동 기록 설정)</param>
 public ScoreRateInput(int width,int? initial,int? scoreWidth=null,bool hints=false)
 {
  Size=new(width,66);Margin=Padding.Empty;int half=scoreWidth??(width-12)/2;
  var scoreName=Boxes.Text("SCORE","dim",9f);scoreName.Font=Theme.Px(AppFonts.Latin,9f,FontStyle.Bold);scoreName.Location=new(0,0);
  var rateName=Boxes.Text("RATE","dim",9f);rateName.Font=Theme.Px(AppFonts.Latin,9f,FontStyle.Bold);rateName.Location=new(half+12,0);
  score=Field(half,"점");score.Location=new(0,22);rate=Field(width-half-12,"%");rate.Location=new(half+12,22);
  Controls.AddRange(new Control[]{scoreName,rateName,score,rate});
  score.Box.AccessibleName="SCORE";rate.Box.AccessibleName="RATE";score.Box.MaxLength=9;rate.Box.MaxLength=6;
  if(hints){score.Box.Hint="점수 입력";rate.Box.Hint="0.00";}
  // RATE 칸의 오른쪽 끝은 언제나 이 부품의 끝이다: 창이 커질 때 자리와 너비를 따로 반올림하면 1px 넘쳐 오른쪽 테두리가 잘린다(150% 에서 봤다)
  void Fit(){int wide=Math.Max(10,Width-rate.Left);if(rate.Width!=wide)rate.Width=wide;}
  SizeChanged+=(_,_)=>Fit();rate.LocationChanged+=(_,_)=>Fit();rate.SizeChanged+=(_,_)=>Fit();
  Set(initial);
  score.Box.TextChanged+=(_,_)=>{if(syncing)return;if(TryScore(score.Box.Text,out int value)){exact=held=value;Fill(rate.Box,Rate(value));}Changed?.Invoke();};
  // RATE 를 고치면 SCORE 는 100점 단위의 환산값이 된다. 다만 처음 채운 SCORE(또는 SCORE 칸에 마지막으로 적은 값)의 RATE 와 같아지면 그 세부 SCORE 로 돌아간다:
  // RATE 를 한 글자씩 고쳐 적는 동안 잠깐 다른 값을 거쳐도, 같은 RATE 로 끝나면 세부 SCORE 가 지켜진다
  rate.Box.TextChanged+=(_,_)=>
  {
   if(syncing)return;
   if(TryRate(rate.Box.Text,out decimal value))
   {
    int next=exact is int kept&&ScoreValue.Rate(kept)==value?kept:ScoreValue.FromAccuracy(value);
    if(held!=next||!TryScore(score.Box.Text,out int shown)||shown!=next){held=next;Fill(score.Box,Points(next));}
   }
   Changed?.Invoke();
  };
  // 칸을 떠날 때 보기 좋은 모양으로 고쳐 적는다(천 단위 구분, 소수 두 자리). 값은 바뀌지 않는다
  score.Box.LostFocus+=(_,_)=>{if(Value is int value)Fill(score.Box,Points(value));};
  rate.Box.LostFocus+=(_,_)=>{if(Value is int value)Fill(rate.Box,Rate(value));};
 }
 static InputBox Field(int width,string unit)
 {
  var box=new InputBox(width,"",0,44);box.Box.Font=Theme.Px(AppFonts.Latin,13.5f,FontStyle.Bold);box.Box.Width=width-22-26;
  var mark=new Label(){Text=unit,AutoSize=true,BackColor=Color.Transparent,Tag="dim",Font=Theme.Px(AppFonts.Ui,9f,FontStyle.Bold),UseMnemonic=false};
  box.Controls.Add(mark);mark.Location=new(width-12-mark.PreferredWidth,(44-mark.PreferredHeight)/2);mark.Anchor=AnchorStyles.Right|AnchorStyles.Top;
  mark.Click+=(_,_)=>box.Box.Focus();
  return box;
 }
 // 숫자는 Windows 의 숫자 형식으로 적고 **같은 형식으로** 읽는다(프로그램의 다른 자리도 그 형식으로 점수를 보인다).
 // 천 단위 구분자가 쉼표가 아닌 형식(예: 독일어의 995.500)에서도 이 칸이 스스로 적은 값을 다시 읽을 수 있어야 한다(쉼표만 떼고 읽으면 그런 형식에서 RATE 로 고친 점수를 저장할 수 없다)
 static string Points(int value)=>value.ToString("N0",CultureInfo.CurrentCulture);
 static string Rate(int value)=>ScoreValue.Rate(value).ToString("F2",CultureInfo.CurrentCulture);
 void Fill(TextBox box,string text){if(box.Text==text)return;syncing=true;try{box.Text=text;}finally{syncing=false;}}
 /// <summary>두 칸을 그 SCORE 로 채운다(없으면 비운다)</summary>
 public void Set(int? value){exact=held=value;Fill(score.Box,value is int points?Points(points):"");Fill(rate.Box,value is int filled?Rate(filled):"");}
 static bool TryScore(string text,out int value)=>int.TryParse(text.Trim(),NumberStyles.AllowThousands,CultureInfo.CurrentCulture,out value)&&value>=0&&value<=1000000;
 static bool TryRate(string text,out decimal value)=>decimal.TryParse(text.Trim(),NumberStyles.AllowDecimalPoint,CultureInfo.CurrentCulture,out value)&&value>=0&&value<=100&&decimal.Round(value,2)==value;
 /// <summary>입력한 SCORE. 두 칸이 모두 바르고 서로 맞을 때만 있다</summary>
 public int? Value=>TryScore(score.Box.Text,out int value)&&TryRate(rate.Box.Text,out decimal shown)&&ScoreValue.Rate(value)==shown?value:null;
 /// <summary>아무것도 적지 않았는가</summary>
 public bool Blank=>score.Box.Text.Trim().Length==0&&rate.Box.Text.Trim().Length==0;
}

/// <summary>오른쪽 끝에 맞춘 두 줄: 위는 값, 아래는 덧붙이는 옅은 글(교체할 판의 점수와 그 판의 시각). 크기는 글에 맞춰 스스로 정한다</summary>
sealed class InkPair:Control
{
 readonly List<InkPart> top,bottom;readonly Font[] tallTop,tallBottom;
 public InkPair(List<InkPart> top,Font[] tallTop,List<InkPart> bottom,Font[] tallBottom)
 {
  SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
  this.top=top;this.bottom=bottom;this.tallTop=tallTop;this.tallBottom=tallBottom;Margin=Padding.Empty;TabStop=false;
  Size=new((int)Math.Ceiling(Math.Max(Ink.Width(top),Ink.Width(bottom)))+2,Theme.S(40));
 }
 // 크기는 이미 창의 크기 단계에 맞춰 정했다. 창이 뜰 때의 키우기에서는 자리만 따른다
 protected override void ScaleControl(SizeF factor,BoundsSpecified specified)=>base.ScaleControl(factor,specified&BoundsSpecified.Location);
 protected override void OnPaint(PaintEventArgs e)
 {
  e.Graphics.Clear(Parent?.BackColor??Theme.Window);
  Ink.DrawRight(e.Graphics,top,Width-1,Ink.Baseline(Height*.27f,tallTop));Ink.DrawRight(e.Graphics,bottom,Width-1,Ink.Baseline(Height*.76f,tallBottom));
 }
}

/// <summary>안내창의 상자에 위아래로 놓는 한 줄: 이름과 값(Under 가 있으면 값 아래에 옅은 글 한 줄). Plain 은 그 값을 글로 적은 것이다(화면 읽기와 개발용 검사가 본다)</summary>
record NoticeRow(string Name,string Plain,List<InkPart> Parts,List<InkPart>? Under=null)
{
 /// <summary>상자에 놓을 값의 그림</summary>
 public Control Value()=>Under==null?new InkLabel(()=>Parts,Ink.Number(15f),24){AccessibleName=Name+" "+Plain}:new InkPair(Parts,Ink.Number(15f),Under,Ink.Number(12f,false)){AccessibleName=Name+" "+Plain};
 public static NoticeRow Word(string name,string value)=>new(name,value,new(){new(value,Ink.Body(15f,true),Theme.Text)});
 /// <summary>입력한 점수: '997,654점 (99.76%)'</summary>
 public static NoticeRow Entered(string name,int score)=>new(name,$"{ScoreText.Points(score)} ({ScoreText.Rate(score)})",Pieces(score,true));
 /// <summary>지금의 최고 기록. 실제로 남아 있는 정밀도만 적는다(RATE 만 남은 기록은 RATE 만). 기록이 없으면 '미등록'</summary>
 public static NoticeRow Best(string name,Play? play)=>play==null?Word(name,"미등록"):new(name,ScoreText.Kept(play),Pieces(play.Score,PlaySource.Exact(play.Source)));
 /// <summary>교체할 판: 원래 플레이 시각과 점수</summary>
 public static NoticeRow Target(string name,Play play)
 {
  return new(name,$"{ScoreText.Time(play)} · {ScoreText.Kept(play)}",Pieces(play.Score,true),new(){new(ScoreText.Time(play),Ink.Number(12f,false),Theme.Dim)});
 }
 static List<InkPart> Pieces(int score,bool exact)=>exact
  ?new(){new($"{score:N0}",Ink.Number(15f),Theme.Text),new("점",Ink.Body(12f,true),Theme.Text,1),new($"({ScoreText.Rate(score)})",Ink.Number(11f,false),Theme.Dim,6)}
  :new(){new(ScoreText.Rate(score),Ink.Number(15f),Theme.Text)};
}

/// <summary>안내창: 사선이 붙은 굵은 첫 줄, (있으면) 두 값을 위아래로 보이는 상자, 그 아래의 안내, '확인' 하나. 확인·Enter·Esc·닫기가 모두 같다</summary>
class PlayNotice:CompactDialog
{
 public const int ContentWidth=404;
 /// <summary>개발용 검사: 창을 띄우지 않고 무엇을 알리려 했는지만 받는다(제목, 첫 줄, 안내). 상자의 줄이 있으면 안내의 앞에 '이름: 값'으로 한 줄씩 적어 준다</summary>
 internal static Action<string,string,string>? Hook{get;set;}
 public PlayNotice(string title,string headline,string text,IReadOnlyList<NoticeRow>? rows=null):base(title)
 {
  Body.Padding=new(18,16,18,16);
  Body.Controls.Add(Parts.TitleLine(headline,ContentWidth));
  if(rows is {Count:>0})
  {
   var box=PlayParts.Pairs(ContentWidth,30,rows.Select(row=>(row.Name,row.Value())).ToArray());
   box.Margin=new(0,12,0,4);Body.Controls.Add(box);
  }
  var body=Boxes.Text(text,null,9.75f,false,ContentWidth);body.Margin=new(0,8,0,18);Body.Controls.Add(body);
  var ok=new Button(){Text="확인",DialogResult=DialogResult.OK,Tag="default"};
  Body.Controls.Add(Parts.Footer(ContentWidth,ok));
  AcceptButton=ok;CancelButton=ok;Shown+=(_,_)=>ActiveControl=ok;
 }
 public static void Show(IWin32Window owner,string title,string headline,string text,IReadOnlyList<NoticeRow>? rows=null)
 {
  if(Hook!=null){Hook(title,headline,rows is {Count:>0}?string.Join("\n",rows.Select(row=>$"{row.Name}: {row.Plain}"))+"\n"+text:text);return;}
  using var dialog=new PlayNotice(title,headline,text,rows);dialog.ShowDialog(owner);
 }
 // 선택 기록 수정·삭제를 멈췄을 때의 안내. 수정과 삭제는 같은 꼴의 글을 쓰고 '수정'·'삭제'만 바꾼다
 public static void Changed(IWin32Window owner,bool edit)=>Show(owner,edit?"기록 수정":"기록 삭제",edit?"기록이 바뀌어 수정을 멈췄습니다.":"기록이 바뀌어 삭제를 멈췄습니다.",
  $"기록 히스토리에서 변경된 내용을 확인한 뒤 {(edit?"수정":"삭제")}할 기록을 다시 선택해 주세요.");
 public static void Gone(IWin32Window owner,bool edit)=>Show(owner,edit?"기록 수정":"기록 삭제","선택한 기록이 이미 삭제되었습니다.","‘확인’을 누르면 최신 기록 목록으로 돌아갑니다.");
 public static void NoPattern(IWin32Window owner)=>Show(owner,"기록 수정","현재 곡 목록에서 이 패턴을 찾을 수 없습니다.","메인 창에서 ‘업데이트 확인’을 눌러 주세요. 최신 버전에서도 같은 안내가 뜨면 곡명과 패턴을 함께 제보해 주세요.");
 public static void OtherProfile(IWin32Window owner,bool edit)=>Show(owner,edit?"기록 수정":"기록 삭제","‘등록한 기록 보기’ 창을 열었을 때와 현재 프로필이 다릅니다.",
  $"‘등록한 기록 보기’ 창을 닫고 메인 창에서 기록을 {(edit?"수정할":"삭제할")} 프로필을 선택한 뒤 ‘등록한 기록 보기’를 다시 열어 주세요.");
 // 기록 파일에 쓰지 못한 까닭과 다음 행동(수동 저장과 같은 글. SaveTrouble). Windows 의 영문 오류를 그대로 보이지 않는다
 public static void Failed(IWin32Window owner,bool edit,Exception error)=>Show(owner,edit?"기록 수정":"기록 삭제",edit?"기록을 수정하지 못했습니다.":"기록을 삭제하지 못했습니다.",SaveTrouble.Retry(error));
}

/// <summary>
/// 기록 파일에 쓰지 못한 까닭과, 다시 하기 전에 할 일(수동 저장, 가져온 기록의 일괄 삭제, 히스토리의 한 판 수정·삭제, V아카이브 가져오기의 안내가 함께 쓴다).
/// Windows 가 주는 오류 글(영문이고 파일의 긴 경로가 들어 있다)을 그대로 보이지 않고, 가를 수 있는 까닭은 한국어로 적는다.
/// 프로그램이 스스로 알리는 까닭(밖에서 바뀐 기록 파일, 원본 사본을 남기지 못함, 업데이트 중)은 그 글을 그대로 쓴다.
/// 가르지 못한 오류는 '기록 파일에 저장하지 못했습니다.' 아래에 받은 글을 '(오류: …)'로 붙인다(제보할 때의 단서)
/// </summary>
static class SaveTrouble
{
 public static (string Reason,string? Before) Of(Exception error)
 {
  int code=error.HResult&0xFFFF;
  // 프로그램이 스스로 알리는 까닭은 글 안에 다음 행동이 있다: 뒤에 행동을 또 붙이지 않는다(Before 가 null).
  // 밖에서 바뀐 기록 파일은 프로그램을 다시 열기 전에는 다시 해도 막힌다
  if(error is RecordStoreFrozenException)return (error.Message,"업데이트가 끝난 뒤");
  if(error is RecordFileChangedException or OriginalCopyException or InvalidRecordsException)return (error.Message,null);
  if(error is IOException&&code is 0x70 or 0x27)return ("기록을 저장할 공간이 부족합니다.","공간을 확보한 뒤");
  if(error is IOException&&code is 0x20 or 0x21)return ("기록 파일을 다른 프로그램이 사용하고 있습니다.","해당 프로그램을 닫은 뒤");
  if(error is IOException&&code is 0x02 or 0x03)return ("‘UserData’ 폴더를 찾을 수 없습니다.","‘UserData’ 폴더가 있는지 확인한 뒤");
  if(error is UnauthorizedAccessException)return ("기록 파일에 쓸 수 없습니다.","‘UserData’ 폴더에 파일을 쓸 수 있는지 확인한 뒤");
  return ($"기록 파일에 저장하지 못했습니다.\n(오류: {error.Message.Trim()})","문제를 해결한 뒤");
 }
 /// <summary>안내의 본문: 까닭(가르지 못한 오류는 받은 글까지 두 줄)과 '… 뒤 다시 시도해 주세요.'</summary>
 public static string Retry(Exception error){var (reason,before)=Of(error);return before==null?reason:reason+"\n"+before+" 다시 시도해 주세요.";}
}

static class PlayParts
{
 public const int NameWidth=72;
 /// <summary>이름과 값이 나란한 줄들을 상자에 놓는다. 다음 줄의 y 를 돌려준다</summary>
 public static int Rows(Control box,int left,int top,params (string Name,Control Value,int Height)[] rows)
 {
  int y=top;
  foreach(var (name,value,height) in rows)
  {
   var label=Boxes.Text(name,"dim",9f);label.BackColor=Color.Transparent;box.Controls.Add(label);label.Location=new(left,y+(height-label.PreferredHeight)/2);
   box.Controls.Add(value);value.Location=new(left+NameWidth,y+(height-(value is InkLabel?height:value.PreferredSize.Height))/2);
   y+=height;
  }
  return y;
 }
 public static Label Plain(string text,bool numbers=false)
 {
  var label=Boxes.Text(text,null,9.75f,!numbers);label.BackColor=Color.Transparent;
  if(numbers)label.Font=Theme.Px(AppFonts.Latin,9.75f);
  return label;
 }
 /// <summary>
 /// 이름은 왼쪽, 값은 오른쪽 끝에 맞춘 줄들을 담은 상자(안내창의 두 값, 일괄 삭제의 프로필과 건수, 되돌리기의 동작별 건수).
 /// 줄의 높이는 rowHeight 이고, 값이 그보다 크면(두 줄짜리 값) 그 줄만 값에 맞춰 커진다. lines: 줄 사이에 선을 긋는다. strong: 이름을 본문 색의 굵은 글자로 적는다
 /// </summary>
 public static FieldPanel Pairs(int width,int rowHeight,(string Name,Control Value)[] rows,bool lines=false,bool strong=false)
 {
  var box=new FieldPanel(){Width=width};int y=lines?0:7;bool first=true;
  foreach(var (name,value) in rows)
  {
   if(lines&&!first){box.Controls.Add(new Panel(){Location=new(14,y),Size=new(width-28,1),Tag="line"});y+=1;}
   first=false;
   // 직접 그리는 값(InkLabel, InkPair)의 크기는 이미 창의 크기 단계가 곱해져 있다. 자리는 100% 때의 값으로 잡는다(창이 뜰 때 자리만 단계만큼 커진다)
   bool drawn=value is InkLabel or InkPair;int wide=drawn?(int)Math.Ceiling(value.Width/Theme.Scale):value.PreferredSize.Width,tall=drawn?(int)Math.Ceiling(value.Height/Theme.Scale):value.PreferredSize.Height;
   int height=Math.Max(rowHeight,tall+8);
   var label=Boxes.Text(name,strong?null:"dim",9.75f,strong);label.BackColor=Color.Transparent;box.Controls.Add(label);label.Location=new(14,y+(height-label.PreferredHeight)/2);
   if(value is Label text)text.BackColor=Color.Transparent;
   box.Controls.Add(value);value.Location=new(width-14-wide,y+(height-tall)/2);
   y+=height;
  }
  box.Height=y+(lines?0:7);return box;
 }
 public static InkLabel Score(Play play,float scorePx,float ratePx)=>new(()=>Ink.Score(play,scorePx,ratePx,Theme.Score,true),Ink.Number(scorePx),(int)Math.Ceiling(scorePx*1.5f));
}

/// <summary>선택 기록 수정: 고른 한 판의 SCORE·RATE 를 고친다. 원래 시각은 그대로다. 저장하면 Value 에 새 SCORE 가 있다</summary>
class EditPlayDialog:CompactDialog
{
 public const int ContentWidth=386;
 readonly ScoreRateInput input;readonly Label wrong;
 public int? Value=>input.Value;
 public ScoreRateInput Input=>input;
 /// <param name="linked">이 판이 노말에도 반영되어 있는가(함께 바뀐다고 알린다)</param>
 public EditPlayDialog(string title,string button,string pattern,string judge,Play play,bool linked):base("선택 기록 수정")
 {
  Body.Padding=new(18,16,18,16);
  Body.Controls.Add(new SongLine(title,ContentWidth,20f,button,pattern,judge+"판정"){Margin=new(0,0,0,6)});
  var facts=new Panel(){Width=ContentWidth,Margin=Padding.Empty};
  facts.Height=PlayParts.Rows(facts,0,0,(ScoreText.TimeName(play),PlayParts.Plain(play.At is {} at?at.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"):"일시 미상",true),24),("기존 기록",PlayParts.Score(play,14f,13f),24));
  Body.Controls.Add(facts);
  Body.Controls.Add(new Panel(){Size=new(ContentWidth,1),Margin=new(0,12,0,12),Tag="line"});
  input=new ScoreRateInput(ContentWidth,play.Score);Body.Controls.Add(input);
  if(linked){var note=Boxes.Text("같은 플레이의 노말판정 기록도 함께 수정됩니다.","dim",9.75f,false,ContentWidth);note.Margin=new(0,10,0,0);Body.Controls.Add(note);}
  wrong=Boxes.Text("SCORE는 0~1,000,000점, RATE는 0.00~100.00%로 입력해 주세요.","bad",9.75f,false,ContentWidth);wrong.Margin=new(0,10,0,0);wrong.Visible=false;Body.Controls.Add(wrong);
  var ok=new Button(){Text="저장",DialogResult=DialogResult.OK,Tag="primary"};var cancel=new Button(){Text="취소",DialogResult=DialogResult.Cancel};
  var footer=Parts.Footer(ContentWidth,ok,cancel);footer.Margin=new(0,18,0,0);Body.Controls.Add(footer);
  AcceptButton=ok;CancelButton=cancel;
  input.Changed+=()=>{bool valid=input.Value!=null;ok.Enabled=valid;wrong.Visible=!valid;};
  Shown+=(_,_)=>{input.ScoreBox.Focus();input.ScoreBox.SelectAll();};
 }
}

/// <summary>삭제 확인에 보일 것. Candidates 가 여럿이면 같은 SCORE 의 일시 미상 기록들이고, 창에서 지울 출처 하나를 고른다</summary>
record DeleteRequest(string Title,string Button,string Pattern,string Judge,IReadOnlyList<Play> Candidates,bool Reflected,Func<Play,IReadOnlyList<string>> Emptied);

/// <summary>
/// 선택 기록 삭제 확인. 처음 선택은 취소다(연 직후의 Enter·Esc·닫기는 모두 취소).
/// 실제로 지워지는 것을 적는다: 한 판, 노말에 반영된 하드·맥스의 원래 판(노말에서도 사라진다), 노말에 보이는 반영만(원래 판은 남는다), 마지막 기록, 여러 출처 가운데 하나
/// </summary>
class DeletePlayDialog:ChoiceDialog
{
 public const int ContentWidth=386;
 readonly DeleteRequest request;readonly ThemedCombo? sources;readonly Label emptied;readonly System.Windows.Forms.Timer watch=new(){Interval=250};
 /// <summary>안내 줄을 지금의 기록으로 다시 적는다</summary>
 internal void RefreshNotes(){string now=string.Join("\n",Notes(request,Chosen));if(emptied.Text==now)return;emptied.Text=now;emptied.Visible=now.Length>0;}
 /// <summary>지금 보이는 안내 줄</summary>
 internal string NotesShown=>emptied.Visible?emptied.Text:"";
 protected override void Dispose(bool disposing){if(disposing)watch.Dispose();base.Dispose(disposing);}
 /// <summary>지울 판(출처가 여럿이면 창에서 고른 것)</summary>
 public Play Chosen=>sources is {SelectedIndex:>=0}?request.Candidates[sources.SelectedIndex]:request.Candidates[0];
 public static string Question(bool reflected)=>reflected?"이 플레이의 노말판정 반영만 삭제할까요?":"선택한 기록을 삭제할까요?";
 /// <summary>확인 창의 안내 줄들(창이 그대로 보인다)</summary>
 public static List<string> Notes(DeleteRequest request,Play play)
 {
  var notes=new List<string>();string from=ScoreText.Judge(play.Key);
  if(request.Reflected)notes.Add($"원래 {from}판정 기록은 유지됩니다.");
  else if(play.Normal==NormalLink.Linked)notes.Add($"같은 플레이의 노말판정 기록({from}판정에서 반영)도 함께 삭제됩니다.");
  if(request.Candidates.Count>1)notes.Add("선택한 출처의 기록 1건만 삭제됩니다.");
  var gone=request.Emptied(play);
  if(gone.Count>0)notes.Add($"{(request.Reflected?"이 반영을":"이 플레이를")} 삭제하면 이 패턴의 {string.Join("·",gone.Select(j=>j+"판정"))} 기록은 미등록이 됩니다.");
  return notes;
 }
 public DeletePlayDialog(DeleteRequest request):base("선택 기록 삭제")
 {
  this.request=request;var play=request.Candidates[0];
  Body.Padding=new(18,16,18,16);
  var question=Parts.TitleLine(Question(request.Reflected),ContentWidth);question.Margin=new(0,0,0,12);Body.Controls.Add(question);
  var box=new FieldPanel(){Width=ContentWidth,Margin=new(0,0,0,12)};
  var song=new SongLine(request.Title,ContentWidth-28,17f,request.Button,request.Pattern,request.Judge+"판정"){Location=new(14,11)};box.Controls.Add(song);
  var rows=new List<(string,Control,int)>
  {
   (ScoreText.TimeName(play),PlayParts.Plain(play.At is {} at?at.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"):"일시 미상",play.At!=null),26),
   ("점수",PlayParts.Score(play,17f,12f),30),
  };
  if(request.Reflected)rows.Add(("반영 출처",PlayParts.Plain(ScoreText.Reflected(play)),26));
  else if(play.At==null&&request.Candidates.Count==1)rows.Add(("출처",PlayParts.Plain(PlaySource.Label(play.Source)),26));
  else if(request.Candidates.Count>1)
  {
   sources=new ThemedCombo(){DropDownStyle=ComboBoxStyle.DropDownList,Width=ContentWidth-28-PlayParts.NameWidth-70,Roomy=true,AccessibleName="삭제할 출처"};
   foreach(var candidate in request.Candidates)sources.Items.Add(PlaySource.Label(candidate.Source));
   sources.SelectedIndex=0;rows.Add(("삭제할 출처",sources,34));
  }
  box.Height=PlayParts.Rows(box,14,11+song.Height+6,rows.ToArray())+10;
  Body.Controls.Add(box);
  emptied=Boxes.Text(string.Join("\n",Notes(request,play)),null,9.75f,false,ContentWidth);emptied.Margin=new(0,0,0,16);emptied.Visible=emptied.Text.Length>0;Body.Controls.Add(emptied);
  if(sources!=null)sources.SelectedIndexChanged+=(_,_)=>RefreshNotes();
  // 이 창이 떠 있는 동안에도 기록은 바뀔 수 있다(등록을 확인하는 단축키로 새 판이 저장되는 경우). 안내 줄만 지금의 기록으로 다시 적는다:
  // '미등록이 됩니다'는 그 판정에 남을 판이 생기면 사라진다. 지울 판은 그대로이고 승인을 다시 받지도 않는다(그 판이 바뀌었는지는 승인한 뒤에 히스토리가 본다)
  watch.Tick+=(_,_)=>RefreshNotes();VisibleChanged+=(_,_)=>watch.Enabled=Visible;
  var delete=new Button(){Text=request.Reflected?"반영만 삭제":"삭제",DialogResult=DialogResult.Yes,Tag="danger"};var cancel=new Button(){Text="취소",DialogResult=DialogResult.No,Tag="primary"};
  Body.Controls.Add(Parts.Footer(ContentWidth,delete,cancel));
  AcceptButton=cancel;CancelButton=cancel;Shown+=(_,_)=>ActiveControl=cancel;
 }
}

/// <summary>가져온 기록의 일괄 삭제 확인(수동 기록 설정의 'V아카이브 삭제'·'컬렉션 삭제'). 지금 프로필과 지울 건수를 보인다. 처음 선택은 취소다(연 직후의 Enter·Esc·닫기는 모두 취소)</summary>
class BulkDeleteDialog:ChoiceDialog
{
 public const int ContentWidth=386;
 public const string Title="V아카이브 일괄 삭제",Question="V아카이브에서 가져온 기록을 모두 삭제할까요?",Note="삭제한 기록은 ‘직전 저장 되돌리기’로 한 번에 복원할 수 있습니다.";
 /// <summary>컬렉션의 일괄 삭제(창 제목은 V아카이브와 같은 꼴)</summary>
 public const string CollectionTitle="컬렉션 일괄 삭제",CollectionQuestion="컬렉션에서 가져온 기록을 모두 삭제할까요?";
 public static string TitleOf(string source)=>source==PlaySource.Collection?CollectionTitle:Title;
 /// <summary>수동 기록 설정의 바닥줄에서 그 출처를 지우는 단추의 이름</summary>
 public static string ButtonOf(string source)=>source==PlaySource.Collection?"컬렉션 삭제":"V아카이브 삭제";
 public BulkDeleteDialog(string profile,int count,string source=PlaySource.VArchive):base(TitleOf(source))
 {
  Body.Padding=new(18,16,18,16);
  var question=Parts.TitleLine(source==PlaySource.Collection?CollectionQuestion:Question,ContentWidth);question.Margin=new(0,0,0,12);Body.Controls.Add(question);
  var name=new List<InkPart>{new(profile,Ink.Body(15f,true),Theme.Text)};
  var number=new List<InkPart>{new($"{count:N0}",Ink.Number(20f),Theme.Text),new("건",Ink.Body(12f,true),Theme.Text,1)};
  var box=PlayParts.Pairs(ContentWidth,32,new (string,Control)[]{("현재 프로필",new InkLabel(()=>name,Ink.Body(15f,true),24){AccessibleName="현재 프로필 "+profile}),("삭제할 기록",new InkLabel(()=>number,Ink.Number(20f),28){AccessibleName=$"삭제할 기록 {count:N0}건"})});
  Body.Controls.Add(box);
  var note=Boxes.Text(Note,null,9.75f,false,ContentWidth);note.Margin=new(0,12,0,16);Body.Controls.Add(note);
  var delete=new Button(){Text="삭제",DialogResult=DialogResult.Yes,Tag="danger"};var cancel=new Button(){Text="취소",DialogResult=DialogResult.No,Tag="primary"};
  Body.Controls.Add(Parts.Footer(ContentWidth,delete,cancel));
  AcceptButton=cancel;CancelButton=cancel;Shown+=(_,_)=>ActiveControl=cancel;
 }
}
