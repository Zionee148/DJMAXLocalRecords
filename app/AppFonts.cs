using System.Drawing.Text;
using System.Runtime.InteropServices;
namespace LadderRecords;

// 프로그램 창의 글꼴. 실행 파일 옆 fonts\ 폴더의 글꼴 파일을 이 프로세스에서만 쓰도록 싣는다(Windows 에 설치하지 않는다).
// 글꼴은 두 군데에 실어야 한다.
//   GDI+ : 프로세스 전용 모음(PrivateFontCollection)에 넣고, 거기서 꺼낸 글꼴 집합으로 Font 를 만든다. 이름 문자열로 만들면 GDI+ 가 찾지 못해 다른 글꼴로 바뀐다
//   GDI  : 같은 파일을 AddFontResourceEx 로도 싣는다. 단추·글 칸처럼 Windows 가 그리는 글자(TextRenderer)가 그 이름을 찾는다
// 글꼴 파일이 없거나 읽지 못해도 프로그램은 떠야 한다(자동 업데이트의 교체·복구를 맡은 실행 파일도 이 코드를 지난다). 그때는 Windows 의 글꼴을 쓴다.
// OBS 오버레이의 글꼴은 여기와 상관없다.
static class AppFonts
{
 [DllImport("gdi32.dll",CharSet=CharSet.Unicode)]static extern int AddFontResourceEx(string file,uint flags,IntPtr reserved);
 static readonly object gate=new();
 static readonly PrivateFontCollection loaded=new();   // 꺼낸 글꼴 집합을 쓰는 동안 살아 있어야 한다
 static readonly Dictionary<string,FontFamily> families=new(StringComparer.OrdinalIgnoreCase);
 static bool tried;

 /// <summary>일반 글자의 글꼴 집합(SUIT. 보통 400 과 굵게 700 이 들어 있다). 글꼴을 만들 때는 Theme.Px 를 거친다: 보통 글자는 아래의 500 으로 바꿔 만든다</summary>
 public static FontFamily Ui=>Find("SUIT","Malgun Gothic");
 /// <summary>일반 글자의 보통 굵기(500): 단추, 설명, 상태. 굵기마다 글꼴 집합의 이름이 따로다(SUIT Medium)</summary>
 public static FontFamily UiMedium=>Find("SUIT Medium","SUIT","Malgun Gothic");
 /// <summary>항목 이름의 굵기(600): 줄의 이름, 목표·현재 같은 값의 이름, 테마 전환</summary>
 public static FontFamily UiSemi=>Find("SUIT SemiBold","SUIT","Malgun Gothic");
 /// <summary>한글 곡명과 제목</summary>
 public static FontFamily Head=>Find("SUITE","SUIT","Malgun Gothic");
 /// <summary>영문 곡명, 숫자, SCORE·RATE, 영문 꼬리표</summary>
 public static FontFamily Latin=>Find("Oxanium","SUIT","Malgun Gothic");
 /// <summary>위의 글꼴에 없는 글자(한자 등)</summary>
 public static FontFamily Wide=>Find("Noto Sans JP","Yu Gothic UI","Malgun Gothic");

 // GDI+ 의 프로세스 전용 모음은 이름이 같은 글꼴 집합에서 먼저 넣은 파일 하나만 쓴다: 보통과 굵게를 함께 넣으면(파일 이름 차례로 굵게가 먼저다) 보통으로 만든 글꼴도 굵은 파일로 그려진다
 // (재어 확인했다). 그래서 위의 Ui·Latin 으로는 굵은 글자만 제대로 나온다. 보통 굵기(400)를 정확히 그려야 하는 자리는 그 파일 하나만 넣은 모음에서 꺼낸 아래의 것을 쓴다
 /// <summary>일반 글자의 400(SUIT Regular). 오버레이 미리보기의 안내 문구</summary>
 public static FontFamily UiRegular=>Single("SUIT-Regular.ttf",()=>Ui);
 /// <summary>영문·숫자의 400(Oxanium Regular). 오버레이 미리보기의 단축키 이름과 작은 숫자</summary>
 public static FontFamily LatinRegular=>Single("Oxanium-Regular.ttf",()=>Latin);
 /// <summary>영문·숫자의 500(Oxanium Medium). 기록 창들의 굵지 않은 숫자: 표의 일반 SCORE, RATE, 날짜(디자인 기준은 500~550 이다. 400 은 가늘게 보인다)</summary>
 public static FontFamily LatinMedium=>Single("Oxanium-Medium.ttf",()=>Latin);
 static readonly Dictionary<string,(PrivateFontCollection? Own,FontFamily Family)> singles=new();
 static FontFamily Single(string file,Func<FontFamily> fallback)
 {
  lock(gate)
  {
   if(singles.TryGetValue(file,out var known))return known.Family;
   PrivateFontCollection? own=null;FontFamily? family=null;
   try{string path=Path.Combine(AppContext.BaseDirectory,"fonts",file);if(File.Exists(path)){own=new PrivateFontCollection();own.AddFontFile(path);family=own.Families.FirstOrDefault();}}catch{}
   singles[file]=(own,family??=fallback());return family;
  }
 }

 /// <summary>실은 글꼴 파일에서 나온 글꼴 집합의 이름들(검사와 진단용)</summary>
 public static IReadOnlyCollection<string> Loaded{get{Load();lock(gate)return families.Keys.ToArray();}}

 static void Load()
 {
  lock(gate)
  {
   if(tried)return;tried=true;
   try
   {
    string folder=Path.Combine(AppContext.BaseDirectory,"fonts");if(!Directory.Exists(folder))return;
    foreach(var file in Directory.GetFiles(folder,"*.ttf"))
     try{loaded.AddFontFile(file);AddFontResourceEx(file,0x10,IntPtr.Zero);}catch{}   // 0x10 = 이 프로세스에서만(끝나면 사라진다)
    foreach(var family in loaded.Families)families[family.Name]=family;
   }
   catch{}
  }
 }
 // 앞에서부터 찾는다: 실은 글꼴, 없으면 Windows 에 설치된 글꼴, 그것도 없으면 Windows 의 기본 글꼴
 static readonly Dictionary<string,FontFamily> chosen=new();
 static FontFamily Find(params string[] names)
 {
  Load();
  lock(gate)
  {
   if(chosen.TryGetValue(names[0],out var known))return known;
   FontFamily? found=null;
   foreach(var name in names)
   {
    if(families.TryGetValue(name,out found))break;
    // 없는 글꼴이면 예외가 난다(다른 글꼴로 바꿔 주지 않는다). 열린 글꼴의 이름은 Windows 의 언어에 따라 다르게 돌아오므로(Malgun Gothic → 맑은 고딕) 이름을 견주지 않는다
    try{found=new FontFamily(name);break;}catch(ArgumentException){}
   }
   return chosen[names[0]]=found??FontFamily.GenericSansSerif;
  }
 }
}
