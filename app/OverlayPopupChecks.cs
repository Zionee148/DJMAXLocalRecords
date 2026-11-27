using System.Reflection;

namespace LadderRecords;

[System.Runtime.Versioning.SupportedOSPlatform("windows10.0.19041")]
static class OverlayPopupChecks
{
 public static void Run(Catalog catalog)
 {
  using var form=new MainForm(catalog,18772,registerHotkeys:false);
  var flags=BindingFlags.Instance|BindingFlags.NonPublic;
  var server=(OverlayServer)typeof(MainForm).GetField("server",flags)!.GetValue(form)!;
  var tip=(ToolTip)typeof(MainForm).GetField("helpTip",flags)!.GetValue(form)!;
  // 주 화면의 도움말 표시(진단 그림 저장)를 그 체크 상자와 같은 줄에서 찾는다. '전체화면 사용'과 그 도움말은 주 화면에 없다.
  var images=(CheckBox)typeof(MainForm).GetField("scoreImagesOption",flags)!.GetValue(form)!;
  var badge=images.Parent!.Controls.OfType<HelpBadge>().Single();
  // 진단 그림 저장의 물음표는 풍선이 아니라 줄 아래에 펴지는 설명이다(두 문단). Tab 으로 갈 수 있고 Space·Enter 로 펴고 접는다. Esc 로도 닫힌다. 체크 상자의 값은 그대로다
  if(badge.Size!=new Size(16,16)||!badge.TabStop||badge.AccessibleDescription!=MainForm.ImagesHelpText||!string.IsNullOrEmpty(tip.GetToolTip(badge)))
   throw new InvalidOperationException("Missing compact diagnostic image help");
  {
   bool before=images.Checked;if(form.ImagesHelpShown)throw new InvalidOperationException("Diagnostic image help should start folded");
   badge.Press(Keys.Space);var shown=Descendants(form).Where(control=>control is Label or WordLabel).Select(control=>control.Text).ToList();
   if(!form.ImagesHelpShown||!MainForm.ImagesHelpText.Split('\n').All(shown.Contains)||MainForm.ImagesHelpText.Split('\n').Length!=2)throw new InvalidOperationException("Diagnostic image help did not open with its two paragraphs");
   badge.Press(Keys.Enter);if(form.ImagesHelpShown)throw new InvalidOperationException("Diagnostic image help did not fold");
   badge.Press(Keys.Space);var escape=typeof(MainForm).GetMethod("ProcessCmdKey",flags)!;var message=new Message();
   bool handled=(bool)escape.Invoke(form,new object[]{message,Keys.Escape})!;
   if(!handled||form.ImagesHelpShown||images.Checked!=before)throw new InvalidOperationException("Escape did not fold the diagnostic image help, or the option changed");
  }
  // 도움말 표시는 모두 같은 작은 크기이고 설명 글이 달려 있어야 한다(풍선이거나, 눌러서 펴는 설명이거나).
  if(Descendants(form).OfType<HelpBadge>().Any(b=>b.Size!=new Size(16,16)||string.IsNullOrWhiteSpace(tip.GetToolTip(b))&&string.IsNullOrWhiteSpace(b.AccessibleDescription)))
   throw new InvalidOperationException("Help badge without compact size or tooltip");

  using var client=new HttpClient(){BaseAddress=new Uri($"http://127.0.0.1:{server.Port}/"),Timeout=TimeSpan.FromSeconds(3)};
  var fit=new PopupFit();
  server.PopupFit=fit;
  client.GetStringAsync($"popup-fit/{fit.Id}/0/600/16/48/420/280").GetAwaiter().GetResult();
  if(fit.Ready.Task.IsCompleted)throw new InvalidOperationException("Invalid viewport was accepted");
  client.GetStringAsync($"popup-fit/{fit.Id}/900/600/16/48/420/280").GetAwaiter().GetResult();
  var size=fit.Ready.Task.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
  if(OverlayPopup.FitAxis(1125,size.OuterWidth,size.ExtraWidth,size.ContentWidth)!=545||
     OverlayPopup.FitAxis(750,size.OuterHeight,size.ExtraHeight,size.ContentHeight)!=410)
   throw new InvalidOperationException("DPI-scaled window size does not include browser chrome");
  File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"popup-fit-validation.txt"),
   "PASS compact help tooltip, popup request validation, and browser chrome with 125% DPI");
 }

 [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window,out Rect rect);
 [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool PrintWindow(IntPtr window,IntPtr dc,uint flags);
 [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool PostMessage(IntPtr window,uint message,IntPtr w,IntPtr l);
 [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool IsWindow(IntPtr window);
 [System.Runtime.InteropServices.DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
 [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
 [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] struct Rect{public int Left,Top,Right,Bottom;}

 // 실제 브라우저로 오버레이 팝업을 연다: --popup-browser-check <chrome|edge> <현재 점수 표시 0|1>
 // 팝업 둘을 차례로 열어, 둘째를 열 때 첫째의 크기가 그대로인지(방금 연 팝업만 조정) 보고, 둘의 그림(PNG)을 남긴 뒤 두 창을 닫는다.
 // 창마다 그 브라우저의 프로세스가 연 창인지, 맞춘 크기가 오버레이 본문보다 작지 않은지, 둘째의 크기가 첫째를 맞춘 크기와 같은지 본다.
 // 알려진 한계: 크기는 "본문보다 작지 않고 두 창이 같다"까지만 본다. 두 창이 똑같이 너무 크게 맞춰지는 경우는 잡지 못한다
 // (창 테두리의 두께가 브라우저와 화면 배율마다 달라 바깥 크기의 기대값을 정할 수 없다). 그 경우는 남긴 그림으로 확인한다.
 // 요청한 브라우저가 없어 앱이 다른 브라우저로 대신 열면 그 사실을 결과에 적고 실행 불가로 끝난다(요청한 브라우저의 검사로 통과하지 않는다).
 // 이 PC 에 설치된 그 브라우저를 쓴다(이미 켜져 있으면 그 안에 앱 창이 하나씩 새로 뜬다). 이 검사가 연 창 말고는 건드리지 않는다.
 // 결과 popup-browser-<브라우저>-<0|1>.json 과 그림 둘.
 public static void Browser(Catalog catalog,string which,string current)
 {
  var browser=which=="chrome"?PopupBrowser.Chrome:which=="edge"?PopupBrowser.Edge:throw new ArgumentException("chrome 또는 edge");
  bool showCurrent=current=="1";
  using var form=new MainForm(catalog,18773,registerHotkeys:false);
  var flags=BindingFlags.Instance|BindingFlags.NonPublic;
  var server=(OverlayServer)typeof(MainForm).GetField("server",flags)!.GetValue(form)!;
  var appearance=(System.Text.Json.JsonElement)typeof(MainForm).GetField("appearance",flags)!.GetValue(form)!;
  server.Set(new{visible=true,label="Rise Up · 6B · MX · 하드판정",goal=99.25m,current=98.72m,showCurrent,appearance,
   overlayTitle="Rise Up",overlayDetails="6B · MX · 하드판정",confirmation=(object?)null,analysis=(object?)null,lastEvent=(object?)null});
  // 앱이 실제로 열 브라우저. Chrome 을 요청했는데 Chrome 이 없으면 앱은 Edge 로 대신 연다. 그때는 Edge 로 열리는지를 보고,
  // Chrome 으로 직접 여는 것은 확인하지 못한 것으로 남긴다(결과에 적고 실행 불가로 끝낸다. Chrome 검사의 통과로 치지 않는다).
  var (actual,found)=OverlayPopup.Resolve(browser,File.Exists,OverlayPopup.ReadAppPath);
  bool fallback=actual!=browser;
  string program=found??"(찾지 못함)";
  string opens=actual==PopupBrowser.Chrome?"chrome":"edge";
  string Owner(IntPtr window){GetWindowThreadProcessId(window,out uint id);try{using var p=System.Diagnostics.Process.GetProcessById((int)id);return p.ProcessName.ToLowerInvariant();}catch(ArgumentException){return "(끝난 프로세스)";}}
  var shots=new List<object>();var opened=new List<IntPtr>();(int Width,int Height) fitted=default;
  try{
   for(int round=1;round<=2;round++){
    var started=DateTime.Now;
    var window=Task.Run(()=>OverlayPopup.Show(server,appearance,showCurrent,browser)).GetAwaiter().GetResult();
    double seconds=(DateTime.Now-started).TotalSeconds;
    if(window==IntPtr.Zero)throw new Exception($"{which} 팝업 {round}: 크기를 맞출 창을 찾지 못했다({seconds:F1}초)");
    opened.Add(window);
    // 크기를 맞춘 창이 실제로 그 브라우저의 창인지 본다(프로세스 이름).
    string owner=Owner(window);
    if(owner!=OverlayPopup.ProcessName(actual))throw new Exception($"{which} 팝업 {round}: {opens} 로 열려야 하는데 창의 프로세스가 {owner} 다");
    // 방금 맞춘 크기. 첫째와 둘째가 같은 내용이므로 같은 크기여야 하고, 오버레이의 내용(본문 크기)보다 작으면 잘린 것이다.
    GetWindowRect(window,out var just);var size=(Width:just.Right-just.Left,Height:just.Bottom-just.Top);
    int contentWidth=OverlayText.Width(appearance,Array.Empty<string>()),contentHeight=OverlayLayout.Height(appearance,showCurrent);
    if(size.Width<contentWidth||size.Height<contentHeight)throw new Exception($"{which} 팝업 {round}: 창 {size.Width}×{size.Height} 이 오버레이 본문 {contentWidth}×{contentHeight} 보다 작다");
    if(round==1)fitted=size;
    else if(size!=fitted)throw new Exception($"{which}: 둘째 팝업의 크기 {size.Width}×{size.Height} 가 첫째를 맞춘 크기 {fitted.Width}×{fitted.Height} 와 다르다");
    // 앞서 연 팝업을 일부러 다른 크기로 바꿔 둔다. 다음 팝업을 연 뒤에도 그 크기이면 방금 연 팝업만 조정한 것이다.
    if(round==1)SetWindowPos(window,IntPtr.Zero,0,0,420,240,0x0002|0x0004|0x0010);
    Thread.Sleep(1200);   // 크기 조정 뒤 다시 그려질 시간
    GetWindowRect(window,out var r);
    var earlier=opened.Take(opened.Count-1).Select(w=>{GetWindowRect(w,out var e);return new{w=(long)w,width=e.Right-e.Left,height=e.Bottom-e.Top};}).ToList();
    using var image=new Bitmap(Math.Max(1,r.Right-r.Left),Math.Max(1,r.Bottom-r.Top));
    using(var g=Graphics.FromImage(image)){var dc=g.GetHdc();PrintWindow(window,dc,2);g.ReleaseHdc(dc);}
    string png=$"popup-browser-{which}-{current}-{round}.png";
    image.Save(Path.Combine(AppContext.BaseDirectory,png),System.Drawing.Imaging.ImageFormat.Png);
    shots.Add(new{round,seconds=Math.Round(seconds,1),window=(long)window,owner,fittedWidth=size.Width,fittedHeight=size.Height,width=r.Right-r.Left,height=r.Bottom-r.Top,png,earlier});
    if(round==2&&earlier.Any(e=>e.width!=420||e.height!=240))throw new Exception($"{which}: 둘째 팝업을 열 때 첫째 팝업의 크기가 바뀌었다");
    if(round==2&&opened[0]==opened[1])throw new Exception($"{which}: 둘째 팝업으로 첫째 창을 골랐다");
   }
  }finally{
   foreach(var w in opened)if(IsWindow(w))PostMessage(w,0x0010,IntPtr.Zero,IntPtr.Zero);   // WM_CLOSE: 이 검사가 연 창만 닫는다
  }
  File.WriteAllText(Path.Combine(AppContext.BaseDirectory,$"popup-browser-{which}-{current}.json"),
   System.Text.Json.JsonSerializer.Serialize(new{requested=which,opened=opens,fallback,directOpenVerified=!fallback,showCurrent,program,shots},new System.Text.Json.JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));
  if(fallback)throw new CheckUnavailableException($"{which} 가 설치돼 있지 않아 {opens} 로 대신 열렸다(그 대체 동작은 확인했다). {which} 로 직접 여는 것은 확인하지 못했다.");
 }

 static IEnumerable<Control> Descendants(Control parent)
 {
  foreach(Control child in parent.Controls){
   yield return child;
   foreach(var descendant in Descendants(child))yield return descendant;
  }
 }
}
