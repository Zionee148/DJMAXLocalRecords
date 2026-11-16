#if DEVELOPMENT_CHECKS
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
namespace LadderRecords;

// 시험용 주 화면에 녹화를 실제 시간으로 넣는다 (기획서 v3 완료 기준 1의 "마지막 앱 경로 시험").
//
// 주 화면(MainForm)을 그대로 띄우고 두 가지만 바꿔 끼운다: 게임 창 캡처 대신 녹화의 "지금 화면", Steam 곡 정보 대신 정해 둔 곡.
// 타이머와 캡처 주기, 곡 인식 흐름(RATE·판정), SCORE 연결, 등록 요청, 확인·취소·저장은 앱의 코드가 그대로 돈다.
// 실제 게임 창 캡처, 실제 Steam, 전역 단축키는 지나가지 않는다(그것은 실제 게임 시험에서 본다).
//
// 한 번 실행에 사례 하나를 녹화 길이만큼 걸려 돌린다. 기록·설정·진단 기록은 실행 파일 옆에 생기므로, 개인 기록이 없는 폴더에서만 돌린다.
// 여러 개를 함께 돌릴 때는 빌드 결과 폴더를 복사해 폴더마다 하나씩 돌리고 포트를 달리 준다.
//
// 실행: DJMAXLocalRecords.exe --score-replay-check <cases.tsv> <사례 이름> <결과.json> [포트] [각본]
//   각본(안 주면 cancel): cancel = 후보가 나오면 취소, confirm = 후보가 나오면 저장하고 저장된 기록을 확인,
//                         early = 시작 3초 뒤(자리가 고정되기 전)에 등록 키, thin = 자리가 고정되고 1초 뒤(플레이 중 근거가 모자랄 때)에 등록 키,
//                         images = 진단 그림 저장을 켜고 cancel 과 같이(남은 그림을 확인), early-images = 진단 그림을 켜고 early 와 같이,
//                         late:<초> = 녹화의 키 시각보다 그만큼 늦게 등록 키(점수가 곧 사라지는 때. 후보가 나오면 취소)
//       DJMAXLocalRecords.exe --score-diagnostics-check <결과.json>   (진단 기록의 보관량 제한, 해상도 안내, 진단 기록 줄을 본다. 녹화가 필요 없다)
[System.Runtime.Versioning.SupportedOSPlatform("windows10.0.19041")]
static partial class ScoreReplayChecks
{
 const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
 const int Fps=30,Width=1920,Height=1080;
 static readonly CultureInfo Inv=CultureInfo.InvariantCulture;

 // 녹화를 실제 시간에 맞춰 풀어 "지금 화면"을 들고 있는다(초당 30장. 앱은 0.1초마다 캡처한다).
 sealed class Replay:IDisposable
 {
  readonly string source;readonly double start,length;
  readonly byte[] current=new byte[Width*Height*4];readonly object gate=new();
  Process? ffmpeg;Thread? reader;Stopwatch clock=new();volatile bool stop;
  public volatile int Frames;public volatile bool Ended;
  public double Seconds=>clock.Elapsed.TotalSeconds;
  public Replay(string source,double start,double length){this.source=source;this.start=start;this.length=length;}
  // 처음부터 튼다. 이미 틀고 있었으면 멈추고 처음부터 다시 튼다(시계도 0 부터).
  public void Start()
  {
   Stop();
   var info=new ProcessStartInfo("ffmpeg"){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true};
   foreach(string a in new[]{"-nostdin","-hide_banner","-loglevel","error","-ss",start.ToString("R",Inv),"-t",length.ToString("0.0##",Inv),"-i",source,
    "-an","-vf",$"fps={Fps},scale={Width}:{Height}","-f","rawvideo","-pix_fmt","bgra","-"})info.ArgumentList.Add(a);
   var process=Process.Start(info)??throw new Exception("ffmpeg 을 시작하지 못했습니다.");
   process.ErrorDataReceived+=(_,_)=>{};process.BeginErrorReadLine();
   var watch=Stopwatch.StartNew();
   ffmpeg=process;clock=watch;stop=false;Ended=false;Frames=0;
   reader=new Thread(()=>Pump(process,watch)){IsBackground=true};reader.Start();
  }
  void Stop()
  {
   stop=true;
   try{if(ffmpeg is {HasExited:false})ffmpeg.Kill();}catch(Exception){}
   reader?.Join(3000);ffmpeg?.Dispose();ffmpeg=null;reader=null;
  }
  void Pump(Process process,Stopwatch watch)
  {
   try{
    var stream=process.StandardOutput.BaseStream;var buffer=new byte[current.Length];
    for(int k=0;!stop;k++){
     int got=0;
     while(got<buffer.Length){int n=stream.Read(buffer,got,buffer.Length-got);if(n<=0){Ended=true;return;}got+=n;}
     double due=(double)k/Fps;   // 이 화면이 보이기 시작하는 시각
     while(!stop&&watch.Elapsed.TotalSeconds<due)Thread.Sleep(1);
     if(stop)return;
     lock(gate)Buffer.BlockCopy(buffer,0,current,0,buffer.Length);
     Frames=k+1;
    }
   }catch(Exception){Ended=true;}
  }
  public Bitmap Capture()
  {
   var image=new Bitmap(Width,Height,PixelFormat.Format32bppArgb);
   var data=image.LockBits(new Rectangle(0,0,Width,Height),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
   try{lock(gate)Marshal.Copy(current,0,data.Scan0,current.Length);}finally{image.UnlockBits(data);}
   return image;
  }
  public void Dispose()=>Stop();
 }

 sealed class Probe:MainForm
 {
  readonly Replay replay;
  public SteamChart? Chart;
  public int Captures;
  public Probe(Catalog catalog,int port,string preferences,Replay replay):base(catalog,port,false,preferences){this.replay=replay;}
  protected override bool ShowWithoutActivation=>true;
  protected override Bitmap CaptureGameFrame(IntPtr handle){Interlocked.Increment(ref Captures);return replay.Capture();}
  protected override SteamSnapshot ReadSteamSnapshot()=>new(true,Chart,"녹화 재생 시험");
 }

 static T Get<T>(object obj,string key)=>(T)typeof(MainForm).GetField(key,Flags)!.GetValue(obj)!;

 // 진단 기록의 보관량 제한(작은 한도로), 해상도 안내, 진단 기록 줄을 본다.
 internal static void Diagnostics(Catalog catalog,string output)
 {
  var result=new Dictionary<string,object?>();var failures=new List<string>();
  void Check(string what,bool ok,object? got=null){result[what]=got??ok;if(!ok)failures.Add(what);}
  string root=Path.Combine(AppContext.BaseDirectory,"diagnostics-check");if(Directory.Exists(root))Directory.Delete(root,true);Directory.CreateDirectory(root);
  try{
   // 글자 기록: 파일 하나 4KB, 합계 16KB 로 줄여 800줄을 쓴다
   string text=Path.Combine(root,"text");Directory.CreateDirectory(text);
   File.WriteAllText(Path.Combine(text,"records.json"),"남의 파일");File.WriteAllText(Path.Combine(text,"score-notes.log"),"이름이 비슷한 남의 파일");
   var log=new DjmaxScore.ScoreDiagnostics(text,null,4096,16384);
   for(int i=0;i<800;i++)log.Write("answer",new{request=i,state="Accepted",reason="",score=991115,note="한글이 들어간 줄"});
   var ours=new DirectoryInfo(text).EnumerateFiles("score-????.log").OrderBy(f=>f.Name).ToList();
   Check("글자 기록: 오류 없음",log.LastError==null,log.LastError);
   Check("글자 기록: 합계가 한도(16KB) 안",ours.Sum(f=>f.Length)<=16384,ours.Sum(f=>f.Length));
   Check("글자 기록: 파일 하나가 한도(4KB) 안",ours.All(f=>f.Length<=4096),ours.Max(f=>f.Length));
   Check("글자 기록: 오래된 파일이 지워지고 새 파일이 남음",ours.Count>=2&&!File.Exists(Path.Combine(text,"score-0001.log")),string.Join(",",ours.Select(f=>f.Name)));
   Check("글자 기록: 마지막 줄이 가장 최근 것",File.ReadAllLines(ours[^1].FullName)[^1].Contains("\"request\":799"));
   Check("글자 기록: 남의 파일은 그대로",File.ReadAllText(Path.Combine(text,"records.json"))=="남의 파일"&&File.Exists(Path.Combine(text,"score-notes.log")));
   // 그림: 합계 300KB 로 줄여 요청 여섯 개의 그림을 남긴다
   string pictures=Path.Combine(root,"images");Directory.CreateDirectory(Path.Combine(pictures,"내 폴더"));File.WriteAllText(Path.Combine(pictures,"내 폴더","keep.png"),"남의 파일");
   var imageLog=new ScoreImageLog(pictures,300*1024);var random=new Random(7);
   for(int request=1;request<=6;request++){
    string name=ScoreImageLog.RequestFolder(new DateTime(2026,11,14,7,0,request),request);
    for(int k=1;k<=4;k++){var bytes=new byte[200*60*4];random.NextBytes(bytes);imageLog.SaveBand(name,k,bytes,200,60,800);}   // 잡음 그림: 장당 36KB 쯤
   }
   imageLog.Flush().Wait(20000);
   var folders=new DirectoryInfo(pictures).EnumerateDirectories("*-request-*").OrderBy(d=>d.Name).ToList();
   long size=folders.Sum(d=>d.EnumerateFiles().Sum(f=>f.Length));
   Check("그림: 오류 없음",imageLog.LastError==null,imageLog.LastError);
   Check("그림: 합계가 한도(300KB) 안",size<=300*1024,size);
   Check("그림: 오래된 요청이 지워지고 최근 요청이 남음",folders.Count is >=1 and <6&&folders[^1].Name.EndsWith("request-0006")&&!folders.Any(d=>d.Name.EndsWith("request-0001")),string.Join(",",folders.Select(d=>d.Name)));
   Check("그림: 남의 폴더는 그대로",File.Exists(Path.Combine(pictures,"내 폴더","keep.png")));
   // 주 화면: 진단 줄, 그림 옵션의 기본값, 해상도 안내
   string preferences=Path.Combine(root,"options.json");
   using(var app=new MainForm(catalog,18795,false,preferences){ShowInTaskbar=false,Opacity=0}){
    app.Show();Application.DoEvents();
    IEnumerable<Control> All(Control c)=>c.Controls.Cast<Control>().SelectMany(x=>new[]{x}.Concat(All(x)));
    Check("주 화면: '진단 기록 폴더 열기' 버튼이 있음",All(app).OfType<Button>().Any(b=>b.Text=="진단 기록 폴더 열기"));
    var option=Get<CheckBox>(app,"scoreImagesOption");
    Check("주 화면: 진단 그림 저장은 기본으로 꺼져 있음",!option.Checked&&All(app).Contains(option));
    option.Checked=true;Check("주 화면: 진단 그림 저장을 켜면 설정에 남음",UserOptions.Load(preferences).DiagnosticImages);
    option.Checked=false;Check("주 화면: 끄면 설정에서도 꺼짐",!UserOptions.Load(preferences).DiagnosticImages);
    var status=Get<Label>(app,"scoreStatus");
    Check("주 화면: 모듈을 읽었음(SCORE 위치 탐색 중)",status.Text.StartsWith("SCORE 위치 탐색 중"),status.Text);
    Check("주 화면: 1920x1080 이면 해상도 안내 없음",!status.Text.Contains("1920×1080 게임 화면을 기준"));
    typeof(MainForm).GetField("captureSize",Flags)!.SetValue(app,((long)1280<<32)|720);Call(app,"UpdateScoreStatus");
    Check("주 화면: 1280x720 이면 해상도 안내가 보임",status.Text.Contains("게임 화면 1280×720")&&status.Text.Contains("게임 해상도를 1920×1080으로 권장합니다")&&!status.Text.Contains("창모드"),status.Text);
    typeof(MainForm).GetField("captureSize",Flags)!.SetValue(app,((long)1920<<32)|1080);Call(app,"UpdateScoreStatus");
    Check("주 화면: 다시 1920x1080 이면 안내가 사라짐",!status.Text.Contains("1920×1080 게임 화면을 기준"),status.Text);
    app.Close();
   }
  }catch(Exception e){failures.Add("오류: "+(e.InnerException??e).Message);}
  result["failures"]=failures;result["passed"]=failures.Count==0;
  File.WriteAllText(output,JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));
 }
 static object? Peek(object obj,string key)=>typeof(MainForm).GetField(key,Flags)!.GetValue(obj);
 static object? Call(object obj,string method,params object[] args)=>typeof(MainForm).GetMethod(method,Flags)!.Invoke(obj,args);

 internal static void Run(Catalog catalog,string casesPath,string caseKey,string output,int port,string script)
 {
  string[] row=File.ReadAllLines(casesPath).Select(l=>l.Split('\t')).First(f=>f[0].Contains(caseKey));
  string id=row[0],position=row[1],source=row[2];
  double start=double.Parse(row[3],Inv),end=double.Parse(row[4],Inv),pressAt=double.Parse(row[5],Inv)-start;
  int expected=int.Parse(row[6],Inv);
  var notes=new List<string>();
  var result=new Dictionary<string,object?>{["case"]=id,["position"]=position,["expected"]=expected,["script"]=script,["pressAt"]=Math.Round(pressAt,3)};
  void Save(){result["notes"]=notes;File.WriteAllText(output,JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));}
  try{
   string preferences=Path.Combine(AppContext.BaseDirectory,"replay-check-options.json");new UserOptions(true).Save(preferences);   // 전체화면 사용: 확인 창을 띄우지 않는다
   string log=Path.Combine(AppContext.BaseDirectory,"diagnostics");
   long logFrom=Directory.Exists(log)?new DirectoryInfo(log).EnumerateFiles("score-*.log").Sum(f=>f.Length):0;
   using var replay=new Replay(source,start,end-start);
   using var app=new Probe(catalog,port,preferences,replay){ShowInTaskbar=false,Opacity=0};
   Get<ComboBox>(app,"gear").SelectedItem=position;
   var windows=Get<ComboBox>(app,"windows");var windowType=typeof(MainForm).GetNestedType("WindowItem",BindingFlags.NonPublic)!;
   var fakeWindow=Activator.CreateInstance(windowType,new IntPtr(1),"녹화 재생")!;windows.Items.Add(fakeWindow);windows.SelectedItem=fakeWindow;
   var session=Get<Session>(app,"session");var store=Get<RecordStore>(app,"store");
   var chart=new SteamChart("FREESTYLE","Fundamental",8,"SC");
   var selection=new Selection(catalog.Songs.Single(s=>s.Name=="Fundamental").Id,8,"SC","노말",0);
   var before=store.Get(selection);
   result["recordBefore"]=before?.Value;
   int phase=0;double pressedAt=0,answeredAt=0;
   var imagesOption=Get<CheckBox>(app,"scoreImagesOption");result["imagesDefault"]=imagesOption.Checked;
   bool images=script.EndsWith("images");if(images){imagesOption.Checked=true;script=script=="images"?"cancel":script[..^7];}
   string imageRoot=Path.Combine(log,"images");
   var imagesBefore=Directory.Exists(imageRoot)?Directory.GetDirectories(imageRoot).ToHashSet():new HashSet<string>();
   if(script=="early")pressAt=3.0;else if(script=="thin")pressAt=double.PositiveInfinity;
   else if(script.StartsWith("late:")){pressAt+=double.Parse(script[5..],Inv);result["pressAt"]=Math.Round(pressAt,3);script="cancel";}
   using var step=new System.Windows.Forms.Timer(){Interval=20};
   using var expiry=new System.Windows.Forms.Timer(){Interval=(int)((end-start+40)*1000)};
   void Finish(string how){if(phase==9)return;phase=9;result["finish"]=how;result["captures"]=app.Captures;result["replaySeconds"]=Math.Round(replay.Seconds,2);result["replayFrames"]=replay.Frames;
    result["scoreStatus"]=Get<Label>(app,"scoreStatus").Text;result["scoreAdvice"]=Get<Label>(app,"scoreAdvice").Text;result["status"]=Get<Label>(app,"status").Text;app.Close();}
   step.Tick+=(_,_)=>{try{
    double t=replay.Seconds;
    if(script=="thin"&&double.IsPositiveInfinity(pressAt)&&Get<Label>(app,"scoreStatus").Text.StartsWith("SCORE 위치 고정")){pressAt=t+1.0;result["locatedAt"]=Math.Round(t,3);}
    if(phase==0&&t>=pressAt){
     // 곡 인식 흐름이 곡·판정을 확정해 두었어야 등록 키가 듣는다. 안 됐으면 시험이 대신 확정하고 그 사실을 적는다.
     bool selfLocked=session.Locked!=null;result["songLockedByApp"]=selfLocked;
     if(!selfLocked){session.Detect(selection);typeof(MainForm).GetField("steamChart",Flags)!.SetValue(app,chart);notes.Add("곡·판정을 앱이 확정하지 못해 시험이 대신 확정함");}
     pressedAt=replay.Seconds;result["pressedAt"]=Math.Round(pressedAt,3);
     Call(app,"Action",2,false);
     phase=1;
    }
    else if(phase==1){
     if(session.Pending is CapturedRecord pending){
      answeredAt=replay.Seconds;
      result["candidate"]=pending.RawScore;result["candidateAccuracy"]=pending.Accuracy;result["answerSeconds"]=Math.Round(answeredAt-pressedAt,3);
      result["verdict"]=pending.RawScore==expected?"right":"wrong";
      if(script=="confirm"){
       Call(app,"Decide",true);
       var saved=store.Get(selection);result["recordAfter"]=saved?.Value;result["pendingAfterConfirm"]=session.Pending!=null;
       result["saved"]=saved?.Value==Math.Max(before?.Value??0,pending.RawScore??0);
       store.Undo();result["recordAfterUndo"]=store.Get(selection)?.Value;   // 다음 사례를 위해 되돌린다
      }else{
       Call(app,"Decide",false);
       result["recordAfter"]=store.Get(selection)?.Value;result["pendingAfterCancel"]=session.Pending!=null;
       result["unchanged"]=store.Get(selection)?.Value==before?.Value;
      }
      phase=2;Finish("candidate");
     }
     else if(Peek(app,"analysisState") is object analysis&&(string?)analysis.GetType().GetProperty("stage")!.GetValue(analysis)=="failure"){
      answeredAt=replay.Seconds;
      result["verdict"]="none";result["failure"]=(string?)analysis.GetType().GetProperty("reason")!.GetValue(analysis);result["answerSeconds"]=Math.Round(answeredAt-pressedAt,3);
      // 오버레이가 받는 상태에 짧은 사유가 실려 있는가
      try{using var http=new HttpClient();using var state=JsonDocument.Parse(http.GetStringAsync($"http://127.0.0.1:{port}/state").GetAwaiter().GetResult());
       result["overlayAnalysis"]=state.RootElement.TryGetProperty("analysis",out var shown)?shown.Clone():null;}catch(Exception e){notes.Add("오버레이 상태를 읽지 못함: "+e.Message);}
      result["recordAfter"]=store.Get(selection)?.Value;
      phase=2;Finish("failure");
     }
     else if(replay.Seconds-pressedAt>14){result["verdict"]="none";result["failure"]="시험의 기다림 한도(14초)를 넘김";Finish("timeout");}
    }
   }catch(Exception e){notes.Add("각본 오류: "+(e.InnerException??e).Message);Finish("error");}};
   expiry.Tick+=(_,_)=>{notes.Add("시험 전체의 시간 한도를 넘김");Finish("expired");};
   app.Shown+=(_,_)=>{try{app.Chart=chart;replay.Start();Call(app,"Action",6,false);step.Start();expiry.Start();}catch(Exception e){notes.Add("시작 오류: "+(e.InnerException??e).Message);Finish("error");}};
   Application.Run(app);
   if(images){
    Get<ScoreImageLog>(app,"scoreImages").Flush().Wait(10000);
    var made=Directory.Exists(imageRoot)?Directory.GetDirectories(imageRoot).Where(d=>!imagesBefore.Contains(d)).ToList():new List<string>();
    result["imageFolders"]=made.Select(Path.GetFileName).ToList();
    result["imageFiles"]=made.SelectMany(d=>new DirectoryInfo(d).EnumerateFiles().OrderBy(f=>f.Name).Select(f=>$"{f.Name} {f.Length}")).ToList();
    if(made.Count==1&&File.Exists(Path.Combine(made[0],"screen.png"))){using var screen=new Bitmap(Path.Combine(made[0],"screen.png"));result["screenSize"]=$"{screen.Width}x{screen.Height}";
     string first=Path.Combine(made[0],"frame-001.png");if(File.Exists(first)){using var band=new Bitmap(first);result["bandSize"]=$"{band.Width}x{band.Height}";}}
   }
   // 이번 실행이 남긴 진단 기록
   if(Directory.Exists(log)){
    var lines=new List<string>();long skipped=0;
    foreach(var file in new DirectoryInfo(log).EnumerateFiles("score-*.log").OrderBy(f=>f.Name)){
     string text=File.ReadAllText(file.FullName);
     foreach(string line in text.Split('\n',StringSplitOptions.RemoveEmptyEntries)){long size=System.Text.Encoding.UTF8.GetByteCount(line)+1;if(skipped+size<=logFrom){skipped+=size;continue;}lines.Add(line);}
    }
    result["diagnostics"]=lines.Select(l=>JsonSerializer.Deserialize<JsonElement>(l)).ToList();
   }
  }catch(Exception e){notes.Add("오류: "+e.Message);result["finish"]="error";}
  Save();
 }
}
#endif
