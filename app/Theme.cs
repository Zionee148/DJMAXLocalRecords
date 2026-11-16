using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
namespace LadderRecords;

// 프로그램 창의 모양 (사용자 결정, 2026-11-15: "밝은 정보형"을 바탕으로 라이트·다크 두 모드).
// 색은 여기 한 곳에서 정하고, 창이 뜰 때 AppWindow 가 Apply 를 불러 부품마다 입힌다.
// 새 단추나 칸을 더해도 따로 색을 줄 필요가 없다. 달리 보여야 하는 것만 Tag 로 표시한다:
//   단추 "primary"(채운 강조) · "default"(강조 테두리) · "danger"(빨간 테두리) · "swatch"(제 색을 그대로 둠)
//   글 "dim"(옅게) · "accent" · "good" · "bad" · "warn",  판 "line" · "border" · "field"
// OBS 오버레이의 디자인은 여기와 상관없다(BadgeDesign, OverlayLayout).
static class Theme
{
 public static bool Dark;
 // 글꼴은 Windows 배율을 따라가지 않게 만든다(사용자 결정 2026-11-16, docs\기록\지원범위-시험.md).
 // 포인트로 만든 글꼴은 배율만큼 커지는데(125% 에서 1.25배) 창의 치수는 픽셀로 정해 둔 곳이 많아, 배율이 100% 가 아니면 글이 잘리고 겹쳤다.
 // 그래서 "포인트 크기를 100%(96 DPI) 때의 픽셀로 바꾼 값"으로 픽셀 단위 글꼴을 만든다. 어느 배율에서나 100% 때와 같은 배치가 된다.
 // 대가: 배율을 올려 쓰는 화면에서는 이 프로그램의 글자가 다른 프로그램보다 작게 보인다. 제목 줄과 스크롤 막대처럼 Windows 가 그리는 부분은 배율을 따라간다.
 public static Font Px(string family,float points,FontStyle style=FontStyle.Regular)=>new Font(family,points*96f/72f,style,GraphicsUnit.Pixel);
 // 글꼴을 따로 정하지 않은 부품이 쓰는 기본 글꼴도 같은 식으로 정한다. 창을 하나도 만들기 전에 한 번 부른다
 public static void FixDefaultFont(){var basic=SystemFonts.MessageBoxFont??Control.DefaultFont;Application.SetDefaultFont(Px(basic.FontFamily.Name,basic.SizeInPoints,basic.Style));}
 static Color C(int light,int dark)=>Color.FromArgb(unchecked((int)0xFF000000)|(Dark?dark:light));
 public static Color Window=>C(0xFFFFFF,0x14161A);
 public static Color Side=>C(0xF4F6F8,0x1B1E24);
 public static Color Line=>C(0xE1E5EA,0x2E333D);
 public static Color Text=>C(0x1C1E22,0xE8EAEE);
 public static Color Dim=>C(0x5A616B,0xA3AAB5);
 public static Color Field=>C(0xFFFFFF,0x101216);
 public static Color Border=>C(0xC3C9D2,0x454B57);
 public static Color ButtonBack=>C(0xFFFFFF,0x262A32);
 public static Color Accent=>C(0x2563EB,0x4C8DFF);
 public static Color OnAccent=>C(0xFFFFFF,0x0B1820);
 public static Color Good=>C(0x469A0C,0xB3FF68);
 public static Color Bad=>C(0xD92D3A,0xFF707B);
 public static Color Warn=>C(0xB04000,0xFFB070);
 public static Color Select=>Mix(Accent,Window,Dark?.76:.86);
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
 // 스크롤 막대는 Windows 가 그린다. 어두운 것으로 바꿔 달라고 창마다 알린다
 static void Scrollbars(IntPtr window)=>SetWindowTheme(window,Dark?"DarkMode_Explorer":null,null);
 public static void ApplyAll(){foreach(var form in Application.OpenForms.OfType<AppWindow>().ToArray())Apply(form);}
 public static void Apply(Form form)
 {
  // 단추의 모양·최소 크기·굵은 글꼴을 바꿀 때마다 창 전체의 배치를 다시 잡으면 주 화면 하나에 2.5초가 든다(잰 값. docs\기록\최적화.md).
  // 다 바꿀 때까지 배치를 멈췄다가 끝에 한 번만 잡는다.
  var held=Hold(form);
  try
  {
   form.BackColor=Window;form.ForeColor=Text;
   if(form.AcceptButton is Button accept&&accept.Tag==null)accept.Tag="primary";
   int dark=Dark?1:0;DwmSetWindowAttribute(form.Handle,20,ref dark,4);   // 제목 줄. 지원하지 않는 Windows 에서는 아무 일도 일어나지 않는다
   if(form.AutoScroll)Scrollbars(form.Handle);
   Walk(form);
  }
  finally{Release(held);}
  form.Invalidate(true);
 }
 // 이 부품과 그 안의 모든 부품의 배치를 멈춘다. Release 는 안쪽부터 풀면서 그동안 밀린 배치를 한 번씩만 잡는다(안쪽의 크기가 정해진 뒤에 바깥이 잡힌다).
 internal static List<Control> Hold(Control top){var all=new List<Control>();void Visit(Control c){all.Add(c);c.SuspendLayout();foreach(Control child in c.Controls)Visit(child);}Visit(top);return all;}
 internal static void Release(List<Control> held){for(int i=held.Count-1;i>=0;i--)if(!held[i].IsDisposed)held[i].ResumeLayout(true);}

 // Windows 가 그리는 부품 가운데 색을 정할 수 없는 것은 그려진 뒤에 덧그린다(체크 상자의 네모, 숫자 칸의 위아래 단추). 부품마다 한 번만 건다
 static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control,object> hooked=new();
 static void Hook(Control control,PaintEventHandler paint){if(hooked.TryGetValue(control,out _))return;hooked.Add(control,paint);control.Paint+=paint;}
 static void PaintCheck(object? sender,PaintEventArgs e)
 {
  var box=(CheckBox)sender!;if(box.Appearance!=Appearance.Normal)return;
  var g=e.Graphics;var glyph=CheckBoxRenderer.GetGlyphSize(g,System.Windows.Forms.VisualStyles.CheckBoxState.UncheckedNormal);
  // Windows 가 잡아 둔 네모 자리(배율만큼 커진다)를 지우고, 그 안에 100% 때의 크기(13)로 그린다. 글자 쪽에 붙여 그려서 네모와 글자의 사이는 100% 때와 같다
  var area=new Rectangle(box.Padding.Left,(box.Height-glyph.Height)/2,glyph.Width,glyph.Height);
  using(var around=new SolidBrush(box.BackColor))g.FillRectangle(around,Rectangle.Inflate(area,1,1));
  int size=Math.Min(glyph.Width,13);var r=new Rectangle(area.Right-size,(box.Height-size)/2,size,size);
  bool on=box.CheckState!=CheckState.Unchecked;var edge=!box.Enabled?Line:on?Accent:Border;
  using(var fill=new SolidBrush(on?edge:Field))g.FillRectangle(fill,r);
  using(var pen=new Pen(edge))g.DrawRectangle(pen,r.X,r.Y,r.Width-1,r.Height-1);
  if(!on)return;
  g.SmoothingMode=SmoothingMode.AntiAlias;using var tick=new Pen(OnAccent,1.7f);
  g.DrawLines(tick,new[]{new PointF(r.X+3f,r.Y+r.Height/2f),new PointF(r.X+r.Width/2f-1f,r.Bottom-4f),new PointF(r.Right-3f,r.Y+3.5f)});
 }
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
   if(c is ScrollableControl{AutoScroll:true} scrolling&&scrolling.IsHandleCreated)Scrollbars(scrolling.Handle);
   switch(c)
   {
    case Button b:
     string kind=b.Tag as string??"";bool primary=kind=="primary";
     b.FlatStyle=FlatStyle.Flat;b.FlatAppearance.BorderSize=1;b.Cursor=Cursors.Hand;
     if(kind=="swatch"){b.FlatAppearance.BorderColor=Border;break;}
     b.FlatAppearance.BorderColor=primary||kind=="default"?Accent:kind=="danger"?Bad:Border;
     b.BackColor=primary?Accent:ButtonBack;b.ForeColor=primary?OnAccent:kind=="default"?Accent:kind=="danger"?Bad:Text;b.MinimumSize=new(primary?84:0,30);
     if((primary||kind is "default" or "danger")&&!b.Font.Bold)b.Font=new Font(b.Font,FontStyle.Bold);
     break;
    case NumericUpDown number:
     number.BackColor=Field;number.ForeColor=Text;number.BorderStyle=BorderStyle.FixedSingle;Frame.Watch(number);
     if(number.Controls.Count>0)Hook(number.Controls[0],PaintSpin);   // 첫 자식이 위아래 단추다
     break;
    case TrackBar bar:bar.BackColor=bar.Parent?.BackColor??Window;break;
    case ListView list:list.BackColor=Field;list.ForeColor=Text;if(list.IsHandleCreated)Scrollbars(list.Handle);break;
    case InfoBox box:box.BackColor=Side;break;
    case TextBox t:
     t.BackColor=Field;t.ForeColor=Text;
     if(t.BorderStyle!=BorderStyle.None&&t.Parent is not UpDownBase){t.BorderStyle=BorderStyle.FixedSingle;Frame.Watch(t);}
     break;
    case ComboBox combo:combo.BackColor=Field;combo.ForeColor=Text;ComboList(combo);break;
    case CheckBox check:check.ForeColor=Text;Hook(check,PaintCheck);break;
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

// 버튼·난이도·판정 꼬리표
sealed class Chip:Control
{
 public bool Outline;   // 테두리만 있는 꼬리표("고정", "후보")
 string? shown;
 public Chip()
 {
  SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
  Font=Theme.Px("맑은 고딕",9.5f,FontStyle.Bold);Margin=new(0,0,6,0);Height=22;Visible=false;TabStop=false;
 }
 public void Set(string text)
 {
  if(text==shown)return;shown=text;   // 같은 글이면 다시 재거나 그리지 않는다(주 화면이 상태를 고칠 때마다 불린다)
  Text=text;Visible=text.Length>0;
  Width=TextRenderer.MeasureText(text,Font,Size.Empty,TextFormatFlags.NoPadding).Width+20;Invalidate();
 }
 protected override void OnPaint(PaintEventArgs e)
 {
  var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Parent?.BackColor??Theme.Window);
  using var path=BadgeDesign.Rounded(new RectangleF(0,0,Width-1,Height-1),4);
  const TextFormatFlags center=TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix;
  if(Outline)
  {
   using var fill=new SolidBrush(Theme.Field);using var pen=new Pen(Theme.Accent);g.FillPath(fill,path);g.DrawPath(pen,path);
   TextRenderer.DrawText(g,Text,Font,ClientRectangle,Theme.Accent,center);
  }
  else
  {
   using var fill=new SolidBrush(Theme.BadgeBack(Text));g.FillPath(fill,path);
   TextRenderer.DrawText(g,Text,Font,ClientRectangle,BadgeDesign.Foreground(Text),center);
  }
 }
}

// 묶음 제목: 작은 굵은 글씨, 오른쪽으로 가는 줄
sealed class SectionHeader:Control
{
 public bool Rule=true,Quiet;
 public SectionHeader(string title)
 {
  SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
  Text=title;Height=22;Font=Theme.Px("맑은 고딕",9.5f,FontStyle.Bold);TabStop=false;
 }
 public int TextWidth=>TextRenderer.MeasureText(Text,Font,Size.Empty,TextFormatFlags.NoPadding).Width;
 protected override void OnPaint(PaintEventArgs e)
 {
  var g=e.Graphics;g.Clear(Parent?.BackColor??Theme.Window);
  var size=TextRenderer.MeasureText(Text,Font,Size.Empty,TextFormatFlags.NoPadding);
  TextRenderer.DrawText(g,Text,Font,new Point(0,(Height-size.Height)/2),Quiet?Theme.Dim:Theme.Accent,TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix);
  if(Rule){using var pen=new Pen(Theme.Line);g.DrawLine(pen,size.Width+10,Height/2,Width,Height/2);}
 }
}

// 라이트 / 다크 전환 단추
sealed class ModeSwitch:Control
{
 public event Action? Toggled;
 public ModeSwitch()
 {
  SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
  Size=new(112,24);Font=Theme.Px("맑은 고딕",9f);Cursor=Cursors.Hand;AccessibleName="밝기 모드";AccessibleRole=AccessibleRole.PushButton;
 }
 protected override void OnPaint(PaintEventArgs e)
 {
  var g=e.Graphics;int half=Width/2;
  const TextFormatFlags center=TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding;
  using var bold=new Font(Font,FontStyle.Bold);
  void Part(Rectangle r,string text,bool active)
  {
   using var fill=new SolidBrush(active?Theme.Accent:Theme.ButtonBack);g.FillRectangle(fill,r);
   TextRenderer.DrawText(g,text,active?bold:Font,r,active?Theme.OnAccent:Theme.Text,center);
  }
  Part(new Rectangle(0,0,half,Height),"라이트",!Theme.Dark);Part(new Rectangle(half,0,Width-half,Height),"다크",Theme.Dark);
  using var pen=new Pen(Theme.Border);g.DrawRectangle(pen,0,0,Width-1,Height-1);
 }
 protected override void OnMouseClick(MouseEventArgs e)
 {
  bool wantDark=e.X>=Width/2;if(wantDark==Theme.Dark)return;
  Theme.Dark=wantDark;Toggled?.Invoke();
 }
}

// 주 화면의 오른쪽 칸: 옅은 바탕, 왼쪽에 가는 줄
sealed class SidePanel:FlowLayoutPanel
{
 public SidePanel(){SetStyle(ControlStyles.ResizeRedraw,true);}
 protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);using var pen=new Pen(Theme.Line);e.Graphics.DrawLine(pen,0,0,0,Height);}
}

// 목록 상자: 테두리와 화살표를 디자인 색으로 덧그린다(Windows 가 그리는 것은 다크에서 흰 테두리가 남는다)
sealed class ThemedCombo:ComboBox
{
 public ThemedCombo(){FlatStyle=FlatStyle.Flat;}
 protected override void WndProc(ref Message m)
 {
  base.WndProc(ref m);
  if(m.Msg!=0x000F||DropDownStyle!=ComboBoxStyle.DropDownList)return;
  using var g=Graphics.FromHwnd(Handle);var r=ClientRectangle;
  using var back=new SolidBrush(Theme.Field);g.FillRectangle(back,r);
  using var pen=new Pen(Theme.Border);g.DrawRectangle(pen,0,0,r.Width-1,r.Height-1);
  TextRenderer.DrawText(g,Text,Font,new Rectangle(6,0,r.Width-28,r.Height),Enabled?Theme.Text:Theme.Dim,TextFormatFlags.VerticalCenter|TextFormatFlags.Left|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
  g.SmoothingMode=SmoothingMode.AntiAlias;using var arrow=new Pen(Theme.Dim,1.5f);int x=r.Width-14,y=r.Height/2;
  g.DrawLines(arrow,new[]{new Point(x-4,y-2),new Point(x,y+2),new Point(x+4,y-2)});
 }
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
   try{Theme.Dark=UserOptions.Load(Path.Combine(AppContext.BaseDirectory,"preferences.json")).Dark;}catch{}
   using var d=new NoticeDialog(title,text){StartPosition=FormStartPosition.CenterScreen,ShowInTaskbar=true};d.ShowDialog();
  }catch{MessageBox.Show(text,title);}
 }
}
