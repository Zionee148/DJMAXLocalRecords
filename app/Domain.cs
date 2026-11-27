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
 public static void Validate(int score){if(score<0||score>1000000)throw new Exception("점수는 0~1,000,000입니다.");}
}
record Score(decimal Accuracy,DateTime At,int? PlayCount=0,DateTime? LastPlayedAt=null,int? RawScore=null)
{
 [System.Text.Json.Serialization.JsonIgnore] public int Value=>RawScore??ScoreValue.FromAccuracy(Accuracy);
}
record Change(string Key,Score? Before,Score? After,List<Change>? Related=null);
class Profile{public string Id{get;set;}=Guid.NewGuid().ToString("N");public string Name{get;set;}="기본";public Dictionary<string,Score> Records{get;set;}=new();public List<Change> Undo{get;set;}=new();public override string ToString()=>Name;}
// 아래 넷(Score, Change, Profile, SavedData)은 형식 1 의 기록 파일의 모양이다. 형식 2(PlayData.cs)가 들어온 뒤로는 옛 파일을 읽어 올릴 때만 쓴다(PlayFormat.Upgrade).
// 제품의 화면은 이 이름들을 쓰지 않는다. 개발용 검사가 판정마다의 최고 기록을 옛 모양으로 읽을 때에만 Score 를 더 쓴다(OldNamesChecks.cs. Debug 빌드에만 있다)
// Format 은 기록 파일의 형식 번호다. 번호가 없는 파일(이 번호를 넣기 전에 쓴 것)은 1로 읽는다. 기록을 읽고 쓰는 방식이 바뀌어 옛 프로그램이 잘못 읽게 될 때만 올린다.
// 이 프로그램이 아는 번호보다 큰 파일은 더 새 버전이 쓴 것이므로 읽지 않고 알린다(이전 버전으로 되돌린 뒤 새 형식의 기록을 조용히 잘못 읽거나 덮어쓰지 않게)
class SavedData{public const int KnownFormat=1;public int Format{get;set;}=1;public string ActiveId{get;set;}="";public List<Profile> Profiles{get;set;}=new();}
record CapturedRecord(Selection Chart,decimal Accuracy,string ProfileId,DateTime? MeasuredAt=null,double? RateAge=null,int? RawScore=null,bool PropagateNormal=true,string? SteamKey=null);
class Session
{
 public bool Running{get;set;}public Selection? Candidate{get;private set;}public Selection? Locked{get;private set;}public CapturedRecord? Pending{get;private set;}public decimal? Current{get;set;}
 double candidateSince,lastObserved;public double StableSeconds{get;private set;}
 public Func<Selection,bool>? Valid{get;set;}
 public void Detect(Selection c){if(Pending!=null)return;if(Valid!=null&&!Valid(c))throw new Exception("존재하는 패턴인지 확인해 주세요.");if(Locked?.Key!=c.Key)Current=null;Candidate=Locked=c;StableSeconds=0;}

 public void Observe(Selection? c,double now){if(Locked!=null||Pending!=null)return;if(c==null){Candidate=null;StableSeconds=0;return;}if(Valid!=null&&!Valid(c)){Candidate=null;StableSeconds=0;return;}if(c!=Candidate||now-lastObserved>2.5){Candidate=c;candidateSince=now;StableSeconds=0;}else{StableSeconds=Math.Max(0,now-candidateSince);if(StableSeconds>=3)Lock();}lastObserved=now;}
 public void Lock(){if(Pending!=null)return;var c=Candidate??throw new Exception("확정할 곡 후보가 없습니다.");if(Valid!=null&&!Valid(c))throw new Exception("존재하는 패턴을 선택해 주세요.");Locked=c;Current=null;}
 public void Next(){if(Pending!=null)throw new Exception("저장 확인에서 ‘저장’이나 ‘취소’를 먼저 눌러 주세요.");Locked=Candidate=null;Current=null;StableSeconds=0;}
 public void Unlock(){if(Pending!=null)throw new Exception("저장 확인에서 ‘저장’이나 ‘취소’를 먼저 눌러 주세요.");Locked=Candidate=null;Current=null;StableSeconds=0;}
 public CapturedRecord Begin(string profile,DateTime? measuredAt=null,double? rateAge=null,decimal? measuredAccuracy=null,int? rawScore=null,bool propagateNormal=true,Selection? chart=null,string? steamKey=null){if(Pending!=null)throw new Exception("이미 저장 확인 중입니다.");var accuracy=measuredAccuracy??Current;if((chart??Locked)==null||accuracy==null)throw new Exception("곡 정보와 현재 RATE가 확인된 뒤에 등록해 주세요.");return Pending=new(chart??Locked!,accuracy.Value,profile,measuredAt??DateTime.UtcNow,rateAge,rawScore,propagateNormal,steamKey);}
 public void Cancel()=>Pending=null;
 /// <summary>저장 확인을 받아 한 판을 저장한다. 저장한 판과 그 직전의 최고 기록을 돌려준다</summary>
 public Registered Confirm(RecordStore store)
 {
  var p=Pending??throw new Exception("저장 확인을 기다리는 기록이 없습니다.");if(p.ProfileId!=store.Active.Id)throw new Exception("프로필이 바뀌었습니다. 취소한 뒤 다시 등록해 주세요.");
  // 읽은 값의 확인: RATE 는 0.00~100.00 이고, SCORE 는 그 RATE 와 맞아야 한다. 제품에서 등록은 언제나 SCORE 로 한다.
  // SCORE 없이 RATE 만으로 등록하는 길은 개발용 검사에서만 닿고, 그때는 100점 단위의 환산 SCORE 로 저장한다
  if(p.Accuracy<0||p.Accuracy>100||decimal.Round(p.Accuracy,2)!=p.Accuracy)throw new Exception("RATE는 0.00~100.00입니다.");
  int score=p.RawScore??ScoreValue.FromAccuracy(p.Accuracy);ScoreValue.Validate(score);
  if(ScoreValue.Rate(score)!=p.Accuracy)throw new Exception("점수와 RATE가 일치하지 않습니다.");
  var result=store.Register(p.Chart,score,p.MeasuredAt??DateTime.UtcNow,!p.PropagateNormal);Pending=null;Next();return result;
 }
}




