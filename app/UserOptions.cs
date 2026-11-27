using System.Text.Json;
namespace LadderRecords;
// Fullscreen 은 v1.1.x 의 '전체화면 사용'이다. 저장 확인이 주 화면 안으로 들어온 뒤로는 쓰지 않는다(켜져 있든 꺼져 있든 같은 흐름이다).
// 예전 설정 파일을 그대로 읽고, 다시 적을 때 값을 그대로 둔다(다른 설정을 지키려고 파일의 모양을 바꾸지 않는다)
// Scale 은 '앱 표시 크기'로 고른 숫자(100·125·150·175·200)다. 없으면 아직 정한 적이 없는 것이고, 주 화면이 처음 뜰 때 정해 적는다(Theme.FirstChoice).
// Small 은 v1.2.0 전의 '작게 보기'다(Windows 배율이 100% 보다 커도 100% 때의 크기로 그린다). 지금은 Scale 이 없을 때 첫 값을 정하는 데에만 읽는다.
// 그래도 계속 함께 적는다(Scale 이 100 이면 켬): 이 기능이 없는 버전으로 복구했을 때 그 버전에서 크기가 튀지 않게. 그 버전은 설정을 저장하며 Scale 을 지우고, 다시 올라온 새 버전은 Small 로 이어받는다
// Sender 는 투컴(송출 PC용 주소)의 설정이다: 허용 여부, 고른 연결, 연결 키 등(SenderLink.cs). 켠 적이 없으면 없다
record UserOptions(bool Fullscreen=false,bool DiagnosticImages=false,bool Dark=false,bool Small=false,SenderSettings? Sender=null,int? Scale=null)
{
 // 설정 파일이 아직 없을 때의 테마(새로 설치하면 다크, 쓰던 사람은 그대로):
 // 같은 폴더에 기록 파일도 없으면 새로 설치한 것으로 보고 클리어 다크, 기록 파일이 있으면 v1.1.x 를 쓰던 사람이므로 그때의 기본값(라이트).
 // v1.1.x 는 옵션을 한 번도 바꾸지 않으면 설정 파일을 만들지 않았다. 주 화면은 이렇게 정한 값을 처음 뜰 때 바로 저장한다
 public static UserOptions Load(string path)=>File.Exists(path)?JsonSerializer.Deserialize<UserOptions>(File.ReadAllText(path))??new():new(Dark:!File.Exists(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!,"records.json")));
 public void Save(string path){string temporary=path+".tmp";File.WriteAllText(temporary,JsonSerializer.Serialize(this));File.Move(temporary,path,true);}
}
