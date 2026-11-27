using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using DjmaxScore;
namespace LadderRecords;
// 오버레이를 상태별로 띄워 보는 도구.
//
// 화면에 보이지 않는 주 화면 하나를 만들어 각 오버레이 상태를 재현한다(감지 중, 곡 고정, 분석 중, 분석 실패, 저장 확인 대기, 등록 결과 셋).
// 오버레이가 받는 상태(/state)는 그 주 화면이 평소처럼 만든 것이다. 표본 JSON 을 따로 손으로 만들지 않는다.
//
//   --overlay-check          사람용 도구. 작은 창의 단추로 상태를 고르고, 브라우저나 OBS 에서 http://127.0.0.1:18766/ 을 보며 창에 적힌 "볼 것"과 견준다.
//                            "닫기"나 창의 X 로 끝낸다. 스스로 끝나지 않는다(사람이 보는 도구다). 화면에 무엇이 어떻게 보였는지는 이 도구가 판정하지 않는다.
//   --overlay-check smoke    자동 검사. 상태를 차례로 만들어, 서버가 답하는지와 그 상태의 값(/state)이 기대한 대로인지 본다. 몇 초면 끝난다. 결과 overlay-tool-validation.json
//                            오버레이 화면이 실제로 어떻게 그려지는지(글자, 색, 배치, 사라지는 때)는 보지 않는다. 그것은 사람용 도구로 본다.
//
// 자료: 필요 없다. 기록과 설정은 실행 파일 옆의 test-data 아래 임시 폴더에 쓴다(‘UserData’의 records.json 은 건드리지 않는다). 실제 게임, 실제 단축키 입력은 쓰지 않는다.
[System.Runtime.Versioning.SupportedOSPlatform("windows10.0.19041")]
static class OverlayToolChecks
{
 const BindingFlags Flags=BindingFlags.NonPublic|BindingFlags.Instance;
 public const int Port=18766;
 const int Candidate=987117;
 sealed class Quiet(Catalog catalog,string preferences):MainForm(catalog,Port,false,preferences){protected override SteamSnapshot ReadSteamSnapshot()=>new(true,null,"오버레이 확인 도구");}
 // 상태 하나: 이름, 만드는 일, 사람이 볼 것, 자동 검사가 /state 에서 볼 것. 등록 결과 알림(lastEvent)이 있어야 하는 상태는 Result 가 true 다
 sealed record State(string Name,Action Make,string Look,Func<JsonElement,string?> Expect,bool Result=false);

 public static void Run(Catalog catalog,bool smoke)
 {
  string folder=Path.Combine(AppContext.BaseDirectory,"test-data",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
  string beside=UserFiles.At("records.json");DateTime? besideAt=File.Exists(beside)?File.GetLastWriteTimeUtc(beside):null;
  // 예전 설정 파일의 '전체화면 사용'이 켜져 있는 채로 돈다(이제 쓰지 않는 값이다. 저장 확인은 주 화면 안과 오버레이에 뜨고 창이 따로 뜨지 않는다)
  File.WriteAllText(Path.Combine(folder,"preferences.json"),JsonSerializer.Serialize(new UserOptions(true,false,false)));
  using var app=CheckPorts.Open(Port,()=>new Quiet(catalog,Path.Combine(folder,"preferences.json")));
  T Field<T>(string name)=>(T)typeof(MainForm).GetField(name,Flags)!.GetValue(app)!;
  object? Peek(string name)=>typeof(MainForm).GetField(name,Flags)!.GetValue(app);
  void Put(string name,object? value)=>typeof(MainForm).GetField(name,Flags)!.SetValue(app,value);
  void Call(string name,params object?[] values)=>typeof(MainForm).GetMethod(name,Flags)!.Invoke(app,values);
  var store=new RecordStore(Path.Combine(folder,"records.json"));Put("store",store);Call("RefreshProfiles");
  var session=Field<Session>("session");var hotkeys=Field<Hotkeys>("hotkeys");
  var song=catalog.Songs.Single(s=>s.Name=="Fundamental");var chart=new Selection(song.Id,8,"SC","하드",0);const string steamKey="FREESTYLE|Fundamental|8|SC";
  string title=OverlayText.Title(catalog,song);
  // 이름이 가장 긴 축에 드는 곡(다른 검사도 긴 제목의 보기로 쓴다)
  var longSong=catalog.Songs.First(s=>s.Name==OverlayText.KartTitle&&s.Charts.Count>0);var longChart=new Selection(longSong.Id,longSong.Charts[0].Button,longSong.Charts[0].Pattern,"노말",0);string longTitle=OverlayText.Title(catalog,longSong);

  // 기록과 등록 상태를 비운 바탕(감지 중, 곡 고정 없음)으로 돌아간다.
  // 앞 상태의 등록 결과 알림도 지운다. 지우지 않으면 오버레이가 그 알림을 7.5초까지 더 보여 줘서, 새로 고른 상태의 "볼 것"과 화면이 어긋난다
  // (앱에서는 등록 뒤 몇 초 동안 결과가 남는 것이 정상이다. 이 도구는 상태를 하나씩 따로 보여 주려는 것이다)
  void Base(int? record)
  {
   if(Peek("activeRegistration")!=null)Call("CancelAnalysis");
   if(session.Pending!=null)Call("Decide",false);
   Put("lastEvent",null);Put("savedChart",null);Put("eventEnded",false);Put("steamChart",null);Put("registeredSteamKey",null);
   session.Running=true;session.Next();session.Current=null;
   foreach(var c in new[]{chart,chart with{Judge="노말"}})if(store.Get(c)!=null)store.Wipe(c);
   if(record is int raw)store.SetManual(chart,ScoreValue.Rate(raw),3,rawScore:raw);
   Call("RefreshState");
  }
  object Request()
  {
   var request=Activator.CreateInstance(typeof(MainForm).GetNestedType("RegistrationRequest",BindingFlags.NonPublic)!,Field<int>("epoch"),steamKey,chart,store.Active.Id,Stopwatch.GetTimestamp(),DateTime.UtcNow,RateSource.Unknown,"FREESTYLE",0,0,new CancellationTokenSource())!;
   Put("activeRegistration",request);return request;
  }
  void Lock(decimal? current=null){session.Detect(chart);session.Current=current;Call("RefreshState");}
  // 게임이 그 곡의 판에 있는 것으로 둔다(Steam 의 곡 정보가 등록 요청의 것과 같다)
  void Stay()=>Put("steamChart",new SteamChart("FREESTYLE","Fundamental",8,"SC"));
  void Offer(){Lock();var request=Request();Call("OnScoreAnswer",request,new ScoreAnswer(1,0,steamKey,new RegistrationResult(1,1,RegistrationStatus.Accepted,"",Candidate,Candidate,null,null,0,0,0),0,0));if(session.Pending==null)throw new Exception("후보가 만들어지지 않음");}

  // /state 를 읽는 도우미
  string? S(JsonElement e,params string[] path){foreach(string key in path){if(e.ValueKind!=JsonValueKind.Object||!e.TryGetProperty(key,out e))return null;}return e.ValueKind switch{JsonValueKind.Null=>null,JsonValueKind.String=>e.GetString(),_=>e.GetRawText()};}
  // 숫자는 값으로 견준다(99.5 와 99.50 은 같다)
  bool Same(string? got,string? value)=>got==value||(decimal.TryParse(got,System.Globalization.NumberStyles.Number,System.Globalization.CultureInfo.InvariantCulture,out var a)&&decimal.TryParse(value,System.Globalization.NumberStyles.Number,System.Globalization.CultureInfo.InvariantCulture,out var b)&&a==b);
  string? Want(JsonElement e,params (string[] Path,string? Value)[] wants){foreach(var (path,value) in wants){string? got=S(e,path);if(!Same(got,value))return $"{string.Join(".",path)} 가 {got??"없음"} (기대: {value??"없음"})";}return null;}
  string[] P(params string[] path)=>path;
  string confirmKey=Hotkeys.Format(hotkeys.Items[2]),cancelKey=Hotkeys.Format(hotkeys.Items[3]);

  var states=new List<State>{
   new("감지 전(숨김)",()=>{Base(null);session.Running=false;Call("RefreshState");},
    "오버레이에 아무것도 보이지 않는다.",
    e=>Want(e,(P("visible"),"false"))),
   new("곡을 찾는 중",()=>Base(null),
    "\"악곡을 감지하고 있습니다\"가 보인다.",
    e=>Want(e,(P("visible"),"true"),(P("label"),"악곡을 감지하고 있습니다"),(P("goal"),null),(P("confirmation"),null),(P("analysis"),null))),
   new("곡 고정 · 기록 없음",()=>{Base(null);Lock();},
    $"곡 이름({title})과 \"8B · SC · 하드판정\"이 보이고, 목표 기록은 미등록으로 보인다.",
    e=>Want(e,(P("visible"),"true"),(P("overlayTitle"),title),(P("overlayDetails"),"8B · SC · 하드판정"),(P("goal"),null),(P("tone"),"unknown"))),
   new("곡 고정 · 긴 곡 이름",()=>{Base(null);session.Detect(longChart);Call("RefreshState");},
    $"긴 곡 이름({longTitle})이 오버레이 폭 안에서 잘리거나 줄어들어 보이고, 다른 글과 겹치지 않는다.",
    e=>Want(e,(P("overlayTitle"),longTitle),(P("visible"),"true"))),
   new("곡 고정 · 현재가 목표보다 낮음",()=>{Base(995034);Lock(98.54m);},
    "목표 99.50%, 현재 98.54% 가 보이고 현재 점수가 \"목표 미만\" 색이다.",
    e=>Want(e,(P("goal"),"99.50"),(P("current"),"98.54"),(P("tone"),"below"))),
   new("곡 고정 · 현재가 목표 이상",()=>{Base(970000);Lock(98.54m);},
    "목표 97.00%, 현재 98.54% 가 보이고 현재 점수가 \"목표 이상\" 색이다.",
    e=>Want(e,(P("goal"),"97.00"),(P("current"),"98.54"),(P("tone"),"above"))),
   new("점수 분석 중",()=>{Base(null);Lock();Call("SetAnalysis",Request(),"checking",null);},
    $"점수를 확인하는 중이라는 표시와 곡 정보, 취소 키({cancelKey})가 보인다.",
    e=>Want(e,(P("analysis","stage"),"checking"),(P("analysis","song"),title),(P("analysis","cancel"),cancelKey),(P("confirmation"),null))),
   new("점수 분석 실패(3초)",()=>{Base(null);Lock();Call("FailRegistration",Request(),"LocationUncertain");},
    "짧은 실패 사유가 3초쯤 보였다가 사라진다(고른 뒤 3초 안에 본다).",
    e=>Want(e,(P("analysis","stage"),"failure"))??(string.IsNullOrEmpty(S(e,"analysis","reason"))?"analysis.reason 이 비어 있음":null)),
   new("저장 확인 대기 · 첫 기록",()=>{Base(null);Offer();},
    // 오버레이의 저장 확인 화면은 세 줄이다: 확인 질문("등록하시겠습니까?"), 등록할 SCORE 와 RATE, 등록·취소 키. 기존 기록은 이 화면에 나오지 않는다(프로그램의 확인 창에는 나온다).
    // 그래서 이 상태와 다음 상태의 "볼 것"에 기존 기록이 보인다고 적지 않는다(실제 화면으로 확인했다)
    $"\"등록하시겠습니까?\", 등록할 기록 987,117 과 98.71%, \"등록 ({confirmKey}) / 취소 ({cancelKey})\"가 보인다. 기존 기록은 오버레이의 이 화면에 나오지 않는다.",
    e=>Want(e,(P("confirmation","score"),"987117"),(P("confirmation","accuracy"),"98.71"),(P("confirmation","previous"),null),(P("confirmation","song"),title),(P("confirmation","confirm"),confirmKey),(P("confirmation","cancel"),cancelKey),(P("analysis"),null))),
   new("저장 확인 대기 · 기존 기록 있음",()=>{Base(970000);Offer();},
    "화면은 \"첫 기록\"과 같다(등록할 기록 987,117 과 98.71%, 등록·취소 키). 기존 기록(970,000 · 97.00%)이 저장돼 있다는 것만 다르고, 그 기록은 오버레이의 이 화면에 나오지 않는다.",
    e=>Want(e,(P("confirmation","score"),"987117"),(P("confirmation","previous"),"97.00"))),
   new("등록 결과 · 최초 등록",()=>{Base(null);Offer();Call("Decide",true);},
    "\"최초 등록\" 알림과 987,117 이 잠깐 보인다(고른 직후에 본다).",
    e=>Want(e,(P("lastEvent","kind"),"first"),(P("lastEvent","text"),"최초 등록"),(P("lastEvent","score"),"987117"),(P("confirmation"),null)),true),
   new("등록 결과 · 기록 갱신",()=>{Base(970000);Offer();Call("Decide",true);},
    "\"기록 갱신\" 알림과 새 점수 987,117, 이전 점수 970,000 이 잠깐 보인다.",
    e=>Want(e,(P("lastEvent","kind"),"best"),(P("lastEvent","text"),"기록 갱신"),(P("lastEvent","score"),"987117"),(P("lastEvent","previousScore"),"970000")),true),
   new("등록 결과 · 기존 기록 유지",()=>{Base(995034);Offer();Call("Decide",true);},
    "\"기존 기록 유지\" 알림이 잠깐 보인다(더 낮은 점수라 최고 기록은 그대로다).",
    e=>Want(e,(P("lastEvent","kind"),"kept"),(P("lastEvent","text"),"기존 기록 유지"),(P("lastEvent","previousScore"),"995034")),true),
   // 저장한 뒤에도 게임이 그 판에 있는 동안(Steam 의 곡 정보가 그대로): 오버레이에는 방금 저장한 곡과 저장이 반영된 개인 최고, 방금 저장한 점수가 간다(결과가 끝나면 그 곡의 화면으로 돌아간다)
   new("등록 결과 · 같은 판이 이어짐(경신)",()=>{Base(970000);Stay();Offer();Call("Decide",true);},
    "결과가 끝나면 같은 곡의 화면으로 돌아온다: 개인 최고 98.71%, 현재 98.71%(달성 색).",
    e=>Want(e,(P("lastEvent","kind"),"best"),(P("overlayTitle"),title),(P("overlayDetails"),"8B · SC · 하드판정"),(P("goal"),"98.71"),(P("current"),"98.71"),(P("eventOver"),"false"),(P("confirmation"),null)),true),
   new("등록 결과 · 같은 판이 이어짐(유지)",()=>{Base(995034);Stay();Offer();Call("Decide",true);},
    "결과가 끝나면 같은 곡의 화면으로 돌아온다: 개인 최고 99.50%, 현재 98.71%(미달성 색).",
    e=>Want(e,(P("lastEvent","kind"),"kept"),(P("overlayTitle"),title),(P("goal"),"99.50"),(P("current"),"98.71"),(P("eventOver"),"false")),true),
   new("등록 결과 · 게임이 그 곡에서 벗어남",()=>{Base(970000);Stay();Offer();Call("Decide",true);Put("steamChart",null);Put("registeredSteamKey",null);Call("RefreshState");},
    "결과가 끝나면 감지 대기로 돌아간다.",
    e=>Want(e,(P("lastEvent","kind"),"best"),(P("overlayTitle"),null),(P("goal"),null),(P("current"),null),(P("eventOver"),"false")),true),
   // 저장 결과를 끝내는 때: 다른 곡의 감지가 확정되거나 새 등록이 실제로 시작되면 끝낸다. 같은 곡이 다시 고정되는 것만으로는 끝내지 않는다. 한 번 끝낸 결과는 새 요청을 취소해도 다시 살아나지 않는다
   new("등록 결과 · 같은 곡이 다시 고정됨",()=>{Base(970000);Offer();Call("Decide",true);Lock();},
    "결과가 이어진다.",
    e=>Want(e,(P("lastEvent","kind"),"best"),(P("overlayTitle"),title),(P("eventOver"),"false")),true),
   new("등록 결과 · 다른 곡이 고정됨",()=>{Base(970000);Offer();Call("Decide",true);session.Detect(longChart);Call("RefreshState");},
    "결과가 끝나고 새 곡의 화면이 보인다(페이지를 새로 열어도 결과가 다시 보이지 않는다).",
    e=>Want(e,(P("overlayTitle"),longTitle),(P("eventOver"),"true")),true),
   new("등록 결과 · 새 등록을 시작했다 취소함",()=>{Base(970000);Offer();Call("Decide",true);Offer();Call("Decide",false);},
    "결과가 끝난 채로 남는다(취소해도 옛 결과가 다시 보이지 않는다).",
    e=>Want(e,(P("confirmation"),null),(P("eventOver"),"true")),true),
   new("등록 취소 뒤",()=>{Base(null);Offer();Call("Decide",false);},
    "저장 확인 표시가 사라지고 곡 정보만 남는다.",
    e=>Want(e,(P("confirmation"),null),(P("analysis"),null),(P("visible"),"true"))),
  };

  if(smoke){Smoke(states,catalog,()=>(File.Exists(beside)?File.GetLastWriteTimeUtc(beside):(DateTime?)null)==besideAt,Field<OverlayServer>("server"));Base(null);app.Close();return;}

  using var window=new CompactDialog("오버레이 확인 도구"){StartPosition=FormStartPosition.CenterScreen,ShowInTaskbar=true,MinimizeBox=true};
  var body=window.Controls.OfType<FlowLayoutPanel>().Single();
  string url=$"http://127.0.0.1:{Port}/";
  body.Controls.Add(Boxes.Text($"브라우저나 OBS 브라우저 소스에서 {url} 을 연 채로, 아래에서 상태를 골라 오버레이를 본다.\n기록과 설정은 임시 폴더에만 쓴다. 닫기나 창의 X 로 끝낸다.",null,10f,false,560));
  var open=new Button(){Text="브라우저로 열기",AutoSize=true,Margin=new(0,8,0,8)};open.Click+=(_,_)=>{try{Process.Start(new ProcessStartInfo(url){UseShellExecute=true});}catch(Exception e){NoticeDialog.Show(window,"오버레이 확인 도구",e.Message);}};
  body.Controls.Add(open);
  var list=new FlowLayoutPanel(){AutoSize=true,WrapContents=true,MaximumSize=new(560,0),Margin=Padding.Empty};
  var now=Boxes.Text("상태를 고르면 여기에 볼 것이 나온다.","dim",10f,false,560);now.Margin=new(0,10,0,0);
  foreach(var state in states){var button=new Button(){Text=state.Name,AutoSize=true,Margin=new(0,0,6,6)};button.Click+=(_,_)=>{try{state.Make();now.Text=$"지금: {state.Name}\n볼 것: {state.Look}";}catch(Exception e){now.Text=$"지금: {state.Name}\n만들지 못함: {(e as TargetInvocationException)?.InnerException?.Message??e.Message}";}};list.Controls.Add(button);}
  body.Controls.Add(list);body.Controls.Add(now);
  var close=new Button(){Text="닫기",AutoSize=true,Tag="default",Margin=new(0,12,0,0)};close.Click+=(_,_)=>window.Close();body.Controls.Add(close);
  Base(null);
  Application.Run(window);
  Base(null);app.Close();
 }

 // 자동 검사: 서버가 답하는가, 상태마다 /state 의 값이 기대한 대로인가
 static void Smoke(List<State> states,Catalog catalog,Func<bool> besideUntouched,OverlayServer server)
 {
  var notes=new List<string>();var wrong=new List<string>();
  void Check(bool ok,string what,string? got=null){if(ok)notes.Add("PASS: "+what);else wrong.Add(got==null?what:$"{what} (본 것: {got})");}
  using var http=new HttpClient(){Timeout=TimeSpan.FromSeconds(5)};
  string Get(string path)=>Task.Run(()=>http.GetStringAsync($"http://127.0.0.1:{Port}{path}")).GetAwaiter().GetResult();
  string page=Get("/");Check(page.Contains("<html",StringComparison.OrdinalIgnoreCase)&&page.Contains("/state"),"오버레이 페이지(/)가 열리고 상태를 읽는 코드가 들어 있음",$"{page.Length}자");
  using(var titles=JsonDocument.Parse(Get("/titles")))Check(titles.RootElement.ValueKind==JsonValueKind.Array&&titles.RootElement.GetArrayLength()==catalog.Songs.Count,"곡 이름 목록(/titles)이 곡 목록의 수만큼 옴",titles.RootElement.GetArrayLength().ToString());
  // 오버레이의 글꼴: 페이지가 부르는 글꼴 파일이 모두 오고, 글꼴 폴더 밖의 파일이나 글꼴이 아닌 것은 주지 않는다
  // ("/fonts/../…" 은 여기서 보지 않는다. 보내는 쪽이 주소를 먼저 정리해 "/…" 로 보내므로 글꼴 길에 닿지 않는다)
  (int Code,byte[] Body,string? Type) Fetch(string path){using var answer=Task.Run(()=>http.GetAsync($"http://127.0.0.1:{Port}{path}")).GetAwaiter().GetResult();return((int)answer.StatusCode,Task.Run(()=>answer.Content.ReadAsByteArrayAsync()).GetAwaiter().GetResult(),answer.Content.Headers.ContentType?.MediaType);}
  var asked=System.Text.RegularExpressions.Regex.Matches(page,@"url\(/fonts/([A-Za-z0-9.\-]+)\)").Select(m=>m.Groups[1].Value).Distinct().ToList();
  Check(asked.Count>=5,"오버레이 페이지가 프로그램이 내주는 글꼴(/fonts/…)을 부름",string.Join(", ",asked));
  foreach(string name in asked)
  {
   var font=Fetch("/fonts/"+name);bool woff=name.EndsWith(".woff2");
   bool signed=font.Body.Length>4&&(woff?font.Body.AsSpan(0,4).SequenceEqual("wOF2"u8):font.Body.AsSpan(0,4).SequenceEqual(new byte[]{0,1,0,0}));
   Check(font.Code==200&&signed&&font.Type==(woff?"font/woff2":"font/ttf"),$"글꼴 {name} 이 옴",$"{font.Code}, {font.Body.Length}바이트, {font.Type}");
  }
  foreach(string bad in new[]{"/fonts/","/fonts/없는글꼴.ttf","/fonts/Missing.ttf","/fonts/records.json","/fonts/..%2Foverlay.html","/fonts/web/Oxanium-Variable.woff2","/fonts/SUIT-Bold.ttf.exe"})
  {
   var answer=Fetch(bad);Check(answer.Code==404&&answer.Body.Length==0,$"{bad} 는 주지 않음(404)",$"{answer.Code}, {answer.Body.Length}바이트");
  }
  // ── 송출용 서버(투컴, OverlayServer.Outer.cs) ── 이 PC 안의 다른 루프백 주소(127.0.0.2)에 묶어서 본다. 루프백이라 Windows 방화벽 창이 뜨지 않는다
  {
   var away=System.Net.IPAddress.Parse("127.0.0.2");string key=OverlayServer.NewKey(),second=OverlayServer.NewKey();
   // 요청 하나를 보내고 답을 통째로 읽는다. 답 없이 끊기거나 닿지 않으면 null
   (int Code,string Head,byte[] Body)? Ask(string line,string extra="")
   {
    try
    {
     using var client=new System.Net.Sockets.TcpClient();client.Connect(away,Port);var stream=client.GetStream();stream.ReadTimeout=5000;
     stream.Write(System.Text.Encoding.ASCII.GetBytes(line+"\r\n"+extra+"\r\n"));
     using var all=new MemoryStream();stream.CopyTo(all);var bytes=all.ToArray();int split=bytes.AsSpan().IndexOf("\r\n\r\n"u8);if(split<0)return null;
     string head=System.Text.Encoding.ASCII.GetString(bytes,0,split);return(int.Parse(head.Split(' ')[1]),head,bytes[(split+4)..]);
    }
    catch(Exception e)when(e is IOException or System.Net.Sockets.SocketException){return null;}
   }
   string Text((int Code,string Head,byte[] Body)? answer)=>answer is {} a?$"{a.Code}, {a.Body.Length}바이트":"답 없음";
   Check(OverlayServer.SameSubnet(System.Net.IPAddress.Parse("192.168.0.5"),System.Net.IPAddress.Parse("192.168.0.77"),24)&&!OverlayServer.SameSubnet(System.Net.IPAddress.Parse("192.168.1.5"),System.Net.IPAddress.Parse("192.168.0.77"),24)
    &&OverlayServer.SameSubnet(System.Net.IPAddress.Parse("192.168.1.5"),System.Net.IPAddress.Parse("192.168.0.77"),16)&&!OverlayServer.SameSubnet(System.Net.IPAddress.Parse("10.0.0.1"),System.Net.IPAddress.Parse("11.0.0.1"),8)
    &&OverlayServer.SameSubnet(System.Net.IPAddress.Parse("1.2.3.4"),System.Net.IPAddress.Parse("9.9.9.9"),0)&&!OverlayServer.SameSubnet(System.Net.IPAddress.Parse("1.2.3.4"),System.Net.IPAddress.Parse("1.2.3.5"),32),"같은 서브넷인지의 셈이 맞음");
   Check(OverlayServer.ValidKey(key)&&key!=second&&!OverlayServer.ValidKey(key.ToUpperInvariant().Replace('0','G'))&&!OverlayServer.ValidKey(key[..31]),"연결 키는 16진수 32자이고 만들 때마다 다름");
   Check(Ask("GET /o/"+key+"/state HTTP/1.1")==null&&server.OuterAddress==null,"송출용 서버를 열기 전에는 닿지 않음");
   server.OpenOuter(away,8,key);
   var outerPage=Ask("GET /o/"+key+"/ HTTP/1.1");string pageText=outerPage is {} p?System.Text.Encoding.UTF8.GetString(p.Body):"";
   Check(outerPage?.Code==200&&outerPage.Value.Head.Contains("text/html")&&pageText.Contains("url(fonts/")&&!pageText.Contains("url(/fonts/")&&pageText.Contains("outerBase"),"전용 길의 페이지가 오고 글꼴을 그 길 아래에서 찾음",Text(outerPage));
   Check(server.OuterSeenAt==0,"페이지를 받는 것만으로는 '연결됨'으로 세지 않음");
   var live=Ask("GET /o/"+key+"/state HTTP/1.1");var nowLine=live?.Head.Split("\r\n").FirstOrDefault(h=>h.StartsWith("X-Now: "));
   bool clock=nowLine!=null&&long.TryParse(nowLine[7..],out long sent)&&Math.Abs(sent-DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())<5000;
   Check(live?.Code==200&&live.Value.Head.Contains("application/json")&&live.Value.Body.AsSpan().SequenceEqual(System.Text.Encoding.UTF8.GetBytes(Get("/state")))&&clock,"전용 길의 상태가 로컬 서버와 같은 내용으로 오고 서버 시각이 함께 옴",Text(live)+" "+nowLine);
   Check(server.OuterSeenAt>0,"키가 맞는 상태 조회가 오면 온 때를 적음");
   var font=Ask("GET /o/"+key+"/fonts/SUIT-Bold.ttf HTTP/1.1");
   Check(font?.Code==200&&font.Value.Head.Contains("font/ttf")&&font.Value.Body.Length>100000,"전용 길의 글꼴이 옴",Text(font));
   var slash=Ask("GET /o/"+key+" HTTP/1.1");
   Check(slash?.Code==302&&slash.Value.Head.Contains("Location: /o/"+key+"/"),"끝의 / 가 빠진 주소는 / 가 붙은 주소로 보냄",Text(slash));
   foreach(string refused in new[]{"GET /o/"+second+"/state HTTP/1.1","GET /o/"+second+"/ HTTP/1.1","GET /state HTTP/1.1","GET / HTTP/1.1","GET /titles HTTP/1.1","GET /popup-fit/1/2/3/4/5/6/7 HTTP/1.1","GET /fonts/SUIT-Bold.ttf HTTP/1.1","GET /o/"+key+"/titles HTTP/1.1","GET /o/"+key+"/fonts/records.json HTTP/1.1","GET /o/"+key+"/state?x=1 HTTP/1.1","POST /o/"+key+"/state HTTP/1.1","GET /o/ HTTP/1.1","GET /o/"+key[..20]+" HTTP/1.1"})
   {
    var answer=Ask(refused);Check(answer?.Code==404&&answer.Value.Body.Length==0,$"송출용 서버: {refused.Replace(key,"<키>").Replace(second,"<다른 키>").Replace(key[..20],"<짧은 키>")} 는 주지 않음(404)",Text(answer));
   }
   Check(Ask("GET /o/"+key+"/state HTTP/1.1","X-Long: "+new string('a',3000)+"\r\n")==null,"2KB 를 넘는 요청은 답 없이 끊음");
   Check(Get("/state").Length>2&&System.Text.Encoding.UTF8.GetString(Fetch("/titles").Body).StartsWith("["),"로컬 서버는 그대로 답함");
   // 동시에 16개까지: 아무것도 보내지 않는 연결 16개를 잡아 두면 그다음 연결은 답 없이 끊긴다. 풀면 다시 받는다
   var idle=Enumerable.Range(0,OverlayServer.OuterConnectionLimit).Select(_=>{var c=new System.Net.Sockets.TcpClient();c.Connect(away,Port);return c;}).ToList();
   Thread.Sleep(400);bool full=Ask("GET /o/"+key+"/state HTTP/1.1")==null;
   foreach(var c in idle)c.Dispose();
   var until=DateTime.UtcNow.AddSeconds(5);(int Code,string Head,byte[] Body)? again=null;while(DateTime.UtcNow<until&&(again=Ask("GET /o/"+key+"/state HTTP/1.1"))==null)Thread.Sleep(100);
   Check(full&&again?.Code==200,"동시에 16개를 넘는 연결은 받지 않고, 풀리면 다시 받음",$"가득 찼을 때 끊김 {full}, 풀린 뒤 {Text(again)}");
   // 키를 바꾸면 옛 키는 곧바로 404
   server.SetOuterKey(second);
   Check(server.OuterSeenAt==0&&Ask("GET /o/"+key+"/state HTTP/1.1")?.Code==404&&Ask("GET /o/"+second+"/state HTTP/1.1")?.Code==200&&server.OuterSeenAt>0,"연결 키를 바꾸면 옛 키는 404, 새 키는 답함");
   // 키를 바꾸기 전에 시작된 조회: 요청을 반만 보내 두고 키를 바꾼 뒤 옛 키로 마저 보내면 주지 않고(404), 새 키의 연결로 세지도 않는다
   {
    string old=second,fresh=OverlayServer.NewKey();
    using var slow=new System.Net.Sockets.TcpClient();slow.Connect(away,Port);var wire=slow.GetStream();wire.ReadTimeout=5000;
    wire.Write(System.Text.Encoding.ASCII.GetBytes("GET /o/"+old+"/state HTTP/1.1\r\n"));Thread.Sleep(300);
    server.SetOuterKey(fresh);
    wire.Write(System.Text.Encoding.ASCII.GetBytes("\r\n"));
    using var whole=new MemoryStream();try{wire.CopyTo(whole);}catch(IOException){}
    string head=System.Text.Encoding.ASCII.GetString(whole.ToArray());
    Check(head.StartsWith("HTTP/1.1 404")&&server.OuterSeenAt==0,"키를 바꾸기 전에 시작된 조회는 주지 않고 새 키의 연결로 세지 않음",head.Split('\r')[0]+", 온 때 "+server.OuterSeenAt);
    Check(Ask("GET /o/"+fresh+"/state HTTP/1.1")?.Code==200&&server.OuterSeenAt>0,"새 키로 새로 시작한 조회는 답하고 세어짐");
    server.SetOuterKey(second);
   }
   // 받는 범위만 바꾸기: 송출용 서버를 다시 열지 않고 범위와 연결 확인이 그대로 이어진다
   Check(Ask("GET /o/"+second+"/state HTTP/1.1")?.Code==200&&server.OuterPrefix==8,"받는 범위를 바꾸기 전: 답함");
   {long seen=server.OuterSeenAt;server.SetOuterPrefix(32);Check(server.OuterPrefix==32&&Ask("GET /o/"+second+"/state HTTP/1.1")==null&&server.OuterSeenAt==seen&&Equals(server.OuterAddress,away),"받는 범위만 좁히면 범위 밖의 연결을 끊고, 송출용 서버를 다시 열지 않으며 연결 확인도 그대로");server.SetOuterPrefix(8);}
   // 같은 서브넷에서 온 연결만: 송출용 서버를 127.0.0.2/32 로 열면 이 검사의 연결(127.0.0.1 에서 감)은 다른 서브넷이라 답 없이 끊긴다
   server.OpenOuter(away,32,second);
   Check(Ask("GET /o/"+second+"/state HTTP/1.1")==null&&server.OuterSeenAt==0,"다른 서브넷에서 온 연결은 답 없이 끊음");
   // 다른 프로그램이 그 주소의 포트를 쓰고 있으면 열지 못하고 그 까닭을 알 수 있다
   var taken=new System.Net.Sockets.TcpListener(System.Net.IPAddress.Parse("127.0.0.3"),Port);taken.Start();
   System.Net.Sockets.SocketException? busy=null;try{server.OpenOuter(System.Net.IPAddress.Parse("127.0.0.3"),8,second);}catch(System.Net.Sockets.SocketException e){busy=e;}
   taken.Stop();
   Check(busy?.SocketErrorCode==System.Net.Sockets.SocketError.AddressAlreadyInUse&&server.OuterAddress==null,"포트를 다른 프로그램이 쓰고 있으면 열지 못하고 까닭이 '사용 중'으로 옴",busy?.SocketErrorCode.ToString());
   server.OpenOuter(away,8,second);server.CloseOuter();
   Check(server.OuterAddress==null&&Ask("GET /o/"+second+"/state HTTP/1.1")==null,"송출용 서버를 닫으면 닿지 않음");
  }
  // 등록 결과가 아닌 상태에는 앞 상태의 등록 결과 알림이 남아 있으면 안 된다(남으면 오버레이가 그 결과를 7.5초까지 더 보여 준다)
  string? Read(State state)
  {
   state.Make();Application.DoEvents();
   using var doc=JsonDocument.Parse(Get("/state"));
   string? problem=state.Expect(doc.RootElement);
   if(problem==null&&!state.Result&&doc.RootElement.TryGetProperty("lastEvent",out var left)&&left.ValueKind!=JsonValueKind.Null)problem="앞 상태의 등록 결과 알림(lastEvent)이 남아 있음: "+left.GetRawText();
   return problem;
  }
  string Why(Exception e)=>(e as TargetInvocationException)?.InnerException?.Message??e.Message;
  foreach(var state in states)
  {
   try{string? problem=Read(state);Check(problem==null,$"상태 \"{state.Name}\": /state 의 값이 기대한 대로임",problem);}
   catch(Exception e){wrong.Add($"상태 \"{state.Name}\": 만들거나 읽지 못함 ({Why(e)})");}
  }
  // 등록 결과 바로 뒤에 다른 상태를 골랐을 때: 그 상태의 값이 맞고 앞의 결과 알림이 남지 않는다
  foreach(var result in states.Where(s=>s.Result))foreach(var next in states.Where(s=>!s.Result&&s.Name is "감지 전(숨김)" or "곡을 찾는 중" or "등록 취소 뒤"))
  {
   try{result.Make();Application.DoEvents();string? problem=Read(next);Check(problem==null,$"\"{result.Name}\" 바로 뒤에 \"{next.Name}\": 그 상태의 값이 맞고 앞의 결과 알림이 남지 않음",problem);}
   catch(Exception e){wrong.Add($"\"{result.Name}\" 바로 뒤에 \"{next.Name}\": 만들거나 읽지 못함 ({Why(e)})");}
  }
  Check(besideUntouched(),"‘UserData’의 기록 파일은 건드리지 않음");
  File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"overlay-tool-validation.json"),JsonSerializer.Serialize(new{passed=wrong.Count==0,wrong,notes,not_checked="오버레이 화면에 실제로 어떻게 그려지는지(글자, 색, 배치, 사라지는 때)는 보지 않는다. 사람용 도구(--overlay-check)로 본다."},new JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));
  if(wrong.Count>0)throw new Exception("오버레이 상태 검사 실패: "+string.Join(" / ",wrong));
 }
}
