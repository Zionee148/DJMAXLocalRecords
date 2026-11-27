using System.Diagnostics;
using System.Runtime.InteropServices;
namespace LadderRecords;
// 컬렉션 가져오기: 읽기의 진행(CollectionScan)을 실제 게임에 잇는다. 화면은 앱의 화면 복사(MainForm.CaptureFrame), 키는 SendInput 의 스캔 코드다.
// 키는 Windows 의 입력 줄에 넣는 것이라 그때의 앞 창이 받는다: 그래서 보내기 직전마다 게임이 앞 창인지 보는 것은 읽기의 진행이 한다(여기서도 한 번 더 본다).
// 게임이 관리자 권한으로 켜져 있고 이 프로그램이 아니면 Windows 가 키를 막는다(막혔다는 것을 SendInput 은 알려 주지 않는다): KeysBlocked 로 미리 본다.

[System.Runtime.Versioning.SupportedOSPlatform("windows10.0.19041")]
sealed class CollectionGame:IScanWorld
{
 [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")]static extern bool IsIconic(IntPtr window);
 [DllImport("user32.dll")]static extern short GetAsyncKeyState(int key);
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
 [DllImport("user32.dll",SetLastError=true)]static extern uint SendInput(uint count,Input[] inputs,int size);
 [DllImport("kernel32.dll",SetLastError=true)]static extern IntPtr OpenProcess(uint access,bool inherit,uint process);
 [DllImport("advapi32.dll",SetLastError=true)]static extern bool OpenProcessToken(IntPtr process,uint access,out IntPtr token);
 [DllImport("advapi32.dll",SetLastError=true)]static extern bool GetTokenInformation(IntPtr token,int kind,out int value,int size,out int needed);
 [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
 [StructLayout(LayoutKind.Sequential)]struct KeyInput{public ushort Vk,Scan;public uint Flags,Time;public IntPtr Extra;}
 [StructLayout(LayoutKind.Sequential)]struct MouseInput{public int X,Y;public uint Data,Flags,Time;public IntPtr Extra;}
 [StructLayout(LayoutKind.Explicit)]struct InputUnion{[FieldOffset(0)]public MouseInput Mouse;[FieldOffset(0)]public KeyInput Key;}
 [StructLayout(LayoutKind.Sequential)]struct Input{public uint Type;public InputUnion Data;}
 const uint Keyboard=1,Extended=1,KeyUp=2,ScanCode=8;
 // 스캔 코드(키보드의 자리). 화살표는 확장 키다
 public const ushort UpKey=0x48,DownKey=0x50,ControlKey=0x1D,EnterKey=0x1C,BackspaceKey=0x0E;

 public IntPtr Window{get;}
 readonly Stopwatch clock=Stopwatch.StartNew();volatile bool stop;
 /// <summary>마지막으로 받은 화면의 크기(읽을 수 있는 크기가 아니어서 받지 못했을 때 알리려고).</summary>
 public Size LastSize{get;private set;}
 /// <summary>보낸 키 가운데 Windows 가 받지 않은 것의 수.</summary>
 public int Refused{get;private set;}

 public CollectionGame(IntPtr window){Window=window;}
 /// <summary>읽을 수 있는 화면의 크기: 1920×1080, 또는 그보다 큰 16:9(1920×1080 으로 줄여 읽는다. 보더리스·전체 화면이 모니터 크기로 늘어날 때).
 /// 1920×1080 보다 작은 화면을 늘린 그림은 글자의 화소가 달라 읽지 않는다. 줄인 화면은 2560×1440 에서만 확인했다</summary>
 public static bool Readable(Size size)=>size.Width==CollectionFrame.Width&&size.Height==CollectionFrame.Height||size.Width>CollectionFrame.Width&&size.Width*9==size.Height*16;
 /// <summary>받은 화면을 1920×1080 으로 줄여 읽는가.</summary>
 public bool Shrunk=>Readable(LastSize)&&LastSize.Width!=CollectionFrame.Width;
 /// <summary>게임 창. 게임이 켜져 있지 않으면 IntPtr.Zero.</summary>
 public static IntPtr Find()
 {
  var games=Process.GetProcessesByName("DJMAX RESPECT V");
  try{return games.FirstOrDefault(p=>p.MainWindowHandle!=IntPtr.Zero)?.MainWindowHandle??IntPtr.Zero;}finally{foreach(var g in games)g.Dispose();}
 }
 /// <summary>사용자가 중지를 눌렀다(창이 단축키를 받아 부른다).</summary>
 public void AskStop()=>stop=true;

 public CollectionFrame? Capture()
 {
  try
  {
   using var bitmap=MainForm.CaptureFrame(Window,out var size);LastSize=size;   // 1920×1080 이 아니면 이미 1920×1080 으로 바꾼 그림이다
   return Readable(size)?new CollectionFrame(bitmap):null;
  }
  catch{return null;}
 }
 public void Key(ScanKey key,bool down)=>Send(key==ScanKey.Up?UpKey:DownKey,true,down);
 /// <summary>키 하나를 누르거나 뗀다. 누르는 것은 게임이 앞 창일 때만 보낸다(떼는 것은 언제나 보낸다: 눌린 채로 남기지 않는다).</summary>
 public void Send(ushort scan,bool extended,bool down)
 {
  // 사용자가 Ctrl·Shift 를 누르고 있으면 보내는 키가 게임에 Ctrl+↓ 처럼 들어간다: 뗄 때까지 기다린다. 그동안 창을 바꾸거나 중지하면 보내지 않는다
  if(down)while(ModifierHeld&&Active&&!stop)Thread.Sleep(15);
  if(down&&(!Active||stop)){Refused++;return;}
  var input=new Input{Type=Keyboard};input.Data.Key=new KeyInput{Scan=scan,Flags=ScanCode|(extended?Extended:0)|(down?0:KeyUp)};
  if(SendInput(1,new[]{input},Marshal.SizeOf<Input>())!=1)Refused++;
 }
 public double Now=>clock.Elapsed.TotalSeconds;
 public void Wait(double seconds){if(seconds>0)Thread.Sleep(Math.Max(1,(int)Math.Round(seconds*1000)));}
 /// <summary>게임이 앞 창이고 최소화되지 않았고, 사용자가 창을 바꾸는 키(Alt·Windows 키)를 누르고 있지 않다.
 /// Alt+Tab 의 창 고르기는 보더리스 게임 위에 그려지는데 그동안 게임이 앞 창으로 남을 수 있다: 그 그림을 곡이 바뀐 것으로 보지 않게 키로도 본다</summary>
 public bool Active=>GetForegroundWindow()==Window&&!IsIconic(Window)&&!SwitchKeys.Any(k=>(GetAsyncKeyState(k)&0x8000)!=0);
 static readonly int[] SwitchKeys={0x12,0x5B,0x5C};   // Alt, 왼쪽·오른쪽 Windows 키
 // 시작 단축키(Ctrl+Enter)와 그 밖의 바꾸개 키. 사용자가 아직 누르고 있으면 보내는 ↑ 가 게임에 Ctrl+↑ 로 들어간다
 static readonly int[] HeldKeys={0x11,0x10,0x12,0x0D};   // Ctrl, Shift, Alt, Enter
 static bool ModifierHeld=>(GetAsyncKeyState(0x11)&0x8000)!=0||(GetAsyncKeyState(0x10)&0x8000)!=0;   // Ctrl, Shift
 /// <summary>시작 단축키나 바꾸개 키가 아직 눌려 있는가(WaitKeysUp 의 한도가 지난 뒤에 본다)</summary>
 public static bool KeysHeld=>HeldKeys.Any(k=>(GetAsyncKeyState(k)&0x8000)!=0);
 /// <summary>사용자가 누른 키(시작 단축키)를 뗄 때까지 기다린다(한도 limit 초). 기다린 시간(초)을 돌려준다</summary>
 public double WaitKeysUp(double limit=3)
 {
  var watch=Stopwatch.StartNew();
  while(watch.Elapsed.TotalSeconds<limit&&HeldKeys.Any(k=>(GetAsyncKeyState(k)&0x8000)!=0))Thread.Sleep(15);
  return watch.Elapsed.TotalSeconds;
 }
 public bool StopAsked=>stop;

 /// <summary>게임이 관리자 권한으로 켜져 있는데 이 프로그램은 아니다: Windows 가 키를 막는다. 게임의 권한을 알 수 없으면(열지 못하면) 막힌 것으로 본다.</summary>
 public bool KeysBlocked=>!Environment.IsPrivilegedProcess&&Elevated(Window)!=false;
 /// <summary>창의 프로세스가 관리자 권한인가. 알 수 없으면 null.</summary>
 public static bool? Elevated(IntPtr window)
 {
  const uint QueryLimited=0x1000,TokenQuery=8;const int TokenElevation=20;
  GetWindowThreadProcessId(window,out uint id);if(id==0)return null;
  var process=OpenProcess(QueryLimited,false,id);if(process==IntPtr.Zero)return null;
  try
  {
   if(!OpenProcessToken(process,TokenQuery,out var token))return null;
   try{return GetTokenInformation(token,TokenElevation,out int value,sizeof(int),out _)?value!=0:null;}finally{CloseHandle(token);}
  }
  finally{CloseHandle(process);}
 }
}

/// <summary>컬렉션 읽기의 시작·중지 단축키. 메인 창의 단축키 다섯과 따로 등록한다(시작 Ctrl+Enter, 중지 Backspace).
/// 시작은 준비 화면이 기다릴 때만, 중지는 읽는 동안만 켠다(다른 때에 그 키를 가로채지 않게). 받은 창은 WM_HOTKEY 의 번호(StartId·StopId)로 가린다.</summary>
sealed class ScanHotkeys:IDisposable
{
 [DllImport("user32.dll")]static extern bool RegisterHotKey(IntPtr window,int id,uint modifiers,uint key);
 [DllImport("user32.dll")]static extern bool UnregisterHotKey(IntPtr window,int id);
 public const int Message=0x0312,StartId=140,StopId=141;
 /// <summary>화면에 적는 시작·중지 키와 그 이름(컬렉션 창의 시작 대기, 단축키 설정 창의 '컬렉션 가져오기' 구역. 아래 등록하는 키와 같아야 한다)</summary>
 public static readonly (string Name,string[] Keys) Start=("가져오기 시작",new[]{"Ctrl","Enter"}),Stop=("읽기 중지",new[]{"Backspace"});
 const uint Control=2,NoRepeat=0x4000,Enter=0x0D,Backspace=0x08;
 readonly IntPtr window;bool start,stop;
 public ScanHotkeys(IntPtr window){this.window=window;}
 /// <summary>참을 돌려주면 등록됐다(끄는 것은 언제나 참). 거짓이면 다른 프로그램이 그 키를 쓰는 중이다.</summary>
 public bool SetStart(bool on)=>Set(ref start,on,StartId,Control|NoRepeat,Enter);
 public bool SetStop(bool on)=>Set(ref stop,on,StopId,NoRepeat,Backspace);
 bool Set(ref bool now,bool on,int id,uint modifiers,uint key)
 {
  if(now==on)return true;
  if(!on){UnregisterHotKey(window,id);now=false;return true;}
  return now=RegisterHotKey(window,id,modifiers,key);
 }
 public void Dispose(){SetStart(false);SetStop(false);}
}
