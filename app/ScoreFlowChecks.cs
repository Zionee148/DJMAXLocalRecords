#if DEVELOPMENT_CHECKS
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using DjmaxScore;
namespace LadderRecords;

// 등록 무결성의 분기 시험: 확인·취소·저장·경합·곡 변경·재시작의 분기를 본다.
//
// 시험용 주 화면(ScoreReplayChecks 의 Probe)에 녹화의 끝부분(키 시각 30초 전부터 끝까지)을 실제 시간으로 넣고, 각본대로 조작한다.
// "다음 곡"은 Steam 곡 정보를 다른 채보로 바꾸고 같은 녹화를 처음부터 다시 트는 것으로, "같은 곡 재시작"은 곡 정보를 그대로 두고 다시 트는 것으로 만든다.
// 만든 시험이다: 실제 게임의 곡 전환·재시작이 아니고, 실제 게임 창 캡처·Steam·전역 단축키도 지나가지 않는다.
// 이름을 묻는 창이나 확인 창이 떠 있는 동안의 조작은 시험이 그 창을 찾아 대신 눌러 준다.
//
// 등록의 확인·취소는 주 화면의 Decide 를 직접 불러서 한다(저장 확인은 주 화면 오른쪽에 뜬다. 창을 따로 띄우지 않는다).
// 위에서 대신 눌러 준다는 "확인 창"은 프로필 삭제, 되돌리기, 기록 삭제를 묻는 창이다. 후보와 저장의 분기는 이 시험이 보지만, 주 화면의 저장 확인이 보이는 것과 그 단추·기존 기록 갱신은 보지 않는다.
// 그것은 UiChecks.Confirmations 가 본다(--dialogs-check 가 부른다. 만든 답을 주 화면에 넣는다. 녹화와 SCORE 판독은 지나가지 않는다).
// 설정 파일에는 예전의 '전체화면 사용'을 켜 둔다(아래의 new UserOptions(true)). 이제 쓰지 않는 값이고, 켜져 있어도 같은 흐름인지를 겸해 본다.
//
// 실행: DJMAXLocalRecords.exe --score-flow-check <cases.tsv> <사례 이름> <결과.json> <포트> <각본>
//   각본: pending-next-confirm, pending-next-cancel, saved-then-restart, pending-restart, change-during-request, cancel-during-request, guards, records-during-pending, stale-dialogs
static partial class ScoreReplayChecks
{
 // 떠 있는 알림 창(NoticeDialog)이나 예·아니요 창(ChoiceDialog)을 제목으로 찾는다.
 static Form? FindNotice(string title)=>Application.OpenForms.Cast<Form>().FirstOrDefault(f=>(f is NoticeDialog||f is ChoiceDialog||f is PlayNotice)&&f.Text==title&&f.Visible);
 // 그 창의 단추를 글자로 찾아 누른다("확인", "예", "아니요")
 static void Answer(Form box,string button)=>Everything(box).OfType<Button>().First(b=>b.Text==button).PerformClick();
 static void Dismiss(Form box)=>Answer(box,"확인");
 // 알림 창은 그것을 띄운 창과 제목이 같을 수 있어서(수동 기록 설정) 여기서는 뺀다
 static Form? FindForm(string title)=>Application.OpenForms.Cast<Form>().FirstOrDefault(f=>f is not NoticeDialog&&f is not PlayNotice&&f.Text==title&&f.Visible);
 static IEnumerable<Control> Everything(Control c)=>c.Controls.Cast<Control>().SelectMany(x=>new[]{x}.Concat(Everything(x)));

 internal static void Flow(Catalog catalog,string casesPath,string caseKey,string output,int port,string scenario)
 {
  const string Guard="점수 확인과 저장 확인이 끝난 뒤 프로필을 변경해 주세요.";
  string[] row=File.ReadAllLines(casesPath).Select(l=>l.Split('\t')).First(f=>f[0].Contains(caseKey));
  string id=row[0],position=row[1],source=row[2];
  double begin=double.Parse(row[3],Inv),end=double.Parse(row[4],Inv),press=double.Parse(row[5],Inv);
  // 키 시각 몇 초 전부터 트는가. 재시작이 들어가는 각본은 녹화를 처음부터 튼다
  // (재시작은 점수가 낮은 데서 다시 오르는 것으로 알아챈다. 끝부분만 틀면 낮은 데서 시작하지 않는다). 그 밖에는 끝 30초만 튼다.
  double Lead=scenario is "saved-then-restart" or "pending-restart" or "records-during-pending"?press-begin:30;
  int expected=int.Parse(row[6],Inv);
  var checks=new List<object>();var failures=new List<string>();var notes=new List<string>();
  var result=new Dictionary<string,object?>{["case"]=id,["position"]=position,["expected"]=expected,["scenario"]=scenario};
  void Check(string what,bool ok,object? got=null){checks.Add(new{what,ok,got=got?.ToString()});if(!ok)failures.Add(what);}
  try{
   string preferences=Path.Combine(AppContext.BaseDirectory,"replay-check-options.json");new UserOptions(true).Save(preferences);
   string log=Path.Combine(AppContext.BaseDirectory,"diagnostics");
   long logFrom=Directory.Exists(log)?new DirectoryInfo(log).EnumerateFiles("score-*.log").Sum(f=>f.Length):0;
   using var replay=new Replay(source,press-Lead,Lead+(end-press));
   using var app=new Probe(catalog,port,preferences,replay){ShowInTaskbar=false,Opacity=0};
   Get<ComboBox>(app,"gear").SelectedItem=position;
   var windows=Get<ComboBox>(app,"windows");var windowType=typeof(MainForm).GetNestedType("WindowItem",BindingFlags.NonPublic)!;
   var fakeWindow=Activator.CreateInstance(windowType,new IntPtr(1),"녹화 재생")!;windows.Items.Add(fakeWindow);windows.SelectedItem=fakeWindow;
   var session=Get<Session>(app,"session");var store=Get<RecordStore>(app,"store");var link=Get<ScoreLink>(app,"scoreLink");
   var status=Get<Label>(app,"status");var profiles=Get<ComboBox>(app,"profiles");
   string song=catalog.Songs.Single(s=>s.Name=="Fundamental").Id;
   var chartA=new SteamChart("FREESTYLE","Fundamental",8,"SC");var chartB=new SteamChart("FREESTYLE","Fundamental",8,"MAXIMUM");
   var selA=new Selection(song,8,"SC","노말",0);var selB=new Selection(song,8,"MAXIMUM","노말",0);
   if(store.Get(selA)!=null||store.Get(selB)!=null)throw new Exception("시험 폴더에 이미 기록이 있습니다. 깨끗한 폴더에서 돌려야 합니다.");

   // 창이 떠 있는 동안의 조작을 대신 하는 자동 조작 타이머: 예약한 조작을 차례로, 조건이 될 때까지 기다렸다가 실행한다.
   var robot=new LinkedList<Func<bool>>();
   using var hands=new System.Windows.Forms.Timer(){Interval=30};
   hands.Tick+=(_,_)=>{if(robot.First is not {} node)return;var step=node.Value;robot.RemoveFirst();try{if(!step())robot.AddFirst(step);}catch(Exception e){notes.Add("자동 조작 오류: "+(e.InnerException??e).Message);robot.Clear();}};
   hands.Start();

   async Task Until(Func<bool> condition,double seconds,string what){var watch=Stopwatch.StartNew();while(!condition()){if(watch.Elapsed.TotalSeconds>seconds)throw new TimeoutException("기다리다 시간이 넘음: "+what);await Task.Delay(20);}}
   void Press()=>Call(app,"Action",2,false);
   string? Try(Action action){try{action();return null;}catch(TargetInvocationException e){return (e.InnerException??e).Message;}catch(Exception e){return e.Message;}}
   object? Analysis()=>Peek(app,"analysisState");
   string? Stage()=>Analysis() is object a?(string?)a.GetType().GetProperty("stage")!.GetValue(a):null;
   bool Requesting()=>Peek(app,"activeRegistration")!=null;
   async Task<CapturedRecord?> Candidate(){await Until(()=>session.Pending!=null||Stage()=="failure",12,"등록 답");if(session.Pending==null)notes.Add("후보가 안 나옴: "+status.Text);return session.Pending;}
   void Begin(SteamChart chart){app.Chart=chart;replay.Start();Call(app,"Action",6,false);}
   Task At(double second)=>Until(()=>replay.Seconds>=second,second+25,$"녹화 {second}초");
   Task ToEnd()=>At(Lead);
   Task SongLocked(Selection sel)=>Until(()=>session.Locked?.Key==sel.Key,12,"곡·판정 확정");
   int? Value(Selection s)=>store.Get(s)?.Value;
   int? Count(Selection s)=>store.Get(s)?.PlayCount;
   Button Find(string text)=>Everything(app).OfType<Button>().First(b=>b.Text==text);
   JsonElement Overlay(){using var http=new HttpClient();using var state=JsonDocument.Parse(http.GetStringAsync($"http://127.0.0.1:{port}/state").GetAwaiter().GetResult());return state.RootElement.Clone();}
   async Task<CapturedRecord?> FirstCandidate()
   {
    Begin(chartA);await ToEnd();await SongLocked(selA);Press();
    var first=await Candidate();
    Check("첫 판: 후보가 기대 점수와 같음",first?.RawScore==expected,first?.RawScore);
    Check("확인 전에는 저장되지 않음",Value(selA)==null,Value(selA));
    return first;
   }

   // 확인을 미룬 채 다음 곡이 시작된다. 앞 곡의 후보를 뒤늦게 확인하거나 취소한다.
   async Task PendingNext(bool confirm)
   {
    if(await FirstCandidate()==null)return;
    int serial=link.PlaySerial;
    app.Chart=chartB;replay.Start();
    await Until(()=>link.PlayKey==chartB.Key,5,"다음 곡의 판이 열림");
    Check("확인 대기 중에 다음 곡의 판이 열림",link.PlaySerial==serial+1,link.PlaySerial-serial);
    await Until(()=>link.Location?.State==LocationState.Locked,25,"다음 곡의 자리 고정");
    long? lockedAt=link.Location!.LockedAt;
    await At(18);
    Check("확인 대기 중: 기다리는 후보는 앞 곡 그대로",session.Pending?.Chart.Key==selA.Key&&session.Pending?.RawScore==expected&&session.Pending?.SteamKey==chartA.Key,session.Pending?.Chart.Key);
    Check("확인 대기 중: 주 화면의 곡 정보는 앞 곡 그대로",session.Locked?.Key==selA.Key,session.Locked?.Key);
    var shown=Overlay();
    Check("확인 대기 중: 오버레이의 확인 화면도 앞 곡의 점수",shown.TryGetProperty("confirmation",out var confirmation)&&confirmation.ValueKind==JsonValueKind.Object&&confirmation.GetProperty("score").GetInt32()==expected&&confirmation.GetProperty("pattern").GetString()=="SC",shown.TryGetProperty("confirmation",out var c2)?c2.ToString():"없음");
    int observed=link.Observed;
    Call(app,"Decide",confirm);
    if(confirm)Check("앞 곡의 후보가 앞 곡에 저장됨",Value(selA)==expected&&Count(selA)==1,$"{Value(selA)} {Count(selA)}");
    else Check("취소: 앞 곡의 기록이 생기지 않음",Value(selA)==null,Value(selA));
    Check("다음 곡의 기록은 아직 없음",Value(selB)==null,Value(selB));
    Check("확인 대기가 풀림",session.Pending==null);
    Check("앞 후보를 처리해도 다음 곡의 판이 그대로임(새로 열지 않음)",link.PlaySerial==serial+1&&link.PlayKey==chartB.Key&&link.Location?.LockedAt==lockedAt,$"{link.PlaySerial-serial} {link.Location?.State}");
    await ToEnd();await SongLocked(selB);
    Check("다음 곡의 판이 끝까지 그대로이고 화면을 계속 받음",link.PlaySerial==serial+1&&link.Location?.LockedAt==lockedAt&&link.Observed>observed,$"{link.PlaySerial-serial} {link.Observed-observed}");
    Press();
    var second=await Candidate();
    Check("다음 곡: 후보가 기대 점수와 같음(그사이 쌓인 근거로)",second?.RawScore==expected,second?.RawScore);
    Check("다음 곡: 후보의 대상이 다음 곡",second?.Chart.Key==selB.Key,second?.Chart.Key);
    if(second==null)return;
    Call(app,"Decide",true);
    Check("다음 곡의 후보가 다음 곡에 저장됨",Value(selB)==expected&&Count(selB)==1,$"{Value(selB)} {Count(selB)}");
    if(confirm)Check("앞 곡의 기록은 그대로",Value(selA)==expected&&Count(selA)==1,$"{Value(selA)} {Count(selA)}");
    else Check("앞 곡의 기록은 여전히 없음",Value(selA)==null,Value(selA));
   }

   // 저장한 뒤 같은 곡을 다시 시작한다(afterRestart: 확인을 미룬 채 다시 시작하고, 알아챈 뒤에 확인한다).
   async Task Restart(bool afterRestart)
   {
    if(await FirstCandidate()==null)return;
    int serial=link.PlaySerial;
    if(!afterRestart){
     Call(app,"Decide",true);
     Check("확인하면 저장됨",Value(selA)==expected&&Count(selA)==1,$"{Value(selA)} {Count(selA)}");
     await Task.Delay(1500);
     string? refused=Try(Press);
     Check("저장한 판이 이어지는 동안 다시 등록하려 하면 거절됨",refused!=null&&session.Pending==null&&!Requesting(),refused);
     Check("상태 줄: 다음 곡을 기다림",status.Text.Contains("다음 곡을 기다리고 있습니다"),status.Text);
     Check("기록은 한 번만 저장됨",Count(selA)==1,Count(selA));
    }
    replay.Start();   // 같은 곡을 다시 시작(Steam 곡 정보는 그대로)
    await Until(()=>link.PlaySerial==serial+1,25,"같은 곡 재시작을 알아챔");
    result["restartNoticedAt"]=Math.Round(replay.Seconds,2);
    Check("같은 곡 재시작을 알아챔(판 번호가 오르고 곡은 그대로)",link.PlayKey==chartA.Key,replay.Seconds);
    if(afterRestart){
     Check("확인 대기 중 재시작: 기다리는 후보는 앞 판 그대로",session.Pending?.RawScore==expected&&session.Pending?.Chart.Key==selA.Key,session.Pending?.RawScore);
     Call(app,"Decide",true);
     Check("앞 판의 후보가 저장됨",Value(selA)==expected&&Count(selA)==1,$"{Value(selA)} {Count(selA)}");
    }
    await SongLocked(selA);
    Check("재시작 뒤에는 다음 곡을 기다리지 않음(다시 등록할 수 있음)",!status.Text.Contains("다음 곡을 기다리고 있습니다"),status.Text);
    await ToEnd();Press();
    var second=await Candidate();
    Check("둘째 판: 후보가 기대 점수와 같음",second?.RawScore==expected,second?.RawScore);
    if(second==null)return;
    Call(app,"Decide",true);
    Check("둘째 판의 저장: 같은 점수라 기존 기록 유지, 횟수 2",Value(selA)==expected&&Count(selA)==2&&status.Text=="기존 기록 유지",$"{Value(selA)} {Count(selA)} {status.Text}");
   }

   // 등록 요청 중에 곡이 바뀐다.
   async Task ChangeDuringRequest()
   {
    Begin(chartA);await At(15);await SongLocked(selA);
    Press();await Task.Delay(300);
    Check("요청이 열려 있음",Requesting()&&Stage()=="checking",Stage());
    var watch=Stopwatch.StartNew();
    app.Chart=chartB;
    await Until(()=>Stage()=="failure"||session.Pending!=null,7,"곡이 바뀐 요청의 답");
    result["answerAfterChange"]=Math.Round(watch.Elapsed.TotalSeconds,2);
    Check("곡이 바뀌면 받은 화면으로 바로 마침(기한 8초를 기다리지 않음)",watch.Elapsed.TotalSeconds<2.5,watch.Elapsed.TotalSeconds);
    Check("후보가 나오지 않음",session.Pending==null,session.Pending?.RawScore);
    Check("기록이 바뀌지 않음",Value(selA)==null&&Value(selB)==null);
    await Until(()=>!Requesting(),6,"실패 표시가 끝남");
    await Until(()=>link.PlayKey==chartB.Key,4,"다음 곡의 판");
    Check("그 뒤 다음 곡의 판이 열림",link.PlayKey==chartB.Key,link.PlayKey);
    await Task.Delay(1500);
    Check("끝난 요청이 뒤늦게 저장되지 않음",session.Pending==null&&Value(selA)==null&&Value(selB)==null);
   }

   // 등록 요청 중에 사용자가 취소한다.
   async Task CancelDuringRequest()
   {
    Begin(chartA);await At(15);await SongLocked(selA);
    Press();await Task.Delay(250);
    Check("요청이 열려 있음",Requesting(),Stage());
    Call(app,"Action",5,false);
    Check("취소하면 요청이 사라짐",!Requesting()&&Analysis()==null);
    await Task.Delay(9000);
    Check("취소한 요청은 뒤늦게 후보를 내지 않음",session.Pending==null&&Value(selA)==null,session.Pending?.RawScore);
    await ToEnd();await SongLocked(selA);Press();
    var candidate=await Candidate();
    Check("취소한 뒤에도 끝에서 등록할 수 있음",candidate?.RawScore==expected,candidate?.RawScore);
    if(candidate==null)return;
    Call(app,"Decide",false);
    Check("후보를 취소하면 기록이 바뀌지 않음",Value(selA)==null&&session.Pending==null,Value(selA));
    string? again=Try(Press);
    var second=again==null?await Candidate():null;
    Check("취소한 뒤 같은 판에서 다시 등록할 수 있음",second?.RawScore==expected,again??second?.RawScore?.ToString());
    if(second!=null){Call(app,"Decide",true);Check("확인하면 한 번 저장됨",Value(selA)==expected&&Count(selA)==1,$"{Value(selA)} {Count(selA)}");Call(app,"Decide",true);Check("확인을 한 번 더 눌러도 다시 저장되지 않음",Count(selA)==1,Count(selA));}
   }

   // 분석 중과 확인 대기 중의 프로필 조작(미리 열어 둔 창 포함).
   async Task Guards()
   {
    string firstId=store.Active.Id;store.Create("시험 둘째",false);store.Select(firstId);Call(app,"RefreshProfiles");
    string activeName=store.Active.Name;int count=store.Data.Profiles.Count;
    // 막을 까닭이 없을 때는 고르는 칸으로 실제로 전환된다(칸의 항목이 지금의 프로필 자료형이 아니면 조용히 아무 일도 일어나지 않는다)
    profiles.SelectedIndex=1-profiles.SelectedIndex;
    Check("등록 요청이 없을 때: 프로필 고르는 칸으로 전환됨",store.Active.Id!=firstId&&(profiles.SelectedItem as PlayProfile)?.Id==store.Active.Id,store.Active.Name);
    profiles.SelectedIndex=1-profiles.SelectedIndex;
    Check("다시 고르면 처음 프로필로 돌아옴",store.Active.Id==firstId,store.Active.Name);
    bool Same()=>store.Active.Id==firstId&&store.Active.Name==activeName&&store.Data.Profiles.Count==count;
    // 전환의 저장이 실패하면(기록 파일이 잠겨 있다) 고르는 칸이 실제 프로필로 돌아온다. 잠금이 풀린 뒤의 한 판은 보이는 프로필에 들어간다
    string recordsPath=(string)typeof(RecordStore).GetField("path",Flags)!.GetValue(store)!;
    using(new FileStream(recordsPath,FileMode.Open,FileAccess.Read,FileShare.None)){status.Text="";profiles.SelectedIndex=1-profiles.SelectedIndex;Check("전환의 저장이 실패하면 선택이 실제 프로필로 돌아옴",status.Text!=""&&Same()&&(profiles.SelectedItem as PlayProfile)?.Id==firstId,status.Text);}
    var probe=selA with{SongId="probe-profile-switch"};store.Add(probe,900000,DateTime.UtcNow,PlaySource.Manual);
    Check("잠금이 풀린 뒤의 한 판은 보이는 프로필에 들어감",(profiles.SelectedItem as PlayProfile)?.Id==store.Active.Id&&store.Active.Id==firstId&&store.Active.Plays.Any(p=>p.Key==probe.Key),store.Active.Name);
    store.Undo();status.Text="";
    void Blocked(string when)
    {
     foreach(string name in new[]{"새로","복사","이름 변경","삭제"}){
      int forms=Application.OpenForms.Count;status.Text="";
      // 점수 확인·저장 확인 대기 중에는 프로필 줄이 누를 수 없는 상태다. 누를 수 없거나, 눌렸다면 거절 안내가 떠야 한다
      var button=Find(name);bool pressable=button.Enabled;button.PerformClick();
      Check($"{when}: 프로필 '{name}'이 막힘(창도 뜨지 않음)",(!pressable||status.Text==Guard)&&Application.OpenForms.Count==forms&&FindNotice("프로필 삭제")==null&&Same(),$"누를 수 있음 {pressable} · {status.Text}");
     }
     status.Text="";
     profiles.SelectedIndex=1-profiles.SelectedIndex;
     Check($"{when}: 프로필 전환이 막히고 선택이 되돌아감",status.Text==Guard&&Same()&&(profiles.SelectedItem as PlayProfile)?.Id==firstId,status.Text);
     string? rescan=Try(()=>Call(app,"Action",3,false));
     Check($"{when}: '새로 감지하기'가 막힘",rescan!=null&&session.Running,rescan);
    }
    Begin(chartA);await At(12);await SongLocked(selA);
    Press();await Task.Delay(300);
    Check("분석 중임",Requesting()&&session.Pending==null,Stage());
    Blocked("분석 중");
    Call(app,"Action",5,false);
    // 미리 열어 둔 창: 창이 떠 있는 동안 등록이 시작되고, 그 뒤에 창에서 확인을 누른다
    void OpenedBefore(string button,string title,bool messageBox,string what,string? typed=null)
    {
     robot.AddLast(()=>{if(messageBox?FindNotice(title)==null:FindForm(title)==null)return false;Press();return true;});
     robot.AddLast(()=>{if(!Requesting())return false;if(messageBox)Answer(FindNotice(title)!,"삭제");else{var f=FindForm(title)!;Everything(f).OfType<TextBox>().First().Text=typed!;((Button)f.AcceptButton!).PerformClick();}return true;});
     status.Text="";
     Find(button).PerformClick();   // 창이 닫힐 때까지 여기서 기다린다(그동안 위의 두 단계가 돈다)
     Check(what,Same()&&status.Text==Guard,$"{store.Active.Name} · 프로필 {store.Data.Profiles.Count}개 · {status.Text}");
     Check(what+" (그때 등록 요청이 열려 있었음)",Requesting());
     Call(app,"Action",5,false);
    }
    OpenedBefore("이름 변경","프로필 이름 변경",false,"이름 변경 창을 연 뒤 등록이 시작되면, 확인을 눌러도 이름이 바뀌지 않음","바뀐 이름");
    OpenedBefore("삭제","프로필 삭제",true,"삭제 확인 창을 연 뒤 등록이 시작되면, 삭제를 눌러도 삭제되지 않음");
    OpenedBefore("새로","새 프로필",false,"새 프로필 창을 연 뒤 등록이 시작되면, 확인을 눌러도 만들어지지 않음","시험 셋째");
    OpenedBefore("복사","프로필 복사",false,"복사 창을 연 뒤 등록이 시작되면, 확인을 눌러도 만들어지지 않음","시험 넷째");
    await ToEnd();await SongLocked(selA);Press();
    var candidate=await Candidate();
    Check("후보가 기대 점수와 같음",candidate?.RawScore==expected,candidate?.RawScore);
    if(candidate==null)return;
    Blocked("확인 대기 중");
    Call(app,"Decide",true);
    Check("확인하면 요청 때의 프로필에 저장됨",Same()&&Value(selA)==expected,Value(selA));
    bool opened=false;
    robot.AddLast(()=>{var f=FindForm("프로필 이름 변경");if(f==null)return false;opened=true;((Button)f.CancelButton!).PerformClick();return true;});
    status.Text="";Find("이름 변경").PerformClick();   // 이제는 막히지 않는다: 창이 뜨고, 자동 조작이 취소를 누른다
    Check("확인이 끝난 뒤에는 이름 변경 창이 다시 열림",opened&&Same()&&status.Text!=Guard,status.Text);
    await Task.Delay(100);
   }

   // 확인을 기다리는 동안 같은 대상의 기록이 바뀐다(수동 기록, 되돌리기, 가져오기).
   async Task RecordsDuringPending()
   {
    if(await FirstCandidate()==null)return;
    store.SetManual(selA,90.00m,3,rawScore:900000);
    await Task.Delay(500);
    Check("확인 대기 중 수동 기록: 반영되고 대기는 유지됨",Value(selA)==900000&&session.Pending?.RawScore==expected,Value(selA));
    var shown=Overlay();
    Check("오버레이의 확인 화면에 보이는 기존 기록이 바뀐 값(90.00%)",shown.GetProperty("confirmation").GetProperty("previous").GetDecimal()==90.00m,shown.GetProperty("confirmation").GetProperty("previous").ToString());
    store.Undo();
    Check("확인 대기 중 되돌리기: 기록이 없어지고 대기는 유지됨",Value(selA)==null&&session.Pending!=null,Value(selA));
    int imported=store.Import(new Dictionary<Selection,decimal>{[selA]=99.50m});
    Check("확인 대기 중 가져오기(99.50%): 반영됨",imported==1&&Value(selA)==995000,Value(selA));
    Call(app,"Decide",true);
    Check("확인: 저장 순간의 기록(995,000)이 더 높아 기존 기록 유지, 가져온 한 줄 옆에 이번 판이 남아 판 수 2",Value(selA)==995000&&Count(selA)==2&&status.Text=="기존 기록 유지",$"{Value(selA)} {Count(selA)} {status.Text}");
    Check("그 낮은 판은 후보의 점수 그대로(1점 자리까지) 등록 출처의 새 줄로 남음",store.Active.Plays[^1] is {Source:PlaySource.Play} keptLow&&keptLow.Score==expected&&keptLow.Key==selA.Key,store.Active.Plays[^1].ToString());
    int serial=link.PlaySerial;
    replay.Start();
    await Until(()=>link.PlaySerial==serial+1,25,"같은 곡 재시작을 알아챔");
    await SongLocked(selA);await ToEnd();Press();
    var second=await Candidate();
    Check("둘째 판: 후보가 기대 점수와 같음",second?.RawScore==expected,second?.RawScore);
    if(second==null)return;
    store.Wipe(selA);store.SetManual(selA,90.00m,3,rawScore:900000);   // 후보가 뜬 뒤에 기록이 낮아진다: 그 판정의 판을 모두 지우고 900,000 의 한 판을 넣는다
    Call(app,"Decide",true);
    Check("확인: 저장 순간의 기록(900,000)보다 높아 기록 갱신, 판 수 2",Value(selA)==expected&&Count(selA)==2&&status.Text=="기록 갱신",$"{Value(selA)} {Count(selA)} {status.Text}");
    store.Undo();
    Check("직전 저장 되돌리기: 확인 전의 기록(900,000, 판 수 1)으로 돌아감",Value(selA)==900000&&Count(selA)==1,$"{Value(selA)} {Count(selA)}");
   }

   // 미리 열어 둔 수정·삭제·되돌리기 창이 오래된 값을 조용히 덮어쓰지 않는가.
   async Task StaleDialogs()
   {
    Begin(chartA);await ToEnd();await SongLocked(selA);
    // 수동 기록 창을 먼저 연다 → 그동안 등록이 저장된다 → 그 창에서 저장을 누른다
    int? seenAtFirstSave=null;string? shownAfter=null;bool warned=false;
    robot.AddLast(()=>{if(FindForm("수동 기록 설정")==null)return false;Press();return true;});
    robot.AddLast(()=>{if(session.Pending==null)return false;Call(app,"Decide",true);return true;});
    robot.AddLast(()=>{var f=(RecordDialog)FindForm("수동 기록 설정")!;f.Input.ScoreBox.Text="123400";f.SaveButton.PerformClick();return true;});   // 알림이 뜨면 이 단계는 알림이 닫힐 때까지 돌아오지 않는다
    robot.AddLast(()=>{var box=FindNotice("수동 기록 설정");if(box==null)return FindForm("수동 기록 설정")==null;warned=true;seenAtFirstSave=Value(selA);Dismiss(box);return true;});
    robot.AddLast(()=>{var f=FindForm("수동 기록 설정") as RecordDialog;if(f==null)return true;if(FindNotice("수동 기록 설정")!=null)return false;shownAfter=f.BestShown;f.SaveButton.PerformClick();return true;});
    Find("수동 기록 설정").PerformClick();
    Check("수동 기록 창을 연 뒤 등록이 저장되면, 그 창의 첫 저장은 알림과 함께 막힘(기록은 등록된 값 그대로)",warned&&seenAtFirstSave==expected,$"알림 {warned}, 그때의 기록 {seenAtFirstSave}");
    Check("그 창의 '현재 최고 기록'이 바뀐 값으로 고쳐짐",shownAfter!=null&&shownAfter.StartsWith($"{expected:N0}점"),shownAfter);
    Check("저장을 한 번 더 누르면 입력한 낮은 값(123,400)이 수동 출처의 한 판으로 저장됨(최고 기록은 등록된 값 그대로)",Value(selA)==expected&&Count(selA)==2&&store.Active.Plays[^1] is {Score:123400,Source:PlaySource.Manual} typed&&typed.Key==selA.Key,$"{Value(selA)} {Count(selA)} {store.Active.Plays[^1]}");
    // 되돌리기 확인 창을 연 뒤에 다른 저장이 들어온다
    robot.AddLast(()=>{var box=FindForm("직전 저장 되돌리기");if(box==null)return false;store.SetManual(selB,50.00m,1,rawScore:500000);Everything(box).OfType<Button>().First(b=>b.Text=="되돌리기").PerformClick();return true;});
    status.Text="";Find("직전 저장 되돌리기").PerformClick();
    Check("되돌리기 확인 창을 연 뒤 기록이 바뀌면, 되돌리기를 눌러도 되돌리지 않음",Value(selA)==expected&&Value(selB)==500000&&status.Text.StartsWith("확인하는 동안 직전 저장이 바뀌었습니다"),$"{Value(selA)} {Value(selB)} {status.Text}");
    // 히스토리에서 한 판의 삭제를 확인하는 동안 기록이 바뀐다(삭제는 목록이 아니라 히스토리에서 한 판씩 한다. 확인 창이 보여 준 그 판이 승인할 때도 그대로여야 지운다).
    // 알림은 창을 띄우지 않고 무엇을 알렸는지만 받는다(PlayNotice.Hook)
    using(var list=new RecordsDialog(catalog,store)){
     list.Show();Application.DoEvents();
     var grid=(RecordsView)typeof(RecordsDialog).GetField("grid",Flags)!.GetValue(list)!;
     grid.SelectIndex(Enumerable.Range(0,grid.RowCount).First(i=>grid.ItemAt(i)!.Chart.Key==selA.Key));list.OpenHistory();Application.DoEvents();
     var history=list.History!;var told=new List<string>();PlayNotice.Hook=(_,headline,_)=>told.Add(headline);
     try{
      Check("목록에서 히스토리가 열리고 주 화면을 막지 않음",history.Visible&&app.Enabled&&history.Table.Rows.Count==2,history.Table.Rows.Count);
      var low=store.History(selA.Key).Single(p=>p.Score==123400);history.Pick(low.Seq);
      bool deleted=history.DeleteSelected(request=>{store.Edit(low.Seq,130000);return request.Candidates[0];});
      Check("삭제를 확인하는 동안 그 판의 점수가 바뀌면 지우지 않고 알림",!deleted&&told.LastOrDefault()=="기록이 바뀌어 삭제를 멈췄습니다."&&Value(selA)==expected&&Count(selA)==2&&store.History(selA.Key).Any(p=>p.Seq==low.Seq&&p.Score==130000),$"{deleted} {told.LastOrDefault()} {Count(selA)}");
      // 삭제는 고른 한 판만 지우므로, 확인하는 동안 다른 판이 더해진 것은 그 판과 상관없다(고른 판이 그대로면 멈추지 않는다)
      history.Pick(low.Seq);
      deleted=history.DeleteSelected(request=>{store.SetManual(selA,77.77m,9,rawScore:777700);return request.Candidates[0];});
      Check("확인하는 동안 다른 판이 더해져도 고른 판이 그대로면 그 판만 지움",deleted&&Value(selA)==expected&&Count(selA)==2&&store.History(selA.Key).All(p=>p.Seq!=low.Seq)&&store.History(selA.Key).Any(p=>p.Score==777700),$"{deleted} {Value(selA)} {Count(selA)}");
      var other=store.History(selA.Key).Single(p=>p.Score==777700);history.Pick(other.Seq);
      deleted=history.DeleteSelected(request=>{store.Delete(other.Seq);return request.Candidates[0];});
      Check("확인하는 동안 그 판이 먼저 지워지면 알리고 목록으로 돌아감",!deleted&&told.LastOrDefault()=="선택한 기록이 이미 삭제되었습니다."&&history.ReturnedToList&&list.History==null&&Value(selA)==expected&&Count(selA)==1,$"{deleted} {told.LastOrDefault()} {Count(selA)}");
     }finally{PlayNotice.Hook=null;}
     list.Close();
    }
    await Task.Delay(100);
   }

   Func<Task> play=scenario switch
   {
    "pending-next-confirm"=>()=>PendingNext(true),"pending-next-cancel"=>()=>PendingNext(false),
    "saved-then-restart"=>()=>Restart(false),"pending-restart"=>()=>Restart(true),
    "change-during-request"=>ChangeDuringRequest,"cancel-during-request"=>CancelDuringRequest,
    "guards"=>Guards,"records-during-pending"=>RecordsDuringPending,"stale-dialogs"=>StaleDialogs,
    _=>throw new Exception("모르는 각본: "+scenario),
   };
   using var expiry=new System.Windows.Forms.Timer(){Interval=(int)((2*(Lead+10)+60)*1000)};
   expiry.Tick+=(_,_)=>{failures.Add("시험 전체의 시간 한도를 넘김");app.Close();};
   app.Shown+=async(_,_)=>{
    try{expiry.Start();await play();}
    catch(Exception e){failures.Add("오류: "+(e.InnerException??e).Message);}
    finally{result["status"]=status.Text;result["scoreStatus"]=Get<Label>(app,"scoreStatus").Text;result["scoreAdvice"]=Get<Label>(app,"scoreAdvice").Text;app.Close();}
   };
   Application.Run(app);
   if(Directory.Exists(log)){
    var lines=new List<string>();long skipped=0;
    foreach(var file in new DirectoryInfo(log).EnumerateFiles("score-*.log").OrderBy(f=>f.Name))
     foreach(string line in File.ReadAllText(file.FullName).Split('\n',StringSplitOptions.RemoveEmptyEntries)){long size=Encoding.UTF8.GetByteCount(line)+1;if(skipped+size<=logFrom){skipped+=size;continue;}lines.Add(line);}
    result["diagnostics"]=lines.Select(l=>JsonSerializer.Deserialize<JsonElement>(l)).ToList();
   }
  }catch(Exception e){failures.Add("오류: "+(e.InnerException??e).Message);}
  result["checks"]=checks;result["failures"]=failures;result["notes"]=notes;result["passed"]=failures.Count==0&&checks.Count>0;
  File.WriteAllText(output,JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));
 }
}
#endif
