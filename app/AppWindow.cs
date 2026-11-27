using System.Drawing.Text;
using System.Runtime.InteropServices;
namespace LadderRecords;

// 프로그램의 모든 창의 바탕.
// 제목 표시줄: Windows 의 제목 줄을 걷어 내고 그 자리(창의 맨 위 띠)에 앱 아이콘과 앱 글꼴의 제목을 직접 그린다. 창 버튼(최소화·최대화·닫기)은 Windows 가 그리고 다룬다.
//   띠의 높이는 Windows 창 버튼의 높이에 맞춘다(배율 100% 에서 30).
//   띠는 창의 본문 영역 안에 있다. 그래서 창에 올린 것들을 띠만큼 아래로 내리고(OnLoad), 크기 계산에서 띠를 더한다. ClientSize 에 넣는 값은 "내용의 크기"(띠 제외)다.
class AppWindow:Form
{
 static readonly Lazy<Icon> ApplicationIcon=new(()=>new Icon(typeof(AppWindow),"AppIcon.ico"));
 static readonly Lazy<Icon> SmallIcon=new(()=>new Icon(ApplicationIcon.Value,16,16));

 // 창이 뜨는 동안에는 컨트롤의 핸들이 하나 생길 때마다 창 전체의 레이아웃을 다시 계산한다(주 화면에서 50번 남짓, 한 번에 30ms. 잰 값).
 // 처음 보일 때부터 Shown 까지 레이아웃 계산을 멈춰 두고, 실제 크기가 필요한 Load 직전에 한 번, Shown 에서 한 번만 계산한다.
 List<Control>? opening;bool opened;
 void Settle(){if(opening is not {} held)return;opening=null;Theme.Release(held);}
 protected override void SetVisibleCore(bool value)
 {
  if(value&&!opened&&opening==null&&!IsDisposed)opening=Theme.Hold(this);
  if(value&&!Visible&&IsHandleCreated&&!IsDisposed)Cloak(true);   // 처음 뜰 때 핸들이 아직 없으면 핸들이 생길 때 가린다(OnHandleCreated)
  try{base.SetVisibleCore(value);}
  catch{Cloak(false);throw;}
  if(value&&opened)Reveal();   // 다시 보일 때: 곧바로 다 그리고 보인다. 처음 뜰 때는 배치가 끝나는 OnShown 에서 보인다
  if(!value)Cloak(false);
 }
 // 창이 뜰 때 하얀 바탕이 먼저 보이지 않게 한다. Windows 는 창을 보이게 한 뒤에 그리라고 알리고, 창의 바탕이 그려진 뒤에도 그 위의 컨트롤들은
 // 하나씩 따로 그려진다. 그때까지 그 자리들이 하얗게 보인다(실제 화면을 이어 찍어 봤다: 주 화면 0.07초쯤, 세부 창은 단추와 바닥줄이 한 장면).
 // 그래서 뜨는 동안에는 창을 가려 두고(DWM 의 cloak: 창은 있는데 화면에만 보이지 않는다), 창과 그 안의 것을 모두 그린 뒤에 푼다.
 // **가린 채로 남으면 창이 영영 보이지 않는다.** 그리다 무슨 일이 나도 반드시 풀고, 가리지 못했으면(예전 Windows) 가리지 않고 그냥 뜬다
 [DllImport("user32.dll")]static extern bool RedrawWindow(IntPtr window,IntPtr area,IntPtr region,uint flags);
 bool cloaked;
 void Cloak(bool on)
 {
  if(cloaked==on||!IsHandleCreated)return;
  try{int value=on?1:0;if(DwmSetWindowAttribute(Handle,13,ref value,4)==0)cloaked=on;else if(!on)cloaked=false;}catch{if(!on)cloaked=false;}
 }
 /// <summary>창과 그 안의 것을 지금 모두 그리고, 가려 둔 것을 푼다</summary>
 void Reveal()
 {
  if(!cloaked)return;
  try{RedrawWindow(Handle,IntPtr.Zero,IntPtr.Zero,0x0001|0x0080|0x0100);}   // 다시 그릴 곳으로 표시 | 자식 창까지 | 지금 그린다
  catch{}
  finally{Cloak(false);}
 }
 /// <summary>지금 화면에서 가려져 있는가(개발용 검사)</summary>
 internal bool Cloaked=>cloaked;
 protected override void OnLoad(EventArgs e)
 {
  Enlarge();Lower();
  Settle();                     // Load 핸들러와 창을 가운데 두는 일은 레이아웃 계산이 끝난 크기를 본다
  base.OnLoad(e);Theme.Apply(this);   // 뜰 때 디자인을 입힌다
  FitScreen();
  if(!opened)opening=Theme.Hold(this);
 }
 protected override void OnFormClosed(FormClosedEventArgs e){Settle();base.OnFormClosed(e);}
 [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
 // 작업 표시줄은 창이 보이는 순간 창에 아이콘을 요청하는데, 응답이 늦으면 Windows 기본 아이콘으로 정하고 다시 요청하지 않는다.
 // 주 화면은 단축키 때문에 창 핸들을 먼저 만들어서, 컨트롤을 만들고 디자인을 입히는 일(몇 초)이 창이 보인 뒤에 돈다. 그동안은 그 요청에 응답하지 못한다.
 // 그 일이 끝난 뒤(Shown)에 아이콘을 한 번 더 알려 작업 표시줄이 다시 읽게 한다.
 // 같은 아이콘을 그대로 다시 넣으면 Windows 가 바뀐 것으로 치지 않으므로, 한 번 비웠다가 다시 넣는다(큰 것 1, 작은 것 0).
 protected override void OnShown(EventArgs e)
 {
  // 마지막 배치 계산이 실패해도 가려 둔 것은 반드시 푼다(가린 채로 남으면 창이 보이지 않는다)
  try{Settle();}
  finally{opened=true;Reveal();}
  base.OnShown(e);
  if(IsDisposed||!IsHandleCreated)return;   // Shown 처리기가 창을 곧바로 닫았으면(일이 첫 걸음에서 실패한 진행 창 등) 그 창에는 더 손대지 않는다
  foreach(int kind in new[]{1,0}){IntPtr icon=SendMessage(Handle,0x007F,(IntPtr)kind,IntPtr.Zero);if(icon==IntPtr.Zero)continue;SendMessage(Handle,0x0080,(IntPtr)kind,IntPtr.Zero);SendMessage(Handle,0x0080,(IntPtr)kind,icon);}
 }
 public AppWindow()
 {
  Icon=ApplicationIcon.Value;
 }

 // ── 제목 띠 ──────────────────────────────────────────────────────────────
 [StructLayout(LayoutKind.Sequential)]struct Rect{public int Left,Top,Right,Bottom;}
 [StructLayout(LayoutKind.Sequential)]struct Margins{public int Left,Right,Top,Bottom;}
 [StructLayout(LayoutKind.Sequential)]struct SizeParams{public Rect First,Second,Third;public IntPtr Position;}
 [DllImport("dwmapi.dll")]static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
 [DllImport("dwmapi.dll")]static extern int DwmGetWindowAttribute(IntPtr window,int attribute,out Rect value,int size);
 [DllImport("dwmapi.dll")]static extern int DwmExtendFrameIntoClientArea(IntPtr window,ref Margins margins);
 [DllImport("dwmapi.dll")]static extern bool DwmDefWindowProc(IntPtr window,int message,IntPtr w,IntPtr l,out IntPtr result);
 [DllImport("uxtheme.dll")]static extern int BufferedPaintInit();
 [DllImport("uxtheme.dll")]static extern IntPtr BeginBufferedPaint(IntPtr target,ref Rect area,int format,IntPtr options,out IntPtr dc);
 [DllImport("uxtheme.dll")]static extern int EndBufferedPaint(IntPtr paint,bool update);
 [DllImport("uxtheme.dll")]static extern int BufferedPaintSetAlpha(IntPtr paint,ref Rect area,byte alpha);
 [DllImport("uxtheme.dll")]static extern int BufferedPaintClear(IntPtr paint,ref Rect area);
 [DllImport("user32.dll")]static extern int GetSystemMetricsForDpi(int index,uint dpi);
 [DllImport("user32.dll")]static extern uint GetDpiForSystem();
 [DllImport("user32.dll")]static extern bool AdjustWindowRectExForDpi(ref Rect area,int style,bool menu,int extended,uint dpi);
 [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
 [DllImport("user32.dll")]static extern bool IsZoomed(IntPtr window);
 const int WM_NCCALCSIZE=0x83,WM_NCHITTEST=0x84,WM_NCACTIVATE=0x86,WM_PRINT=0x317,WM_DWMCOMPOSITIONCHANGED=0x31E;
 static bool painterReady;
 bool lowered,active=true,printing;

 // 이 프로그램은 배율을 시스템 단위로 안다(창마다 다르지 않다). 창 버튼의 크기는 Windows 가 정하고, 그 높이는 제목 줄과 위쪽 틀의 치수에서 나온다(100% 에서 30)
 static uint Dpi=>GetDpiForSystem();
 static int Grip=>GetSystemMetricsForDpi(33,Dpi)+GetSystemMetricsForDpi(92,Dpi);   // 크기를 바꾸는 틀의 두께
 /// <summary>제목 띠의 높이</summary>
 public static int BarHeight=>GetSystemMetricsForDpi(4,Dpi)+Grip-1;
 // Windows 가 이 창의 꼴(틀의 종류)에 잡아 두는 위쪽 바깥 영역의 높이. 그 영역을 걷어 내 본문으로 돌리므로 크기 계산에서 뺀다
 int Reclaimed{get{var cp=CreateParams;var area=new Rect();AdjustWindowRectExForDpi(ref area,cp.Style,false,cp.ExStyle,Dpi);return -area.Top;}}

 protected override void SetClientSizeCore(int x,int y)=>base.SetClientSizeCore(x,y+BarHeight-Reclaimed);
 public override Size GetPreferredSize(Size proposedSize){var size=base.GetPreferredSize(proposedSize);return new Size(size.Width,size.Height-Reclaimed);}
 // 창에 올린 것들을 띠 아래로 내린다(한 번). 가장자리에 붙인 것(Dock)은 안쪽 여백으로, 자리를 정해 올린 것은 자리를 옮겨서
 void Lower()
 {
  if(lowered)return;lowered=true;int bar=BarHeight;
  // 크기를 내용에 맞추는 창(AutoSize)은 안쪽 여백만으로 내용이 내려간다. 자리를 정해 올린 것까지 옮기면 두 번 내려간다(화면에서 확인했다)
  if(!AutoSize)foreach(Control child in Controls)if(child.Dock==DockStyle.None)child.Top+=bar;
  Padding=new Padding(Padding.Left,Padding.Top+bar,Padding.Right,Padding.Bottom);
 }
 // 창의 크기 단계(Theme.Scale)만큼 창 안의 자리·크기·여백과 글꼴을 키운다(한 번. 띠를 내리기 전에). 창과 그 안의 것들은 모두 100% 때의 값으로 만든다.
 // 자리와 크기를 먼저 키우고 글꼴을 나중에 키운다: 글자에 맞춰 스스로 크기를 정하는 부품은 글꼴이 바뀔 때 다시 맞춘다
 bool enlarged;
 /// <summary>이 창이 지금 몇 배로 그려지는가(키우기 전에는 1). 뜨기 전과 뜬 뒤에 모두 불리는 자리 계산이 쓴다</summary>
 protected float Unit=>enlarged?Theme.Scale:1f;
 void Enlarge()
 {
  if(enlarged||Theme.Scale==1f)return;enlarged=true;
  var factor=new SizeF(Theme.Scale,Theme.Scale);
  var content=new Size(ClientSize.Width,ClientSize.Height-BarHeight);var least=MinimumSize;var most=MaximumSize;   // 창이 만들어진 뒤의 본문 영역은 내용 + 띠다
  MinimumSize=Size.Empty;MaximumSize=Size.Empty;
  foreach(Control child in Controls)child.Scale(factor);
  Padding=new Padding(Theme.S(Padding.Left),Theme.S(Padding.Top),Theme.S(Padding.Right),Theme.S(Padding.Bottom));
  Theme.ScaleFonts(this,true);
  if(!AutoSize)ClientSize=new Size(Theme.S(content.Width),Theme.S(content.Height));
  if(!least.IsEmpty)MinimumSize=new Size(Theme.S(least.Width),Theme.S(least.Height));
  if(!most.IsEmpty)MaximumSize=new Size(Theme.S(most.Width),Theme.S(most.Height));
 }
 /// <summary>지금의 바깥 크기를 100% 때의 값으로 바꾼다. 크기를 기억하는 창이 저장할 때 쓴다(다시 뜰 때 그 값이 단계만큼 커진다. 테두리와 띠는 커지지 않으므로 빼고 나눈다)</summary>
 protected Size Unscaled(Size outer)
 {
  if(Unit==1f)return outer;int wide=Width-ClientSize.Width,tall=Height-ClientSize.Height+BarHeight;
  return new Size((int)Math.Round((outer.Width-wide)/Unit)+wide,(int)Math.Round((outer.Height-tall)/Unit)+tall);
 }
 /// <summary>100% 때의 길이로 바꾼다(기억하는 칸 너비 등)</summary>
 protected int Unscaled(int length)=>Unit==1f?length:(int)Math.Round(length/Unit);
 // 크기를 바꿀 수 있는 창(기록 목록 등)은 기억해 둔 크기가 단계만큼 커진 뒤에 화면보다 클 수 있다. 뜰 자리의 작업 영역 안으로 크기와 자리를 맞춘다(아래의 단추 줄이 화면 밖에 놓이지 않게)
 void FitScreen()
 {
  if(FormBorderStyle!=FormBorderStyle.Sizable||WindowState!=FormWindowState.Normal)return;
  var area=Screen.FromRectangle(Bounds).WorkingArea;
  var size=new Size(Math.Min(Width,area.Width),Math.Min(Height,area.Height));
  var at=new Point(Math.Clamp(Left,area.Left,Math.Max(area.Left,area.Right-size.Width)),Math.Clamp(Top,area.Top,Math.Max(area.Top,area.Bottom-size.Height)));
  if(size!=Size||at!=Location)Bounds=new Rectangle(at,size);
 }
 static int ColorRef(Color c)=>c.R|c.G<<8|c.B<<16;
 /// <summary>창 버튼 뒤의 색과 테두리를 지금의 테마에 맞춘다(Windows 11. 그 앞의 Windows 에서는 아무 일도 일어나지 않는다)</summary>
 public void ApplyFrame()
 {
  if(!IsHandleCreated)return;
  int dark=Theme.Dark?1:0;DwmSetWindowAttribute(Handle,20,ref dark,4);
  int caption=ColorRef(Theme.Window);DwmSetWindowAttribute(Handle,35,ref caption,4);
  int ink=ColorRef(Theme.Text);DwmSetWindowAttribute(Handle,36,ref ink,4);
  int border=ColorRef(Theme.Line);DwmSetWindowAttribute(Handle,34,ref border,4);
  var margins=new Margins{Top=BarHeight};DwmExtendFrameIntoClientArea(Handle,ref margins);
  Invalidate(new Rectangle(0,0,ClientSize.Width,BarHeight));
 }
 protected override void OnHandleCreated(EventArgs e)
 {
  base.OnHandleCreated(e);
  if(!opened)Cloak(true);   // 처음 뜨기 전: 다 그릴 때까지 가려 둔다(OnShown 의 Reveal 이 푼다)
  if(!painterReady){BufferedPaintInit();painterReady=true;}
  ApplyFrame();
  SetWindowPos(Handle,IntPtr.Zero,0,0,0,0,0x0001|0x0002|0x0004|0x0010|0x0020);   // 틀이 바뀌었음을 알린다(크기와 자리는 그대로)
 }
 protected override void OnTextChanged(EventArgs e){base.OnTextChanged(e);if(IsHandleCreated)Invalidate(new Rectangle(0,0,ClientSize.Width,BarHeight));}
 protected override void OnResize(EventArgs e){base.OnResize(e);if(IsHandleCreated)Invalidate(new Rectangle(0,0,ClientSize.Width,BarHeight));}
 protected override void WndProc(ref Message m)
 {
  if(m.Msg!=WM_NCCALCSIZE&&DwmDefWindowProc(m.HWnd,m.Msg,m.WParam,m.LParam,out IntPtr handled)){m.Result=handled;return;}   // 창 버튼 위의 마우스는 Windows 가 맡는다
  switch(m.Msg)
  {
   case WM_NCCALCSIZE when m.WParam!=IntPtr.Zero:
    var before=Marshal.PtrToStructure<SizeParams>(m.LParam);
    base.WndProc(ref m);
    var after=Marshal.PtrToStructure<SizeParams>(m.LParam);
    // 위쪽의 제목 줄만 걷어 낸다(왼쪽·오른쪽·아래의 틀은 Windows 의 것 그대로). 최대화한 창은 화면 밖으로 틀 두께만큼 나가 있으므로 그만큼 내린다
    after.First.Top=before.First.Top+(IsZoomed(m.HWnd)?Grip:0);
    Marshal.StructureToPtr(after,m.LParam,false);m.Result=IntPtr.Zero;
    return;
   case WM_NCHITTEST:
    base.WndProc(ref m);
    if((int)m.Result==1)   // 본문이라고 답한 자리 가운데 띠는 제목 줄로 답한다(끌기, 두 번 눌러 최대화, 오른쪽 단추의 창 메뉴를 Windows 가 맡는다)
    {
     var p=PointToClient(new Point(unchecked((short)(long)m.LParam),unchecked((short)((long)m.LParam>>16))));
     if(FormBorderStyle==FormBorderStyle.Sizable&&WindowState==FormWindowState.Normal&&p.Y<Grip)m.Result=(IntPtr)12;   // 위쪽 가장자리(크기 조절)
     else if(p.Y<BarHeight)m.Result=(IntPtr)2;
    }
    return;
   case WM_PRINT:
    printing=true;try{base.WndProc(ref m);}finally{printing=false;}   // 창을 그림으로 옮길 때(개발용 검사)는 창 버튼의 자리도 띠의 색으로 칠한다
    return;
  }
  base.WndProc(ref m);
  if(m.Msg==WM_NCACTIVATE){active=m.WParam!=IntPtr.Zero;Invalidate(new Rectangle(0,0,ClientSize.Width,BarHeight));}
  if(m.Msg==WM_DWMCOMPOSITIONCHANGED)ApplyFrame();
 }
 protected override void OnPaintBackground(PaintEventArgs e)
 {
  int bar=BarHeight;using var back=new SolidBrush(BackColor);e.Graphics.FillRectangle(back,0,bar,ClientSize.Width,Math.Max(0,ClientSize.Height-bar));
 }
 protected override void OnPaint(PaintEventArgs e)
 {
  base.OnPaint(e);
  int height=BarHeight,width=ClientSize.Width;if(width<=0)return;
  // 창 버튼의 자리는 비워 두어야(투명) Windows 가 그린 버튼이 보인다. 나머지는 불투명하게 칠한다(그냥 그리면 Windows 가 이 띠를 반투명으로 본다)
  int edge=width;
  if(!printing&&DwmGetWindowAttribute(Handle,5,out var buttons,Marshal.SizeOf<Rect>())==0&&buttons.Right>buttons.Left)edge=Math.Clamp(buttons.Left-(Width-width)/2,0,width);
  var all=new Rect{Right=width,Bottom=height};var mine=new Rect{Right=edge,Bottom=height};
  IntPtr target=e.Graphics.GetHdc();
  try
  {
   IntPtr paint=BeginBufferedPaint(target,ref all,2,IntPtr.Zero,out IntPtr dc);   // 2 = 위에서 아래로 놓인 32비트 그림
   if(paint==IntPtr.Zero)return;
   BufferedPaintClear(paint,ref all);
   using(var g=Graphics.FromHdc(dc))
   {
    using var back=new SolidBrush(Theme.Window);g.FillRectangle(back,0,0,edge,height);
    // 띠의 높이와 창 버튼은 Windows 가 제 배율로 그린다. 그 안의 제목과 아이콘도 앱의 단계가 아니라 Windows 배율로 그려야 띠에 맞는다
    // (앱만 200% 로 키우면 30px 띠에 30px 글자가 들어가지 않는다)
    float bar=Dpi/96f;int icon=(int)Math.Round(16*bar),left=(int)Math.Round(12*bar);
    g.DrawIcon(icon==16?SmallIcon.Value:ApplicationIcon.Value,new Rectangle(left,(height-icon)/2,icon,icon));
    g.TextRenderingHint=TextRenderingHint.AntiAlias;
    using var font=new Font(AppFonts.Head,15f*bar,FontStyle.Bold,GraphicsUnit.Pixel);using var ink=new SolidBrush(active?Theme.Text:Theme.Dim);
    using var format=new StringFormat(StringFormat.GenericTypographic){Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap};
    float line=font.GetHeight(g);
    int text=left+icon+(int)Math.Round(8*bar);
    g.DrawString(Text,font,ink,new RectangleF(text,(height-line)/2f,Math.Max(0,edge-text-(int)Math.Round(8*bar)),line+1),format);
   }
   BufferedPaintSetAlpha(paint,ref mine,255);
   EndBufferedPaint(paint,true);
  }
  finally{e.Graphics.ReleaseHdc(target);}
 }
}
