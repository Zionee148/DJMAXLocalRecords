using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
namespace LadderRecords;

// 주 화면의 짧은 연출을 돌리는 바탕.
// 연출은 보이는 것만 바꾼다: 값과 상태는 연출이 시작될 때 이미 끝의 것이다(연출을 건너뛰어도 같은 화면이 된다).
// Windows 의 '애니메이션 효과'를 꺼 둔 사용자에게는 연출 없이 끝의 모습을 바로 보인다('동작 줄이기').
static class Motion
{
 [DllImport("user32.dll")]static extern bool SystemParametersInfo(uint action,uint parameter,out bool value,uint flags);
 /// <summary>Windows 설정 → 접근성 → 시각 효과 → 애니메이션 효과가 꺼져 있는가(SPI_GETCLIENTAREAANIMATION)</summary>
 public static bool Reduced=>SystemParametersInfo(0x1042,0,out bool enabled,0)&&!enabled;
#if DEVELOPMENT_CHECKS
 /// <summary>개발용 검사가 연출의 한 순간을 붙잡아 화면을 찍을 때 쓴다: 값이 있으면 모든 연출이 그 진행도(0~1)에 멈춰 있다</summary>
 public static double? Frozen;
#endif

 public sealed class Run
 {
  internal long Started;internal double Seconds;internal Action<double> Step=_=>{};internal Action? Done;
  public bool Active{get;internal set;}
 }
 static readonly List<Run> runs=new();
 static System.Windows.Forms.Timer? timer;
 /// <summary>연출 하나를 시작한다. step 은 진행도(0~1)를 받고 마지막에 1 로 한 번 불린다. 동작 줄이기가 켜져 있으면 바로 1 로 부르고 끝낸다</summary>
 public static Run Start(double seconds,Action<double> step,Action? done=null)
 {
  var run=new Run{Started=System.Diagnostics.Stopwatch.GetTimestamp(),Seconds=seconds,Step=step,Done=done};
  if(Reduced){step(1);done?.Invoke();return run;}
  run.Active=true;runs.Add(run);step(Now(run));
  timer??=Make();if(!timer.Enabled)timer.Start();
  return run;
 }
 /// <summary>연출을 끝낸다(끝의 모습으로). 이미 끝났으면 아무 일도 없다</summary>
 public static void Stop(Run? run){if(run is not {Active:true})return;run.Active=false;runs.Remove(run);run.Step(1);run.Done?.Invoke();}
 static double Now(Run run)
 {
#if DEVELOPMENT_CHECKS
  if(Frozen is double held)return held;
#endif
  return Math.Clamp(System.Diagnostics.Stopwatch.GetElapsedTime(run.Started).TotalSeconds/run.Seconds,0,1);
 }
 static System.Windows.Forms.Timer Make()
 {
  var made=new System.Windows.Forms.Timer{Interval=15};
  made.Tick+=(_,_)=>
  {
   foreach(var run in runs.ToArray())
   {
    if(!run.Active)continue;
    double t=Now(run);
    if(t>=1){run.Active=false;runs.Remove(run);run.Step(1);run.Done?.Invoke();}else run.Step(t);
   }
   if(runs.Count==0)made.Stop();
  };
  return made;
 }

 // CSS 의 cubic-bezier(x1,y1,x2,y2): x 에 해당하는 곡선의 y. 이분법으로 푼다(연출 몇 개가 화면마다 한 번씩 부른다)
 public static double Bezier(double x,double x1,double y1,double x2,double y2)
 {
  static double At(double t,double a,double b)=>3*(1-t)*(1-t)*t*a+3*(1-t)*t*t*b+t*t*t;
  double low=0,high=1,t=x;
  for(int i=0;i<24;i++){double seen=At(t,x1,x2);if(Math.Abs(seen-x)<1e-5)break;if(seen<x)low=t;else high=t;t=(low+high)/2;}
  return At(t,y1,y2);
 }
 public static double EaseOut(double t)=>Bezier(t,0,0,.58,1);
 public static double EaseIn(double t)=>Bezier(t,.42,0,1,1);
 public static double EaseInOut(double t)=>Bezier(t,.42,0,.58,1);

 /// <summary>
 /// 안쪽 테두리를 시계 방향으로 한 바퀴 도는 빛(감지 시작의 게임 화면, 그 판의 첫 SCORE 의 SCORE 줄). area 의 2px 안쪽을 따라 둘레의 17% 길이인 선이 돈다.
 /// 처음 13% 동안 나타나고 마지막 24% 동안 사라진다.
 /// </summary>
 public static void DrawTrace(Graphics g,Rectangle area,double t,Color color)
 {
  if(t<=0||t>=1)return;
  double offset,opacity;
  if(t<.13){offset=17*(1-t/.13);opacity=.98*t/.13;}
  else if(t<.76){offset=-68*(t-.13)/.63;opacity=.98;}
  else{offset=-68-15*(t-.76)/.24;opacity=.98*(1-(t-.76)/.24);}
  float inset=Theme.S(2f)+Theme.S(1.2f),width=area.Width-inset*2,height=area.Height-inset*2;if(width<=0||height<=0)return;
  float left=area.Left+inset,top=area.Top+inset,around=2*(width+height);
  // 둘레 위의 거리 d(왼쪽 위에서 오른쪽으로 시작해 시계 방향)의 점
  PointF At(float d)
  {
   d=(d%around+around)%around;
   if(d<width)return new(left+d,top);d-=width;
   if(d<height)return new(left+width,top+d);d-=height;
   if(d<width)return new(left+width-d,top+height);d-=width;
   return new(left,top+height-d);
  }
  float from=(float)(-offset/100*around),length=.17f*around;
  var points=new List<PointF>{At(from)};
  // 모서리를 지날 때마다 점을 하나 넣는다
  float[] corners={0,width,width+height,2*width+height};
  for(int lap=-1;lap<=1;lap++)foreach(float corner in corners){float at=corner+lap*around;if(at>from&&at<from+length)points.Add(At(at));}
  // 넣은 차례가 곧 지나는 차례다(lap 과 모서리가 커지는 순)
  points.Add(At(from+length));
  var state=g.Save();g.SmoothingMode=SmoothingMode.AntiAlias;
  using var pen=new Pen(Color.FromArgb((int)(255*Math.Clamp(opacity,0,1)),color),Theme.S(2.4f)){StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round};
  g.DrawLines(pen,points.ToArray());
  g.Restore(state);
 }

 /// <summary>왼쪽에서 오른쪽으로 한 번 지나가는 빛. 막대의 너비와 지금의 왼쪽 자리(막대 너비의 몇 배만큼 밀렸는가), 색의 차례를 받아 그린다</summary>
 public static void DrawGlint(Graphics g,RectangleF track,float barWidth,float shift,Color[] colors,float[] stops,double opacity=1)
 {
  if(opacity<=0||barWidth<=0)return;
  var bar=new RectangleF(track.Left+shift*barWidth,track.Top,barWidth,track.Height);
  if(bar.Right<=track.Left||bar.Left>=track.Right)return;
  var state=g.Save();g.SetClip(track);g.SmoothingMode=SmoothingMode.AntiAlias;
  using var brush=new LinearGradientBrush(new RectangleF(bar.Left-1,bar.Top,bar.Width+2,bar.Height),Color.Black,Color.Black,LinearGradientMode.Horizontal)
   {InterpolationColors=new ColorBlend{Colors=colors.Select(c=>Color.FromArgb((int)(c.A*Math.Clamp(opacity,0,1)),c)).ToArray(),Positions=stops}};
  g.FillRectangle(brush,bar);
  g.Restore(state);
 }
}

// 한글 곡 이름이 한 글자씩 쳐지는 모습의 장면들. 한글 음절은 자음 → 모음 → 받침의 차례로 조합되고(겹모음과 겹받침은 첫 글쇠의 모양을 먼저 거친다), 그 밖의 글자는 한 글자씩 나온다.
static class TypingFrames
{
 const string Initials="ㄱㄲㄴㄷㄸㄹㅁㅂㅃㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎ";
 static readonly Dictionary<int,int> FirstVowel=new(){{9,8},{10,8},{11,8},{14,13},{15,13},{16,13},{19,18}};
 static readonly Dictionary<int,int> FirstFinal=new(){{3,1},{5,4},{6,4},{9,8},{10,8},{11,8},{12,8},{13,8},{14,8},{15,8},{18,17}};
 public static List<string> Of(string name)
 {
  var frames=new List<string>();string prefix="";
  var e=System.Globalization.StringInfo.GetTextElementEnumerator(name.Normalize());
  while(e.MoveNext())
  {
   string segment=(string)e.Current;int offset=segment.Length==1?segment[0]-0xAC00:-1;
   if(offset>=0&&offset<11172)
   {
    int initial=offset/588,vowel=offset/28%21,final=offset%28;
    string Syllable(int v,int f=0)=>prefix+(char)(0xAC00+initial*588+v*28+f);
    frames.Add(prefix+Initials[initial]);
    if(FirstVowel.TryGetValue(vowel,out int early))frames.Add(Syllable(early));
    frames.Add(Syllable(vowel));
    if(FirstFinal.TryGetValue(final,out int half))frames.Add(Syllable(vowel,half));
    if(final!=0)frames.Add(Syllable(vowel,final));
   }
   else frames.Add(prefix+segment);
   prefix+=segment;
  }
  return frames;
 }
 /// <summary>장면 사이의 시간(초)과 끝나는 때(초). 장면 사이는 0.036초를 넘지 않고, 긴 이름도 1.2초 안에 다 쳐진다. 연출은 0.95초보다 일찍 끝나지 않는다</summary>
 public static (double Gap,double Total) Timing(int frames){double gap=Math.Min(.036,1.2/Math.Max(1,frames-1));return(gap,Math.Max(.95,frames*gap));}
}

// 같은 자리의 두 모습을 잇는 짧은 전환: 앞의 모습이 흐려지고(나가는 시간) 이어서 뒤의 모습이 나타난다(들어오는 시간. 처음 36% 동안은 비어 있다).
// 목표·현재 ↔ 저장 확인이 바뀔 때 그 자리 위에 잠깐 덮여 두 그림을 섞어 보인다. 끝나면 사라지고 아래의 실제 컨트롤이 보인다.
sealed class FadeLayer:Control
{
 Bitmap? before,after;double outShare=.3,progress;Motion.Run? run;
 public FadeLayer()
 {
  SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
  Visible=false;TabStop=false;Margin=Padding.Empty;
 }
 public bool Playing=>run is {Active:true};
 // 덮여 있는 0.26초쯤 동안에도 마우스는 아래의 컨트롤(저장·취소 단추)이 받는다
 protected override void WndProc(ref Message m){if(m.Msg==0x0084){m.Result=(IntPtr)(-1);return;}base.WndProc(ref m);}   // WM_NCHITTEST → HTTRANSPARENT
 public void Play(Bitmap from,Bitmap to,double outSeconds,double inSeconds)
 {
  Cancel();before=from;after=to;outShare=outSeconds/(outSeconds+inSeconds);progress=0;
  Visible=true;BringToFront();
  run=Motion.Start(outSeconds+inSeconds,t=>{progress=t;Invalidate();},Cancel);
 }
 public void Cancel()
 {
  var running=run;run=null;if(running is {Active:true})Motion.Stop(running);
  if(Visible)Visible=false;before?.Dispose();after?.Dispose();before=after=null;
 }
 static void Blend(Graphics g,Bitmap image,double opacity)
 {
  if(opacity<=0)return;
  using var attributes=new ImageAttributes();attributes.SetColorMatrix(new ColorMatrix{Matrix33=(float)Math.Clamp(opacity,0,1)});
  g.DrawImage(image,new Rectangle(0,0,image.Width,image.Height),0,0,image.Width,image.Height,GraphicsUnit.Pixel,attributes);
 }
 protected override void OnPaint(PaintEventArgs e)
 {
  var g=e.Graphics;g.Clear(Parent?.BackColor??Theme.Side);
  if(before==null||after==null)return;
  if(progress<outShare)Blend(g,before,1-Motion.EaseIn(progress/outShare));
  else{double t=(progress-outShare)/(1-outShare);Blend(g,after,t<.36?0:Motion.EaseOut((t-.36)/.64));}
 }
 protected override void Dispose(bool disposing){if(disposing){before?.Dispose();after?.Dispose();}base.Dispose(disposing);}
}
