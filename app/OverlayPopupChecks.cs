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
  // 주 화면에는 도움말 표시가 여럿 있다(전체화면 사용, 진단 그림 저장). 전체화면 사용의 것을 그 체크 상자와 같은 줄에서 찾는다.
  var fullscreen=(CheckBox)typeof(MainForm).GetField("fullscreen",flags)!.GetValue(form)!;
  var badge=fullscreen.Parent!.Controls.OfType<HelpBadge>().Single();
  if(badge.Size!=new Size(16,16)||tip.GetToolTip(badge)?.Contains("등록·취소 단축키")!=true)
   throw new InvalidOperationException("Missing compact fullscreen help");
  // 도움말 표시는 모두 같은 작은 크기이고 설명 글이 달려 있어야 한다.
  if(Descendants(form).OfType<HelpBadge>().Any(b=>b.Size!=new Size(16,16)||string.IsNullOrWhiteSpace(tip.GetToolTip(b))))
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

 static IEnumerable<Control> Descendants(Control parent)
 {
  foreach(Control child in parent.Controls){
   yield return child;
   foreach(var descendant in Descendants(child))yield return descendant;
  }
 }
}
