using System.Text.RegularExpressions;
namespace LadderRecords;

// 이 프로그램의 버전. 묶음을 만들 때 준 번호가 실행 파일에 들어간다(DJMAXLadderRecords.csproj 의 Version).
// 번호를 주지 않고 빌드한 것은 0.0.0 이고 개발용 빌드로 본다: 새 버전을 확인하지 않는다.
static partial class AppVersion
{
 public static readonly Version Current=Three(typeof(AppVersion).Assembly.GetName().Version);
 public static bool IsDevelopment=>Current.Major==0&&Current.Minor==0&&Current.Build==0;
 public static string Text(Version version)=>$"v{version.Major}.{version.Minor}.{version.Build}";
 public static string Plain(Version version)=>$"{version.Major}.{version.Minor}.{version.Build}";
 static Version Three(Version? version)=>version==null?new(0,0,0):new(version.Major,version.Minor,Math.Max(version.Build,0));

 // 릴리즈의 이름표는 v1.2.3 꼴만 정식 버전으로 받는다. v1.2.3-beta, 1.2.3, v1.2 같은 것은 받지 않는다
 [GeneratedRegex(@"\Av([0-9]{1,5})\.([0-9]{1,5})\.([0-9]{1,5})\z")]private static partial Regex TagShape();
 [GeneratedRegex(@"\A([0-9]{1,5})\.([0-9]{1,5})\.([0-9]{1,5})\z")]private static partial Regex PlainShape();
 public static bool TryParseTag(string? tag,out Version version)=>Read(TagShape(),tag,out version);
 public static bool TryParsePlain(string? text,out Version version)=>Read(PlainShape(),text,out version);
 static bool Read(Regex shape,string? text,out Version version)
 {
  version=new(0,0,0);
  if(text==null)return false;
  var match=shape.Match(text);
  if(!match.Success)return false;
  version=new(int.Parse(match.Groups[1].Value),int.Parse(match.Groups[2].Value),int.Parse(match.Groups[3].Value));
  return true;
 }
}
