namespace LadderRecords;

// 개발용 검사만 쓰는 옛 이름(Debug 빌드에만 있다. 이름이 Checks.cs 로 끝나는 파일은 Release 빌드에 들어가지 않는다).
// 저장층(RecordStore)에는 옛 이름 Get·View·Records·Put·SetManual 이 없다. 제품의 화면과 등록은 판 단위의 부름만 쓴다.
// 저장층만 보는 검사 가운데 많은 수가 "판정마다의 최고 기록"을 옛 모양(Score)으로 읽어 견준다. 그 검사들을 손으로 옮기면 견주는 값이 달라지기 쉬워
// 그 읽는 모양만 여기서 판 단위의 부름(Best·History·Recent·Add)으로 만들어 준다.
// **제품 코드가 이 이름을 쓰면 Release 빌드가 깨진다**(이 파일이 없어서). 그것이 이 이름이 제품으로 되돌아오지 않게 막는 장치다.
// 판정 단위로 한 번에 지우는 DeleteRecord 는 여기에도 없다(그 동작 자체가 없다). 판정을 비우는 검사는 판마다 지우는 Wipe 를 쓴다.
static class OldNames
{
 /// <summary>그 판정의 최고 기록을 옛 모양으로(없으면 null). PlayCount 는 그 판정에 보이는 판의 수, LastPlayedAt 은 '최근 플레이', At 은 최고 기록인 판의 시각(없으면 빈 값)</summary>
 public static Score? Get(this RecordStore store,Selection chart)=>store.View(chart.Key);
 public static Score? View(this RecordStore store,string key,PlayProfile? profile=null)=>store.Best(key,profile) is {} best?new Score(ScoreValue.Rate(best.Score),best.At??default,store.History(key,profile).Count,store.Recent(key,profile),best.Score):null;
 public static IEnumerable<KeyValuePair<string,Score>> Records(this RecordStore store,PlayProfile? profile=null)=>store.Keys(profile).Select(key=>new KeyValuePair<string,Score>(key,store.View(key,profile)!));
 /// <summary>옛 등록: RATE(와 있으면 SCORE)로 한 판을 저장하고 결과의 글을 돌려준다. manual 이면 수동 등록의 판으로, propagateNormal 이 false 면 연습·무효로 저장한다</summary>
 public static string Put(this RecordStore store,Selection c,decimal value,bool manual=false,DateTime? measuredAt=null,int? rawScore=null,bool propagateNormal=true)
 {
  if(value<0||value>100||decimal.Round(value,2)!=value)throw new Exception("정확도는 0.00~100.00입니다.");
  int raw=rawScore??ScoreValue.FromAccuracy(value);ScoreValue.Validate(raw);
  if(rawScore!=null&&ScoreValue.Rate(raw)!=value)throw new Exception("스코어와 정확도가 일치하지 않습니다.");
  var old=store.Best(c.Key);
  store.Add(c,raw,measuredAt??DateTime.UtcNow,manual?PlaySource.Manual:PlaySource.Play,!propagateNormal);
  return old==null?"최초 등록":raw>old.Score?"기록 갱신":"기존 기록 유지";
 }
 /// <summary>옛 수동 저장: 입력한 점수의 한 판을 수동 등록으로 더한다(플레이 카운트는 쓰지 않는다)</summary>
 public static void SetManual(this RecordStore store,Selection c,decimal value,int playCount,DateTime? savedAt=null,int? rawScore=null)
 {
  if(value<0||value>100||decimal.Round(value,2)!=value)throw new Exception("정확도는 0.00~100.00입니다.");if(playCount<0)throw new Exception("플레이 카운트는 0 이상이어야 합니다.");
  int raw=rawScore??ScoreValue.FromAccuracy(value);ScoreValue.Validate(raw);if(ScoreValue.Rate(raw)!=value)throw new Exception("스코어와 정확도가 일치하지 않습니다.");
  store.Add(c,raw,savedAt??DateTime.UtcNow,PlaySource.Manual);
 }
 /// <summary>
 /// 그 판정에 보이는 판을 한 판씩 없앤다: 그 판정의 판은 지우고, 하드·맥스에서 반영되어 온 판은 노말 반영만 끊는다(히스토리 창에서 한 줄씩 지우는 것과 같다).
 /// **작업은 판마다 하나다.** 판정 단위로 한 번에 지우는 작업(DeleteRecord)은 없다
 /// </summary>
 public static void Wipe(this RecordStore store,Selection c){foreach(var play in store.History(c.Key).ToList()){if(play.Key==c.Key)store.Delete(play.Seq);else store.CancelNormal(play.Seq);}}
}
