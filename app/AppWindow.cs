namespace LadderRecords;

class AppWindow:Form
{
 static readonly Lazy<Icon> ApplicationIcon=new(()=>new Icon(typeof(AppWindow),"AppIcon.ico"));

 // 창이 뜨는 동안에는 컨트롤의 핸들이 하나 생길 때마다 창 전체의 레이아웃을 다시 계산한다(주 화면에서 50번 남짓, 한 번에 30ms. 잰 값은 docs\기록\최적화.md).
 // 처음 보일 때부터 Shown 까지 레이아웃 계산을 멈춰 두고, 실제 크기가 필요한 Load 직전에 한 번, Shown 에서 한 번만 계산한다.
 List<Control>? opening;bool opened;
 void Settle(){if(opening is not {} held)return;opening=null;Theme.Release(held);}
 protected override void SetVisibleCore(bool value){if(value&&!opened&&opening==null&&!IsDisposed)opening=Theme.Hold(this);base.SetVisibleCore(value);}
 protected override void OnLoad(EventArgs e)
 {
  Settle();                     // Load 핸들러와 창을 가운데 두는 일은 레이아웃 계산이 끝난 크기를 본다
  base.OnLoad(e);Theme.Apply(this);   // 뜰 때 디자인을 입힌다
  if(!opened)opening=Theme.Hold(this);
 }
 protected override void OnFormClosed(FormClosedEventArgs e){Settle();base.OnFormClosed(e);}
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
 // 작업 표시줄은 창이 보이는 순간 창에 아이콘을 요청하는데, 응답이 늦으면 Windows 기본 아이콘으로 정하고 다시 요청하지 않는다.
 // 주 화면은 단축키 때문에 창 핸들을 먼저 만들어서, 컨트롤을 만들고 디자인을 입히는 일(몇 초)이 창이 보인 뒤에 돈다. 그동안은 그 요청에 응답하지 못한다.
 // 그 일이 끝난 뒤(Shown)에 아이콘을 한 번 더 알려 작업 표시줄이 다시 읽게 한다.
 // 같은 아이콘을 그대로 다시 넣으면 Windows 가 바뀐 것으로 치지 않으므로, 한 번 비웠다가 다시 넣는다(큰 것 1, 작은 것 0).
 protected override void OnShown(EventArgs e)
 {
  Settle();opened=true;
  base.OnShown(e);
  foreach(int kind in new[]{1,0}){IntPtr icon=SendMessage(Handle,0x007F,(IntPtr)kind,IntPtr.Zero);if(icon==IntPtr.Zero)continue;SendMessage(Handle,0x0080,(IntPtr)kind,IntPtr.Zero);SendMessage(Handle,0x0080,(IntPtr)kind,icon);}
 }
 public AppWindow()
 {
  Icon=ApplicationIcon.Value;
 }
}
