using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace LadderRecords;

sealed class HelpBadge:Control
{
 bool pressable;
 /// <summary>
 /// 눌러서 설명을 펴고 접는 물음표로 쓴다: Tab 으로 올 수 있고, Space·Enter 가 누른 것과 같고, 초점이 있으면 테두리가 강조색이다.
 /// 부르지 않으면 마우스를 올려 읽는 정적인 표시다(다른 창의 물음표는 그대로다)
 /// </summary>
 public void MakePressable(){pressable=true;SetStyle(ControlStyles.Selectable,true);TabStop=true;AccessibleRole=AccessibleRole.PushButton;Cursor=Cursors.Hand;}
 /// <summary>키를 누른 것과 같다(개발용 검사)</summary>
 internal void Press(Keys key)=>OnKeyDown(new KeyEventArgs(key));
 protected override void OnKeyDown(KeyEventArgs e){base.OnKeyDown(e);if(pressable&&e.KeyCode is Keys.Space or Keys.Enter){OnClick(EventArgs.Empty);e.Handled=true;}}
 protected override bool IsInputKey(Keys keyData)=>pressable&&keyData==Keys.Enter||base.IsInputKey(keyData);
 protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(pressable)Focus();}
 protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);Invalidate();}
 protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);Invalidate();}
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
  using var border=new Pen(pressable&&Focused?Theme.Accent:ForeColor,pressable&&Focused?1.6f:1f);
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

// 오버레이 팝업을 여는 브라우저. Windows 기본 웹 브라우저가 Chrome 이면 Chrome, 그 밖에는(Edge 포함) Edge 로 연다.
// Chrome 이 기본인데 Chrome 을 찾지 못하면 Edge 로 대신 연다.
enum PopupBrowser{Chrome,Edge}

static class OverlayPopup
{
 const string Title="디맥 오버레이 팝업";
 const uint SizeOnly=0x0002|0x0004|0x0010;
 const string Associations=@"HKEY_CURRENT_USER\Software\Microsoft\Windows\Shell\Associations\UrlAssociations\";

 /// <summary>기본 웹 브라우저의 등록 이름(ProgId)으로 팝업을 열 브라우저를 고른다. 정식판 Chrome 만 Chrome 이다:
 /// 모든 사용자용 설치는 "ChromeHTML", 사용자별 설치는 뒤에 그 설치의 접미사가 붙은 "ChromeHTML.&lt;접미사&gt;" 로 등록한다.
 /// Edge(MSEdgeHTM)와 그 밖의 브라우저, Chrome 의 베타·개발·카나리판(ChromeBHTML, ChromeDHTML, ChromeSSHTM), 읽지 못한 경우는 모두 Edge 다.</summary>
 internal static PopupBrowser Choose(string? progId)
 {
  string name=progId?.Trim()??"";
  return name.Equals("ChromeHTML",StringComparison.OrdinalIgnoreCase)||name.StartsWith("ChromeHTML.",StringComparison.OrdinalIgnoreCase)?PopupBrowser.Chrome:PopupBrowser.Edge;
 }

 /// <summary>Windows 기본 웹 브라우저의 ProgId 를 읽는다. 새 Windows 는 UserChoiceLatest\ProgId 에, 그 전은 UserChoice 에 적는다.
 /// https 를 먼저 보고 http 를 본다. 어디서도 읽지 못하면 null.</summary>
 internal static string? DefaultProgId(Func<string,string?> read)
 {
  foreach(string scheme in new[]{"https","http"})
  foreach(string key in new[]{$@"{scheme}\UserChoiceLatest\ProgId",$@"{scheme}\UserChoice"}){
   string? value=read(key);
   if(!string.IsNullOrWhiteSpace(value))return value;
  }
  return null;
 }
 internal static string? ReadAssociation(string key)
 {
  try{return Microsoft.Win32.Registry.GetValue(Associations+key,"ProgId",null) as string;}
  catch(Exception e) when(e is System.Security.SecurityException or IOException or UnauthorizedAccessException){return null;}
 }

 /// <summary>브라우저의 실행 파일을 찾는다: 정해진 설치 자리(전체 사용자, 32비트, 사용자별)를 먼저 보고, 없으면 Windows 에 등록된 경로(App Paths)를 본다.
 /// 찾지 못하면 null.</summary>
 internal static string? Executable(PopupBrowser browser,Func<string,bool> exists,Func<string,string?> appPath)
 {
  string Under(Environment.SpecialFolder folder,params string[] parts)=>Path.Combine(new[]{Environment.GetFolderPath(folder)}.Concat(parts).ToArray());
  var fixedPlaces=browser==PopupBrowser.Chrome
   ?new[]{Under(Environment.SpecialFolder.ProgramFiles,"Google","Chrome","Application","chrome.exe"),
          Under(Environment.SpecialFolder.ProgramFilesX86,"Google","Chrome","Application","chrome.exe"),
          Under(Environment.SpecialFolder.LocalApplicationData,"Google","Chrome","Application","chrome.exe")}
   :new[]{Under(Environment.SpecialFolder.ProgramFilesX86,"Microsoft","Edge","Application","msedge.exe"),
          Under(Environment.SpecialFolder.ProgramFiles,"Microsoft","Edge","Application","msedge.exe")};
  foreach(string place in fixedPlaces)if(exists(place))return place;
  string? registered=appPath(browser==PopupBrowser.Chrome?"chrome.exe":"msedge.exe")?.Trim().Trim('"');
  return !string.IsNullOrEmpty(registered)&&exists(registered)?registered:null;
 }
 internal static string? ReadAppPath(string exe)
 {
  foreach(string root in new[]{"HKEY_CURRENT_USER","HKEY_LOCAL_MACHINE"}){
   try{if(Microsoft.Win32.Registry.GetValue($@"{root}\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{exe}","",null) is string path&&path.Length>0)return path;}
   catch(Exception e) when(e is System.Security.SecurityException or IOException or UnauthorizedAccessException){}
  }
  return null;
 }

 /// <summary>팝업을 실제로 열 브라우저와 그 실행 파일. Chrome 을 골랐는데 Chrome 을 찾지 못하면 Edge 로 대신 연다.
 /// Edge 도 찾지 못하면 실행 파일은 null 이다(브라우저는 Edge).</summary>
 internal static (PopupBrowser Browser,string? Program) Resolve(PopupBrowser chosen,Func<string,bool> exists,Func<string,string?> appPath)
 {
  string? program=Executable(chosen,exists,appPath);
  if(program==null&&chosen==PopupBrowser.Chrome)return (PopupBrowser.Edge,Executable(PopupBrowser.Edge,exists,appPath));
  return (chosen,program);
 }

 // 사용자에게 보이는 안내(메인 창의 상태 줄). Chrome 을 찾지 못하면 Edge 로 대신 열므로, 찾지 못했다는 안내는 Edge 의 것만 있다.
 internal const string NotFound="Edge를 찾을 수 없습니다. Edge 설치 상태를 확인한 뒤 다시 시도하세요.";
 // 실행을 시작하지 못했을 때는 그 브라우저의 이름으로 알린다(조사도 이름에 맞춘다).
 internal static string CannotStart(PopupBrowser browser)=>browser==PopupBrowser.Chrome
  ?"Chrome에서 오버레이 팝업을 열 수 없습니다. Chrome이 정상적으로 실행되는지 확인하세요."
  :"Edge에서 오버레이 팝업을 열 수 없습니다. Edge가 정상적으로 실행되는지 확인하세요.";
 internal static string ProcessName(PopupBrowser browser)=>browser==PopupBrowser.Chrome?"chrome":"msedge";
 delegate bool WindowCallback(IntPtr window,IntPtr data);
 [DllImport("user32.dll")] static extern bool EnumWindows(WindowCallback callback,IntPtr data);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr window,StringBuilder text,int length);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr window,StringBuilder text,int length);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
 [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window,out Bounds bounds);
 [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
 [StructLayout(LayoutKind.Sequential)] struct Bounds{public int Left,Top,Right,Bottom;}

 // 팝업 제목으로 시작하고 Chromium 창 종류이며 그 브라우저의 프로세스가 연 창들.
 static List<IntPtr> Windows(PopupBrowser browser)
 {
  string name=ProcessName(browser);
  var result=new List<IntPtr>();
  var buffer=new StringBuilder(256);
  EnumWindows((window,_)=>{
   if(GetWindowText(window,buffer,buffer.Capacity)==0||!buffer.ToString().StartsWith(Title,StringComparison.Ordinal))return true;
   if(GetClassName(window,buffer,buffer.Capacity)==0||buffer.ToString()!="Chrome_WidgetWin_1")return true;
   GetWindowThreadProcessId(window,out var id);
   try{using var process=Process.GetProcessById((int)id);if(process.ProcessName.Equals(name,StringComparison.OrdinalIgnoreCase))result.Add(window);}
   catch(ArgumentException){}
   return true;
  },IntPtr.Zero);
  return result;
 }

 /// <summary>메인 창의 '오버레이 팝업'. 고른 브라우저를 주소창·탭 없는 앱 창(--app)으로 열고, 페이지가 알려 준 크기에 맞춰
 /// 방금 연 창 하나만 크기를 조정한다. Chrome 을 찾지 못하면 Edge 로 대신 연다. Edge 도 찾지 못하거나 브라우저가 실행을 시작하지 못하면
 /// 안내 글로 InvalidOperationException 을 낸다.
 /// 창은 열렸는데 크기를 알려 오지 않으면(7초) 처음 넘긴 크기 그대로 두고 오류로 치지 않는다.
 /// 크기를 조정한 창을 돌려준다(못 찾았으면 IntPtr.Zero). browser 를 주지 않으면 기본 웹 브라우저로 고른다(개발용 검사는 정해서 준다).</summary>
 public static async Task<IntPtr> Show(OverlayServer server,JsonElement appearance,bool showCurrent,PopupBrowser? browser=null)
 {
  if(server.PopupFit!=null)return IntPtr.Zero;
  var (chosen,found)=Resolve(browser??Choose(DefaultProgId(ReadAssociation)),File.Exists,ReadAppPath);
  string program=found??throw new InvalidOperationException(NotFound);

  var before=Windows(chosen);
  var fit=new PopupFit();
  server.PopupFit=fit;
  try{
   var start=new ProcessStartInfo(program){UseShellExecute=false};
   start.ArgumentList.Add($"--app=http://127.0.0.1:{server.Port}/popup/{fit.Id}");
   int width=OverlayText.Width(appearance,Array.Empty<string>());
   int height=OverlayLayout.Height(appearance,showCurrent);
   start.ArgumentList.Add($"--window-size={width+16},{height+48}");
   try{using var process=Process.Start(start);}
   catch(Exception e) when(e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException){throw new InvalidOperationException(CannotStart(chosen),e);}

   var geometry=await fit.Ready.Task.WaitAsync(TimeSpan.FromSeconds(7));
   for(int attempt=0;attempt<10;attempt++){
    var window=Windows(chosen).FirstOrDefault(item=>!before.Contains(item));
    if(window!=IntPtr.Zero&&GetWindowRect(window,out var bounds)){
     int outerWidth=FitAxis(bounds.Right-bounds.Left,geometry.OuterWidth,geometry.ExtraWidth,geometry.ContentWidth);
     int outerHeight=FitAxis(bounds.Bottom-bounds.Top,geometry.OuterHeight,geometry.ExtraHeight,geometry.ContentHeight);
     SetWindowPos(window,IntPtr.Zero,0,0,outerWidth,outerHeight,SizeOnly);
     return window;
    }
    await Task.Delay(50);
   }
  }catch(TimeoutException){
   // 팝업이 크기를 알려 오지 않았다. 브라우저는 시작할 때 넘긴 크기(--window-size)로 열려 있으므로 실패로 알리지 않는다.
  }finally{if(server.PopupFit==fit)server.PopupFit=null;}
  return IntPtr.Zero;
 }

 internal static int FitAxis(int nativeOuter,int browserOuter,int chrome,int content)
  =>(int)Math.Ceiling(nativeOuter*(chrome+content)/(double)browserOuter);
}
