using System.Text.Json;
namespace LadderRecords;

/// <summary>개인 파일을 새 자리에서 쓸 수 없어 시작하지 않는다. 글은 사용자에게 그대로 보여 준다</summary>
class UserFilesException(string message):IOException(message);

/// <summary>
/// 개인 기록과 설정의 자리(프로그램 폴더의 'UserData')와, v1.1.x 까지 실행 파일 옆에 두던 파일을 그 자리로 옮기는 첫 전환.
/// 규칙:
///  · 먼저 새 자리의 기록이 믿을 수 있는지 본다. 읽을 수 없거나 본 파일 없이 백업·저장하다 만 파일만 있으면 아무것도 옮기지 않고 시작하지 않는다
///  · 새 자리에 없고 옛 자리에 있는 것만 복사한다. 옛 파일은 고치지도 지우지도 않는다(이전 버전으로 되돌리면 그 파일을 쓴다)
///  · 새 자리에 이미 있는 것은 그대로 둔다. 한 번 새 자리에 자리 잡은 파일("정리됨")은 사용자가 지워도 옛 자리에서 다시 가져오지 않는다
///  · 기록의 백업(records.json.bak)은 **이 프로그램이 본 파일을 옮기기 시작한 경우에만** 그 짝으로 옮긴다: 옮기기 전에 "이 내용의 기록을 옮기는 중"이라고 적어 두고(Moving),
///    다시 켰을 때 새 자리와 옛 자리의 본 파일이 모두 그 내용이면 백업을 마저 옮긴다. 사용자가 미리 넣어 둔 기록 옆에는 옛 백업을 두지 않는다(그 기록의 직전 저장이라는 보장이 없다)
///  · 복사는 임시 이름으로 한 뒤 원본과 같은지 보고 이름을 바꾼다. 무엇이 정리됐는지는 맨 나중에 적는다(folder-info.json). 끊기면 다음에 빠진 것만 마저 한다
///  · 복사하지 못하거나 견줄 파일을 읽지 못하면 시작하지 않는다(옛 자리의 원본과 새 자리에 원래 있던 것은 건드리지 않았다. 이미 복사한 것은 새 자리에 남는다)
/// </summary>
static class UserFiles
{
 public const string FolderName="UserData",InfoName="folder-info.json";
 public const string Records="records.json",Backup="records.json.bak",Temporary="records.json.tmp";
 /// <summary>개인 파일의 이름(옮기는 차례. 기록이 먼저다)</summary>
 public static readonly string[] Names={Records,Backup,"hotkeys.json","preferences.json","records-view.json","appearance.json"};
 const string MovingEnd=".moving";   // 복사하는 동안의 임시 이름. 기록 보호가 보는 .tmp·.bak 과 겹치지 않게 다른 이름을 쓴다
 /// <summary>옛 자리의 기록이 처음 옮긴 때와 달라졌을 때 한 번 보여 주는 글</summary>
 public const string ChangedText="이전 버전에서 변경한 기록이 있습니다. 현재 버전은 ‘UserData’ 폴더의 기록을 사용합니다. 이전 버전의 기록 파일도 보관되어 있습니다.";

 public static string Folder=>FolderOf(AppContext.BaseDirectory);
 public static string At(string name)=>Path.Combine(Folder,name);
 public static string FolderOf(string appDir)=>Path.Combine(appDir,FolderName);
 /// <summary>그 프로그램 폴더에서 지금 쓰이는 개인 파일: 새 자리에 있으면 그것, 없으면 옛 자리의 것. 읽기만 할 때 쓴다(전환을 하지 않는 실행: 업데이트·복구를 맡아 켜진 것, 시작 알림)</summary>
 public static string Existing(string appDir,string name){string now=Path.Combine(FolderOf(appDir),name);return File.Exists(now)?now:Path.Combine(appDir,name);}
 /// <summary>그 프로그램 폴더에서 개인 파일이 있는 폴더: 'UserData'가 있으면 그것(그 안에 없는 파일을 옛 자리에서 찾지 않는다), 없으면 프로그램 폴더. 업데이트 직전의 사본을 뜰 때 쓴다</summary>
 public static string Used(string appDir)=>Directory.Exists(FolderOf(appDir))?FolderOf(appDir):appDir;
 /// <summary>시작하지 못했을 때 글의 끝에 붙이는 두 자리(경로 상자의 이름. v1.1 을 쓴 적 없는 사람에게도 맞게 '프로그램 폴더'·'UserData 폴더'로 부른다)</summary>
 public static string Places(string appDir)=>$"{AppPlace}{appDir}\n{DataPlace}{FolderOf(appDir)}";
 /// <summary>경로 상자에서 'UserData' 폴더의 줄이 시작하는 글</summary>
 public const string DataPlace=FolderName+" 폴더: ",AppPlace="프로그램 폴더: ";
 /// <summary>
 /// 이 프로그램 폴더의 기록이 'UserData'에 자리 잡았는가. 그러면 첫 전환(Prepare)이 옛 자리의 기록을 다시 가져오지 않는다:
 /// 새 자리에 기록 파일이 있거나(있으면 다음에 켤 때 자리 잡음으로 적는다), 전환 정보에 기록이 자리 잡음으로 적혀 있다.
 /// 업데이트·복구의 안내가 "현재 기록은 'UserData'에 보관되고 복구 뒤 이전 버전에서 저장한 기록은 따로 보관된다"고 적어도 되는지 가른다.
 /// 설정만 옮긴 것(옛 자리에 기록이 없던 사용자)은 아니다: 그 뒤 이전 버전에서 저장한 기록은 다시 업데이트할 때 새 자리로 옮겨진다. 정보를 읽지 못하면 아닌 것으로 본다
 /// </summary>
 public static bool RecordsMoved(string appDir)
 {
  try
  {
   if(File.Exists(Path.Combine(FolderOf(appDir),Records)))return true;
   string path=Path.Combine(FolderOf(appDir),InfoName);if(!File.Exists(path))return false;
   return JsonSerializer.Deserialize<Info>(File.ReadAllText(path)) is {Settled:not null} info&&info.Settled.Contains(Records,StringComparer.OrdinalIgnoreCase);
  }
  catch(Exception e)when(e is IOException or UnauthorizedAccessException or JsonException){return false;}
 }

 /// <summary>folder-info.json. Settled: 새 자리에 자리 잡은 이름. OldRecords: 옛 자리 기록 파일의 내용(SHA-256. 없으면 빈 글)을 처음 본 때, 또는 그것을 복사해 온 때의 값.
 /// Noticed: 옛 자리의 기록이 달라졌다는 글을 사용자가 이미 본 내용. Moving: 이 프로그램이 옮기기 시작한 기록의 내용(옮기는 중이 아니면 빈 글)</summary>
 public sealed record Info(int Version,List<string> Settled,string OldRecords,string Noticed,string? Moving=null);
 /// <summary>Notice: 주 화면이 뜬 뒤 한 번 보여 줄 글(없으면 null). NoticeKey: 그 글을 보여 준 뒤 Shown 에 넘길 값. Copied: 이번에 옮긴 이름. Store: 전환이 미리 열어 본 새 자리의 기록(있으면)</summary>
 public sealed record Result(string? Notice,string NoticeKey,IReadOnlyList<string> Copied,RecordStore? Store);

 /// <summary>검사가 끼어드는 자리: "copy:이름"(그 파일을 복사하기 직전), "info"(folder-info.json 을 쓰기 직전). 던지면 그 자리에서 끊기거나 실패한 것이 된다</summary>
 internal static Action<string>? Step{get;set;}
 /// <summary>주 화면이 뜬 뒤 한 번 보여 줄 것과, 전환이 미리 열어 본 기록. Program 이 넣고 주 화면이 꺼낸다</summary>
 public static Result? Pending;
 /// <summary>전환이 미리 열어 본 기록이 그 경로의 것이면 한 번 내준다(시작할 때 같은 파일을 두 번 읽지 않게). 아니면 새로 연다</summary>
 public static RecordStore Open(string path)
 {
  var opened=Pending?.Store;
  if(opened!=null&&string.Equals(Path.GetFullPath(path),Path.GetFullPath(At(Records)),StringComparison.OrdinalIgnoreCase)){Pending=Pending! with{Store=null};return opened;}
  return new RecordStore(path);
 }

 /// <summary>프로그램 폴더의 개인 파일을 새 자리에서 쓸 수 있게 한다. 다른 개인 파일을 읽기 전에 한 번 부른다. 시작하면 안 되는 상태면 UserFilesException</summary>
 public static Result Prepare(string appDir)
 {
  appDir=Path.GetFullPath(appDir).TrimEnd(Path.DirectorySeparatorChar);string folder=FolderOf(appDir);
  string New(string name)=>Path.Combine(folder,name);
  string Old(string name)=>Path.Combine(appDir,name);
  bool Has(string name)=>File.Exists(New(name));
  try
  {
   Directory.CreateDirectory(folder);
   // 끊긴 복사가 남긴 임시 파일. 이 전환이 쓰는 이름만 지운다(같은 끝을 가진 사용자의 파일은 건드리지 않는다)
   foreach(string name in Names.Append(InfoName)){string left=New(name)+MovingEnd;if(File.Exists(left))File.Delete(left);}
  }
  catch(Exception e)when(e is IOException or UnauthorizedAccessException){throw new UserFilesException(Failed($"‘{FolderName}’ 폴더를 준비하지 못했습니다.",e,appDir,false));}
  // 견줄 파일의 내용. 읽지 못한 것을 "없음"으로 치지 않는다: 잠깐 잡혀 있을 수 있으니 조금 뒤에 다시 해 보고, 끝내 읽지 못하면 시작하지 않는다
  string Sum(string path)
  {
   Exception? last=null;
   for(int attempt=0;attempt<3;attempt++)
   {
    try{return UpdateFiles.Sha256(path);}
    catch(Exception e)when(e is IOException or UnauthorizedAccessException){last=e;if(attempt<2)Thread.Sleep(200);}
   }
   throw new UserFilesException(Failed($"{Path.GetFileName(path)}의 내용을 확인하지 못했습니다.",last!,appDir,true));
  }
  // 기록을 읽어 본다. 읽을 수 없으면 그 글로 멈춘다. 옛 자리의 기록을 읽어 볼 때에도 글의 끝에 두 자리를 적는다
  RecordStore Read(string path){try{return new RecordStore(path,Places(appDir));}catch(InvalidDataException e){throw new UserFilesException(e.Message);}}

  // ① 전환의 표시를 읽는다(여기서는 읽기만 한다). 잠깐 읽지 못한 것과 내용이 깨진 것을 가른다
  string infoPath=New(InfoName);Info? info=null;bool broken=false;
  if(File.Exists(infoPath))
  {
   string text;
   try{text=File.ReadAllText(infoPath);}
   catch(Exception e)when(e is IOException or UnauthorizedAccessException){throw new UserFilesException(Failed($"기록 위치 정보 파일({InfoName})을 읽지 못했습니다.",e,appDir,false));}
   try{info=JsonSerializer.Deserialize<Info>(text) is {Settled:not null,OldRecords:not null,Noticed:not null} read?read:null;}catch(JsonException){}
   broken=info==null;
  }

  // ② 새 자리의 기록이 믿을 수 있는가. 여기서 멈추면 아무것도 옮기지 않았고 표시도 쓰지 않았다
  RecordStore? store=null;
  if(Has(Records))store=Read(New(Records));
  else
  {
   var left=new[]{".bak",".tmp"}.Where(end=>File.Exists(New(Records)+end)).ToList();
   if(left.Count>0)
   {
    // 남은 파일을 치우고 다시 켰을 때 실제로 일어나는 일을 적는다. 전환을 마쳤는지, 옛 자리에 무엇이 남았는지에 따라 다르다.
    // 어느 경우든 두 자리에 실제로 있는 기록 파일을 한 줄씩 먼저 적고, 그 아래의 선택에서는 그 방법에 필요한 파일만 짚는다
    var olds=new[]{Records,Backup,Temporary}.Where(name=>File.Exists(Old(name))).ToList();
    string here=string.Join(", ",left.Select(end=>Records+end)),there=string.Join(", ",olds);
    var found=new[]{$"UserData 폴더에 남은 기록 파일: {here}",$"프로그램 폴더에 있는 기록 파일: {(olds.Count==0?"없음":there)}"};
    const string empty="· 빈 기록으로 새로 시작하려면: 남은 파일을 다른 폴더로 옮긴 뒤 다시 실행해 주세요.";
    string both=$"· 빈 기록으로 새로 시작하려면: UserData 폴더의 파일({here})과 프로그램 폴더의 파일({there})을 위치별로 따로 다른 폴더에 옮긴 뒤 다시 실행해 주세요. 이전 버전으로 되돌리면 프로그램 폴더의 파일을 쓰므로 지우지 말고 보관해 주세요.";
    const string again="· UserData 폴더의 파일만 옮기면 프로그램 폴더에 남은 파일 때문에 다시 멈춥니다.";
    var choices=new List<string>();
    if(broken)
    {
     // 표시까지 깨져 있다. 옛 자리에 개인 파일이 있으면 남은 파일만 치워서는 표시 파일의 안내가 뜬다: 그 안내를 거치지 않게 표시 파일도 함께 치우라고 바로 적는다.
     // 저장하다 만 파일만 있으면 그 파일의 안내에서 다시 멈춘다. 아무것도 없으면 빈 기록으로 열린다
     if(Names.Any(name=>File.Exists(Old(name))))choices.Add($"· 프로그램 폴더에 남은 파일로 다시 옮기려면: UserData 폴더에 남은 파일({here})과 기록 위치 정보 파일({InfoName})을 다른 폴더로 옮긴 뒤 다시 실행해 주세요.");
     else if(olds.Count>0){choices.Add(both);choices.Add(again);}
     else choices.Add(empty);
    }
    else if(info?.Settled.Contains(Records,StringComparer.OrdinalIgnoreCase)==true)
    {
     // 전환을 마친 뒤다: 옛 자리의 기록을 다시 가져오지 않으므로 빈 기록으로 시작한다(옛 자리의 파일은 치우지 않아도 된다). 옛 자리의 기록을 쓰려면 사용자가 직접 넣는다
     choices.Add($"· 빈 기록으로 새로 시작하려면: UserData 폴더에 남은 파일({here})을 다른 폴더로 옮긴 뒤 다시 실행해 주세요.{(olds.Count>0?" 프로그램 폴더의 파일은 그대로 두어도 됩니다.":"")}");
     if(olds.Contains(Records))choices.Add($"· 프로그램 폴더의 기록을 쓰려면: 프로그램 폴더의 {Records}을 UserData 폴더에 복사한 뒤 다시 실행해 주세요.");
    }
    else if(olds.Contains(Records)){choices.Add($"· UserData 폴더에 남은 파일({here})을 다른 폴더로 옮긴 뒤 다시 실행하면: 프로그램 폴더의 기록({Records})을 가져와 시작합니다.");choices.Add(both);}
    else if(olds.Count>0){choices.Add(both);choices.Add(again);}
    else choices.Add(empty);
    throw new UserFilesException(RecordStore.Missing(New(Records),left,Places(appDir),choices,found));
   }
  }

  // ③ 깨진 표시
  bool rewrite=false;
  if(broken)
  {
   if(store!=null)
   {
    // 표시가 깨졌지만 새 자리의 기록은 온전하다: 전환은 끝난 것으로 보고 있는 것을 그대로 쓴다. 옛 자리에서 아무것도 다시 가져오지 않는다
    // (무엇을 사용자가 일부러 지웠는지 알 수 없다). 옛 기록과 견줄 기준은 잃었으므로 지금의 내용을 새 기준으로 삼는다(그 전에 달라진 것은 알릴 수 없다)
    info=new(1,Names.ToList(),File.Exists(Old(Records))?Sum(Old(Records)):"","");rewrite=true;
   }
   else if(Names.Any(name=>File.Exists(Old(name))))
   {
    // 표시가 깨졌고 새 자리에 기록도 없다: 처음 하는 것인지, 전환을 마친 뒤에 사용자가 기록을 치운 것인지 알 수 없다. 옛 자리에서 되살리지 않고 묻는다
    throw new UserFilesException(string.Join("\n",
     $"기록 위치 정보 파일({InfoName})을 읽을 수 없고 UserData 폴더에 기록 파일이 없습니다. 프로그램 폴더의 기록과 설정을 다시 가져와도 되는지 알 수 없습니다. 기존 파일은 보존되어 있습니다.",
     "",
     $"· 프로그램 폴더에 남은 파일로 다시 옮기려면: UserData 폴더의 {InfoName}을 다른 폴더로 옮긴 뒤 다시 실행해 주세요.",
     $"· 따로 보관한 기록 파일을 쓰려면: 그 파일을 UserData 폴더에 {Records} 이름으로 넣은 뒤 다시 실행해 주세요.",
     "",
     Places(appDir)));
   }
   else rewrite=true;   // 옛 자리에도 아무것도 없다: 되살릴 것이 없으므로 새로 설치한 것과 같다
  }
  var settled=new HashSet<string>(info?.Settled??[],StringComparer.OrdinalIgnoreCase);
  string moving=info?.Moving??"";

  // 옛 자리에 본 파일 없이 백업·저장하다 만 파일만 있고 새 자리에도 기록이 없으면 처음 실행이 아니다. 빈 기록으로 시작하지 않고 알린다(v1.1.x 의 보호 규칙과 같다)
  if(!settled.Contains(Records)&&!Has(Records)&&!File.Exists(Old(Records)))
  {
   var left=new[]{".bak",".tmp"}.Where(end=>File.Exists(Old(Records)+end)).ToList();
   if(left.Count>0)throw new UserFilesException(RecordStore.Missing(Old(Records),left,Places(appDir)));
  }

  var copied=new List<string>();
  void Copy(string name)
  {
   string source=Old(name),target=New(name),temporary=target+MovingEnd;Exception? last=null;
   for(int attempt=0;attempt<3;attempt++)   // 동기화 도구나 백신이 잠깐 잡고 있는 파일은 조금 뒤에 다시 해 본다
   {
    try
    {
     Step?.Invoke("copy:"+name);
     string before=UpdateFiles.Sha256(source);
     File.Copy(source,temporary,true);
     if(UpdateFiles.Sha256(temporary)!=before)throw new IOException("복사한 내용이 원본과 다릅니다.");
     File.Move(temporary,target,false);   // 그사이에 생긴 파일은 덮지 않는다
     copied.Add(name);return;
    }
    catch(Exception e)when(e is IOException or UnauthorizedAccessException)
    {
     last=e;try{File.Delete(temporary);}catch(IOException){}catch(UnauthorizedAccessException){}
     if(attempt<2)Thread.Sleep(200);
    }
   }
   throw new UserFilesException(Failed($"{name}을 UserData 폴더에 복사하지 못했습니다.",last!,appDir,true));
  }
  void Write(Info next,bool midMove)
  {
   string temporary=infoPath+MovingEnd;
   try{Step?.Invoke("info");File.WriteAllText(temporary,JsonSerializer.Serialize(next));File.Move(temporary,infoPath,true);}
   catch(Exception e)when(e is IOException or UnauthorizedAccessException)
   {
    try{File.Delete(temporary);}catch(IOException){}catch(UnauthorizedAccessException){}
    throw new UserFilesException(Failed($"기록 위치 정보 파일({InfoName})을 저장하지 못했습니다.",e,appDir,midMove));
   }
  }

  // ④ 기록과 그 백업
  string first=info?.OldRecords??"";bool baseline=info!=null;   // 옛 기록과 견줄 기준과, 그 기준이 정해져 있는가
  string oldRecords="";bool oldKnown=false;                    // 옛 자리 기록의 지금 내용(없으면 빈 글)과, 그것을 읽었는가
  if(!settled.Contains(Records))
  {
   oldRecords=File.Exists(Old(Records))?Sum(Old(Records)):"";oldKnown=true;
   if(!Has(Records)&&oldRecords!="")
   {
    Read(Old(Records));   // 옮기기 전에 옛 기록이 읽히는지 본다. 읽히지 않으면 아무것도 옮기지 않고 멈춘다(빈 기록으로 시작하지 않는다)
    // 옮기기 전에 "이 내용의 기록을 옮기는 중"이라고 적는다. 본 파일만 옮기고 끊겨도, 다음에 이 표시를 보고 백업을 마저 옮긴다
    if(moving!=oldRecords){moving=oldRecords;Write(new(1,Names.Where(settled.Contains).ToList(),baseline?first:oldRecords,info?.Noticed??"",moving),true);}
    Copy(Records);store=Read(New(Records));
    first=oldRecords;baseline=true;
   }
   if(Has(Records))
   {
    // 이 프로그램이 옮기던 기록이고(새 자리와 옛 자리의 본 파일이 모두 적어 둔 내용이다) 백업이 아직 오지 않았으면 마저 옮긴다. 그 밖에는 옛 백업을 가져오지 않는다
    if(moving!=""&&!Has(Backup)&&File.Exists(Old(Backup))&&oldRecords==moving&&Sum(New(Records))==moving)Copy(Backup);
    settled.Add(Records);settled.Add(Backup);moving="";
    if(!baseline){first=oldRecords;baseline=true;}
   }
  }
  // ⑤ 나머지 설정
  foreach(string name in Names)
  {
   if(name is Records or Backup||settled.Contains(name))continue;
   if(!Has(name)&&File.Exists(Old(name)))Copy(name);
   if(Has(name))settled.Add(name);
  }
  if(!baseline&&oldKnown){first=oldRecords;baseline=true;}

  // ⑥ 옛 자리의 기록이 처음 본 때(또는 복사해 온 때)와 달라졌으면 한 번 알린다: 되돌린 이전 버전에서 저장했거나, 나중에 옛 파일을 가져다 놓았다. 새 버전은 새 자리의 기록을 쓴다.
  // 여기서는 알릴 것만 돌려주고, 사용자가 본 뒤에 Shown 이 "알렸음"으로 적는다. 견줄 파일을 지금 읽지 못하면 이번에는 견주지 않는다(이미 자리 잡은 뒤에는 시작을 막지 않는다)
  string noticed=info?.Noticed??"";string? notice=null;
  if(settled.Contains(Records))
  {
   if(!oldKnown){try{oldRecords=File.Exists(Old(Records))?UpdateFiles.Sha256(Old(Records)):"";oldKnown=true;}catch(IOException){}catch(UnauthorizedAccessException){}}
   if(oldKnown&&oldRecords!=""&&oldRecords!=first&&oldRecords!=noticed)notice=ChangedText;
  }

  var after=new Info(1,Names.Where(settled.Contains).ToList(),first,noticed,moving);
  bool same=info!=null&&info.Settled.SequenceEqual(after.Settled)&&info.OldRecords==after.OldRecords&&info.Noticed==after.Noticed&&(info.Moving??"")==(after.Moving??"");
  // 적을 것이 없으면(새로 설치해 아직 아무 파일도 없음) 쓰지 않는다
  if((rewrite||!same)&&(after.Settled.Count>0||after.OldRecords!=""||File.Exists(infoPath)))Write(after,copied.Count>0||!string.IsNullOrEmpty(info?.Moving));   // 이번에 옮겼거나 옮기던 중일 때만 옮기기의 글
  return new(notice,notice==null?"":oldRecords,copied,store);
 }

 /// <summary>옛 자리의 기록이 달라졌다는 글을 사용자가 봤다. 같은 내용으로는 다시 알리지 않게 적는다. 적지 못하면 다음에 한 번 더 알릴 뿐이다</summary>
 public static void Shown(string appDir,string noticeKey)
 {
  if(noticeKey=="")return;
  try
  {
   string path=Path.Combine(FolderOf(appDir),InfoName);
   if(JsonSerializer.Deserialize<Info>(File.ReadAllText(path)) is not {Settled:not null} info)return;
   string temporary=path+MovingEnd;File.WriteAllText(temporary,JsonSerializer.Serialize(info with{Noticed=noticeKey}));File.Move(temporary,path,true);
  }
  catch(JsonException){}catch(IOException){}catch(UnauthorizedAccessException){}
 }

 // 확인된 원인과 다음 행동을 함께 적는다. 원인을 가를 수 없으면 쓸 수 있는 폴더인지 보라고 한다.
 // what 은 까닭 한 문장이다(안내창의 첫 줄이 이미 '프로그램을 시작할 수 없습니다.'라 본문은 까닭부터 적는다)
 static string Failed(string what,Exception e,string appDir,bool moving)
 {
  int code=e.HResult&0xFFFF;
  string advice=code is 0x70 or 0x27?"저장 공간을 확보한 뒤 다시 실행해 주세요."
   :code is 0x20 or 0x21?"이 파일을 사용 중인 다른 프로그램을 닫은 뒤 다시 실행해 주세요."
   :"표시된 폴더에 파일을 쓸 수 있는지 확인한 뒤 다시 실행해 주세요.";
  // 옮기는 중이 아니면(켤 때마다 지나는 자리) 옮기기의 이야기를 하지 않는다: 옮길 것이 없는 사람에게는 사실이 아니다
  if(!moving)return $"{what} {advice}\n\n{Places(appDir)}";
  return $"{what} 프로그램 폴더의 원본은 보존되어 있습니다. {advice} 그러면 이미 복사한 파일을 확인하고 옮기기를 이어갑니다.\n\n{Places(appDir)}";
 }
}
