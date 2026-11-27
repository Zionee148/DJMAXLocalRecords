namespace LadderRecords;

// 켜져 있는 프로그램이 띄우는 업데이트·복구 안내와 창을 본다(--dialogs-check 가 부른다). 결과는 update-guide-validation.txt
// 보는 것: 까닭의 종류마다 첫 줄과 다음 행동, 작업 중 안내 열 가지, 복구 확인의 두 문안과 기본 선택, 지난 업데이트 중단과 백업을 모를 때,
//  첫 줄이 안내창의 너비 안에 들어가는가, 새 버전 안내의 기본 선택과 패치노트 칸, 진행 창의 단계·용량·높이·취소와 잠금, 까닭에 종류가 실리는가
[System.Runtime.Versioning.SupportedOSPlatform("windows10.0.19041")]
static class UpdateGuideChecks
{
 static IEnumerable<Control> All(Control top){foreach(Control child in top.Controls){yield return child;foreach(var inner in All(child))yield return inner;}}
 static UpdateTrouble KindOf(Action action){try{action();}catch(UpdateException e){return e.Kind;}return(UpdateTrouble)(-1);}
 public static void Run()
 {
  var notes=new List<string>();void Check(bool value,string text){if(!value)throw new Exception("UpdateGuide: "+text);notes.Add("PASS: "+text);}
  var now=new Version(1,2,1);
  static string Strong(Guide guide)=>string.Join("\n",guide.Parts.OfType<GuideText>().Where(text=>text.Tone=="strong").Select(text=>text.Text));

  // 1. 업데이트 확인의 짧은 결과 셋
  Check(UpdateGuides.Latest(now) is {Title:"업데이트",Headline:"현재 버전(v1.2.1)이 최신 버전입니다.",Parts.Count:0}&&UpdateGuides.Latest(now).ButtonTexts.SequenceEqual(new[]{"확인"}),"up to date: one line and '확인'");
  var failed=UpdateGuides.CheckFailed("서버가 응답하지 않습니다. 인터넷 연결을 확인해 주세요.");
  Check(failed.Headline=="새 버전이 있는지 확인하지 못했습니다"&&failed.Plain().Contains("인터넷 연결을 확인해 주세요.")&&failed.Plain().Contains("현재 버전을 계속 사용할 수 있습니다."),"check failed: what stopped, the reason with its next step, and that this version still works");
  var package=UpdateGuides.NoPackage(new Version(1,2,2),"릴리즈에 자동 업데이트용 파일(x.zip)이 없습니다.");
  Check(package.Headline=="자동으로 업데이트할 수 없습니다."&&package.ButtonTexts.SequenceEqual(new[]{"릴리즈 페이지 열기","닫기"})&&package.Default==1&&package.Plain().Contains("v1.2.2")&&package.Plain().Contains("x.zip")&&package.Plain().Contains("릴리즈 페이지에서 직접 받아 설치해야 합니다."),"no package: says to install it from the release page, '닫기' is the default");

  // 2. 작업 중 안내: 까닭 다섯의 첫 줄과, 업데이트·복구마다 다시 시도하라는 둘째 줄. 둘째 줄 가운데 둘(감지 중의 업데이트, 저장 확인 대기의 복구)은 글자 그대로 견준다
  var heads=new Dictionary<UpdateBusy,string>{[UpdateBusy.Scoring]="등록할 점수를 확인하고 있습니다.",[UpdateBusy.Pending]="저장 확인을 기다리고 있습니다.",[UpdateBusy.Detecting]="게임 화면을 감지하고 있습니다.",[UpdateBusy.Importing]="V아카이브 기록을 가져오고 있습니다.",[UpdateBusy.OtherWindow]="다른 창이 열려 있습니다."};
  foreach(var (why,head) in heads)foreach(bool restore in new[]{false,true})
  {
   var busy=UpdateGuides.Busy(why,restore);string next=((GuideText)busy.Parts.Single()).Text;
   Check(busy.Title==(restore?"이전 버전 복구":"업데이트")&&busy.Headline==head&&next.EndsWith((restore?"복구를":"업데이트를")+" 다시 시도해 주세요.")&&busy.ButtonTexts.SequenceEqual(new[]{"확인"}),$"busy {why} ({(restore?"restore":"update")}): '{head}' and what to do before trying again");
  }
  Check(((GuideText)UpdateGuides.Busy(UpdateBusy.Detecting,false).Parts[0]).Text=="‘중지 / 숨기기’로 감지를 멈춘 뒤 업데이트를 다시 시도해 주세요."&&((GuideText)UpdateGuides.Busy(UpdateBusy.Pending,true).Parts[0]).Text=="기록을 저장하거나 취소한 뒤 복구를 다시 시도해 주세요.","two of the second lines are checked word for word");

  // 3. 교체 전에 멈춘 업데이트: 종류마다 첫 줄과 다음 행동. 모두 현재 버전·보존 줄이 있고, 받은 글은 펼치는 '오류 내용' 안에 그대로 있다
  string app="D:\\DJMAXLocalRecords";
  var cases=new (UpdateException Error,string Head,string Next)[]
  {
   (new("받는 도중 연결이 끊겼습니다(파일을 끝까지 받지 못했습니다).",UpdateTrouble.Network),"업데이트 파일을 다 받지 못했습니다","인터넷 연결을 확인한 뒤 ‘업데이트 확인’을 눌러 다시 시도해 주세요."),
   (new("받은 파일을 디스크에 쓰지 못했습니다. (다른 프로세스가 사용 중)",UpdateTrouble.Disk),"받은 업데이트 파일을 저장하지 못했습니다","디스크의 빈 공간이 충분한지, 프로그램 폴더 안의 update 폴더를 다른 프로그램(백신, 동기화 프로그램 등)이 쓰고 있지 않은지 확인한 뒤 ‘업데이트 확인’을 눌러 다시 시도해 주세요."),
   (new("받은 파일이 릴리즈에 적힌 것과 다릅니다(SHA-256이 맞지 않습니다).",UpdateTrouble.BadFile),"받은 업데이트 파일이 올바르지 않습니다",UpdateGuides.Retry),
   (new("남은 빈 공간은 100 MB이고, 업데이트에는 700 MB 이상이 필요합니다.",UpdateTrouble.NoSpace,app,null,700L<<20),"디스크 공간이 부족합니다","프로그램 폴더가 있는 디스크의 빈 공간을 700 MB 이상 확보한 뒤 ‘업데이트 확인’을 눌러 다시 시도해 주세요."),
   (new("남은 빈 공간이 모자랍니다.",UpdateTrouble.NoSpace),"디스크 공간이 부족합니다","프로그램 폴더가 있는 디스크의 빈 공간을 확보한 뒤 ‘업데이트 확인’을 눌러 다시 시도해 주세요."),
   (new("프로그램 폴더에 파일을 만들지 못했습니다. (x)",UpdateTrouble.Locked,app),"프로그램 폴더에 쓸 수 없습니다","프로그램을 종료하고 프로그램 폴더를 쓰기 권한이 있는 곳(예: 문서, 바탕 화면)으로 옮긴 뒤, 옮긴 폴더에서 다시 실행해 ‘업데이트 확인’을 눌러 주세요."),
   (new("이 프로그램의 것이 아닌 같은 이름의 파일이나 폴더가 있습니다: …",UpdateTrouble.Clash,app,new[]{app+"\\overlay.html",app+"\\data\\new.txt"}),"새 버전의 파일과 이름이 같은 항목이 있습니다","위 항목을 프로그램 폴더 밖으로 옮긴 뒤 ‘업데이트 확인’을 눌러 다시 시도해 주세요."),
   (new("지금 설치된 프로그램의 파일 목록(update-manifest.json)이 없거나 읽히지 않습니다.",UpdateTrouble.NoList),"설치된 프로그램의 파일 목록을 읽지 못했습니다","GitHub에서 새 버전을 직접 받아 설치해 주세요."),
   (new("기록과 설정을 백업하지 못했습니다. (x)",UpdateTrouble.Backup),"기록과 설정을 백업하지 못했습니다",UpdateGuides.Retry),
   (new("새 버전의 설치 과정이 40초 동안 응답하지 않았습니다.",UpdateTrouble.HandOver),"새 버전의 설치를 시작하지 못했습니다",UpdateGuides.Retry),
   (new("Access to the path is denied."),"업데이트를 준비하지 못했습니다",UpdateGuides.Retry),
  };
  foreach(var (error,head,next) in cases)
  {
   var guide=UpdateGuides.Failed(error,now);var fold=guide.Parts.OfType<GuideFold>().Single();
   Check(guide.Title=="업데이트 실패"&&guide.Headline==head&&guide.Parts[0]==new GuideText("현재 버전(v1.2.1)을 계속 사용할 수 있습니다.","strong")&&guide.Parts[1]==new GuideText(UpdateGuides.Kept,"dim")
    &&Strong(guide).Split('\n').Last()==next&&fold.Title=="오류 내용"&&fold.Inside.Single()==new GuideText(error.Message)&&guide.Parts[^1]==fold,$"failed before the swap ({error.Kind}): '{head}', this version still works, records kept, '{next}', the received words under '오류 내용'");
  }
  var clash=UpdateGuides.Failed(cases[6].Error,now);
  Check(clash.Parts.OfType<GuidePaths>().Single().Items.Single() is ("이름이 겹치는 항목",var clashed)&&clashed==app+"\\overlay.html\n"+app+"\\data\\new.txt","a clash shows the paths in a box before the next step");
  Check(UpdateGuides.Failed(cases[5].Error,now).Parts.OfType<GuidePaths>().Single().Items.Single()==("프로그램 폴더",app)&&UpdateGuides.Failed(cases[7].Error,now).Parts.OfType<GuideLink>().Single()==new GuideLink("업데이트·복구 안내",UpdateBackup.GuidePage),"a locked folder names the folder, a missing list points to the guide page");
  Check(UpdateGuides.Failed(new Exception(""),now).Parts.OfType<GuideFold>().Single().Inside.Single()==new GuideText("알 수 없는 오류입니다."),"an error without words still shows something under '오류 내용' (in Korean, not the type name)");
  Check(UpdateGuides.Cancelled(now) is {Headline:"업데이트를 취소했습니다"}&&UpdateGuides.Cancelled(now).Plain().Contains("현재 버전(v1.2.1)을 계속 사용할 수 있습니다.")&&UpdateGuides.Cancelled(now).Plain().Contains(UpdateGuides.Kept),"cancelled: says this version still works and the records are kept");

  // 4. 까닭에 종류가 실린다(문장을 만드는 자리마다. 문장은 그대로다)
  string root=Path.Combine(AppContext.BaseDirectory,"test-data","update-guide-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  string blocked=Path.Combine(root,"blocked");Directory.CreateDirectory(blocked);File.WriteAllText(Path.Combine(blocked,"update"),"a file where the folder should be");
  try{UpdatePrepare.CheckFolder(new(blocked),1000);}catch(UpdateException e){Check(e.Kind==UpdateTrouble.Locked&&e.Place==new UpdatePaths(blocked).App,"a folder that cannot be written is Locked and names the program folder");}
  try{UpdatePrepare.CheckFolder(new(root),long.MaxValue/16);Check(false,"space");}catch(UpdateException e){Check(e.Kind==UpdateTrouble.NoSpace&&e.Needed>0,"too little disk space is NoSpace with the bytes needed");}
  string bare=Path.Combine(root,"bare");Directory.CreateDirectory(bare);
  Check(KindOf(()=>UpdateSwap.CheckPlace(bare,new[]{"a.dll"}))==UpdateTrouble.NoList,"an installation without its file list is NoList");
  string zip=Path.Combine(root,"not.zip");File.WriteAllText(zip,"not a zip");
  Check(KindOf(()=>UpdatePackage.Extract(zip,Path.Combine(root,"staging"),now))==UpdateTrouble.BadFile,"a package that is not a ZIP is BadFile");
  var release=new ReleaseInfo(now,"",UpdateSource.Official.PagePrefix,"x.zip",UpdateSource.Official.DownloadPrefix+"x.zip",5,new string('a',64));
  Check(KindOf(()=>UpdatePackage.VerifyDownload(zip,release))==UpdateTrouble.BadFile,"a downloaded file of the wrong size is BadFile");
  Check(KindOf(()=>UpdatePrepare.Launch(new UpdatePaths(root),Path.Combine(root,"missing.exe"),"--x","시험 과정"))==UpdateTrouble.HandOver,"a hand-over that cannot start is HandOver");
  var lines=UpdateSwap.ListedLines(app,Enumerable.Range(1,7).Select(i=>$"f{i}.txt").ToList());
  Check(lines.Count==6&&lines[0]==app+"\\f1.txt"&&lines[^1]=="(그 밖에 2개)"&&UpdateSwap.Listed(app,new[]{"a.txt"})=="  "+app+"\\a.txt","the clash list keeps five paths and counts the rest; the message keeps its old shape");
  // 내려받다 디스크에 쓰지 못한 것은 Disk, 받다가 끊긴 것은 Network. 이 PC 안에서만 응답하는 작은 서버로 실제 내려받기 함수를 부른다
  UpdateTrouble Downloaded(int sent,bool lockPart)
  {
   var listener=new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback,0);listener.Start();int port=((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
   var body=new byte[4000];
   var serving=Task.Run(async()=>{try{using var client=await listener.AcceptTcpClientAsync();using var stream=client.GetStream();var buffer=new byte[4096];_=await stream.ReadAsync(buffer);
    await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n"));await stream.WriteAsync(body.AsMemory(0,sent));}catch(Exception){}});
   string target=Path.Combine(root,"download-"+Guid.NewGuid().ToString("N"),"x.zip");Directory.CreateDirectory(Path.GetDirectoryName(target)!);
   var release=new ReleaseInfo(now,"",UpdateSource.Official.PagePrefix,"x.zip",$"http://127.0.0.1:{port}/x.zip",body.Length,new string('a',64));
   try
   {
    using var held=lockPart?new FileStream(target+".part",FileMode.Create,FileAccess.Write,FileShare.None):null;
    try{Task.Run(()=>UpdatePackage.Download(release,target,null,default)).GetAwaiter().GetResult();}catch(UpdateException e){return e.Kind;}
    return(UpdateTrouble)(-1);
   }
   finally{listener.Stop();try{serving.Wait(5000);}catch(AggregateException){}}
  }
  Check(Downloaded(4000,true)==UpdateTrouble.Disk,"a download that cannot write its file (held by another process) is Disk, not a network problem");
  Check(Downloaded(1000,false)==UpdateTrouble.Network,"a download cut short by the server is still Network");
  // 풀다가 쓰지 못한 것도 Disk(풀 자리에 같은 이름의 파일이 있어 폴더를 만들지 못한다)
  string goodZip=Path.Combine(root,"good.zip");
  using(var zipFile=System.IO.Compression.ZipFile.Open(goodZip,System.IO.Compression.ZipArchiveMode.Create)){var entry=zipFile.CreateEntry("DJMAXLocalRecords/a.txt");using var write=new StreamWriter(entry.Open());write.Write("a");}
  string blockedStaging=Path.Combine(root,"blocked-staging");Directory.CreateDirectory(blockedStaging);File.WriteAllText(Path.Combine(blockedStaging,"DJMAXLocalRecords"),"a file where the folder should be");
  Check(KindOf(()=>UpdatePackage.Extract(goodZip,blockedStaging,now))==UpdateTrouble.Disk,"a package that cannot be written out while unpacking is Disk");
  try{Directory.Delete(root,true);}catch(IOException){}catch(UnauthorizedAccessException){}

  // 5. 주 화면의 '이전 버전 복구'
  Check(UpdateGuides.NoBackup is {Title:"이전 버전 복구",Headline:"이전 버전의 프로그램 백업을 찾을 수 없습니다."}&&UpdateGuides.NoBackup.Plain().EndsWith("현재 버전을 계속 사용할 수 있습니다."),"no backup: the headline and the last line");
  var ask=UpdateGuides.RestoreAsk(now,"1.2.0","2026-11-21 18:34:09");var versions=ask.Parts.OfType<GuideVersions>().Single();
  Check(ask is {Title:"이전 버전 복구",Headline:"이전 버전으로 복구할까요?",Default:1}&&ask.ButtonTexts.SequenceEqual(new[]{"복구","취소"})&&versions==new GuideVersions("현재 버전","v1.2.1","복구할 버전","v1.2.0","백업 시각","2026-11-21 18:34:09")
   &&Strong(ask)=="이전 버전에서도 ‘UserData’ 폴더의 기록과 설정을 그대로 사용합니다."&&ask.Plain().EndsWith("복구하면 프로그램이 잠시 종료된 뒤 이전 버전으로 다시 실행됩니다."),"restore: both versions and the backup time, '취소' is the default, the same folder is used by 1.2.0");
  Check(UpdateGuides.RecordsAfterRestore("1.1.0",false)==UpdateGuides.OldPlace&&UpdateGuides.RecordsAfterRestore("1.1.0").Contains("따로 보관됩니다")&&UpdateGuides.RecordsAfterRestore("").Contains("확인할 수 없습니다")&&UpdateGuides.RecordsAfterRestore("1.3.0",false).Contains("그대로 사용합니다"),
   "the records line says only what is known: before the first move it does not claim the new folder, an unknown version does not decide the place");
  {
   string converted=Path.Combine(AppContext.BaseDirectory,"test-data","update-guide-converted-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(UserFiles.FolderOf(converted));
   try
   {
    string folder=UserFiles.FolderOf(converted),info=Path.Combine(folder,UserFiles.InfoName);
    void Info(params string[] settled)=>File.WriteAllText(info,System.Text.Json.JsonSerializer.Serialize(new UserFiles.Info(1,settled.ToList(),"","")));
    bool nothing=UserFiles.RecordsMoved(converted);
    Info("hotkeys.json","preferences.json","appearance.json");bool settingsOnly=UserFiles.RecordsMoved(converted);
    File.WriteAllText(Path.Combine(folder,"records.json"),"{}");bool fileThere=UserFiles.RecordsMoved(converted);File.Delete(Path.Combine(folder,"records.json"));
    Info("records.json","records.json.bak","preferences.json");bool settled=UserFiles.RecordsMoved(converted);
    File.WriteAllText(info,"{broken");bool broken=UserFiles.RecordsMoved(converted);
    Check(!nothing&&!settingsOnly&&fileThere&&settled&&!broken,"the records count as moved only when the new folder has the records file or the folder-info says so; moving settings alone is not enough");
   }
   finally{try{Directory.Delete(converted,true);}catch(IOException){}catch(UnauthorizedAccessException){}}
  }
  Check(Strong(UpdateGuides.RestoreAsk(now,"1.1.0","x")).StartsWith("이전 버전은 실행 파일 옆의 기록과 설정을 사용합니다.")&&UpdateGuides.UsesOldPlace("1.1.9")==true&&UpdateGuides.UsesOldPlace("1.2.0")==false&&UpdateGuides.UsesOldPlace("엉터리")==null&&UpdateGuides.UsesOldPlace("0.0.0")==null,"a version below 1.2.0 reads the records beside its program: the restore says so");
  string backup=Path.Combine(AppContext.BaseDirectory,"test-data","update-guide-backup-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(UpdateBackup.Program(backup));File.WriteAllText(Path.Combine(UpdateBackup.Program(backup),UpdateManifest.Executable),"exe");
  try
  {
   var notStarted=UpdateGuides.RestoreNotStarted(new UpdateException("복구 과정이 40초 동안 응답하지 않았습니다.",UpdateTrouble.HandOver),backup,app);
   Check(notStarted.Headline=="이전 버전으로 복구하지 못했습니다"&&Strong(notStarted).StartsWith("복구를 시작하지 못했습니다. 현재 버전은 그대로입니다.")&&notStarted.Parts.OfType<GuideFold>().Select(fold=>fold.Title).SequenceEqual(new[]{"오류 내용","직접 복구하는 방법"}),"a restore that could not start says nothing changed, with the error and the manual way folded");
   var interrupted=UpdateGuides.Interrupted(backup,app+"\\");var copy=interrupted.Parts.OfType<GuideFold>().Single();
   Check(interrupted is {Title:"업데이트",Headline:"지난 업데이트가 중단되었습니다"}&&copy.Title=="프로그램에 문제가 있을 때 · 백업으로 복구하는 방법"
    &&copy.Inside.OfType<GuidePaths>().Single().Items.SequenceEqual(new[]{("복사할 백업 폴더",UpdateBackup.Program(backup)),("붙여넣을 프로그램 폴더",app)})
    &&copy.Inside.OfType<GuideSteps>().Single().Steps[1]=="위에 표시된 백업 폴더 안의 파일과 폴더를 프로그램 폴더에 복사합니다. 이름이 같은 파일은 덮어씁니다."&&copy.Inside.OfType<GuideLink>().Single().Name=="업데이트·복구 안내",
    "an interrupted update: what may be mixed, records kept, and the manual copy folded with both folders, three steps and the guide");
   Check(UpdateGuides.CopyBack(null,app).OfType<GuideText>().First().Text=="복구에 쓸 백업 폴더를 찾을 수 없습니다."&&UpdateGuides.CopyBack(backup+"-없음",app).OfType<GuidePaths>().Any()==false,"when the backup folder is unknown or gone it says so instead of showing a path");
   // 프로그램 파일이 하나도 없는 백업(교체가 옛 파일을 옮기기 전에 멈춤)은 복사하라고 하지 않고, 설명서의 직접 업데이트 절차로 안내한다
   string empty=backup+"-빈";Directory.CreateDirectory(UpdateBackup.Program(empty));
   try
   {
    var none=UpdateGuides.CopyBack(empty,app);var stopped=UpdateGuides.Interrupted(empty,app);
    Check(!UpdateGuides.HasBackupFiles(empty)&&UpdateGuides.HasBackupFiles(backup)&&none.OfType<GuideText>().Select(text=>text.Text).SequenceEqual(new[]{"백업 폴더에 복구할 프로그램 파일이 없습니다.","기존 프로그램 폴더를 다른 곳에 복사해 두고, GitHub에서 받은 새 버전을 새 폴더에 풀어 주세요. 기록과 설정을 옮기는 방법은 ‘업데이트·복구 안내’에서 확인해 주세요."})
     &&!none.OfType<GuidePaths>().Any()&&none.OfType<GuideLink>().Single().Name=="업데이트·복구 안내"&&stopped.Parts.OfType<GuideFold>().Single().Title=="프로그램에 문제가 있을 때 · 직접 설치하는 방법",
     "a backup with no program files is not offered for copying; the manual update steps match the guide (new folder, then move the records)");
    Check(UpdateGuides.SwapBroken(new SwapResult(false,false,"a","b"),empty,app).Plain().Contains("아래 안내대로 새 버전을 직접 설치해 주세요.")&&UpdateGuides.RestoreNotStarted(new Exception("x"),empty,app).Parts.OfType<GuideFold>().Last().Title=="직접 설치하는 방법"
     &&UpdateGuides.SwapBroken(new SwapResult(false,false,"a","b"),backup,app).Plain().Contains("아래 백업에서 프로그램 파일을 직접 복구해 주세요."),"the outer sentence and the fold follow the same rule");
   }
   finally{try{Directory.Delete(empty,true);}catch(IOException){}catch(UnauthorizedAccessException){}}
  }
  finally{try{Directory.Delete(backup,true);}catch(IOException){}catch(UnauthorizedAccessException){}}

  // 6. 모든 안내의 첫 줄이 안내창의 너비 안에 한 줄로 들어간다(첫 줄은 줄을 바꾸지 않는다)
  var all=new List<Guide>{UpdateGuides.Latest(now),failed,package,UpdateGuides.Cancelled(now),UpdateGuides.NoBackup,ask,UpdateGuides.Interrupted(null,app),UpdateGuides.RestoreNotStarted(new Exception("x"),app,app)};
  all.AddRange(heads.Keys.SelectMany(why=>new[]{UpdateGuides.Busy(why,false),UpdateGuides.Busy(why,true)}));all.AddRange(cases.Select(item=>UpdateGuides.Failed(item.Error,now)));
  int widest=all.Max(guide=>new SectionHeader(guide.Headline){Rule=false}.TextWidth);
  Check(widest<=GuideDialog.ContentWidth,$"every first line fits on one line of the guide window (widest {widest} of {GuideDialog.ContentWidth})");

  // 7. 새 버전 안내: '업데이트'는 강조색, 처음 초점과 연 직후의 Enter, Esc·닫기는 '나중에'. 패치노트는 짧으면 내용 높이, 길면 칸 안에서 굴린다
  var offerRelease=new ReleaseInfo(new Version(1,2,2),"## 기록 히스토리\n곡과 패턴별로 저장한 점수를 **확인**할 수 있습니다.\n\n- 첫째\n* 둘째",UpdateSource.Official.PagePrefix+"tag/v1.2.2","x.zip",UpdateSource.Official.DownloadPrefix+"x.zip",134_217_728,new string('a',64));
  Check(UpdateDialog.NoteLines(offerRelease.Notes).SequenceEqual(new[]{("기록 히스토리",true,false),("곡과 패턴별로 저장한 점수를 확인할 수 있습니다.",false,false),("· 첫째",false,true),("· 둘째",false,false)})&&UpdateDialog.NoteLines(" \n").Single().Text=="(패치노트가 비어 있습니다)","release notes: headings bold, bullets dotted, ** removed, blank lines kept as gaps");
  int shortHeight,longHeight;Rectangle shortButtons,longButtons;
  using(var offer=new UpdateDialog(offerRelease,UpdateSource.Official))
  {
   offer.Show();Application.DoEvents();var buttons=All(offer).OfType<Button>().ToList();var update=buttons.Single(b=>b.Text=="업데이트");var later=buttons.Single(b=>b.Text=="나중에");
   Check(update.Tag as string=="primary"&&later.Tag as string!="primary"&&offer.AcceptButton==later&&offer.CancelButton==later&&offer.ActiveControl==later,"offer: '업데이트' is the filled button, '나중에' has the first focus and answers Enter, Esc and closing");
   Check(All(offer).OfType<LinkLabel>().Single().Text=="릴리즈 페이지 열기"&&All(offer).Any(c=>c.Text=="v1.2.2")&&All(offer).Any(c=>c.Text=="128 MB")&&All(offer).Any(c=>c.Text=="패치노트")&&!offer.NotesArea.Scrolling,"offer: the release page link, the new version, the size; short notes are not scrolled");
   shortHeight=offer.Height;shortButtons=offer.RectangleToClient(update.RectangleToScreen(update.ClientRectangle));
   offer.Close();Check(offer.DialogResult!=DialogResult.Yes,"closing the offer does not start the update");
  }
  using(var offer=new UpdateDialog(offerRelease with{Notes=string.Join("\n",Enumerable.Range(1,60).Select(i=>$"- 고친 것 {i}"))},UpdateSource.Official))
  {
   offer.Show();Application.DoEvents();var update=All(offer).OfType<Button>().Single(b=>b.Text=="업데이트");
   longHeight=offer.Height;var area=offer.NotesArea;
   Check(area.Scrolling&&area.Height<=Theme.S(UpdateDialog.NotesMax)&&!area.HorizontalScroll.Visible,"long notes scroll inside their box, up and down only");
   longButtons=offer.RectangleToClient(update.RectangleToScreen(update.ClientRectangle));
   area.AutoScrollPosition=new Point(0,100000);Application.DoEvents();var atEnd=offer.RectangleToClient(update.RectangleToScreen(update.ClientRectangle));
   Check(atEnd==longButtons&&longHeight>shortHeight,"the buttons stay where they are at the first and the last line of the notes");
   var small=new Rectangle(0,0,1200,Math.Max(Theme.S(360),offer.Height-Theme.S(120)));offer.FitNotes(small);offer.PerformLayout();Application.DoEvents();
   Check(offer.Height<=small.Height&&area.Scrolling&&area.Height<longButtons.Height+Theme.S(UpdateDialog.NotesMax),$"on a small work area the notes box shrinks so the whole window fits ({offer.Height} <= {small.Height})");
   offer.Close();
  }
  // 8. 진행 창: 처음에는 어느 단추에도 초점이 없다. 다운로드에만 용량과 막대가 보이고 높이는 단계가 바뀌어도 그대로다. Esc·닫기는 취소 요청이고, 잠그면 취소 단추 대신 기다려 달라는 글
  using(var gate=new ManualResetEventSlim())
  using(var progress=new UpdateProgressDialog(new Version(1,2,1),new Version(1,2,2)))
  {
   progress.Work=async(dialog,cancel)=>{await Task.Run(()=>WaitHandle.WaitAny(new[]{gate.WaitHandle,cancel.WaitHandle}));cancel.ThrowIfCancellationRequested();};
   progress.Show();Application.DoEvents();
   Check(progress.ActiveControl==null&&!progress.CancelButtonForChecks.Focused&&progress.AcceptButton==null,"progress: nothing has the first focus, so Enter does not cancel");
   progress.Report(UpdatePrepare.Download,"48.0 / 128.0 MB",.375);Application.DoEvents();int first=progress.Height;
   Check(progress.Stages.StateOf(0)==StageState.Now&&progress.Stages.StateOf(1)==StageState.Later&&progress.Detail.Text==UpdateProgressDialog.Downloading&&progress.Amount.Text=="48.0 / 128.0 MB"&&!progress.Bar.Hidden&&progress.Bar.Value==.375,"download: the stage is marked, the amount and the bar show");
   progress.Report(UpdatePrepare.Backup,"기록과 설정을 백업하는 중",null);Application.DoEvents();
   Check(progress.Stages.StateOf(0)==StageState.Done&&progress.Stages.StateOf(1)==StageState.Done&&progress.Stages.StateOf(2)==StageState.Now&&progress.Detail.Text=="기록과 설정을 백업하는 중"&&progress.Amount.Text==""&&progress.Bar.Hidden&&progress.Height==first,"backup: earlier stages ticked, the work in words, no amount or bar, and the same height");
   Check(All(progress).OfType<StageRow>().Select(row=>row.Text).SequenceEqual(UpdatePrepare.Stages)&&All(progress).Any(c=>c.Text=="업데이트 진행"),"every stage name and the first line are window text (the update test reads them)");
   Check(progress.LockCancel()&&progress.CancelLocked,"the cancel can be locked before the swap");
   progress.Report(UpdatePrepare.Replace,"새 버전 설치를 시작하는 중",null);Application.DoEvents();
   var note=progress.WaitNote;Check(note.Visible&&note.Parent!.GetChildAtPoint(new Point(note.Left+note.Width/2,note.Top+note.Height/2))==note&&!progress.CancelButtonForChecks.Visible&&progress.Height==first,"once locked the cancel button is gone and the wait note is in its place, same height");
   progress.Close();Application.DoEvents();Check(progress.Visible,"once locked closing does nothing");
   gate.Set();for(int i=0;i<200&&progress.Visible;i++){Application.DoEvents();Thread.Sleep(10);}
   Check(!progress.Visible&&progress.Completed,"the window closes itself when the work ends");
  }
  using(var progress=new UpdateProgressDialog(new Version(1,2,1),new Version(1,2,2)))
  {
   progress.Work=async(dialog,cancel)=>{await Task.Delay(Timeout.Infinite,cancel);};
   progress.Show();Application.DoEvents();
   progress.Report(UpdatePrepare.Download,"1.0 / 2.0 MB",.5);
   var esc=new KeyEventArgs(Keys.Escape);typeof(Form).GetMethod("OnKeyDown",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(progress,new object[]{esc});
   for(int i=0;i<200&&progress.Visible;i++){Application.DoEvents();Thread.Sleep(10);}
   Check(!progress.Visible&&progress.Cancelled&&progress.Failure==null,"Esc asks to cancel and the window ends cancelled");
  }
  using(var progress=new UpdateProgressDialog(new Version(1,2,1),new Version(1,2,2)))
  {
   progress.Work=(dialog,cancel)=>throw new UpdateException("받은 파일의 크기가 릴리즈에 적힌 것과 다릅니다.",UpdateTrouble.BadFile);
   progress.Show();for(int i=0;i<200&&progress.Visible;i++){Application.DoEvents();Thread.Sleep(10);}
   Check(progress.Failure is UpdateException{Kind:UpdateTrouble.BadFile},"a failure keeps its kind for the window that explains it");
  }
  Swapper(Check,Strong);
  File.WriteAllLines(Path.Combine(AppContext.BaseDirectory,"update-guide-validation.txt"),notes);
 }

 // 교체·복구를 맡은 프로그램이 띄우는 것
 static void Swapper(Action<bool,string> Check,Func<Guide,string> Strong)
 {
  string app="D:\\DJMAXLocalRecords";
  string backup=Path.Combine(AppContext.BaseDirectory,"test-data","update-guide-swap-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(UpdateBackup.Program(backup));File.WriteAllText(Path.Combine(UpdateBackup.Program(backup),UpdateManifest.Executable),"exe");
  string exe=Path.Combine(UpdateBackup.Program(backup),UpdateManifest.Executable);
  try
  {
   // 파일을 바꾸기 전에 멈춤: 아무것도 바뀌지 않았다. 준비 정보·백업 정보가 맞지 않을 때만 '확인' 뒤 이전 버전을 다시 켠다
   foreach(var why in Enum.GetValues<UpdateGuides.SwapStop>())
   {
    var guide=UpdateGuides.SwapNotStarted(why,"1.2.0");bool restart=why is UpdateGuides.SwapStop.Mismatch or UpdateGuides.SwapStop.NoBackupInfo;
    Check(guide.Title=="업데이트 실패"&&guide.Parts[0]==new GuideText("현재 버전(v1.2.0)을 계속 사용할 수 있습니다.","strong")&&guide.Parts[1]==new GuideText(UpdateGuides.Kept,"dim")
     &&Strong(guide).Contains("‘확인’을 누르면 현재 버전을 다시 실행합니다.")==restart&&!guide.Parts.OfType<GuideFold>().Any(),$"swap not started ({why}): this version still works, records kept, and only a mismatch or a missing backup restarts the previous version");
   }
   Check(UpdateGuides.SwapNotStarted(UpdateGuides.SwapStop.OldStillRunning,null).Parts[0]==new GuideText("현재 버전을 계속 사용할 수 있습니다.","strong"),"without the state file it does not guess a version number");
   var clash=UpdateGuides.SwapCheckFailed(new UpdateException("…",UpdateTrouble.Clash,app,new[]{app+"\\a.txt"}),"1.2.0");
   Check(clash.Headline=="새 버전의 파일과 이름이 같은 항목이 있습니다"&&clash.Parts[^3]==new GuideText("‘확인’을 누르면 현재 버전을 다시 실행합니다.","strong")&&clash.Parts[^2] is GuideText{Tone:"strong"}&&clash.Parts[^1] is GuideFold,"a clash found just before the swap says the current version will start again, before the next step");
   var rolled=UpdateGuides.SwapRolledBack("파일을 쓰는 중입니다.","1.2.0");
   Check(rolled is {Title:"업데이트 실패",Headline:"새 버전의 파일을 설치하지 못했습니다"}&&Strong(rolled)=="이전 버전(v1.2.0)으로 복구했습니다.\n‘확인’을 누르면 이전 버전을 다시 실행합니다."&&rolled.Parts.OfType<GuideFold>().Single().Inside.Single()==new GuideText("파일을 쓰는 중입니다."),
    "swap failed and put back: which version it is again, that '확인' starts it, the error folded");
   var swap=new SwapResult(false,false,"들여온 파일이 다릅니다","a.dll: 접근 거부");
   var broken=UpdateGuides.SwapBroken(swap,backup,app);var errors=broken.Parts.OfType<GuideFold>().Single();
   Check(broken.Headline=="업데이트를 마치지 못했습니다"&&broken.Plain().Contains(UpdateGuides.Mixed)&&broken.Parts.OfType<GuidePaths>().Single().Items[0]==("복사할 백업 폴더",UpdateBackup.Program(backup))
    &&errors.Inside.SequenceEqual(new GuidePart[]{new GuideText("파일 교체 중 오류\n들여온 파일이 다릅니다"),new GuideText("복구 중 오류\na.dll: 접근 거부")}),"swap and put-back both failed: files may be mixed, the manual copy is shown, both errors are named");
   var crashed=UpdateGuides.InstallCrashed(new Exception("boom"),null,app);
   Check(crashed.Plain().Contains("프로그램 파일의 상태를 확인할 수 없습니다")&&crashed.Plain().Contains("복구에 쓸 백업 폴더를 찾을 수 없습니다.")&&crashed.Parts[^1] is GuideFold{Title:"오류 내용"},"an unexpected install error does not name a working version and says when the backup is unknown");
   // 새 버전이 뜨지 않을 때의 선택: 기본은 오른쪽('새 버전 유지' 또는 '더 기다리기'), 복구는 눌러야만 시작한다
   var dead=UpdateGuides.StartFailed(false,"1.2.0","1.2.1",backup);var alive=UpdateGuides.StartFailed(true,"1.2.0","1.2.1",backup);
   Check(dead is {Title:"업데이트",Headline:"이전 버전으로 복구할까요?",Default:1}&&dead.ButtonTexts.SequenceEqual(new[]{"이전 버전으로 복구","새 버전 유지"})&&alive.ButtonTexts.SequenceEqual(new[]{"이전 버전으로 복구","더 기다리기"})&&alive.Default==1,
    "start failure: '새 버전 유지' or '더 기다리기' is the default on the right; restoring needs the left button");
   Check(dead.Parts.OfType<GuideVersions>().Single()==new GuideVersions("설치된 새 버전","v1.2.1","복구할 버전","v1.2.0")&&Strong(dead).StartsWith("새 버전이 정상적으로 시작되지 않았습니다.")&&Strong(alive).StartsWith("새 버전이 2분 동안 시작을 마치지 못했습니다.")
    &&dead.Parts.OfType<GuideText>().Any(text=>text.Tone=="dim"&&text.Text==UpdateGuides.RecordsAfterRestore("1.2.0")),"start failure: both versions, why, and what happens to the records");
   var later=dead.Parts.OfType<GuideFold>().Single();
   Check(later.Title=="나중에 이전 버전으로 복구하는 방법"&&later.Inside.OfType<GuidePaths>().Single().Items.Single()==("복구를 시작할 실행 파일",exe)&&!alive.Parts.OfType<GuideFold>().Any()
    &&later.Inside.OfType<GuideText>().First().Text=="아래 실행 파일을 실행한 뒤 이전 버전 복구를 승인해 주세요."&&!dead.Plain().Contains("‘복구’를")&&!UpdateGuides.RollbackBlocked(backup).Plain().Contains("‘복구’를")&&!UpdateGuides.RollbackKeptNew("x","1.2.1",backup).Plain().Contains("‘복구’를")
    &&UpdateGuides.StartFailed(false,"1.1.0","1.2.0",backup,false).Plain().Contains(UpdateGuides.OldPlace)&&!UpdateGuides.StartFailed(false,"1.1.0","1.2.0",backup,false).Plain().Contains("따로 보관"),"keeping the new version folds how to restore later with the backup's program; waiting has no such fold");
   Check(UpdateGuides.RolledBack("1.1.0") is {Title:"업데이트",Headline:"이전 버전으로 복구했습니다"}&&Strong(UpdateGuides.RolledBack("1.1.0"))=="‘확인’을 누르면 이전 버전(v1.1.0)을 실행합니다."&&UpdateGuides.RolledBack("1.1.0").Plain().Contains("실행 파일 옆의 기록과 설정"),"restored during an update: once, then '확인' starts it");
   var blocked=UpdateGuides.RollbackBlocked(backup);var kept=UpdateGuides.RollbackKeptNew("x","1.2.1",backup);var mixed=UpdateGuides.RollbackBroken(swap,backup,app);
   Check(new[]{blocked,kept,mixed}.All(g=>g.Title=="업데이트 실패"&&g.Headline=="이전 버전으로 복구하지 못했습니다")&&!blocked.Parts.OfType<GuideFold>().Any()&&kept.Parts.OfType<GuideFold>().Any()
    &&blocked.Parts.OfType<GuidePaths>().Single().Items.Single().Path==exe&&Strong(kept).StartsWith("새 버전(v1.2.1)의 프로그램 파일이 그대로 남아 있습니다.")&&mixed.Parts.OfType<GuidePaths>().Single().Items[0].Path==UpdateBackup.Program(backup),
    "restore during update failed: running → the backup's program; new files kept → the same plus the error; mixed → the manual copy");
   Check(UpdateGuides.DirectRun("이전 버전 복구",app+"\\").Parts.OfType<GuidePaths>().Single().Items.Single()==("실행할 파일",app+"\\DJMAXLocalRecords.exe"),"restored but not started: the full path of the program to run");
   var reopen=UpdateGuides.OpenCurrent(app);
   Check(reopen is {Title:"업데이트",Headline:"프로그램을 다시 실행하지 못했습니다"}&&Strong(reopen)=="현재 버전은 그대로입니다."&&reopen.Parts.OfType<GuidePaths>().Single().Items.Single()==("실행할 파일",app+"\\DJMAXLocalRecords.exe"),"a promised restart that fails names the program to open");
   // 백업의 실행 파일
   Check(UpdateGuides.BackupWhileRunning is {Title:"이전 버전 복구",Headline:"프로그램이 실행 중입니다."}&&UpdateGuides.BackupWhileRunning.Plain().Contains("메인 창에 ‘이전 버전 복구’가 보이면 거기서도 복구할 수 있습니다."),"the backup opened while the program runs");
   var direct=UpdateGuides.RestoreAskDirect("v1.2.1","1.2.0","2026-11-21 18:34:09");
   Check(direct is {Headline:"이 백업으로 이전 버전을 복구할까요?",Default:1}&&direct.ButtonTexts.SequenceEqual(new[]{"복구","취소"})&&direct.Parts.OfType<GuideVersions>().Single()==new GuideVersions("현재 버전","v1.2.1","복구할 버전","v1.2.0","백업 시각","2026-11-21 18:34:09")
    &&Strong(direct)==UpdateGuides.RecordsAfterRestore("1.2.0")&&direct.Plain().EndsWith("프로그램 파일을 복구한 뒤 이전 버전을 실행합니다."),"the backup opened directly asks first, '취소' is the default, and the records line is the one for the version being restored");
   Check(UpdateGuides.RestoreAskDirect("알 수 없음","","알 수 없음").Parts.OfType<GuideVersions>().Single().To=="알 수 없음","a backup without its info still asks, naming the version '알 수 없음'");
   var running=UpdateGuides.RestoreFailed(UpdateGuides.RestoreStop.Running,backup,app,running:true);
   var clashed=UpdateGuides.RestoreFailed(UpdateGuides.RestoreStop.Clash,backup,app,clash:new[]{app+"\\overlay.html"});
   var inUse=UpdateGuides.RestoreFailed(UpdateGuides.RestoreStop.Restored,backup,app,swap:new SwapResult(false,true,"사용 중"){InUse=true});
   var other=UpdateGuides.RestoreFailed(UpdateGuides.RestoreStop.Restored,backup,app,swap:new SwapResult(false,true,"다른 오류"));
   Check(running.Plain().Contains("켜져 있는 프로그램은 그대로 사용할 수 있습니다.")&&clashed.Plain().Contains("현재 버전을 사용하려면 프로그램 폴더의 DJMAXLocalRecords.exe를 실행해 주세요.")&&clashed.Parts.OfType<GuideFold>().Single().Title=="직접 복구하는 방법"
    &&clashed.Parts.OfType<GuidePaths>().Single().Items.Single()==("이름이 겹치는 항목",app+"\\overlay.html"),"a restore stopped before changing files says which program to use now and shows the clashing items");
   Check(Strong(inUse).Contains("파일을 사용 중인 프로그램을 닫은 뒤")&&!Strong(other).Contains("사용 중인")&&Strong(other).Contains("잠시 뒤에 ‘이전 버전 복구’를 다시 시도해 주세요."),"only a sharing violation is called 'in use'");
   var restoreBroken=UpdateGuides.RestoreFailed(UpdateGuides.RestoreStop.Broken,backup,app,swap:swap);var restoreCrashed=UpdateGuides.RestoreFailed(UpdateGuides.RestoreStop.Crashed,backup,app,error:new Exception("boom"));
   Check(restoreBroken.Parts.OfType<GuideFold>().Single().Inside.Length==2&&restoreBroken.Parts.OfType<GuideSteps>().Any()&&restoreCrashed.Plain().Contains("상태를 확인할 수 없습니다")&&restoreCrashed.Parts.OfType<GuideFold>().Single().Inside.Single()==new GuideText("boom"),"a broken or crashed restore shows the manual copy and its errors");
   Check(UpdateGuides.StagingOpened is {Title:"업데이트",Headline:"업데이트용 임시 파일을 열었습니다."}&&UpdateGuides.StagingOpened.Parts.OfType<GuidePaths>().Single().Items.Single()==("","DJMAXLocalRecords.exe"),"the staging program opened directly");
   var all=new[]{dead,alive,rolled,broken,crashed,clash,blocked,kept,mixed,direct,running,clashed,inUse,restoreBroken,restoreCrashed,UpdateGuides.StagingOpened,UpdateGuides.BackupWhileRunning,UpdateGuides.RolledBack("1.1.0"),UpdateGuides.DirectRun("업데이트",app)}
    .Concat(Enum.GetValues<UpdateGuides.SwapStop>().Select(why=>UpdateGuides.SwapNotStarted(why,"1.2.0")));
   int widest=all.Max(guide=>new SectionHeader(guide.Headline){Rule=false}.TextWidth);
   Check(widest<=GuideDialog.ContentWidth,$"every first line of the swap and restore guides fits on one line ({widest} of {GuideDialog.ContentWidth})");
  }
  finally{try{Directory.Delete(backup,true);}catch(IOException){}catch(UnauthorizedAccessException){}}

  // 진행 창: 닫을 수 없고 닫기 단추도 없다. 높이는 상세 글이 두 줄이 되어도 그대로다. 업데이트 도중의 복구로 바뀌면 첫 줄·버전·'복구 중'·아래 줄이 바뀐다
  using(var gate=new ManualResetEventSlim())
  using(var install=new InstallWindow("업데이트",UpdateGuides.InstallHead,"v1.2.0","v1.2.1",UpdatePrepare.Stages,UpdatePrepare.Replace,UpdateGuides.InstallWait))
  {
   int answered=-1;
   install.Work=w=>{w.Stage(UpdatePrepare.Replace,UpdateGuides.WaitingOld);gate.Wait();};
   install.Show();for(int i=0;i<50;i++){Application.DoEvents();Thread.Sleep(5);}
   int first=install.Height;
   Check(!install.ControlBox&&!install.Plain&&install.Headline=="업데이트 진행"&&install.Rows!.StateOf(2)==StageState.Done&&install.Rows.StateOf(3)==StageState.Now&&install.WaitNote.Text=="업데이트가 끝날 때까지 기다려 주세요."&&!All(install).OfType<Button>().Any(),
    "install: no close button and no buttons, the swap stage marked, the wait line at the bottom");
   install.Close();Application.DoEvents();Check(install.Visible,"install: it cannot be closed while working");
   install.Restoring("v1.2.1","v1.2.0");install.Stage(UpdatePrepare.Replace,UpdateGuides.RollingBack);Application.DoEvents();
   Check(install.Headline=="이전 버전 복구"&&install.Rows!.NowText=="복구 중"&&install.WaitNote.Text=="복구가 끝날 때까지 기다려 주세요."&&All(install).Any(c=>c.Text=="v1.2.1")&&install.Detail.Text==UpdateGuides.RollingBack&&install.Height==first,
    "restoring during an update: first line, version direction, '복구 중', the wait line and the same height");
   try{GuideDialog.Hook=guide=>{answered=guide.Default;return guide.Default;};Check(install.Show(UpdateGuides.StartFailed(false,"1.2.0","1.2.1",AppContext.BaseDirectory))==1&&answered==1,"a guide over the install window answers with its default when closed");}
   finally{GuideDialog.Hook=null;}
   gate.Set();for(int i=0;i<200&&install.Visible;i++){Application.DoEvents();Thread.Sleep(10);}
   Check(!install.Visible,"install: it closes itself when the work ends");
  }
  using(var plain=new InstallWindow("이전 버전 복구",UpdateGuides.RestoreHead,"v1.2.1","v1.2.0",new[]{"교체","재실행"},0,UpdateGuides.RestoreWait,plain:true))
  {
   plain.Work=w=>w.Stage(1,UpdateGuides.RestoreStarting);plain.Show();for(int i=0;i<200&&plain.Visible;i++){Application.DoEvents();Thread.Sleep(10);}
   Check(plain.Plain&&!plain.ControlBox&&!plain.Visible,"the plain fallback window works the same way (it is what is shown when the new one cannot be made)");
  }
 }
}
