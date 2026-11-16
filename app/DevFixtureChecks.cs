using System.Text.Json;
namespace LadderRecords;

// 검사를 돌릴 수 없다(자료가 없거나 환경이 맞지 않는다). 검사가 틀린 것을 찾은 "실패"와 구분한다.
// 실행 도구(tools\run_dev_checks.py)가 test-error.txt 의 첫 줄에서 이 이름을 보고 "실행 불가"로 가른다.
class CheckUnavailableException(string message):Exception(message);

static class CheckPorts
{
 // 시험용 주 화면을 만든다. 그 포트를 다른 프로그램이 이미 쓰고 있어서 못 만든 것만 "실행 불가"로 알린다(환경 탓이다). 그 밖의 오류는 그대로 낸다(검사가 찾아야 할 것일 수 있다).
 // 리뷰 docs\reviews\20261116-개발용검사정리-후속변경-리뷰.md 1번: 전에는 오버레이 검사만 갈랐고 단축키 흐름·기록 창 검사는 "실패"로 나왔다
 public static T Open<T>(int port,Func<T> create)
 {
  try{return create();}
  catch(System.Net.Sockets.SocketException e)when(e.SocketErrorCode==System.Net.Sockets.SocketError.AddressAlreadyInUse)
  {throw new CheckUnavailableException($"이 검사가 쓰는 포트 {port} 를 다른 프로그램이 이미 쓰고 있다. 다른 개발용 검사나 사람용 도구(--overlay-check, --hotkey-probe)가 떠 있으면 끝낸 뒤 다시 돌린다.");}
 }
}

// 개발용 검사에 쓰는 합성 자료를 만든다(2026-11-16, 기획서 docs\plans\20261116-개발용검사정리-기획서.md 3절).
// 개인 기록·설정을 읽지 않는다. 곡 목록(data\catalog.json)만 읽어 정해진 규칙으로 만들기 때문에 어느 PC 에서 만들어도 같은 내용이다.
//
// 실행: DJMAXLocalRecords.exe --dev-fixtures     (개인 기록이 없는 새 시험 폴더에 복사한 Debug 빌드에서)
// 만드는 것(실행 파일 옆):
//   records.json                    합성 기록 3,200개(프로필 하나). --records-modal-check, --store-optimization-check 가 읽는다
//   dev-fixtures\varchive\button-4.json, button-5.json, button-6.json, button-8.json
//                                   V아카이브 응답 형식을 따른 합성 표본. --release-feature-check 가 읽는다
// 실행 파일 옆에 records.json 이 이미 있으면 만들지 않고 멈춘다(남의 기록을 덮지 않는다).
static class DevFixtureChecks
{
 public const int RecordCount=3200;
 public const string ProfileName="합성 기록(개발용 검사)";
 public static string VArchiveFolder=>Path.Combine(AppContext.BaseDirectory,"dev-fixtures","varchive");

 public static void Run(Catalog catalog)
 {
  string records=Path.Combine(AppContext.BaseDirectory,"records.json");
  foreach(string end in new[]{"",".bak",".tmp"})
   if(File.Exists(records+end))throw new CheckUnavailableException($"실행 파일 옆에 {Path.GetFileName(records)+end} 이 이미 있다. 합성 자료는 기록이 없는 새 시험 폴더에서만 만든다.");
  int written=WriteRecords(catalog,records);
  var counts=WriteVArchive(catalog,VArchiveFolder);
  File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"dev-fixtures","made.json"),JsonSerializer.Serialize(new{records=written,profile=ProfileName,varchive=counts},new JsonSerializerOptions{WriteIndented=true}));
 }

 // 0 이상 1 미만의 정해진 수열(같은 번호에는 언제나 같은 값). 난수를 쓰지 않는다
 static double Unit(int index,int salt){unchecked{uint x=(uint)(index*2654435761u)^(uint)(salt*40503u+0x9E3779B9u);x^=x>>16;x*=0x85EBCA6Bu;x^=x>>13;x*=0xC2B2AE35u;x^=x>>16;return x/4294967296.0;}}

 // 곡 목록의 채보를 차례로 돌며 합성 기록을 만든다. 점수는 800,000 ~ 1,000,000, 판정은 노말·하드·맥스를 번갈아, 날짜는 2026-01-01 부터 한 시간씩
 public static int WriteRecords(Catalog catalog,string path)
 {
  var store=new RecordStore(path);store.Active.Name=ProfileName;
  var charts=catalog.Songs.SelectMany(song=>song.Charts.Select(chart=>(song,chart))).ToList();
  if(charts.Count*3<RecordCount)throw new CheckUnavailableException($"곡 목록의 채보가 모자라다({charts.Count}개). 합성 기록 {RecordCount}개를 만들 수 없다.");
  string[] judges={"노말","하드","맥스"};var start=new DateTime(2026,1,1,0,0,0,DateTimeKind.Utc);
  for(int i=0;i<RecordCount;i++)
  {
   int which=i%charts.Count,round=i/charts.Count;var (song,chart)=charts[which];
   var key=new Selection(song.Id,chart.Button,chart.Pattern,judges[(which+round)%3],0).Key;   // 같은 채보는 바퀴마다 다른 판정이라 겹치지 않는다
   int raw=i%97==0?1000000:800000+(int)(Unit(i,1)*200000);
   var at=start.AddHours(i);
   store.Active.Records[key]=new Score(ScoreValue.Rate(raw),at,1+(int)(Unit(i,2)*40),at.AddMinutes(30),raw);
  }
  store.Save();
  return store.Active.Records.Count;
 }

 // V아카이브의 버튼별 응답(https://v-archive.net/api/v2/archive/{닉네임}/button/{버튼})과 같은 칸 이름·값의 종류로 만든다.
 // 칸의 구성은 실제 응답에서 이름과 종류만 확인했다(값은 옮기지 않았다): success, userNo, nickname, button, count, records[
 //  title(곡 번호), name, dlcCode, pattern(NM·HD·MX·SC), level, floor(없을 수 있음), floorName(없을 수 있음), newTab, maxRating, score, maxCombo, rating, djpower, maxDjpower, updatedAt ]
 // 닉네임과 번호는 꾸민 값이고, 점수는 정해진 수열이다. 곡 목록에 없는 곡 하나를 끼워 넣는다(목록이 서비스보다 오래됐을 때의 모습).
 public static Dictionary<int,int> WriteVArchive(Catalog catalog,string folder)
 {
  Directory.CreateDirectory(folder);var counts=new Dictionary<int,int>();
  string Short(string pattern)=>pattern switch{"NORMAL"=>"NM","HARD"=>"HD","MAXIMUM"=>"MX",_=>pattern};
  foreach(int button in new[]{4,5,6,8})
  {
   var records=new List<object>();int index=0;
   foreach(var song in catalog.Songs)foreach(var chart in song.Charts.Where(c=>c.Button==button))
   {
    index++;if(index%2==0)continue;   // 절반만 플레이한 것으로
    double u=Unit(index,button);decimal score=index%53==0?100m:Math.Round(90m+(decimal)(u*9.99),2);
    int level=1+(int)(Unit(index,button+10)*15);bool leveled=chart.Pattern=="SC"||level>=8;int floor=level*10+(int)(Unit(index,button+20)*10);
    records.Add(new{title=song.SourceId,name=song.Name,dlcCode=song.Pack,pattern=Short(chart.Pattern),level,floor=leveled?floor:(int?)null,floorName=leveled?$"{floor/10}.{floor%10}":null,newTab=false,
     maxRating=level*15,score,maxCombo=index%7==0,rating=Math.Round((double)score*level*0.15,4),djpower=Math.Round((double)score*0.86,4),maxDjpower=86.67,updatedAt=new DateTime(2026,4,1,0,0,0,DateTimeKind.Utc).AddMinutes(index).ToString("yyyy-MM-ddTHH:mm:ss.000Z")});
   }
   records.Add(new{title=9999999,name="Synthetic Unknown Song",dlcCode="R",pattern="SC",level=10,floor=(int?)105,floorName="10.5",newTab=false,maxRating=150,score=99.5m,maxCombo=false,rating=140.1,djpower=85.5,maxDjpower=86.67,updatedAt="2026-04-01T00:00:00.000Z"});
   File.WriteAllText(Path.Combine(folder,$"button-{button}.json"),JsonSerializer.Serialize(new{success=true,userNo=0,nickname="합성표본",button,count=records.Count,records}));
   counts[button]=records.Count;
  }
  return counts;
 }
}
