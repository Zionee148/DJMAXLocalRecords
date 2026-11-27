using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace LadderRecords;

/// <summary>새 형식으로 처음 저장하기 직전에 이전 형식의 원본 사본을 남기지 못했다. 이번 변경은 저장하지 않았다. 글은 사용자에게 그대로 보여 준다</summary>
class OriginalCopyException(string message):IOException(message);
/// <summary>저장하려는 기록의 모양이 맞지 않아 저장하지 않았다(프로그램의 잘못이다. 이대로 쓰면 다음에 그 파일을 열 수 없다).
/// 화면에는 정해 둔 글(Text)만 보인다. 무엇이 틀렸는지(개발용 진단 글)는 Problem 에 따로 둔다</summary>
class InvalidRecordsException(string problem):IOException(Text)
{
 public const string Text="프로그램 오류로 기록을 저장하지 않았습니다. 기존 기록은 그대로입니다. 프로그램을 다시 실행해도 같으면 제보해 주세요.";
 /// <summary>무엇이 틀렸는지(PlayFormat.Problem 의 글). 화면에 보이지 않는다</summary>
 public string Problem{get;}=problem;
}
// 저장을 잠근 동안(자동 업데이트가 진행되는 동안) 저장하려 했음을 알린다. 저장에 실패한 것으로 다뤄져 메모리의 기록도 되돌려진다
class RecordStoreFrozenException():IOException("업데이트를 진행하는 중이라 저장하지 않았습니다.");
// 기록 파일이 프로그램 밖에서 바뀌었거나 사라져서 저장하지 않았음을 알린다
class RecordFileChangedException(string message):IOException(message);

/// <summary>한 번에 확정하는 작업의 종류(되돌리기의 한 단계에 적힌다)</summary>
static class PlayOps
{
 // delete-all 은 예전 개발 빌드가 만들던 종류다(판정에 보이는 판을 한 번에 지웠다). 이제는 만들지 않는다. 이미 저장된 되돌리기 내역에 남아 있을 수 있어 읽는 이름만 둔다
 public const string Add="add",Replace="replace",Edit="edit",Delete="delete",DeleteAll="delete-all",Unlink="unlink",ImportVArchive="import-varchive",DeleteVArchive="delete-varchive",DeleteCollection="delete-collection",ImportCollection="import-collection";
 /// <summary>되돌리기를 묻는 창에 적는 작업의 이름(여러 판을 바꾼 작업에 쓴다. 한 판을 바꾼 작업은 질문과 대상으로 보인다: UndoView)</summary>
 public static string Name(string kind)=>kind switch{Add=>"등록",Replace=>"최근 플레이 교체",Edit=>"기록 수정",Delete=>"기록 삭제",DeleteAll=>"기록 삭제",Unlink=>"노말 반영 취소",ImportVArchive=>"V아카이브 가져오기",DeleteVArchive=>"V아카이브 일괄 삭제",DeleteCollection=>"컬렉션 일괄 삭제",ImportCollection=>"컬렉션 가져오기",_=>kind};
}

/// <summary>등록을 확정한 결과: 저장한 판과 그 직전의 최고 기록(기록이 없었으면 null)</summary>
record Registered(Play Play,Play? Previous)
{
 /// <summary>오버레이의 결과 장면이 가르는 종류: 첫 기록 first, 최고 SCORE 를 넘음 best, 같거나 낮음 kept(그때도 한 판은 남는다)</summary>
 public string Kind=>Previous==null?"first":Play.Score>Previous.Score?"best":"kept";
 /// <summary>상태 줄과 오버레이에 적는 글</summary>
 public string Text=>Kind switch{"first"=>"최초 등록","best"=>"기록 갱신",_=>"기존 기록 유지"};
}

/// <summary>
/// 기록 파일(형식 2: 저장한 판을 한 줄씩)을 읽고 쓰고, 판을 더하고 고치고 지우고 되돌린다.
/// 파일:
///  · 형식 1 의 파일은 읽을 때 메모리에서만 형식 2 로 올린다. **파일은 건드리지 않는다.** 새 형식으로 쓰는 것은 무언가를 저장하는 첫 순간이다
///    (주 화면이 뜬 뒤다. 새 버전이 켜지다 실패해 이전 버전으로 되돌려도 옛 프로그램이 기록을 읽을 수 있게)
///  · 그 첫 저장 직전에, 읽었던 형식 1 파일의 바이트를 그대로 같은 폴더의 records.format1.json 으로 남긴다. 남기지 못하면 저장하지 않는다.
///    같은 내용의 사본이 이미 있으면 그대로 둔다. 다른 내용의 사본이 있으면 덮지 않고 번호를 붙인 이름(records.format1-2.json …)으로 하나 더 남긴다
///  · 형식 번호와 파일의 실제 모양이 함께 맞을 때만 연다. 있어야 하는 칸이 빠진 파일을 기본값으로 채워 읽지 않는다. 저장하기 전에도 같은 확인을 한다
///  · 밖에서 바뀐 파일을 덮어쓰지 않는 것, 백업을 남기는 것은 GuardedFile 이 한다. 본 파일 없이 백업·저장하다 만 파일만 남았으면 시작하지 않는다
///  · 이 프로그램이 아는 것보다 큰 형식 번호의 파일은 읽지 않고 알린다
/// 판:
///  · 한 번에 확정하는 변경(추가, 최근 한 판 교체, 수정, 삭제, 노말 반영 취소, 가져오기, 일괄 삭제)이 되돌리기의 한 단계다. 저장에 실패하면 메모리의 기록도 그 전으로 돌아간다
///  · 하드·맥스의 한 판이 노말에도 보이는지는 그 판을 **새로 저장하는 순간에만** 정한다(연습·무효가 아니고 그때의 노말 최고를 넘으면). 그 뒤의 수정·삭제로는 생기지도 끊기지도 않는다.
///    끊는 것은 사용자가 노말 쪽에서 지울 때뿐이고, 한 번 끊은 것은 다시 이어지지 않는다
/// </summary>
class RecordStore
{
 public const string OriginalName="records.format1.json";
 const int UndoSteps=20;   // 프로필마다 되돌릴 수 있는 작업의 수(줄 수가 아니라 작업의 수다)
 // 붙여 쓴다: 들여 쓸 때보다 크기가 43% 작고 저장이 30%쯤 빠르다. 읽는 쪽은 두 모양을 모두 읽는다. 빈 칸은 적지 않는다
 static readonly JsonSerializerOptions SaveOptions=new(){DefaultIgnoreCondition=JsonIgnoreCondition.WhenWritingNull};
 readonly string path;readonly GuardedFile file;
 byte[]? original;   // 형식 1 로 읽은 파일의 내용. 새 형식으로 처음 저장하기 직전에 사본으로 남기고, 남긴 뒤에는 null 이다
 public PlayData Data{get;private set;}
 public PlayProfile Active=>Data.Profiles.Single(p=>p.Id==Data.ActiveId);
 public long Revision{get;private set;}
 /// <summary>이전 형식의 파일을 올려 읽었고 아직 새 형식으로 저장하지 않았는가(디스크의 파일은 아직 이전 형식이다)</summary>
 public bool Upgraded=>original!=null;
 /// <summary>true 인 동안 Save 가 아무것도 쓰지 않고 실패한다(자동 업데이트가 기록 사본을 만든 뒤 프로그램을 바꾸는 동안)</summary>
 public bool Frozen{get;set;}
 /// <summary>검사가 끼어드는 자리: "original:경로"(원본 사본을 그 이름으로 쓰기 직전). 던지면 그 자리에서 실패한 것이 된다</summary>
 internal static Action<string>? Step{get;set;}

 // ───────────────────────────── 파일

 /// <summary>places: 시작하지 못했을 때 글의 끝에 적을 자리(주지 않으면 기록 파일이 있는 폴더로 정한다. 첫 전환이 옛 자리의 기록을 읽어 볼 때 두 자리를 준다)</summary>
 public RecordStore(string path,string? places=null)
 {
  this.path=path;file=new(path);
  InvalidDataException Bad(string? why=null,Exception? inner=null)=>new(Unreadable(path,places,inner),inner??(why==null?null:new InvalidDataException(why)));
  if(File.Exists(path)){
   // 읽을 수 없는 기록 파일은 빈 기록으로 보지 않는다: 시작하지 않고 어느 파일인지 알린다(파일은 건드리지 않는다)
   byte[] bytes;try{bytes=file.Read()??throw new FileNotFoundException(path);}catch(Exception e)when(e is IOException or UnauthorizedAccessException){throw Bad(inner:e);}
   // 파일 앞머리의 표시를 보고 UTF-8·UTF-16 을 가린다(편집기로 다시 저장한 옛 파일도 읽는다). 원본 사본은 바꾸기 전의 바이트 그대로다
   string text;using(var reader=new StreamReader(new MemoryStream(bytes)))text=reader.ReadToEnd();
   try
   {
    using var document=JsonDocument.Parse(text);var top=document.RootElement;
    int format=FormatOf(top);
    if(format>PlayData.KnownFormat)throw new InvalidDataException(Newer(path,format,places));
    if(Shape(top,format) is {} wrong)throw Bad(wrong);
    if(format==PlayData.KnownFormat)Data=top.Deserialize<PlayData>()??throw Bad("내용이 없다");
    else{Data=PlayFormat.Upgrade(top.Deserialize<SavedData>()??throw Bad("내용이 없다"));original=bytes;}
   }
   catch(JsonException e){throw Bad(inner:e);}
   if(PlayFormat.Problem(Data) is {} problem)throw Bad(problem);
  }else{
   // 본 파일이 없는데 백업이나 저장하다 만 파일이 남아 있으면 처음 실행이 아니다. 빈 기록으로 시작해 저장하면 남은 파일까지 덮이므로 시작하지 않고 알린다.
   // 어느 파일을 되살릴지는 사용자가 정한다(스스로 되살리지 않는다)
   var left=new[]{".bak",".tmp"}.Where(end=>File.Exists(path+end)).ToList();
   if(left.Count>0)throw new InvalidDataException(Missing(path,left,places));
   var first=new PlayProfile();Data=new(){ActiveId=first.Id,Profiles=new(){first}};
  }
 }
 // 파일의 형식 번호. 번호가 없는 파일(이 번호를 넣기 전에 쓴 것)은 1 이다. 번호가 여럿이면 모두 같아야 하고(다른 번호가 섞인 파일은 어느 형식인지 알 수 없다), 1 보다 작은 번호는 없다
 static int FormatOf(JsonElement top)
 {
  if(top.ValueKind!=JsonValueKind.Object)throw new JsonException("기록 파일의 맨 바깥이 객체가 아니다");
  int? format=null;
  foreach(var property in top.EnumerateObject())
  {
   if(property.Name!="Format")continue;
   if(property.Value.ValueKind!=JsonValueKind.Number||!property.Value.TryGetInt32(out int number)||number<1)throw new JsonException("형식 번호가 1 이상의 정수가 아니다");
   if(format!=null&&format!=number)throw new JsonException("형식 번호가 서로 다르게 두 번 적혀 있다");
   format=number;
  }
  return format??1;
 }
 // 그 형식 번호의 파일이 갖춰야 하는 모양인가. 아니면 무엇이 틀렸는지. 다른 형식의 목록이 들어 있거나 있어야 하는 칸이 빠졌으면 읽지 않는다
 // (형식 2 의 칸은 [JsonRequired] 가 한 번 더 본다. 형식 1 의 자료형에는 그 표시가 없어 여기서 본다)
 static string? Shape(JsonElement top,int format)
 {
  bool Is(JsonElement parent,string name,JsonValueKind kind)=>parent.TryGetProperty(name,out var value)&&value.ValueKind==kind;
  if(!Is(top,"ActiveId",JsonValueKind.String))return "고른 프로필의 번호가 없다";
  if(!Is(top,"Profiles",JsonValueKind.Array))return "프로필 목록이 없다";
  foreach(var profile in top.GetProperty("Profiles").EnumerateArray())
  {
   if(profile.ValueKind!=JsonValueKind.Object)return "프로필 자리에 프로필이 아닌 것이 있다";
   if(!Is(profile,"Id",JsonValueKind.String)||!Is(profile,"Name",JsonValueKind.String))return "프로필의 이름이나 번호가 없다";
   bool plays=profile.TryGetProperty("Plays",out _),records=profile.TryGetProperty("Records",out _);
   if(format==PlayData.KnownFormat)
   {
    if(records)return "형식 번호는 2 인데 형식 1 의 기록 목록(Records)이 들어 있다";
    if(!Is(profile,"Plays",JsonValueKind.Array))return "판의 목록(Plays)이 없다";
   }
   else
   {
    if(plays)return "형식 번호는 1(또는 없음)인데 형식 2 의 판 목록(Plays)이 들어 있다";
    if(!Is(profile,"Records",JsonValueKind.Object))return "기록 목록(Records)이 없다";
    foreach(var record in profile.GetProperty("Records").EnumerateObject())
    {
     if(record.Value.ValueKind!=JsonValueKind.Object)return $"기록 자리에 기록이 아닌 것이 있다({record.Name})";
     if(!Is(record.Value,"Accuracy",JsonValueKind.Number)||!Is(record.Value,"At",JsonValueKind.String))return $"기록에 RATE 나 저장 시각이 없다({record.Name})";
    }
   }
  }
  return null;
 }
 // 글의 끝에 붙이는 자리: 프로그램 폴더의 'UserData'에 있는 기록이면 프로그램 폴더와 'UserData' 폴더를 한 줄씩, 그 밖(검사가 쓰는 임시 폴더 등)에는 그 폴더
 internal static string Places(string path)
 {
  string dir=Path.GetDirectoryName(Path.GetFullPath(path))!;
  return Path.GetFileName(dir)==UserFiles.FolderName&&Path.GetDirectoryName(dir) is {} app?UserFiles.Places(app):"폴더: "+dir;
 }
 // 읽을 수 없는 기록 파일의 글. 안내창의 첫 줄이 '프로그램을 시작할 수 없습니다.'라 까닭부터 적고, 파일을 건드리지 않았다고 적는다.
 // 백업이 있으면 그것으로 되살리는 법을 함께 적는다(읽을 수 없는 본 파일이 그 자리에 있으므로 먼저 옮겨 두어야 사본의 이름을 바꿀 수 있다)
 internal static string Unreadable(string path,string? places=null,Exception? failed=null)
 {
  string full=Path.GetFullPath(path),name=Path.GetFileName(full);
  // 파일을 열지 못한 것(다른 프로그램이 잡고 있음, 권한)은 내용이 깨진 것이 아니다: 그 까닭과 다음 행동만 적고 백업으로 되살리는 법은 붙이지 않는다.
  // 잠금을 풀고 다시 켜면 최신 기록 그대로 열린다. 백업을 쓰면 마지막 저장 한 건이 빠진다
  if(failed is UnauthorizedAccessException)return $"기록 파일을 읽을 수 없습니다. {full} 파일을 읽을 권한이 없습니다. 파일과 폴더의 권한을 확인한 뒤 다시 실행해 주세요. 파일은 건드리지 않았습니다.\n\n{places??Places(path)}";
  if(failed is IOException io&&(io.HResult&0xFFFF) is 0x20 or 0x21)return $"기록 파일을 읽을 수 없습니다. {full} 파일을 다른 프로그램이 사용하고 있습니다. 그 프로그램(동기화 프로그램, 백신 등)을 닫은 뒤 다시 실행해 주세요. 파일은 건드리지 않았습니다.\n\n{places??Places(path)}";
  if(failed is IOException)return $"기록 파일을 읽을 수 없습니다. {full} 파일을 여는 중 오류가 났습니다. 잠시 뒤 다시 실행해 주세요. 파일은 건드리지 않았습니다.\n\n{places??Places(path)}";
  string backup=File.Exists(full+".bak")?$"\n\n· 백업으로 되살리려면: 읽을 수 없는 {name}을 다른 폴더로 옮겨 보관하고, {name}.bak을 복사해 사본의 이름을 {name}으로 바꾼 뒤 다시 실행해 주세요. 백업은 마지막 저장 직전의 기록입니다.":"";
  return $"기록 파일을 읽을 수 없습니다. {full} 파일을 확인해 주세요. 파일은 건드리지 않았습니다.{backup}\n\n{places??Places(path)}";
 }
 /// <summary>본 파일 없이 백업·저장하다 만 파일만 남았을 때의 글. places 를 주면 끝의 자리를 그것으로 쓴다(첫 전환이 알릴 때).
 /// choices: 남은 파일을 치우고 다시 켰을 때 일어나는 일의 안내. 주지 않으면 "빈 기록으로 시작한다"다. 첫 전환은 옛 자리(실행 파일 옆)에 무엇이 남았는지,
 /// 전환을 마쳤는지에 따라 결과가 달라지므로 실제 결과에 맞는 글을 준다.
 /// found: 남은 파일이 있는 폴더를 적는 줄 다음에 더할 줄들(첫 전환이 두 자리에 실제로 있는 기록 파일을 한 줄씩 적는다)</summary>
 internal static string Missing(string path,List<string> left,string? places=null,IReadOnlyList<string>? choices=null,IReadOnlyList<string>? found=null)
 {
  string name=Path.GetFileName(path),folder=Path.GetDirectoryName(Path.GetFullPath(path))!,where=places??Places(path);bool backup=left.Contains(".bak"),temporary=left.Contains(".tmp");
  // 안내창의 첫 줄이 '프로그램을 시작할 수 없습니다.'라 까닭부터 적는다.
  // 남은 파일이 있는 폴더가 끝의 경로 상자에 이미 적혀 있으면('UserData' 폴더나 프로그램 폴더) 그 줄을 따로 적지 않고, 어느 폴더인지는 첫 문장에 적는다
  var boxed=where.Split('\n');
  bool inData=boxed.Any(line=>string.Equals(line,UserFiles.DataPlace+folder,StringComparison.OrdinalIgnoreCase)),inApp=!inData&&boxed.Any(line=>string.Equals(line,UserFiles.AppPlace+folder,StringComparison.OrdinalIgnoreCase));
  bool listed=inData||inApp;
  string at=inData?"‘"+UserFiles.FolderName+"’ 폴더에 ":inApp?"프로그램 폴더에 ":"";
  var lines=new List<string>{$"{at}기록 파일({name})이 없고 {string.Join(", ",left.Select(end=>name+end))}만 남아 있습니다. 남아 있는 파일을 다른 폴더에 복사해 보관한 뒤 복구할 기록을 확인해 주세요."};
  if(!listed)lines.Add("남은 파일이 있는 폴더: "+folder);
  else if(found!=null)lines.Add("");   // 경로 상자가 빠진 자리: 두 자리의 파일 목록은 첫 문단과 떼어 둔다
  if(found!=null)lines.AddRange(found);
  lines.Add("");
  if(backup)lines.Add($"· 백업으로 되살리려면: {name}.bak을 복사하고 사본의 이름을 {name}으로 바꾼 뒤 다시 실행해 주세요. 백업은 마지막 저장 직전의 기록입니다.");
  if(temporary)lines.Add($"· {name}.tmp는 저장하다 끊긴 파일이라 내용이 온전하지 않을 수 있습니다.");
  if(choices!=null)lines.AddRange(choices);
  else lines.Add("· 빈 기록으로 새로 시작하려면: 남은 파일을 다른 폴더로 옮긴 뒤 다시 실행해 주세요.");
  lines.Add("");lines.Add(where);
  return string.Join("\n",lines);
 }
 internal static string Newer(string path,int format,string? places=null)=>string.Join("\n",
  $"기록 파일({Path.GetFileName(path)})은 이 프로그램보다 새 버전에서 저장된 형식입니다(기록 형식 {format}).",
  "기록 파일은 건드리지 않았습니다.",
  "",
  "· 새 버전으로 다시 업데이트하면 이 기록을 그대로 쓸 수 있습니다.",
  "· 이 버전을 계속 쓰려면: 업데이트하기 전의 기록 사본이 프로그램 폴더의 update\\backup 안 버전별 폴더의 data 폴더에 있습니다. 지금의 기록 파일은 지우지 말고 다른 곳에 옮겨 둔 뒤, 그 사본을 기록 파일이 있던 폴더에 복사해 주세요. 업데이트한 뒤에 쌓인 기록은 그 사본에 없습니다.",
  "· 자세한 안내: "+UpdateBackup.GuidePage,
  "",
  places??Places(path));

 /// <summary>저장을 거절한 까닭(InvalidRecordsException.Problem)을 받는 곳. 주 화면이 진단 기록에 남긴다</summary>
 public static Action<string>? Rejected;
 public void Save()
 {
  if(Frozen)throw new RecordStoreFrozenException();
  // 쓸 수 없는 모양은 저장하지 않는다(원본 사본도 만들지 않는다). 여기에 걸리면 프로그램의 잘못이다
  if(PlayFormat.Problem(Data) is {} problem){Rejected?.Invoke(problem);throw new InvalidRecordsException(problem);}   // 화면에는 정해 둔 글만, 무엇이 틀렸는지는 예외의 Problem 에 둔다
  if(original!=null)KeepOriginal(original);   // 남기지 못하면 던지고, 아래의 저장을 하지 않는다
  file.Write(stream=>JsonSerializer.Serialize(stream,Data,SaveOptions));
  original=null;Revision++;
 }
 // 이전 형식의 원본을 같은 폴더에 남긴다. 같은 내용의 사본이 있으면 그것으로 됐고, 다른 내용의 사본이 있으면 덮지 않고 다음 번호의 이름을 쓴다
 void KeepOriginal(byte[] bytes)
 {
  string folder=Path.GetDirectoryName(Path.GetFullPath(path))!;byte[] sum=SHA256.HashData(bytes);
  string stem=Path.GetFileNameWithoutExtension(OriginalName),end=Path.GetExtension(OriginalName),target=Path.Combine(folder,OriginalName);
  try
  {
   for(int number=1;number<=99;number++)
   {
    target=Path.Combine(folder,number==1?OriginalName:$"{stem}-{number}{end}");
    if(File.Exists(target))
    {
     byte[] there;using(var stream=File.OpenRead(target))there=SHA256.HashData(stream);
     if(there.AsSpan().SequenceEqual(sum))return;   // 이미 남겨 두었다
     continue;                                      // 다른 원본의 사본이다. 덮지 않는다
    }
    Step?.Invoke("original:"+target);
    string moving=target+".moving";
    File.WriteAllBytes(moving,bytes);
    byte[] written;using(var stream=File.OpenRead(moving))written=SHA256.HashData(stream);
    if(!written.AsSpan().SequenceEqual(sum)){File.Delete(moving);throw new IOException("남긴 사본이 원본과 다릅니다.");}
    File.Move(moving,target,false);
    return;
   }
   throw new IOException("원본 사본의 이름을 더 만들 수 없습니다.");
  }
  catch(Exception e)when(e is IOException or UnauthorizedAccessException)
  {
   try{File.Delete(target+".moving");}catch(IOException){}catch(UnauthorizedAccessException){}
   // 원인을 확인했으면 그에 맞는 다음 행동을, 가를 수 없으면 쓸 수 있는 폴더인지 보라고 한다. 아래에 보관하려던 파일의 전체 경로를 적는다
   int code=e.HResult&0xFFFF;
   // 어느 창에서 저장했든 맞게 '다시 시도'라고 적는다(가져오기 창에는 '저장' 단추가 없다)
   string advice=code is 0x70 or 0x27?"저장 공간을 확보한 뒤 다시 시도해 주세요."
    :code is 0x20 or 0x21?"이 파일을 사용 중인 다른 프로그램을 닫은 뒤 다시 시도해 주세요."
    :"표시된 폴더에 파일을 쓸 수 있는지 확인한 뒤 다시 시도해 주세요.";
   throw new OriginalCopyException($"이전 기록의 원본 사본을 보관하지 못해 이번 변경을 저장하지 않았습니다. 기존 기록은 그대로입니다. {advice}\n\n보관하려던 파일: {target}");
  }
 }

 // ───────────────────────────── 한 번에 확정하는 변경

 internal PlayData Snapshot()=>new(){Format=Data.Format,ActiveId=Data.ActiveId,Profiles=Data.Profiles.Select(p=>new PlayProfile{Id=p.Id,Name=p.Name,NextSeq=p.NextSeq,Plays=new(p.Plays),Recent=new(p.Recent),Undo=new(p.Undo)}).ToList()};
 // 고치고 저장한다. 저장하지 못하면 메모리의 기록도 고치기 전으로 돌린다(일부만 바뀐 채로 남지 않는다)
 void Transaction(Action edit){var before=Snapshot();rows=null;try{edit();rows=null;Save();}catch{Data=before;rows=null;throw;}}
 public void Select(string id)=>Transaction(()=>{if(!Data.Profiles.Any(p=>p.Id==id))throw new Exception("프로필 없음");Data.ActiveId=id;});
 // 이름 칸은 40자까지만 받으므로 실제로는 빈 이름일 때만 걸린다. 40자를 넘는 이름은 그에 맞는 글로 따로 막는다
 void NameCheck(string name,bool rename=false){if(string.IsNullOrWhiteSpace(name))throw new Exception("프로필 이름을 입력해 주세요.");if(name.Trim().Length>40)throw new Exception("프로필 이름은 40자까지 입력할 수 있습니다.");if(Data.Profiles.Any(p=>(!rename||p.Id!=Active.Id)&&p.Name.Equals(name.Trim(),StringComparison.OrdinalIgnoreCase)))throw new Exception("같은 이름의 프로필이 이미 있습니다.");}
 /// <summary>copy: 지금 프로필의 판과 넘겨받은 최근 플레이 시각을 모두 복사한다(순번도 그대로). 되돌리기 내역은 복사하지 않는다</summary>
 public void Create(string name,bool copy){NameCheck(name);Transaction(()=>{var from=Active;var p=copy?new PlayProfile{Name=name.Trim(),NextSeq=from.NextSeq,Plays=new(from.Plays),Recent=new(from.Recent)}:new PlayProfile{Name=name.Trim()};Data.Profiles.Add(p);Data.ActiveId=p.Id;});}
 public void Rename(string name){NameCheck(name,true);Transaction(()=>Active.Name=name.Trim());}
 public void Delete(){if(Data.Profiles.Count<2)throw new Exception("마지막 프로필은 삭제할 수 없습니다.");Transaction(()=>{Data.Profiles.Remove(Active);Data.ActiveId=Data.Profiles[0].Id;});}

 // ───────────────────────────── 화면이 묻는 것

 // 프로필마다, 판정(키)마다 보이는 판. 노말 판정에는 그 판정의 판과, 같은 곡·버튼·패턴의 하드·맥스 판 가운데 노말에 반영된 것이 함께 보인다. 기록이 바뀔 때마다 버리고 다시 만든다
 Dictionary<string,Dictionary<string,List<Play>>>? rows;
 Dictionary<string,List<Play>> Rows(PlayProfile profile)
 {
  rows??=new();
  if(rows.TryGetValue(profile.Id,out var found))return found;
  found=new(StringComparer.Ordinal);
  void Show(string key,Play play){if(!found.TryGetValue(key,out var list))found[key]=list=new();list.Add(play);}
  foreach(var play in profile.Plays){Show(play.Key,play);if(play.Normal==NormalLink.Linked&&NormalKey(play.Key) is {} normal)Show(normal,play);}
  return rows[profile.Id]=found;
 }
 /// <summary>하드·맥스의 키에 대응하는 노말의 키(노말의 키면 null)</summary>
 public static string? NormalKey(string key){int at=key.LastIndexOf('|');return at<0||key[(at+1)..]=="노말"?null:key[..(at+1)]+"노말";}
 /// <summary>그 판정에 보이는 판(순번의 차례)</summary>
 public IReadOnlyList<Play> History(string key,PlayProfile? profile=null)=>Rows(profile??Active).TryGetValue(key,out var list)?list:Array.Empty<Play>();
 /// <summary>기록이 있는 판정의 키들</summary>
 public IEnumerable<string> Keys(PlayProfile? profile=null)=>Rows(profile??Active).Keys;
 // 더 앞에 두는 판(최고 기록과 PB 를 고르는 규칙): 높은 SCORE → SCORE 가 같으면 시각이 있는 판 → 둘 다 시각이 있으면 더 늦은 시각 → 시각도 같으면(둘 다 일시 미상일 때도) 더 큰 순번, 곧 나중에 등록한 판
 internal static bool Ahead(Play a,Play b)=>a.Score!=b.Score?a.Score>b.Score:(a.At!=null)!=(b.At!=null)?a.At!=null:a.At!=b.At?a.At>b.At:a.Seq>b.Seq;
 /// <summary>그 판정의 최고 기록(출처를 가리지 않는다). 최고 SCORE 가 같은 판이 여럿이면 PB 의 규칙으로 하나를 고른다</summary>
 public Play? Best(string key,PlayProfile? profile=null){Play? best=null;foreach(var play in History(key,profile))if(best==null||Ahead(play,best))best=play;return best;}
 /// <summary>그 판정의 최고 기록의 RATE(주 화면과 오버레이의 '개인 최고'). 기록이 없으면 null</summary>
 public decimal? BestRate(Selection chart)=>Best(chart.Key) is {} best?ScoreValue.Rate(best.Score):null;
 /// <summary>지금 프로필에서 그 출처의 줄 수(가져온 기록의 일괄 삭제가 몇 건을 지우는지)</summary>
 public int Count(string source)=>Active.Plays.Count(p=>p.Source==source);
 /// <summary>PB 표식을 붙일 한 판: 최고 SCORE 인 판 가운데 시각이 있는 것 → 가장 늦은 시각 → 같으면 나중에 등록한 것. 최고 SCORE 인 판이 모두 일시 미상이면 판에는 붙이지 않는다(null)</summary>
 public Play? Pb(string key,PlayProfile? profile=null)=>Best(key,profile) is {At:not null} best?best:null;
 /// <summary>목록의 '최근 플레이': 직접 저장한 판의 가장 늦은 시각과 형식 1 에서 넘겨받은 시각 가운데 늦은 것. 옛 최고 기록의 저장 시각과 가져온 기록은 쓰지 않는다</summary>
 public DateTime? Recent(string key,PlayProfile? profile=null)
 {
  profile??=Active;DateTime? latest=profile.Recent.TryGetValue(key,out var kept)?kept:null;
  foreach(var play in History(key,profile))if(PlaySource.Direct(play.Source)&&play.At is {} at&&(latest==null||at>latest))latest=at;
  return latest;
 }
 /// <summary>'최근 플레이 1건 교체'의 대상: 그 판정에 직접 저장한 판 가운데 플레이 시각이 가장 늦은 것, 같으면 나중에 등록한 것. 옛 기록, 가져온 기록, 다른 판정에서 반영된 판은 대상이 아니다</summary>
 public Play? ReplaceTarget(string key)
 {
  Play? target=null;
  foreach(var play in Active.Plays)if(play.Key==key&&PlaySource.Direct(play.Source)&&(target==null||play.At>target.At||play.At==target.At&&play.Seq>target.Seq))target=play;
  return target;
 }

 // ───────────────────────────── 판을 더하고 고치고 지운다

 // 한 작업. edit 가 바꿀 줄을 모으면 그대로 적용하고, 마지막 판이 사라진 판정의 '넘겨받은 최근 플레이 시각'을 함께 지우고, 되돌리기에 한 단계로 남긴다
 void Apply(string kind,Action<PlayProfile,List<PlayChange>> edit)=>Transaction(()=>{
  var profile=Active;int next=profile.NextSeq;var plays=new List<PlayChange>();
  edit(profile,plays);
  SetAll(profile,plays.Select(c=>(c.Before,c.After)).ToList());
  rows=null;var recent=new List<RecentChange>();
  foreach(string key in plays.Where(c=>c.Before!=null).SelectMany(c=>new[]{c.Before!.Key,NormalKey(c.Before.Key)}).Where(k=>k!=null).Distinct()!)
   if(History(key!,profile).Count==0&&profile.Recent.Remove(key!,out var kept))recent.Add(new(key!,kept,null));
  profile.Undo.Add(new(kind,DateTime.UtcNow,next,plays,recent.Count==0?null:recent));
  if(profile.Undo.Count>UndoSteps)profile.Undo.RemoveAt(0);
 });
 // 한 줄을 바꾼다: before 가 있으면 그 순번의 줄을 after 로 바꾸거나(after 가 없으면 지운다), before 가 없으면 after 를 순번의 자리에 넣는다
 internal static void Set(PlayProfile profile,Play? before,Play? after)
 {
  if(before!=null)
  {
   int at=profile.Plays.FindIndex(p=>p.Seq==before.Seq);if(at<0)throw new InvalidOperationException("바꿀 플레이가 기록에 없습니다.");
   if(after==null)profile.Plays.RemoveAt(at);else profile.Plays[at]=after;
  }
  else if(after!=null)
  {
   if(profile.Plays.Count==0||profile.Plays[^1].Seq<after.Seq){profile.Plays.Add(after);return;}
   int at=profile.Plays.FindIndex(p=>p.Seq>after.Seq);profile.Plays.Insert(at<0?profile.Plays.Count:at,after);
  }
 }
 /// <summary>
 /// 여러 줄을 차례로 바꾼다. 결과는 차례대로 Set 을 부른 것과 같다. 줄이 순번 차례로 놓여 있고 바꾸는 줄의 순번이 서로 다르며 고치는 줄의 순번이 그대로일 때는
 /// 목록을 한 번만 훑는다(컬렉션 가져오기와 그 되돌리기는 줄 수천 개를 한 작업에서 바꾼다. 줄마다 목록을 뒤지면 1만 줄에서 1초를 넘었다)
 /// </summary>
 internal static void SetAll(PlayProfile profile,List<(Play? Before,Play? After)> changes)
 {
  var plays=profile.Plays;bool sorted=true;for(int i=1;i<plays.Count&&sorted;i++)sorted=plays[i-1].Seq<plays[i].Seq;
  var touched=new HashSet<int>();var replace=new Dictionary<int,Play?>();var inserts=new List<Play>();
  bool quick=sorted&&changes.Count>1;
  foreach(var (before,after) in changes)
  {
   if(!quick)break;
   if(before!=null){quick=touched.Add(before.Seq)&&(after==null||after.Seq==before.Seq);replace[before.Seq]=after;}
   else if(after!=null){quick=touched.Add(after.Seq);inserts.Add(after);}
  }
  if(!quick){foreach(var (before,after) in changes)Set(profile,before,after);return;}
  var result=new List<Play>(plays.Count+inserts.Count);int found=0;
  foreach(var play in plays)
  {
   if(touched.Contains(play.Seq)&&!replace.ContainsKey(play.Seq)){foreach(var (before,after) in changes)Set(profile,before,after);return;}   // 넣을 순번이 이미 있다: 하나씩(같은 결과)
   if(!replace.TryGetValue(play.Seq,out var now)){result.Add(play);continue;}
   found++;if(now!=null)result.Add(now);
  }
  if(found!=replace.Count)throw new InvalidOperationException("바꿀 플레이가 기록에 없습니다.");
  // 넣는 줄: 순번 차례의 자리에(Set 은 순번이 더 큰 첫 줄 앞에 넣는다. 줄이 순번 차례이면 차례대로 합친 것과 같다)
  inserts.Sort((a,b)=>a.Seq.CompareTo(b.Seq));
  var merged=new List<Play>(result.Count+inserts.Count);int k=0;
  foreach(var play in result){while(k<inserts.Count&&inserts[k].Seq<play.Seq)merged.Add(inserts[k++]);merged.Add(play);}
  while(k<inserts.Count)merged.Add(inserts[k++]);
  plays.Clear();plays.AddRange(merged);
 }
 Play Row(int seq)=>Active.Plays.FirstOrDefault(p=>p.Seq==seq)??throw new Exception("그 기록이 없습니다. 목록을 다시 확인해 주세요.");

 /// <summary>
 /// 한 판을 더한다(자동 인식 뒤 확인한 판은 play, 수동으로 더한 판은 manual). 최고점 미만이나 동점도 한 판으로 남는다.
 /// 하드·맥스의 판이 연습·무효가 아니고 지금의 노말 최고 SCORE 를 넘으면(노말 기록이 없어도) 노말에도 보이게 한다. 동점은 넘은 것이 아니다. 과거 시각으로 더해도 지금의 노말 최고와 견준다
 /// </summary>
 public Play Add(Selection chart,int score,DateTime at,string source=PlaySource.Play,bool practice=false)
 {
  ScoreValue.Validate(score);if(!PlaySource.Direct(source))throw new ArgumentException("직접 저장하는 플레이의 출처가 아닙니다.",nameof(source));
  Play? added=null;
  Apply(PlayOps.Add,(profile,plays)=>{
   bool upper=chart.Judge is "하드" or "맥스";string? normal=null;
   if(upper&&!practice&&(Best(NormalKey(chart.Key)!,profile) is not {} best||score>best.Score))normal=NormalLink.Linked;
   added=new Play(profile.NextSeq++,chart.Key,score,at,source,normal,upper&&practice);
   plays.Add(new(null,added));
   DropSameCollection(profile,added,plays);
  });
  return added!;
 }
 /// <summary>
 /// 등록을 확인해 한 판을 저장한다(자동 인식 뒤의 확인). 저장한 판과 그 직전의 최고 기록을 함께 돌려준다: 주 화면의 상태 줄과 오버레이의 결과 장면이 이 둘로 '최초 등록 · 기록 갱신 · 기존 기록 유지'를 가른다.
 /// 같거나 낮은 점수도 한 판으로 남는다. practice 면 연습·무효로 저장한다(노말에 반영하지 않는다)
 /// </summary>
 public Registered Register(Selection chart,int score,DateTime at,bool practice=false){var previous=Best(chart.Key);return new(Add(chart,score,at,PlaySource.Play,practice),previous);}
 /// <summary>'최근 플레이 1건을 입력한 점수로 교체': 새 줄을 만들지 않고 대상 판의 점수만 바꾼다. 원래 시각, 노말 반영 상태, 연습 여부는 그대로다</summary>
 public Play Replace(Selection chart,int score)
 {
  ScoreValue.Validate(score);var target=ReplaceTarget(chart.Key)??throw new Exception("교체할 최근 플레이가 없습니다.");
  Apply(PlayOps.Replace,(profile,plays)=>{var after=target with{Score=score};plays.Add(new(target,after));DropSameCollection(profile,after,plays);});
  return target with{Score=score};
 }
 /// <summary>한 판의 점수를 고친다(원래 시각을 유지한다). 직접 저장한 판과 세부 SCORE 가 남은 옛 기록만 고칠 수 있다. 노말에 반영된 판이면 노말에서도 함께 바뀐다(연결은 그대로다)</summary>
 public void Edit(int seq,int score)
 {
  ScoreValue.Validate(score);var row=Row(seq);
  if(!PlaySource.Timed(row.Source))throw new Exception("이 기록은 수정할 수 없습니다.");
  Apply(PlayOps.Edit,(_,plays)=>plays.Add(new(row,row with{Score=score})));
 }
 /// <summary>한 판을 지운다. 노말에 반영된 하드·맥스의 판이면 노말에서도 함께 사라진다</summary>
 public void Delete(int seq){var row=Row(seq);Apply(PlayOps.Delete,(_,plays)=>plays.Add(new(row,null)));}
 /// <summary>노말 쪽에서 지운다: 하드·맥스의 원래 판은 남기고 노말 반영만 끊는다. 한 번 끊은 것은 원본을 고쳐도 다시 이어지지 않는다</summary>
 public void CancelNormal(int seq)
 {
  var row=Row(seq);if(row.Normal!=NormalLink.Linked)throw new Exception("노말에 반영된 기록이 아닙니다.");
  Apply(PlayOps.Unlink,(_,plays)=>plays.Add(new(row,row with{Normal=NormalLink.Cancelled})));
 }
 /// <summary>
 /// V아카이브에서 가져올 때 바뀔 줄. 노말 판정마다 varchive 한 줄을 둔다: 그 줄이 없으면 다른 출처의 기록보다 낮아도 더하고, 있으면 가져온 값이 그 줄보다 높을 때만 고친다.
 /// 같거나 낮은 값, 되풀이한 가져오기로는 줄이 늘지 않는다. 더할 줄의 순번은 적용할 때 준다(여기서는 0 이다)
 /// </summary>
 public List<PlayChange> PreviewImport(IReadOnlyDictionary<Selection,decimal> scores)
 {
  var kept=Active.Plays.Where(p=>p.Source==PlaySource.VArchive).ToDictionary(p=>p.Key);var changes=new List<PlayChange>();
  foreach(var (chart,rate) in scores)
  {
   if(chart.Judge!="노말"||rate<0||rate>100)continue;
   int score=ScoreValue.FromAccuracy(rate);
   if(!kept.TryGetValue(chart.Key,out var old))changes.Add(new(null,new Play(0,chart.Key,score,null,PlaySource.VArchive)));
   else if(score>old.Score)changes.Add(new(old,old with{Score=score}));
  }
  return changes;
 }
 /// <summary>V아카이브의 기록을 가져온다(한 작업). 바뀐 줄의 수를 돌려준다. 바뀔 것이 없으면 아무것도 하지 않는다(되돌리기에도 남지 않는다)</summary>
 public int Import(IReadOnlyDictionary<Selection,decimal> scores)
 {
  var changes=PreviewImport(scores);if(changes.Count==0)return 0;
  Apply(PlayOps.ImportVArchive,(profile,plays)=>{foreach(var change in changes)plays.Add(change.Before==null?new(null,change.After! with{Seq=profile.NextSeq++}):change);});
  return changes.Count;
 }
 /// <summary>
 /// 직접 저장한 판(등록·수동 추가·최근 플레이 교체)이 노말 히스토리에 날짜와 함께 그 점수로 보이게 되면, 그 노말 키의 같은 점수의 컬렉션 줄을 같은 작업 안에서 지운다
 /// (컬렉션을 먼저 가져왔다가 같은 점수를 직접 저장해도, 같은 점수의 날짜 있는 노말 기록이 있으면 컬렉션 줄을 두지 않는 CollectionPlan 과 같은 결과가 된다. 되돌리면 함께 돌아온다. 히스토리의 수정으로는 지우지 않는다)
 /// </summary>
 static void DropSameCollection(PlayProfile profile,Play saved,List<PlayChange> plays)
 {
  if(saved.At==null)return;
  string? shown=NormalKey(saved.Key)==null?saved.Key:saved.Normal==NormalLink.Linked?NormalKey(saved.Key):null;
  if(shown!=null&&profile.Plays.FirstOrDefault(p=>p.Source==PlaySource.Collection&&p.Key==shown&&p.Score==saved.Score) is {} same)plays.Add(new(same,null));
 }
 /// <summary>
 /// 컬렉션 가져오기를 적용한다(한 작업. 직전 저장 되돌리기 한 번으로 적용 직전으로 돌아간다). 저장하기 직전에 지금 기록으로 다시 셈해 사용자가 본 셈(seen)과 견주고,
 /// 달라졌으면 저장하지 않고 다시 확인할 키와 지금의 셈을 돌려준다(이미 읽은 값은 다시 읽지 않는다). 바뀔 것이 없으면 저장하지 않고 되돌리기에도 남기지 않는다.
 /// 결과를 읽은 프로필이 지금 프로필이 아니면 저장하지 않는다(던진다). 규칙은 CollectionPlan
 /// </summary>
 /// <summary>컬렉션을 읽기 시작한 프로필과 저장하려는 지금 프로필이 다를 때의 글(완료 화면의 저장 실패 안내에 그대로 쓴다)</summary>
 public const string CollectionProfileChanged="읽기를 시작한 프로필과 지금 프로필이 다릅니다.";
 public CollectionSaveResult ImportCollection(CollectionOutcome outcome,IReadOnlyDictionary<string,IReadOnlyList<string>> charts,CollectionPlan seen,CollectionChoices choices)
 {
  if(Active.Id!=outcome.ProfileId||seen.ProfileId!=outcome.ProfileId)throw new Exception(CollectionProfileChanged);
  var now=CollectionPlan.Make(this,charts,outcome);var differ=CollectionPlan.Differences(seen,now,choices);
  if(differ.Count>0)return new(CollectionSave.Changed,0,0,0,differ,now);
  var changes=now.Changes(choices);if(changes.Count==0)return new(CollectionSave.Nothing,0,0,0,Array.Empty<string>(),now);
  Apply(PlayOps.ImportCollection,(profile,plays)=>{foreach(var change in changes)plays.Add(change.Before==null?new(null,change.After! with{Seq=profile.NextSeq++}):change);});
  return new(CollectionSave.Saved,now.AddCount,now.UpdateCount,now.DeleteCount(choices),Array.Empty<string>(),now);
 }
 /// <summary>지금 프로필에서 그 출처(varchive, collection)의 줄을 모두 지운다(한 작업). 지운 줄의 수를 돌려준다. 다른 출처와 다른 프로필은 그대로다</summary>
 public int DeleteSource(string source)
 {
  if(source is not (PlaySource.VArchive or PlaySource.Collection))throw new ArgumentException("가져온 기록의 출처가 아닙니다.",nameof(source));
  var found=Active.Plays.Where(p=>p.Source==source).ToList();if(found.Count==0)return 0;
  Apply(source==PlaySource.VArchive?PlayOps.DeleteVArchive:PlayOps.DeleteCollection,(_,plays)=>plays.AddRange(found.Select(p=>new PlayChange(p,null))));
  return found.Count;
 }
 /// <summary>마지막 작업 하나를 되돌린다: 그 작업이 바꾼 모든 줄, 넘겨받은 최근 플레이 시각, 다음 순번이 작업 전으로 돌아간다. 작업의 차례(플레이 시각의 차례가 아니다)로 되돌린다</summary>
 public void Undo()
 {
  if(Active.Undo.Count==0)throw new Exception("되돌릴 저장이 없습니다.");
  Transaction(()=>{
   var profile=Active;var step=profile.Undo[^1];
   SetAll(profile,Enumerable.Range(0,step.Plays.Count).Reverse().Select(i=>(step.Plays[i].After,step.Plays[i].Before)).ToList());
   if(step.Recent!=null)foreach(var change in step.Recent){if(change.Before is {} at)profile.Recent[change.Key]=at;else profile.Recent.Remove(change.Key);}
   profile.NextSeq=step.NextSeq;profile.Undo.RemoveAt(profile.Undo.Count-1);
  });
 }
}
