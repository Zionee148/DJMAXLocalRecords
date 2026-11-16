using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
namespace LadderRecords;

// 자동 업데이트: 배포 파일(ZIP)을 내려받고, 온전한지 견주고, 풀어서 내용이 맞는지 확인하는 부분 (기획서 3절·6절).
// 여기까지는 지금의 프로그램과 기록을 건드리지 않는다. 쓰는 곳은 프로그램 폴더의 update\ 아래뿐이다.

/// <summary>사용자에게 그대로 보여 줄 까닭이 든 실패.</summary>
class UpdateException(string message):Exception(message);

record ManifestFile(string Path,long Size,string Sha256);

/// <summary>배포물에 든 프로그램 파일의 목록(update-manifest.json). 묶음을 만들 때 tools\make_bundle.py 가 쓴다.
/// 업데이트는 이 목록에 있는 파일만 바꾸고, 목록에 없는 것(기록, 설정, 진단 기록, 사용자가 둔 파일)은 건드리지 않는다.</summary>
record UpdateManifest(string Product,string Version,string Target,List<ManifestFile> Files)
{
 public const string FileName="update-manifest.json",ProductName="DJMAXLocalRecords",TargetName="win-x64",Executable="DJMAXLocalRecords.exe";
 /// <summary>폴더의 목록을 읽는다. 없거나 읽지 못하면 null.</summary>
 public static UpdateManifest? Read(string folder)
 {
  try
  {
   string path=System.IO.Path.Combine(folder,FileName);
   if(!File.Exists(path))return null;
   var manifest=JsonSerializer.Deserialize<UpdateManifest>(File.ReadAllText(path));
   return manifest is {Product:not null,Version:not null,Target:not null,Files:not null}&&manifest.Files.All(f=>f is {Path:not null,Sha256:not null})?manifest:null;
  }
  catch(JsonException){return null;}
  catch(IOException){return null;}
  catch(UnauthorizedAccessException){return null;}
 }
}

static class UpdateFiles
{
 // 프로그램이 실행 중에 실행 파일 옆에 만들거나 사용자가 두는 것. 배포물에 들어 있으면 안 되고, 업데이트가 프로그램 파일로 다뤄서도 안 된다
 static readonly string[] ProtectedNames={"records.json","records.json.bak","records.json.tmp","hotkeys.json","preferences.json","records-view.json","appearance.json",UpdateTestConfig.FileName};
 static readonly string[] ProtectedFolders={"diagnostics","update"};
 /// <summary>기록·설정 사본에 담는 파일(있는 것만).</summary>
 public static readonly string[] DataFiles={"records.json","records.json.bak","hotkeys.json","preferences.json","records-view.json","appearance.json"};

 /// <summary>프로그램 폴더를 기준으로 한 경로(/ 로 나눈 것)가 개인 자료나 업데이트 작업 폴더를 가리키는가.</summary>
 public static bool IsProtected(string relative)
 {
  string first=relative.Split('/')[0];
  return ProtectedNames.Contains(relative,StringComparer.OrdinalIgnoreCase)||ProtectedFolders.Contains(first,StringComparer.OrdinalIgnoreCase);
 }
 /// <summary>폴더 밖을 가리키거나 Windows 에서 쓸 수 없는 경로가 아닌가. / 로 나눈 상대 경로만 받는다.</summary>
 public static bool IsSafe(string relative)
 {
  if(string.IsNullOrEmpty(relative)||relative.Length>240||relative.Contains('\\')||relative.Contains(':'))return false;
  var bad=System.IO.Path.GetInvalidFileNameChars();
  foreach(string part in relative.Split('/'))
   if(part.Length==0||part.Length>200||part=="."||part==".."||part.IndexOfAny(bad)>=0||part.EndsWith(' ')||part.EndsWith('.'))return false;
  return true;
 }
 public static string Local(string relative)=>relative.Replace('/',System.IO.Path.DirectorySeparatorChar);
 public static string Sha256(string path)
 {
  using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,1<<20);
  return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
 }
 /// <summary>폴더 아래의 모든 파일을 / 로 나눈 상대 경로로.</summary>
 public static List<string> Under(string folder)
 {
  if(!Directory.Exists(folder))return new();
  return Directory.EnumerateFiles(folder,"*",SearchOption.AllDirectories).Select(path=>System.IO.Path.GetRelativePath(folder,path).Replace('\\','/')).OrderBy(p=>p,StringComparer.OrdinalIgnoreCase).ToList();
 }
}

static class UpdatePackage
{
 const long MaxUnpackedBytes=3L<<30;const int MaxEntries=20000;
 static readonly TimeSpan Stall=TimeSpan.FromSeconds(30);

 /// <summary>배포 파일을 target 으로 내려받는다. 다 받기 전에는 target+".part" 로만 쓴다. progress 는 (받은 바이트, 전체 바이트).</summary>
 public static async Task Download(ReleaseInfo release,string target,Action<long,long>? progress,CancellationToken cancel)
 {
  string part=target+".part";
  Directory.CreateDirectory(Path.GetDirectoryName(target)!);
  try
  {
   using var stall=CancellationTokenSource.CreateLinkedTokenSource(cancel);stall.CancelAfter(Stall);
   using var response=await UpdateChecker.Http.GetAsync(release.Url,HttpCompletionOption.ResponseHeadersRead,stall.Token);
   if(!response.IsSuccessStatusCode)throw new UpdateException($"배포 파일을 받지 못했습니다(응답 {(int)response.StatusCode}).");
   if(response.Content.Headers.ContentLength is long announced&&announced!=release.Size)
    throw new UpdateException("받으려는 파일의 크기가 릴리즈에 적힌 것과 다릅니다.");
   await using var source=await response.Content.ReadAsStreamAsync(stall.Token);
   await using(var file=new FileStream(part,FileMode.Create,FileAccess.Write,FileShare.None,1<<16,true))
   {
    var buffer=new byte[1<<16];long total=0;
    while(true)
    {
     stall.CancelAfter(Stall);   // 30초 동안 아무것도 오지 않으면 끊긴 것으로 본다
     int read=await source.ReadAsync(buffer,stall.Token);
     if(read==0)break;
     total+=read;
     if(total>release.Size)throw new UpdateException("받은 파일이 릴리즈에 적힌 크기보다 큽니다.");
     await file.WriteAsync(buffer.AsMemory(0,read),stall.Token);
     progress?.Invoke(total,release.Size);
    }
    if(total!=release.Size)throw new UpdateException("받는 도중 연결이 끊겼습니다(파일을 끝까지 받지 못했습니다).");
   }
   File.Move(part,target,true);
  }
  catch(OperationCanceledException)when(!cancel.IsCancellationRequested){Remove(part);throw new UpdateException("응답이 멈춰 내려받지 못했습니다. 인터넷 연결을 확인해 주세요.");}
  catch(HttpRequestException){Remove(part);throw new UpdateException("연결하지 못해 내려받지 못했습니다. 인터넷 연결을 확인해 주세요.");}
  catch(IOException e){Remove(part);throw new UpdateException("받는 도중 연결이 끊겼거나 파일을 쓰지 못했습니다. ("+e.Message+")");}
  catch{Remove(part);throw;}
 }
 static void Remove(string path){try{File.Delete(path);}catch(IOException){}catch(UnauthorizedAccessException){}}

 /// <summary>받은 파일의 크기와 SHA-256 이 릴리즈에 적힌 것과 같은지 본다.</summary>
 public static void VerifyDownload(string file,ReleaseInfo release)
 {
  if(new FileInfo(file).Length!=release.Size)throw new UpdateException("받은 파일의 크기가 릴리즈에 적힌 것과 다릅니다.");
  if(!string.Equals(UpdateFiles.Sha256(file),release.Sha256,StringComparison.OrdinalIgnoreCase))
   throw new UpdateException("받은 파일이 릴리즈에 적힌 것과 다릅니다(SHA-256 이 맞지 않습니다). 파일이 손상되었거나 바뀐 것일 수 있습니다.");
 }

 /// <summary>배포 파일을 stagingRoot 아래에 풀고 내용을 확인한다. 풀린 프로그램 폴더는 stagingRoot\DJMAXLocalRecords 다.
 /// 맞지 않는 것이 하나라도 있으면 UpdateException 을 던진다(풀던 것은 부르는 쪽이 지운다).</summary>
 public static UpdateManifest Extract(string zip,string stagingRoot,Version expected,CancellationToken cancel=default)
 {
  const string Top=UpdateManifest.ProductName+"/";
  string appRoot=Path.GetFullPath(Path.Combine(stagingRoot,UpdateManifest.ProductName));
  string inside=appRoot.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
  var extracted=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  try
  {
   using var archive=ZipFile.OpenRead(zip);
   if(archive.Entries.Count>MaxEntries)throw new UpdateException("배포 파일에 든 파일이 너무 많습니다.");
   long total=0;
   foreach(var entry in archive.Entries)
   {
    cancel.ThrowIfCancellationRequested();
    string name=entry.FullName.Replace('\\','/');
    if(name.EndsWith('/'))continue;   // 폴더 항목
    if(!name.StartsWith(Top,StringComparison.Ordinal))throw new UpdateException($"배포 파일의 구조가 예상과 다릅니다(맨 위 폴더가 {UpdateManifest.ProductName} 이 아닙니다).");
    string relative=name[Top.Length..];
    if(!UpdateFiles.IsSafe(relative))throw new UpdateException("배포 파일에 쓸 수 없는 경로가 들어 있습니다: "+name);
    if(UpdateFiles.IsProtected(relative))throw new UpdateException("배포 파일에 개인 자료의 자리를 차지하는 파일이 들어 있습니다: "+relative);
    if(!extracted.Add(relative))throw new UpdateException("배포 파일에 같은 경로가 두 번 들어 있습니다: "+relative);
    total+=entry.Length;
    if(total>MaxUnpackedBytes)throw new UpdateException("배포 파일을 풀면 예상보다 훨씬 큽니다.");
    string target=Path.GetFullPath(Path.Combine(appRoot,UpdateFiles.Local(relative)));
    if(!target.StartsWith(inside,StringComparison.OrdinalIgnoreCase))throw new UpdateException("배포 파일에 폴더 밖을 가리키는 경로가 들어 있습니다: "+name);
    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
    using var from=entry.Open();using var to=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None,1<<16);
    // 적힌 크기보다 더 풀리면 멈춘다
    var buffer=new byte[1<<16];long written=0;int read;
    while((read=from.Read(buffer,0,buffer.Length))>0)
    {
     written+=read;if(written>entry.Length)throw new UpdateException("배포 파일의 압축 정보가 맞지 않습니다: "+relative);
     to.Write(buffer,0,read);
    }
   }
  }
  catch(InvalidDataException){throw new UpdateException("배포 파일이 ZIP 으로 읽히지 않습니다(손상되었을 수 있습니다).");}
  catch(IOException e){throw new UpdateException("배포 파일을 풀지 못했습니다. ("+e.Message+")");}
  catch(UnauthorizedAccessException e){throw new UpdateException("배포 파일을 풀지 못했습니다. ("+e.Message+")");}

  var manifest=UpdateManifest.Read(appRoot)??throw new UpdateException($"배포 파일에 프로그램 파일 목록({UpdateManifest.FileName})이 없거나 읽히지 않습니다.");
  if(manifest.Product!=UpdateManifest.ProductName)throw new UpdateException("이 프로그램의 배포 파일이 아닙니다.");
  if(manifest.Target!=UpdateManifest.TargetName)throw new UpdateException($"Windows 64비트용 배포 파일이 아닙니다(대상: {manifest.Target}).");
  if(!AppVersion.TryParsePlain(manifest.Version,out var version)||version!=expected)
   throw new UpdateException($"배포 파일에 든 버전({manifest.Version})이 고른 버전({AppVersion.Plain(expected)})과 다릅니다.");
  var listed=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  foreach(var file in manifest.Files)
  {
   if(!UpdateFiles.IsSafe(file.Path)||UpdateFiles.IsProtected(file.Path)||file.Path.Equals(UpdateManifest.FileName,StringComparison.OrdinalIgnoreCase)||!listed.Add(file.Path))
    throw new UpdateException("프로그램 파일 목록에 받아들일 수 없는 경로가 있습니다: "+file.Path);
  }
  if(!listed.Contains(UpdateManifest.Executable))throw new UpdateException("배포 파일에 실행 파일이 없습니다.");
  extracted.Remove(UpdateManifest.FileName);
  var extra=extracted.Where(p=>!listed.Contains(p)).ToList();var missing=listed.Where(p=>!extracted.Contains(p)).ToList();
  if(extra.Count>0)throw new UpdateException("배포 파일에 목록에 없는 파일이 들어 있습니다: "+string.Join(", ",extra.Take(3)));
  if(missing.Count>0)throw new UpdateException("목록에 있는 파일이 배포 파일에 없습니다: "+string.Join(", ",missing.Take(3)));
  foreach(var file in manifest.Files)
  {
   cancel.ThrowIfCancellationRequested();
   string path=Path.Combine(appRoot,UpdateFiles.Local(file.Path));
   if(new FileInfo(path).Length!=file.Size||!string.Equals(UpdateFiles.Sha256(path),file.Sha256,StringComparison.OrdinalIgnoreCase))
    throw new UpdateException("배포 파일에서 푼 파일이 목록과 다릅니다: "+file.Path);
  }
  return manifest;
 }
}
