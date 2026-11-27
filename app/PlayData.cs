using System.Text.Json.Serialization;
namespace LadderRecords;

// 기록 파일의 형식 2: 저장한 판을 한 줄씩 둔다.
// 형식 1(Domain.cs 의 SavedData)은 곡·버튼·패턴·판정마다 최고 기록 하나와 플레이 카운트였다. 형식 1 의 파일은 읽을 때 메모리에서 형식 2 로 올린다(PlayFormat.Upgrade).
// 이 파일에는 모양과 그 확인(PlayFormat)만 둔다. 파일의 읽기·쓰기, 판을 더하고 고치고 지우는 일과 되돌리기는 RecordStore 가 한다.
// [JsonRequired] 를 붙인 칸은 파일에 반드시 있어야 한다: 빠진 칸을 기본값(0점, 빈 목록)으로 채워 읽지 않는다

/// <summary>한 판이 어디서 왔는가</summary>
static class PlaySource
{
 /// <summary>play 자동 인식 뒤 확인해 저장한 판 · manual 수동으로 더한 판 · old 세부 SCORE 가 남은 옛 최고 기록(At 은 저장 시각이다) · oldrate RATE 만 남은 옛 기록 ·
 /// varchive V아카이브에서 가져온 최고 기록 · collection 컬렉션에서 가져온 최고 기록</summary>
 public const string Play="play",Manual="manual",Old="old",OldRate="oldrate",VArchive="varchive",Collection="collection";
 public static readonly string[] All={Play,Manual,Old,OldRate,VArchive,Collection};
 /// <summary>세부 SCORE 를 아는 출처인가(아니면 RATE 를 100점 단위로 환산한 값이다)</summary>
 public static bool Exact(string source)=>source is Play or Manual or Old or Collection;
 /// <summary>이 형식이 들어온 뒤에 사용자가 직접 저장한 판인가(최근 한 판 교체의 대상, 최근 플레이 시각의 근거)</summary>
 public static bool Direct(string source)=>source is Play or Manual;
 /// <summary>시각이 있는 출처인가. 가져온 최고 기록과 RATE 만 남은 옛 기록은 일시 미상이다</summary>
 public static bool Timed(string source)=>source is Play or Manual or Old;
 /// <summary>
 /// 히스토리가 처음 들어간 배포 버전. 옛 기록의 출처 표기('v1.2.0 이전 기록')에 들어가고, 뒤의 버전에서도 바꾸지 않는다.
 /// **실제로 내는 버전이 이 값과 다르면 릴리즈 전에 고친다**
 /// </summary>
 public const string FirstVersion="v1.2.0";
 /// <summary>일시 미상 기록의 출처 표기. 시각이 있는 판에는 쓰지 않는다</summary>
 public static string Label(string source)=>source switch{VArchive=>"V아카이브",Collection=>"컬렉션",OldRate=>$"{FirstVersion} 이전 기록 (RATE 환산)",Old=>$"{FirstVersion} 이전 기록",Manual=>"수동 등록",_=>"플레이"};
}
/// <summary>하드·맥스의 한 판이 노말 판정에도 보이는가. 값이 없으면 보이지 않는다</summary>
static class NormalLink
{
 /// <summary>linked 노말 히스토리에도 보인다("하드판정에서 반영") · cancelled 사용자가 노말 반영을 껐다(다시 켜지지 않는다)</summary>
 public const string Linked="linked",Cancelled="cancelled";
}
/// <summary>
/// 저장한 한 판. Seq: 프로필 안의 등록 순번(한 번 준 번호는 다시 쓰지 않고 그 판을 고쳐도 바뀌지 않는다. 시각이 같을 때의 차례를 정한다).
/// Key: 곡|버튼|패턴|원래 판정(형식 1 과 같은 글자). Score: 세부 SCORE(0~1,000,000. RATE 는 여기서 셈한다). At: 플레이 시각(UTC. 없으면 일시 미상).
/// Normal: 하드·맥스의 판에만(NormalLink). Practice: 연습 모드·무효 매치로 저장한 판
/// </summary>
record Play([property:JsonRequired]int Seq,[property:JsonRequired]string Key,[property:JsonRequired]int Score,DateTime? At,[property:JsonRequired]string Source,string? Normal=null,[property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingDefault)]bool Practice=false);
/// <summary>되돌리기의 한 줄: 그 판의 바뀌기 전과 뒤(없던 판이면 Before 가, 지운 판이면 After 가 없다)</summary>
record PlayChange(Play? Before,Play? After);
/// <summary>되돌리기의 한 줄: 넘겨받은 최근 플레이 시각의 바뀌기 전과 뒤</summary>
record RecentChange([property:JsonRequired]string Key,DateTime? Before,DateTime? After);
/// <summary>한 번에 확정한 변경(되돌리기의 한 단계). Kind: 추가·교체·수정·삭제 등 작업의 종류. NextSeq: 그 작업을 하기 전의 다음 순번</summary>
record Operation([property:JsonRequired]string Kind,[property:JsonRequired]DateTime At,[property:JsonRequired]int NextSeq,[property:JsonRequired]List<PlayChange> Plays,List<RecentChange>? Recent=null);
/// <summary>Recent: 형식 1 에서 넘겨받은 최근 플레이 시각(판의 시각으로 쓰지 않는다. 목록의 '최근 플레이' 열이 이어 쓴다). Undo: 작업 단위의 되돌리기(형식 1 의 것은 넘기지 않는다)</summary>
class PlayProfile
{
 [JsonRequired]public string Id{get;set;}=Guid.NewGuid().ToString("N");[JsonRequired]public string Name{get;set;}="기본";
 [JsonRequired]public int NextSeq{get;set;}=1;[JsonRequired]public List<Play> Plays{get;set;}=new();[JsonRequired]public Dictionary<string,DateTime> Recent{get;set;}=new();[JsonRequired]public List<Operation> Undo{get;set;}=new();
 public override string ToString()=>Name;
}
class PlayData{public const int KnownFormat=2;[JsonRequired]public int Format{get;set;}=KnownFormat;[JsonRequired]public string ActiveId{get;set;}="";[JsonRequired]public List<PlayProfile> Profiles{get;set;}=new();}

static class PlayFormat
{
 public static readonly string[] Judges={"노말","하드","맥스"};
 /// <summary>
 /// 형식 1 의 기록을 형식 2 로 올린다. 같은 내용은 언제 올려도 같은 결과가 나온다(순번을 저장 시각 → 키의 차례로 준다). 그래서 다시 해도 줄이 겹치지 않는다.
 ///  · 세부 SCORE 칸이 차 있으면(100점 단위로 끝나도) old: 그 값과 저장 시각 · 비어 있으면 oldrate: RATE 의 환산값, 일시 미상
 ///  · 최근 플레이 시각은 Recent 로(판의 시각으로 쓰지 않는다) · 플레이 카운트와 되돌리기 내역은 넘기지 않는다(원본 사본에 남는다)
 ///  · 옛 "하드·맥스 → 노말 반영"의 두 기록은 각각 한 줄이다. 잇지 않는다(같은 판이라는 표시가 옛 파일에 없다)
 /// 있어야 하는 칸이 빠진 옛 파일은 여기에 오기 전에 걸러진다(RecordStore 가 파일의 모양을 먼저 본다). 범위 밖의 값은 바꾸지 않고 그대로 옮겨 Problem 이 걸러 내게 한다
 /// </summary>
 public static PlayData Upgrade(SavedData old)
 {
  var data=new PlayData{ActiveId=old.ActiveId};
  foreach(var profile in old.Profiles)
  {
   var next=new PlayProfile{Id=profile.Id,Name=profile.Name};
   foreach(var (key,score) in profile.Records.OrderBy(r=>r.Value.At).ThenBy(r=>r.Key,StringComparer.Ordinal))
   {
    bool exact=score.RawScore!=null;
    // 범위 밖의 RATE 는 환산할 수 없다. 범위 밖의 값으로 두어 Problem 이 "읽을 수 없는 기록"으로 걸러 내게 한다(고쳐 읽지 않는다)
    int value=exact?score.RawScore!.Value:score.Accuracy is >=0 and <=100?ScoreValue.FromAccuracy(score.Accuracy):-1;
    next.Plays.Add(new(next.NextSeq++,key,value,exact?score.At:null,exact?PlaySource.Old:PlaySource.OldRate));
    if(score.LastPlayedAt is {} played)next.Recent[key]=played;
   }
   data.Profiles.Add(next);
  }
  return data;
 }

 /// <summary>키(곡|버튼|패턴|판정)의 판정. 그렇게 나눌 수 없는 키면 null. 버튼과 패턴의 값은 정해 두지 않는다(게임에 새 버튼·패턴이 생겨도, 그 기록이 든 파일을
 /// 이전 버전이 통째로 거절하지 않게). 판정만 정해진 셋 가운데 하나여야 한다(노말 반영의 규칙이 판정에 걸려 있다)</summary>
 public static string? JudgeOf(string? key)
 {
  if(string.IsNullOrEmpty(key))return null;
  string[] parts=key.Split('|');
  if(parts.Length<4||parts[..^3].All(string.IsNullOrEmpty)||!int.TryParse(parts[^3],out int button)||button<=0||parts[^2].Length==0)return null;
  return Judges.Contains(parts[^1])?parts[^1]:null;
 }
 /// <summary>한 판의 칸들이 함께 쓸 수 있는 값인가. 아니면 무엇이 틀렸는지</summary>
 public static string? Problem(Play? play)
 {
  if(play==null)return "빈 판이 있다";
  if(JudgeOf(play.Key) is not {} judge)return $"곡·버튼·패턴·판정으로 나눌 수 없는 키가 있다({play.Key})";
  if(play.Seq<1)return $"순번이 1 보다 작다({play.Seq})";
  if(play.Score<0||play.Score>1000000)return $"범위 밖의 SCORE 가 있다({play.Key})";
  if(!PlaySource.All.Contains(play.Source))return $"모르는 출처가 있다({play.Source})";
  // 출처마다의 시각과 점수: 직접 저장한 판과 세부 SCORE 가 남은 옛 기록은 시각이 있고, RATE 환산과 가져온 최고 기록은 일시 미상이다. RATE 환산은 100점 단위다
  if(PlaySource.Timed(play.Source)!=(play.At!=null))return $"출처({play.Source})와 시각이 맞지 않는 판이 있다({play.Key})";
  if(!PlaySource.Exact(play.Source)&&play.Score%100!=0)return $"RATE 를 환산한 출처({play.Source})인데 100점 단위가 아닌 판이 있다({play.Key})";
  if(play.Source is PlaySource.VArchive or PlaySource.Collection&&judge!="노말")return $"가져온 최고 기록이 노말 판정이 아니다({play.Key})";
  // 노말 반영과 연습: 하드·맥스에서 직접 저장한 판에만 있다. 연습·무효로 저장한 판은 노말에 반영되지 않는다
  if(play.Normal is not (null or NormalLink.Linked or NormalLink.Cancelled))return $"모르는 노말 반영 값이 있다({play.Normal})";
  if((play.Normal!=null||play.Practice)&&(judge=="노말"||!PlaySource.Direct(play.Source)))return $"노말 반영이나 연습 표시가 붙을 수 없는 판에 붙어 있다({play.Key})";
  if(play.Practice&&play.Normal!=null)return $"연습으로 저장한 판에 노말 반영이 붙어 있다({play.Key})";
  return null;
 }
 /// <summary>쓸 수 있는 모양의 형식 2 인가. 아니면 무엇이 틀렸는지(진단용 글). 틀린 것을 고쳐 읽지 않는다: 읽을 수 없는 기록으로 다룬다. 저장하기 전에도 같은 확인을 한다</summary>
 public static string? Problem(PlayData data)
 {
  if(data.Format!=PlayData.KnownFormat)return $"형식 번호가 {PlayData.KnownFormat} 이 아니다({data.Format})";
  if(data.Profiles==null||data.Profiles.Count==0)return "프로필이 없다";
  if(data.Profiles.Any(p=>p==null||string.IsNullOrEmpty(p.Id)||p.Name==null))return "프로필의 이름이나 번호가 비었다";
  if(data.Profiles.Select(p=>p.Id).Distinct().Count()!=data.Profiles.Count)return "프로필의 번호가 겹친다";
  if(data.Profiles.All(p=>p.Id!=data.ActiveId))return "고른 프로필이 없다";
  foreach(var profile in data.Profiles)
  {
   string where=$"프로필 '{profile.Name}': ";
   if(profile.Plays==null||profile.Recent==null||profile.Undo==null)return where+"칸이 비었다";
   if(profile.NextSeq<1)return where+$"다음 순번이 1 보다 작다({profile.NextSeq})";
   var seen=new HashSet<int>();var single=new HashSet<string>();
   foreach(var play in profile.Plays)
   {
    if(Problem(play) is {} wrong)return where+wrong;
    if(play.Seq>=profile.NextSeq||!seen.Add(play.Seq))return where+$"순번이 맞지 않는다({play.Seq})";
    // 한 패턴·판정에 한 줄만 있어야 하는 것: V아카이브의 최고 기록, 컬렉션의 최고 기록, 옛 최고 기록(세부 SCORE 가 있든 없든 하나였다)
    string? kind=play.Source switch{PlaySource.VArchive=>"v",PlaySource.Collection=>"c",PlaySource.Old or PlaySource.OldRate=>"o",_=>null};
    if(kind!=null&&!single.Add(kind+play.Key))return where+$"한 줄만 있어야 하는 출처({play.Source})의 판이 둘이다({play.Key})";
   }
   if(profile.Recent.Keys.Any(key=>JudgeOf(key)==null))return where+"넘겨받은 최근 플레이 시각의 키가 맞지 않는다";
   foreach(var step in profile.Undo)
   {
    if(step==null||string.IsNullOrEmpty(step.Kind)||step.Plays==null||step.NextSeq<1)return where+"되돌리기 항목이 비었다";
    foreach(var change in step.Plays)
    {
     if(change==null||change.Before==null&&change.After==null)return where+"되돌리기에 빈 줄이 있다";
     foreach(var play in new[]{change.Before,change.After})if(play!=null&&Problem(play) is {} wrong)return where+"되돌리기 안의 판: "+wrong;
    }
    if(step.Recent!=null&&step.Recent.Any(recent=>recent==null||JudgeOf(recent.Key)==null))return where+"되돌리기 안의 최근 플레이 시각이 맞지 않는다";
   }
  }
  return null;
 }
}
