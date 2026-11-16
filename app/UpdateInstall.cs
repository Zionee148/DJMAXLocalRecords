using System.Diagnostics;
using System.Text.Json;
namespace LadderRecords;

// 자동 업데이트: 프로그램 파일을 바꾸고, 실패하면 되돌리고, 백업을 남기고 정리하는 부분 (기획서 5·6·7절).
//
// 프로그램 폴더 안의 자리
//   update\download\            내려받는 배포 파일(끝나면 지운다)
//   update\staging\DJMAXLocalRecords\   풀어 놓은 새 버전. 교체는 여기의 새 실행 파일이 한다(지금 실행 파일은 자기 자신을 바꿀 수 없다)
//   update\backup\<이름>\        직전 버전 하나: program\(옛 프로그램 파일), data\(업데이트 직전의 기록·설정 사본), backup-info.json
//   update\state.json           어디까지 했는지
//   update\update.log           한 일의 기록
// 바꾸는 것은 프로그램 파일 목록(update-manifest.json)에 있는 파일뿐이다. 기록·설정·진단 기록과 사용자가 둔 파일은 옮기지도 지우지도 않는다.

/// <summary>어디까지 했는지. Phase: prepared(교체할 준비가 됨) → swapping(교체 중) → swapped(새 버전을 켬, 아직 확인 전) → 새 버전이 정상으로 뜨면 지운다.
/// rolled-back(교체나 시작에 실패해 옛 버전으로 되돌림), broken(되돌리지 못함), restored(사용자가 이전 버전으로 되돌림).</summary>
record UpdateState(string Phase,string From,string To,string Backup,string At,string? Note=null);

record BackupInfo(string Product,string Version,string ReplacedBy,string Made,bool Program);

sealed class UpdatePaths(string appDir)
{
 public string App{get;}=Path.GetFullPath(appDir).TrimEnd(Path.DirectorySeparatorChar);
 public string Root=>Path.Combine(App,"update");
 public string Download=>Path.Combine(Root,"download");
 public string Staging=>Path.Combine(Root,"staging");
 public string StagedApp=>Path.Combine(Staging,UpdateManifest.ProductName);
 public string Backups=>Path.Combine(Root,"backup");
 public string StateFile=>Path.Combine(Root,"state.json");
 public string LogFile=>Path.Combine(Root,"update.log");
 public string Executable=>Path.Combine(App,UpdateManifest.Executable);

 public UpdateState? ReadState()
 {
  try{return File.Exists(StateFile)?JsonSerializer.Deserialize<UpdateState>(File.ReadAllText(StateFile)):null;}
  catch(JsonException){return null;}catch(IOException){return null;}catch(UnauthorizedAccessException){return null;}
 }
 public void WriteState(UpdateState state)
 {
  Directory.CreateDirectory(Root);string tmp=StateFile+".tmp";
  File.WriteAllText(tmp,JsonSerializer.Serialize(state,Indented));File.Move(tmp,StateFile,true);
 }
 public void ClearState(){try{File.Delete(StateFile);}catch(IOException){}catch(UnauthorizedAccessException){}}
 public void Log(string line)
 {
  try{Directory.CreateDirectory(Root);File.AppendAllText(LogFile,$"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {line}{Environment.NewLine}");}
  catch(IOException){}catch(UnauthorizedAccessException){}
 }
 internal static readonly JsonSerializerOptions Indented=new(){WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping};
 public static string Now=>DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

 /// <summary>실행 파일이 프로그램 폴더의 update\backup\이름\program 에서 켜졌으면 그 프로그램 폴더와 백업 폴더를 돌려준다.</summary>
 public static bool FromBackup(string baseDir,out string appDir,out string backupDir)
 {
  appDir=backupDir="";
  var program=new DirectoryInfo(Path.GetFullPath(baseDir).TrimEnd(Path.DirectorySeparatorChar));
  if(!Named(program,"program")||program.Parent is not {} backup||!Named(backup.Parent,"backup")||!Named(backup.Parent!.Parent,"update")||backup.Parent.Parent!.Parent is not {} app)return false;
  if(!File.Exists(Path.Combine(backup.FullName,UpdateBackup.InfoName)))return false;
  appDir=app.FullName;backupDir=backup.FullName;return true;
 }
 /// <summary>실행 파일이 프로그램 폴더의 update\staging\DJMAXLocalRecords 에서 켜졌으면 그 프로그램 폴더를 돌려준다.</summary>
 public static bool FromStaging(string baseDir,out string appDir)
 {
  appDir="";
  var staged=new DirectoryInfo(Path.GetFullPath(baseDir).TrimEnd(Path.DirectorySeparatorChar));
  if(!Named(staged,UpdateManifest.ProductName)||!Named(staged.Parent,"staging")||!Named(staged.Parent!.Parent,"update")||staged.Parent.Parent!.Parent is not {} app)return false;
  appDir=app.FullName;return true;
 }
 static bool Named(DirectoryInfo? folder,string name)=>folder!=null&&folder.Name.Equals(name,StringComparison.OrdinalIgnoreCase);

 /// <summary>프로그램 폴더의 실행 파일로 켜진 다른 프로세스들(update\ 아래에서 켜진 것과 자기 자신은 뺀다).</summary>
 public List<int> Running()
 {
  var found=new List<int>();
  foreach(var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(UpdateManifest.Executable)))
   using(process)
   {
    try
    {
     if(process.Id==Environment.ProcessId||process.HasExited)continue;
     string? file=process.MainModule?.FileName;
     if(file!=null&&string.Equals(Path.GetFullPath(file),Executable,StringComparison.OrdinalIgnoreCase))found.Add(process.Id);
    }
    catch(System.ComponentModel.Win32Exception){}catch(InvalidOperationException){}
   }
  return found;
 }
 public long FreeBytes()=>new DriveInfo(Path.GetPathRoot(App)!).AvailableFreeSpace;
}

static class UpdateBackup
{
 public const string InfoName="backup-info.json";
 /// <summary>손으로 하는 업데이트·복구의 안내가 있는 곳. 프로그램 폴더에는 안내 글을 따로 두지 않는다(사용자 결정, 2026-11-16).
 /// 이 주소는 내보낸 버전들이 계속 가리키므로 바꾸지 않는다(저장소의 첫 화면이고, 자세한 사용법은 거기서 이어진다).</summary>
 public const string GuidePage="https://github.com/Zionee148/DJMAXLocalRecords";
 /// <summary>백업의 실행 파일로 이전 버전으로 되돌리는 방법(백업에 옛 프로그램이 온전히 있을 때 알린다).</summary>
 public static string HowToRestore(string backupDir)=>"이전 버전으로 되돌리려면 아래 파일을 실행해 주세요(되돌릴지 묻는 창이 뜹니다).\n"+Path.Combine(Program(backupDir),UpdateManifest.Executable)+"\n자세한 안내: "+GuidePage;
 /// <summary>손으로 복사해 되돌리는 방법(프로그램 파일이 섞였거나 백업의 실행 파일이 켜지지 않을 때 알린다).</summary>
 public static string HowToCopyBack(string backupDir)=>"이전 버전으로 되돌리려면 프로그램을 모두 끄고, 아래 폴더 안의 모든 파일과 폴더를 프로그램 폴더에 복사해 덮어쓴 뒤 다시 켜 주세요.\n"+Program(backupDir)+"\n자세한 안내: "+GuidePage;
 public static string Program(string backupDir)=>Path.Combine(backupDir,"program");
 public static string Data(string backupDir)=>Path.Combine(backupDir,"data");

 public static BackupInfo? Info(string backupDir)
 {
  try
  {
   string path=Path.Combine(backupDir,InfoName);
   return File.Exists(path)?JsonSerializer.Deserialize<BackupInfo>(File.ReadAllText(path)):null;
  }
  catch(JsonException){return null;}catch(IOException){return null;}catch(UnauthorizedAccessException){return null;}
 }
 static void WriteInfo(string backupDir,BackupInfo info)=>File.WriteAllText(Path.Combine(backupDir,InfoName),JsonSerializer.Serialize(info,UpdatePaths.Indented));

 /// <summary>새 백업 폴더를 만들고 업데이트 직전의 기록·설정을 복사해 넣는다. 복사한 것이 원본과 같은지 견준다. 폴더의 이름을 돌려준다.
 /// 프로그램 파일은 교체할 때 이 폴더의 program\ 으로 옮겨진다.</summary>
 public static string Create(UpdatePaths paths,Version from,Version to)
 {
  string name=$"{AppVersion.Text(from)}-{DateTime.Now:yyyyMMdd-HHmmss}";
  string folder=Path.Combine(paths.Backups,name);
  if(Directory.Exists(folder))throw new IOException("같은 이름의 백업 폴더가 이미 있습니다: "+folder);
  Directory.CreateDirectory(Data(folder));Directory.CreateDirectory(Program(folder));
  WriteInfo(folder,new(UpdateManifest.ProductName,AppVersion.Plain(from),AppVersion.Plain(to),UpdatePaths.Now,false));
  foreach(string file in UpdateFiles.DataFiles)
  {
   string source=Path.Combine(paths.App,file);
   if(!File.Exists(source))continue;
   string copy=Path.Combine(Data(folder),file);
   string before=UpdateFiles.Sha256(source);
   File.Copy(source,copy);
   if(UpdateFiles.Sha256(copy)!=before||UpdateFiles.Sha256(source)!=before)throw new IOException("백업하는 사이에 파일이 바뀌었습니다: "+file);
  }
  return name;
 }
 /// <summary>옛 프로그램 파일이 다 옮겨진 뒤에 부른다. 이때부터 이 폴더로 이전 버전을 되살릴 수 있다.</summary>
 public static void MarkProgram(string backupDir,bool present)
 {
  if(Info(backupDir) is {} info)WriteInfo(backupDir,info with{Program=present});
 }
 /// <summary>되돌릴 수 있는 백업(프로그램 파일이 든 것) 가운데 가장 나중에 만든 것.</summary>
 public static (string Folder,BackupInfo Info)? Latest(UpdatePaths paths)
 {
  if(!Directory.Exists(paths.Backups))return null;
  return Directory.EnumerateDirectories(paths.Backups)
   .Select(folder=>(Folder:folder,Info:Info(folder)))
   .Where(b=>b.Info is {Program:true}&&File.Exists(Path.Combine(Program(b.Folder),UpdateManifest.Executable)))
   .OrderByDescending(b=>b.Info!.Made,StringComparer.Ordinal).Select(b=>((string,BackupInfo)?)(b.Folder,b.Info!)).FirstOrDefault();
 }

 /// <summary>keep 만 남기고 자동 업데이트가 만든 백업 폴더를 지운다. backup-info.json 이 든 폴더만 자동 업데이트의 것으로 본다(사용자가 둔 폴더와 파일은 건드리지 않는다).</summary>
 public static void Retain(UpdatePaths paths,string? keep)
 {
  if(!Directory.Exists(paths.Backups))return;
  foreach(string folder in Directory.EnumerateDirectories(paths.Backups).ToList())
  {
   if(keep!=null&&Path.GetFileName(folder).Equals(keep,StringComparison.OrdinalIgnoreCase))continue;
   if(!File.Exists(Path.Combine(folder,InfoName)))continue;
   UpdateSwap.RemoveFolder(folder);paths.Log("지난 백업을 지움: "+Path.GetFileName(folder));
  }
 }
}

/// <summary>Done: 바꿨다. 아니면 Restored 가 true 일 때 바꾸기 전으로 되돌려 놓았고, false 면 되돌리지 못해 파일이 섞여 있을 수 있다.</summary>
record SwapResult(bool Done,bool Restored,string? Error,string? RestoreError=null);

static class UpdateSwap
{
 const int Attempts=12;internal static int Pause=250;   // 다시 해 보는 사이(밀리초)
#if DEVELOPMENT_CHECKS
 /// <summary>검사가 실패를 끼워 넣는 자리. (한 일, 경로)를 받아 예외를 던지면 그 일이 실패한 것이 된다.</summary>
 internal static Action<string,string>? Probe;
#endif
 static void Try(string what,string path,Action action)
 {
  for(int attempt=1;;attempt++)
  {
   try
   {
#if DEVELOPMENT_CHECKS
    Probe?.Invoke(what,path);
#endif
    action();return;
   }
   // 백신이나 동기화 도구가 파일을 잠깐 잡고 있을 수 있어 몇 번 다시 해 본다
   catch(Exception e)when((e is IOException or UnauthorizedAccessException)&&attempt<Attempts){Thread.Sleep(Pause);}
  }
 }
 static void Move(string from,string to)=>Try("move",from,()=>{Directory.CreateDirectory(Path.GetDirectoryName(to)!);File.Move(from,to,false);});
 static void Copy(string from,string to)=>Try("copy",to,()=>{Directory.CreateDirectory(Path.GetDirectoryName(to)!);File.Copy(from,to,false);});
 static void Delete(string path)=>Try("delete",path,()=>{if(File.Exists(path)){File.SetAttributes(path,FileAttributes.Normal);File.Delete(path);}});
 public static void RemoveFolder(string folder)
 {
  try{if(Directory.Exists(folder))Directory.Delete(folder,true);}
  catch(IOException){}catch(UnauthorizedAccessException){}
 }

 /// <summary>프로그램 폴더에서 치워도 되는 이 프로그램의 파일: 지금 설치의 목록(update-manifest.json)에 있는 것과 그 목록 파일(개인 자료는 빼고, 실제로 있는 것만).
 /// 목록에 없는 파일은 새 버전의 파일과 이름이 같아도 이 프로그램의 것으로 보지 않는다(사용자가 둔 파일일 수 있다).
 /// placed: 이 교체 과정이 프로그램 폴더에 직접 들여놓은 것으로 확인된 이름(되돌릴 때만 준다).</summary>
 public static List<string> Current(string appDir,IEnumerable<string>? placed=null)
 {
  var names=new HashSet<string>(placed??[],StringComparer.OrdinalIgnoreCase){UpdateManifest.FileName};
  if(UpdateManifest.Read(appDir) is {} manifest)foreach(var file in manifest.Files)names.Add(file.Path);
  return names.Where(name=>UpdateFiles.IsSafe(name)&&!UpdateFiles.IsProtected(name)&&File.Exists(Path.Combine(appDir,UpdateFiles.Local(name)))).OrderBy(n=>n,StringComparer.OrdinalIgnoreCase).ToList();
 }

 /// <summary>bring 을 들여오려면 건드려야 하는, 이 프로그램의 것이 아닌 파일·폴더(park 는 치워 둘 이 프로그램의 파일). 하나라도 있으면 바꾸기를 시작하지 않는다.</summary>
 public static List<string> Conflicts(string appDir,IEnumerable<string> park,IEnumerable<string> bring)
 {
  var ours=new HashSet<string>(park,StringComparer.OrdinalIgnoreCase);var found=new List<string>();
  static string? Up(string name){int cut=name.LastIndexOf('/');return cut<0?null:name[..cut];}
  foreach(string file in bring)
  {
   string full=Path.Combine(appDir,UpdateFiles.Local(file));
   if(!ours.Contains(file)&&(File.Exists(full)||Directory.Exists(full))){found.Add(file);continue;}
   // 들여올 파일의 윗폴더가 될 자리에 파일이 있어도 들여올 수 없다
   for(string? up=Up(file);up!=null;up=Up(up))
    if(!ours.Contains(up)&&File.Exists(Path.Combine(appDir,UpdateFiles.Local(up)))){found.Add(up);break;}
  }
  return found.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n=>n,StringComparer.OrdinalIgnoreCase).ToList();
 }
 /// <summary>도중에 멈춘 교체가 프로그램 폴더에 들여놓고 간 새 버전의 파일 가운데 지금 설치의 목록에 없는 것.
 /// 그 교체의 것이라고 확인되는 것만 고른다: 상태 파일이 교체가 도중에 멈췄다고 적고(backup 을 주면 그 백업을 만든 교체일 때만),
 /// 풀어 놓은 새 버전이 그 버전 그대로 남아 있고, 파일의 크기와 SHA-256 이 그 목록과 같아야 한다.
 /// 하나라도 확인할 수 없으면 고르지 않는다(사용자의 파일일 수 있다).</summary>
 public static List<string> Leftovers(UpdatePaths paths,string? backup=null)
 {
  if(paths.ReadState() is not {Phase:"swapping" or "broken"} state)return [];
  if(backup!=null&&!string.Equals(state.Backup,backup,StringComparison.OrdinalIgnoreCase))return [];
  if(UpdateManifest.Read(paths.StagedApp) is not {} staged||staged.Version!=state.To)return [];
  var ours=new HashSet<string>(Current(paths.App),StringComparer.OrdinalIgnoreCase);var found=new List<string>();
  foreach(var file in staged.Files)
  {
   if(ours.Contains(file.Path)||!UpdateFiles.IsSafe(file.Path)||UpdateFiles.IsProtected(file.Path))continue;
   string full=Path.Combine(paths.App,UpdateFiles.Local(file.Path));
   try{if(File.Exists(full)&&new FileInfo(full).Length==file.Size&&string.Equals(UpdateFiles.Sha256(full),file.Sha256,StringComparison.OrdinalIgnoreCase))found.Add(file.Path);}
   catch(IOException){}catch(UnauthorizedAccessException){}
  }
  return found;
 }
 /// <summary>새 업데이트를 준비하기 전에, 도중에 멈춘 지난 교체가 남긴 새 버전의 파일(Leftovers)을 update\failed-… 로 치워 둔다(다음에 켜질 때 지워진다).
 /// 백업 폴더를 손으로 복사해 되돌린 뒤에는 그 파일들이 남는다. 그대로 두면 다음 업데이트가 "이 프로그램의 것이 아닌 파일"로 보고 멈춘다.
 /// 옮기지 못한 것은 그대로 둔다. 치운 수를 돌려준다.</summary>
 public static int SetAside(UpdatePaths paths)
 {
  if(UpdateManifest.Read(paths.App)==null)return 0;   // 목록이 없는 설치는 어차피 업데이트하지 않는다
  var left=Leftovers(paths);
  if(left.Count==0)return 0;
  string aside=Path.Combine(paths.Root,"failed-"+DateTime.Now.ToString("yyyyMMdd-HHmmss"));int moved=0;
  foreach(string file in left)
   try{Move(Path.Combine(paths.App,UpdateFiles.Local(file)),Path.Combine(aside,UpdateFiles.Local(file)));moved++;}
   catch(Exception e)when(e is IOException or UnauthorizedAccessException){paths.Log("지난 교체가 남긴 파일을 치우지 못함: "+file+" ("+e.Message+")");}
  Tidy(paths.App,left);
  paths.Log($"지난 교체가 남긴 새 버전의 파일 {moved}개를 치워 둠: {aside}");
  return moved;
 }
 /// <summary>풀어 놓은 새 버전(목록이 확인된 것)에서 들여올 파일의 이름: 목록의 파일들과 목록 파일.</summary>
 public static List<string> Incoming(string stagedApp)
  =>(UpdateManifest.Read(stagedApp)??throw new UpdateException("풀어 놓은 새 버전의 프로그램 파일 목록을 읽지 못했습니다.")).Files.Select(file=>file.Path).Append(UpdateManifest.FileName).ToList();
 /// <summary>겹치는 것을 알리는 글의 목록 부분(많으면 앞의 몇 개만).</summary>
 public static string Listed(string appDir,IReadOnlyList<string> names)
  =>string.Join("\n",names.Take(5).Select(name=>"  "+Path.Combine(appDir,UpdateFiles.Local(name))))+(names.Count>5?$"\n  (그 밖에 {names.Count-5}개)":"");

 /// <summary>새 버전의 파일(bring)을 들여와도 되는 폴더인지 본다. 안 되면 UpdateException. 프로그램을 바꾸기 전에 부른다.</summary>
 public static void CheckPlace(string appDir,IReadOnlyList<string> bring)
 {
  if(UpdateManifest.Read(appDir)==null)
   throw new UpdateException($"지금 설치된 프로그램의 파일 목록({UpdateManifest.FileName})이 없거나 읽히지 않아, 어느 파일을 바꿔도 되는지 알 수 없습니다.\n자동으로 업데이트할 수 없습니다. 새 버전을 직접 받아 설치해 주세요.\n자세한 안내: "+UpdateBackup.GuidePage);
  var clash=Conflicts(appDir,Current(appDir),bring);
  if(clash.Count>0)
   throw new UpdateException("프로그램 폴더에 새 버전의 파일과 이름이 같은 파일이나 폴더가 있습니다. 이 프로그램의 파일이 아니어서 덮어쓰지 않고 멈췄습니다.\n아래의 것을 다른 곳으로 옮긴 뒤 다시 업데이트해 주세요.\n"+Listed(appDir,clash));
 }

 /// <summary>프로그램 파일을 바꾼다: appDir 의 park 파일들을 parkDir 로 옮겨 두고, sourceDir 의 bring 파일들을 appDir 로 들여온다(move 면 옮기고 아니면 복사한다).
 /// 들여온 파일은 expected(있으면)의 크기·해시, 없으면 원본과 견준다. 도중에 실패하면 들여온 것을 치우고 옮겨 둔 것을 제자리에 돌려놓는다.</summary>
 public static SwapResult Exchange(string appDir,IReadOnlyList<string> park,string parkDir,string sourceDir,IReadOnlyList<string> bring,bool move,IReadOnlyDictionary<string,ManifestFile>? expected,Action<string> log)
 {
  var parked=new List<string>();var brought=new List<string>();
  string At(string root,string relative)=>Path.Combine(root,UpdateFiles.Local(relative));
  try
  {
   foreach(string file in park){Move(At(appDir,file),At(parkDir,file));parked.Add(file);}
   log($"프로그램 파일 {parked.Count}개를 옮겨 둠: {parkDir}");
   foreach(string file in bring)
   {
    string source=At(sourceDir,file),target=At(appDir,file);
    // 원본과 견줄 값은 옮기기 전에 구해 둔다
    long size=expected!=null&&expected.TryGetValue(file,out var listed)?listed.Size:new FileInfo(source).Length;
    string hash=expected!=null&&expected.TryGetValue(file,out listed)?listed.Sha256:UpdateFiles.Sha256(source);
    // 치워 둔 뒤에도 그 자리에 무엇이 있으면 우리가 아는 프로그램 파일이 아니다. 덮어쓰지 않고 멈춘다(되돌릴 때 지우지도 않는다)
    if(File.Exists(target)||Directory.Exists(target))throw new IOException("들여올 자리에 다른 것이 있습니다: "+file);
    brought.Add(file);
    if(move)Move(source,target);else Copy(source,target);
    if(new FileInfo(target).Length!=size||!string.Equals(UpdateFiles.Sha256(target),hash,StringComparison.OrdinalIgnoreCase))throw new IOException("들여온 파일이 원본과 다릅니다: "+file);
   }
   log($"프로그램 파일 {brought.Count}개를 들여옴: {sourceDir}");
   Tidy(appDir,park);
   return new(true,false,null);
  }
  catch(Exception e)when(e is IOException or UnauthorizedAccessException)
  {
   log("교체 실패: "+e.Message);
   var problems=new List<string>();
   for(int i=brought.Count-1;i>=0;i--)
    try
    {
     string target=At(appDir,brought[i]);
     if(!File.Exists(target))continue;
     if(move)Move(target,At(sourceDir,brought[i]));else Delete(target);
    }
    catch(Exception undo)when(undo is IOException or UnauthorizedAccessException){problems.Add(brought[i]+": "+undo.Message);}
   for(int i=parked.Count-1;i>=0;i--)
    try{Move(At(parkDir,parked[i]),At(appDir,parked[i]));}
    catch(Exception undo)when(undo is IOException or UnauthorizedAccessException){problems.Add(parked[i]+": "+undo.Message);}
   if(problems.Count==0){log("바꾸기 전으로 되돌림");return new(false,true,e.Message);}
   log("되돌리지 못한 파일: "+string.Join(" | ",problems));
   return new(false,false,e.Message,string.Join("\n",problems.Take(5)));
  }
 }
 /// <summary>파일을 옮긴 뒤 비어 버린 폴더를 지운다(비어 있지 않으면 그대로 둔다. 프로그램 폴더 자체와 개인 자료의 폴더는 건드리지 않는다).</summary>
 static void Tidy(string appDir,IEnumerable<string> moved)
 {
  var folders=moved.Select(file=>Path.GetDirectoryName(UpdateFiles.Local(file))).Where(d=>!string.IsNullOrEmpty(d)).Cast<string>()
   .SelectMany(d=>{var all=new List<string>();for(string? at=d;!string.IsNullOrEmpty(at);at=Path.GetDirectoryName(at))all.Add(at);return all;})
   .Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(d=>d.Length);
  foreach(string folder in folders)
  {
   if(UpdateFiles.IsProtected(folder.Replace('\\','/')))continue;
   try{string full=Path.Combine(appDir,folder);if(Directory.Exists(full)&&!Directory.EnumerateFileSystemEntries(full).Any())Directory.Delete(full,false);}
   catch(IOException){}catch(UnauthorizedAccessException){}
  }
 }
}

/// <summary>지금 켜진 프로그램이 업데이트를 준비하는 부분: 내려받기 → 파일 확인 → 백업. 여기까지는 취소할 수 있고, 프로그램과 기록을 바꾸지 않는다.</summary>
static class UpdatePrepare
{
 public const int Download=0,Verify=1,Backup=2,Replace=3,Restart=4;
 public static readonly string[] Stages={"다운로드","파일 확인","백업","교체","재실행"};

 /// <summary>쓸 수 있는 폴더인지, 공간이 넉넉한지 본다. 안 되면 UpdateException.</summary>
 public static void CheckFolder(UpdatePaths paths,long packageBytes)
 {
  try
  {
   Directory.CreateDirectory(paths.Root);string probe=Path.Combine(paths.Root,"write-test-"+Guid.NewGuid().ToString("N"));
   File.WriteAllText(probe,"");File.Delete(probe);
  }
  catch(Exception e)when(e is IOException or UnauthorizedAccessException)
  {
   throw new UpdateException("프로그램 폴더에 쓸 수 없어 업데이트할 수 없습니다. 폴더를 쓸 수 있는 곳(예: 문서, 바탕 화면)으로 옮긴 뒤 다시 해 주세요.\n폴더: "+paths.App);
  }
  // 가장 많이 쓸 때: 풀어 놓은 것 + 프로그램 폴더로 복사한 것(옛 파일은 같은 디스크의 백업 폴더로 옮기기만 한다). 푼 크기는 받아 봐야 알므로 배포 파일의 몇 배로 잡는다
  long needed=packageBytes*5+(64L<<20);
  if(paths.FreeBytes()<needed)throw new UpdateException($"디스크의 빈 공간이 모자랍니다. {needed/1048576.0:N0}MB 이상이 필요합니다.");
 }

 /// <summary>지난 시도가 남긴 내려받던 파일과 풀어 놓은 것을 지운다.</summary>
 public static void Clear(UpdatePaths paths)
 {
  UpdateSwap.RemoveFolder(paths.Download);UpdateSwap.RemoveFolder(paths.Staging);
 }

 /// <summary>내려받고, 확인하고, 풀고, 기록·설정을 백업한다. 만든 백업 폴더의 이름을 돌려준다. 실패하면 UpdateException, 취소하면 OperationCanceledException.</summary>
 public static async Task<string> Run(UpdatePaths paths,ReleaseInfo release,Action<int,string?,double?> report,CancellationToken cancel)
 {
  report(Download,null,0);
  // 시작할 때의 뒷정리가 아직 돌고 있으면 먼저 멈춘다. 같은 자리(download, staging)를 이번 시도가 다시 쓴다
  // 도중에 멈춘 지난 교체가 남긴 새 버전의 파일은 풀어 놓은 것을 지우기 전에 가려내 치워 둔다(가려내는 데 그 목록을 쓴다)
  await Task.Run(()=>{UpdateStartup.StopCleaning();CheckFolder(paths,release.Size);UpdateSwap.SetAside(paths);Clear(paths);},cancel);
  paths.Log($"업데이트 시작: {AppVersion.Text(AppVersion.Current)} → {AppVersion.Text(release.Version)}  ({release.Url})");
  string zip=Path.Combine(paths.Download,release.AssetName);
  await UpdatePackage.Download(release,zip,(done,total)=>report(Download,$"{done/1048576.0:N1} / {total/1048576.0:N1}MB",(double)done/total),cancel);
  cancel.ThrowIfCancellationRequested();

  report(Verify,"받은 파일을 견주는 중",null);
  await Task.Run(()=>UpdatePackage.VerifyDownload(zip,release),cancel);
  report(Verify,"푸는 중",null);
  await Task.Run(()=>UpdatePackage.Extract(zip,paths.Staging,release.Version,cancel),cancel);
  UpdateSwap.RemoveFolder(paths.Download);
  paths.Log("배포 파일 확인 끝");
  cancel.ThrowIfCancellationRequested();
  // 새 버전의 파일이 들어갈 자리에 이 프로그램의 것이 아닌 파일이 있으면 여기서 멈춘다(아직 아무것도 바꾸지 않았다)
  await Task.Run(()=>UpdateSwap.CheckPlace(paths.App,UpdateSwap.Incoming(paths.StagedApp)),cancel);

  report(Backup,"기록과 설정을 백업하는 중",null);
  long staged=await Task.Run(()=>UpdateFiles.Under(paths.StagedApp).Sum(file=>new FileInfo(Path.Combine(paths.StagedApp,UpdateFiles.Local(file))).Length),cancel);
  if(paths.FreeBytes()<staged+(64L<<20))throw new UpdateException("디스크의 빈 공간이 모자라 프로그램을 바꿀 수 없습니다.");
  try
  {
   string name=await Task.Run(()=>UpdateBackup.Create(paths,AppVersion.Current,release.Version),cancel);
   paths.Log("기록·설정 백업 끝: "+name);
   return name;
  }
  catch(Exception e)when(e is IOException or UnauthorizedAccessException)
  {
   throw new UpdateException("기록과 설정을 백업하지 못해 프로그램을 바꾸지 않았습니다. ("+e.Message+")");
  }
 }

 /// <summary>새 버전의 실행 파일을 설치 모드로 켜고, 준비됐다는 신호를 기다린다. 신호가 오면 이 프로그램은 끝나야 한다.</summary>
 public static void HandOver(UpdatePaths paths,ReleaseInfo release,string backup)
 {
  paths.WriteState(new("prepared",AppVersion.Plain(AppVersion.Current),AppVersion.Plain(release.Version),backup,UpdatePaths.Now));
  Launch(paths,Path.Combine(paths.StagedApp,UpdateManifest.Executable),UpdateProgram.ApplyFlag,"새 버전의 설치 과정");
  paths.Log("새 버전의 설치 과정에 넘김");
 }

 /// <summary>다른 폴더의 실행 파일을 켜고(교체나 복구를 맡을 것), 그것이 준비됐다고 알릴 때까지 기다린다.</summary>
 public static void Launch(UpdatePaths paths,string executable,string flag,string what)
 {
  string signal=@"Local\DJMAXLocalRecords.Update.Ready."+Guid.NewGuid().ToString("N");
  using var ready=new EventWaitHandle(false,EventResetMode.ManualReset,signal);
  Process process;
  try
  {
   var start=new ProcessStartInfo(executable){UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(executable)!};
   start.ArgumentList.Add(flag);start.ArgumentList.Add(Environment.ProcessId.ToString());start.ArgumentList.Add(signal);
   process=Process.Start(start)??throw new UpdateException(what+"을 시작하지 못했습니다.");
  }
  catch(System.ComponentModel.Win32Exception e){throw new UpdateException(what+"을 시작하지 못했습니다. ("+e.Message+")");}
  using(process)
  {
   var waited=Stopwatch.StartNew();
   while(!ready.WaitOne(200))
   {
    if(process.HasExited)throw new UpdateException(what+"이 시작되자마자 끝났습니다. 프로그램은 바뀌지 않았습니다.");
    if(waited.Elapsed>TimeSpan.FromSeconds(40))
    {
     // 우리가 방금 켠 프로세스다. 응답이 없으면 끝내고 지금 버전을 계속 쓴다
     try{process.Kill();}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}
     throw new UpdateException(what+"이 응답하지 않습니다. 프로그램은 바뀌지 않았습니다.");
    }
   }
  }
 }
}
