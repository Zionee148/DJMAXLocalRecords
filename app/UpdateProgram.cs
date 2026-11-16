using System.Diagnostics;
namespace LadderRecords;

// 자동 업데이트: 프로그램 폴더 밖(update\ 아래)에서 켜진 실행 파일이 하는 일.
//   update\staging\DJMAXLocalRecords\DJMAXLocalRecords.exe --apply-update <옛 프로세스> <신호>   새 버전이 스스로 설치한다
//   update\backup\<이름>\program\DJMAXLocalRecords.exe --restore-backup <지금 프로세스> <신호>   주 화면의 [이전 버전 복구]
//   update\backup\<이름>\program\DJMAXLocalRecords.exe                                         프로그램이 켜지지 않을 때 사용자가 직접 실행
// 지금 켜져 있는 실행 파일은 자기 자신의 파일을 바꿀 수 없어서, 바꾸는 일은 언제나 다른 폴더에서 켜진 쪽이 한다.
// 일하는 부분은 한 스레드에서 차례로 돈다(잡은 뮤텍스를 같은 스레드에서 놓아야 한다).
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
  if(args.Length==0&&UpdatePaths.FromStaging(here,out _))
  {
   NoticeDialog.ShowAlone("업데이트","이 폴더는 자동 업데이트가 내려받아 풀어 놓은 임시 파일입니다.\n프로그램 폴더의 DJMAXLocalRecords.exe 를 실행해 주세요.");
   return true;
  }
  return false;
 }

 static void UseTheme(UpdatePaths paths){try{Theme.Dark=UserOptions.Load(Path.Combine(paths.App,"preferences.json")).Dark;}catch{}}
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

 // ── 새 버전이 스스로 설치한다
 static void Apply(UpdatePaths paths,int oldProcess,string signal)
 {
  UseTheme(paths);
  var state=paths.ReadState();
  string heading=state==null?"업데이트":$"v{state.From}  →  v{state.To}";
  using var window=new InstallWindow("업데이트",heading,UpdatePrepare.Stages,UpdatePrepare.Replace);
  window.Work=w=>
  {
   try{ApplyWork(w,paths,state,oldProcess,signal);}
   catch(Exception e)
   {
    paths.Log("설치 과정 오류: "+e);
    w.Stage(UpdatePrepare.Replace,null,true);
    w.Notice("업데이트 실패","업데이트하는 도중 예상하지 못한 오류가 났습니다.\n"+e.Message+"\n\n기록과 설정은 건드리지 않았습니다. 프로그램이 켜지지 않는다면:\n"+(state==null?"자세한 안내: "+UpdateBackup.GuidePage:UpdateBackup.HowToCopyBack(Path.Combine(paths.Backups,state.Backup))));
   }
  };
  Application.Run(window);
 }

 static void ApplyWork(InstallWindow w,UpdatePaths paths,UpdateState? state,int oldProcess,string signal)
 {
  paths.Log("설치 과정 시작 ("+AppVersion.Text(AppVersion.Current)+")");
  // 준비됐다고 알린다. 이제 옛 프로그램이 끝난다
  Signal(signal);
  w.Stage(UpdatePrepare.Replace,"기존 프로그램이 끝나기를 기다리는 중");
  void Unchanged(string why)
  {
   paths.Log("교체하지 않음: "+why);paths.ClearState();
   if(state!=null)RemoveNewBackup(paths,state.Backup);
   w.Stage(UpdatePrepare.Replace,null,true);
   w.Notice("업데이트 실패",why+"\n\n프로그램과 기록은 바뀌지 않았습니다. 기존 버전을 계속 사용할 수 있습니다.");
  }
  if(!WaitExit(oldProcess,ExitWait)){Unchanged("기존 프로그램이 끝나지 않아 업데이트하지 않았습니다.");return;}
  var mutex=Claim(paths);
  if(mutex==null||paths.Running().Count>0){Release(ref mutex);Unchanged("프로그램이 다시 켜져 있어 업데이트하지 않았습니다. 프로그램을 끄고 다시 업데이트해 주세요.");return;}
  try
  {
   var incoming=UpdateManifest.Read(AppContext.BaseDirectory);
   if(state is not {Phase:"prepared"}||incoming==null||state.To!=AppVersion.Plain(AppVersion.Current)||incoming.Version!=state.To)
   {Release(ref mutex);Unchanged("업데이트 준비 정보가 맞지 않아 업데이트하지 않았습니다.");if(Start(paths) is {} again)again.Dispose();return;}
   string backupDir=Path.Combine(paths.Backups,state.Backup);
   if(UpdateBackup.Info(backupDir)==null)
   {Release(ref mutex);Unchanged("기록과 설정의 백업을 찾지 못해 업데이트하지 않았습니다.");if(Start(paths) is {} again)again.Dispose();return;}

   var bring=incoming.Files.Select(file=>file.Path).Append(UpdateManifest.FileName).ToList();
   var expected=incoming.Files.ToDictionary(file=>file.Path,StringComparer.OrdinalIgnoreCase);
   // 기존 프로그램이 넘기기 전에 본 것을 바꾸기 직전에 한 번 더 본다(그 사이에 파일이 놓였을 수 있다)
   try{UpdateSwap.CheckPlace(paths.App,bring);}
   catch(UpdateException e){Release(ref mutex);Unchanged(e.Message);if(Start(paths) is {} again)again.Dispose();return;}

   paths.WriteState(state with{Phase="swapping",At=UpdatePaths.Now});
   w.Stage(UpdatePrepare.Replace,"프로그램 파일을 바꾸는 중");
   var swap=UpdateSwap.Exchange(paths.App,UpdateSwap.Current(paths.App),UpdateBackup.Program(backupDir),AppContext.BaseDirectory,bring,false,expected,paths.Log);
   if(!swap.Done)
   {
    w.Stage(UpdatePrepare.Replace,null,true);
    if(swap.Restored)
    {
     UpdateSwap.RemoveFolder(backupDir);
     paths.WriteState(state with{Phase="rolled-back",At=UpdatePaths.Now,Note=swap.Error});
     Release(ref mutex);
     w.Notice("업데이트 실패","프로그램 파일을 바꾸지 못해 기존 버전으로 되돌렸습니다.\n까닭: "+swap.Error+"\n\n프로그램과 기록은 업데이트하기 전 그대로입니다. 확인을 누르면 기존 버전을 다시 켭니다.");
     if(Start(paths) is {} old)old.Dispose();
    }
    else Broken(w,paths,state,backupDir,swap);
    return;
   }
   UpdateBackup.MarkProgram(backupDir,true);
   paths.WriteState(state with{Phase="swapped",At=UpdatePaths.Now});
   Release(ref mutex);

   w.Stage(UpdatePrepare.Restart,"새 버전을 켜는 중");
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
    bool back=w.Ask("업데이트",alive?"새 버전이 2분이 지나도록 시작을 마치지 못했습니다.":"새 버전이 정상으로 시작되지 않았습니다.",
     "되돌려도 기록과 설정은 지금 것을 그대로 둡니다."+(alive?"\n[아니요]를 누르면 조금 더 기다립니다.":"\n[아니요]를 누르면 새 버전을 그대로 둡니다.\n\n나중에 "+UpdateBackup.HowToRestore(backupDir)),
     $"이전 버전(v{state.From})으로 되돌릴까요?");
    if(!back){if(alive){w.Stage(UpdatePrepare.Restart,"새 버전이 뜨기를 기다리는 중");continue;}return;}
    if(started is {HasExited:false})
    {
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
  w.Stage(UpdatePrepare.Replace,"이전 버전으로 되돌리는 중");
  var mutex=Claim(paths);
  try
  {
   if(mutex==null||paths.Running().Count>0)
   {w.Notice("업데이트","프로그램이 켜져 있어 되돌리지 못했습니다. 프로그램을 끈 뒤에 다시 해 주세요.\n\n"+UpdateBackup.HowToRestore(backupDir));return;}
   string program=UpdateBackup.Program(backupDir);var old=UpdateFiles.Under(program);
   string parked=Path.Combine(paths.Root,"failed-"+Stamp);
   // incoming 은 이 과정이 방금 들여놓은 새 버전의 파일이다. 목록을 읽지 못하더라도 이 프로그램의 것이 확실하다
   var swap=UpdateSwap.Exchange(paths.App,UpdateSwap.Current(paths.App,incoming),parked,program,old,true,null,paths.Log);
   if(swap.Done)
   {
    UpdateSwap.RemoveFolder(parked);UpdateBackup.MarkProgram(backupDir,false);
    paths.WriteState(state with{Phase="rolled-back",At=UpdatePaths.Now,Note="새 버전이 시작되지 않음"});
    Release(ref mutex);
    w.Notice("업데이트","이전 버전으로 되돌렸습니다. 기록과 설정은 그대로입니다.\n확인을 누르면 이전 버전을 켭니다.");
    if(Start(paths) is {} old2)old2.Dispose();
   }
   else if(swap.Restored)
    w.Notice("업데이트","이전 버전으로 되돌리지 못했습니다. 새 버전의 파일은 그대로 있습니다.\n까닭: "+swap.Error+"\n\n"+UpdateBackup.HowToRestore(backupDir));
   else Broken(w,paths,state,backupDir,swap);
  }
  finally{Release(ref mutex);}
 }

 static void Broken(InstallWindow w,UpdatePaths paths,UpdateState state,string backupDir,SwapResult swap)
 {
  try{paths.WriteState(state with{Phase="broken",At=UpdatePaths.Now,Note=swap.Error});}catch(IOException){}catch(UnauthorizedAccessException){}
  w.Notice("업데이트 실패","프로그램 파일을 바꾸다 실패했고, 되돌리지도 못했습니다. 프로그램 파일이 섞여 있을 수 있습니다.\n까닭: "+swap.Error+"\n되돌리지 못한 것: "+swap.RestoreError
   +"\n\n기록과 설정은 건드리지 않았습니다.\n"+UpdateBackup.HowToCopyBack(backupDir));
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
  string version=info==null?"이전 버전":"v"+info.Version;
  string now=UpdateManifest.Read(paths.App)?.Version is {} v?"v"+v:"알 수 없음";
  if(currentProcess==null)
  {
   // 사용자가 백업 폴더의 실행 파일을 직접 켰다
   if(paths.Running().Count>0)
   {
    NoticeDialog.ShowAlone("이전 버전 복구","프로그램이 켜져 있습니다. 프로그램을 끈 뒤 다시 실행해 주세요.\n(프로그램이 켜진다면 주 화면의 [이전 버전 복구] 단추로도 되돌릴 수 있습니다)");
    return;
   }
   using var ask=new ChoiceDialog("이전 버전 복구",$"이 폴더는 자동 업데이트가 남긴 {version} 의 백업입니다.",
    new InfoBox(){Controls={Boxes.Stats(12.5f,("지금 버전",now,null),("되돌릴 버전",version,"accent"),("백업한 때",info?.Made??"-",null))}},
    "기록과 설정은 지금 것을 그대로 둡니다. 프로그램 파일만 이전 버전으로 바꿉니다.","이 버전으로 되돌릴까요?"){StartPosition=FormStartPosition.CenterScreen,ShowInTaskbar=true};
   if(ask.ShowDialog()!=DialogResult.Yes)return;
  }
  using var window=new InstallWindow("이전 버전 복구",$"{now}  →  {version}",new[]{"교체","재실행"},0);
  window.Work=w=>
  {
   try{RestoreWork(w,paths,backupDir,info,now,currentProcess,signal);}
   catch(Exception e)
   {
    paths.Log("복구 과정 오류: "+e);
    w.Stage(0,null,true);
    w.Notice("이전 버전 복구","되돌리는 도중 예상하지 못한 오류가 났습니다.\n"+e.Message+"\n\n기록과 설정은 건드리지 않았습니다.\n"+UpdateBackup.HowToCopyBack(backupDir));
   }
  };
  Application.Run(window);
 }

 static void RestoreWork(InstallWindow w,UpdatePaths paths,string backupDir,BackupInfo? info,string now,int? currentProcess,string? signal)
 {
  paths.Log($"이전 버전 복구 시작: {now} → v{info?.Version}  ({backupDir})");
  Signal(signal);
  w.Stage(0,"프로그램이 끝나기를 기다리는 중");
  void Unchanged(string why){paths.Log("되돌리지 않음: "+why);w.Stage(0,null,true);w.Notice("이전 버전 복구",why+"\n\n프로그램과 기록은 바뀌지 않았습니다.");}
  if(!WaitExit(currentProcess,ExitWait)){Unchanged("프로그램이 끝나지 않아 되돌리지 않았습니다.");return;}
  var mutex=Claim(paths);
  try
  {
   if(mutex==null||paths.Running().Count>0){Unchanged("프로그램이 켜져 있어 되돌리지 않았습니다. 프로그램을 끄고 다시 해 주세요.");return;}
   w.Stage(0,"프로그램 파일을 바꾸는 중");
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
    Unchanged("프로그램 폴더에 이전 버전의 파일과 이름이 같은 파일이나 폴더가 있습니다. 지금 설치의 프로그램 파일 목록에 없는 것이어서 덮어쓰지 않고 멈췄습니다.\n"+UpdateSwap.Listed(paths.App,clash)
     +"\n\n위의 것을 다른 곳으로 옮긴 뒤 다시 해 주세요. 직접 되돌리려면:\n"+UpdateBackup.HowToCopyBack(backupDir));
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
     w.Notice("이전 버전 복구","프로그램 파일을 바꾸지 못해 되돌리지 않았습니다. 지금 버전 그대로입니다.\n까닭: "+swap.Error);
    }
    else w.Notice("이전 버전 복구","프로그램 파일을 바꾸다 실패했고, 원래대로 돌려놓지도 못했습니다. 프로그램 파일이 섞여 있을 수 있습니다.\n까닭: "+swap.Error+"\n되돌리지 못한 것: "+swap.RestoreError
     +"\n\n기록과 설정은 건드리지 않았습니다.\n"+UpdateBackup.HowToCopyBack(backupDir));
    return;
   }
   paths.WriteState(new("restored",now.TrimStart('v'),info?.Version??"",Path.GetFileName(backupDir),UpdatePaths.Now));
   Release(ref mutex);
   w.Stage(1,"이전 버전을 켜는 중");
   if(Start(paths) is {} started)started.Dispose();
   else w.Notice("이전 버전 복구","이전 버전으로 되돌렸지만 켜지 못했습니다. 프로그램 폴더의 DJMAXLocalRecords.exe 를 직접 실행해 주세요.");
  }
  finally{Release(ref mutex);}
 }
}

/// <summary>프로그램이 보통으로 켜졌을 때 지난 업데이트의 뒷정리를 한다.</summary>
static class UpdateStartup
{
 /// <summary>주 화면이 뜬 뒤에 부른다. Note 는 업데이트 줄에 보일 짧은 글, Notice 는 창으로 알릴 글(둘 다 없을 수 있다).</summary>
 public static (string? Note,string? Notice) Resolve(UpdatePaths paths)
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
    return("이전 버전으로 되돌림",null);
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
    return("지난 업데이트 중단됨",
     "지난 업데이트가 프로그램 파일을 바꾸는 도중에 멈췄습니다. 프로그램 파일이 섞여 있을 수 있습니다.\n기록과 설정은 건드리지 않았습니다.\n\n"
     +UpdateBackup.HowToCopyBack(Path.Combine(paths.Backups,state.Backup))
     +"\n\n프로그램이 문제없이 동작한다면 [업데이트 확인]으로 다시 업데이트해도 됩니다.");
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
 /// <summary>뒷정리를 멈추고 끝나기를 기다린다. 돌아온 뒤에는 뒷정리가 아무것도 지우지 않는다.
 /// 새 업데이트를 준비하기 전에 부른다(뒷정리와 준비가 같은 자리 download, staging 을 쓴다).</summary>
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
