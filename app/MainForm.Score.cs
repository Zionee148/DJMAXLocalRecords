using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using DjmaxScore;
namespace LadderRecords;

// SCORE 자동 인식과 등록 요청. 새 SCORE 모듈(dotnet\ScoreModule)을 ScoreLink 로 부른다.
//
//   - 캡처한 화면에서 선택한 기어 위치의 띠만 잘라 ScoreLink 에 넘긴다. 자리 찾기, 플레이 중 읽기, 등록 판단은 모듈이 한다. 옛 SCORE 구현은 쓰지 않는다.
//   - SCORE 의 "판"은 Steam 곡 정보를 따라 여기(CaptureTick)에서 연다. 곡 인식 쪽의 흐름(Tick)과 따로 돈다.
//     저장 확인을 기다리는 동안 Tick 은 멈추지만 캡처와 SCORE 추적은 계속한다. 그래야 확인을 미룬 채 시작된 다음 판의 근거가 쌓인다.
//   - 같은 곡을 다시 시작한 것은 모듈이 읽은 점수의 흐름으로 알아채고, 그때마다 판 번호(ScoreLink.PlaySerial)가 오른다.
partial class MainForm
{
 readonly System.Windows.Forms.Timer captureTimer=new(){Interval=100};
 readonly PictureBox scorePicture=new(){Width=640,Height=68,SizeMode=PictureBoxSizeMode.Zoom,BackColor=Color.FromArgb(20,24,30)};
 readonly Label scoreStatus=new(){Text="SCORE 위치 탐색 대기",AutoSize=true,MaximumSize=new(640,0),Font=Theme.Px("맑은 고딕",9)};
 // 등록 답을 내지 못한 사유와 다음 행동. 다음 등록 요청이나 다음 곡까지 남긴다(status 는 곡 인식 흐름이 계속 덮어쓴다).
 readonly Label scoreAdvice=new(){Text="",AutoSize=true,MaximumSize=new(650,0),ForeColor=Color.FromArgb(176,64,0)};
 Bitmap? latestFrame;long latestFrameAt;string? latestFrameKey;bool captureBusy;
 RegistrationRequest? activeRegistration;object? analysisState;
 ScoreLink scoreLink=null!;ScoreRunner scoreRunner=null!;ScoreDiagnostics scoreLog=null!;ScoreImageLog scoreImages=null!;
 // 진단 그림: 사용자가 켰을 때만 남긴다(기본은 꺼짐). 등록 키를 누른 순간의 게임 화면과 그 뒤 점수를 읽는 데 쓴 화면.
 readonly CheckBox scoreImagesOption=new(){Text="진단 그림 저장",AutoSize=true};
 string? scoreImageFolder;   // 열려 있는 요청의 그림을 두는 폴더 이름(그림을 남기지 않으면 null)
 string scorePlayWhy="곡 정보 감지";       // 다음에 여는 판의 까닭(진단 기록에 남는다)
 string? scoreAdviceKey;                    // 실패 안내가 어느 곡의 것인지
 int pendingPlaySerial,registeredPlaySerial; // 확인을 기다리는 후보가 나온 판, 저장한 판
 long captureSize;                          // 가장 최근에 캡처한 게임 화면의 크기(너비<<32|높이). 캡처 스레드가 쓴다
 ScoreArea? drawnArea;long drawnAt;string? runnerErrorLogged;   // 진단 기록에 남긴 판독 작업의 마지막 오류(같은 오류를 되풀이해 적지 않는다)
 sealed record RegistrationRequest(int Epoch,string SteamKey,Selection Chart,string ProfileId,long PressedAt,DateTime PressedUtc,
  RateSource Source,string Mode,int GearIndex,int PlaySerial,CancellationTokenSource Cancellation);

 // 분석 중이나 저장 확인을 기다리는 동안에는 프로필을 바꾸는 조작(전환·새로·복사·삭제·이름 변경)을 막는다. 후보는 요청 당시의 프로필에 저장돼야 한다.
 // 조작을 시작할 때와, 이름을 묻는 창이나 확인 창이 닫혀 실제로 바꾸기 직전에 모두 부른다(그 창이 떠 있는 동안에도 단축키로 등록이 시작될 수 있다).
 // 기록을 바꾸는 조작(수동 기록, 목록에서 고치기·지우기, 가져오기, 되돌리기)은 막지 않는다. 저장은 그 순간의 최신 기록과 견준다.
 void EnsureProfileFree(){if(activeRegistration!=null||session.Pending!=null)throw new Exception("점수 확인과 저장 확인이 끝난 뒤 프로필을 변경해 주세요.");}
 long seenRevision=-1;   // 저장 확인과 오버레이에 보이는 '기존 기록'을 기록이 바뀔 때 다시 그리려고 본다
 long heldPreviewAt;      // 점수 확인·저장 확인 대기 중에 미리 보기를 마지막으로 고친 때
 GearPosition ScoreGear=>GearIndex switch{1=>GearPosition.Right,2=>GearPosition.Center,_=>GearPosition.Left};
 bool ScoreLocated=>scoreLink.Location?.State==LocationState.Locked;

 void InitScore()
 {
  string folder=Path.Combine(AppContext.BaseDirectory,"data","score");
  scoreLog=new ScoreDiagnostics(Path.Combine(AppContext.BaseDirectory,"diagnostics"));
  RecordStore.Rejected=problem=>{try{scoreLog.Write("records-rejected",new{problem});}catch{}};   // 저장을 거절한 까닭(화면에는 정해 둔 글만 보인다)
  scoreImages=new ScoreImageLog(Path.Combine(scoreLog.Folder,"images"));
  scoreRunner=new ScoreRunner(SynchronizationContext.Current??new WindowsFormsSynchronizationContext());
  scoreLink=new ScoreLink(Path.Combine(folder,"finder.onnx"),Path.Combine(folder,"reader.onnx"),scoreRunner,scoreLog);
  scoreLink.Changed+=()=>{if(!IsDisposed&&!Disposing)UpdateScoreStatus();};
  UpdateScoreStatus();
 }

 // 지금 판을 끝낸다. 다음 캡처에서 곡 정보가 잡히면 why 를 까닭으로 새 판이 열린다(자리와 점수 이력을 처음부터).
 void EndScorePlay(string why){scorePlayWhy=why;scoreLink.EndPlay(why);}

 async Task CaptureTick()
 {
  if(IsDisposed||Disposing)return;
  scoreLink.Poll();   // 화면이 안 들어와도 등록 요청의 기한은 지나간다
  if(store.Revision!=seenRevision){seenRevision=store.Revision;if(session.Pending!=null)RefreshState();}
  if(captureBusy||!session.Running||windows.SelectedItem is not WindowItem window)return;
  captureBusy=true;int generation=epoch;
  try{
   var request=activeRegistration;
   var before=ReadSteamSnapshot();long at=Stopwatch.GetTimestamp();
   if(request!=null&&before.Available&&before.Chart?.Key!=request.SteamKey)scoreLink.ContextLeft();   // 이 요청의 곡이 끝났다. 받은 화면으로 마치게 한다(취소가 아니다)
   if(before.Available&&before.Chart==null){if(!scoreLink.RequestOpen)scoreLink.EndPlay("곡 정보 없음");return;}
   var frame=await Task.Run(()=>CaptureGameFrame(window.Handle));
   long ended=Stopwatch.GetTimestamp();
   if(IsDisposed||Disposing||generation!=epoch){frame.Dispose();return;}
   var after=ReadSteamSnapshot();
   var old=latestFrame;latestFrame=frame;latestFrameAt=at;latestFrameKey=before.Available&&after.Available&&before.Chart?.Key==after.Chart?.Key?after.Chart?.Key:null;old?.Dispose();
   // 점수를 확인하는 동안과 저장 확인을 기다리는 동안에는 곡 인식 흐름(Tick)이 쉬어 미리 보기가 멈춘다. 그동안은 여기서 게임 화면과 SCORE 그림을 고친다(1초에 네 번까지)
   if((request!=null||session.Pending!=null)&&Stopwatch.GetElapsedTime(heldPreviewAt).TotalSeconds>=.25){heldPreviewAt=Stopwatch.GetTimestamp();if(ShouldUpdatePreview())ShowImage(frame);RefreshScorePreview(frame);}
   if(request!=null&&after.Available&&after.Chart?.Key!=request.SteamKey)scoreLink.ContextLeft();
   if(latestFrameKey==null)return;   // 캡처 앞뒤의 곡 정보가 다르면 어느 곡의 화면인지 알 수 없다. 쓰지 않는다
   if(!scoreLink.RequestOpen){
    // 판은 Steam 곡 정보와 기어 위치를 따라 연다. 같으면 아무 일도 하지 않으므로, 확인을 기다리는 동안 쌓인 다음 판의 근거가 지워지지 않는다.
    int serial=scoreLink.PlaySerial;
    scoreLink.EnsurePlay(latestFrameKey,ScoreGear,scorePlayWhy);
    if(scoreLink.PlaySerial!=serial){scorePlayWhy="곡 정보 감지";if(scoreAdviceKey!=latestFrameKey)scoreAdvice.Text="";}
   }
   if(ScoreBand(frame,ScoreGear,at,ended) is var (shape,bytes)){
    int index=scoreLink.Offer(shape,bytes,latestFrameKey);
    if(index>0&&scoreImageFolder!=null)scoreImages.SaveBand(scoreImageFolder,index,bytes,shape.Width,shape.Height,shape.Stride);
   }
  }catch(Exception){/* The main recognition loop reports capture failures. */}
  finally{captureBusy=false;}
 }

 // 1920x1080 화면에서 선택한 기어 위치의 띠(가로 960, 세로 740 아래)만 BGRA 로 복사한다. 화면이 그 크기가 아니면 null.
 static (CapturedFrame Shape,byte[] Bgra)? ScoreBand(Bitmap frame,GearPosition gear,long at,long ended)
 {
  if(frame.Width!=ScoreModule.ScreenWidth||frame.Height!=ScoreModule.ScreenHeight)return null;
  var area=new Rectangle(ScoreModule.BandLeft(gear),ScoreModule.BandTop,ScoreModule.BandWidth,ScoreModule.BandHeight);
  var data=frame.LockBits(area,ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
  try{
   int stride=area.Width*4;var bytes=new byte[stride*area.Height];
   for(int y=0;y<area.Height;y++)Marshal.Copy(data.Scan0+y*data.Stride,bytes,y*stride,stride);
   return (new CapturedFrame(at,ended,area.Width,area.Height,stride,frame.Width,frame.Height,area.X,area.Y),bytes);
  }finally{frame.UnlockBits(data);}
 }

 void RequestRegistration()
 {
  if(activeRegistration!=null||session.Pending!=null)return;
  if(!session.Running||session.Locked==null||steamChart==null)
   throw new Exception("감지를 시작하고 곡 정보와 판정이 확정된 뒤에 등록해 주세요.");
  var now=Stopwatch.GetTimestamp();
  var request=new RegistrationRequest(epoch,steamChart.Key,session.Locked,store.Active.Id,now,DateTime.UtcNow,
   rateSource,steamChart.Mode,GearIndex,scoreLink.PlaySerial,new());
  activeRegistration=request;
  scoreAdvice.Text="";scoreAdviceKey=null;
  SetAnalysis(request,"checking");
  // 이 곡의 판이 아직 열리지 않았으면(곡 정보가 막 바뀜) 다른 판에 요청을 열지 않는다.
  if(scoreLink.Available&&scoreLink.PlayKey!=request.SteamKey){FailRegistration(request,"LocationUncertain");return;}
  long id=scoreLink.BeginRequest(now,8.0,answer=>OnScoreAnswer(request,answer));
  if(scoreImagesOption.Checked)SaveRequestScreen(id);
 }
 // 진단 그림을 켰으면 등록 키를 누른 순간의 게임 화면을 남기고, 이 요청의 그림을 둘 폴더를 정한다.
 void SaveRequestScreen(long id)
 {
  string name=ScoreImageLog.RequestFolder(DateTime.Now,id);
  if(scoreLink.RequestOpen)scoreImageFolder=name;   // 그 자리에서 답이 난 요청(자리 고정 전, 근거 부족)은 뒤따르는 화면이 없다
  if(latestFrame!=null)scoreImages.SaveScreen(name,(Bitmap)latestFrame.Clone());
  scoreLog.Write("images",new{request=id,folder=name});
 }
 void OpenDiagnostics()
 {
  Directory.CreateDirectory(scoreLog.Folder);
  Process.Start(new ProcessStartInfo{FileName=scoreLog.Folder,UseShellExecute=true});
 }
 bool IsCurrent(RegistrationRequest request)=>!IsDisposed&&!Disposing&&session.Running&&!request.Cancellation.IsCancellationRequested
  &&ReferenceEquals(activeRegistration,request)&&request.ProfileId==store.Active.Id&&request.Epoch==epoch;
 void SetAnalysis(RegistrationRequest request,string stage,string? reason=null)
 {
  analysisState=new{stage,reason,song=OverlayText.Title(catalog,catalog.Songs.Single(s=>s.Id==request.Chart.SongId)),button=request.Chart.Button,
   pattern=request.Chart.Pattern,judge=request.Chart.Judge,cancel=Hotkeys.Format(hotkeys.Items[3])};
  status.Text=stage=="checking"?"점수를 확인하고 있습니다…":(reason??"점수를 확인하지 못했습니다")+".";
  RefreshState();
 }
 // 사용자의 취소, 감지 중지, 종료. 요청을 버린다(모듈에도 알린다). 뒤늦게 온 답은 요청이 달라 반영되지 않는다.
 void CancelAnalysis()
 {
  var request=activeRegistration;analysisState=null;
  if(request==null)return;
  activeRegistration=null;
  try{request.Cancellation.Cancel();}catch(ObjectDisposedException){}
  scoreLink.CancelRequest();
 }
 void FinishReading(){busy=false;}

 // 모듈의 답. 화면 스레드에서 불린다.
 void OnScoreAnswer(RegistrationRequest request,ScoreAnswer answer)
 {
  scoreImageFolder=null;
  if(!ReferenceEquals(activeRegistration,request)){request.Cancellation.Dispose();return;}   // 취소됐거나 이미 끝난 요청이다
  var result=answer.Result;
  if(!IsCurrent(request)||result.State==RegistrationStatus.Cancelled){
   activeRegistration=null;analysisState=null;request.Cancellation.Dispose();
   if(!IsDisposed&&!Disposing)RefreshState();
   return;
  }
  if(result.State==RegistrationStatus.Accepted&&result.RawScore is int score){
   try{
    bool propagate=!(request.Mode=="LADDERMATCH"&&request.Source==RateSource.Hud);
    var pending=session.Begin(request.ProfileId,request.PressedUtc,measuredAccuracy:ScoreValue.Rate(score),rawScore:score,
     propagateNormal:propagate,chart:request.Chart,steamKey:request.SteamKey);
    pendingPlaySerial=answer.PlaySerial;
    activeRegistration=null;analysisState=null;request.Cancellation.Dispose();
    ShowPendingConfirmation(pending);
   }catch(Exception e){FailRegistration(request,"Error");status.Text=status.Text.TrimEnd('.')+" · "+e.Message;}   // 앞 글의 마침표를 떼고 붙인다
   return;
  }
  FailRegistration(request,result.Reason);
 }

 // 답을 내지 못했다: 오버레이에 짧은 사유를 3초 보이고, 프로그램에는 사유와 다음 행동을 다음 요청이나 다음 곡까지 남긴다.
 async void FailRegistration(RegistrationRequest request,string reason)
 {
  try{
   var (brief,advice)=ScoreFailure.Describe(reason);
   scoreAdvice.Text=$"등록 실패: {brief}. {advice}";scoreAdviceKey=request.SteamKey;
   SetAnalysis(request,"failure",brief);
   await Task.Delay(3000,request.Cancellation.Token);
  }catch(OperationCanceledException){}
  catch(ObjectDisposedException){}
  finally{
   if(ReferenceEquals(activeRegistration,request)){activeRegistration=null;analysisState=null;}
   request.Cancellation.Dispose();
   if(!IsDisposed&&!Disposing)RefreshState();
  }
 }

 // 후보가 나왔다: 주 화면 오른쪽에 저장 확인을 보인다(RefreshState → ShowConfirmArea. MainForm.Look.cs). 창을 따로 띄우지 않는다
 void ShowPendingConfirmation(CapturedRecord pending)
 {
  pendingNote=null;status.Text="저장 확인 대기";RefreshState();
 }

 // SCORE 모듈을 쓸 수 없는 까닭. 모듈이 준 글(내부 이름·경로·영어 예외)은 진단 기록에 남기고(ScoreLink 의 "unavailable") 화면에는 한국어로 적는다
 static string UnavailableText(string? reason)=>reason!=null&&reason.EndsWith(" 가 없다")
  ?"판독 파일이 없습니다\n등록은 ‘수동 기록 설정’을 이용해 주세요. 프로그램 파일이 빠졌을 수 있습니다. 프로그램을 새로 받아 설치해 주세요."
  :"판독 모듈을 불러오지 못했습니다\n등록은 ‘수동 기록 설정’을 이용해 주세요. Microsoft Visual C++ 재배포 패키지가 없으면 이 상태가 될 수 있습니다.";
 // SCORE 상태 줄: 자리를 찾는 중인지, 고정됐는지, 쓸 수 없는지. 게임 화면이 1920x1080 이 아니면 그 안내도 함께 보인다.
 void UpdateScoreStatus()
 {
  string text;
  if(!scoreLink.Available)text="SCORE 자동 인식을 쓸 수 없습니다 · "+UnavailableText(scoreLink.Unavailable);
  else if(!session.Running)text="SCORE 위치 탐색 대기";   // 감지가 꺼져 있으면 찾지 않는다
  // 판독 작업의 예외 원문은 진단 기록에만 남기고(바뀔 때 한 번) 화면에는 한국어로 적는다. 위치가 고정돼 있어도 오류가 먼저다.
  // 다음 작업이 끝나면 LastError 가 비므로 지난 오류가 남지 않는다
  else if(scoreRunner.LastError is {} error){text="SCORE 판독 중 오류가 났습니다\n계속되면 진단 기록을 함께 제보해 주세요.";if(error!=runnerErrorLogged){runnerErrorLogged=error;scoreLog.Write("runner-error",new{message=error});}}
  else if(scoreLink.Location is {State:LocationState.Locked,Area:not null})text="SCORE 위치 고정";   // 좌표와 크기는 보이지 않는다
  else text="SCORE 위치 탐색 중";
  long size=Interlocked.Read(ref captureSize);int width=(int)(size>>32),height=(int)(size&0xffffffff);
  if(width>0&&(width!=ScoreModule.ScreenWidth||height!=ScoreModule.ScreenHeight))
   text+=$"\n게임 화면 {width}×{height} · SCORE 자동 인식은 1920×1080 게임 화면을 기준으로 개발되었습니다. 낮은 해상도에서는 숫자 인식이 불안정할 수 있으며, 다른 해상도와 화면 비율은 충분히 검증되지 않았습니다. 게임 해상도를 1920×1080으로 권장합니다.";
  if(scoreStatus.Text!=text)scoreStatus.Text=text;
 }
 void RefreshScorePreview(Bitmap frame)
 {
  UpdateScoreStatus();
  if(!ShouldUpdatePreview())return;
  if(scoreLink.Location is not {State:LocationState.Locked,Area:ScoreArea located}){var previous=scorePicture.Image;scorePicture.Image=null;previous?.Dispose();drawnArea=null;return;}
  // 영역은 플레이 중에 조금씩 바뀐다(자릿수가 늘 때, 움직이는 점수판). 그림은 처음 고정됐을 때 바로 그리고, 그 뒤로는 아래의 간격으로 다시 그린다.
  // 자리가 그대로면 1초에 한 번, 자리가 바뀌었으면 0.25초에 한 번까지(자리가 같다고 건너뛰면 예전 숫자가 그대로 남는다)
  if(scorePicture.Image!=null&&Stopwatch.GetElapsedTime(drawnAt).TotalSeconds<(drawnArea==located?1:.25))return;
  var area=new Rectangle(located.X,located.Y,located.Width,located.Height);
  area.Inflate(Math.Max(24,area.Width/4),8);area.Intersect(new(0,740,frame.Width,frame.Height-740));
  if(area.Width<1||area.Height<1)return;
  var image=frame.Clone(area,PixelFormat.Format24bppRgb);var old=scorePicture.Image;scorePicture.Image=image;old?.Dispose();
  drawnArea=located;drawnAt=Stopwatch.GetTimestamp();
 }
}
