using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
namespace LadderRecords;
record Chart(int Button,string Pattern);
record Song(string Id,int SourceId,string Name,string Artist,string Pack,List<Chart> Charts){public override string ToString()=>$"{Name} — {Artist}";}
record Catalog(string Source,DateTime RetrievedAt,List<Song> Songs);
record Selection(string SongId,int Button,string Pattern,string Judge,int Round){public string Key=>$"{SongId}|{Button}|{Pattern}|{Judge}";}
static class ScoreValue
{
 public static int FromAccuracy(decimal rate)=>checked((int)(decimal.Truncate(rate*100)*100));
 public static decimal Rate(int score)=>decimal.Truncate(score/100m)/100m;
 public static void Validate(int score){if(score<0||score>1000000)throw new Exception("스코어는 0~1,000,000입니다.");}
}
record Score(decimal Accuracy,DateTime At,int? PlayCount=0,DateTime? LastPlayedAt=null,int? RawScore=null)
{
 [System.Text.Json.Serialization.JsonIgnore] public int Value=>RawScore??ScoreValue.FromAccuracy(Accuracy);
}
record Change(string Key,Score? Before,Score? After,List<Change>? Related=null);
class Profile{public string Id{get;set;}=Guid.NewGuid().ToString("N");public string Name{get;set;}="기본";public Dictionary<string,Score> Records{get;set;}=new();public List<Change> Undo{get;set;}=new();public override string ToString()=>Name;}
// Format 은 기록 파일의 형식 번호다. 번호가 없는 파일(이 번호를 넣기 전에 쓴 것)은 1로 읽는다. 기록을 읽고 쓰는 방식이 바뀌어 옛 프로그램이 잘못 읽게 될 때만 올린다.
// 이 프로그램이 아는 번호보다 큰 파일은 더 새 버전이 쓴 것이므로 읽지 않고 알린다(이전 버전으로 되돌린 뒤 새 형식의 기록을 조용히 잘못 읽거나 덮어쓰지 않게)
class SavedData{public const int KnownFormat=1;public int Format{get;set;}=1;public string ActiveId{get;set;}="";public List<Profile> Profiles{get;set;}=new();}
// 저장을 잠근 동안(자동 업데이트가 진행되는 동안) 저장하려 했음을 알린다. 저장에 실패한 것으로 다뤄져 메모리의 기록도 되돌려진다
class RecordStoreFrozenException():IOException("업데이트를 진행하는 중이라 저장하지 않았습니다.");
// 기록 파일이 프로그램 밖에서 바뀌었거나 사라져서 저장하지 않았음을 알린다
class RecordFileChangedException(string message):IOException(message);
class RecordStore
{
 static readonly JsonSerializerOptions SaveOptions=new(){WriteIndented=true};
 readonly string path;public SavedData Data{get;private set;}public Profile Active=>Data.Profiles.Single(p=>p.Id==Data.ActiveId);public long Revision{get;private set;}
 // 마지막으로 읽었거나 쓴 기록 파일의 내용(SHA-256). 파일 없이 시작했으면 null 이다. 저장하기 직전에 디스크의 파일과 견줘, 프로그램 밖에서 바뀐 기록을 덮어쓰지 않는다.
 // 시각이 아니라 내용으로 견준다(동기화 도구나 백신이 시각만 건드려도 저장이 막히지 않게)
 byte[]? seen;
 public RecordStore(string path)
 {
  this.path=path;
  if(File.Exists(path)){
   // 한 번만 읽어서, 견줄 내용과 풀어 쓸 내용이 같은 것이게 한다. 글자로 바꾸는 방법은 File.ReadAllText 와 같다
   byte[] bytes=File.ReadAllBytes(path);seen=SHA256.HashData(bytes);
   using var reader=new StreamReader(new MemoryStream(bytes));
   Data=JsonSerializer.Deserialize<SavedData>(reader.ReadToEnd())??throw new InvalidDataException("기록 파일이 비었습니다.");
   if(Data.Format>SavedData.KnownFormat)throw new InvalidDataException(Newer(path,Data.Format));
   if(Data.Profiles.Count==0||Data.Profiles.All(p=>p.Id!=Data.ActiveId))throw new InvalidDataException("기록 프로필을 확인하세요.");
  }else{
   // 본 파일이 없는데 백업이나 저장하다 만 파일이 남아 있으면 처음 실행이 아니다. 빈 기록으로 시작해 저장하면 남은 파일까지 덮이므로 시작하지 않고 알린다.
   // 어느 파일을 되살릴지는 사용자가 정한다(스스로 되살리지 않는다)
   var left=new[]{".bak",".tmp"}.Where(end=>File.Exists(path+end)).ToList();
   if(left.Count>0)throw new InvalidDataException(Missing(path,left));
   var p=new Profile();Data=new(){ActiveId=p.Id,Profiles=new(){p}};
  }
 }
 static string Missing(string path,List<string> left)
 {
  string name=Path.GetFileName(path);bool backup=left.Contains(".bak"),temporary=left.Contains(".tmp");
  var lines=new List<string>{
   $"기록 파일({name})이 없는데 {(backup?$"백업 파일({name}.bak)":"")}{(backup&&temporary?"과 ":"")}{(temporary?$"저장하다 만 파일({name}.tmp)":"")}이 남아 있어 시작하지 않았습니다.",
   "이대로 빈 기록으로 시작해 저장하면 남은 파일이 덮여 이전 기록을 잃을 수 있습니다.",
   ""};
  if(backup)lines.Add($"· 이전 기록을 되살리려면: {name}.bak 을 복사하고, 사본의 이름을 {name} 으로 바꾼 뒤 다시 실행하세요. (백업은 마지막 저장보다 한 단계 앞의 기록입니다)");
  if(temporary)lines.Add($"· {name}.tmp 는 저장하다 끊긴 파일이라 내용이 온전하지 않을 수 있습니다.");
  lines.Add("· 빈 기록으로 새로 시작하려면: 남은 파일을 다른 폴더로 옮긴 뒤 다시 실행하세요.");
  lines.Add("");lines.Add("폴더: "+Path.GetDirectoryName(Path.GetFullPath(path)));
  return string.Join("\n",lines);
 }
 static string Newer(string path,int format)=>string.Join("\n",
  $"기록 파일({Path.GetFileName(path)})이 이 프로그램보다 새 버전에서 저장된 형식(형식 번호 {format})이라 시작하지 않았습니다.",
  "기록 파일은 건드리지 않았습니다.",
  "",
  "· 새 버전으로 다시 업데이트하면 이 기록을 그대로 쓸 수 있습니다.",
  "· 이 버전을 계속 쓰려면: 업데이트하기 전의 기록 사본이 프로그램 폴더의 update\\backup 아래 data 폴더에 있습니다. 지금의 기록 파일은 지우지 말고 다른 곳에 옮겨 둔 뒤, 그 사본을 프로그램 폴더에 복사하세요(업데이트한 뒤에 쌓인 기록은 그 사본에 없습니다).",
  "· 자세한 안내: "+UpdateBackup.GuidePage,
  "",
  "폴더: "+Path.GetDirectoryName(Path.GetFullPath(path)));
 /// <summary>true 인 동안 Save 가 아무것도 쓰지 않고 실패한다(자동 업데이트가 기록 사본을 만든 뒤 프로그램을 바꾸는 동안).</summary>
 public bool Frozen{get;set;}
 public void Save()
 {
  if(Frozen)throw new RecordStoreFrozenException();
  string tmp=path+".tmp";
  using(var stream=File.Create(tmp))JsonSerializer.Serialize(stream,Data,SaveOptions);
  byte[] written;using(var stream=File.OpenRead(tmp))written=SHA256.HashData(stream);
  // 본 파일을 다른 쪽이 고치거나 바꿔 넣지 못하게 잡은 채로 읽어서 견주고, 그 읽은 내용을 백업으로 쓴다. 놓자마자 교체한다(놓는 것과 교체 사이의 틈은 남는다)
  using(var held=Hold()){
   byte[]? disk=null;if(held!=null){disk=new byte[held.Length];held.ReadExactly(disk);}
   bool same=seen==null?disk==null:disk!=null&&SHA256.HashData(disk).AsSpan().SequenceEqual(seen);
   if(!same){
    // 밖에서 바뀐 본 파일과 지금의 백업은 건드리지 않는다. 받아들이지 않은 내용이 임시 파일로 남지 않게 지운다
    try{File.Delete(tmp);}catch(IOException){}catch(UnauthorizedAccessException){}
    throw new RecordFileChangedException(disk==null?"기록 파일이 사라져서 저장하지 않았습니다. 프로그램을 다시 켜서 확인해 주세요.":"기록 파일이 프로그램 밖에서 바뀌어서 저장하지 않았습니다. 프로그램을 다시 켜 주세요.");
   }
   if(disk!=null)File.WriteAllBytes(path+".bak",disk);
  }
  File.Move(tmp,path,true);seen=written;Revision++;
 }
 FileStream? Hold(){try{return new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);}catch(FileNotFoundException){return null;}catch(DirectoryNotFoundException){return null;}}
 internal SavedData Snapshot()=>new SavedData{Format=Data.Format,ActiveId=Data.ActiveId,Profiles=Data.Profiles.Select(p=>new Profile{Id=p.Id,Name=p.Name,Records=new(p.Records),Undo=new(p.Undo)}).ToList()};
 void Transaction(Action edit){var before=Snapshot();try{edit();Save();}catch{Data=before;throw;}}
 public void Select(string id)=>Transaction(()=>{if(!Data.Profiles.Any(p=>p.Id==id))throw new Exception("프로필 없음");Data.ActiveId=id;});
 void NameCheck(string name,bool rename=false){if(string.IsNullOrWhiteSpace(name)||name.Trim().Length>40)throw new Exception("이름은 1~40자입니다.");if(Data.Profiles.Any(p=>(!rename||p.Id!=Active.Id)&&p.Name.Equals(name.Trim(),StringComparison.OrdinalIgnoreCase)))throw new Exception("중복 이름입니다.");}
 public void Create(string name,bool copy){NameCheck(name);Transaction(()=>{var p=new Profile(){Name=name.Trim(),Records=copy?new(Active.Records):new()};Data.Profiles.Add(p);Data.ActiveId=p.Id;});}
 public void Rename(string name){NameCheck(name,true);Transaction(()=>Active.Name=name.Trim());}
 public void Delete(){if(Data.Profiles.Count<2)throw new Exception("마지막 프로필은 삭제할 수 없습니다.");Transaction(()=>{Data.Profiles.Remove(Active);Data.ActiveId=Data.Profiles[0].Id;});}
 public Score? Get(Selection c)=>Active.Records.GetValueOrDefault(c.Key);
 void Remember(Change change)
 {
  Active.Undo.Add(change);
  if(Active.Undo.Count>20)Active.Undo.RemoveAt(0);
 }
 public string Put(Selection c,decimal value,bool manual=false,DateTime? measuredAt=null,int? rawScore=null,bool propagateNormal=true)
 {
  if(value<0||value>100||decimal.Round(value,2)!=value)throw new Exception("정확도는 0.00~100.00입니다.");
  int raw=rawScore??ScoreValue.FromAccuracy(value);ScoreValue.Validate(raw);
  if(rawScore!=null&&ScoreValue.Rate(raw)!=value)throw new Exception("스코어와 정확도가 일치하지 않습니다.");
  var old=Get(c);
  bool keep=!manual&&old!=null&&raw<=old.Value;
  int? count=manual?(old==null?0:old.PlayCount):checked((old?.PlayCount??0)+1);
  var now=DateTime.UtcNow;
  var playedAt=manual?old?.LastPlayedAt:measuredAt??now;
  var next=keep?old! with{PlayCount=count,LastPlayedAt=playedAt}:new Score(value,now,count,playedAt,raw);
  var change=new Change(c.Key,old,next);
  if(!manual&&propagateNormal&&c.Judge is "하드" or "맥스"){
   var normal=c with{Judge="노말"};
   var oldNormal=Get(normal);
   if(oldNormal==null||raw>oldNormal.Value){
    var nextNormal=new Score(value,now,checked((oldNormal?.PlayCount??0)+1),playedAt,raw);
    change=change with{Related=new(){new(normal.Key,oldNormal,nextNormal)}};
   }
  }
  Apply(change);
  return keep?"기존 기록 유지":old==null?"최초 등록":"기록 갱신";
 }
 public List<Change> PreviewImport(IReadOnlyDictionary<Selection,decimal> scores)
 {
  var changes=new List<Change>();
  var now=DateTime.UtcNow;
  foreach(var (chart,value) in scores){
   var old=Get(chart);
   if(old!=null&&value<=old.Accuracy)continue;
   changes.Add(new(chart.Key,old,new Score(value,now,old?.PlayCount,old?.LastPlayedAt)));
  }
  return changes;
 }
 public int Import(IReadOnlyDictionary<Selection,decimal> scores)
 {
  var changes=PreviewImport(scores);
  if(changes.Count!=0)Apply(changes[0] with{Related=changes.Skip(1).ToList()});
  return changes.Count;
 }
 void Apply(Change change)=>Transaction(()=>{
  Set(change.Key,change.After);
  if(change.Related!=null)foreach(var item in change.Related)Set(item.Key,item.After);
  Remember(change);
 });
 void Set(string key,Score? score){if(score==null)Active.Records.Remove(key);else Active.Records[key]=score;}
 public void SetManual(Selection c,decimal value,int playCount,DateTime? savedAt=null,int? rawScore=null){if(value<0||value>100||decimal.Round(value,2)!=value)throw new Exception("정확도는 0.00~100.00입니다.");if(playCount<0)throw new Exception("플레이 카운트는 0 이상이어야 합니다.");int raw=rawScore??ScoreValue.FromAccuracy(value);ScoreValue.Validate(raw);if(ScoreValue.Rate(raw)!=value)throw new Exception("스코어와 정확도가 일치하지 않습니다。");var old=Get(c);var now=savedAt??DateTime.UtcNow;var next=new Score(value,old!=null&&old.Value==raw?old.At:now,playCount,now,raw);Transaction(()=>{Active.Records[c.Key]=next;Active.Undo.Add(new(c.Key,old,next));if(Active.Undo.Count>20)Active.Undo.RemoveAt(0);});}
 public void DeleteRecord(Selection c){var old=Get(c)??throw new Exception("삭제할 기록이 없습니다.");Transaction(()=>{Active.Records.Remove(c.Key);Active.Undo.Add(new(c.Key,old,null));if(Active.Undo.Count>20)Active.Undo.RemoveAt(0);});}
 public void Undo(){if(Active.Undo.Count==0)throw new Exception("되돌릴 저장이 없습니다.");Transaction(()=>{var item=Active.Undo[^1];Set(item.Key,item.Before);if(item.Related!=null)foreach(var related in item.Related)Set(related.Key,related.Before);Active.Undo.RemoveAt(Active.Undo.Count-1);});}
}
record CapturedRecord(Selection Chart,decimal Accuracy,string ProfileId,DateTime? MeasuredAt=null,double? RateAge=null,int? RawScore=null,bool PropagateNormal=true,string? SteamKey=null);
class Session
{
 public bool Running{get;set;}public Selection? Candidate{get;private set;}public Selection? Locked{get;private set;}public CapturedRecord? Pending{get;private set;}public decimal? Current{get;set;}
 double candidateSince,lastObserved;public double StableSeconds{get;private set;}
 public Func<Selection,bool>? Valid{get;set;}
 public void Detect(Selection c){if(Pending!=null)return;if(Valid!=null&&!Valid(c))throw new Exception("존재하는 채보를 확인하세요.");if(Locked?.Key!=c.Key)Current=null;Candidate=Locked=c;StableSeconds=0;}

 public void Observe(Selection? c,double now){if(Locked!=null||Pending!=null)return;if(c==null){Candidate=null;StableSeconds=0;return;}if(Valid!=null&&!Valid(c)){Candidate=null;StableSeconds=0;return;}if(c!=Candidate||now-lastObserved>2.5){Candidate=c;candidateSince=now;StableSeconds=0;}else{StableSeconds=Math.Max(0,now-candidateSince);if(StableSeconds>=3)Lock();}lastObserved=now;}
 public void Lock(){if(Pending!=null)return;var c=Candidate??throw new Exception("확정할 곡 후보가 없습니다.");if(Valid!=null&&!Valid(c))throw new Exception("존재하는 채보를 선택하세요.");Locked=c;Current=null;}
 public void Next(){if(Pending!=null)throw new Exception("저장 확인 또는 취소를 먼저 선택하세요.");Locked=Candidate=null;Current=null;StableSeconds=0;}
 public void Unlock(){if(Pending!=null)throw new Exception("저장 확인 또는 취소를 먼저 선택하세요.");Locked=Candidate=null;Current=null;StableSeconds=0;}
 public CapturedRecord Begin(string profile,DateTime? measuredAt=null,double? rateAge=null,decimal? measuredAccuracy=null,int? rawScore=null,bool propagateNormal=true,Selection? chart=null,string? steamKey=null){if(Pending!=null)throw new Exception("이미 저장 확인 중입니다.");var accuracy=measuredAccuracy??Current;if((chart??Locked)==null||accuracy==null)throw new Exception("곡 고정과 현재 정확도 인식을 확인하세요.");return Pending=new(chart??Locked!,accuracy.Value,profile,measuredAt??DateTime.UtcNow,rateAge,rawScore,propagateNormal,steamKey);}
 public void Cancel()=>Pending=null;
 public string Confirm(RecordStore store){var p=Pending??throw new Exception("저장 대기 없음");if(p.ProfileId!=store.Active.Id)throw new Exception("프로필이 바뀌었습니다. 취소 후 다시 등록하세요.");string result=store.Put(p.Chart,p.Accuracy,measuredAt:p.MeasuredAt,rawScore:p.RawScore,propagateNormal:p.PropagateNormal);Pending=null;Next();return result;}
}




