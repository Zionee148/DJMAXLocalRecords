using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using DjmaxScore;
namespace LadderRecords;
// 단축키가 지금의 SCORE 등록 흐름으로 이어지는지 본다(2026-11-16 에 다시 썼다. 전에는 옛 RATE 등록을 기대했다).
//
// 두 가지로 돌린다. 확인하는 것은 같고, 단축키를 누가 누르느냐가 다르다.
//   --hotkey-flow-check   자동 검사. 단축키가 눌렸을 때 Windows 가 창에 보내는 메시지(WM_HOTKEY)를 검사가 직접 보낸다. 몇 초면 끝난다(30초가 지나면 시간 초과).
//   --hotkey-probe        사람용 도구. 창 제목에 나오는 키(Ctrl+Alt+Shift+F1~F5)를 사람이 실제로 누른다. 단계마다 2분, 모두 10분 안에 누르지 않으면 시간 초과로 끝난다.
//
// 차례(자동): 감지 시작 → 등록(분석 시작) → 저장 취소(분석을 취소) → 등록(후보) → 저장 취소(후보를 취소) → 등록(후보) → 저장 확인(저장) → 창 핸들을 다시 만든 뒤 새로 감지하기
// 차례(사람): 감지 시작 → 등록(후보) → 저장 취소 → 등록(후보) → 저장 확인 → 새로 감지하기   (분석 중 취소는 사람이 맞춰 누르기 어려워 자동 검사만 본다)
//
// 등록 키가 여는 등록 요청은 앱이 실제로 만든 것이다(주 화면의 RequestRegistration 이 SCORE 모듈에 요청을 연다).
// 실제 게임 화면이 없으면 모듈은 "점수 위치를 아직 찾지 못했습니다"로 곧 답한다. 그래서 후보가 필요한 단계에서는,
// 앱이 등록 키를 처리한 바로 그 자리에서(모듈의 답이 화면 스레드로 넘어오기 전에) 그 요청에 대한 답("수락, 987,117점")을 검사가 만들어 넣는다.
// 이 검사가 보지 않는 것: 화면에서 점수를 읽는 것(녹화로 하는 분기 시험과 실제 게임이 본다),
// 자동 검사에서는 실제 키 입력(Windows 가 전역 단축키로 받아 주는 것, 게임이 앞에 있을 때)도 보지 않는다. 그것은 --hotkey-probe 나 실제 사용으로 본다.
//
// 자료: 필요 없다. 기록은 실행 파일 옆의 test-data 아래 임시 파일에 쓴다(실행 파일 옆의 records.json 은 건드리지 않는다).
// 결과: hotkey-flow-validation.json (passed, mode, steps, 실패하면 failed_step 과 error)
[System.Runtime.Versioning.SupportedOSPlatform("windows10.0.19041")]
static class HotkeyFlowChecks
{
 [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr h,int message,IntPtr w,IntPtr l);
 const BindingFlags Flags=BindingFlags.NonPublic|BindingFlags.Instance;
 const int Candidate=987117;
 const int Port=18769;
 const string Checking="점수를 확인하고 있습니다…";
 // 시험용 키: 기본 키(Ctrl+F8~F12)와 겹치지 않게 Ctrl+Alt+Shift+F1~F5. 차례는 Hotkeys.Defaults() 와 같다(등록, 새로 감지하기, 저장 확인, 저장 취소, 감지 시작)
 static readonly List<Hotkey> KeysForTest=Enumerable.Range(0,5).Select(i=>new Hotkey(Hotkeys.Defaults()[i].Name,Keys.F1+i,7)).ToList();
 class Probe:MainForm
 {
  internal readonly SteamChart Chart=new("FREESTYLE","Fundamental",8,"SC");
  internal Action<int>? AfterHotkey;   // 앱이 단축키 메시지를 처리한 바로 뒤에 불린다(단축키 번호: 0 등록, 1 새로 감지하기, 2 저장 확인, 3 저장 취소, 4 감지 시작)
  public Probe(Catalog catalog,string preferences):base(catalog,Port,true,preferences,KeysForTest){}
  protected override SteamSnapshot ReadSteamSnapshot()=>new(true,Chart,"시험용 곡 정보");
  protected override void WndProc(ref Message m){base.WndProc(ref m);if(m.Msg==0x0312)AfterHotkey?.Invoke(m.WParam.ToInt32()-102);}
  public void RecreateForTest()=>RecreateHandle();
 }
 sealed class StepFailed:Exception{public string Step{get;}public StepFailed(string step,string message):base($"{step}: {message}"){Step=step;}}

 public static void Run(Catalog catalog,bool automatic=false)
 {
  string folder=Path.Combine(AppContext.BaseDirectory,"test-data",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
  string resultPath=Path.Combine(AppContext.BaseDirectory,"hotkey-flow-validation.json");File.Delete(resultPath);
  string beside=Path.Combine(AppContext.BaseDirectory,"records.json");DateTime? besideAt=File.Exists(beside)?File.GetLastWriteTimeUtc(beside):null;
  using var app=CheckPorts.Open(Port,()=>new Probe(catalog,Path.Combine(folder,"preferences.json")));
  T Field<T>(string name)=>(T)typeof(MainForm).GetField(name,Flags)!.GetValue(app)!;
  object? Peek(string name)=>typeof(MainForm).GetField(name,Flags)!.GetValue(app);
  void Put(string name,object? value)=>typeof(MainForm).GetField(name,Flags)!.SetValue(app,value);
  void Call(string name,params object[] values)=>typeof(MainForm).GetMethod(name,Flags)!.Invoke(app,values);
  var store=new RecordStore(Path.Combine(folder,"records.json"));Put("store",store);Call("RefreshProfiles");
  var session=Field<Session>("session");var status=Field<Label>("status");var link=Field<ScoreLink>("scoreLink");var bindings=Field<Hotkeys>("hotkeys");
  var chart=new Selection(catalog.Songs.Single(s=>s.Name=="Fundamental").Id,8,"SC","노말",0);
  var steps=new List<string>();string current="준비";Exception? error=null,hookError=null;bool done=false;
  int Registered()=>((System.Collections.ICollection)typeof(Hotkeys).GetField("registered",Flags)!.GetValue(bindings)!).Count;
  IntPtr Bound()=>(IntPtr)typeof(Hotkeys).GetField("handle",Flags)!.GetValue(bindings)!;
  // 앱이 스스로 화면을 읽으러 가지 않게 한다(게임 창이 없으면 오류가 나고, 게임이 켜져 있으면 그 화면을 읽어 곡 고정을 바꿀 수 있다):
  // 게임 창 고르기를 비우고, 곡 감지가 "읽는 중"이라고 여겨 건너뛰게 하고, 감지가 시작되면 두 타이머를 세운다
  void HoldDetection(){Field<ComboBox>("windows").SelectedIndex=-1;Put("busy",true);Field<System.Windows.Forms.Timer>("timer").Stop();Field<System.Windows.Forms.Timer>("captureTimer").Stop();}
  async Task Until(Func<bool> reached,string what,int? milliseconds=null)
  {
   int limit=milliseconds??(automatic?10000:120000);var watch=Stopwatch.StartNew();
   while(true){
    if(hookError!=null)throw new StepFailed(current,(hookError as TargetInvocationException)?.InnerException?.Message??hookError.Message);
    if(reached())return;
    if(watch.ElapsedMilliseconds>limit)throw new StepFailed(current,$"{limit/1000}초 안에 일어나지 않음: {what} (상태 글: {status.Text})");
    await Task.Delay(30);HoldDetection();
   }
  }
  // 단축키 하나. 자동이면 Windows 가 보내는 메시지를 창에 직접 보내고(앱이 처리하고 나서 돌아온다), 사람용이면 창 제목에 누를 키를 적고 기다린다
  void Press(int index)
  {
   var key=KeysForTest[index];
   if(automatic)SendMessage(app.Handle,0x0312,(IntPtr)(102+index),IntPtr.Zero);
   else app.Text=$"단축키 시험 · {current} · 지금 누를 키: {Hotkeys.Format(key)} ({key.Name})";
  }
  // 등록 키가 처리된 바로 뒤에 본 것. answerNext 가 켜져 있으면 그 자리에서 모듈의 답을 만들어 넣는다
  bool answerNext=false,opened=false;string? statusAtRegister=null;
  void Answer()
  {
   var request=Peek("activeRegistration")!;
   Call("OnScoreAnswer",request,new ScoreAnswer(1,link.PlaySerial,app.Chart.Key,new RegistrationResult(1,1,RegistrationStatus.Accepted,"",Candidate,Candidate,null,null,0,0,0),0,0));
   link.CancelRequest();   // 모듈에 열려 있던 요청은 닫는다. 모듈이 뒤늦게 내는 답은 요청이 달라 버려진다
  }
  app.AfterHotkey=index=>{
   if(index!=0)return;
   try{
    opened=Peek("activeRegistration")!=null;statusAtRegister=status.Text;
    if(!answerNext)return;
    answerNext=false;
    if(!opened||statusAtRegister!=Checking)throw new StepFailed(current,$"등록 키를 눌렀는데 등록 요청이 열리지 않았거나 분석 중이 아님(상태 글: {statusAtRegister})");
    Answer();
   }catch(Exception e){hookError=e;}
  };
  void Lock(){session.Detect(chart);Put("steamChart",app.Chart);link.EnsurePlay(app.Chart.Key,GearPosition.Left,"단축키 시험");Call("RefreshState");}
  void Pass(string what){steps.Add("PASS: "+what);}
  void Save(bool passed,string? failedStep=null,string? message=null)=>File.WriteAllText(resultPath,JsonSerializer.Serialize(new{passed,mode=automatic?"자동(단축키 메시지를 창에 보냄)":"사람(실제 키 입력)",steps,failed_step=failedStep,error=message,
   keys=KeysForTest.Select(k=>$"{k.Name} = {Hotkeys.Format(k)}")},new JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));
  // 등록 키 → (그 자리에서 답) → 후보가 확인을 기다림
  async Task Offer(string step)
  {
   current=step;Lock();statusAtRegister=null;answerNext=true;Press(0);
   await Until(()=>session.Pending!=null,"후보가 확인을 기다림");
   if(session.Pending is not {RawScore:Candidate,Accuracy:98.71m}||session.Pending.Chart.Key!=chart.Key||Peek("activeRegistration")!=null||status.Text!="기록 등록 확인 대기")
    throw new StepFailed(current,$"후보가 다름: {session.Pending} (상태 글: {status.Text})");
  }

  async Task Flow()
  {
   current="준비";
   if(Registered()!=5||Bound()!=app.Handle)throw new CheckUnavailableException($"시험용 전역 단축키(Ctrl+Alt+Shift+F1~F5) 다섯 개를 Windows 에 등록하지 못했다(등록된 것 {Registered()}개). 다른 프로그램이 그 키를 쓰고 있거나 이 검사가 이미 돌고 있다.");
   if(Field<CheckBox>("fullscreen").Checked||!ReferenceEquals(Field<RecordStore>("store"),store))throw new StepFailed(current,"전제가 어긋남: 전체화면 사용이 꺼져 있고 주 화면이 임시 기록 파일을 써야 한다");
   HoldDetection();

   current="감지 시작";Press(4);
   await Until(()=>session.Running,"감지가 시작됨");HoldDetection();
   Pass("감지 시작 키로 감지가 시작됨");

   if(automatic){
    current="등록(분석 시작)";Lock();statusAtRegister=null;Press(0);
    if(!opened||statusAtRegister!=Checking||session.Pending!=null)throw new StepFailed(current,$"등록 키를 눌렀는데 SCORE 등록 요청이 열리지 않았거나 분석 중이 아님(상태 글: {statusAtRegister})");
    Pass("등록 키로 SCORE 등록 요청이 열리고 분석 중이 됨");
    current="저장 취소(분석 취소)";Press(3);
    if(Peek("activeRegistration")!=null||session.Pending!=null||store.Get(chart)!=null||!session.Running)throw new StepFailed(current,"분석을 취소했는데 요청이 남았거나, 후보·기록이 생겼거나, 감지가 멈춤");
    Pass("분석 중에 저장 취소 키로 분석이 취소됨(후보·기록 없음, 감지는 계속)");
   }

   await Offer("등록(후보)");
   Pass(automatic?"취소한 직후에 등록 키로 요청이 다시 열리고, 모듈의 답(987,117점)이 오면 후보가 저장 확인을 기다림":"등록 키로 SCORE 등록 요청이 열리고, 모듈의 답(987,117점)이 오면 후보가 저장 확인을 기다림");

   current="저장 취소(후보 취소)";long revision=store.Revision;Press(3);
   await Until(()=>session.Pending==null,"후보가 취소됨");
   if(store.Get(chart)!=null||store.Revision!=revision||status.Text!="등록 취소")throw new StepFailed(current,$"취소했는데 기록이 생겼거나 상태 글이 다름({status.Text})");
   Pass("저장 취소 키로 후보가 취소됨(기록 없음)");

   await Offer("등록(저장할 후보)");
   Pass("취소한 뒤 등록 키로 다시 후보가 나옴");

   current="저장 확인";Press(2);
   await Until(()=>session.Pending==null,"저장 확인이 끝남");
   if(store.Get(chart) is not {Value:Candidate,Accuracy:98.71m,PlayCount:1})throw new StepFailed(current,$"저장된 기록이 다름: {store.Get(chart)} (상태 글: {status.Text})");
   if(new RecordStore(Path.Combine(folder,"records.json")).Get(chart) is not {Value:Candidate})throw new StepFailed(current,"기록 파일에 쓰이지 않음");
   Pass("저장 확인 키로 후보가 저장됨(987,117 · 98.71% · 횟수 1, 파일에도 쓰임)");

   current="새로 감지하기(창 핸들을 다시 만든 뒤)";Lock();
   IntPtr before=app.Handle;app.RecreateForTest();Application.DoEvents();
   if(app.Handle==before||Registered()!=5||Bound()!=app.Handle)throw new StepFailed(current,$"창 핸들을 다시 만든 뒤 단축키가 새 창에 다시 등록되지 않음(등록된 것 {Registered()}개)");
   Press(1);
   await Until(()=>session.Locked==null&&Peek("steamChart")==null,"곡 고정이 풀리고 새로 감지함");HoldDetection();
   if(!session.Running)throw new StepFailed(current,"새로 감지하기 뒤에 감지가 멈춤");
   Pass("창 핸들을 다시 만든 뒤에도 새로 감지하기 키가 들음(단축키 다섯 개가 새 창에 다시 등록됨)");

   current="마무리";
   if((File.Exists(beside)?File.GetLastWriteTimeUtc(beside):(DateTime?)null)!=besideAt)throw new StepFailed(current,"실행 파일 옆의 records.json 을 건드림");
   Pass("실행 파일 옆의 기록 파일은 건드리지 않음");
  }

  using var start=new System.Windows.Forms.Timer(){Interval=200};using var expiry=new System.Windows.Forms.Timer(){Interval=automatic?30000:600000};
  start.Tick+=async(_,_)=>{start.Stop();try{await Flow();done=true;}catch(Exception e){error=e;}finally{expiry.Stop();app.Close();}};
  expiry.Tick+=(_,_)=>{expiry.Stop();error??=new StepFailed(current,automatic?"30초 안에 끝나지 않음(시간 초과)":"10분 안에 끝까지 누르지 않음(시간 초과)");app.Close();};
  if(!automatic)app.Text="단축키 시험 · 준비 중";
  start.Start();expiry.Start();Application.Run(app);
  if(!done&&error==null)error=new StepFailed(current,"끝까지 가지 못하고 창이 닫힘");
  Save(error==null,(error as StepFailed)?.Step,error?.Message);
  if(error!=null)throw error;
 }
}
