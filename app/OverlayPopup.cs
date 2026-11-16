using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace LadderRecords;

sealed class HelpBadge:Control
{
 public HelpBadge()
 {
  Size=new Size(16,16);
  Margin=new Padding(2,6,3,0);
  TabStop=false;
  AccessibleRole=AccessibleRole.StaticText;
 }
 protected override void OnPaint(PaintEventArgs e)
 {
  base.OnPaint(e);
  e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
  using var border=new Pen(ForeColor);
  e.Graphics.DrawEllipse(border,1,1,Width-3,Height-3);
  using var font=new Font(Font.FontFamily,Math.Max(8,Height*.67f),FontStyle.Bold,GraphicsUnit.Pixel);
  TextRenderer.DrawText(e.Graphics,"?",font,ClientRectangle,ForeColor,
   TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
 }
}

record PopupGeometry(int OuterWidth,int OuterHeight,int ExtraWidth,int ExtraHeight,int ContentWidth,int ContentHeight);
sealed class PopupFit
{
 public string Id{get;}=Guid.NewGuid().ToString("N");
 public TaskCompletionSource<PopupGeometry> Ready{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
}

static class OverlayPopup
{
 const string Title="디맥 오버레이 팝업";
 const uint SizeOnly=0x0002|0x0004|0x0010;
 delegate bool WindowCallback(IntPtr window,IntPtr data);
 [DllImport("user32.dll")] static extern bool EnumWindows(WindowCallback callback,IntPtr data);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr window,StringBuilder text,int length);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr window,StringBuilder text,int length);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
 [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window,out Bounds bounds);
 [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
 [StructLayout(LayoutKind.Sequential)] struct Bounds{public int Left,Top,Right,Bottom;}

 static List<IntPtr> Windows()
 {
  var result=new List<IntPtr>();
  var buffer=new StringBuilder(256);
  EnumWindows((window,_)=>{
   if(GetWindowText(window,buffer,buffer.Capacity)==0||!buffer.ToString().StartsWith(Title,StringComparison.Ordinal))return true;
   if(GetClassName(window,buffer,buffer.Capacity)==0||buffer.ToString()!="Chrome_WidgetWin_1")return true;
   GetWindowThreadProcessId(window,out var id);
   try{using var process=Process.GetProcessById((int)id);if(process.ProcessName.Equals("msedge",StringComparison.OrdinalIgnoreCase))result.Add(window);}
   catch(ArgumentException){}
   return true;
  },IntPtr.Zero);
  return result;
 }

 public static async Task Show(OverlayServer server,JsonElement appearance,bool showCurrent)
 {
  if(server.PopupFit!=null)return;
  string edge=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Microsoft","Edge","Application","msedge.exe");
  if(!File.Exists(edge))edge=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Microsoft","Edge","Application","msedge.exe");
  if(!File.Exists(edge))throw new InvalidOperationException("오버레이 팝업을 열려면 Microsoft Edge가 필요합니다.");

  var before=Windows();
  var fit=new PopupFit();
  server.PopupFit=fit;
  try{
   var start=new ProcessStartInfo(edge){UseShellExecute=false};
   start.ArgumentList.Add($"--app=http://127.0.0.1:{server.Port}/popup/{fit.Id}");
   int width=OverlayText.Width(appearance,Array.Empty<string>());
   int height=OverlayLayout.Height(appearance,showCurrent);
   start.ArgumentList.Add($"--window-size={width+16},{height+48}");
   using var process=Process.Start(start);

   var geometry=await fit.Ready.Task.WaitAsync(TimeSpan.FromSeconds(7));
   for(int attempt=0;attempt<10;attempt++){
    var window=Windows().FirstOrDefault(item=>!before.Contains(item));
    if(window!=IntPtr.Zero&&GetWindowRect(window,out var bounds)){
     int outerWidth=FitAxis(bounds.Right-bounds.Left,geometry.OuterWidth,geometry.ExtraWidth,geometry.ContentWidth);
     int outerHeight=FitAxis(bounds.Bottom-bounds.Top,geometry.OuterHeight,geometry.ExtraHeight,geometry.ContentHeight);
     SetWindowPos(window,IntPtr.Zero,0,0,outerWidth,outerHeight,SizeOnly);
     return;
    }
    await Task.Delay(50);
   }
  }catch(TimeoutException){
   // Edge can still use the size passed on startup when it cannot report its viewport.
  }finally{if(server.PopupFit==fit)server.PopupFit=null;}
 }

 internal static int FitAxis(int nativeOuter,int browserOuter,int chrome,int content)
  =>(int)Math.Ceiling(nativeOuter*(chrome+content)/(double)browserOuter);
}
