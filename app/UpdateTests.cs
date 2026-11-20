using System.IO.Compression;
using System.Text;
using System.Text.Json;
namespace LadderRecords;

// 자동 업데이트의 검사(개발용 빌드에만 들어간다. --self-test 가 부른다). 인터넷을 쓰지 않고, 실행 파일 옆의 test-data 아래 새 폴더에서만 파일을 만든다.
// 실제로 프로그램을 끄고 켜는 흐름은 여기서 보지 않는다(tools\update_test.py 가 시험용 빌드와 가짜 릴리즈 서버로 본다).
static class UpdateTests
{
 static readonly List<string> notes=new();
 static void Check(bool ok,string name){if(!ok)throw new Exception("Update: "+name);notes.Add("PASS "+name);}
 static string Fails(Action action){try{action();}catch(UpdateException e){return e.Message;}return"";}

 public static void Run()
 {
  notes.Clear();
  string root=Path.Combine(AppContext.BaseDirectory,"test-data","update-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  Versions();Picking();Paths();Notes();
  Packages(Path.Combine(root,"packages"));
  Swapping(Path.Combine(root,"swap"));
  Backups(Path.Combine(root,"backup"));
  Startup(Path.Combine(root,"startup"));
  Store(Path.Combine(root,"store"));
  File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"update-validation.txt"),string.Join("\n",notes)+$"\nPASS: {notes.Count} checks");
 }

 /// <summary>진짜 공식 저장소의 릴리즈 목록을 읽어 본다(--update-official-check 로만 돈다. 인터넷을 쓴다: GitHub 에 릴리즈 목록을 세 번 조회한다). 내려받지는 않는다.
 /// 프로그램이 실제 응답의 형식을 읽을 수 있는지 보는 것이다. 결과는 update-official-check.txt.</summary>
 public static void Official()
 {
  static string Describe(CheckResult result)=>result switch
  {
   UpToDate=>"더 높은 정식 릴리즈 없음",
   UpdateAvailable found=>$"새 버전 {AppVersion.Text(found.Release.Version)} · 파일 {found.Release.AssetName} · {found.Release.Size:N0}바이트 · sha256 {found.Release.Sha256} · 패치노트 {found.Release.Notes.Length}자",
   NoPackage missing=>$"새 버전 {AppVersion.Text(missing.Version)} 은 있으나 자동으로 받을 수 없음: {missing.Reason} ({missing.Page})",
   CheckFailed failed=>"확인 실패: "+failed.Reason,
   _=>"?"
  };
  var lines=new List<string>{"source: "+UpdateSource.Official.Releases,"at: "+UpdatePaths.Now};
  foreach(var from in new[]{new Version(0,9,0),new Version(1,0,0),new Version(99,0,0)})
   lines.Add($"지금 버전이 {AppVersion.Text(from)} 일 때 → "+Describe(Task.Run(()=>UpdateChecker.Check(from,UpdateSource.Official)).GetAwaiter().GetResult()));
  File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"update-official-check.txt"),string.Join("\n",lines)+"\n");
 }

 /// <summary>자동 업데이트의 창을 캡처해 실행 파일 옆에 PNG 로 저장한다(--update-windows-check. 라이트·다크). 인터넷을 쓰지 않고 아무것도 내려받지 않는다.</summary>
 public static void Previews()
 {
  var release=new ReleaseInfo(new Version(1,2,0),"# v1.2.0 패치노트\n\n- 자동 업데이트를 추가했습니다.\n- 지원 기어를 추가했습니다: 예시 기어\n- 추가 악곡과 SC 패턴을 반영했습니다.\n\n## 고친 것\n- **기록 목록 창**을 닫을 때 프로그램이 늦게 끝나던 문제",
   UpdateSource.Official.PagePrefix+"tag/v1.2.0",UpdateChecker.AssetName(new Version(1,2,0)),UpdateSource.Official.DownloadPrefix+"v1.2.0/x.zip",79_600_000,new string('a',64));
  void Save(Form form,string name){Application.DoEvents();using var shot=new Bitmap(form.Width,form.Height);form.DrawToBitmap(shot,new Rectangle(Point.Empty,shot.Size));shot.Save(Path.Combine(AppContext.BaseDirectory,name+".png"));}
  bool dark=Theme.Dark;
  try
  {
   foreach(bool mode in new[]{false,true})
   {
    Theme.Dark=mode;string end=mode?"-dark":"";
    using(var offer=new UpdateDialog(release,UpdateSource.Official)){offer.Show();Save(offer,"update-offer-preview"+end);offer.Close();}
    using(var progress=new UpdateProgressDialog(new Version(1,1,0),release.Version))
    {
     // 내려받는 중의 모습에서 멈춰 두고 찍은 뒤 취소로 끝낸다
     progress.Work=async(dialog,cancel)=>{dialog.Report(UpdatePrepare.Download,"38.2 / 75.9MB",.5);await Task.Delay(Timeout.Infinite,cancel);};
     progress.Show();Save(progress,"update-progress-preview"+end);
     progress.Close();for(int i=0;i<200&&!progress.IsDisposed&&progress.Visible;i++){Application.DoEvents();Thread.Sleep(10);}
     if(!progress.Cancelled)throw new Exception("Update: closing the progress window did not cancel");
    }
    using var gate=new ManualResetEventSlim();
    using(var install=new InstallWindow("업데이트","v1.1.0  →  v1.2.0",UpdatePrepare.Stages,UpdatePrepare.Replace))
    {
     install.Work=w=>{w.Stage(UpdatePrepare.Replace,"프로그램 파일을 바꾸는 중");gate.Wait();};
     install.Show();for(int i=0;i<50;i++){Application.DoEvents();Thread.Sleep(10);}
     Save(install,"update-install-preview"+end);
     install.Close();Application.DoEvents();
     if(!install.Visible)throw new Exception("Update: the install window could be closed while working");
     gate.Set();for(int i=0;i<200&&install.Visible;i++){Application.DoEvents();Thread.Sleep(10);}
    }
   }
  }
  finally{Theme.Dark=dark;}
 }

 static void Versions()
 {
  Check(AppVersion.TryParseTag("v1.2.3",out var v)&&v==new Version(1,2,3),"tag v1.2.3");
  foreach(string bad in new[]{"1.2.3","v1.2","v1.2.3.4","v1.2.3-beta","V1.2.3","v1.2.3 ","v1.2.3\n"," v1.2.3","v01.2.x","","v1.2.3rc1","v１.2.3"})
   Check(!AppVersion.TryParseTag(bad,out _),"tag rejected: "+bad.Trim());
  Check(!AppVersion.TryParseTag(null,out _),"tag null");
  Check(AppVersion.TryParsePlain("10.0.12",out var p)&&p==new Version(10,0,12)&&!AppVersion.TryParsePlain("v1.0.0",out _),"plain version");
  Check(new Version(1,10,0)>new Version(1,9,9)&&new Version(2,0,0)>new Version(1,99,99),"version order is numeric");
  Check(AppVersion.Text(new Version(1,2,3))=="v1.2.3"&&UpdateChecker.AssetName(new Version(1,2,3))=="DJMAXLocalRecords-v1.2.3-win-x64.zip","names");
 }

 static string Release(string tag,bool draft=false,bool prerelease=false,string? asset=null,string? digest=null,long size=1000,string? url=null,bool noFlags=false)
 {
  string hash=digest??"sha256:"+new string('a',64);
  string name=asset??(AppVersion.TryParseTag(tag,out var v)?UpdateChecker.AssetName(v):"x.zip");
  string address=url??$"https://github.com/Zionee148/DJMAXLocalRecords/releases/download/{tag}/{name}";
  string flags=noFlags?"":$"\"draft\":{(draft?"true":"false")},\"prerelease\":{(prerelease?"true":"false")},";
  return $"{{\"tag_name\":\"{tag}\",{flags}\"html_url\":\"https://github.com/Zionee148/DJMAXLocalRecords/releases/tag/{tag}\",\"body\":\"# notes\\n- one\",\"assets\":[{{\"name\":\"{name}\",\"size\":{size},\"digest\":{(hash==""?"null":"\""+hash+"\"")},\"browser_download_url\":\"{address}\"}}]}}";
 }
 static void Picking()
 {
  var official=UpdateSource.Official;var now=new Version(1,1,0);
  CheckResult Pick(params string[] releases)=>UpdateChecker.Pick("["+string.Join(",",releases)+"]",now,official);
  Check(Pick() is UpToDate,"empty list is up to date");
  Check(Pick(Release("v1.1.0"),Release("v1.0.0")) is UpToDate,"same and lower are not offered");
  Check(Pick(Release("v1.2.0")) is UpdateAvailable{Release:{Sha256.Length:64,Size:1000} r}&&r.Version==new Version(1,2,0)&&r.Url.EndsWith("DJMAXLocalRecords-v1.2.0-win-x64.zip"),"higher is offered");
  Check(Pick(Release("v1.2.0"),Release("v1.10.0"),Release("v1.9.0")) is UpdateAvailable{Release:var top}&&top.Version==new Version(1,10,0),"highest wins regardless of order");
  Check(Pick(Release("v1.2.0",draft:true)) is UpToDate,"draft excluded");
  Check(Pick(Release("v1.2.0",prerelease:true)) is UpToDate,"prerelease excluded");
  Check(Pick(Release("v1.2.0",noFlags:true)) is UpToDate,"release without explicit flags excluded");
  Check(Pick(Release("v1.2.0-beta"),Release("1.3.0"),Release("test")) is UpToDate,"other tag shapes excluded");
  Check(Pick(Release("v2.0.0",prerelease:true),Release("v1.2.0")) is UpdateAvailable{Release:var formal}&&formal.Version==new Version(1,2,0),"prerelease above does not hide formal");
  Check(Pick(Release("v1.2.0",asset:"other.zip")) is NoPackage{Version:var missing}&&missing==new Version(1,2,0),"no matching asset");
  Check(Pick(Release("v1.3.0",asset:"other.zip"),Release("v1.2.0")) is NoPackage,"highest without asset is reported, lower is not offered instead");
  Check(Pick(Release("v1.2.0",digest:"")) is NoPackage,"digest missing");
  Check(Pick(Release("v1.2.0",digest:"sha256:abc")) is NoPackage&&Pick(Release("v1.2.0",digest:"md5:"+new string('a',64))) is NoPackage&&Pick(Release("v1.2.0",digest:"sha256:"+new string('g',64))) is NoPackage,"digest malformed");
  Check(Pick(Release("v1.2.0",size:0)) is NoPackage&&Pick(Release("v1.2.0",size:UpdateChecker.MaxPackageBytes+1)) is NoPackage,"size out of range");
  Check(Pick(Release("v1.2.0",url:"https://example.com/DJMAXLocalRecords-v1.2.0-win-x64.zip")) is NoPackage,"download from another host refused");
  Check(Pick(Release("v1.2.0",url:"http://github.com/Zionee148/DJMAXLocalRecords/releases/download/v1.2.0/DJMAXLocalRecords-v1.2.0-win-x64.zip")) is NoPackage,"http refused for official source");
  Check(Pick(Release("v1.2.0",url:"https://github.com/Zionee148/DJMAXLocalRecords/releases/download/../../../evil/x.zip")) is UpdateAvailable or NoPackage,"odd path does not crash");
  Check(UpdateChecker.Pick("{\"message\":\"rate limit\"}",now,official) is CheckFailed&&UpdateChecker.Pick("not json",now,official) is CheckFailed&&UpdateChecker.Pick("[1,\"x\",null]",now,official) is UpToDate,"malformed answers");
  var test=UpdateSource.For(new("http://127.0.0.1:1/"));
  Check(test.Test&&test.Releases=="http://127.0.0.1:1/releases"&&UpdateSource.For(null)==official&&UpdateSource.For(new(""))==official,"test source");
  Check(UpdateChecker.Pick("["+Release("v1.2.0",url:"http://127.0.0.1:1/download/DJMAXLocalRecords-v1.2.0-win-x64.zip")+"]",now,test) is UpdateAvailable,"test source accepts its own http address");
 }

 static void Paths()
 {
  foreach(string ok in new[]{"a.dll","data/catalog.json","data/score/reader.onnx","사용안내.txt"})Check(UpdateFiles.IsSafe(ok)&&!UpdateFiles.IsProtected(ok),"safe path "+ok);
  foreach(string bad in new[]{"","../a","a/../b","/a","a//b","a\\b","C:/a","a/.","a/b.","a/b ","a:b","con<","a/"})Check(!UpdateFiles.IsSafe(bad),"unsafe path "+bad);
  foreach(string mine in new[]{"records.json","RECORDS.JSON","records.json.bak","records.json.tmp","hotkeys.json","preferences.json","records-view.json","appearance.json","diagnostics/a.log","Diagnostics/images/x.png","update/state.json","update-test.json"})
   Check(UpdateFiles.IsProtected(mine),"protected "+mine);
  Check(!UpdateFiles.IsProtected("data/records.json")&&!UpdateFiles.IsProtected("update-manifest.json"),"not protected");
  string app=Path.Combine(Path.GetTempPath(),"x","App");
  Check(UpdatePaths.FromStaging(Path.Combine(app,"update","staging","DJMAXLocalRecords")+Path.DirectorySeparatorChar,out string a)&&a==app,"staging folder recognized");
  Check(!UpdatePaths.FromStaging(app,out _)&&!UpdatePaths.FromStaging(Path.Combine(app,"staging","DJMAXLocalRecords"),out _),"ordinary folder is not staging");
  Check(!UpdatePaths.FromBackup(Path.Combine(app,"update","backup","v1","program"),out _,out _),"backup folder without marker is not a backup");
 }

 static void Notes()
 {
  Check(UpdateChecker.PlainNotes("# 제목\r\n\r\n- 하나\n* 둘\n  - 셋\n**굵게** 글")=="제목\r\n\r\n· 하나\r\n· 둘\r\n  · 셋\r\n굵게 글","patch notes as plain text");
  Check(UpdateChecker.PlainNotes("  \n")=="(패치노트가 비어 있습니다)","empty patch notes");
 }

 // ── 배포 파일
 static (string Name,byte[] Content)[] Program(string version)=>new[]{("DJMAXLocalRecords.exe",Encoding.UTF8.GetBytes("exe "+version)),("DJMAXLocalRecords.dll",Encoding.UTF8.GetBytes("dll "+version)),("data/catalog.json",Encoding.UTF8.GetBytes("{\"v\":\""+version+"\"}")),("overlay.html",Encoding.UTF8.GetBytes("<html>"+version))};
 static string Manifest((string Name,byte[] Content)[] files,string version,string product="DJMAXLocalRecords",string target="win-x64")
  =>JsonSerializer.Serialize(new UpdateManifest(product,version,target,files.Select(f=>new ManifestFile(f.Name,f.Content.Length,Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(f.Content)).ToLowerInvariant())).ToList()));
 static void Zip(string path,IEnumerable<(string Name,byte[] Content)> entries)
 {
  Directory.CreateDirectory(Path.GetDirectoryName(path)!);
  using var archive=ZipFile.Open(path,ZipArchiveMode.Create);
  foreach(var(name,content)in entries){using var stream=archive.CreateEntry(name).Open();stream.Write(content);}
 }
 static IEnumerable<(string,byte[])> Packed((string Name,byte[] Content)[] files,string manifest,string top="DJMAXLocalRecords/")
  =>files.Select(f=>(top+f.Name,f.Content)).Append((top+UpdateManifest.FileName,Encoding.UTF8.GetBytes(manifest)));

 static void Packages(string root)
 {
  var version=new Version(1,2,0);var files=Program("1.2.0");int n=0;
  string Try(IEnumerable<(string,byte[])> entries)
  {
   string zip=Path.Combine(root,$"p{++n}.zip"),staging=Path.Combine(root,$"s{n}");Zip(zip,entries);
   return Fails(()=>UpdatePackage.Extract(zip,staging,version));
  }
  string good=Path.Combine(root,"good.zip"),goodStaging=Path.Combine(root,"good");Zip(good,Packed(files,Manifest(files,"1.2.0")));
  var manifest=UpdatePackage.Extract(good,goodStaging,version);
  Check(manifest.Files.Count==4&&File.ReadAllText(Path.Combine(goodStaging,"DJMAXLocalRecords","data","catalog.json")).Contains("1.2.0")&&UpdateFiles.Under(Path.Combine(goodStaging,"DJMAXLocalRecords")).Count==5,"good package extracts");
  Check(Try(Packed(files,Manifest(files,"1.3.0"))).Contains("버전"),"package of another version refused");
  Check(Try(Packed(files,Manifest(files,"1.2.0",target:"linux-x64"))).Contains("64비트"),"package for another target refused");
  Check(Try(Packed(files,Manifest(files,"1.2.0",product:"Other"))).Contains("이 프로그램"),"package of another product refused");
  Check(Try(files.Select(f=>("DJMAXLocalRecords/"+f.Name,f.Content))).Contains("목록"),"package without manifest refused");
  Check(Try(Packed(files,"{broken")).Contains("목록"),"package with unreadable manifest refused");
  Check(Try(Packed(files,Manifest(files,"1.2.0")).Append(("DJMAXLocalRecords/extra.dll",new byte[]{1}))).Contains("목록에 없는"),"extra file refused");
  Check(Try(Packed(files.Take(3).ToArray(),Manifest(files,"1.2.0"))).Contains("배포 파일에 없습니다"),"missing file refused");
  var changed=files.ToArray();changed[1]=(changed[1].Name,Encoding.UTF8.GetBytes("dll 9.9.9"));
  Check(Try(Packed(changed,Manifest(files,"1.2.0"))).Contains("목록과 다릅니다"),"changed file refused");
  Check(Try(Packed(files.Skip(1).ToArray(),Manifest(files.Skip(1).ToArray(),"1.2.0"))).Contains("실행 파일"),"package without executable refused");
  Check(Try(Packed(files,Manifest(files,"1.2.0"),"Other/")).Contains("맨 위 폴더"),"other top folder refused");
  Check(Try(Packed(files,Manifest(files,"1.2.0")).Append(("DJMAXLocalRecords/../escape.txt",new byte[]{1}))).Contains("쓸 수 없는 경로"),"path climbing out refused");
  Check(!File.Exists(Path.Combine(root,"escape.txt"))&&!Directory.EnumerateFiles(root,"escape.txt",SearchOption.AllDirectories).Any(),"nothing written outside");
  Check(Try(Packed(files,Manifest(files,"1.2.0")).Append(("DJMAXLocalRecords/records.json",Encoding.UTF8.GetBytes("{}")))).Contains("개인 자료"),"package carrying records refused");
  Check(Try(Packed(files,Manifest(files,"1.2.0")).Append(("DJMAXLocalRecords/update/state.json",Encoding.UTF8.GetBytes("{}")))).Contains("개인 자료"),"package writing into update folder refused");
  Check(Try(Packed(files,Manifest(files,"1.2.0")).Append(("DJMAXLocalRecords/OVERLAY.HTML",new byte[]{1}))).Contains("두 번"),"same path twice refused");
  (string Name,byte[] Content)[] withRecords=files.Append(("diagnostics/x.log",new byte[]{1})).ToArray();
  Check(Try(Packed(withRecords,Manifest(withRecords,"1.2.0"))).Contains("개인 자료"),"manifest listing protected path refused");
  string junk=Path.Combine(root,"junk.zip");File.WriteAllText(junk,"this is not a zip");
  Check(Fails(()=>UpdatePackage.Extract(junk,Path.Combine(root,"junk"),version)).Contains("ZIP"),"non-zip refused");
  // 내려받은 파일의 크기와 해시
  var release=new ReleaseInfo(version,"","https://x/","good.zip","https://x/good.zip",new FileInfo(good).Length,UpdateFiles.Sha256(good));
  UpdatePackage.VerifyDownload(good,release);notes.Add("PASS intact download accepted");
  Check(Fails(()=>UpdatePackage.VerifyDownload(good,release with{Sha256=new string('0',64)})).Contains("SHA-256"),"download with another hash refused");
  Check(Fails(()=>UpdatePackage.VerifyDownload(good,release with{Size=release.Size+1})).Contains("크기"),"download with another size refused");
 }

 // ── 프로그램 파일 바꾸기와 되돌리기
 static Dictionary<string,string> Picture(string folder)=>UpdateFiles.Under(folder).ToDictionary(file=>file,file=>UpdateFiles.Sha256(Path.Combine(folder,UpdateFiles.Local(file))),StringComparer.OrdinalIgnoreCase);
 static bool Same(Dictionary<string,string> a,Dictionary<string,string> b)=>a.Count==b.Count&&a.All(pair=>b.TryGetValue(pair.Key,out var other)&&other==pair.Value);
 static void Lay(string folder,(string Name,byte[] Content)[] files,string? manifestVersion)
 {
  foreach(var(name,content)in files){string path=Path.Combine(folder,UpdateFiles.Local(name));Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllBytes(path,content);}
  if(manifestVersion!=null)File.WriteAllText(Path.Combine(folder,UpdateManifest.FileName),Manifest(files,manifestVersion));
 }
 static readonly (string Name,byte[] Content)[] Mine={("records.json",Encoding.UTF8.GetBytes("{\"mine\":1}")),("records.json.bak",Encoding.UTF8.GetBytes("{\"mine\":0}")),("hotkeys.json",Encoding.UTF8.GetBytes("[]")),("preferences.json",Encoding.UTF8.GetBytes("{}")),("appearance.json",Encoding.UTF8.GetBytes("{}")),("records-view.json",Encoding.UTF8.GetBytes("{}")),("diagnostics/score.log",Encoding.UTF8.GetBytes("log")),("내 백업/records.json",Encoding.UTF8.GetBytes("user copy")),("data/my-note.txt",Encoding.UTF8.GetBytes("user file in a program folder"))};

 static void Swapping(string root)
 {
  (string Name,byte[] Content)[] oldFiles=Program("1.1.0").Append(("old-only.dll",Encoding.UTF8.GetBytes("gone in 1.2.0"))).Append(("old/only/deep.bin",new byte[]{7})).ToArray();
  (string Name,byte[] Content)[] newFiles=Program("1.2.0").Append(("new-only.dll",Encoding.UTF8.GetBytes("new in 1.2.0"))).ToArray();
  var bring=newFiles.Select(f=>f.Name).Append(UpdateManifest.FileName).ToList();
  var expected=JsonSerializer.Deserialize<UpdateManifest>(Manifest(newFiles,"1.2.0"))!.Files.ToDictionary(f=>f.Path,StringComparer.OrdinalIgnoreCase);
  (string App,string Staged,string Park) Fresh(string name)
  {
   string app=Path.Combine(root,name,"app"),staged=Path.Combine(app,"update","staging","DJMAXLocalRecords"),park=Path.Combine(app,"update","backup","v1.1.0-x","program");
   Lay(app,oldFiles,"1.1.0");Lay(app,Mine,null);Lay(staged,newFiles,"1.2.0");return(app,staged,park);
  }
  var log=new List<string>();int pause=UpdateSwap.Pause;UpdateSwap.Pause=2;   // 다시 해 보는 사이를 줄여 검사가 빨리 끝나게 한다

  // 정상
  var(app,staged,park)=Fresh("ok");var mineBefore=Mine.ToDictionary(f=>f.Name,f=>UpdateFiles.Sha256(Path.Combine(app,UpdateFiles.Local(f.Name))));
  var current=UpdateSwap.Current(app);
  Check(current.Count==oldFiles.Length+1&&current.Contains("old-only.dll")&&!current.Any(UpdateFiles.IsProtected)&&!current.Contains("data/my-note.txt"),"files to park are the program files only");
  Check(UpdateSwap.Conflicts(app,current,bring).Count==0&&Fails(()=>UpdateSwap.CheckPlace(app,bring))==""&&UpdateSwap.Incoming(staged).SequenceEqual(bring),"nothing in the way of an ordinary update");
  var done=UpdateSwap.Exchange(app,current,park,staged,bring,false,expected,log.Add);
  Check(done is {Done:true},"swap succeeds");
  Check(File.ReadAllText(Path.Combine(app,"DJMAXLocalRecords.exe"))=="exe 1.2.0"&&File.Exists(Path.Combine(app,"new-only.dll"))&&!File.Exists(Path.Combine(app,"old-only.dll"))&&UpdateManifest.Read(app)!.Version=="1.2.0","new program in place, removed files gone");
  Check(!Directory.Exists(Path.Combine(app,"old"))&&Directory.Exists(Path.Combine(app,"data")),"emptied folder removed, others kept");
  Check(Mine.All(f=>UpdateFiles.Sha256(Path.Combine(app,UpdateFiles.Local(f.Name)))==mineBefore[f.Name]),"records, settings, diagnostics and user files untouched");
  Check(File.ReadAllText(Path.Combine(park,"DJMAXLocalRecords.exe"))=="exe 1.1.0"&&UpdateManifest.Read(park)!.Version=="1.1.0"&&File.Exists(Path.Combine(park,"old","only","deep.bin")),"old program kept whole in the backup");
  Check(UpdateFiles.Under(staged).Count==newFiles.Length+1&&File.Exists(Path.Combine(staged,"DJMAXLocalRecords.exe")),"staged files stay (copied, not moved)");

  // 되돌리기 ①: 설치를 맡은 쪽이 옮겨 둔 것을 제자리로(옮기기)
  var oldBack=UpdateFiles.Under(park);string failed=Path.Combine(app,"update","failed-x");
  var back=UpdateSwap.Exchange(app,UpdateSwap.Current(app,bring),failed,park,oldBack,true,null,log.Add);
  var(reference,_,_)=Fresh("reference");var wanted=Picture(reference);foreach(string key in wanted.Keys.Where(k=>k.StartsWith("update/")).ToList())wanted.Remove(key);
  Dictionary<string,string> Program2(string folder){var all=Picture(folder);foreach(string key in all.Keys.Where(k=>k.StartsWith("update/")).ToList())all.Remove(key);return all;}
  Check(back.Done&&Same(Program2(app),wanted),"rolling back by moving restores the folder exactly");
  Check(!UpdateFiles.Under(park).Any()&&File.Exists(Path.Combine(failed,"new-only.dll")),"new files parked aside, backup emptied");

  // 되돌리기 ②: 백업의 실행 파일이 복사해서 되돌린다(백업은 남는다)
  (app,staged,park)=Fresh("restore");current=UpdateSwap.Current(app);
  Check(UpdateSwap.Exchange(app,current,park,staged,bring,false,expected,log.Add).Done,"swap before restore");
  File.WriteAllText(Path.Combine(app,"records.json"),"{\"mine\":2,\"added after update\":true}");
  oldBack=UpdateFiles.Under(park);string replaced=Path.Combine(app,"update","replaced-x");
  var restore=UpdateSwap.Exchange(app,UpdateSwap.Current(app),replaced,park,oldBack,false,null,log.Add);
  wanted["records.json"]=UpdateFiles.Sha256(Path.Combine(app,"records.json"));
  Check(restore.Done&&Same(Program2(app),wanted),"restoring by copying brings the old program back");
  Check(File.ReadAllText(Path.Combine(app,"records.json")).Contains("added after update"),"records written after the update survive the restore");
  Check(UpdateFiles.Under(park).Count==oldBack.Count&&File.Exists(Path.Combine(replaced,"new-only.dll")),"backup kept, newer program parked aside");

  // 실패를 끼워 넣는다: 어디서 실패하든 바꾸기 전과 똑같아야 한다
  int ops=0;UpdateSwap.Probe=(_,_)=>ops++;
  (app,staged,park)=Fresh("count");UpdateSwap.Exchange(app,UpdateSwap.Current(app),park,staged,bring,false,expected,log.Add);int total=ops;
  Check(total==oldFiles.Length+1+bring.Count,"one operation per file");
  foreach(int failAt in new[]{0,1,oldFiles.Length,oldFiles.Length+1,oldFiles.Length+3,total-1})
  {
   (app,staged,park)=Fresh("fail-"+failAt);var before=Picture(app);int seen=0;string? failing=null;
   // 한 번 실패한 일은 다시 해 봐도 계속 실패하게 한다(같은 일, 같은 경로). 되돌리는 일은 막지 않는다
   UpdateSwap.Probe=(what,path)=>{string key=what+"|"+path;if(failing==null){if(seen++!=failAt)return;failing=key;}if(key==failing)throw new UnauthorizedAccessException("injected at "+failAt);};
   var result=UpdateSwap.Exchange(app,UpdateSwap.Current(app),park,staged,bring,false,expected,log.Add);
   Check(result is {Done:false,Restored:true}&&Same(Picture(app),before),$"failure at operation {failAt} leaves the folder exactly as before");
  }
  // 들여온 파일이 목록과 다르면 실패로 치고 되돌린다
  (app,staged,park)=Fresh("tampered");var untouched=Picture(app);UpdateSwap.Probe=null;
  File.WriteAllText(Path.Combine(staged,"new-only.dll"),"changed after verification");untouched["update/staging/DJMAXLocalRecords/new-only.dll"]=UpdateFiles.Sha256(Path.Combine(staged,"new-only.dll"));
  var tampered=UpdateSwap.Exchange(app,UpdateSwap.Current(app),park,staged,bring,false,expected,log.Add);
  Check(tampered is {Done:false,Restored:true}&&Same(Picture(app),untouched),"staged file changed after verification: refused and rolled back");
  // 들여올 자리에 다른 것이 있으면 덮어쓰지 않는다
  (app,staged,park)=Fresh("occupied");Directory.CreateDirectory(Path.Combine(app,"new-only.dll"));File.WriteAllText(Path.Combine(app,"new-only.dll","keep.txt"),"user");untouched=Picture(app);
  var occupied=UpdateSwap.Exchange(app,UpdateSwap.Current(app),park,staged,bring,false,expected,log.Add);
  Check(occupied is {Done:false,Restored:true}&&Same(Picture(app),untouched),"something else in the way: refused and rolled back, not deleted");
  Check(UpdateSwap.Conflicts(app,UpdateSwap.Current(app),bring).SequenceEqual(new[]{"new-only.dll"}),"a folder in the way is found before swapping");
  // 프로그램 파일 목록에 없는 사용자의 파일이 새 버전의 파일과 이름이 같은 경우: 프로그램 파일로 보지 않고, 교체 전의 확인(Conflicts, CheckPlace)이 찾아내며, 그래도 교체(Exchange)를 실행하면 덮어쓰지도 백업으로 옮기지도 않는다
  (app,staged,park)=Fresh("users-file");File.WriteAllText(Path.Combine(app,"new-only.dll"),"USER DATA");untouched=Picture(app);
  current=UpdateSwap.Current(app);
  Check(current.Count==oldFiles.Length+1&&!current.Contains("new-only.dll"),"user's file named like a new program file is not taken for a program file");
  Check(UpdateSwap.Conflicts(app,current,bring).SequenceEqual(new[]{"new-only.dll"}),"it is found before swapping");
  string refusal=Fails(()=>UpdateSwap.CheckPlace(app,bring));
  Check(refusal.Contains(Path.Combine(app,"new-only.dll"))&&refusal.Contains("덮어쓰지 않고")&&Same(Picture(app),untouched),"the update refuses to start and names the file");
  var users=UpdateSwap.Exchange(app,current,park,staged,bring,false,expected,log.Add);
  Check(users is {Done:false,Restored:true}&&Same(Picture(app),untouched)&&File.ReadAllText(Path.Combine(app,"new-only.dll"))=="USER DATA"&&!UpdateFiles.Under(park).Any(),"even if swapped anyway: not overwritten, not moved into the backup, rolled back");
  // 새 버전의 폴더가 될 자리에 사용자의 파일이 있어도 알아챈다
  File.WriteAllText(Path.Combine(app,"data","sub"),"user");
  Check(UpdateSwap.Conflicts(app,current,new[]{"data/sub/deep/x.bin","overlay.html"}).SequenceEqual(new[]{"data/sub"}),"a user's file where a new folder must go is found too");
  // 지금 설치에 프로그램 파일 목록이 없으면 어느 파일도 이 프로그램의 것으로 보지 않고, 업데이트를 시작하지 않는다
  (app,staged,park)=Fresh("no-list");File.Delete(Path.Combine(app,UpdateManifest.FileName));untouched=Picture(app);
  Check(UpdateSwap.Current(app).Count==0&&Fails(()=>UpdateSwap.CheckPlace(app,bring)).Contains("파일 목록")&&Same(Picture(app),untouched),"without the installed file list nothing is replaced");
  // 되돌리는 것까지 실패하면 되돌렸다고 말하지 않는다
  (app,staged,park)=Fresh("broken");int count=0;string? stuck=null;
  UpdateSwap.Probe=(what,path)=>{string key=what+"|"+path;if(stuck==null){if(count++!=oldFiles.Length+3)return;stuck=key;}if(key==stuck)throw new IOException("injected");if(what=="move"&&path.EndsWith("DJMAXLocalRecords.exe"))throw new IOException("locked");};
  var broken=UpdateSwap.Exchange(app,UpdateSwap.Current(app),park,staged,bring,false,expected,log.Add);
  Check(broken is {Done:false,Restored:false,RestoreError:not null},"failed undo is reported as not restored");
  Check(Mine.All(f=>File.Exists(Path.Combine(app,UpdateFiles.Local(f.Name)))),"records still there even then");
  UpdateSwap.Probe=null;
  // 교체가 도중에 멈춰 섞였다: 새 파일 둘이 지워지지 않고 남고, 옛 파일 목록은 백업에서 돌아오지 못했다.
  // 이때 백업의 실행 파일은, 백업에 남은 옛 파일의 자리에 놓인 것을 그 교체가 들여놓은 것으로 보고 치워야 되돌릴 수 있다
  (app,staged,park)=Fresh("mixed");count=0;stuck=null;
  UpdateSwap.Probe=(what,path)=>{string key=what+"|"+path;if(stuck==null){if(count++!=oldFiles.Length+3)return;stuck=key;}if(key==stuck)throw new IOException("injected");if(what=="delete"||what=="move"&&path.EndsWith(UpdateManifest.FileName))throw new IOException("stuck");};
  var mixed=UpdateSwap.Exchange(app,UpdateSwap.Current(app),park,staged,bring,false,expected,log.Add);
  UpdateSwap.Probe=null;
  var inBackup=UpdateFiles.Under(park);
  Check(mixed is {Done:false,Restored:false}&&inBackup.Count==3&&inBackup.Contains(UpdateManifest.FileName)&&!File.Exists(Path.Combine(app,UpdateManifest.FileName)),"mixed folder: two new files stuck, the old list left in the backup");
  Check(UpdateSwap.Conflicts(app,UpdateSwap.Current(app),inBackup).Count==2&&UpdateSwap.Conflicts(app,UpdateSwap.Current(app,inBackup),inBackup).Count==0,"files the interrupted swap brought in are known to be the program's");
  var unmix=UpdateSwap.Exchange(app,UpdateSwap.Current(app,inBackup),Path.Combine(app,"update","replaced-y"),park,inBackup,false,null,log.Add);
  var(reference2,_,_)=Fresh("reference2");
  Check(unmix.Done&&Same(Program2(app),Program2(reference2)),"restoring from the backup untangles the mixed folder");

  // 새 파일은 모두 들어왔는데 새 프로그램 파일 목록을 넣기 전에 교체가 멈춘 경우. 새 버전에만 있는 파일(new-only.dll)은 백업에 같은 이름이 없어서,
  // 백업의 이름만으로 고르면 되돌린 뒤에도 남고, 다음 업데이트가 그것을 사용자의 파일로 보고 멈춘다.
  // 풀어 놓은 새 버전의 목록과 크기·해시가 같은 것만 그 교체가 들여놓은 것으로 본다
  (string App,UpdatePaths Paths,string Park) Stopped(string name)
  {
   var(a,_,p)=Fresh(name);
   foreach(string file in UpdateSwap.Current(a)){string to=Path.Combine(p,UpdateFiles.Local(file));Directory.CreateDirectory(Path.GetDirectoryName(to)!);File.Move(Path.Combine(a,UpdateFiles.Local(file)),to);}
   foreach(var(file,content)in newFiles){string to=Path.Combine(a,UpdateFiles.Local(file));Directory.CreateDirectory(Path.GetDirectoryName(to)!);File.WriteAllBytes(to,content);}
   var at=new UpdatePaths(a);at.WriteState(new("swapping","1.1.0","1.2.0","v1.1.0-x",UpdatePaths.Now));
   return(a,at,p);
  }
  // 사용자가 안내대로 백업의 program 폴더를 프로그램 폴더에 복사해 덮어쓴다
  void CopyBack(string a,string p){foreach(string file in UpdateFiles.Under(p)){string to=Path.Combine(a,UpdateFiles.Local(file));Directory.CreateDirectory(Path.GetDirectoryName(to)!);File.Copy(Path.Combine(p,UpdateFiles.Local(file)),to,true);}}
  // ① 백업의 실행 파일로 되돌린다
  var(stopApp,stopPaths,stopPark)=Stopped("stopped");var stopOld=UpdateFiles.Under(stopPark);
  var left=UpdateSwap.Leftovers(stopPaths,"v1.1.0-x");
  Check(left.Count==newFiles.Length&&left.Contains("new-only.dll"),"files the interrupted swap brought in are recognised by the staged list and their hashes");
  Check(UpdateSwap.Leftovers(stopPaths,"v0.9.0-other").Count==0,"but not when restoring another backup");
  var unstop=UpdateSwap.Exchange(stopApp,UpdateSwap.Current(stopApp,stopOld.Concat(left)),Path.Combine(stopApp,"update","replaced-z"),stopPark,stopOld,false,null,log.Add);
  Check(unstop.Done&&!File.Exists(Path.Combine(stopApp,"new-only.dll"))&&Same(Program2(stopApp),Program2(reference2)),"restore after such a stop leaves no file of the new version behind");
  stopPaths.ClearState();
  Check(Fails(()=>UpdateSwap.CheckPlace(stopApp,bring))==""&&UpdateSwap.Exchange(stopApp,UpdateSwap.Current(stopApp),Path.Combine(stopApp,"update","backup","v1.1.0-y","program"),stopPaths.StagedApp,bring,false,expected,log.Add).Done&&UpdateManifest.Read(stopApp)!.Version=="1.2.0","and the same update then goes through");
  // ② 손으로 복사해 되돌린다: 새 버전에만 있는 파일이 남는다. 다음 업데이트를 준비할 때 가려내 치워 둔다
  (stopApp,stopPaths,stopPark)=Stopped("copied-back");CopyBack(stopApp,stopPark);
  Check(Fails(()=>UpdateSwap.CheckPlace(stopApp,bring)).Contains("new-only.dll"),"copied back by hand: the new version's own file is still there and would stop the next update");
  Check(UpdateSwap.Leftovers(stopPaths).SequenceEqual(new[]{"new-only.dll"}),"only that file is recognised as left by the interrupted swap");
  Check(UpdateSwap.SetAside(stopPaths)==1&&!File.Exists(Path.Combine(stopApp,"new-only.dll"))&&Directory.EnumerateFiles(stopPaths.Root,"new-only.dll",SearchOption.AllDirectories).Any(f=>f.Contains("failed-"))&&Same(Program2(stopApp),Program2(reference2)),"it is set aside before the next update and nothing else is touched");
  Check(Fails(()=>UpdateSwap.CheckPlace(stopApp,bring))=="","the next update is no longer stopped");
  // 내용이 다르면 그 교체가 들여놓은 것이라고 확인할 수 없다: 사용자의 파일로 보고 그대로 둔다
  (stopApp,stopPaths,stopPark)=Stopped("copied-back-users");CopyBack(stopApp,stopPark);File.WriteAllText(Path.Combine(stopApp,"new-only.dll"),"USER DATA");
  Check(UpdateSwap.Leftovers(stopPaths).Count==0&&UpdateSwap.SetAside(stopPaths)==0&&File.ReadAllText(Path.Combine(stopApp,"new-only.dll"))=="USER DATA"&&Fails(()=>UpdateSwap.CheckPlace(stopApp,bring)).Contains("new-only.dll"),"a file with other content is not taken for a leftover: kept, and the update still stops for it");
  // 풀어 놓은 새 버전이 남아 있지 않거나, 다른 버전이거나, 멈춘 흔적이 없으면 확인할 수 없으므로 건드리지 않는다
  (stopApp,stopPaths,stopPark)=Stopped("copied-back-no-staging");CopyBack(stopApp,stopPark);Directory.Delete(stopPaths.Staging,true);
  Check(UpdateSwap.Leftovers(stopPaths).Count==0&&UpdateSwap.SetAside(stopPaths)==0&&File.Exists(Path.Combine(stopApp,"new-only.dll")),"without the staged new version nothing can be verified, so nothing is moved");
  (stopApp,stopPaths,stopPark)=Stopped("copied-back-other-version");CopyBack(stopApp,stopPark);stopPaths.WriteState(new("broken","1.1.0","1.3.0","v1.1.0-x",UpdatePaths.Now));
  Check(UpdateSwap.Leftovers(stopPaths).Count==0,"nor when the staged version is not the one the interrupted swap was installing");
  stopPaths.WriteState(new("broken","1.1.0","1.2.0","v1.1.0-x",UpdatePaths.Now));
  Check(UpdateSwap.Leftovers(stopPaths).SequenceEqual(new[]{"new-only.dll"}),"a swap that could not be undone (broken) counts as interrupted too");
  stopPaths.ClearState();
  Check(UpdateSwap.Leftovers(stopPaths).Count==0&&UpdateSwap.SetAside(stopPaths)==0&&File.Exists(Path.Combine(stopApp,"new-only.dll")),"and never without the mark of an interrupted swap");
  // 실제 잠금: 다른 쪽이 잡고 있는 파일은 옮길 수 없다
  (app,staged,park)=Fresh("locked");untouched=Picture(app);
  using(new FileStream(Path.Combine(app,"overlay.html"),FileMode.Open,FileAccess.Read,FileShare.Read))
  {
   var locked=UpdateSwap.Exchange(app,UpdateSwap.Current(app),park,staged,bring,false,expected,log.Add);
   Check(locked is {Done:false,Restored:true},"a file held open by another program: swap fails and rolls back");
  }
  Check(Same(Picture(app),untouched),"folder as before after the held file");
  UpdateSwap.Pause=pause;
 }

 static void Backups(string root)
 {
  string app=Path.Combine(root,"app");Lay(app,Program("1.1.0"),"1.1.0");Lay(app,Mine,null);var paths=new UpdatePaths(app);
  Check(UpdateBackup.Latest(paths)==null,"no backup at first");
  string name=UpdateBackup.Create(paths,new Version(1,1,0),new Version(1,2,0));string folder=Path.Combine(paths.Backups,name);
  Check(name.StartsWith("v1.1.0-")&&UpdateBackup.Info(folder) is {Version:"1.1.0",ReplacedBy:"1.2.0",Program:false},"backup folder made and marked");
  Check(UpdateFiles.DataFiles.All(file=>UpdateFiles.Sha256(Path.Combine(UpdateBackup.Data(folder),file))==UpdateFiles.Sha256(Path.Combine(app,file)))&&UpdateFiles.Under(UpdateBackup.Data(folder)).Count==UpdateFiles.DataFiles.Length,"records and settings copied, nothing else");
  Check(UpdateBackup.Latest(paths)==null,"backup without program files is not offered for restore");
  File.WriteAllText(Path.Combine(UpdateBackup.Program(folder),UpdateManifest.Executable),"exe");UpdateBackup.MarkProgram(folder,true);
  Check(UpdateBackup.Latest(paths) is {} latest&&latest.Folder==folder,"backup with program files is offered");
  // 안내 글을 파일로 두지 않는다. 창에 넣는 글이 실행할 파일·복사할 폴더와 GitHub 안내를 알려 준다
  Check(UpdateFiles.Under(folder).All(file=>file.StartsWith("data/")||file.StartsWith("program/")||file==UpdateBackup.InfoName),"backup folder holds only the program, the data copy and its marker");
  Check(UpdateBackup.HowToRestore(folder).Contains(Path.Combine(folder,"program",UpdateManifest.Executable))&&UpdateBackup.HowToRestore(folder).Contains(UpdateBackup.GuidePage)&&UpdateBackup.HowToCopyBack(folder).Contains(Path.Combine(folder,"program"))&&UpdateBackup.HowToCopyBack(folder).Contains(UpdateBackup.GuidePage)&&UpdateBackup.GuidePage.StartsWith("https://github.com/Zionee148/DJMAXLocalRecords"),"restore hints name the file to run, the folder to copy and the guide page");
  Check(UpdatePaths.FromBackup(UpdateBackup.Program(folder)+Path.DirectorySeparatorChar,out string foundApp,out string foundBackup)&&foundApp==paths.App&&foundBackup==folder,"backup folder recognized from its program folder");
  // 다음 업데이트가 성공하면 이번 것만 남긴다. 표시가 없는 폴더와 파일은 사용자의 것으로 보고 건드리지 않는다
  Thread.Sleep(1100);string second=UpdateBackup.Create(paths,new Version(1,2,0),new Version(1,3,0));
  string users=Path.Combine(paths.Backups,"내가 만든 백업");Directory.CreateDirectory(users);File.WriteAllText(Path.Combine(users,"records.json"),"mine");File.WriteAllText(Path.Combine(paths.Backups,"note.txt"),"mine");
  UpdateBackup.Retain(paths,second);
  Check(!Directory.Exists(folder)&&Directory.Exists(Path.Combine(paths.Backups,second)),"older automatic backup removed, the current one kept");
  Check(File.Exists(Path.Combine(users,"records.json"))&&File.Exists(Path.Combine(paths.Backups,"note.txt"))&&File.Exists(Path.Combine(app,"records.json.bak"))&&Directory.Exists(Path.Combine(app,"내 백업")),"user's own backups and the store's .bak untouched");
  UpdateProgram.RemoveNewBackup(paths,second);Check(!Directory.Exists(Path.Combine(paths.Backups,second)),"unused new backup removed after a cancelled attempt");
  UpdateProgram.RemoveNewBackup(paths,"내가 만든 백업");UpdateProgram.RemoveNewBackup(paths,"..");UpdateProgram.RemoveNewBackup(paths,"");
  Check(Directory.Exists(users)&&File.Exists(Path.Combine(app,"records.json")),"cleanup never touches unmarked folders");
  // 백업하는 사이에 기록이 바뀌면 멈춘다(바뀐 것을 모르는 채 교체로 넘어가지 않는다)
  string blocked=Path.Combine(root,"blocked");Lay(blocked,Mine,null);File.WriteAllText(Path.Combine(blocked,"update"),"a file where the folder should be");
  bool refused=false;try{UpdateBackup.Create(new(blocked),new Version(1,1,0),new Version(1,2,0));}catch(IOException){refused=true;}catch(UnauthorizedAccessException){refused=true;}
  Check(refused,"backup that cannot be written stops the update");
  Check(Fails(()=>UpdatePrepare.CheckFolder(new(blocked),1000)).Contains("쓸 수 없어"),"folder that cannot be written is reported before downloading");
  Check(Fails(()=>UpdatePrepare.CheckFolder(paths,long.MaxValue/16)).Contains("빈 공간"),"not enough disk space is reported before downloading");
 }

 static void Startup(string root)
 {
  string me=AppVersion.Plain(AppVersion.Current);int n=0;
  UpdatePaths Fresh(){string app=Path.Combine(root,"s"+(++n));Lay(app,Program(me),me);Lay(app,Mine,null);return new(app);}
  var paths=Fresh();
  Check(UpdateStartup.Resolve(paths) is (null,null)&&!Directory.Exists(paths.Root),"nothing to do without a past update");
  // 새 버전이 정상으로 떴다: 이때 앞의 백업을 지운다
  paths=Fresh();string old=UpdateBackup.Create(paths,new Version(0,9,0),new Version(0,9,5));Thread.Sleep(1100);string now=UpdateBackup.Create(paths,new Version(0,9,5),AppVersion.Current);
  Directory.CreateDirectory(paths.StagedApp);File.WriteAllText(Path.Combine(paths.StagedApp,"x"),"x");
  paths.WriteState(new("swapped","0.9.5",me,now,UpdatePaths.Now));
  var(note,notice)=UpdateStartup.Resolve(paths);
  Check(note!=null&&note.Contains("업데이트 완료")&&notice==null&&!File.Exists(paths.StateFile),"first normal start confirms the update");
  Check(!Directory.Exists(Path.Combine(paths.Backups,old))&&Directory.Exists(Path.Combine(paths.Backups,now)),"only then the older backup is removed");
  for(int i=0;i<40&&Directory.Exists(paths.Staging);i++)Thread.Sleep(250);
  Check(!Directory.Exists(paths.Staging),"staged files cleaned up afterwards");
  // 새 버전이 아닌 것이 켜졌으면(되돌린 뒤 등) 확인으로 치지 않고 백업도 지우지 않는다
  paths=Fresh();old=UpdateBackup.Create(paths,new Version(0,9,0),new Version(0,9,5));
  paths.WriteState(new("swapped",me,"99.0.0",old,UpdatePaths.Now));UpdateStartup.Resolve(paths);
  Check(Directory.Exists(Path.Combine(paths.Backups,old)),"another version starting does not confirm, backups kept");
  // 교체 전에 멈춘 흔적: 이번 시도의 빈 백업만 치운다
  paths=Fresh();old=UpdateBackup.Create(paths,new Version(0,9,0),new Version(0,9,5));File.WriteAllText(Path.Combine(UpdateBackup.Program(Path.Combine(paths.Backups,old)),UpdateManifest.Executable),"exe");UpdateBackup.MarkProgram(Path.Combine(paths.Backups,old),true);
  Thread.Sleep(1100);now=UpdateBackup.Create(paths,AppVersion.Current,new Version(99,0,0));paths.WriteState(new("prepared",me,"99.0.0",now,UpdatePaths.Now));
  (note,notice)=UpdateStartup.Resolve(paths);
  Check(note!=null&&notice==null&&!File.Exists(paths.StateFile)&&!Directory.Exists(Path.Combine(paths.Backups,now))&&Directory.Exists(Path.Combine(paths.Backups,old)),"attempt that never reached the swap is cleaned up, earlier backup kept");
  // 교체 도중에 멈춘 흔적: 아무것도 지우지 않고 알린다
  paths=Fresh();now=UpdateBackup.Create(paths,AppVersion.Current,new Version(99,0,0));paths.WriteState(new("swapping",me,"99.0.0",now,UpdatePaths.Now));
  (note,notice)=UpdateStartup.Resolve(paths);
  Check(notice!=null&&notice.Contains(now)&&File.Exists(paths.StateFile)&&Directory.Exists(Path.Combine(paths.Backups,now)),"interrupted swap is reported with the backup folder and nothing is removed");
  paths=Fresh();paths.WriteState(new("rolled-back",me,"99.0.0","x",UpdatePaths.Now,"why"));
  Check(UpdateStartup.Resolve(paths) is (not null,null)&&!File.Exists(paths.StateFile),"rolled back update is noted once");
  paths=Fresh();Directory.CreateDirectory(Path.Combine(paths.Root,"replaced-1"));paths.WriteState(new("restored","99.0.0",me,"x",UpdatePaths.Now));
  (note,notice)=UpdateStartup.Resolve(paths);for(int i=0;i<40&&Directory.Exists(Path.Combine(paths.Root,"replaced-1"));i++)Thread.Sleep(250);
  Check(note!=null&&note.Contains("되돌림")&&!Directory.Exists(Path.Combine(paths.Root,"replaced-1")),"restore is noted and the parked newer program removed");
  paths=Fresh();Directory.CreateDirectory(paths.Root);File.WriteAllText(paths.StateFile,"{broken");
  Check(UpdateStartup.Resolve(paths) is (null,null)&&File.Exists(paths.StateFile),"unreadable state file is left alone");
  // 지난 시도의 임시 파일을 다른 프로그램이 잡고 있어 임시 폴더 정리(CleanLater)가 끝나지 않는 사이에 새 업데이트가 시작되는 경우.
  // 그 정리는 같은 자리(download, staging)를 다시 지우므로, 새 업데이트를 준비하기 전에 멈춰야 한다
  paths=Fresh();Directory.CreateDirectory(paths.Staging);string held=Path.Combine(paths.Staging,"held.bin"),fresh=Path.Combine(paths.Download,"new-download.part");File.WriteAllText(held,"left by an earlier attempt");
  void Put(){for(int i=0;;i++)try{Directory.CreateDirectory(paths.Download);File.WriteAllText(fresh,"new attempt");return;}catch(Exception e)when((e is IOException or UnauthorizedAccessException)&&i<50){Thread.Sleep(10);}}
  int cleanPause=UpdateStartup.CleanPause;UpdateStartup.CleanPause=100;
  using(new FileStream(held,FileMode.Open,FileAccess.Read,FileShare.Read))
  {
   UpdateStartup.Resolve(paths);
   // 멈추지 않으면: 임시 폴더 정리가 다시 돌면서 새로 생긴 파일을 지운다(검사가 그 상황을 실제로 만들었는지 본다)
   Put();for(int i=0;i<100&&File.Exists(fresh);i++)Thread.Sleep(20);
   Check(!File.Exists(fresh)&&File.Exists(held),"cleanup keeps retrying while an old file is held, and would remove a new attempt's file");
   UpdateStartup.StopCleaning();
   Put();Thread.Sleep(700);
   Check(File.Exists(fresh)&&File.Exists(held),"once stopped for a new update, cleanup no longer touches the new attempt's files");
  }
  UpdateStartup.CleanPause=cleanPause;
 }

 // ── 기록 저장: 잠금과 형식 번호
 static void Store(string root)
 {
  Directory.CreateDirectory(root);string path=Path.Combine(root,"records.json");
  var store=new RecordStore(path);store.Create("둘째",false);store.Create("예비",false);
  string saved=File.ReadAllText(path);
  Check(JsonDocument.Parse(saved).RootElement.GetProperty("Format").GetInt32()==SavedData.KnownFormat,"format number written");
  string before=UpdateFiles.Sha256(path),backupBefore=UpdateFiles.Sha256(path+".bak");int profiles=store.Data.Profiles.Count;
  store.Frozen=true;bool frozen=false;
  try{store.Create("셋째",false);}catch(RecordStoreFrozenException){frozen=true;}
  Check(frozen&&store.Data.Profiles.Count==profiles&&UpdateFiles.Sha256(path)==before&&UpdateFiles.Sha256(path+".bak")==backupBefore&&!File.Exists(path+".tmp"),"frozen store refuses to save, changes nothing on disk, rolls memory back");
  store.Frozen=false;store.Create("셋째",false);
  Check(store.Data.Profiles.Count==profiles+1&&new RecordStore(path).Data.Profiles.Count==profiles+1,"saving works again after the freeze is lifted");
  // 번호가 없는 옛 파일은 1로 읽는다
  string old=Path.Combine(root,"old.json");File.WriteAllText(old,saved.Replace("\"Format\": 1,",""));
  Check(!File.ReadAllText(old).Contains("Format")&&new RecordStore(old).Data is {Format:1,Profiles.Count:3},"file without a format number reads as format 1");
  // 더 새 형식은 읽지 않고, 파일을 건드리지 않고 알린다
  string newer=Path.Combine(root,"newer.json");File.WriteAllText(newer,saved.Replace("\"Format\": 1","\"Format\": 2"));string newerBefore=UpdateFiles.Sha256(newer);
  string message="";try{_=new RecordStore(newer);}catch(InvalidDataException e){message=e.Message;}
  Check(message.Contains("새 버전")&&message.Contains("건드리지 않았습니다")&&UpdateFiles.Sha256(newer)==newerBefore&&!File.Exists(newer+".bak")&&!File.Exists(newer+".tmp"),"file of a newer format is refused and left untouched");
 }
}
