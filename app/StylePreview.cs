using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Text.Json;
namespace LadderRecords;

// 오버레이 디자인 설정 창의 미리보기: OBS 오버레이(app\overlay.html)의 장면과 연출을 창 안에 다시 그린다(창 안에 브라우저를 넣지 않고 C# 으로 그린다).
// 오버레이 페이지와 따로 구현한 것이다. **overlay.html 의 치수·색·시간을 고치면 여기도 함께 고친다.** 아래의 수치는 그 페이지의 CSS 값을 540×276 의 픽셀로 옮긴 것이다(1u = 5.4px).
// 다른 점: Oxanium 은 창에 실은 굵기가 보통(400)과 굵게(700)뿐이라 550·600·650 을 굵게로, 500 을 보통으로 그린다. 숫자의 칸 너비 고정(tabular-nums)은 쓰지 않는다.
// 그림자 빛(노란빛·괄호의 번짐)은 옅은 겹으로 흉내 낸다. 장면이 겹쳐 바뀌는 짧은 동안의 흐림은 묶음이 아니라 글자·꼬리표마다 따로 옅어진다.
// 보이는 점수는 고정된 예시다: 이번 점수 994,372(99.43%), 기존 최고는 달성·경신 993,900, 미달성·유지 996,800. 곡과 패턴은 창이 정해 준다(정해 주지 않으면 '아침형 인간' 8B SC 하드판정).
// '효과 다시 보기'의 차례:
//   달성·미달성·미등록  곡 이름이 써지고 꼬리표가 차례로 나타나고 구분선 위로 노란빛이 지나간다
//   악곡 감지 중        0.16초에 드러난다
//   점수 확인 중        플레이 화면에서 0.14초 뒤 바뀌며 넓은 노란빛이 지나간다
//   점수 확인 실패      점수 확인 중에서 0.14초 뒤 바뀌고 3초 뒤 플레이 화면으로 돌아간다
//   등록 확인           점수 확인 중에서 0.14초 뒤 바뀌며 노란 괄호가 모였다 사라진다
//   저장 결과 셋        결과 문구 1.5초 → 점수 상세 6초 → 저장이 반영된 플레이 화면
// Windows 의 '애니메이션 효과'를 꺼 둔 PC 에서는 장면이 연출 없이 바뀐다(차례와 시간은 같다).
class StylePreview:Control
{
 public const int BaseWidth=496;                       // 100% 때의 너비(체크무늬 틀 포함). 높이는 BaseHeight
 public static int BaseHeight=>(int)Math.Round(12+(BaseWidth-12)*H/W);
 const float W=540,H=276,U=5.4f;
 const float EdgeLeft=4.8f*U,EdgeRight=W-4.8f*U,Inner=EdgeRight-EdgeLeft;
 public const string DefaultSong="아침형 인간";
 const int NewScore=994372;

 readonly Func<JsonElement> appearance;
 readonly string confirmKey,cancelKey;
 readonly System.Windows.Forms.Timer motion=new(){Interval=15};
 long started;bool replaying,reduced;
 Bitmap? layer;
 [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public bool ShowCurrent{get;set;}
 /// <summary>예시로 보일 곡 이름. 긴 이름은 오버레이처럼 끝을 "…" 으로 줄인다</summary>
 [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public string SongText{get;set;}=DefaultSong;
 [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public string ButtonText{get;set;}="8B";
 [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public string PatternText{get;set;}="SC";
 [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public string JudgeText{get;set;}="하드";
 /// <summary>보일 상태: 0 달성, 1 미달성, 2 미등록, 3 악곡 감지 중, 4 점수 확인 중, 5 점수 확인 실패, 6 등록 확인, 7 첫 기록 등록, 8 최고 기록 경신, 9 최고 기록 유지</summary>
 [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public int Mode{get;set;}
 /// <summary>지금 연출을 돌리는 중인가</summary>
 public bool Playing=>motion.Enabled;

 public StylePreview(Func<JsonElement> appearance,bool showCurrent,List<Hotkey>? shortcuts=null)
 {
  var keys=shortcuts??Hotkeys.Defaults();
  confirmKey=Hotkeys.Format(keys[2]);cancelKey=Hotkeys.Format(keys[3]);
  this.appearance=appearance;ShowCurrent=showCurrent;
  SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
  Size=new(BaseWidth,BaseHeight);TabStop=false;Margin=Padding.Empty;
  motion.Tick+=(_,_)=>{if(Clock is not double t||t>=End)motion.Stop();Invalidate();};
 }
 /// <summary>고른 상태를 보인다. replay 면 그 상태가 될 때의 연출을 처음부터 돌리고, 아니면 완성된 모습을 바로 보인다</summary>
 public void Play(bool replay)
 {
  replaying=replay;reduced=Motion.Reduced;started=System.Diagnostics.Stopwatch.GetTimestamp();
  if(replay)motion.Start();else motion.Stop();
  Invalidate();
 }
 // 연출을 시작한 뒤로 지난 시간(ms). 연출을 돌리지 않으면 null(완성된 모습)
 double? Clock=>replaying?System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds:null;
 // 이 상태의 연출이 다 끝나는 때(ms). 그 뒤로는 화면이 바뀌지 않는다
 double End=>Mode switch{<=2=>TypingFrames.Timing(TypingFrames.Of(SongText).Count).Total*1000+60,3=>220,4=>1150,5=>3500,6=>1050,_=>7760};
 protected override void Dispose(bool disposing){if(disposing){motion.Dispose();layer?.Dispose();}base.Dispose(disposing);}

 // ── 글꼴 ──
 static readonly Dictionary<(FontFamily Family,float Px,bool Bold),Font> faces=new();   // 글꼴 집합은 이름이 같아도 꺼낸 모음이 다르면 다른 것이다(AppFonts.UiRegular)
 static Font Face(FontFamily family,float px,bool bold)
 {
  var key=(family,px,bold);if(faces.TryGetValue(key,out var font))return font;
  var style=bold?FontStyle.Bold:FontStyle.Regular;
  if(!family.IsStyleAvailable(style))style=bold?FontStyle.Regular:FontStyle.Bold;   // SUITE 는 굵은 꼴만 실려 있다
  return faces[key]=new Font(family,px,style,GraphicsUnit.Pixel);
 }
 // 글꼴의 차례(앞선 글꼴에 없는 글자는 다음 글꼴로 그린다. 페이지의 font-family 와 같은 차례)
 static Font[] Title(float px)=>new[]{Face(AppFonts.Latin,px,true),Face(AppFonts.Head,px,true),Face(AppFonts.Wide,px,true),Face(AppFonts.Ui,px,true)};
 static Font[] Number(float px,bool bold=true)=>bold?new[]{Face(AppFonts.Latin,px,true),Face(AppFonts.Ui,px,true)}:KeyName(px);
 static Font[] Heading(float px)=>new[]{Face(AppFonts.Head,px,true),Face(AppFonts.Ui,px,true)};
 static Font[] Plain(float px)=>new[]{Face(AppFonts.UiRegular,px,false)};
 static Font[] Medium(float px)=>new[]{Face(AppFonts.UiMedium,px,false)};
 static Font[] Caption(float px)=>new[]{Face(AppFonts.LatinRegular,px,false),Face(AppFonts.Head,px,true),Face(AppFonts.Wide,px,false),Face(AppFonts.UiRegular,px,false)};
 static readonly Lazy<FontFamily> Symbols=new(()=>{try{return new FontFamily("Segoe UI Symbol");}catch(ArgumentException){return AppFonts.Ui;}});

 // 한 줄: 조각들을 같은 밑줄에 놓는다. 줄의 높이는 조각마다 '글자 크기 × 줄 높이 배수'이고, 글자는 그 안의 세로 가운데에 온다(브라우저의 줄 상자와 같은 셈)
 sealed class TextLine
 {
  readonly List<(MixedLine Line,float X,float Width,Color Ink)> parts=new();
  public float Width,Above,Below;
  public float Height=>Above+Below;
  /// <summary>글자 없이 줄의 높이에만 끼는 글꼴(블록의 기본 글꼴)</summary>
  public TextLine Strut(Font font,float lineHeight)
  {
   var family=font.FontFamily;float em=family.GetEmHeight(font.Style),up=font.Size*family.GetCellAscent(font.Style)/em,down=font.Size*family.GetCellDescent(font.Style)/em,half=(lineHeight-(up+down))/2;
   Above=Math.Max(Above,up+half);Below=Math.Max(Below,down+half);return this;
  }
  /// <param name="lineHeight">줄 높이(픽셀)</param>
  /// <param name="spacing">글자 사이(픽셀. 마지막 글자 뒤에도 붙는다)</param>
  /// <param name="before">이 조각 앞의 빈 자리</param>
  public TextLine Add(string text,Font[] chain,Color ink,float lineHeight,float spacing=0,float before=0)
  {
   Strut(chain[0],lineHeight);
   var line=MixedLine.Plain(text,chain,spacing);float width=line.Width+(spacing!=0&&text.Length>0?spacing:0);
   parts.Add((line,Width+before,width,ink));Width+=before+width;return this;
  }
  public void Draw(Graphics g,float x,float baseline,float alpha=1)
  {
   foreach(var part in parts){using var brush=new SolidBrush(Fade(part.Ink,alpha));part.Line.Draw(g,brush,x+part.X,baseline);}
  }
  /// <summary>줄 상자의 위쪽이 top 인 자리에, 가운데를 center 에 맞춰 그린다</summary>
  public void Center(Graphics g,float center,float top,float alpha=1)=>Draw(g,center-Width/2,top+Above,alpha);
 }
 static Color Fade(Color color,float alpha)=>alpha>=1?color:Color.FromArgb((int)Math.Round(color.A*Math.Clamp(alpha,0,1)),color);
 static Color Whiter(Color color,double amount)=>Color.FromArgb(color.A,(int)Math.Round(color.R+(255-color.R)*amount),(int)Math.Round(color.G+(255-color.G)*amount),(int)Math.Round(color.B+(255-color.B)*amount));
 static float Ease(double t,double start,double ms)=>(float)Motion.EaseOut(Math.Clamp((t-start)/ms,0,1));

 protected override void OnPaint(PaintEventArgs e)
 {
  var g=e.Graphics;g.Clear(Parent?.BackColor??Theme.Window);
  float k=Width/(float)BaseWidth,pad=6*k;
  var frame=new RectangleF(0,0,Width-1,Height-1);
  // 체크무늬 바탕(투명도를 그 위에서 본다): 9px 칸, 테두리 한 줄
  var state=g.Save();g.SmoothingMode=SmoothingMode.AntiAlias;
  using(var shape=BadgeDesign.Rounded(frame,5*k))
  {
   g.SetClip(shape,CombineMode.Intersect);
   using var light=new SolidBrush(Color.FromArgb(0xE2,0xE3,0xE5));using var dark=new SolidBrush(Color.FromArgb(0xBF,0xC1,0xC5));
   float cell=9*k;g.SmoothingMode=SmoothingMode.None;
   for(int row=0;row*cell<Height;row++)for(int column=0;column*cell<Width;column++)g.FillRectangle((row+column)%2==0?light:dark,column*cell,row*cell,cell+1,cell+1);
   g.Restore(state);state=g.Save();g.SmoothingMode=SmoothingMode.AntiAlias;
   using var pen=new Pen(Theme.Line);g.DrawPath(pen,shape);
  }
  g.Restore(state);
  float width=Width-2*pad;
  DrawOverlay(g,new RectangleF(pad,pad,width,width*H/W),Clock);
 }

 /// <summary>오버레이 한 장면을 area 에 그린다(가로 540 : 세로 276 의 비율이어야 한다). clock 은 연출을 시작한 뒤로 지난 시간(ms)이고 null 이면 완성된 모습이다</summary>
 public void DrawOverlay(Graphics g,RectangleF area,double? clock)
 {
  var look=OverlayStyle.Look(appearance());
  float scale=area.Width/W;
  var state=g.Save();var home=g.Transform;
  g.TranslateTransform(area.X,area.Y);g.ScaleTransform(scale,scale);
  Ready(g);
  using(var box=new GraphicsPath())
  {
   // 모서리: 가로 2u, 세로 2.05u 의 둥근 꼴
   float rx=2*U*2,ry=2.05f*U*2;
   box.AddArc(0,0,rx,ry,180,90);box.AddArc(W-rx,0,rx,ry,270,90);box.AddArc(W-rx,H-ry,rx,ry,0,90);box.AddArc(0,H-ry,rx,ry,90,90);box.CloseFigure();
   g.SetClip(box,CombineMode.Intersect);
   using var back=new SolidBrush(Color.FromArgb((int)Math.Round(255*(1-look.BackgroundTransparency/100f)),look.Background));g.FillPath(back,box);
  }
  float ink=1-look.TextTransparency/100f;
  if(ink>=1)Content(g,look,clock);
  else if(ink>0)
  {
   // 글자 투명도는 내용 전체를 한 묶음으로 옅게 한다(꼬리표의 바탕과 글자가 따로 비치지 않게). 따로 그린 그림을 옅게 덮는다
   int wide=Math.Max(1,(int)Math.Ceiling(area.Width)),tall=Math.Max(1,(int)Math.Ceiling(area.Height));
   if(layer==null||layer.Width!=wide||layer.Height!=tall){layer?.Dispose();layer=new Bitmap(wide,tall,PixelFormat.Format32bppPArgb);}
   using(var inner=Graphics.FromImage(layer)){inner.Clear(Color.Transparent);inner.ScaleTransform(scale,scale);Ready(inner);Content(inner,look,clock);}
   using var attributes=new ImageAttributes();attributes.SetColorMatrix(new ColorMatrix{Matrix33=ink});
   g.Transform=home;
   g.DrawImage(layer,new Rectangle((int)Math.Round(area.X),(int)Math.Round(area.Y),wide,tall),0,0,wide,tall,GraphicsUnit.Pixel,attributes);
  }
  home.Dispose();g.Restore(state);
 }
 static void Ready(Graphics g){g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=TextRenderingHint.AntiAlias;g.PixelOffsetMode=PixelOffsetMode.HighQuality;}

 // ── 장면 ──
 void Content(Graphics g,OverlayLook look,double? clock)
 {
  bool still=clock==null,cut=reduced;double t=clock??0;
  if(Mode<7){Game(g,look,clock,1,false);return;}
  // 저장 결과: 플레이 화면 → 결과 문구(0.115초에 흐려지고 0.055초 뒤부터 0.21초에 드러난다) → 1.5초에 점수 상세 → 7.5초에 플레이 화면(0.2초 동안 함께 바뀐다)
  float game=0,intro=0,detail=0;bool saved=false;
  if(still)detail=1;
  else if(t<1500){game=cut?0:1-Ease(t,0,115);intro=cut?1:Ease(t,55,210);}
  else if(t<7500){intro=cut?0:1-Ease(t,1500,115);detail=cut?1:Ease(t,1555,210);}
  else{saved=true;detail=cut?0:1-Ease(t,7500,200);game=cut?1:Ease(t,7500,200);}
  if(game>0)Game(g,look,null,game,saved);
  if(intro>0)Intro(g,look,intro,cut?double.NaN:t);
  if(detail>0)Detail(g,look,detail,still||cut?double.NaN:t-1500);
 }

 // 플레이 화면: 윗칸(곡명·꼬리표 또는 감지 문구), 구분선, 아랫칸(개인 최고·현재 / 점수 확인 중 / 등록 확인 / 확인 실패)
 void Game(Graphics g,OverlayLook look,double? clock,float alpha,bool saved)
 {
  bool still=clock==null||reduced;double t=clock??0;
  bool detected=Mode!=3;
  if(!detected&&!still)alpha*=Ease(t,0,160);
  // 기존 최고와 이번 점수. 저장 결과에서 돌아온 플레이 화면은 저장이 반영된 값이다
  int previous=Mode switch{1 or 9=>996800,2 or 7=>0,_=>993900};
  if(saved)previous=Math.Max(previous,NewScore);
  decimal? goal=!detected?null:previous==0?null:previous/100/100m,current=detected?NewScore/100/100m:null;

  float headingBottom=3.15f*U+1.08f*6.5f*U+1.95f*U+6*U+2.6f*U;   // 윗칸의 높이(위 여백 + 곡명 줄 + 사이 + 꼬리표 + 아래 여백)
  if(detected)
  {
   // 곡 정보가 나타나는 연출(달성·미달성·미등록의 '효과 다시 보기')
   string name=SongText;double reveal=double.NaN;
   if(!still&&Mode<=2)
   {
    var frames=TypingFrames.Of(SongText);var (gap,total)=TypingFrames.Timing(frames.Count);
    if(t/1000<total){reveal=t;name=frames[Math.Min(frames.Count-1,(int)(t/1000/gap))];}
   }
   float top=3.15f*U;
   var title=new TextLine().Add(Fit(name,Title(6.5f*U),-.13f*U,Inner),Title(6.5f*U),look.Text,1.08f*6.5f*U,-.13f*U);
   title.Draw(g,EdgeLeft,top+title.Above,alpha);
   float x=EdgeLeft,tagTop=top+1.08f*6.5f*U+1.95f*U;int index=0;
   foreach(string text in new[]{ButtonText,PatternText,JudgeText+"판정"})
   {
    float shown=1,drop=0;
    if(!double.IsNaN(reveal)){double p=(reveal-155*index)/150;shown=p<0?0:(float)Motion.EaseOut(Math.Min(1,p));drop=(1-shown)*.5f*U;}
    x+=Badge(g,text,x,tagTop+drop,alpha*shown)+.9f*U;index++;
   }
   if(!double.IsNaN(reveal)&&reveal<920)
   {
    // 구분선 위를 지나는 노란빛: 너비의 44% 인 막대가 왼쪽 밖에서 오른쪽 밖으로(0.92초)
    float bar=.44f*Inner,shift=(float)(-1+3.32*Motion.Bezier(reveal/920,.28,.04,.34,1)),thick=Math.Max(2,.76f*U);
    Color[] colors={Color.FromArgb(0,0xFF,0xC6,0x2E),Color.FromArgb(0xFF,0xC6,0x2E),Color.FromArgb(0xFF,0xDF,0x45),Color.FromArgb(0xFF,0xF5,0xA1),Color.FromArgb(0xFF,0xDA,0x32),Color.FromArgb(0,0xFF,0xDA,0x32)};
    float[] stops={0,.42f,.70f,.88f,.96f,1};
    Motion.DrawGlint(g,new RectangleF(EdgeLeft,headingBottom+.5f-thick/2-.54f*U,Inner,thick+1.08f*U),bar,shift,colors,stops,.25*alpha);   // 번진 빛
    Motion.DrawGlint(g,new RectangleF(EdgeLeft,headingBottom+.5f-thick/2,Inner,thick),bar,shift,colors,stops,alpha);
   }
  }
  else
  {
   // 감지 문구: 윗칸의 세로 가운데, 왼쪽
   var waiting=new TextLine().Add("악곡을 감지하고 있습니다",Heading(7*U),look.Text,1.08f*7*U,-.13f*U);
   waiting.Draw(g,EdgeLeft,(headingBottom-waiting.Height)/2+waiting.Above,alpha);
  }
  using(var line=new SolidBrush(Fade(Color.FromArgb(51,look.Text),alpha)))g.FillRectangle(line,EdgeLeft,headingBottom,Inner,1);

  // 아랫칸의 자리(구분선 한 줄과 2.6u 를 띄운 아래)
  float lowerTop=headingBottom+1+2.6f*U,lowerHeight=H-3.4f*U-lowerTop;
  // 아랫칸이 바뀌는 차례: 앞의 것이 0.12초에 흐려지고 새것이 0.06초 뒤부터 0.19초에 드러난다
  string last=Mode switch{4=>"check",5=>"failure",6=>"confirm",_=>"play"};
  (double At,string Name)[] steps=clock==null||Mode<4||Mode>6?new[]{(0d,last)}:Mode==4?new[]{(0d,"play"),(140d,"check")}:Mode==5?new[]{(0d,"check"),(140d,"failure"),(3140d,"play")}:new[]{(0d,"check"),(140d,"confirm")};
  int now=0;for(int i=1;i<steps.Length;i++)if(t>=steps[i].At)now=i;
  double since=t-steps[now].At;bool moving=now>0&&!reduced;   // 움직임을 줄인 PC 에서는 차례와 시간은 같고 곧바로 바뀐다
  if(moving&&since<120)Lower(g,look,steps[now-1].Name,alpha*(float)(1-Motion.EaseIn(since/120)),lowerTop,lowerHeight,goal,current,detected,double.NaN);
  float entering=moving?Ease(since,60,190):1;
  if(moving&&steps[now].Name=="check")
  {
   // 넓은 노란빛: 구분선 아래부터 바닥까지, 양쪽 가장자리까지. 0.045초 뒤부터 0.88초 동안 글자 뒤로 지나간다
   double p=(since-45)/880;
   if(p>0&&p<1)Sweep(g,new RectangleF(0,headingBottom+1,W,H-headingBottom-1),p,Yellow,alpha);
  }
  if(entering>0)Lower(g,look,steps[now].Name,alpha*entering,lowerTop,lowerHeight,goal,current,detected,moving&&steps[now].Name=="confirm"?since:double.NaN);
 }

 // 글자가 넘치면 끝을 "…" 으로 줄인다(페이지의 text-overflow:ellipsis)
 static string Fit(string text,Font[] chain,float spacing,float room)
 {
  float Wide(string s)=>MixedLine.Plain(s,chain,spacing).Width+spacing;
  if(Wide(text)<=room)return text;
  var e=System.Globalization.StringInfo.ParseCombiningCharacters(text);
  for(int keep=e.Length-1;keep>0;keep--){string cut=text[..e[keep]]+"…";if(Wide(cut)<=room)return cut;}
  return "…";
 }
 // 꼬리표 하나를 그리고 그 너비를 돌려준다
 static float Badge(Graphics g,string text,float x,float top,float alpha)
 {
  var chain=new[]{Face(AppFonts.Latin,3.52f*U,true),Face(AppFonts.Head,3.52f*U,true)};
  var line=new TextLine().Add(text,chain,BadgeDesign.Foreground(text),3.52f*U);
  float width=Math.Max(9.2f*U,line.Width+2*1.9f*U),height=6*U;
  if(alpha<=0)return width;
  using(var shape=BadgeDesign.Rounded(new RectangleF(x,top,width,height),1.05f*U))using(var fill=new SolidBrush(Fade(BadgeDesign.Background(text),alpha)))g.FillPath(fill,shape);
  bool english=text.All(c=>c<0x80);   // 영문·숫자만 조금 내려야 꼬리표의 가운데에 온다
  line.Center(g,x+width/2,top+(height-line.Height)/2+(english?.32f*U:0),alpha);
  return width;
 }

 void Lower(Graphics g,OverlayLook look,string name,float alpha,float top,float height,decimal? goal,decimal? current,bool detected,double locking)
 {
  if(alpha<=0)return;
  float center=W/2;
  switch(name)
  {
   case "play":Scores(g,look,alpha,top,height,goal,current,detected);break;
   case "check":
   case "failure":
   {
    bool failed=name=="failure";float size=(failed?5.5f:6.2f)*U,gap=(failed?2f:2.3f)*U;
    var message=new TextLine().Add(failed?"플레이 중 확인한 점수가 부족합니다":"점수를 확인하고 있습니다…",Plain(size),look.Text,1.25f*size);
    var hint=new TextLine().Strut(Plain(4.2f*U)[0],1.3f*4.2f*U);
    if(failed)hint.Add("잠시 후 자동으로 취소됩니다",Plain(4.2f*U),look.Text,1.3f*4.2f*U);
    else hint.Add("취소 (",Plain(4.2f*U),look.Text,1.3f*4.2f*U).Add(cancelKey,KeyName(4.2f*U),look.Text,1.3f*4.2f*U).Add(")",Plain(4.2f*U),look.Text,1.3f*4.2f*U);
    float y=top+(height-(message.Height+gap+hint.Height))/2;
    message.Center(g,center,y,alpha);hint.Center(g,center,y+message.Height+gap,alpha);
    break;
   }
   case "confirm":
   {
    var question=new TextLine().Add("등록하시겠습니까?",Heading(5.4f*U),look.Text,1.16f*5.4f*U);
    float size=8.15f*U,line=1.08f*size,spacing=-.2f*U;
    var raw=new TextLine().Add(NewScore.ToString("N0",System.Globalization.CultureInfo.InvariantCulture),Number(size),look.Text,line,spacing);
    var rate=new TextLine().Add("99.43%",Number(size),look.Text,line,spacing);
    var keys=new TextLine().Strut(Plain(3.85f*U)[0],1.23f*3.85f*U);
    keys.Add("등록 (",Plain(3.85f*U),look.Text,1.23f*3.85f*U).Add(confirmKey,KeyName(3.85f*U),look.Text,1.23f*3.85f*U).Add(") / 취소 (",Plain(3.85f*U),look.Text,1.23f*3.85f*U).Add(cancelKey,KeyName(3.85f*U),look.Text,1.23f*3.85f*U).Add(")",Plain(3.85f*U),look.Text,1.23f*3.85f*U);
    float gap=1.8f*U,y=top+(height-(question.Height+gap+line+gap+keys.Height))/2;
    question.Center(g,center,y,alpha);
    // SCORE │ RATE: 가운데 세로선의 양쪽에 2.2u 씩 띄운다
    float row=y+question.Height+gap,column=(Inner-1-2*2.2f*U)/2,rawRight=EdgeLeft+column,rateLeft=rawRight+2*2.2f*U+1;
    float baseline=row+(line-raw.Height)/2+raw.Above;
    raw.Draw(g,rawRight-raw.Width,baseline,alpha);rate.Draw(g,rateLeft,baseline,alpha);
    using(var divider=new SolidBrush(Fade(Color.FromArgb(161,look.Text),alpha)))g.FillRectangle(divider,rawRight+2.2f*U,row+(line-.74f*size)/2,1,.74f*size);
    keys.Center(g,center,row+line+gap,alpha);
    if(!double.IsNaN(locking))
    {
     // 노란 괄호 한 쌍: 0.085초 뒤부터 0.74초 동안 바깥에서 모였다 사라진다(글자와의 사이는 글자 크기의 0.18배)
     double p=(locking-85)/740;
     if(p>0&&p<1)
     {
      static double Curve(double x)=>Motion.Bezier(Math.Clamp(x,0,1),.22,.65,.25,1);
      float away=(float)(p<.46?.42*size*(1-Curve(p/.46)):0),shown=(float)(p<.22?Curve(p/.22):p<.73?1:1-Curve((p-.73)/.27))*alpha;
      float bracketTop=row+.11f*line-.075f*size,bracketHeight=line-(.11f*line-.075f*size)-(.10f*line+.075f*size),wide=.23f*size;
      Bracket(g,rawRight-raw.Width-.18f*size-wide-away,bracketTop,wide,bracketHeight,true,shown);
      Bracket(g,rateLeft+rate.Width+.18f*size+away,bracketTop,wide,bracketHeight,false,shown);
     }
    }
    break;
   }
  }
 }
 static Font[] KeyName(float px)=>new[]{Face(AppFonts.LatinRegular,px,false),Face(AppFonts.UiRegular,px,false)};
 static void Bracket(Graphics g,float x,float y,float width,float height,bool left,float alpha)
 {
  if(alpha<=0)return;
  float thin=Math.Max(1,.42f*U),thick=Math.Max(1,.55f*U),glow=.36f*U;
  using(var halo=new SolidBrush(Fade(Color.FromArgb(70,0xEF,0xA9,0x22),alpha)))
  {
   g.FillRectangle(halo,x-glow,y-glow,width+2*glow,thin+2*glow);g.FillRectangle(halo,x-glow,y+height-thin-glow,width+2*glow,thin+2*glow);
   g.FillRectangle(halo,(left?x:x+width-thick)-glow,y+thin+glow,thick+2*glow,height-2*thin-2*glow);
  }
  using var edge=new SolidBrush(Fade(Color.FromArgb(0xFF,0xE3,0x63),alpha));using var spine=new SolidBrush(Fade(Color.FromArgb(0xFF,0xF0,0xA0),alpha));
  g.FillRectangle(edge,x,y,width,thin);g.FillRectangle(edge,x,y+height-thin,width,thin);
  g.FillRectangle(spine,left?x:x+width-thick,y,thick,height);
 }

 // 개인 최고와 현재 점수의 두 줄. 달성·미달성의 색은 한 줄에만 입힌다: 현재 점수를 보이면 현재 줄, 보이지 않으면 개인 최고 줄
 void Scores(Graphics g,OverlayLook look,float alpha,float top,float height,decimal? goal,decimal? current,bool detected)
 {
  Color? tone=goal==null||current==null?null:current>=goal?look.Above:look.Below;
  Color goalInk=tone!=null&&!ShowCurrent?tone.Value:look.Text,currentInk=tone!=null&&ShowCurrent?tone.Value:look.Text;
  float goalSize=8.8f*U,currentSize=8*U;
  var goalLabel=new TextLine().Add("개인 최고 점수:",Medium(6.3f*U),goalInk,6.3f*U);
  var goalValue=Value(goal,detected,goalSize,goalInk);
  float goalAbove=Math.Max(goalLabel.Above,goalValue.Above),goalHeight=goalAbove+Math.Max(goalLabel.Below,goalValue.Below);
  var currentLabel=new TextLine().Add("현재 점수:",Medium(5.355f*U),currentInk,5.355f*U);
  var currentValue=Value(current,detected,currentSize,currentInk);
  float currentAbove=Math.Max(currentLabel.Above,currentValue.Above),currentHeight=currentAbove+Math.Max(currentLabel.Below,currentValue.Below);
  float y=top+(height-(goalHeight+(ShowCurrent?2*U+currentHeight:0)))/2;
  float baseline=y+goalAbove;
  goalLabel.Draw(g,EdgeLeft,baseline,alpha);
  // 개인 최고의 숫자는 현재보다 15% 크다: 숫자 칸의 오른쪽 가운데를 붙잡고 1.15/1.1 배로 키운다
  float middle=baseline-goalValue.Above+goalSize/2;
  if(detected&&goal==null)
  {
   // 미등록: 숫자 자리의 세로 가운데에 현재 숫자의 크기로
   var none=new TextLine().Add("미등록",Heading(8*U),goalInk,8*U,-.035f*8*U);
   none.Draw(g,EdgeRight-none.Width,middle-none.Height/2+none.Above,alpha);
  }
  else
  {
   var state=g.Save();
   g.TranslateTransform(EdgeRight,middle);g.ScaleTransform(1.15f/1.1f,1.15f/1.1f);g.TranslateTransform(-EdgeRight,-middle);
   goalValue.Draw(g,EdgeRight-goalValue.Width,baseline,alpha);
   g.Restore(state);
  }
  if(!ShowCurrent)return;
  baseline=y+goalHeight+2*U+currentAbove+1.1f*U;   // 현재 줄은 문구와 숫자를 함께 1.1u 내린다
  currentLabel.Draw(g,EdgeLeft,baseline,alpha);currentValue.Draw(g,EdgeRight-currentValue.Width,baseline,alpha);
 }
 // 숫자 줄의 값: 값이 없으면 대시만 보이고 % 를 감춘다. % 는 숫자의 0.64배 크기다
 static TextLine Value(decimal? value,bool detected,float size,Color ink)
 {
  var line=new TextLine();
  if(value==null&&!detected)return line.Add("—",Number(size),ink,size,-.32f*U);
  line.Add((value??0).ToString("0.00",System.Globalization.CultureInfo.InvariantCulture),Number(size),ink,size,-.32f*U);
  return line.Add("%",Number(.64f*size),ink,.64f*size,-.18f*U,.14f*U);
 }

 // 왼쪽 밖에서 오른쪽 밖으로 한 번 지나가는 넓은 빛(100도 기운 띠). 처음 12% 동안 나타나고 마지막 15% 동안 사라진다
 static readonly (Color Color,float Alpha)[] Yellow={(Color.FromArgb(255,192,20),.09f),(Color.FromArgb(255,198,26),.27f),(Color.FromArgb(255,220,70),.40f),(Color.FromArgb(255,235,133),.46f),(Color.FromArgb(255,207,39),.19f)};
 static void Sweep(Graphics g,RectangleF box,double progress,(Color Color,float Alpha)[] tones,float alpha)
 {
  float shown=(float)(progress<.12?progress/.12:progress<.85?1:(1-progress)/.15)*alpha;if(shown<=0)return;
  float move=(float)(-1+2*progress)*box.Width;
  double angle=100*Math.PI/180;float dx=(float)Math.Sin(angle),dy=(float)-Math.Cos(angle),length=Math.Abs(box.Width*dx)+Math.Abs(box.Height*dy);
  float cx=box.X+box.Width/2+move,cy=box.Y+box.Height/2;
  var from=new PointF(cx-dx*length/2,cy-dy*length/2);var to=new PointF(cx+dx*length/2,cy+dy*length/2);
  Color Tone(int index,bool clear=false)=>Color.FromArgb(clear?0:(int)Math.Round(255*tones[index].Alpha*shown),tones[index].Color);
  using var brush=new LinearGradientBrush(from,to,Color.Black,Color.Black)
  {
   InterpolationColors=new ColorBlend{Colors=new[]{Tone(0,true),Tone(0,true),Tone(0),Tone(1),Tone(2),Tone(3),Tone(4),Tone(4,true),Tone(4,true)},Positions=new[]{0,.04f,.23f,.47f,.65f,.71f,.83f,.96f,1}},
   WrapMode=WrapMode.TileFlipXY,
  };
  var state=g.Save();g.SetClip(box,CombineMode.Intersect);
  g.FillRectangle(brush,RectangleF.Intersect(box,new RectangleF(box.X+move,box.Y,box.Width,box.Height)));
  g.Restore(state);
 }

 // 저장 결과의 첫 장면: 영문 꼬리말과 결과 문구. 첫 기록은 하늘색 빛, 경신은 달성 색의 빛과 문구 양옆의 작은 위 화살표, 유지는 빛 없이 나타나기만 한다
 void Intro(Graphics g,OverlayLook look,float alpha,double t)
 {
  bool first=Mode==7,best=Mode==8;
  Color tone=first?Color.FromArgb(0x86,0xDC,0xFF):best?look.Above:Color.FromArgb(0xB7,0xD8,0xFF);
  if(!double.IsNaN(t)&&(first||best))
  {
   double p=(t-45)/520;
   Color beam=best?look.Above:Color.FromArgb(96,202,255),crest=best?Whiter(look.Above,.5):Color.FromArgb(190,240,255);
   if(p>0&&p<1)Sweep(g,new RectangleF(0,0,W,H),p,new[]{(beam,.09f),(beam,.27f),(beam,.40f),(crest,.46f),(beam,.19f)},alpha);
  }
  float tagSize=4.8f*U,nameSize=9.55f*U;
  var tag=new TextLine().Add(first?"FIRST SCORE SAVED":best?"NEW PERSONAL BEST":"PERSONAL BEST UNCHANGED",Number(tagSize),tone,1.18f*tagSize,.07f*tagSize);
  var name=new TextLine().Add(first?"첫 기록 등록":best?"최고 기록 경신":"최고 기록 유지",Heading(nameSize),look.Text,1.16f*nameSize);
  float gap=1.55f*U,y=(H-(tag.Height+gap+name.Height))/2;
  tag.Center(g,W/2,y,alpha);
  float nameTop=y+tag.Height+gap;
  name.Center(g,W/2,nameTop,alpha);
  if(!best||double.IsNaN(t))return;
  // 위 화살표: 왼쪽은 0.22초, 오른쪽은 0.37초에 시작하고 둘째 화살표가 0.075초 늦게 따른다. 0.46초 동안 아래에서 올라오며 나타났다 사라진다
  float wide=.62f*nameSize,tall=.35f*nameSize;
  foreach(bool left in new[]{true,false})
  {
   float x=left?W/2-name.Width/2-.22f*nameSize-wide:W/2+name.Width/2+.22f*nameSize;
   for(int second=0;second<2;second++)
   {
    double p=(t-(left?220:370)-75*second)/460;if(p<=0||p>=1)continue;
    double shown=p<.28?.95*Motion.EaseOut(p/.28):p<.65?.95-.10*Motion.EaseOut((p-.28)/.37):.85*(1-Motion.EaseOut((p-.65)/.35));
    float rise=(float)(.44-.87*Motion.EaseOut(p))*nameSize,top=nameTop+.12f*nameSize+(second==1?.32f*nameSize:0)+rise;
    var points=new[]{(.5f,0f),(1f,.74f),(1f,1f),(.5f,.39f),(0f,1f),(0f,.74f)}.Select(point=>new PointF(x+point.Item1*wide,top+point.Item2*tall)).ToArray();
    using var fill=new SolidBrush(Fade(second==1?Whiter(look.Above,.35):look.Above,(float)shown*alpha));g.FillPolygon(fill,points);
   }
  }
 }

 // 점수 상세: RATE 를 가장 크게, 그 아래 SCORE, 값마다 바로 아래에 차이. 유지일 때는 기존 최고도 보인다. 맨 아래는 곡명과 패턴 한 줄.
 // t 는 이 장면에 들어온 뒤의 시간(ms). RATE 는 곧바로 올라 0.34초에, SCORE 는 0.12초에 출발해 0.48초에 멈춘다. 다 멈추면 0.09초 뒤부터 0.18초에 두 차이가 함께 나타난다
 void Detail(Graphics g,OverlayLook look,float alpha,double t)
 {
  bool first=Mode==7,best=Mode==8,kept=Mode==9,counting=!double.IsNaN(t)&&t<480;
  int previous=first?0:best?993900:996800,rateNow=NewScore/100,rateBefore=previous/100;
  Color highlight=first?Color.FromArgb(0x86,0xDC,0xFF):best?look.Above:look.Text;
  var culture=System.Globalization.CultureInfo.InvariantCulture;
  int rateShown=counting?(int)Math.Floor(rateNow*Math.Min(1,t/340)):rateNow;
  double scoreProgress=counting?Math.Clamp((t-120)/360,0,1):1;
  int scoreShown=(int)Math.Floor(NewScore*(1-Math.Pow(1-scoreProgress,2)));
  float gapShown=double.IsNaN(t)?1:counting?0:Ease(t,570,180);
  float amount=7.3f*U,big=11.4f*U,spacing=-.03f*amount;
  var rate=new TextLine().Add((rateShown/100m).ToString("0.00",culture)+"%",Number(big),highlight,1.04f*big,spacing);
  var score=new TextLine().Add(scoreShown.ToString("N0",culture),Number(amount),highlight,1.04f*amount,spacing);
  float gapSize=3.95f*U,gapHeight=1.05f*gapSize,between=.2f*U,stack=1.35f*U;
  float rateHeight=1.04f*big+between+gapHeight,scoreHeight=1.04f*amount+between+gapHeight;
  var old=new TextLine();
  if(kept)old.Add("BEST SCORE",Number(3.55f*U,false),look.Text,1.15f*3.55f*U).Add(previous.ToString("N0",culture)+" | "+(rateBefore/100m).ToString("0.00",culture)+"%",Number(3.55f*U,false),look.Text,1.15f*3.55f*U,0,3.55f*U);
  string suffix=$" · {ButtonText} · {PatternText} · {JudgeText}판정";
  float captionSize=3.75f*U,captionLine=1.25f*captionSize;
  var tail=new TextLine().Add(suffix,Caption(captionSize),look.Text,captionLine);
  string shownSong=Fit(SongText,Caption(captionSize),0,.904f*W-tail.Width);
  var caption=new TextLine().Add(shownSong,Caption(captionSize),look.Text,captionLine);
  float y=(H-(rateHeight+stack+scoreHeight+(kept?stack+1.15f*3.55f*U:0)+stack+captionLine))/2;
  rate.Center(g,W/2,y+(1.04f*big-rate.Height)/2,alpha);
  Difference(g,look,rateNow-rateBefore,(Math.Abs(rateNow-rateBefore)/100m).ToString("0.00",culture)+"%",y+1.04f*big+between,gapSize,gapHeight,alpha*gapShown,gapShown);
  y+=rateHeight+stack;
  if(!counting||t>=120)
  {
   score.Center(g,W/2,y+(1.04f*amount-score.Height)/2,alpha);
   Difference(g,look,NewScore-previous,Math.Abs(NewScore-previous).ToString("N0",culture),y+1.04f*amount+between,gapSize,gapHeight,alpha*gapShown,gapShown);
  }
  y+=scoreHeight+stack;
  if(kept){old.Center(g,W/2,y+(1.15f*3.55f*U-old.Height)/2,alpha*gapShown);y+=1.15f*3.55f*U+stack;}
  // 곡 이름이 다 들어가면 줄 전체를 가운데에 놓는다. 끝이 줄어들면 줄이 너비의 90.4% 를 다 차지한다: 곡 이름은 왼쪽 끝에서, 패턴은 오른쪽 끝에 붙는다(페이지의 말줄임과 같다)
  float captionWidth=shownSong==SongText?caption.Width+tail.Width:.904f*W,captionLeft=(W-captionWidth)/2,captionBase=y+(captionLine-caption.Height)/2+(kept?0:1.4f*U)+caption.Above;
  caption.Draw(g,captionLeft,captionBase,alpha);tail.Draw(g,captionLeft+captionWidth-tail.Width,captionBase,alpha);
 }
 // 차이 한 칸: 늘었으면 붉은 ▲(아래에서 올라온다), 줄었으면 푸른 ▼(위에서 내려온다), 같으면 화살표 없이 기본 글자색
 static void Difference(Graphics g,OverlayLook look,int difference,string text,float top,float size,float height,float alpha,float arrived)
 {
  if(alpha<=0)return;
  Color ink=difference>0?Color.FromArgb(0xFF,0x74,0x7C):difference<0?Color.FromArgb(0x63,0xB7,0xFF):look.Text;
  float travel=(1-arrived)*.75f*U*Math.Sign(difference);
  var amount=new TextLine().Add(text,Number(size),ink,height);
  float markSize=.82f*size,between=.33f*U;
  var mark=new TextLine();if(difference!=0)mark.Add(difference>0?"▲":"▼",new[]{Face(Symbols.Value,markSize,false)},ink,markSize);
  float width=amount.Width+(difference!=0?mark.Width+between:0),x=W/2-width/2;
  if(difference!=0){mark.Draw(g,x,top+travel+(height-mark.Height)/2+mark.Above,alpha);x+=mark.Width+between;}
  amount.Draw(g,x,top+travel+(height-amount.Height)/2+amount.Above,alpha);
 }
}

// 오버레이의 겉모습으로 사용자가 정하는 것: 네 색(달성·미달성·기본 글자·배경)과 글자·배경의 투명도(0~100%. 100% 가 완전 투명). 글꼴과 글자 크기는 정해진 배치로 고정이다.
readonly record struct OverlayLook(Color Above,Color Below,Color Text,Color Background,int TextTransparency,int BackgroundTransparency);

// appearance.json 을 읽고 쓰는 규칙:
//   읽을 때  빠졌거나 읽을 수 없는 색은 그 색의 기본값을 쓴다. 투명도는 0~100 으로 맞추고, 빠졌거나 읽을 수 없으면 글자 0%, 배경 5% 다
//   쓸 때    예전 판의 글꼴·글자 크기 같은 다른 키와 값은 파일에 있던 그대로 둔다(새 화면은 쓰지 않는다. 예전 판으로 돌아가도 그 값이 남아 있다)
static class OverlayStyle
{
 public static readonly (string Key,string Name)[] Colors={("Above","달성 색상"),("Below","미달성 색상"),("Text","기본 글자 색상"),("Background","배경 색상")};
 public static readonly (string Key,string Name)[] Percents={("TextTransparency","글자 투명도"),("BackgroundTransparency","배경 투명도")};
 static bool Mine(string key)=>Colors.Any(c=>c.Key==key)||Percents.Any(p=>p.Key==key);
 static readonly System.Text.RegularExpressions.Regex Hex=new("^#[0-9a-fA-F]{6}$");
 /// <summary>색 하나("#RRGGBB"). 없거나 읽을 수 없으면 기본값</summary>
 public static string ColorOf(JsonElement a,string key)
  =>a.ValueKind==JsonValueKind.Object&&a.TryGetProperty(key,out var item)&&item.ValueKind==JsonValueKind.String&&item.GetString() is string text&&Hex.IsMatch(text)?text.ToUpperInvariant():BadgeDesign.Defaults.GetProperty(key).GetString()!;
 /// <summary>투명도 하나(0~100). 없거나 읽을 수 없으면 기본값</summary>
 public static int PercentOf(JsonElement a,string key)
  =>a.ValueKind==JsonValueKind.Object&&a.TryGetProperty(key,out var item)&&item.ValueKind==JsonValueKind.Number&&item.TryGetInt32(out int number)?Math.Clamp(number,0,100):BadgeDesign.Defaults.GetProperty(key).GetInt32();
 public static OverlayLook Look(JsonElement a)
  =>new(ColorTranslator.FromHtml(ColorOf(a,"Above")),ColorTranslator.FromHtml(ColorOf(a,"Below")),ColorTranslator.FromHtml(ColorOf(a,"Text")),ColorTranslator.FromHtml(ColorOf(a,"Background")),PercentOf(a,"TextTransparency"),PercentOf(a,"BackgroundTransparency"));

 /// <summary>
 /// 저장된 겉모습을 읽는다. 돌려주는 값에는 기본값의 키가 모두 들어 있다(오버레이 페이지와 글꼴·크기 키를 읽는 코드가 읽는다. 예전 판의 글자 크기는 12~64 로 맞춘다).
 /// 파일이 없거나 읽지 못하면 기본값이다.
 /// </summary>
 public static JsonElement Load(string path)
 {
  var defaults=BadgeDesign.Defaults;
  if(!File.Exists(path))return defaults;
  try{
   using var saved=JsonDocument.Parse(File.ReadAllText(path));
   if(saved.RootElement.ValueKind!=JsonValueKind.Object)return defaults;
   var values=defaults.EnumerateObject().ToDictionary(p=>p.Name,p=>(object)(p.Value.ValueKind==JsonValueKind.String?p.Value.GetString()!:p.Value.GetInt32()));
   foreach(string key in values.Keys.ToArray()){
    if(Colors.Any(c=>c.Key==key)){values[key]=ColorOf(saved.RootElement,key);continue;}
    if(Percents.Any(p=>p.Key==key)){values[key]=PercentOf(saved.RootElement,key);continue;}
    if(!saved.RootElement.TryGetProperty(key,out var item))continue;
    if(values[key] is int&&item.ValueKind==JsonValueKind.Number&&item.TryGetInt32(out int number))values[key]=Math.Clamp(number,12,64);
    else if(values[key] is string&&item.ValueKind==JsonValueKind.String&&item.GetString() is string value&&!string.IsNullOrWhiteSpace(value))values[key]=value;
   }
   return JsonSerializer.SerializeToElement(values);
  }catch(JsonException){return defaults;}catch(IOException){return defaults;}
 }
 /// <summary>네 색과 두 투명도를 파일에 쓴다. 파일에 있던 다른 키와 값은 그대로 둔다</summary>
 public static void Save(string path,JsonElement value)
 {
  var data=new Dictionary<string,JsonElement>();
  try{
   if(File.Exists(path)){using var old=JsonDocument.Parse(File.ReadAllText(path));if(old.RootElement.ValueKind==JsonValueKind.Object)foreach(var item in old.RootElement.EnumerateObject())data[item.Name]=item.Value.Clone();}
  }catch(JsonException){}catch(IOException){}
  foreach(var (key,_) in Colors)data[key]=JsonSerializer.SerializeToElement(ColorOf(value,key));
  foreach(var (key,_) in Percents)data[key]=JsonSerializer.SerializeToElement(PercentOf(value,key));
  File.WriteAllText(path,JsonSerializer.Serialize(data));
 }
 /// <summary>창이 돌려줄 값: 받은 값의 다른 키는 그대로 두고 네 색과 두 투명도만 바꾼다</summary>
 public static JsonElement With(JsonElement original,IReadOnlyDictionary<string,string> colors,IReadOnlyDictionary<string,int> percents)
 {
  var data=new Dictionary<string,JsonElement>();
  if(original.ValueKind==JsonValueKind.Object)foreach(var item in original.EnumerateObject())if(!Mine(item.Name))data[item.Name]=item.Value.Clone();
  foreach(var (key,_) in Colors)data[key]=JsonSerializer.SerializeToElement(colors[key]);
  foreach(var (key,_) in Percents)data[key]=JsonSerializer.SerializeToElement(percents[key]);
  return JsonSerializer.SerializeToElement(data);
 }
}
