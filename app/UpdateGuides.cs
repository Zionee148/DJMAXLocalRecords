namespace LadderRecords;

// 업데이트·복구 안내의 글. 켜져 있는 프로그램이 띄우는 것과 교체·복구를 맡은 프로그램이 띄우는 것(아래 "교체·복구를 맡은 프로그램"). 창을 띄우는 자리(MainForm.Update.cs)와 떼어 두어 자체 검사가 표째로 본다.

/// <summary>업데이트·복구를 지금 시작할 수 없는 까닭. 여럿이 걸리면 이 차례로 먼저 걸리는 것 하나를 알린다</summary>
enum UpdateBusy{Scoring,Pending,Detecting,Importing,OtherWindow}

static class UpdateGuides
{
 public const string UpdateTitle="업데이트",FailedTitle="업데이트 실패",RestoreTitle="이전 버전 복구";
 /// <summary>보존 안내(옅게. 업데이트·복구가 멈춘 안내마다 넣는다)</summary>
 public const string Kept="플레이 기록과 설정은 그대로입니다.";
 /// <summary>설명서 주소 위의 이름</summary>
 public const string GuideName="업데이트·복구 안내";
 public const string ErrorFold="오류 내용";
 /// <summary>기록을 'UserData' 폴더에서 읽는 첫 버전. 이보다 낮은 버전은 실행 파일 옆의 기록을 쓴다</summary>
 public static readonly Version FolderSince=new(1,2,0);

 static string V(Version version)=>AppVersion.Text(version);
 static GuideText Usable(Version now)=>Usable(V(now));
 static GuideText Usable(string? now)=>new(now==null?"현재 버전을 계속 사용할 수 있습니다.":$"현재 버전({now})을 계속 사용할 수 있습니다.","strong");
 /// <summary>상태 파일 등에 적힌 점 버전("1.2.0")을 화면의 글("v1.2.0")로</summary>
 static string? Vp(string? plain)=>string.IsNullOrWhiteSpace(plain)?null:"v"+plain;
 /// <summary>예외의 글(비어 있으면 알 수 없는 오류라고 적는다. 예외 형의 이름은 보이지 않는다)</summary>
 static string Message(Exception error)=>string.IsNullOrWhiteSpace(error.Message)?"알 수 없는 오류입니다.":error.Message;

 // ── 업데이트 확인의 짧은 결과 셋(프로그램의 버전은 다른 안내와 같이 '현재 버전'이라 적는다)

 public static Guide Latest(Version now)=>new(UpdateTitle,$"현재 버전({V(now)})이 최신 버전입니다.",Array.Empty<GuidePart>());
 /// <summary>조회 실패. detail 은 받은 예외의 글이다: 본문에 두지 않고 펼치는 '오류 내용'에 둔다. 첫 줄은 실패 안내라 마침표가 없다</summary>
 public static Guide CheckFailed(string reason,string? detail=null)
 {
  var parts=new List<GuidePart>{new GuideText(reason),new GuideText("현재 버전을 계속 사용할 수 있습니다.","dim")};
  if(!string.IsNullOrWhiteSpace(detail))parts.Add(new GuideFold(ErrorFold,new GuideText(detail)));
  return new(UpdateTitle,"새 버전이 있는지 확인하지 못했습니다",parts);
 }
 /// <summary>자동 업데이트용 파일을 쓸 수 없는 릴리즈. 단추: 0 '릴리즈 페이지 열기'(릴리즈 페이지를 연다), 1 '닫기'(기본)</summary>
 public static Guide NoPackage(Version version,string reason)=>new(UpdateTitle,"자동으로 업데이트할 수 없습니다.",new GuidePart[]
 {
  new GuideText($"새 버전({V(version)})은 릴리즈 페이지에서 직접 받아 설치해야 합니다.","strong"),
  new GuideText(reason,"dim"),
 },new[]{"릴리즈 페이지 열기","닫기"},1);

 // ── 작업 중 안내(다른 일을 하고 있어 업데이트·복구를 시작할 수 없다. 첫 줄은 하고 있는 일, 둘째 줄은 그 일을 마친 뒤 다시 시도하라는 안내)

 public static Guide Busy(UpdateBusy why,bool restore)
 {
  string what=restore?"복구를":"업데이트를";
  var(head,next)=why switch
  {
   UpdateBusy.Scoring=>("등록할 점수를 확인하고 있습니다.",$"점수 확인이 끝나고 저장 확인이 뜨면 기록을 저장하거나 취소한 뒤 {what} 다시 시도해 주세요."),
   UpdateBusy.Pending=>("저장 확인을 기다리고 있습니다.",$"기록을 저장하거나 취소한 뒤 {what} 다시 시도해 주세요."),
   UpdateBusy.Detecting=>("게임 화면을 감지하고 있습니다.",$"‘중지 / 숨기기’로 감지를 멈춘 뒤 {what} 다시 시도해 주세요."),
   UpdateBusy.Importing=>("V아카이브 기록을 가져오고 있습니다.",$"가져오기가 끝나면 {what} 다시 시도해 주세요."),
   _=>("다른 창이 열려 있습니다.",$"열려 있는 창(기록 목록, 설정 등)에서 작업을 마치고 닫은 뒤 {what} 다시 시도해 주세요."),
  };
  return new(restore?RestoreTitle:UpdateTitle,head,new GuidePart[]{new GuideText(next)});
 }

 // ── 업데이트를 준비하다 멈췄을 때(교체 전. 다운로드·파일 확인·백업 실패와 이름 충돌·설치 파일 목록 확인 실패)

 // 첫 줄의 마침표: 실패·완료 안내(이 취소도 완료다)의 첫 줄에는 붙이지 않고, 알림과 질문에는 붙인다
 public static Guide Cancelled(Version now)=>new(UpdateTitle,"업데이트를 취소했습니다",new GuidePart[]{Usable(now),new GuideText(Kept,"dim")});

 /// <summary>까닭의 종류마다 첫 줄</summary>
 public static string Headline(UpdateTrouble kind)=>kind switch
 {
  UpdateTrouble.Network=>"업데이트 파일을 다 받지 못했습니다",
  UpdateTrouble.Disk=>"받은 업데이트 파일을 저장하지 못했습니다",
  UpdateTrouble.BadFile=>"받은 업데이트 파일이 올바르지 않습니다",
  UpdateTrouble.NoSpace=>"디스크 공간이 부족합니다",
  UpdateTrouble.Locked=>"프로그램 폴더에 쓸 수 없습니다",
  UpdateTrouble.Clash=>"새 버전의 파일과 이름이 같은 항목이 있습니다",
  UpdateTrouble.NoList=>"설치된 프로그램의 파일 목록을 읽지 못했습니다",
  UpdateTrouble.Backup=>"기록과 설정을 백업하지 못했습니다",
  UpdateTrouble.HandOver=>"새 버전의 설치를 시작하지 못했습니다",
  _=>"업데이트를 준비하지 못했습니다",
 };
 public const string Retry="잠시 뒤에 ‘업데이트 확인’을 눌러 다시 시도해 주세요.";

 /// <summary>교체 전에 멈춘 업데이트의 안내: 막힌 일 → 현재 버전 → 보존 → (경로) → 다음 행동 → 펼치는 '오류 내용'(받은 글 그대로)</summary>
 public static Guide Failed(Exception error,Version now)=>Failed(error,V(now));
 static Guide Failed(Exception error,string? now)
 {
  var trouble=error as UpdateException;var kind=trouble?.Kind??UpdateTrouble.Unknown;
  var parts=new List<GuidePart>{Usable(now),new GuideText(Kept,"dim")};
  switch(kind)
  {
   case UpdateTrouble.Network:parts.Add(new GuideText("인터넷 연결을 확인한 뒤 ‘업데이트 확인’을 눌러 다시 시도해 주세요.","strong"));break;
   // 받은 파일을 디스크에 쓰지 못한 것을 인터넷 문제로 안내하지 않는다
   case UpdateTrouble.Disk:parts.Add(new GuideText("디스크의 빈 공간이 충분한지, 프로그램 폴더 안의 update 폴더를 다른 프로그램(백신, 동기화 프로그램 등)이 쓰고 있지 않은지 확인한 뒤 ‘업데이트 확인’을 눌러 다시 시도해 주세요.","strong"));break;
   case UpdateTrouble.NoSpace:
    parts.Add(new GuideText(trouble!.Needed>0?$"프로그램 폴더가 있는 디스크의 빈 공간을 {trouble.Needed/1048576.0:N0} MB 이상 확보한 뒤 ‘업데이트 확인’을 눌러 다시 시도해 주세요.":"프로그램 폴더가 있는 디스크의 빈 공간을 확보한 뒤 ‘업데이트 확인’을 눌러 다시 시도해 주세요.","strong"));break;
   case UpdateTrouble.Locked:
    if(trouble!.Place!=null)parts.Add(new GuidePaths(("프로그램 폴더",trouble.Place)));
    parts.Add(new GuideText("프로그램을 종료하고 프로그램 폴더를 쓰기 권한이 있는 곳(예: 문서, 바탕 화면)으로 옮긴 뒤, 옮긴 폴더에서 다시 실행해 ‘업데이트 확인’을 눌러 주세요.","strong"));break;
   case UpdateTrouble.Clash:
    if(trouble!.Items.Count>0)parts.Add(new GuidePaths(("이름이 겹치는 항목",string.Join("\n",trouble.Items))));
    parts.Add(new GuideText("위 항목을 프로그램 폴더 밖으로 옮긴 뒤 ‘업데이트 확인’을 눌러 다시 시도해 주세요.","strong"));break;
   case UpdateTrouble.NoList:
    parts.Add(new GuideText("GitHub에서 새 버전을 직접 받아 설치해 주세요.","strong"));parts.Add(new GuideLink(GuideName,UpdateBackup.GuidePage));break;
   default:parts.Add(new GuideText(Retry,"strong"));break;
  }
  parts.Add(new GuideFold(ErrorFold,new GuideText(Message(error))));
  return new(FailedTitle,Headline(kind),parts);
 }

 /// <summary>
 /// 지난 업데이트가 프로그램 파일을 바꾸는 도중에 멈춰 있을 때. 상태 파일을 지우지 않으므로 켤 때마다 뜬다(다음 업데이트가 성공하거나 복구할 때까지).
 /// backupFolder 가 null 이면 백업의 자리를 모른다
 /// </summary>
 public static Guide Interrupted(string? backupFolder,string app)=>new(UpdateTitle,"지난 업데이트가 중단되었습니다",new GuidePart[]
 {
  new GuideText("파일 교체 도중 멈춰 두 버전의 프로그램 파일이 섞여 있을 수 있습니다.","strong"),
  new GuideText(Kept,"dim"),
  new GuideText("프로그램이 정상적으로 동작하면 메인 창에서 ‘업데이트 확인’을 눌러 다시 업데이트할 수 있습니다.","strong"),
  new GuideFold(HasBackupFiles(backupFolder)?"프로그램에 문제가 있을 때 · 백업으로 복구하는 방법":"프로그램에 문제가 있을 때 · 직접 설치하는 방법",CopyBack(backupFolder,app)),
 });

 /// <summary>
 /// 손으로 복사해 복구할 수 있는 백업인가: 백업의 program 폴더에 파일이 하나라도 있다.
 /// 교체는 백업 폴더를 먼저 만들고 상태를 '교체 중'으로 적은 뒤 옛 파일을 옮기므로, 그 사이에 멈추면 빈 폴더만 있다.
 /// 일부만 옮겨진 채 멈췄으면 그 파일들을 복사해 넣으면 옛 버전이 온전히 돌아온다(옮기지 않은 것은 프로그램 폴더에 그대로 있다): 복사 안내가 맞다
 /// </summary>
 public static bool HasBackupFiles(string? backupFolder)
 {
  if(backupFolder==null)return false;
  try{string program=UpdateBackup.Program(backupFolder);return Directory.Exists(program)&&Directory.EnumerateFiles(program,"*",SearchOption.AllDirectories).Any();}
  catch(Exception e)when(e is IOException or UnauthorizedAccessException){return false;}
 }
 /// <summary>직접 복구하라는 바깥 문장: 쓸 수 있는 백업이 있으면 백업에서, 없으면 새 버전을 직접 설치하라고(CopyBack 과 같이 HasBackupFiles 로 가른다)</summary>
 static string Recover(string? backupFolder,string prefix="")=>prefix+(HasBackupFiles(backupFolder)?"아래 백업에서 프로그램 파일을 직접 복구해 주세요.":"아래 안내대로 새 버전을 직접 설치해 주세요.");
 static string RecoverFold(string? backupFolder)=>HasBackupFiles(backupFolder)?"직접 복구하는 방법":"직접 설치하는 방법";

 /// <summary>
 /// 백업의 프로그램 파일을 손으로 복사해 되돌리는 방법: 복사할 백업 폴더와 붙여넣을 프로그램 폴더, 절차 셋, 안내 주소.
 /// 복구할 파일이 든 백업이 없으면 그 사실(백업 폴더가 없는지, 폴더에 파일이 없는지)을 적고 새 버전을 직접 설치하라고 안내한다
 /// </summary>
 public static GuidePart[] CopyBack(string? backupFolder,string app)
 {
  string? program=backupFolder==null?null:UpdateBackup.Program(backupFolder);
  var link=new GuideLink(GuideName,UpdateBackup.GuidePage);
  // 쓸 수 있는 백업이 없다: 그 사실과, 설명서의 직접 업데이트 절차(기존 폴더를 백업하고 새 폴더에 푼 뒤 기록을 옮긴다. guide\update.md 의 2절)와 같은 안내
  if(!HasBackupFiles(backupFolder))
   return new GuidePart[]
   {
    new GuideText(program!=null&&Directory.Exists(program)?"백업 폴더에 복구할 프로그램 파일이 없습니다.":"복구에 쓸 백업 폴더를 찾을 수 없습니다.","strong"),
    new GuideText("기존 프로그램 폴더를 다른 곳에 복사해 두고, GitHub에서 받은 새 버전을 새 폴더에 풀어 주세요. 기록과 설정을 옮기는 방법은 ‘업데이트·복구 안내’에서 확인해 주세요."),link,
   };
  return new GuidePart[]
  {
   new GuidePaths(("복사할 백업 폴더",program!),("붙여넣을 프로그램 폴더",app.TrimEnd('\\'))),
   new GuideSteps("디맥 개인 기록 저장 오버레이를 모두 종료합니다.","위에 표시된 백업 폴더 안의 파일과 폴더를 프로그램 폴더에 복사합니다. 이름이 같은 파일은 덮어씁니다.",$"프로그램 폴더에서 {UpdateManifest.Executable}를 실행합니다."),
   link,
  };
 }

 // ── 주 화면의 '이전 버전 복구'

 public static Guide NoBackup=>new(RestoreTitle,"이전 버전의 프로그램 백업을 찾을 수 없습니다.",new GuidePart[]{new GuideText("현재 버전을 계속 사용할 수 있습니다.")});

 /// <summary>복구될 버전이 실행 파일 옆의 옛 기록을 쓰는가(1.2.0 보다 낮은 버전). 버전을 읽지 못하면 null(모른다. 단정하지 않는다)</summary>
 public static bool? UsesOldPlace(string backupVersion)=>AppVersion.TryParsePlain(backupVersion,out var version)&&version>new Version(0,0,0)?version<FolderSince:null;
 public const string OldPlace="이전 버전은 실행 파일 옆의 기록과 설정을 사용합니다.";
 /// <summary>
 /// 복구될 버전이 읽을 기록을 알리는 줄. converted: 이 프로그램 폴더의 기록이 'UserData'에 자리 잡았는가(UserFiles.RecordsMoved. 설정만 옮긴 것은 아니다).
 /// 옛 자리를 쓰는 버전으로 돌아가는데 아직 옮기지 않았거나 옮기다 멈췄으면, 'UserData'에 보관된다거나 따로 보관된다고 적지 않는다(실제로 확인한 것만 적는다).
 /// 버전을 모르면 위치를 확인할 수 없다고 적는다
 /// </summary>
 public static string RecordsAfterRestore(string backupVersion,bool converted=true)=>UsesOldPlace(backupVersion) switch
 {
  true when converted=>OldPlace+"\n현재 기록과 설정은 ‘UserData’ 폴더에 보관되며, 다시 업데이트하면 이 폴더를 사용합니다.\n복구 후 이전 버전에서 저장한 기록은 따로 보관됩니다.",
  true=>OldPlace,
  false=>"이전 버전에서도 ‘UserData’ 폴더의 기록과 설정을 그대로 사용합니다.",
  null=>"이전 버전이 어느 위치의 기록과 설정을 사용하는지 확인할 수 없습니다. 복구는 기록과 설정 파일을 바꾸지 않습니다.",
 };

 /// <summary>복구 확인. 단추: 0 '복구', 1 '취소'(기본. 처음 초점이 '취소'라 연 직후의 Enter 와 Esc·닫기는 취소). moved: 기록이 'UserData'에 자리 잡았는가(UserFiles.RecordsMoved)</summary>
 public static Guide RestoreAsk(Version now,string backupVersion,string made,bool moved=true)=>new(RestoreTitle,"이전 버전으로 복구할까요?",new GuidePart[]
 {
  new GuideVersions("현재 버전",V(now),"복구할 버전","v"+backupVersion,"백업 시각",made),
  new GuideText(RecordsAfterRestore(backupVersion,moved),"strong"),
  new GuideText("복구하면 프로그램이 잠시 종료된 뒤 이전 버전으로 다시 실행됩니다.","dim"),
 },new[]{"복구","취소"},1);

 /// <summary>복구를 맡을 실행 파일(백업의 이전 버전)을 켜지 못했을 때. 아무것도 바뀌지 않았다(파일을 바꾸기 전에 복구가 멈춘 것과 같다)</summary>
 public static Guide RestoreNotStarted(Exception error,string backupFolder,string app)=>new(RestoreTitle,"이전 버전으로 복구하지 못했습니다",new GuidePart[]
 {
  new GuideText("복구를 시작하지 못했습니다. 현재 버전은 그대로입니다.","strong"),
  new GuideText(Kept,"dim"),
  new GuideText("잠시 뒤에 ‘이전 버전 복구’를 다시 시도해 주세요.","strong"),
  new GuideFold(ErrorFold,new GuideText(Message(error))),
  new GuideFold(RecoverFold(backupFolder),CopyBack(backupFolder,app)),
 });

 // ── 교체·복구를 맡은 프로그램이 띄우는 것.
 //  새 버전의 설치 과정(update\staging 의 새 버전)과 백업의 실행 파일(update\backup 의 이전 버전)이 그린다. 둘 다 주 화면이 없다.
 //  낱말: 프로그램 버전은 '이전 버전'과 '복구'로 적는다. 두 오류는 '파일 교체 중 오류'와 '복구 중 오류'
 //  (이전 버전 복구가 실패한 두 창은 '이전 버전 파일을 들여오던 중 오류'와 '교체 전 상태로 되돌리던 중 오류')

 // 진행 창의 글. 진행 글의 끝나다·켜다·뜨다는 종료·시작으로 적는다. 업데이트 도중의 복구는 아래 줄을 복구 진행 창과 같은 글로 적고, 상세에도 '복원' 대신 '복구'를 쓴다
 public const string InstallHead="업데이트 진행",RestoreHead="복구 진행",RollbackHead="이전 버전 복구";
 public const string InstallWait="업데이트가 끝날 때까지 기다려 주세요.",RestoreWait="복구가 끝날 때까지 기다려 주세요.",RollbackWait="복구가 끝날 때까지 기다려 주세요.";
 public const string WaitingOld="기존 프로그램이 종료되기를 기다리는 중",Swapping="프로그램 파일을 교체하는 중",StartingNew="새 버전을 시작하는 중",WaitingNew="새 버전이 시작되기를 기다리는 중",
  StoppingNew="실행 중인 새 버전을 종료하는 중",RollingBack="백업에서 이전 버전의 프로그램 파일을 복구하고 있습니다.";
 public const string RestoreWaitingExit="현재 프로그램이 종료되기를 기다리는 중입니다.",RestoreSwapping="프로그램 파일을 백업해 둔 버전으로 교체하고 있습니다.",RestoreStarting="이전 버전의 프로그램을 시작하고 있습니다.";
 public const string Mixed="두 버전의 프로그램 파일이 섞여 있을 수 있습니다.";

 /// <summary>'오류 내용' 안에 두 오류를 나눠 적는다. rollback: 이전 버전 복구가 실패한 창이다. 첫 오류가 복구 때의 것이라 이름을 바꾼다</summary>
 static GuideFold TwoErrors(string? swap,string? restore,bool rollback=false)=>new(ErrorFold,
  new GuideText((rollback?"이전 버전 파일을 들여오던 중 오류":"파일 교체 중 오류")+"\n"+(swap??"")),new GuideText((rollback?"교체 전 상태로 되돌리던 중 오류":"복구 중 오류")+"\n"+(restore??"")));
 /// <summary>백업의 실행 파일로 복구를 다시 시작하는 자리(경로 상자와 안내 주소)</summary>
 static GuidePart[] RestoreFrom(string backupFolder)=>new GuidePart[]{new GuidePaths(("복구를 시작할 실행 파일",Path.Combine(UpdateBackup.Program(backupFolder),UpdateManifest.Executable))),new GuideLink(GuideName,UpdateBackup.GuidePage)};

 /// <summary>새 버전의 설치 과정이 파일을 바꾸기 전에 멈춘 까닭</summary>
 public enum SwapStop{OldStillRunning,RunningAgain,Mismatch,NoBackupInfo}
 /// <summary>
 /// 새 버전의 설치 과정이 파일을 바꾸기 전에 멈췄다(아무것도 바뀌지 않았다). 기존 실행이 남았으면 프로그램을 닫고 다시 업데이트하도록,
 /// 준비 정보가 맞지 않거나 백업 정보를 찾지 못했으면 '확인' 뒤 이전 버전의 실행을 시도한다. from 은 상태 파일의 이전 버전(모르면 null)
 /// </summary>
 public static Guide SwapNotStarted(SwapStop why,string? from)
 {
  bool restart=why is SwapStop.Mismatch or SwapStop.NoBackupInfo;
  string head=why switch{SwapStop.OldStillRunning=>"기존 프로그램이 종료되지 않았습니다",SwapStop.RunningAgain=>"프로그램이 실행 중이어서 파일을 교체하지 않았습니다",SwapStop.Mismatch=>"업데이트 준비 정보가 맞지 않습니다",_=>"기록과 설정의 백업을 찾지 못했습니다"};
  string next=restart?"‘확인’을 누르면 현재 버전을 다시 실행합니다. 그 뒤 ‘업데이트 확인’을 눌러 다시 시도해 주세요.":"프로그램을 모두 종료한 뒤 다시 실행해 ‘업데이트 확인’을 눌러 주세요.";
  return new(FailedTitle,head,new GuidePart[]{Usable(Vp(from)),new GuideText(Kept,"dim"),new GuideText(next,"strong")});
 }
 /// <summary>교체 직전에 다시 본 자리가 맞지 않다(이름 충돌, 지금 설치의 목록 없음). 켜져 있는 프로그램이 띄우는 같은 안내(Failed)에 '확인' 뒤 이전 버전을 다시 켠다는 줄을 더한다</summary>
 public static Guide SwapCheckFailed(Exception error,string? from)
 {
  var guide=Failed(error,Vp(from));var parts=guide.Parts.ToList();
  // 파일을 바꾸기 전이라 다시 켜는 것은 현재 버전이다. 다음 행동(‘업데이트 확인’)보다 앞에 둔다
  int next=parts.FindLastIndex(p=>p is GuideText{Tone:"strong"});
  parts.Insert(next>0?next:parts.Count-1,new GuideText("‘확인’을 누르면 현재 버전을 다시 실행합니다.","strong"));
  return guide with{Parts=parts};
 }
 /// <summary>파일을 바꾸다 실패했고 이전 버전으로 복구했다</summary>
 public static Guide SwapRolledBack(string? error,string? from)=>new(FailedTitle,"새 버전의 파일을 설치하지 못했습니다",new GuidePart[]
 {
  new GuideText(Vp(from) is {} v?$"이전 버전({v})으로 복구했습니다.":"이전 버전으로 복구했습니다.","strong"),new GuideText(Kept,"dim"),
  new GuideText("‘확인’을 누르면 이전 버전을 다시 실행합니다.","strong"),new GuideFold(ErrorFold,new GuideText(error??"")),
 });
 /// <summary>파일을 바꾸다 실패했고 원래대로 돌려놓지도 못했다. 손으로 복사하는 방법을 처음부터 보인다</summary>
 public static Guide SwapBroken(SwapResult swap,string backupFolder,string app)=>new(FailedTitle,"업데이트를 마치지 못했습니다",new GuidePart[]
 {
  new GuideText("원래 파일을 되돌리는 데도 실패해 "+Mixed,"strong"),new GuideText(Kept,"dim"),new GuideText(Recover(backupFolder),"strong"),
 }.Concat(CopyBack(backupFolder,app)).Append(TwoErrors(swap.Error,swap.RestoreError)).ToArray());
 /// <summary>설치 과정에서 예상하지 못한 오류(설치 상태가 불분명하면 실행 가능한 버전을 단정하지 않는다. 백업 경로를 알면 직접 복사 절차를 보인다)</summary>
 public static Guide InstallCrashed(Exception error,string? backupFolder,string app)=>new(FailedTitle,"업데이트를 마치지 못했습니다",new GuidePart[]
 {
  new GuideText("업데이트 도중 오류가 발생해 프로그램 파일의 상태를 확인할 수 없습니다.","strong"),new GuideText(Kept,"dim"),
  new GuideText(Recover(backupFolder,"프로그램이 열리지 않으면 "),"strong"),
 }.Concat(CopyBack(backupFolder,app)).Append(new GuideFold(ErrorFold,new GuideText(Message(error)))).ToArray());

 /// <summary>
 /// 새 버전이 뜨지 않을 때의 선택. 단추: 0 '이전 버전으로 복구', 1 '새 버전 유지'(끝났을 때) 또는 '더 기다리기'(아직 켜져 있을 때). 기본은 1 이다(처음 초점이 1 이라 연 직후의 Enter 와 Esc·닫기는 1).
 /// </summary>
 public static Guide StartFailed(bool alive,string from,string to,string backupFolder,bool converted=true)
 {
  var parts=new List<GuidePart>
  {
   new GuideText(alive?"새 버전이 2분 동안 시작을 마치지 못했습니다.":"새 버전이 정상적으로 시작되지 않았습니다.","strong"),
   new GuideVersions("설치된 새 버전","v"+to,"복구할 버전","v"+from),
   new GuideText(alive?"조금 더 기다릴 수 있습니다. 복구하면 실행 중인 새 버전을 먼저 종료합니다.":"‘새 버전 유지’를 누르면 새 버전 파일을 그대로 두고 업데이트를 마칩니다.","strong"),
   new GuideText(RecordsAfterRestore(from,converted),"dim"),
  };
  if(!alive)parts.Add(new GuideFold("나중에 이전 버전으로 복구하는 방법",new GuidePart[]{new GuideText("아래 실행 파일을 실행한 뒤 이전 버전 복구를 승인해 주세요.")}.Concat(RestoreFrom(backupFolder)).ToArray()));
  return new(UpdateTitle,"이전 버전으로 복구할까요?",parts,new[]{"이전 버전으로 복구",alive?"더 기다리기":"새 버전 유지"},1);
 }
 /// <summary>업데이트 도중의 복구가 끝났다. '확인' 뒤 이전 버전을 실행한다</summary>
 public static Guide RolledBack(string from,bool converted=true)=>new(UpdateTitle,"이전 버전으로 복구했습니다",new GuidePart[]{new GuideText(RecordsAfterRestore(from,converted),"dim"),new GuideText($"‘확인’을 누르면 이전 버전(v{from})을 실행합니다.","strong")});
 /// <summary>업데이트 도중의 복구를 시작하지 못했다: 프로그램이 켜져 있다</summary>
 public static Guide RollbackBlocked(string backupFolder)=>new(FailedTitle,"이전 버전으로 복구하지 못했습니다",new GuidePart[]
 {
  new GuideText("프로그램이 아직 실행 중이어서 파일을 교체하지 않았습니다.","strong"),new GuideText(Kept,"dim"),
  new GuideText("디맥 개인 기록 저장 오버레이를 모두 닫고 아래 실행 파일을 실행한 뒤, 이전 버전 복구를 승인해 주세요.","strong"),
 }.Concat(RestoreFrom(backupFolder)).ToArray());
 /// <summary>업데이트 도중의 복구가 실패했고 새 버전의 파일은 그대로다</summary>
 public static Guide RollbackKeptNew(string? error,string to,string backupFolder)=>new(FailedTitle,"이전 버전으로 복구하지 못했습니다",new GuidePart[]
 {
  new GuideText($"새 버전(v{to})의 프로그램 파일이 그대로 남아 있습니다.","strong"),new GuideText(Kept,"dim"),
  new GuideText("프로그램을 모두 닫고 아래 실행 파일을 실행한 뒤, 이전 버전 복구를 승인해 주세요.","strong"),
 }.Concat(RestoreFrom(backupFolder)).Append(new GuideFold(ErrorFold,new GuideText(error??""))).ToArray());
 /// <summary>업데이트 도중의 복구가 실패했고 원래대로 돌려놓지도 못했다</summary>
 public static Guide RollbackBroken(SwapResult swap,string backupFolder,string app)=>new(FailedTitle,"이전 버전으로 복구하지 못했습니다",new GuidePart[]
 {
  new GuideText(Mixed,"strong"),new GuideText(Kept,"dim"),new GuideText(Recover(backupFolder),"strong"),
 }.Concat(CopyBack(backupFolder,app)).Append(TwoErrors(swap.Error,swap.RestoreError,true)).ToArray());

 /// <summary>복구한 이전 버전을 자동으로 켜지 못했다. 프로그램 폴더의 실행 파일의 실제 경로를 보인다</summary>
 public static Guide DirectRun(string title,string app)=>new(title,"이전 버전으로 복구했습니다",new GuidePart[]
 {
  new GuideText("프로그램이 자동으로 실행되지 않았습니다.","strong"),new GuideText("아래 파일을 직접 실행해 주세요."),
  new GuidePaths(("실행할 파일",Path.Combine(app.TrimEnd('\\'),UpdateManifest.Executable))),
 });

 /// <summary>파일을 바꾸기 전에 멈춘 뒤 '확인'에서 다시 켜겠다고 한 현재 버전을 켜지 못했다. 프로그램 폴더의 실행 파일을 직접 열게 한다</summary>
 public static Guide OpenCurrent(string app)=>new(UpdateTitle,"프로그램을 다시 실행하지 못했습니다",new GuidePart[]
 {
  new GuideText("현재 버전은 그대로입니다.","strong"),new GuideText("아래 파일을 직접 실행해 주세요."),
  new GuidePaths(("실행할 파일",Path.Combine(app.TrimEnd('\\'),UpdateManifest.Executable))),
 });

 // ── 백업의 실행 파일이 하는 복구(주 화면의 '이전 버전 복구'로 켜졌거나 사용자가 직접 열었다)

 /// <summary>백업의 실행 파일을 직접 열었는데 프로그램이 켜져 있다</summary>
 public static Guide BackupWhileRunning=>new(RestoreTitle,"프로그램이 실행 중입니다.",new GuidePart[]
 {
  new GuideText("이 백업으로 복구하려면 프로그램을 종료한 뒤 백업 폴더의 실행 파일을 다시 열어 주세요."),
  new GuideText("메인 창에 ‘이전 버전 복구’가 보이면 거기서도 복구할 수 있습니다.","dim"),
 });
 /// <summary>백업의 실행 파일을 직접 열었다: 이 백업으로 복구할지. 단추: 0 '복구', 1 '취소'(기본)</summary>
 public static Guide RestoreAskDirect(string now,string backupVersion,string made,bool converted=true)=>new(RestoreTitle,"이 백업으로 이전 버전을 복구할까요?",new GuidePart[]
 {
  new GuideVersions("현재 버전",now,"복구할 버전",Vp(backupVersion)??"알 수 없음","백업 시각",made),
  new GuideText(RecordsAfterRestore(backupVersion,converted),"strong"),
  new GuideText("프로그램 파일을 복구한 뒤 이전 버전을 실행합니다.","dim"),
 },new[]{"복구","취소"},1);

 /// <summary>백업의 실행 파일이 하는 복구가 멈춘 까닭</summary>
 public enum RestoreStop{Running,Clash,Restored,Broken,Crashed}
 /// <summary>
 /// 백업의 실행 파일이 하는 복구가 끝나지 못했다(까닭 다섯). 파일을 바꾸기 전에 멈췄으면 다시 켜 주지 않고,
 /// 프로그램이 켜져 있으면 그 창을, 꺼져 있으면 현재 버전을 직접 열어 쓰도록 알린다(running)
 /// </summary>
 public static Guide RestoreFailed(RestoreStop why,string backupFolder,string app,bool running=false,SwapResult? swap=null,IReadOnlyList<string>? clash=null,Exception? error=null)
 {
  var parts=new List<GuidePart>();
  string useNow=running?"켜져 있는 프로그램은 그대로 사용할 수 있습니다.":$"현재 버전을 사용하려면 프로그램 폴더의 {UpdateManifest.Executable}를 실행해 주세요.";
  switch(why)
  {
   case RestoreStop.Running:
    parts.Add(new GuideText("프로그램이 아직 실행 중이어서 파일을 교체하지 않았습니다.","strong"));parts.Add(new GuideText(Kept,"dim"));
    parts.Add(new GuideText("디맥 개인 기록 저장 오버레이를 모두 종료한 뒤 다시 실행해 ‘이전 버전 복구’를 눌러 주세요.","strong"));parts.Add(new GuideText(useNow,"dim"));break;
   case RestoreStop.Clash:
    parts.Add(new GuideText("프로그램 폴더에 백업 파일과 이름이 같은 항목이 있어 복구를 멈췄습니다. 현재 버전은 그대로입니다.","strong"));parts.Add(new GuideText(Kept,"dim"));
    if(clash is {Count:>0})parts.Add(new GuidePaths(("이름이 겹치는 항목",string.Join("\n",clash))));
    parts.Add(new GuideText("위 항목을 프로그램 폴더 밖으로 옮긴 뒤 ‘이전 버전 복구’를 다시 시도해 주세요.","strong"));parts.Add(new GuideText(useNow,"dim"));
    parts.Add(new GuideFold(RecoverFold(backupFolder),CopyBack(backupFolder,app)));break;
   case RestoreStop.Restored:
    parts.Add(new GuideText("프로그램 파일을 교체하지 못했습니다. 복구를 시도하기 전 버전으로 돌아왔습니다.","strong"));parts.Add(new GuideText(Kept,"dim"));
    parts.Add(new GuideText(swap is {InUse:true}?"파일을 사용 중인 프로그램을 닫은 뒤 ‘이전 버전 복구’를 다시 시도해 주세요.":"잠시 뒤에 ‘이전 버전 복구’를 다시 시도해 주세요.","strong"));parts.Add(new GuideText(useNow,"dim"));
    parts.Add(new GuideFold(ErrorFold,new GuideText(swap?.Error??"")));break;
   case RestoreStop.Broken:
    parts.Add(new GuideText("파일 교체에 실패했고, 교체 전 상태로도 되돌리지 못했습니다. "+Mixed,"strong"));parts.Add(new GuideText(Kept,"dim"));
    parts.Add(new GuideText(Recover(backupFolder),"strong"));parts.AddRange(CopyBack(backupFolder,app));parts.Add(TwoErrors(swap?.Error,swap?.RestoreError,true));break;
   default:
    parts.Add(new GuideText("복구 도중 오류가 발생해 프로그램 파일의 상태를 확인할 수 없습니다.","strong"));parts.Add(new GuideText(Kept,"dim"));
    parts.Add(new GuideText(Recover(backupFolder),"strong"));parts.AddRange(CopyBack(backupFolder,app));
    parts.Add(new GuideFold(ErrorFold,new GuideText(error==null?"":Message(error))));break;
  }
  return new(RestoreTitle,"이전 버전으로 복구하지 못했습니다",parts);
 }

 /// <summary>업데이트용 임시 파일(update\staging 의 실행 파일)을 직접 열었다</summary>
 public static Guide StagingOpened=>new(UpdateTitle,"업데이트용 임시 파일을 열었습니다.",new GuidePart[]
 {
  new GuideText("update 폴더와 같은 위치에 있는 아래 파일을 실행해 주세요."),new GuidePaths(("",UpdateManifest.Executable)),
 });
}
