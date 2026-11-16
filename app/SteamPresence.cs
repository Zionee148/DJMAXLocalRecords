using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
namespace LadderRecords;
record SteamChart(string Mode,string Name,int Button,string Pattern){public string Key=>$"{Mode}|{Name}|{Button}|{Pattern}";}
record SteamSnapshot(bool Available,SteamChart? Chart,string Detail);
class SteamPresence:IDisposable
{
 IntPtr library,friends,user;bool initialized;DateTime retry;
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int InitCall(IntPtr error);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate IntPtr Accessor();
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate ulong UserId(IntPtr self);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate uint AppId(IntPtr self);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate IntPtr RichValue(IntPtr self,ulong id,[MarshalAs(UnmanagedType.LPUTF8Str)]string key);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate void VoidCall();
 T Export<T>(string name) where T:Delegate=>Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library,name));
 IntPtr Interface(string prefix,params string[] versions){foreach(var v in versions)if(NativeLibrary.TryGetExport(library,prefix+v,out var address)){var p=Marshal.GetDelegateForFunctionPointer<Accessor>(address)();if(p!=IntPtr.Zero)return p;}throw new Exception("스팀 인터페이스를 사용할 수 없습니다.");}
 static string FindLibrary(){var game=System.Diagnostics.Process.GetProcessesByName("DJMAX RESPECT V").FirstOrDefault();if(game!=null)try{string? exe=game.MainModule?.FileName;if(exe!=null){string path=Path.Combine(Path.GetDirectoryName(exe)!,"DJMAX RESPECT V_Data","Plugins","x86_64","steam_api64.dll");if(File.Exists(path))return path;}}catch{}
  string steam=Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam","SteamPath",null) as string??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Steam");var roots=new List<string>{steam};string config=Path.Combine(steam,"steamapps","libraryfolders.vdf");if(File.Exists(config))foreach(Match m in Regex.Matches(File.ReadAllText(config),"\"path\"\\s*\"([^\"]+)\""))roots.Add(m.Groups[1].Value.Replace(@"\\",@"\"));foreach(string root in roots){string path=Path.Combine(root,"steamapps","common","DJMAX RESPECT V","DJMAX RESPECT V_Data","Plugins","x86_64","steam_api64.dll");if(File.Exists(path))return path;}throw new Exception("디맥 설치 폴더의 스팀 연동 파일을 찾지 못했습니다.");
 }
 public SteamSnapshot Read(){try{
  if(!initialized){if(DateTime.UtcNow<retry)return new(false,null,"스팀 연결 재시도 대기 중");retry=DateTime.UtcNow.AddSeconds(5);if(library==IntPtr.Zero)library=NativeLibrary.Load(FindLibrary());Environment.SetEnvironmentVariable("SteamAppId","960170");Environment.SetEnvironmentVariable("SteamGameId","960170");var error=Marshal.AllocHGlobal(1024);try{Marshal.Copy(new byte[1024],0,error,1024);if(Export<InitCall>("SteamAPI_InitFlat")(error)!=0)throw new Exception("스팀 연결 실패: "+Marshal.PtrToStringUTF8(error));}finally{Marshal.FreeHGlobal(error);}initialized=true;friends=Interface("SteamAPI_SteamFriends_v","018","017");user=Interface("SteamAPI_SteamUser_v","023","022","021");var utils=Interface("SteamAPI_SteamUtils_v","010","009");if(Export<AppId>("SteamAPI_ISteamUtils_GetAppID")(utils)!=960170)throw new Exception("스팀 게임 번호가 일치하지 않습니다.");}
  Export<VoidCall>("SteamAPI_RunCallbacks")();ulong id=Export<UserId>("SteamAPI_ISteamUser_GetSteamID")(user);var read=Export<RichValue>("SteamAPI_ISteamFriends_GetFriendRichPresence");string Value(string key)=>Marshal.PtrToStringUTF8(read(friends,id,key))??"";
  string mode=Value("gamemode"),name=Value("songname"),button=Value("keymode"),pattern=NormalizePattern(Value("difficult"));if(string.IsNullOrWhiteSpace(name))return new(true,null,"스팀 플레이 대기 · "+mode);if(mode is not ("FREESTYLE" or "LADDERMATCH" or "OPENMATCH"))return new(true,null,"지원하지 않는 모드: "+mode);if(!Regex.IsMatch(button,@"^[4568]B$"))return new(false,null,"스팀 버튼 정보 확인 중");if(pattern is not ("NORMAL" or "HARD" or "MAXIMUM" or "SC"))return new(false,null,"스팀 난이도 정보 확인 중");return new(true,new(mode,name,int.Parse(button[..1]),pattern),$"스팀: {name} · {button} · {pattern}");
 }catch(Exception e){if(initialized){try{Export<VoidCall>("SteamAPI_Shutdown")();}catch{}initialized=false;}retry=DateTime.UtcNow.AddSeconds(5);return new(false,null,e.Message);}}
 internal static string NormalizePattern(string value)=>value.Trim().ToUpperInvariant() switch
 {
  "NM" or "NORMAL"=>"NORMAL",
  "HD" or "HARD"=>"HARD",
  "MX" or "MAXIMUM"=>"MAXIMUM",
  "SC"=>"SC",
  _=>""
 };
 public void Dispose(){if(initialized){try{Export<VoidCall>("SteamAPI_Shutdown")();}catch{}initialized=false;}}
}
static class SteamSongs
{
 internal static string Clean(string text)=>Regex.Replace(text.Normalize(NormalizationForm.FormKC).ToUpperInvariant(),@"[^\p{L}\p{N}]","");
 internal static string Alias(string text){string key=Clean(text);return key is "GREGORIUSISYMPHONY" or "GREGORIUSSYMPHONY"?"GREGORIUSSYMPHONY":key;}
 public static List<Song> Find(Catalog catalog,SteamChart chart){
  bool Valid(Song song)=>song.Charts.Contains(new(chart.Button,chart.Pattern));
  var exact=catalog.Songs.Where(song=>song.Name.Equals(chart.Name,StringComparison.OrdinalIgnoreCase)&&Valid(song)).ToList();if(exact.Count>0)return exact;
  string key=Alias(chart.Name);var alias=catalog.Songs.Where(song=>Alias(song.Name)==key&&Valid(song)).ToList();if(alias.Count>0)return alias;
  // Steam 이 "(feat. …)"의 가수 이름을 곡 목록과 다른 글자로 줄 때가 있다(곡 목록 "Cata (feat. NC.A)", Steam "Cata (feat. 앤씨아)").
  // 그 부분을 뺀 제목으로 다시 견주되, 곡 목록 전체에서 그 제목인 곡이 하나뿐일 때만 받아들인다(둘 이상이면 어느 곡인지 알 수 없으므로 맞추지 않는다).
  string title=Title(chart.Name);if(title=="")return new();
  var same=catalog.Songs.Where(song=>Title(song.Name)==title).ToList();return same.Count==1&&Valid(same[0])?same:new();
 }
 // "(feat. …)"부터 끝까지를 뺀 제목. 괄호 없이 적힌 것("Wake Up feat.a-m")도 같이 뺀다.
 internal static string Title(string text)=>Clean(Regex.Replace(text,@"[\(\[]?\s*\bfeat\b.*$","",RegexOptions.IgnoreCase));
}
