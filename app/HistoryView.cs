namespace LadderRecords;

// 기록 창들이 점수·시각·출처를 글로 적는 규칙과, 기록 히스토리가 보여 줄 것의 셈.
// 화면을 그리는 코드와 떼어 두어 자체 검사가 그대로 돌린다(app\HistoryViewTests.cs).

static class ScoreText
{
 public static string Points(int score)=>$"{score:N0}점";
 public static string Rate(int score)=>$"{ScoreValue.Rate(score):F2}%";
 /// <summary>점수를 견주는 화면(목록, 히스토리, 삭제 확인)의 표기: 세부 SCORE 를 아는 기록은 '995,421점 (99.54%)', RATE 만 남은 기록은 '995,400점 (99.54% · 환산)'</summary>
 public static string Full(Play play)=>$"{Points(play.Score)} ({Rate(play.Score)}{(PlaySource.Exact(play.Source)?"":" · 환산")})";
 /// <summary>실제로 바뀔 값을 확인하는 화면(수동 입력 중의 변경 안내, 되돌리기 확인)의 표기: 남아 있는 정밀도만 적는다. RATE 만 남은 기록은 '99.54%'</summary>
 public static string Kept(Play play)=>PlaySource.Exact(play.Source)?$"{Points(play.Score)} ({Rate(play.Score)})":Rate(play.Score);
 /// <summary>시각. 세부 SCORE 가 남은 옛 최고 기록의 시각은 플레이한 때가 아니라 저장한 때다</summary>
 public static string Time(Play play)=>play.At is {} at?at.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")+(play.Source==PlaySource.Old?" (저장 시각)":""):"일시 미상";
 /// <summary>그 시각의 이름(확인 창의 항목 이름)</summary>
 public static string TimeName(Play play)=>play.Source==PlaySource.Old&&play.At!=null?"저장 시각":"플레이 시각";
 /// <summary>일시 미상 기록의 줄에 적는 출처. 환산한 점수의 출처에는 환산이라고 적는다(옛 RATE 기록의 이름에는 이미 들어 있다)</summary>
 public static string Source(Play play)=>play.Source==PlaySource.VArchive?PlaySource.Label(play.Source)+" (RATE 환산)":PlaySource.Label(play.Source);
 public static string Judge(string key)=>key[(key.LastIndexOf('|')+1)..];
 /// <summary>노말 판정의 히스토리에 함께 보이는 하드·맥스의 판에 붙는 글</summary>
 public static string Reflected(Play play)=>$"{Judge(play.Key)}판정에서 반영";
 /// <summary>곡 목록에 없는 곡의 이름 자리: '곡 정보 없음 (곡 번호 123)'. 번호는 V-ARCHIVE 의 곡 번호다(기록의 곡 번호 'varchive:123' 에서 앞을 뗀다)</summary>
 public static string MissingSong(string songId){const string prefix="varchive:";string number=songId.StartsWith(prefix,StringComparison.Ordinal)?songId[prefix.Length..]:songId;return $"곡 정보 없음 (곡 번호 {number})";}
}

/// <summary>히스토리 표의 한 줄. Group 이 있으면 'PB · 일시 미상' 줄이다(최고 SCORE 가 일시 미상 기록에만 있을 때 표 맨 아래에 한 줄. 그 SCORE 의 출처들이 Group 이다)</summary>
record HistoryRow(Play Play,bool Tie,bool Pb,bool Reflected,bool Editable,IReadOnlyList<Play>? Group=null);
/// <summary>그래프 아래 '일시 미상 기록'의 한 줄: 같은 SCORE 의 기록들(출처마다 하나)</summary>
record UndatedGroup(int Score,IReadOnlyList<Play> Plays);

/// <summary>한 판정(프로필·곡·버튼·난이도·판정)의 히스토리가 보여 줄 것</summary>
sealed class HistoryModel
{
 public string Key{get;}
 /// <summary>그 판정에 보이는 판 전부(노말에는 하드·맥스에서 반영되어 온 판이 함께 있다)</summary>
 public IReadOnlyList<Play> All{get;}
 /// <summary>최고 기록(출처를 가리지 않는다). 없으면 미등록이다</summary>
 public Play? Best{get;}
 /// <summary>PB 표식을 붙일 한 판(RecordStore.Ahead 의 차례): 최고 SCORE 인 판 가운데 시각이 있는 것 → 가장 늦은 시각 → 시각도 같으면 나중에 등록한 것. 최고 SCORE 가 일시 미상에만 있으면 없다</summary>
 public Play? Pb{get;}
 /// <summary>시각이 있는 판. 오래된 것부터다(그래프의 1회, 2회 …)</summary>
 public IReadOnlyList<Play> Dated{get;}
 /// <summary>일시 미상 기록. SCORE 가 높은 줄부터, 같은 SCORE 는 한 줄에 출처마다 하나</summary>
 public IReadOnlyList<UndatedGroup> Undated{get;}

 public HistoryModel(string key,IEnumerable<Play> plays)
 {
  Key=key;All=plays.ToList();
  foreach(var play in All)if(Best==null||RecordStore.Ahead(play,Best))Best=play;
  Pb=Best is {At:not null}?Best:null;
  Dated=All.Where(p=>p.At!=null).OrderBy(p=>p.At).ThenBy(p=>p.Seq).ToList();
  Undated=All.Where(p=>p.At==null).GroupBy(p=>p.Score).OrderByDescending(g=>g.Key)
   .Select(g=>new UndatedGroup(g.Key,g.OrderBy(p=>Array.IndexOf(SourceOrder,p.Source)).ThenBy(p=>p.Seq).ToList())).ToList();
 }
 public static HistoryModel Read(RecordStore store,string key,PlayProfile? profile=null)=>new(key,store.History(key,profile));
 // 한 줄에 여러 출처를 적는 차례: 컬렉션 → V아카이브 → RATE 만 남은 옛 기록 → 세부 SCORE 가 남은 옛 기록. 같은 출처끼리는 등록 순번의 차례다
 static readonly string[] SourceOrder={PlaySource.Collection,PlaySource.VArchive,PlaySource.OldRate,PlaySource.Old};

 /// <summary>다른 판정에서 반영되어 와서 이 판정에 보이는 판인가(노말에 보이는 하드·맥스의 판)</summary>
 public bool Reflected(Play play)=>play.Key!=Key;
 /// <summary>이 히스토리에서 고칠 수 있는 판인가: 직접 저장한 판과 세부 SCORE 가 남은 옛 기록. 반영되어 온 판은 원래 판정의 히스토리에서 고친다</summary>
 public bool Editable(Play play)=>!Reflected(play)&&PlaySource.Timed(play.Source)&&play.At!=null;
 HistoryRow Row(Play play)=>new(play,Best!=null&&play.Score==Best.Score,Pb!=null&&play.Seq==Pb.Seq,Reflected(play),Editable(play));

 /// <summary>
 /// 표의 줄. 시각이 있는 판만 둔다(일시 미상 기록은 그래프 아래에서 본다). 기본은 최신순이다.
 /// 점수순에서 SCORE 가 같으면 더 늦은 시각의 판, 시각도 같으면 나중에 등록한 판이 앞이다(오름차순에서도 같다). 최고 SCORE 가 일시 미상 기록에만 있으면 어느 정렬에서든 맨 아래에 'PB · 일시 미상' 한 줄을 둔다
 /// </summary>
 public List<HistoryRow> Rows(bool byScore=false,bool descending=true)
 {
  IEnumerable<Play> ordered=byScore
   ?(descending?Dated.OrderByDescending(p=>p.Score):Dated.OrderBy(p=>p.Score)).ThenByDescending(p=>p.At).ThenByDescending(p=>p.Seq)
   :descending?Dated.Reverse():Dated;
  var rows=ordered.Select(Row).ToList();
  if(Best is {At:null}&&Undated.Count>0)rows.Add(new(Undated[0].Plays[0],true,true,false,false,Undated[0].Plays));
  return rows;
 }
 /// <summary>지우면 이 판정에 보이는 판이 하나도 남지 않게 되는 판정들(삭제 확인의 '미등록이 됩니다' 안내). 노말에 반영된 하드·맥스의 원래 판을 지우면 노말에서도 사라지므로 두 판정을 모두 본다</summary>
 public static List<string> Emptied(RecordStore store,Play play,bool onlyUnlink)
 {
  var judges=new List<string>();
  string? normal=play.Normal==NormalLink.Linked?RecordStore.NormalKey(play.Key):null;
  if(!onlyUnlink&&store.History(play.Key).Count==1)judges.Add(ScoreText.Judge(play.Key));
  if(normal!=null&&store.History(normal).Count==1)judges.Add(ScoreText.Judge(normal));
  return judges;
 }
}
