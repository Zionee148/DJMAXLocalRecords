using System.Drawing.Drawing2D;
using System.Drawing.Text;
namespace LadderRecords;

// 주 화면 오른쪽의 곡 정보 카드: 옅은 바탕, 왼쪽 5px 띠, 오른쪽 끝의 옅은 빗금, 한 줄 곡 이름, 그 아래 꼬리표 셋.
// 곡 이름은 한 줄이다. 기본 35px 로 재서 넘치면 한 줄에 다 들어가는 가장 큰 크기까지 줄이고(하한 25px = 70% 올림), 그래도 넘치면 끝을 "…" 으로 줄인다.
// 줄인 이름은 마우스를 올리면 전체가 보인다. Text 에는 언제나 원래 이름이 그대로 있다.
// 치수는 100% 때의 값이고 그릴 때 창의 크기 단계(Theme.S)만큼 키운다.
sealed class SongCard:Label
{
 public const int BasePx=35,Band=5,Side=21;
 /// <summary>곡 이름의 글자 사이(글자 크기에 대한 비율). 디자인 기준의 값(−0.025) 그대로다</summary>
 public static float TitleSpacing=-0.025f;
 readonly ToolTip tip;MixedLine? line;string? fitted;int fittedWidth;
 // 새 곡 정보가 왔을 때의 연출: 곡 이름이 한 글자씩 쳐지고(한글은 자음·모음이 조합된다) 카드 아래로 테마색 빛이 한 번 지난다(0.92초).
 // 글자 크기는 완성된 이름으로 먼저 정해 두고 그 크기로 친다. 연출이 끝나면 맞춘 모습(넘치면 말줄임)이 된다
 string? typed;double sweep=-1;Motion.Run? typing,sweeping;
 public void Reveal()
 {
  StopReveal();
  var frames=TypingFrames.Of(Text);if(frames.Count==0)return;var(gap,total)=TypingFrames.Timing(frames.Count);
  typing=Motion.Start(total,t=>{typed=t>=1?null:frames[Math.Min((int)(t*total/gap),frames.Count-1)];Invalidate();});
  sweeping=Motion.Start(.92,t=>{sweep=t>=1?-1:Motion.Bezier(t,.28,.04,.34,1);Invalidate();});
 }
 public void StopReveal(){Motion.Stop(typing);Motion.Stop(sweeping);typing=sweeping=null;}
 public bool Cut=>Fitted().Cut;
 public int TitlePx=>Fitted().Px;
 public SongCard(ToolTip tip)
 {
  this.tip=tip;
  SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
  AutoSize=false;UseMnemonic=false;Margin=Padding.Empty;Tag="card";
 }
 static Font[] Chain(int px)=>new[]{MixedLine.Face(AppFonts.Latin,px),MixedLine.Face(AppFonts.Head,px),MixedLine.Face(AppFonts.Wide,px)};
 MixedLine Fitted()
 {
  if(line!=null&&fitted==Text&&fittedWidth==Width)return line;
  fitted=Text;fittedWidth=Width;
  return line=MixedLine.Fit(Text,Chain,Theme.S(BasePx),Width-Theme.S(Band)-Theme.S(Side)*2,TitleSpacing);
 }
 protected override void OnTextChanged(EventArgs e)
 {
  base.OnTextChanged(e);
  tip.SetToolTip(this,Cut?Text:null);Invalidate();
 }
 protected override void OnPaint(PaintEventArgs e)
 {
  var g=e.Graphics;g.Clear(Theme.Card);BackColor=Theme.Card;
  // 오른쪽 35% 의 빗금: 14px 마다 한 줄, 오른쪽으로 갈수록 또렷해진다
  int from=(int)(Width*.65f);
  var state=g.Save();g.SetClip(new Rectangle(from,0,Width-from,Height));g.SmoothingMode=SmoothingMode.AntiAlias;
  using(var pen=new Pen(Color.FromArgb(Theme.Dark?25:27,Theme.Dark?Color.FromArgb(0xFF,0xDB,0x58):Color.FromArgb(0xB5,0x25,0x49))))
  {
   float step=Theme.S(14f)/(float)Math.Cos(Math.PI/6),lean=Height*(float)Math.Tan(Math.PI/6);   // 줄은 세로에서 30° 기울었다
   for(float x=from-lean;x<Width+lean;x+=step)g.DrawLine(pen,x+lean,0,x,Height);
  }
  using(var fade=new LinearGradientBrush(new Rectangle(from-1,0,Width-from+2,Height),Theme.Card,Color.FromArgb(0,Theme.Card),LinearGradientMode.Horizontal))g.FillRectangle(fade,from,0,Width-from,Height);
  g.Restore(state);
  using(var band=new SolidBrush(Theme.Stripe))g.FillRectangle(band,0,0,Theme.S(Band),Height);
  // 글자의 밑줄 자리: 기본 크기일 때 카드 위에서 49. 줄어든 크기에서는 대문자 높이의 가운데가 같은 자리에 오게 한다
  var title=Fitted();
  g.TextRenderingHint=TextRenderingHint.AntiAlias;using var ink=new SolidBrush(Theme.Text);
  float left=Theme.S(Band)+Theme.S(Side),baseline=Theme.S(49f)-(Theme.S(BasePx)-title.Px)*.35f;
  if(typed==null)title.Draw(g,ink,left,baseline);
  else
  {
   // 쳐지는 동안: 완성된 이름에 맞춘 크기 그대로, 칸을 넘는 부분은 가린다
   var clip=g.Save();g.SetClip(new RectangleF(left,0,Width-left-Theme.S(Side),Height));
   MixedLine.Plain(typed,Chain(title.Px),TitleSpacing*title.Px).Draw(g,ink,left,baseline);g.Restore(clip);
  }
  if(sweep>=0)
  {
   // 카드 아래(바닥에서 2 위, 높이 3)를 지나는 빛: 카드 너비의 42% 인 막대가 왼쪽 밖에서 오른쪽 밖으로
   var stripe=Theme.Stripe;
   Motion.DrawGlint(g,new RectangleF(0,Height-Theme.S(2)-Theme.S(3),Width,Theme.S(3)),Width*.42f,(float)(-1+3.5*sweep),
    new[]{Color.FromArgb(0,stripe),stripe,Theme.Mix(stripe,Color.White,.45),Color.FromArgb(0,stripe)},new[]{0f,.67f,.86f,1f});
  }
 }
}

// 목표·현재의 큰 숫자. "99.90%" 는 숫자를 Oxanium 굵게 37px 로, % 를 그 절반쯤의 크기로 밑줄을 맞춰 그린다. 숫자가 아닌 글("미등록", "—")은 일반 글자의 굵은 글꼴로 그린다.
// Text 와 Tag("good"·"bad")는 다른 글(Label)과 같은 뜻으로 쓴다.
sealed class BigValue:Label
{
 public BigValue()
 {
  SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
  AutoSize=false;UseMnemonic=false;Margin=Padding.Empty;Size=new(218,40);Text="—";
 }
 protected override void OnTextChanged(EventArgs e){base.OnTextChanged(e);Invalidate();}
 protected override void OnPaint(PaintEventArgs e)
 {
  var g=e.Graphics;g.Clear(Parent?.BackColor??Theme.Side);g.TextRenderingHint=TextRenderingHint.AntiAlias;
  using var ink=new SolidBrush(Theme.For(this));
  float baseline=Theme.S(33f);
  if(Text.EndsWith('%')&&Text.Length>1)
  {
   float x=MixedLine.Plain(Text[..^1],new[]{MixedLine.Face(AppFonts.Latin,Theme.S(37))},Theme.S(-1.25f)).Draw(g,ink,0,baseline);
   // % 는 보통 굵기(400)로 그린다. AppFonts.Latin 으로 보통을 만들면 굵은 파일로 그려져서(AppFonts 의 설명) 보통만 실은 집합을 쓴다
   MixedLine.Plain("%",new[]{MixedLine.Face(AppFonts.LatinRegular,Theme.S(18),FontStyle.Regular)}).Draw(g,ink,x+Theme.S(3f),baseline);
  }
  else MixedLine.Plain(Text,new[]{MixedLine.Face(AppFonts.Ui,Theme.S(24)),MixedLine.Face(AppFonts.Latin,Theme.S(24))}).Draw(g,ink,0,baseline-Theme.S(2f));
 }
}

// SCORE 줄의 상자: 왼쪽 칸과 같은 바탕, 테두리 한 줄
sealed class StripPanel:Panel
{
 public StripPanel(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);Margin=Padding.Empty;}
 // 그 판의 첫 SCORE 가 잡혔을 때 안쪽 테두리를 한 바퀴 도는 빛의 진행도(없으면 음수)
 double trace=-1;
 public void SetTrace(double t){trace=t;Invalidate();}
 protected override void OnPaint(PaintEventArgs e)
 {
  BackColor=Theme.Window;e.Graphics.Clear(Theme.Window);
  using var pen=new Pen(Theme.Line);e.Graphics.DrawRectangle(pen,0,0,Width-1,Height-1);
  if(trace>=0)Motion.DrawTrace(e.Graphics,ClientRectangle,trace,Theme.Stripe);
 }
}

// 상태 줄 앞의 작은 점. 색으로 지금의 상태를 알린다: "good" 감지 중 · "stripe" 확인 중 · "bad" 실패 · 그 밖은 옅은 색
sealed class StatusDot:Control
{
 string kind="dim";
 [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
 public string Kind{get=>kind;set{if(kind==value)return;kind=value;Invalidate();}}
 public StatusDot(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);Size=new(8,8);TabStop=false;}
 protected override void OnPaint(PaintEventArgs e)
 {
  var g=e.Graphics;g.Clear(Parent?.BackColor??Theme.Side);g.SmoothingMode=SmoothingMode.AntiAlias;
  using var fill=new SolidBrush(kind switch{"good"=>Theme.Good,"stripe"=>Theme.Stripe,"bad"=>Theme.Lower,_=>Theme.Dim});g.FillEllipse(fill,0,0,Width-1,Height-1);
 }
}
