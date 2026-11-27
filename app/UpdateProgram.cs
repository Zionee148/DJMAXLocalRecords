using System.Diagnostics;
namespace LadderRecords;

// 자동 업데이트: 프로그램 폴더 밖(update\ 아래)에서 켜진 실행 파일이 하는 일.
//   update\staging\DJMAXLocalRecords\DJMAXLocalRecords.exe --apply-update <옛 프로세스> <신호>   새 버전이 스스로 설치한다
//   update\backup\<이름>\program\DJMAXLocalRecords.exe --restore-backup <지금 프로세스> <신호>   주 화면의 [이전 버전 복구]
//   update\backup\<이름>\program\DJMAXLocalRecords.exe                                         프로그램이 켜지지 않을 때 사용자가 직접 실행
// 지금 켜져 있는 실행 파일은 자기 자신의 파일을 바꿀 수 없어서, 바꾸는 일은 언제나 다른 폴더에서 켜진 쪽이 한다.
// 일하는 부분은 한 스레드에서 차례로 돈다(잡은 뮤텍스를 같은 스레드에서 놓아야 한다).
// 안내의 글은 UpdateGuides, 진행 창은 InstallWindow 에 있다.
// 교체 절차는 배포된 버전과 같게 둔다(상태 파일과 목록은 버전 사이에서 주고받는다): 상태 파일과 목록의 모양, 기다리는 시간, 단계의 차례, 교체 도중의 닫기 막기, 선택의 기본(새 버전 유지·더 기다리기·취소).
// 새 창을 만들다 실패하면 단순한 모양의 창으로, 안내창을 띄우지 못하면 Windows 의 알림 창으로 물러선다(이 실행 파일은 글꼴·테마를 읽지 못해도 알려야 한다)
static class UpdateProgram
{
 public const string ApplyFlag="--apply-update",RestoreFlag="--restore-backup";
 static readonly TimeSpan ExitWait=TimeSpan.FromSeconds(60),StartWait=TimeSpan.FromSeconds(120);

 /// <summary>업데이트나 복구를 맡아 켜진 것이면 그 일을 하고 true 를 돌려준다(프로그램은 여기서 끝난다).</summary>
 public static bool Handle(string[] args)
 {
  string here=AppContext.BaseDirectory;
  if(args.Length>0&&args[0]==ApplyFlag)
  {
   // 풀어 놓은 자리에서 켜졌을 때만 한다. 다른 자리에서 이 인자로 켜졌으면 아무것도 하지 않는다
   if(args.Length==3&&int.TryParse(args[1],out int old)&&UpdatePaths.FromStaging(here,out string app))Apply(new(app),old,args[2]);
   return true;
  }
  if(args.Length>0&&args[0]==RestoreFlag)
  {
   if(args.Length==3&&int.TryParse(args[1],out int current)&&UpdatePaths.FromBackup(here,out string app,out string backup))Restore(new(app),backup,current,args[2]);
   return true;
  }
  if(args.Length==0&&UpdatePaths.FromBackup(here,out string appDir,out string backupDir)){Restore(new(appDir),backupDir,null,null);return true;}
  if(args.Length==0&&UpdatePaths.FromStaging(here,out string stagedFor))
  {
   UseTheme(new(stagedFor));   // 프로그램 폴더의 설정에서 사용자의 테마를 읽는다(풀어 놓은 새 버전의 폴더에는 설정 파일이 없다)
   GuideDialog.ShowAlone(UpdateGuides.StagingOpened);
   return true;
  }
  return false;
 }

 static void UseTheme(UpdatePaths paths){try{var options=UserOptions.Load(UserFiles.Existing(paths.App,"preferences.json"));Theme.Dark=options.Dark;Theme.DecideScale(options);}catch{}}
 static void Signal(string? name)
 {
  if(name==null)return;
  try{using var ready=EventWaitHandle.OpenExisting(name);ready.Set();}
  catch(WaitHandleCannotBeOpenedException){}catch(UnauthorizedAccessException){}
 }
 static bool WaitExit(int? id,TimeSpan limit)
 {
  if(id==null)return true;
  try{using var process=Process.GetProcessById(id.Value);return process.WaitForExit(limit);}
  catch(ArgumentException){return true;}   // 이미 끝났다
  catch(InvalidOperationException){return true;}
 }
 /// <summary>프로그램이 하나만 켜지게 하는 뮤텍스를 잡는다. 파일을 바꾸는 동안 프로그램이 새로 켜지지 못하게 한다. 못 잡으면 null.</summary>
 static Mutex? Claim(UpdatePaths paths)
 {
  var mutex=new Mutex(false,SingleInstance.MutexName(UpdateTestConfig.Load(paths.App)));
  try{if(mutex.WaitOne(TimeSpan.FromSeconds(15)))return mutex;}
  catch(AbandonedMutexException){return mutex;}
  mutex.Dispose();return null;
 }
 static void Release(ref Mutex? mutex){if(mutex==null)return;try{mutex.ReleaseMutex();}catch(ApplicationException){}mutex.Dispose();mutex=null;}
 static Process? Start(UpdatePaths paths)
 {
  try{return Process.Start(new ProcessStartInfo(paths.Executable){UseShellExecute=false,WorkingDirectory=paths.App});}
  catch(System.ComponentModel.Win32Exception e){paths.Log("프로그램을 켜지 못함: "+e.Message);return null;}
 }
 static string Stamp=>DateTime.Now.ToString("yyyyMMdd-HHmmss");
 /// <summary>상태 파일의 점 버전("1.2.0")을 화면의 글("v1.2.0")로. 모르면 null</summary>
 static string? V(string? plain)=>string.IsNullOrWhiteSpace(plain)?null:"v"+plain;
 /// <summary>'확인' 뒤에 다시 켜겠다고 알린 프로그램을 켠다. 켜지 못하면 직접 열 파일을 알린다</summary>
 static void StartOrTell(InstallWindow w,UpdatePaths paths,Guide ifNot){if(Start(paths) is {} started)started.Dispose();else w.Show(ifNot);}
 /// <summary>진행 창을 만든다. 새 모양을 만들다 실패하면 단순한 모양으로 물러선다</summary>
 static InstallWindow Window(UpdatePaths paths,string title,string headline,string? from,string? to,string[] names,int start,string wait)
 {
  try{return new InstallWindow(title,headline,from,to,names,start,wait);}
  catch(Exception e){paths.Log("진행 창을 새 모양으로 만들지 못해 단순한 모양으로 띄움: "+e.Message);return new InstallWindow(title,headline,from,to,names,start,wait,plain:true);}
 }

 // ── 새 버전이 스스로 설치한다
 static void Apply(UpdatePaths paths,int oldProcess,string signal)
 {
  UseTheme(paths);
  var state=paths.ReadState();
  using var window=Window(paths,"업데이트",UpdateGuides.InstallHead,V(state?.From),V(state?.To),UpdatePrepare.Stages,UpdatePrepare.Replace,UpdateGuides.InstallWait);
  window.Work=w=>
  {
   try{ApplyWork(w,paths,state,oldProcess,signal);}
   catch(Exception e)
   {
    paths.Log("설치 과정 오류: "+e);
    w.Stage(UpdatePrepare.Replace,null,true);
    w.Show(UpdateGuides.InstallCrashed(e,state==null||string.IsNullOrWhiteSpace(state.Backup)?null:Path.Combine(paths.Backups,state.Backup),paths.App));
   }
  };
  Application.Run(window);
 }

 static void ApplyWork(InstallWindow w,UpdatePaths paths,UpdateState? state,int oldProcess,string signal)
 {
  paths.Log("설치 과정 시작 ("+AppVersion.Text(AppVersion.Current)+")");
  // 준비됐다고 알린다. 이제 옛 프로그램이 끝난다
  Signal(signal);
  w.Stage(UpdatePrepare.Replace,UpdateGuides.WaitingOld);
  void Unchanged(string why,Guide guide)
  {
   paths.Log("교체하지 않음: "+why);paths.ClearState();
   if(state!=null)RemoveNewBackup(paths,state.Backup);
   w.Stage(UpdatePrepare.Replace,null,true);
   w.Show(guide);
  }
  if(!WaitExit(oldProcess,ExitWait)){Unchanged("기존 프로그램이 끝나지 않음",UpdateGuides.SwapNotStarted(UpdateGuides.SwapStop.OldStillRunning,state?.From));return;}
  var mutex=Claim(paths);
  if(mutex==null||paths.Running().Count>0){Release(ref mutex);Unchanged("프로그램이 다시 켜져 있음",UpdateGuides.SwapNotStarted(UpdateGuides.SwapStop.RunningAgain,state?.From));return;}
  try
  {
   var incoming=UpdateManifest.Read(AppContext.BaseDirectory);
   if(state is not {Phase:"prepared"}||incoming==null||state.To!=AppVersion.Plain(AppVersion.Current)||incoming.Version!=state.To)
   {Release(ref mutex);Unchanged("업데이트 준비 정보가 맞지 않음",UpdateGuides.SwapNotStarted(UpdateGuides.SwapStop.Mismatch,state?.From));StartOrTell(w,paths,UpdateGuides.OpenCurrent(paths.App));return;}
   string backupDir=Path.Combine(paths.Backups,state.Backup);
   if(UpdateBackup.Info(backupDir)==null)
   {Release(ref mutex);Unchanged("기록과 설정의 백업을 찾지 못함",UpdateGuides.SwapNotStarted(UpdateGuides.SwapStop.NoBackupInfo,state.From));StartOrTell(w,paths,UpdateGuides.OpenCurrent(paths.App));return;}

   var bring=incoming.Files.Select(file=>file.Path).Append(UpdateManifest.FileName).ToList();
   var expected=incoming.Files.ToDictionary(file=>file.Path,StringComparer.OrdinalIgnoreCase);
   // 기존 프로그램이 넘기기 전에 본 것을 바꾸기 직전에 한 번 더 본다(그 사이에 파일이 놓였을 수 있다)
   try{UpdateSwap.CheckPlace(paths.App,bring);}
   catch(UpdateException e){Release(ref mutex);Unchanged(e.Message,UpdateGuides.SwapCheckFailed(e,state.From));StartOrTell(w,paths,UpdateGuides.OpenCurrent(paths.App));return;}

   paths.WriteState(state with{Phase="swapping",At=UpdatePaths.Now});
   w.Stage(UpdatePrepare.Replace,UpdateGuides.Swapping);
   var swap=UpdateSwap.Exchange(paths.App,UpdateSwap.Current(paths.App),UpdateBackup.Program(backupDir),AppContext.BaseDirectory,bring,false,expected,paths.Log);
   if(!swap.Done)
   {
    w.Stage(UpdatePrepare.Replace,null,true);
    if(swap.Restored)
    {
     UpdateSwap.RemoveFolder(backupDir);
     paths.WriteState(state with{Phase="rolled-back",At=UpdatePaths.Now,Note=swap.Error});
     Release(ref mutex);
     w.Show(UpdateGuides.SwapRolledBack(swap.Error,state.From));
     StartOrTell(w,paths,UpdateGuides.DirectRun(UpdateGuides.UpdateTitle,paths.App));
    }
    else Broken(w,paths,state,swap,UpdateGuides.SwapBroken(swap,backupDir,paths.App));
    return;
   }
   UpdateBackup.MarkProgram(backupDir,true);
   paths.WriteState(state with{Phase="swapped",At=UpdatePaths.Now});
   Release(ref mutex);

   w.Stage(UpdatePrepare.Restart,UpdateGuides.StartingNew);
   using var started=Start(paths);
   while(true)
   {
    // 새 버전이 정상으로 뜨면 상태 파일을 지운다. 프로세스가 생겼다는 것만으로는 성공으로 치지 않는다
    var waited=Stopwatch.StartNew();bool confirmed=false;
    while(waited.Elapsed<StartWait&&started is {HasExited:false})
    {
     if(!File.Exists(paths.StateFile)){confirmed=true;break;}
     Thread.Sleep(300);
    }
    if(confirmed||!File.Exists(paths.StateFile)){paths.Log("새 버전이 정상으로 시작됨");return;}
    bool alive=started is {HasExited:false};
    paths.Log(alive?"새 버전이 시작을 마치지 못함(시간 초과)":"새 버전이 시작되지 않음");
    w.Stage(UpdatePrepare.Restart,null,true);
    // 단추: 0 '이전 버전으로 복구', 1 '새 버전 유지'(끝났을 때) 또는 '더 기다리기'(아직 켜져 있을 때). 처음 초점이 1 이라 연 직후의 Enter 와 Esc·닫기는 1 이다
    bool back=w.Show(UpdateGuides.StartFailed(alive,state.From,state.To,backupDir,UserFiles.RecordsMoved(paths.App)))==0;
    if(!back){if(alive){w.Stage(UpdatePrepare.Restart,UpdateGuides.WaitingNew);continue;}return;}
    // 업데이트 도중의 복구: 같은 창에서 첫 줄·버전의 방향·'복구 중'을 바꾼다
    w.Restoring(V(state.To),V(state.From));
    if(started is {HasExited:false})
    {
     w.Stage(UpdatePrepare.Replace,UpdateGuides.StoppingNew);
     // 우리가 방금 켠 프로세스다. 파일을 되돌리려면 끝나 있어야 한다
     try{started.Kill();started.WaitForExit(10000);}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}
    }
    RollBack(w,paths,state,backupDir,bring);
    return;
   }
  }
  finally{Release(ref mutex);}
 }

 /// <summary>새 버전을 켜 보았으나 뜨지 않아, 옮겨 둔 옛 프로그램 파일을 제자리로 돌린다.</summary>
 static void RollBack(InstallWindow w,UpdatePaths paths,UpdateState state,string backupDir,List<string> incoming)
 {
  w.Stage(UpdatePrepare.Replace,UpdateGuides.RollingBack);
  var mutex=Claim(paths);
  try
  {
   if(mutex==null||paths.Running().Count>0)
   {w.Stage(UpdatePrepare.Replace,null,true);w.Show(UpdateGuides.RollbackBlocked(backupDir));return;}
   string program=UpdateBackup.Program(backupDir);var old=UpdateFiles.Under(program);
   string parked=Path.Combine(paths.Root,"failed-"+Stamp);
   // incoming 은 이 과정이 방금 들여놓은 새 버전의 파일이다. 목록을 읽지 못하더라도 이 프로그램의 것이 확실하다
   var swap=UpdateSwap.Exchange(paths.App,UpdateSwap.Current(paths.App,incoming),parked,program,old,true,null,paths.Log);
   if(swap.Done)
   {
    UpdateSwap.RemoveFolder(parked);UpdateBackup.MarkProgram(backupDir,false);
    paths.WriteState(state with{Phase="rolled-back",At=UpdatePaths.Now,Note="새 버전이 시작되지 않음"});
    Release(ref mutex);
    w.Show(UpdateGuides.RolledBack(state.From,UserFiles.RecordsMoved(paths.App)));
    // '확인' 뒤에 이전 버전을 켠다. 켜지 못하면 직접 실행할 파일을 알린다
    StartOrTell(w,paths,UpdateGuides.DirectRun(UpdateGuides.UpdateTitle,paths.App));
   }
   else if(swap.Restored){w.Stage(UpdatePrepare.Replace,null,true);w.Show(UpdateGuides.RollbackKeptNew(swap.Error,state.To,backupDir));}
   else{w.Stage(UpdatePrepare.Replace,null,true);Broken(w,paths,state,swap,UpdateGuides.RollbackBroken(swap,backupDir,paths.App));}
  }
  finally{Release(ref mutex);}
 }

 static void Broken(InstallWindow w,UpdatePaths paths,UpdateState state,SwapResult swap,Guide guide)
 {
  try{paths.WriteState(state with{Phase="broken",At=UpdatePaths.Now,Note=swap.Error});}catch(IOException){}catch(UnauthorizedAccessException){}
  w.Show(guide);
 }
 /// <summary>이번 시도가 만든 백업 폴더(아직 프로그램 파일이 옮겨지지 않은 것)를 지운다.</summary>
 internal static void RemoveNewBackup(UpdatePaths paths,string name)
 {
  if(string.IsNullOrEmpty(name)||name.IndexOfAny(Path.GetInvalidFileNameChars())>=0)return;
  string folder=Path.Combine(paths.Backups,name);
  if(UpdateBackup.Info(folder) is {Program:false}&&!UpdateFiles.Under(UpdateBackup.Program(folder)).Any())UpdateSwap.RemoveFolder(folder);
 }

 // ── 백업의 실행 파일이 이전 버전으로 되돌린다
 static void Restore(UpdatePaths paths,string backupDir,int? currentProcess,string? signal)
 {
  UseTheme(paths);
  var info=UpdateBackup.Info(backupDir);
  // 백업 정보를 읽지 못하면 이 실행 파일 자신의 버전을 쓴다(백업의 실행 파일이 돌고 있으므로 그 백업의 버전이다. 개발용 빌드면 모른다)
  string? own=AppVersion.IsDevelopment?null:AppVersion.Plain(AppVersion.Current);
  string backupVersion=info?.Version??own??"";
  string version=backupVersion.Length==0?"알 수 없음":"v"+backupVersion;   // 모르는 버전·시각은 모두 '알 수 없음'으로 적는다
  string now=UpdateManifest.Read(paths.App)?.Version is {} v?"v"+v:"알 수 없음";   // 상태 파일에도 이 값을 적는다(아래 RestoreWork. 모양을 바꾸지 않는다)
  if(currentProcess==null)
  {
   // 사용자가 백업 폴더의 실행 파일을 직접 켰다
   if(paths.Running().Count>0){GuideDialog.ShowAlone(UpdateGuides.BackupWhileRunning);return;}
   // 단추: 0 '복구', 1 '취소'(기본. 처음 초점이 '취소'라 연 직후의 Enter 와 Esc·닫기는 취소)
   if(GuideDialog.ShowAlone(UpdateGuides.RestoreAskDirect(now,backupVersion,info?.Made??"알 수 없음",UserFiles.RecordsMoved(paths.App)))!=0)return;
  }
  using var window=Window(paths,"이전 버전 복구",UpdateGuides.RestoreHead,now,version,new[]{"교체","재실행"},0,UpdateGuides.RestoreWait);
  window.Work=w=>
  {
   try{RestoreWork(w,paths,backupDir,backupVersion,now,currentProcess,signal);}
   catch(Exception e)
   {
    paths.Log("복구 과정 오류: "+e);
    w.Stage(0,null,true);
    w.Show(UpdateGuides.RestoreFailed(UpdateGuides.RestoreStop.Crashed,backupDir,paths.App,error:e));
   }
  };
  Application.Run(window);
 }

 /// <param name="backupVersion">되돌릴 버전(백업 정보의 버전. 그것을 읽지 못하면 이 실행 파일 자신의 버전. 모르면 빈 글). 복구 완료 상태에 적는다</param>
 static void RestoreWork(InstallWindow w,UpdatePaths paths,string backupDir,string backupVersion,string now,int? currentProcess,string? signal)
 {
  paths.Log($"이전 버전 복구 시작: {now} → v{backupVersion}  ({backupDir})");
  Signal(signal);
  w.Stage(0,UpdateGuides.RestoreWaitingExit);
  // 파일을 바꾸기 전에 멈추면 아무것도 바뀌지 않았다. 프로그램을 다시 켜 주지 않는다(안내가 켜져 있는 창이나 현재 버전을 쓰도록 알린다)
  void Unchanged(string why,Guide guide){paths.Log("되돌리지 않음: "+why);w.Stage(0,null,true);w.Show(guide);}
  bool Running()=>paths.Running().Count>0;
  if(!WaitExit(currentProcess,ExitWait)){Unchanged("프로그램이 끝나지 않음",UpdateGuides.RestoreFailed(UpdateGuides.RestoreStop.Running,backupDir,paths.App,running:true));return;}
  var mutex=Claim(paths);
  try
  {
   if(mutex==null||paths.Running().Count>0){Unchanged("프로그램이 켜져 있음",UpdateGuides.RestoreFailed(UpdateGuides.RestoreStop.Running,backupDir,paths.App,running:true));return;}
   w.Stage(0,UpdateGuides.RestoreSwapping);
   string program=UpdateBackup.Program(backupDir);var old=UpdateFiles.Under(program);
   string parked=Path.Combine(paths.Root,"replaced-"+Stamp);
   // 치울 것은 지금 설치의 목록에 있는 파일이다. 이 백업을 만든 교체가 도중에 멈춘 흔적이 있으면, 백업에 옮겨진 파일의 자리에 놓인 것도
   // 그 교체가 들여놓은 새 버전의 파일이므로 함께 치운다(그 교체는 시작하기 전에 그 자리들에 다른 파일이 없는 것을 확인했다).
   // 새 버전에만 있는 파일은 백업에 같은 이름이 없으므로, 풀어 놓은 새 버전의 목록과 크기·해시가 같은 것을 그 교체가 들여놓은 것으로 보고 치운다.
   // 남겨 두면 되돌린 뒤의 다음 업데이트가 그것을 "이 프로그램의 것이 아닌 파일"로 보고 멈춘다
   string backupName=Path.GetFileName(backupDir);
   bool interrupted=paths.ReadState() is {Phase:"swapping" or "broken"} stopped&&string.Equals(stopped.Backup,backupName,StringComparison.OrdinalIgnoreCase);
   var park=UpdateSwap.Current(paths.App,interrupted?old.Concat(UpdateSwap.Leftovers(paths,backupName)):null);
   var clash=UpdateSwap.Conflicts(paths.App,park,old);
   if(clash.Count>0)
   {
    Unchanged("이전 버전의 파일과 이름이 같은 것이 있음: "+string.Join(", ",clash.Take(5)),UpdateGuides.RestoreFailed(UpdateGuides.RestoreStop.Clash,backupDir,paths.App,running:Running(),clash:UpdateSwap.ListedLines(paths.App,clash)));
    return;
   }
   // 이 실행 파일이 백업 폴더에서 돌고 있으므로 옮기지 않고 복사한다. 백업은 그대로 남는다
   var swap=UpdateSwap.Exchange(paths.App,park,parked,program,old,false,null,paths.Log);
   if(!swap.Done)
   {
    w.Stage(0,null,true);
    if(swap.Restored)
    {
     Release(ref mutex);
     w.Show(UpdateGuides.RestoreFailed(UpdateGuides.RestoreStop.Restored,backupDir,paths.App,running:Running(),swap:swap));
    }
    else w.Show(UpdateGuides.RestoreFailed(UpdateGuides.RestoreStop.Broken,backupDir,paths.App,swap:swap));
    return;
   }
   // 되돌린 버전을 적는다. 다음에 켜진 이전 버전이 자기 버전과 같으면 '이전 버전으로 복구함'을 적는다(백업 정보가 깨졌으면 이 실행 파일 자신의 버전을 적는다. 빈 글을 적으면 그 표시를 건너뛴다. v1.1.x 로 복구하면 그 버전의 글 '이전 버전으로 되돌림'이 적힌다)
   paths.WriteState(new("restored",now.TrimStart('v'),backupVersion,Path.GetFileName(backupDir),UpdatePaths.Now));
   Release(ref mutex);
   w.Stage(1,UpdateGuides.RestoreStarting);
   if(Start(paths) is {} started)started.Dispose();
   else w.Show(UpdateGuides.DirectRun(UpdateGuides.RestoreTitle,paths.App));
  }
  finally{Release(ref mutex);}
 }
}

/// <summary>프로그램이 보통으로 켜졌을 때 지난 업데이트의 상태 파일을 보고 결과를 확정하고, 남은 임시 폴더와 지난 백업을 정리한다.</summary>
static class UpdateStartup
{
 /// <summary>업데이트 줄의 글 가운데 초록으로 적을 것(업데이트 완료, 이전 버전으로 복구함). 지난 업데이트의 실패·중단은 경고색이다.
 /// 프로그램 버전에는 '복구'를 쓴다</summary>
 public static bool Good(string note)=>note is "업데이트 완료" or "이전 버전으로 복구함";
 /// <summary>주 화면이 뜬 뒤에 부른다. Note 는 업데이트 줄에 보일 짧은 글, Notice 는 안내창으로 알릴 것(둘 다 없을 수 있다).</summary>
 public static (string? Note,Guide? Notice) Resolve(UpdatePaths paths)
 {
  var state=paths.ReadState();string me=AppVersion.Plain(AppVersion.Current);
  if(state==null)
  {
   // 상태 파일이 깨졌으면 그대로 둔다. 없으면 지난 시도가 남긴 임시 폴더만 치운다
   if(!File.Exists(paths.StateFile))CleanLater(paths);
   return(null,null);
  }
  switch(state.Phase)
  {
   case "swapped" when state.To==me:
    // 새 버전이 정상으로 떴다. 이때에야 이번 백업만 남기고 그 앞의 백업을 지운다
    paths.Log("업데이트 확인됨: "+AppVersion.Text(AppVersion.Current));
    UpdateBackup.Retain(paths,state.Backup);paths.ClearState();CleanLater(paths);
    return("업데이트 완료",null);
   case "restored" when state.To==me:
    paths.Log("이전 버전 복구 확인됨: "+AppVersion.Text(AppVersion.Current));
    paths.ClearState();CleanLater(paths);
    return("이전 버전으로 복구함",null);
   case "rolled-back":
    paths.ClearState();CleanLater(paths);
    return("지난 업데이트 실패",null);
   case "prepared":
    // 설치 과정이 시작되지 못하고 끝났다. 프로그램 파일은 바뀌지 않았다
    paths.Log("지난 업데이트가 교체 전에 멈춰 있어 정리함");
    UpdateProgram.RemoveNewBackup(paths,state.Backup);paths.ClearState();CleanLater(paths);
    return("지난 업데이트 실패",null);
   case "swapping":
   case "broken":
    // 상태 파일을 지우지 않으므로 켤 때마다 뜬다(다음 업데이트가 성공하거나 복구할 때까지). 백업의 이름이 비어 있으면 백업의 자리를 모른다
    return("지난 업데이트 중단됨",UpdateGuides.Interrupted(string.IsNullOrWhiteSpace(state.Backup)?null:Path.Combine(paths.Backups,state.Backup),paths.App));
   default:
    paths.ClearState();CleanLater(paths);
    return(null,null);
  }
 }

 /// <summary>풀어 놓은 새 버전, 내려받던 파일, 되돌릴 때 치워 둔 파일을 지운다. 설치를 맡았던 프로세스가 아직 끝나지 않았을 수 있어 조금씩 다시 해 본다.</summary>
 static void CleanLater(UpdatePaths paths)
 {
  if(!Directory.Exists(paths.Root))return;
  StopCleaning();
  var stop=new CancellationTokenSource();var token=stop.Token;
  var work=Task.Run(async()=>
  {
   for(int attempt=0;attempt<20;attempt++)
   {
    var left=new List<string>{paths.Staging,paths.Download};
    try{left.AddRange(Directory.EnumerateDirectories(paths.Root,"failed-*"));left.AddRange(Directory.EnumerateDirectories(paths.Root,"replaced-*"));}
    catch(IOException){}catch(UnauthorizedAccessException){}
    left=left.Where(Directory.Exists).ToList();
    if(left.Count==0)return;
    foreach(string folder in left){if(token.IsCancellationRequested)return;UpdateSwap.RemoveFolder(folder);}
    try{await Task.Delay(CleanPause,token);}catch(OperationCanceledException){return;}
   }
  });
  lock(CleanGate)cleaning=(stop,work);
 }
 internal static int CleanPause=2000;   // 다시 해 보는 사이(밀리초)
 static readonly object CleanGate=new();
 static (CancellationTokenSource Stop,Task Work)? cleaning;
 /// <summary>CleanLater 가 시작한 임시 폴더 정리를 멈추고 끝나기를 기다린다. 돌아온 뒤에는 그 정리가 아무것도 지우지 않는다.
 /// 새 업데이트를 준비하기 전에 부른다(임시 폴더 정리와 준비가 같은 자리 download, staging 을 쓴다).</summary>
 internal static void StopCleaning()
 {
  (CancellationTokenSource Stop,Task Work)? running;
  lock(CleanGate){running=cleaning;cleaning=null;}
  if(running is not {} was)return;
  was.Stop.Cancel();
  try{was.Work.Wait();}catch(AggregateException){}
  was.Stop.Dispose();
 }
}
