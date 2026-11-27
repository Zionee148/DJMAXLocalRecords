using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
namespace LadderRecords;

// 프로그램 창의 색과 모양(라이트·다크 두 모드).
// 색은 여기 한 곳에서 정하고, 창이 뜰 때 AppWindow 가 Theme.Apply 를 불러 컨트롤마다 적용한다.
// 새 단추나 칸을 더해도 따로 색을 줄 필요가 없다. 달리 보여야 하는 것만 Tag 로 표시한다:
//   단추 "primary"(채운 강조) · "default"(강조 테두리) · "danger"(빨간 테두리) · "swatch"(제 색을 그대로 둠)
//   글 "dim"(옅게) · "accent" · "good" · "bad" · "warn",  패널 "line" · "border" · "field"
// OBS 오버레이의 디자인은 여기와 상관없다(BadgeDesign, OverlayLayout).
static class Theme
{
 public static bool Dark;
 // 창의 글자 크기: 디자인 기준의 글자 크기를 모두 14/13 배로 그린다. 기준의 일반 글자는 13px 인데, 글꼴이 맑은 고딕(v1.1)에서 SUIT 로 바뀌어
 // 같은 크기면 한글이 v1.1 보다 작게 보인다(높이 91%). 14px 이면 v1.1 과 거의 같다(98%). 영문·숫자도 함께 키운다.
 // 코드의 글자 크기는 디자인 기준의 값(13px = 9.75pt 등)으로 적고, 글꼴을 만드는 자리(Px·PxS)가 이 배수를 곱한다.
 // 줄 높이·단추 높이·여백은 디자인 기준의 값 그대로다. 글자 크기로 제 치수의 단위를 셈하는 부품은 이 배수를 빼고 센다(아래의 TextPx)
 public const float TextScale=14f/13f;
 /// <summary>디자인 기준에서 px 로 적은 글자의 지금 크기(100% 때). 부품이 "지금 창이 몇 배로 그려지는가"를 글꼴 크기에서 알아낼 때 쓴다: Font.Size / TextPx(기준의 px)</summary>
 public static float TextPx(float px)=>px*TextScale;
 // 창의 크기 단계. 1 이면 100% 때의 모양 그대로다. 프로그램이 뜰 때 한 번 정한다(DecideScale).
 // 사용자가 '앱 표시 크기'로 고른 숫자(100·125·150·175·200)에서 시작해, 주 화면이 주 모니터의 작업 영역에 들어가지 않으면 들어가는 가장 큰 단계로 내린다(고른 숫자는 그대로 남는다).
 // 고른 적이 없으면 첫 값을 정한다: 예전의 '작게 보기'를 켜 두었으면 100, 아니면 Windows 배율 이하의 가장 가까운 단계(위 끝 200).
 // 치수와 글꼴은 모두 100% 때의 값으로 적고 만든다. 창이 뜰 때 AppWindow 가 창 안의 자리·크기·글꼴을 이 값만큼 키운다. 직접 그리는 부품은 그릴 때 S() 로 키운다.
 // Windows 가 그리는 것(제목 표시줄의 높이와 창 버튼, 스크롤 막대, 풍선 도움말)은 이 단계가 아니라 Windows 배율을 따른다. 제목 표시줄 안의 제목과 아이콘도 그 높이에 맞게 Windows 배율로 그린다
 public static float Scale{get;private set;}=1f;
 /// <summary>Windows 의 배율(1 = 100%)</summary>
 public static float SystemScale{get;private set;}=1f;
 /// <summary>고를 수 있는 단계(퍼센트)</summary>
 public static readonly int[] Steps={100,125,150,175,200};
 /// <summary>사용자가 고른(또는 처음 정해진) 단계. 화면이 작아 더 낮은 단계로 그리고 있어도 이 값은 고른 그대로다</summary>
 public static int Chosen{get;private set;}=100;
 /// <summary>지금 실제로 그리는 단계(퍼센트)</summary>
 public static int Shown=>(int)Math.Round(Scale*100);
 public static int S(int px)=>Scale==1f?px:(int)Math.Round(px*Scale);
 public static float S(float px)=>px*Scale;
 [DllImport("user32.dll")]static extern uint GetDpiForSystem();
 public const int MainWidth=1024,MainHeight=762;   // 주 화면의 내용 크기(제목 띠 제외. 100% 때의 값)
 /// <summary>고른 적이 없을 때의 첫 값: '작게 보기'를 켜 두었으면 100, 아니면 Windows 배율 이하에서 가장 가까운 단계(140% → 125, 190% → 175, 250% → 200)</summary>
 public static int FirstChoice(bool small,float system)=>small?100:Steps.Last(step=>step==100||step<=(int)Math.Round(system*100));
 /// <summary>설정에 적힌 숫자가 고를 수 있는 단계면 그것, 아니면 첫 값</summary>
 public static int Choice(int? saved,bool small,float system)=>saved is int value&&Array.IndexOf(Steps,value)>=0?value:FirstChoice(small,system);
 /// <summary>
 /// 고른 단계에서 시작해 주 화면이 작업 영역(area)에 들어가는 가장 큰 단계. 하나도 들어가지 않으면 100 이다.
 /// sides·above 는 창의 테두리와 제목 띠가 차지하는 크기다(Windows 가 제 배율로 그린다. 앱의 단계와 상관없다)
 /// </summary>
 public static int Fit(int chosen,Size area,int sides,int above)
 {
  for(int at=Array.IndexOf(Steps,chosen);at>0;at--)
   if(MainWidth*Steps[at]/100f+sides<=area.Width&&MainHeight*Steps[at]/100f+above<=area.Height)return Steps[at];
  return 100;
 }
 /// <summary>뜰 때 한 번: 설정의 숫자(없으면 '작게 보기'와 Windows 배율), 주 모니터의 작업 영역으로 단계를 정한다</summary>
 public static void DecideScale(int? saved,bool small)
 {
  SystemScale=Math.Max(1f,GetDpiForSystem()/96f);Chosen=Choice(saved,small,SystemScale);Scale=1f;
#if DEVELOPMENT_CHECKS
  // 개발용: 설정을 바꾸지 않고 큰 단계의 모양을 본다(예: DJMAX_UI_SCALE=1.5). 화면에 들어가는지는 보지 않는다. 제목 띠와 창 버튼은 Windows 의 배율 그대로다
  if(float.TryParse(Environment.GetEnvironmentVariable("DJMAX_UI_SCALE"),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out float forced)&&forced>=1f){Scale=forced;Chosen=Steps.OrderBy(step=>Math.Abs(step-forced*100)).First();return;}
#endif
  var area=Screen.PrimaryScreen?.WorkingArea.Size??new Size(1920,1040);
  int sides=(int)Math.Ceiling(16*SystemScale),above=AppWindow.BarHeight+(int)Math.Ceiling(8*SystemScale);   // 창의 테두리와 제목 띠(Windows 가 배율만큼 키운다)
  Scale=Fit(Chosen,area,sides,above)/100f;
 }
 public static void DecideScale(UserOptions options)=>DecideScale(options.Scale,options.Small);
 // 창 안의 글꼴을 단계만큼 키운다. 글꼴을 따로 정한 컨트롤만 새로 만들고, 물려받는 컨트롤은 부모가 바뀌면 따라온다(물려받은 글꼴은 부모의 것과 같은 개체다)
 public static void ScaleFonts(Control control,bool own)
 {
  var children=control.Controls.Cast<Control>().Select(child=>(Control:child,Own:!ReferenceEquals(child.Font,control.Font))).ToArray();
  if(own)control.Font=new Font(control.Font.FontFamily,control.Font.Size*Scale,control.Font.Style,control.Font.Unit);
  foreach(var child in children)ScaleFonts(child.Control,child.Own);
 }
 // 글꼴은 Windows 배율을 따라가지 않게 만든다.
 // 포인트로 만든 글꼴은 배율만큼 커지는데(125% 에서 1.25배) 창의 치수는 픽셀로 정해 둔 곳이 많아, 배율이 100% 가 아니면 글이 잘리고 겹쳤다.
 // 그래서 "포인트 크기를 100%(96 DPI) 때의 픽셀로 바꾼 값"으로 픽셀 단위 글꼴을 만든다. 어느 배율에서나 100% 때와 같은 배치가 된다.
 // 대가: 배율을 올려 쓰는 화면에서는 이 프로그램의 글자가 다른 프로그램보다 작게 보인다. 제목 줄과 스크롤 막대처럼 Windows 가 그리는 부분은 배율을 따라간다.
 // 창의 글자는 앱이 실은 글꼴로 그린다(AppFonts). 글꼴 이름을 "맑은 고딕"으로 준 자리는 일반 글자의 글꼴로 바꿔 만든다
 public static Font Px(string family,float points,FontStyle style=FontStyle.Regular)=>family=="맑은 고딕"?Px(AppFonts.Ui,points,style):new Font(family,points*96f/72f*TextScale,style,GraphicsUnit.Pixel);
 public static Font Px(FontFamily family,float points,FontStyle style=FontStyle.Regular)=>new Font(Weighted(family,style),points*96f/72f*TextScale,style,GraphicsUnit.Pixel);
 // 일반 글자의 굵기: 보통 글자는 500(SUIT Medium), 굵게는 700(SUIT Bold). 항목 이름(600)은 AppFonts.UiSemi 를 직접 준다
 static FontFamily Weighted(FontFamily family,FontStyle style)=>ReferenceEquals(family,AppFonts.Ui)&&(style&FontStyle.Bold)==0?AppFonts.UiMedium:family;
 /// <summary>같은 크기의 굵은 글꼴. 500·600 의 글꼴 집합에는 굵은 꼴이 없어(Windows 가 흉내 내어 그린다) 일반 글자의 700 으로 바꿔 만든다</summary>
 public static Font Bold(Font font)
 {
  var family=font.FontFamily;if(family.Name==AppFonts.UiMedium.Name||family.Name==AppFonts.UiSemi.Name)family=AppFonts.Ui;
  return new Font(family,font.Size,font.Style|FontStyle.Bold,font.Unit);
 }
 // 창이 뜰 때의 키우기를 거치지 않는 글꼴(직접 그리는 부품이 저 혼자 쓰는 것)은 처음부터 단계만큼 키워 만든다
 public static Font PxS(FontFamily family,float points,FontStyle style=FontStyle.Regular)=>new Font(Weighted(family,style),points*96f/72f*TextScale*Scale,style,GraphicsUnit.Pixel);
 // 글꼴을 따로 정하지 않은 컨트롤이 쓰는 앱의 기본 글꼴도 같은 식으로 설정한다. 첫 창을 만들기 전에 한 번 부른다(Program.Main)
 public static void FixDefaultFont(){var basic=SystemFonts.MessageBoxFont??Control.DefaultFont;Application.SetDefaultFont(Px(AppFonts.Ui,9.75f,basic.Style));}   // 일반 글자 13px
 static Color C(int light,int dark)=>Color.FromArgb(unchecked((int)0xFF000000)|(Dark?dark:light));
 // 창의 색(디자인 기준의 값).
 // 앞이 페일 라이트, 뒤가 클리어 다크.
 public static Color Window=>C(0xFFFFFF,0x251D33);      // 주 화면의 왼쪽, 세부 창, 단추의 바탕
 public static Color Side=>C(0xF8F7F8,0x1C1627);        // 주 화면의 오른쪽
 public static Color Line=>C(0xD6CFD4,0x483B58);        // 구분선, 단추와 칸의 테두리
 public static Color Text=>C(0x271E26,0xF7F5FA);
 public static Color Dim=>C(0x655866,0xC1B4CC);
 public static Color Field=>C(0xF8F6F7,0x30263D);       // 글 칸의 바탕
 public static Color Card=>C(0xFFF7F8,0x30223E);        // 곡명 영역의 바탕
 public static Color Border=>Line;
 public static Color ButtonBack=>Window;
 public static Color Fill=>C(0xB52549,0xFFD447);        // 채운 단추의 바탕
 public static Color OnAccent=>C(0xFFFFFF,0x261D12);    // 채운 단추의 글자
 public static Color Accent=>C(0xAA2145,0xFFDB58);      // 강조 글자와 강조 테두리
 public static Color Stripe=>C(0xC43155,0xFFD447);      // 제목의 사선, 곡명 왼쪽의 띠
 public static Color Soft=>C(0xFFF1F4,0x3C302C);        // 강조 테두리 단추의 옅은 바탕
 public static Color HoverBack=>C(0xFFE3EB,0x43352D);   // 마우스를 올린 단추의 바탕
 public static Color HoverEdge=>C(0xB52549,0xFFDC64);   // 마우스를 올린 단추의 테두리와 아래쪽 선
 public static Color FillHover=>C(0x981C3D,0xFFE994);   // 마우스를 올린 채운 단추의 바탕
 public static Color FillHoverEdge=>C(0x5B1427,0x90671A);
 public static Color Good=>C(0x12724E,0x96E8B2);
 public static Color Bad=>C(0xD92D3A,0xFF707B);
 public static Color Lower=>C(0xAB2443,0xFF99A6);       // 기존 기록보다 낮은 이번 기록, 상태 줄의 실패 점
 public static Color Warn=>C(0x733B03,0xFFCF94);        // 실패 안내의 글자
 public static Color WarnBack=>C(0xFFF2DF,0x352923);    // 실패 안내의 바탕
 public static Color Select=>Mix(Accent,Window,Dark?.76:.86);
 // 기록 창의 표(목록, 히스토리)의 색
 public static Color Table=>C(0xFFFFFF,0x1C1627);       // 표의 바탕
 public static Color RowLine=>C(0xF0EDF1,0x382E43);     // 줄 사이의 옅은 선
 public static Color RowPick=>C(0xFAEAF0,0x38303B);     // 고른 줄의 바탕
 public static Color Pb=>C(0x205CA5,0x93C5FF);          // 최고 SCORE 와 같은 점수, PB 표식
 public static Color PbWash=>C(0xEAF2FD,0x28283C);      // 최고 SCORE 와 같은 줄의 바탕
 public static Color Score=>C(0x271E26,0xFFFFFF);       // 표와 일시 미상 기록의 일반 SCORE
 public static Color GraphTrace=>C(0x655770,0xCBBED8);  // 그래프의 플레이 선과 점, 낮은 판의 마름모
 public static Color GraphGrid=>C(0xE0DCE2,0x473C53);   // 그래프의 숫자가 붙은 눈금선(그 밖의 눈금선은 RowLine)
 public static Color Mix(Color a,Color b,double amount)=>Color.FromArgb((int)(a.R+(b.R-a.R)*amount),(int)(a.G+(b.G-a.G)*amount),(int)(a.B+(b.B-a.B)*amount));
 public static Color For(Control c)=>(c.Tag as string)switch{"dim"=>Dim,"accent"=>Accent,"good"=>Good,"bad"=>Bad,"warn"=>Warn,_=>Text};

 // 꼬리표 색은 오버레이의 것을 쓰고, 버튼과 난이도만 조금 연하게 한다(판정은 그대로)
 static readonly HashSet<string> Softened=new(){"4B","5B","6B","8B","NORMAL","HARD","MAXIMUM","SC"};
 public static Color BadgeBack(string text){var color=BadgeDesign.Background(text);return Softened.Contains(text)?Mix(color,Color.White,.18):color;}
 public static void DrawBadge(Graphics g,string text,Font font,RectangleF r)
 {
  // 글자의 가장자리를 부드럽게 그린다. 정해 주지 않으면 새로 만든 그림에서는 계단처럼 각지게 나온다(오버레이의 그림도 같은 설정을 쓴다).
  // 받은 Graphics 의 설정은 그리고 나서 되돌려, 이 뒤의 다른 그리기에 남지 않게 한다
  var smoothing=g.SmoothingMode;var hint=g.TextRenderingHint;
  g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
  using var path=BadgeDesign.Rounded(r,5);
  using var fill=new SolidBrush(BadgeBack(text));g.FillPath(fill,path);using var ink=new SolidBrush(BadgeDesign.Foreground(text));
  using var format=new StringFormat(){Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center};g.DrawString(text,font,ink,r,format);
  g.SmoothingMode=smoothing;g.TextRenderingHint=hint;
 }

 [DllImport("dwmapi.dll")]static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
 [DllImport("uxtheme.dll",CharSet=CharSet.Unicode)]static extern int SetWindowTheme(IntPtr window,string? app,string? ids);
 [StructLayout(LayoutKind.Sequential)]struct Bounds{public int Left,Top,Right,Bottom;}
 [StructLayout(LayoutKind.Sequential)]struct ComboInfo{public int Size;public Bounds Item,Button;public int State;public IntPtr Combo,Edit,List;}
 [DllImport("user32.dll")]static extern bool GetComboBoxInfo(IntPtr combo,ref ComboInfo info);
 // 스크롤 막대는 Windows 가 그린다. 창마다 SetWindowTheme 으로 현재 모드(라이트·다크)의 테마를 적용한다
 static void Scrollbars(IntPtr window)=>SetWindowTheme(window,Dark?"DarkMode_Explorer":null,null);
 public static void ApplyAll(){foreach(var form in Application.OpenForms.OfType<AppWindow>().ToArray())Apply(form);}
 public static void Apply(Form form)
 {
  // 단추의 모양·최소 크기·굵은 글꼴을 바꿀 때마다 창 전체의 레이아웃을 다시 계산하면 주 화면 하나에 2.5초가 든다(잰 값).
  // 다 바꿀 때까지 레이아웃 계산을 멈췄다가 끝에 한 번만 계산한다.
  var held=Hold(form);
  try
  {
   form.BackColor=Window;form.ForeColor=Text;
   if(form.AcceptButton is Button accept&&accept.Tag==null)accept.Tag="primary";
   if(form is AppWindow window)window.ApplyFrame();   // 제목 띠와 창 버튼 뒤의 색
   else{int dark=Dark?1:0;DwmSetWindowAttribute(form.Handle,20,ref dark,4);}
   if(form.AutoScroll)Scrollbars(form.Handle);
   Walk(form);
  }
  finally{Release(held);}
  form.Invalidate(true);
 }
 // 이 컨트롤과 그 안의 모든 컨트롤의 레이아웃 계산을 멈춘다(SuspendLayout). Release 는 안쪽부터 풀면서 그동안 밀린 레이아웃을 한 번씩만 계산한다(안쪽의 크기가 정해진 뒤에 바깥을 계산한다).
 internal static List<Control> Hold(Control top){var all=new List<Control>();void Visit(Control c){all.Add(c);c.SuspendLayout();foreach(Control child in c.Controls)Visit(child);}Visit(top);return all;}
 internal static void Release(List<Control> held){for(int i=held.Count-1;i>=0;i--)if(!held[i].IsDisposed)held[i].ResumeLayout(true);}

 // Windows 가 그리는 컨트롤 가운데 색을 정할 수 없는 것은 그려진 뒤에 덧그린다(체크 상자의 네모, 숫자 칸의 위아래 단추). Paint 핸들러는 컨트롤마다 한 번만 등록한다
 static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control,object> hooked=new();
 static void Hook(Control control,PaintEventHandler paint){if(hooked.TryGetValue(control,out _))return;hooked.Add(control,paint);control.Paint+=paint;}
 static void PaintCheck(object? sender,PaintEventArgs e)
 {
  // 체크 상자는 통째로 직접 그린다: 네모, 글자, 초점 표시가 모두 앱의 단계를 따른다.
  // Windows 가 그리는 네모는 Windows 배율의 크기라, 앱만 키우면 글자는 커지고 네모는 작게 남는다. 쓸 수 없는 체크 상자의 글자도 Windows 의 회색(다크에서 바탕보다 어둡다)이 아니라 옅은 색으로 적는다
  var box=(CheckBox)sender!;if(box.Appearance!=Appearance.Normal)return;
  var g=e.Graphics;using(var around=new SolidBrush(box.BackColor))g.FillRectangle(around,box.ClientRectangle);
  int size=S(13);var r=new Rectangle(box.Padding.Left,(box.Height-size)/2,size,size);float k=size/13f;
  bool on=box.CheckState!=CheckState.Unchecked;var edge=!box.Enabled?Line:on?Fill:Mix(Dim,Window,.4);
  using(var fill=new SolidBrush(on?edge:Field))g.FillRectangle(fill,r);
  using(var pen=new Pen(edge))g.DrawRectangle(pen,r.X,r.Y,r.Width-1,r.Height-1);
  if(on)
  {
   var smoothing=g.SmoothingMode;g.SmoothingMode=SmoothingMode.AntiAlias;using var tick=new Pen(OnAccent,1.7f*k);
   g.DrawLines(tick,new[]{new PointF(r.X+3f*k,r.Y+r.Height/2f),new PointF(r.X+r.Width/2f-1f*k,r.Bottom-4f*k),new PointF(r.Right-3f*k,r.Y+3.5f*k)});
   g.SmoothingMode=smoothing;
  }
  if(box.Text.Length==0){if(box.Focused&&box.ShowFocusCuesNow())using(var focus=new Pen(Accent))g.DrawRectangle(focus,r.X-2,r.Y-2,r.Width+3,r.Height+3);return;}
  var words=new Rectangle(r.Right+1+S(3),0,Math.Max(0,box.Width-r.Right-1-S(3)),box.Height);
  TextRenderer.DrawText(g,box.Text,box.Font,words,box.Enabled?box.ForeColor:Mix(Dim,box.BackColor,.45),TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);
  if(box.Focused&&box.ShowFocusCuesNow())
  {
   var measured=TextRenderer.MeasureText(g,box.Text,box.Font,words.Size,TextFormatFlags.Left|TextFormatFlags.SingleLine);
   var ring=new Rectangle(words.X+1,(box.Height-measured.Height)/2,Math.Min(measured.Width-2,words.Width-2),measured.Height);
   ControlPaint.DrawFocusRectangle(g,ring,box.ForeColor,box.BackColor);
  }
 }
 // Windows 가 잡아 두는 네모의 자리는 Windows 배율의 크기다. 앱의 단계가 더 크면 직접 그리는 네모가 그보다 커서 글자가 오른쪽으로 밀린다. 밀리는 만큼 오른쪽 여백을 더해 글자가 잘리지 않게 한다(한 번)
 static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CheckBox,object> widened=new();
 static void WidenCheck(CheckBox box)
 {
  if(box.Appearance!=Appearance.Normal||box.Text.Length==0||widened.TryGetValue(box,out _))return;widened.Add(box,box);
  int glyph;using(var g=box.CreateGraphics())glyph=CheckBoxRenderer.GetGlyphSize(g,System.Windows.Forms.VisualStyles.CheckBoxState.UncheckedNormal).Width;
  int extra=Math.Max(0,S(13)-glyph)+Math.Max(0,S(3)-3);if(extra>0)box.Padding=new Padding(box.Padding.Left,box.Padding.Top,box.Padding.Right+extra,box.Padding.Bottom);
 }
 // 단추: 모서리 3px, 테두리 한 줄. 마우스를 올리면 바탕과 테두리가 바뀌고 아래쪽에 2px 선이 생긴다(글자와 단추의 자리는 그대로).
 // 키보드로 옮겨 온 초점은 안쪽 외곽선으로 표시한다. Windows 가 그린 단추 위에 통째로 덧그린다(단추를 만드는 코드는 그대로 둔다)
 static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Button,object> hovered=new();
 static void HookButton(Button b)
 {
  if(hooked.TryGetValue(b,out _))return;
  Hook(b,PaintButton);
  b.MouseEnter+=(_,_)=>{hovered.AddOrUpdate(b,b);b.Invalidate();};b.MouseLeave+=(_,_)=>{hovered.Remove(b);b.Invalidate();};
 }
 static void PaintButton(object? sender,PaintEventArgs e)
 {
  var b=(Button)sender!;string kind=b.Tag as string??"";if(kind=="swatch")return;
  bool primary=kind=="primary",strong=kind=="default",danger=kind=="danger",hot=b.Enabled&&hovered.TryGetValue(b,out _);
  Color around=b.Parent?.BackColor??Window;
  Color back=primary?(hot?FillHover:Fill):hot?HoverBack:strong?Soft:ButtonBack;
  Color edge=primary?Accent:hot?HoverEdge:strong?Accent:danger?Bad:Border;
  Color ink=primary?OnAccent:hot?Text:strong?Accent:danger?Bad:Text;
  if(!b.Enabled){back=Mix(back,around,primary?.55:0);edge=Mix(edge,around,.45);ink=Mix(ink,back,.55);}
  var g=e.Graphics;var r=new Rectangle(0,0,b.Width-1,b.Height-1);
  using(var clear=new SolidBrush(around))g.FillRectangle(clear,b.ClientRectangle);
  var smoothing=g.SmoothingMode;g.SmoothingMode=SmoothingMode.AntiAlias;
  using(var shape=BadgeDesign.Rounded(r,S(3f)))
  {
   using(var fill=new SolidBrush(back))g.FillPath(fill,shape);
   if(hot){var clip=g.Clip;g.SetClip(shape);using var under=new SolidBrush(primary?FillHoverEdge:HoverEdge);g.FillRectangle(under,0,b.Height-S(3),b.Width,S(3));g.Clip=clip;}
   using(var pen=new Pen(edge))g.DrawPath(pen,shape);
  }
  if(b.Focused&&b.ShowFocusCuesNow())using(var focus=new Pen(primary?OnAccent:Accent){DashStyle=DashStyle.Solid})using(var inner=BadgeDesign.Rounded(new RectangleF(2,2,b.Width-5,b.Height-5),2))g.DrawPath(focus,inner);
  g.SmoothingMode=smoothing;
  if(b is KeyButton keyed){keyed.DrawContent(g,ink);return;}   // 이름과 단축키를 함께 보이는 단추는 글자를 스스로 그린다
  var flags=TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|(b.UseMnemonic?TextFormatFlags.HidePrefix:TextFormatFlags.NoPrefix)|(b.AutoEllipsis?TextFormatFlags.EndEllipsis:0);   // 말줄임을 켠 단추는 넘치는 글자를 끝에서 '…'로
  TextRenderer.DrawText(g,b.Text,b.Font,b.ClientRectangle,ink,flags);
 }
 // 키보드로 초점을 옮겼을 때만 초점 표시를 보인다(Windows 의 규칙을 따른다)
 static bool ShowFocusCuesNow(this ButtonBase b)=>(SendMessage(b.Handle,0x0129,IntPtr.Zero,IntPtr.Zero).ToInt64()&1)==0;   // WM_QUERYUISTATE, UISF_HIDEFOCUS
 [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr window,int message,IntPtr w,IntPtr l);
 static void PaintSpin(object? sender,PaintEventArgs e)
 {
  var c=(Control)sender!;var g=e.Graphics;int half=c.Height/2;
  using(var back=new SolidBrush(ButtonBack))g.FillRectangle(back,c.ClientRectangle);
  using(var line=new Pen(Border)){g.DrawLine(line,0,0,0,c.Height);g.DrawLine(line,0,half,c.Width,half);}
  g.SmoothingMode=SmoothingMode.AntiAlias;using var arrow=new Pen(Dim,1.4f);float x=c.Width/2f,up=half/2f,down=half+(c.Height-half)/2f;
  g.DrawLines(arrow,new[]{new PointF(x-3,up+1.5f),new PointF(x,up-1.5f),new PointF(x+3,up+1.5f)});
  g.DrawLines(arrow,new[]{new PointF(x-3,down-1.5f),new PointF(x,down+1.5f),new PointF(x+3,down-1.5f)});
 }
 // 펼친 목록의 스크롤 막대
 static void ComboList(ComboBox combo)
 {
  if(!combo.IsHandleCreated)return;var info=new ComboInfo{Size=Marshal.SizeOf<ComboInfo>()};
  if(GetComboBoxInfo(combo.Handle,ref info)&&info.List!=IntPtr.Zero)Scrollbars(info.List);
 }

 static void Walk(Control parent)
 {
  foreach(Control c in parent.Controls)
  {
   if(c is ScrollableControl scrolling&&(scrolling.AutoScroll||c is VerticalScroller)&&scrolling.IsHandleCreated)Scrollbars(scrolling.Handle);   // 넘칠 때만 굴리는 칸은 아직 굴리지 않아도 미리 맞춘다
   switch(c)
   {
    case Button b:
     string kind=b.Tag as string??"";bool primary=kind=="primary";
     b.FlatStyle=FlatStyle.Flat;b.FlatAppearance.BorderSize=1;b.Cursor=Cursors.Hand;
     if(kind=="swatch"){b.FlatAppearance.BorderColor=Border;break;}
     // 단추의 겉모습은 PaintButton 이 통째로 그린다. 여기서 정하는 색은 그 전에 잠깐 보이는 바탕과 크기 계산에만 쓰인다
     b.FlatAppearance.BorderColor=primary||kind=="default"?Accent:kind=="danger"?Bad:Border;
     b.BackColor=primary?Fill:kind=="default"?Soft:ButtonBack;b.ForeColor=primary?OnAccent:kind=="default"?Accent:kind=="danger"?Bad:Text;b.MinimumSize=new(b.MinimumSize.Width>0?b.MinimumSize.Width:primary?S(72):0,S(27));   // 너비를 정해 준 단추(창 아래의 단추 줄)는 그대로 둔다
     if((primary||kind is "default" or "danger")&&!b.Font.Bold)b.Font=Bold(b.Font);
     HookButton(b);
     break;
    case NumericUpDown number:
     number.BackColor=Field;number.ForeColor=Text;
     if(number.Tag is not "bare"){number.BorderStyle=BorderStyle.FixedSingle;Frame.Watch(number);}   // 테두리를 스스로 그리는 칸 안의 숫자 칸(PercentBox)은 그대로 둔다
     if(number.Controls.Count>0)Hook(number.Controls[0],PaintSpin);   // 첫 자식이 위아래 단추다
     break;
    case TrackBar bar:bar.BackColor=bar.Parent?.BackColor??Window;break;
    case ListView list:list.BackColor=list.Tag is "table"?Table:Field;list.ForeColor=Text;if(list.IsHandleCreated)Scrollbars(list.Handle);break;
    case InfoBox box:box.BackColor=Side;break;
    case WrapBox wrap:wrap.BackColor=wrap.OnField?Field:wrap.Parent?.BackColor??Window;wrap.ForeColor=wrap.Dimmed?Dim:Text;break;
    case TextBox t:
     t.BackColor=Field;t.ForeColor=Text;
     if(t.BorderStyle!=BorderStyle.None&&t.Parent is not UpDownBase){t.BorderStyle=BorderStyle.FixedSingle;Frame.Watch(t);}
     break;
    case ComboBox combo:combo.BackColor=Field;combo.ForeColor=Text;ComboList(combo);break;
    case CheckBox check:check.ForeColor=Text;WidenCheck(check);Hook(check,PaintCheck);break;
    case HelpBadge badge:badge.ForeColor=Dim;break;
    case Label label:label.ForeColor=For(label);break;
    case SidePanel side:side.BackColor=Side;break;
    case Panel panel when panel.Tag is string tag:panel.BackColor=tag=="line"?Line:tag=="border"?Border:tag=="field"?Field:panel.BackColor;break;
   }
   Walk(c);
  }
 }
}

// 글 칸과 숫자 칸의 테두리. Windows 가 그리는 테두리(창의 바깥 줄)는 색을 정할 수 없어서, 그려질 때마다 그 위에 디자인 색으로 한 줄을 긋는다
sealed class Frame:NativeWindow
{
 [DllImport("user32.dll")]static extern IntPtr GetWindowDC(IntPtr window);
 [DllImport("user32.dll")]static extern int ReleaseDC(IntPtr window,IntPtr dc);
 static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control,Frame> watched=new();
 readonly Control owner;
 Frame(Control control)
 {
  owner=control;if(control.IsHandleCreated)AssignHandle(control.Handle);
  control.HandleCreated+=(_,_)=>AssignHandle(control.Handle);control.HandleDestroyed+=(_,_)=>ReleaseHandle();
 }
 public static void Watch(Control control){if(watched.TryGetValue(control,out var frame))frame.Draw();else watched.Add(control,new Frame(control));}
 protected override void WndProc(ref Message m){base.WndProc(ref m);if(m.Msg is 0x0085 or 0x000F or 0x0007 or 0x0008)Draw();}   // 바깥 줄을 그릴 때, 안을 그릴 때, 포커스가 오갈 때
 void Draw()
 {
  if(Handle==IntPtr.Zero)return;var dc=GetWindowDC(Handle);if(dc==IntPtr.Zero)return;
  try{using var g=Graphics.FromHdc(dc);using var pen=new Pen(owner.Focused||owner.ContainsFocus?Theme.Accent:Theme.Border);g.DrawRectangle(pen,0,0,owner.Width-1,owner.Height-1);}
  finally{ReleaseDC(Handle,dc);}
 }
}

// 버튼·난이도·판정 꼬리표. 크기는 글자와 창의 크기 단계에 맞춰 스스로 정한다(높이 22, 좌우 여백 9. 100% 때의 값)
sealed class Chip:Control
{
 string? shown;
 // 새 곡 정보가 왔을 때 꼬리표가 차례로 나타나는 모습의 진행도(1 이면 다 나타난 것): 옅은 데서 진해지며 2px 아래에서 올라온다
 double reveal=1;
 public void SetReveal(double value){if(reveal==value)return;reveal=value;Invalidate();}
 public Chip()
 {
  SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
  Font=Theme.Px(AppFonts.Ui,9f,FontStyle.Bold);Margin=new(0,0,4,0);Height=Theme.S(22);Visible=false;TabStop=false;
 }
 // 창이 뜰 때의 키우기에서는 자리와 바깥 여백만 따른다. 크기는 위에서 이미 단계에 맞췄다
 protected override void ScaleControl(SizeF factor,BoundsSpecified specified)=>base.ScaleControl(factor,specified&BoundsSpecified.Location);
 // 영문·숫자 꼬리표(8B, SC, MAXIMUM)는 Oxanium, 한글이 든 꼬리표(하드판정, 고정)는 일반 글자의 글꼴. 크기는 12px
 static readonly Lazy<Font> LatinFont=new(()=>Theme.PxS(AppFonts.Latin,9f,FontStyle.Bold)),HangulFont=new(()=>Theme.PxS(AppFonts.Ui,9f,FontStyle.Bold));
 static bool Latin(string text)=>text.All(c=>c<0x80);
 Font Face=>Latin(Text)?LatinFont.Value:HangulFont.Value;
 // 오른쪽 아래 모서리만 5px 잘린 네모
 static GraphicsPath Cut(RectangleF r,float cut){var p=new GraphicsPath();p.AddPolygon(new[]{new PointF(r.Left,r.Top),new PointF(r.Right,r.Top),new PointF(r.Right,r.Bottom-cut),new PointF(r.Right-cut,r.Bottom),new PointF(r.Left,r.Bottom)});return p;}
 /// <summary>꼬리표의 너비(다른 부품이 제 그림 안에 그릴 때)</summary>
 public static int Measure(string text)=>TextRenderer.MeasureText(text,Latin(text)?LatinFont.Value:HangulFont.Value,Size.Empty,TextFormatFlags.NoPadding).Width+Theme.S(18);
 /// <summary>다른 부품이 제 그림 안에 꼬리표를 그린다(기록 목록의 줄, 히스토리의 머리). x 에서 시작해 세로 가운데를 middle 에 맞춘다. 그린 너비를 돌려준다</summary>
 public static int Draw(Graphics g,string text,float x,float middle)
 {
  int width=Measure(text),height=Theme.S(22);var r=new RectangleF((float)Math.Round(x),(float)Math.Round(middle-height/2f),width,height);
  var smoothing=g.SmoothingMode;g.SmoothingMode=SmoothingMode.AntiAlias;
  using(var path=Cut(r,Theme.S(5f)))using(var fill=new SolidBrush(Theme.BadgeBack(text)))g.FillPath(fill,path);
  g.SmoothingMode=smoothing;
  TextRenderer.DrawText(g,text,Latin(text)?LatinFont.Value:HangulFont.Value,Rectangle.Round(r),BadgeDesign.Foreground(text),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix);
  return width;
 }
 public void Set(string text)
 {
  if(text==shown)return;shown=text;   // 같은 글이면 다시 재거나 그리지 않는다(주 화면이 상태를 고칠 때마다 불린다)
  Text=text;Visible=text.Length>0;
  Width=TextRenderer.MeasureText(text,Face,Size.Empty,TextFormatFlags.NoPadding).Width+Theme.S(18);Invalidate();
 }
 protected override void OnPaint(PaintEventArgs e)
 {
  var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Parent?.BackColor??Theme.Window);
  if(reveal<=0)return;
  Color around=Parent?.BackColor??Theme.Window,back=Theme.BadgeBack(Text),ink=BadgeDesign.Foreground(Text);float drop=0;
  if(reveal<1){back=Theme.Mix(back,around,1-reveal);ink=Theme.Mix(ink,around,1-reveal);drop=Theme.S(2f)*(float)(1-reveal);}
  using var path=Cut(new RectangleF(0,drop,Width,Height-drop),Theme.S(5f));
  const TextFormatFlags center=TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix;
  using var fill=new SolidBrush(back);g.FillPath(fill,path);
  // 글자는 꼬리표의 세로 가운데에 놓는다. 디자인 기준의 '영문·숫자만 2px 아래로'는 브라우저에서 가운데에 오게 하는 값이고, 여기서는 내리지 않을 때 기준 그림과 같다(잰 값: 글자 위아래의 여백이 기준 그림 6·6, 여기 7·6)
  var area=ClientRectangle;area.Offset(0,(int)Math.Round(drop));
  TextRenderer.DrawText(g,Text,Face,area,ink,center);
 }
}

// 묶음 제목: 사선 → 제목 → 가로선. 사선은 너비 4, 높이 12, 기울기 −12°, 글자와의 사이 7(글자 14px 일 때의 값. 글자가 커지면 같은 비율로 커진다)
sealed class SectionHeader:Control
{
 public bool Rule=true;
 /// <summary>제목 글자를 강조색으로 그린다</summary>
 public bool Keyed;
 public SectionHeader(string title)
 {
  SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
  Text=title;Height=22;Font=Theme.Px(AppFonts.Head,10.5f,FontStyle.Bold);TabStop=false;
 }
 float Unit=>Font.Size/Theme.TextPx(14f);
 int Lead=>(int)Math.Round(2*Unit);
 int TextLeft=>Lead+(int)Math.Round(4*Unit)+(int)Math.Round(7*Unit);
 public int TextWidth=>TextLeft+TextRenderer.MeasureText(Text,Font,Size.Empty,TextFormatFlags.NoPadding).Width;
 // 가로선이 없는 제목은 글자에서 끝난다. 글자가 바뀌거나 커지면 너비를 다시 맞춘다
 void Refit(){if(!Rule&&IsHandleCreated)Width=TextWidth+Lead;Invalidate();}
 protected override void OnFontChanged(EventArgs e){base.OnFontChanged(e);if(!Rule)Width=TextWidth+Lead;Invalidate();}
 protected override void OnTextChanged(EventArgs e){base.OnTextChanged(e);Refit();}
 protected override void OnPaint(PaintEventArgs e)
 {
  var g=e.Graphics;g.Clear(Parent?.BackColor??Theme.Window);
  var size=TextRenderer.MeasureText(Text,Font,Size.Empty,TextFormatFlags.NoPadding);
  float wide=4*Unit,tall=12*Unit,mid=Height/2f,lean=(float)Math.Tan(12*Math.PI/180)*tall/2;
  g.SmoothingMode=SmoothingMode.AntiAlias;
  using(var slash=new SolidBrush(Theme.Stripe))
   g.FillPolygon(slash,new[]{new PointF(Lead+lean,mid-tall/2f),new PointF(Lead+lean+wide,mid-tall/2f),new PointF(Lead-lean+wide,mid+tall/2f),new PointF(Lead-lean,mid+tall/2f)});
  g.SmoothingMode=SmoothingMode.None;
  int x=TextLeft;
  TextRenderer.DrawText(g,Text,Font,new Point(x,(Height-size.Height)/2),Keyed?Theme.Fill:Theme.Text,TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix);
  if(Rule){using var pen=new Pen(Theme.Line);g.DrawLine(pen,x+size.Width+(int)Math.Round(10*Unit),Height/2,Width,Height/2);}
 }
}

// 라이트 / 다크 전환 단추: 둥근 틀 안에 두 칸. 고른 쪽은 채운 색, 다른 쪽은 마우스를 올리면 바탕이 바뀐다.
// 크기는 글자에 맞춰 스스로 정한다(글자 11px 일 때 높이 27, 칸의 좌우 여백 11)
sealed class ModeSwitch:Control
{
 public event Action? Toggled;
 int hot=-1;
 public ModeSwitch()
 {
  SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
  Font=Theme.Px(AppFonts.UiSemi,8.25f);Cursor=Cursors.Hand;AccessibleName="밝기 모드";AccessibleRole=AccessibleRole.PushButton;TabStop=false;Fit();
 }
 float Unit=>Font.Size/Theme.TextPx(11f);
 int U(float px)=>(int)Math.Round(px*Unit);
 (Rectangle Light,Rectangle Dark) Parts()
 {
  int Wide(string text)=>TextRenderer.MeasureText(text,Font,Size.Empty,TextFormatFlags.NoPadding).Width+U(22);
  var light=new Rectangle(U(3),U(3),Wide("라이트"),U(21));return(light,new Rectangle(light.Right+U(2),U(3),Wide("다크"),U(21)));
 }
 void Fit(){Size=new(Parts().Dark.Right+U(3),U(27));}
 protected override void OnFontChanged(EventArgs e){base.OnFontChanged(e);int right=Right;Fit();Left=right-Width;Invalidate();}   // 오른쪽 끝을 지킨다
 protected override void OnPaint(PaintEventArgs e)
 {
  var g=e.Graphics;g.Clear(Parent?.BackColor??Theme.Side);g.SmoothingMode=SmoothingMode.AntiAlias;var(light,dark)=Parts();
  const TextFormatFlags center=TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding;
  using(var frame=BadgeDesign.Rounded(new RectangleF(0,0,Width-1,Height-1),4*Unit)){using var back=new SolidBrush(Theme.ButtonBack);g.FillPath(back,frame);using var pen=new Pen(Theme.Line);g.DrawPath(pen,frame);}
  void Part(Rectangle r,string text,bool active,bool over)
  {
   if(active||over){using var shape=BadgeDesign.Rounded(new RectangleF(r.X,r.Y,r.Width,r.Height),2*Unit);using var fill=new SolidBrush(active?(over?Theme.FillHover:Theme.Fill):Theme.HoverBack);g.FillPath(fill,shape);}
   TextRenderer.DrawText(g,text,Font,r,active?Theme.OnAccent:over?Theme.Text:Theme.Dim,center);
  }
  Part(light,"라이트",!Theme.Dark,hot==0);Part(dark,"다크",Theme.Dark,hot==1);
 }
 protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);int now=e.X<Parts().Dark.Left-1?0:1;if(now!=hot){hot=now;Invalidate();}}
 protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);if(hot!=-1){hot=-1;Invalidate();}}
 protected override void OnMouseClick(MouseEventArgs e)
 {
  bool wantDark=e.X>=Parts().Dark.Left-1;if(wantDark==Theme.Dark)return;
  Theme.Dark=wantDark;Toggled?.Invoke();
 }
}

// 주 화면의 오른쪽 칸: 옅은 바탕, 왼쪽에 가는 줄
sealed class SidePanel:Panel
{
 public SidePanel(){SetStyle(ControlStyles.ResizeRedraw|ControlStyles.OptimizedDoubleBuffer,true);}
 /// <summary>칸의 바탕 위에 덧그릴 것(등록을 눌렀을 때 목표·현재 아래를 지나는 빛)</summary>
 public Action<Graphics>? Overlay;
 protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);using var pen=new Pen(Theme.Line);e.Graphics.DrawLine(pen,0,0,0,Height);Overlay?.Invoke(e.Graphics);}
}

// 목록 상자: 테두리와 화살표를 디자인 색으로 덧그린다(Windows 가 그리는 것은 다크에서 흰 테두리가 남는다). 모서리 3px.
// Roomy 를 켜면 높이가 단추와 같은 27 이 된다(글자 13px 일 때. 항목을 직접 그려야 높이를 정할 수 있다). 켜지 않은 목록 상자는 Windows 가 정하는 높이 그대로다
sealed class ThemedCombo:ComboBox
{
 public ThemedCombo(){FlatStyle=FlatStyle.Flat;}
 [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
 public bool Roomy{get=>DrawMode==DrawMode.OwnerDrawFixed;set{if(!value)return;DrawMode=DrawMode.OwnerDrawFixed;Tall();}}
 float Unit=>Font.Size/Theme.TextPx(13f);
 /// <summary>Roomy 일 때의 높이(글자 13px 일 때의 값). Roomy 보다 먼저 정한다</summary>
 public int Tallness=27;
 void Tall(){int height=Math.Max(8,(int)Math.Round(Tallness*Unit)-6);if(ItemHeight!=height)ItemHeight=height;}   // 상자의 높이는 항목의 높이 + 6 이다
 protected override void OnFontChanged(EventArgs e){base.OnFontChanged(e);if(Roomy)Tall();}
 // 코드가 고른 것을 바꾸면 Windows 가 글 자리만 다시 그려 화살표가 지워진 채 남을 때가 있다(큰 단계에서 봤다). 상자를 통째로 다시 그린다
 protected override void OnSelectedIndexChanged(EventArgs e){base.OnSelectedIndexChanged(e);if(Roomy&&IsHandleCreated)Invalidate();}
 protected override void OnDrawItem(DrawItemEventArgs e)
 {
  if(e.Index<0)return;
  // 닫힌 상자의 글 자리는 고른 항목이라도 강조하지 않는다(초점이 있을 때 Windows 가 이 자리를 따로 그리게 한다. 상자의 모양은 WndProc 이 그린다)
  bool picked=(e.State&DrawItemState.Selected)!=0&&(e.State&DrawItemState.ComboBoxEdit)==0;int inset=(int)Math.Round(6*Unit);
  using(var back=new SolidBrush(picked?Theme.Select:Theme.Field))e.Graphics.FillRectangle(back,e.Bounds);
  TextRenderer.DrawText(e.Graphics,GetItemText(Items[e.Index]),Font,new Rectangle(e.Bounds.X+inset,e.Bounds.Y,e.Bounds.Width-inset-2,e.Bounds.Height),Theme.Text,TextFormatFlags.VerticalCenter|TextFormatFlags.Left|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
 }
 [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]struct PaintInfo{public IntPtr Dc;public int Erase,Left,Top,Right,Bottom,Restore,Update;[System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValArray,SizeConst=32)]public byte[] Reserved;}
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern IntPtr BeginPaint(IntPtr window,out PaintInfo info);
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool EndPaint(IntPtr window,ref PaintInfo info);
 // 닫힌 상자(고르는 목록의 것)는 프로그램만 그린다. Windows 가 먼저 그리고 그 위에 덧그리면 마우스를 올리거나 목록을 펼 때마다
 // Windows 의 모양(하얀 화살표 단추)이 한순간 먼저 보인다(실제 화면을 이어 찍어 확인했다).
 // 그리는 것은 메모리의 그림에 다 그린 뒤 한 번에 옮긴다(바탕을 지우는 것도 건너뛴다)
 protected override void WndProc(ref Message m)
 {
  if(DropDownStyle==ComboBoxStyle.DropDownList&&IsHandleCreated)
  {
   if(m.Msg==0x0014){m.Result=(IntPtr)1;return;}   // WM_ERASEBKGND: 지우지 않는다(아래에서 통째로 그린다)
   if(m.Msg==0x000F)
   {
    IntPtr dc=BeginPaint(Handle,out var info);
    try
    {
     var r=ClientRectangle;if(r.Width>0&&r.Height>0&&dc!=IntPtr.Zero)
     {
      using var buffer=BufferedGraphicsManager.Current.Allocate(dc,r);
      DrawClosed(buffer.Graphics,r);buffer.Render();
     }
    }
    finally{EndPaint(Handle,ref info);}
    return;
   }
  }
  base.WndProc(ref m);
 }
 void DrawClosed(Graphics g,Rectangle r)
 {
  float k=Unit;
  using(var around=new SolidBrush(Parent?.BackColor??Theme.Window))g.FillRectangle(around,r);
  g.SmoothingMode=SmoothingMode.AntiAlias;
  // 쓸 수 없을 때(점수 확인·저장 확인 대기 중)는 단추처럼 바탕 쪽으로 흐리게 그린다
  Color behind=Parent?.BackColor??Theme.Window,field=Enabled?Theme.Field:Theme.Mix(Theme.Field,behind,.5),ink=Enabled?Theme.Text:Theme.Mix(Theme.Dim,behind,.5);
  using(var shape=BadgeDesign.Rounded(new RectangleF(0,0,r.Width-1,r.Height-1),3*k)){using var back=new SolidBrush(field);g.FillPath(back,shape);using var pen=new Pen(Focused?Theme.Accent:Enabled?Theme.Border:Theme.Mix(Theme.Border,behind,.45));g.DrawPath(pen,shape);}
  TextRenderer.DrawText(g,Text,Font,new Rectangle((int)(7*k),0,r.Width-(int)(28*k),r.Height),ink,TextFormatFlags.VerticalCenter|TextFormatFlags.Left|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
  using var arrow=new Pen(Enabled?Theme.Dim:ink,1.5f*k);float x=r.Width-14*k,y=r.Height/2f;
  g.DrawLines(arrow,new[]{new PointF(x-4*k,y-2*k),new PointF(x,y+2*k),new PointF(x+4*k,y-2*k)});
 }
 // 초점이 오가거나 고른 것이 바뀌면 Windows 는 글 자리만 따로 그리라고 한다(OnDrawItem 의 ComboBoxEdit). 테두리의 색(초점)과 화살표까지 맞게 상자를 통째로 다시 그린다
 protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);if(IsHandleCreated)Invalidate();}
 protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);if(IsHandleCreated)Invalidate();}
 protected override void OnEnabledChanged(EventArgs e){base.OnEnabledChanged(e);if(IsHandleCreated)Invalidate();}
}

// 곡과 기록을 보여 주는 옅은 상자
sealed class InfoBox:FlowLayoutPanel
{
 public InfoBox(){AutoSize=true;AutoSizeMode=AutoSizeMode.GrowAndShrink;FlowDirection=FlowDirection.TopDown;WrapContents=false;Padding=new(12,10,12,10);Margin=new(0,0,0,10);SetStyle(ControlStyles.ResizeRedraw,true);}
 protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);using var pen=new Pen(Theme.Line);e.Graphics.DrawRectangle(pen,0,0,Width-1,Height-1);}
}

static class Boxes
{
 public static Label Text(string text,string? tag=null,float size=10f,bool bold=false,int maxWidth=0)=>new(){Text=text,AutoSize=true,Tag=tag,Margin=Padding.Empty,UseMnemonic=false,Font=Theme.Px("맑은 고딕",size,bold?FontStyle.Bold:FontStyle.Regular),MaximumSize=new(maxWidth,0)};
 public static Control Chips(string button,string pattern,string judge)
 {
  var row=new FlowLayoutPanel(){AutoSize=true,WrapContents=false,Margin=new(0,6,0,6)};
  foreach(var text in new[]{button,pattern,judge}){var chip=new Chip();chip.Set(text);row.Controls.Add(chip);}
  return row;
 }
 public static InfoBox Song(string title,string button,string pattern,string judge,params string[] lines)
 {
  var box=new InfoBox();box.Controls.Add(Text(title,null,11.5f,true,480));box.Controls.Add(Chips(button,pattern,judge));
  foreach(var line in lines)box.Controls.Add(Text(line,null,10f,false,480));return box;
 }
 public static Control Stats(float size,params (string Name,string Value,string? Tag)[] items)
 {
  var table=new TableLayoutPanel(){AutoSize=true,ColumnCount=items.Length,RowCount=2,Margin=Padding.Empty};
  for(int i=0;i<items.Length;i++)
  {
   var name=Text(items[i].Name,"dim",9f);name.Margin=new(0,0,28,0);var value=Text(items[i].Value,items[i].Tag,size,true);value.Margin=new(0,0,28,0);
   table.Controls.Add(name,i,0);table.Controls.Add(value,i,1);
  }
  return table;
 }
}

// 예 / 아니요 를 묻는 창. Windows 가 그리는 알림 창 대신 쓴다(다크 모드에서도 같은 모양).
// 기본 단추는 "아니요"다(Enter 로 넘어가지 않게). 닫으면 "아니요"로 본다.
class ChoiceDialog:CompactDialog
{
 protected ChoiceDialog(string title):base(title){}   // 내용을 스스로 채우는 선택 창(프로필 삭제 등)
 public ChoiceDialog(string title,string? lead,Control? box,string? note,string? question):base(title)
 {
  if(lead!=null){var first=Boxes.Text(lead);first.Margin=new(0,0,0,10);Body.Controls.Add(first);}
  if(box!=null)Body.Controls.Add(box);
  if(!string.IsNullOrEmpty(note)){var more=Boxes.Text(note,"dim",10f,false,480);more.Margin=new(0,0,0,10);Body.Controls.Add(more);}
  if(question!=null)Body.Controls.Add(Boxes.Text(question));
  var row=new FlowLayoutPanel(){AutoSize=true,WrapContents=false,Margin=new(0,12,0,0)};
  var yes=new Button(){Text="예",AutoSize=true,DialogResult=DialogResult.Yes,Tag="plain"};
  var no=new Button(){Text="아니요",AutoSize=true,DialogResult=DialogResult.No,Tag="default"};
  row.Controls.AddRange(new Control[]{yes,no});Body.Controls.Add(row);
  AcceptButton=no;CancelButton=no;Shown+=(_,_)=>ActiveControl=no;
 }
 public static DialogResult Ask(IWin32Window owner,string title,string? lead,Control? box,string? note,string? question){using var d=new ChoiceDialog(title,lead,box,note,question);return d.ShowDialog(owner);}
}

// 알리기만 하는 창(단추는 "확인" 하나). Windows 가 그리는 알림 창 대신 쓴다(다크 모드에서도 같은 모양). 닫아도 "확인"과 같다.
class NoticeDialog:CompactDialog
{
 public NoticeDialog(string title,string text):base(title)
 {
  Body.Controls.Add(Boxes.Text(text,null,10f,false,520));
  var row=new FlowLayoutPanel(){AutoSize=true,WrapContents=false,Margin=new(0,14,0,0)};
  var ok=new Button(){Text="확인",AutoSize=true,DialogResult=DialogResult.OK,Tag="default"};
  row.Controls.Add(ok);Body.Controls.Add(row);
  AcceptButton=ok;CancelButton=ok;Shown+=(_,_)=>ActiveControl=ok;
 }
 public static void Show(IWin32Window owner,string title,string text){using var d=new NoticeDialog(title,text);d.ShowDialog(owner);}
 // 주 화면이 생기기 전에 띄우는 알림(시작 오류, 이미 켜져 있음). 주인 창이 없으니 화면 가운데에 띄우고 작업 표시줄에도 보이게 한다.
 // 고른 모드(라이트·다크)는 설정 파일에서 읽어 맞춘다. 이 창을 띄우지 못하면 Windows 알림 창으로라도 알린다(알림이 사라지면 안 된다)
 public static void ShowAlone(string title,string text)
 {
  try{
   try{Theme.Dark=UserOptions.Load(UserFiles.Existing(AppContext.BaseDirectory,"preferences.json")).Dark;}catch{}
   using var d=new NoticeDialog(title,text){StartPosition=FormStartPosition.CenterScreen,ShowInTaskbar=true};d.ShowDialog();
  }catch{MessageBox.Show(text,title);}
 }
}
