namespace LadderRecords;

// 오버레이 팝업을 열 브라우저를 고르는 규칙의 검사(자체 검사 --self-test 가 부른다. 개발용 빌드에만 들어간다).
// 실제 레지스트리와 설치 폴더 대신 꾸민 값을 넣어 본다. 실제 브라우저로 여는 것은 --popup-browser-check(OverlayPopupChecks)가 본다.
// 결과 popup-browser-validation.txt
[System.Runtime.Versioning.SupportedOSPlatform("windows10.0.19041")]
static class PopupBrowserTests
{
 public static void Run()
 {
  var notes=new List<string>();
  void Check(bool condition,string name){if(!condition)throw new Exception("Popup browser: "+name);notes.Add("PASS: "+name);}

  // 기본 웹 브라우저 → 팝업을 열 브라우저
  Check(OverlayPopup.Choose("ChromeHTML")==PopupBrowser.Chrome,"Chrome default opens Chrome");
  Check(OverlayPopup.Choose("MSEdgeHTM")==PopupBrowser.Edge,"Edge default opens Edge");
  foreach(string other in new[]{"FirefoxURL-308046B0AF4A39CB","WhaleHTML","BraveHTML","ChromeBHTML","ChromeDHTML","ChromeSSHTM","MSEdgeBHTML","MSEdgeDHTML","IE.HTTP","AppXq0fevzme2pys62n3e0fbqa7peapykr8v",""," "})
   Check(OverlayPopup.Choose(other)==PopupBrowser.Edge,$"Other default '{other}' opens Edge");
  Check(OverlayPopup.Choose(null)==PopupBrowser.Edge,"Unreadable default opens Edge");
  Check(OverlayPopup.Choose(" ChromeHTML ")==PopupBrowser.Chrome,"ProgId with spaces is trimmed");
  // 사용자별로 설치한 정식판 Chrome 은 등록 이름 뒤에 그 설치의 접미사가 붙는다. 베타·개발·카나리판은 접미사가 붙어도 Chrome 으로 보지 않는다
  foreach(string perUser in new[]{"ChromeHTML.ABCDEFGHIJKLMNOPQRSTUVWXYZ","ChromeHTML.7XKQ2D5B3A","chromehtml.abc","CHROMEHTML"})
   Check(OverlayPopup.Choose(perUser)==PopupBrowser.Chrome,$"Per-user or differently cased Chrome '{perUser}' opens Chrome");
  foreach(string notStable in new[]{"ChromeBHTML.ABCDEFGHIJKLMNOPQRSTUVWXYZ","ChromeDHTML.ABC","ChromeSSHTM.ABC","ChromeHTMLX","ChromeHTML2.ABC","ChromiumHTM","ChromiumHTM.ABC","XChromeHTML"})
   Check(OverlayPopup.Choose(notStable)==PopupBrowser.Edge,$"Not stable Chrome '{notStable}' opens Edge");

  // 기본 웹 브라우저를 읽는 차례: https 의 UserChoiceLatest → https 의 UserChoice → http 의 둘
  string? Read(Dictionary<string,string?> values,string key)=>values.TryGetValue(key,out var value)?value:null;
  var both=new Dictionary<string,string?>{[@"https\UserChoiceLatest\ProgId"]="ChromeHTML",[@"https\UserChoice"]="MSEdgeHTM"};
  Check(OverlayPopup.DefaultProgId(k=>Read(both,k))=="ChromeHTML","Newer UserChoiceLatest wins over UserChoice");
  var old=new Dictionary<string,string?>{[@"https\UserChoiceLatest\ProgId"]="",[@"https\UserChoice"]="ChromeHTML"};
  Check(OverlayPopup.DefaultProgId(k=>Read(old,k))=="ChromeHTML","Empty UserChoiceLatest falls back to UserChoice");
  var http=new Dictionary<string,string?>{[@"http\UserChoice"]="ChromeHTML"};
  Check(OverlayPopup.DefaultProgId(k=>Read(http,k))=="ChromeHTML","http is read when https is missing");
  var secure=new Dictionary<string,string?>{[@"https\UserChoice"]="MSEdgeHTM",[@"http\UserChoiceLatest\ProgId"]="ChromeHTML"};
  Check(OverlayPopup.DefaultProgId(k=>Read(secure,k))=="MSEdgeHTM","https is read before http");
  Check(OverlayPopup.DefaultProgId(_=>null)==null,"No association reads as null");

  // 실행 파일 찾기: 정해진 설치 자리를 먼저, 없으면 등록된 경로(따옴표를 벗긴다), 둘 다 없으면 null
  string Folder(Environment.SpecialFolder folder,params string[] parts)=>Path.Combine(new[]{Environment.GetFolderPath(folder)}.Concat(parts).ToArray());
  string chromeUser=Folder(Environment.SpecialFolder.LocalApplicationData,"Google","Chrome","Application","chrome.exe");
  string chromeSystem=Folder(Environment.SpecialFolder.ProgramFiles,"Google","Chrome","Application","chrome.exe");
  string edgeSystem=Folder(Environment.SpecialFolder.ProgramFilesX86,"Microsoft","Edge","Application","msedge.exe");
  Check(OverlayPopup.Executable(PopupBrowser.Chrome,p=>p==chromeUser,_=>null)==chromeUser,"Per-user Chrome install is found");
  Check(OverlayPopup.Executable(PopupBrowser.Chrome,p=>p==chromeSystem||p==chromeUser,_=>null)==chromeSystem,"System Chrome install is preferred");
  Check(OverlayPopup.Executable(PopupBrowser.Chrome,p=>p==@"D:\Apps\Chrome\chrome.exe",e=>e=="chrome.exe"?"\"D:\\Apps\\Chrome\\chrome.exe\"":null)==@"D:\Apps\Chrome\chrome.exe","Registered Chrome path is used when not in a usual folder");
  Check(OverlayPopup.Executable(PopupBrowser.Chrome,p=>p==edgeSystem,_=>null)==null,"Chrome is not found when only Edge exists");
  Check(OverlayPopup.Executable(PopupBrowser.Edge,p=>p==edgeSystem,_=>null)==edgeSystem,"Edge install is found");
  Check(OverlayPopup.Executable(PopupBrowser.Edge,_=>false,_=>@"C:\Gone\msedge.exe")==null,"Registered path that does not exist is not used");

  // 안내 글: 브라우저 이름과 다음 행동
  // 실제로 열 브라우저: Chrome 을 찾지 못하면 Edge 로 대신 연다. Edge 를 골랐는데 없으면 Chrome 으로 바꾸지 않는다
  Check(OverlayPopup.Resolve(PopupBrowser.Chrome,p=>p==chromeSystem||p==edgeSystem,_=>null)==(PopupBrowser.Chrome,chromeSystem),"Chrome default with Chrome installed opens Chrome");
  Check(OverlayPopup.Resolve(PopupBrowser.Chrome,p=>p==edgeSystem,_=>null)==(PopupBrowser.Edge,edgeSystem),"Chrome default without Chrome opens Edge instead");
  Check(OverlayPopup.Resolve(PopupBrowser.Chrome,_=>false,_=>null)==(PopupBrowser.Edge,null),"Neither Chrome nor Edge found reports Edge");
  Check(OverlayPopup.Resolve(PopupBrowser.Edge,p=>p==chromeSystem,_=>null)==(PopupBrowser.Edge,null),"Edge default without Edge does not switch to Chrome");

  Check(OverlayPopup.NotFound=="Edge를 찾을 수 없습니다. Edge 설치 상태를 확인한 뒤 다시 시도하세요.","Edge not found message");
  Check(OverlayPopup.CannotStart(PopupBrowser.Chrome)=="Chrome에서 오버레이 팝업을 열 수 없습니다. Chrome이 정상적으로 실행되는지 확인하세요.","Chrome start failure message");
  Check(OverlayPopup.CannotStart(PopupBrowser.Edge)=="Edge에서 오버레이 팝업을 열 수 없습니다. Edge가 정상적으로 실행되는지 확인하세요.","Edge start failure message");

  // 참고(판정하지 않는다): 이 PC 의 기본 웹 브라우저를 실제로 읽은 값과 고른 브라우저
  string? here=OverlayPopup.DefaultProgId(OverlayPopup.ReadAssociation);
  notes.Add($"NOTE: this PC default ProgId={(here??"(none)")} → {OverlayPopup.Choose(here)}");

  File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"popup-browser-validation.txt"),string.Join("\n",notes));
 }
}
