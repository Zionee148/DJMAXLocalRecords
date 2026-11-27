using System.Text.Json;
namespace LadderRecords;

// 앱 표시 크기의 규칙을 본다.
// 화면도 Windows 배율도 쓰지 않는 셈만 본다: 첫 값, 저장된 숫자를 받는 규칙, 화면에 들어가는 가장 큰 단계, 선택 상자 옆의 글, 설정 파일의 모양. 결과는 scale-validation.txt
[System.Runtime.Versioning.SupportedOSPlatform("windows10.0.19041")]
static class ScaleTests
{
 public static void Run()
 {
  var notes=new List<string>();void Check(bool value,string name){if(!value)throw new Exception(name);notes.Add("PASS: "+name);}
  // 1. 첫 값: '작게 보기'를 켜 두었으면 100, 아니면 Windows 배율 이하에서 가장 가까운 단계. 위 끝은 200 이다
  Check(Theme.Steps.SequenceEqual(new[]{100,125,150,175,200}),"the choices are 100, 125, 150, 175 and 200 percent");
  Check(Theme.FirstChoice(false,1f)==100&&Theme.FirstChoice(false,1.25f)==125&&Theme.FirstChoice(false,1.5f)==150&&Theme.FirstChoice(false,1.75f)==175&&Theme.FirstChoice(false,2f)==200,"a Windows scale that matches a choice starts at that choice");
  Check(Theme.FirstChoice(false,1.1f)==100&&Theme.FirstChoice(false,1.4f)==125&&Theme.FirstChoice(false,1.9f)==175,"a custom Windows scale starts at the nearest choice that is not above it (140 → 125, 190 → 175)");
  Check(Theme.FirstChoice(false,2.25f)==200&&Theme.FirstChoice(false,2.5f)==200&&Theme.FirstChoice(false,3f)==200,"above 200 the first choice is 200 (250 → 200)");
  Check(Theme.FirstChoice(true,1f)==100&&Theme.FirstChoice(true,1.5f)==100&&Theme.FirstChoice(true,2.5f)==100,"the old 'small' option carries over as 100");
  // 2. 저장된 숫자: 고를 수 있는 단계면 Windows 배율과 상관없이 그것. 아니면(없거나, 목록에 없는 값) 첫 값의 규칙
  Check(Theme.Choice(175,false,1.25f)==175&&Theme.Choice(175,false,1.5f)==175&&Theme.Choice(100,false,2f)==100&&Theme.Choice(200,true,1f)==200,"a saved choice stays when the Windows scale changes, and wins over the old 'small' option");
  Check(Theme.Choice(null,false,1.5f)==150&&Theme.Choice(null,true,1.5f)==100&&Theme.Choice(130,false,1.5f)==150&&Theme.Choice(250,false,1f)==100&&Theme.Choice(0,true,2f)==100,"no saved number, or one that is not a choice, falls back to the first-choice rule");
  // 3. 화면에 들어가는 가장 큰 단계(주 화면의 내용 1024×762 에 테두리와 제목 띠를 더해 본다). 두 단계 이상 내려갈 수 있고, 하나도 안 들어가면 100
  var fhd=new Size(1920,1032);var qhd=new Size(2560,1392);var uhd=new Size(3840,2112);const int sides=16,above=38;
  Check(Theme.Fit(200,uhd,sides,above)==200&&Theme.Fit(200,qhd,sides,above)==175&&Theme.Fit(200,fhd,sides,above)==125&&Theme.Fit(150,fhd,sides,above)==125&&Theme.Fit(125,fhd,sides,above)==125,"the chosen step is lowered to the largest one that fits the work area (200 on a 1080p screen opens at 125)");
  Check(Theme.Fit(100,fhd,sides,above)==100&&Theme.Fit(200,new Size(1100,700),sides,above)==100&&Theme.Fit(175,uhd,sides,above)==175,"100 is never lowered, a screen too small for anything still opens at 100, and a step that fits is kept");
  Check(Theme.Fit(150,new Size(1552,1181),sides,above)==150&&Theme.Fit(150,new Size(1551,1181),sides,above)==125&&Theme.Fit(150,new Size(1552,1180),sides,above)==125,"the fit is exact at the edge: 1536 + 16 wide and 1143 + 38 tall is just enough for 150");
  // 4. 선택 상자 옆의 글: 바꿔 둔 동안에는 재실행 안내가 먼저, 고른 그대로인데 낮춰 그리는 중이면 실제 크기, 같으면 없다
  Check(MainForm.ScaleNote(175,125,125)=="다시 실행하면 적용됩니다"&&MainForm.ScaleNote(175,200,125)=="다시 실행하면 적용됩니다","while a different size is picked, it says to run again (also when this screen shows a lower size)");
  Check(MainForm.ScaleNote(200,200,125)=="이 화면에서는 125%로 표시됩니다"&&MainForm.ScaleNote(150,150,100)=="이 화면에서는 100%로 표시됩니다","with the started choice picked again and a lower size shown, it tells the size shown on this screen");
  Check(MainForm.ScaleNote(150,150,150)==""&&MainForm.ScaleNote(100,100,100)=="","when the choice and the shown size are the same, it says nothing");
  // 5. 설정 파일: 숫자는 Scale 로 적힌다. 이 기능이 없는 버전의 파일(Scale 없음)을 읽으면 없는 것으로 온다. '작게 보기'의 칸은 그대로 읽힌다
  string folder=Path.Combine(AppContext.BaseDirectory,"test-data","scale-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);string path=Path.Combine(folder,"preferences.json");
  new UserOptions(false,true,true,false,null,175).Save(path);var read=UserOptions.Load(path);
  Check(read is {Scale:175,Small:false,Dark:true,DiagnosticImages:true}&&JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("Scale").GetInt32()==175,"the chosen number is written to the settings file and read back");
  File.WriteAllText(path,"{\"Fullscreen\":false,\"DiagnosticImages\":false,\"Dark\":false,\"Small\":true}");var old=UserOptions.Load(path);
  Check(old is {Scale:null,Small:true}&&Theme.Choice(old.Scale,old.Small,1.5f)==100,"a settings file from a version without this choice has no number, and its 'small' option gives 100");
  File.WriteAllText(path,"{\"Fullscreen\":false,\"DiagnosticImages\":false,\"Dark\":false,\"Small\":false}");old=UserOptions.Load(path);
  Check(old is {Scale:null,Small:false}&&Theme.Choice(old.Scale,old.Small,1.5f)==150&&Theme.Choice(old.Scale,old.Small,1f)==100,"with 'small' off it starts from the Windows scale");
  File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"scale-validation.txt"),string.Join("\n",notes)+"\n");
 }
}
