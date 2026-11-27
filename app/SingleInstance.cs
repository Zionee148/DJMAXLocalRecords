using System.Diagnostics;
using System.Runtime.InteropServices;
namespace LadderRecords;

// 프로그램을 한 개만 켜지게 한다: 이미 켜져 있으면 그 창을 앞으로 가져오고 두 번째 실행은 끝낸다(5초 안에 그 창을 찾지 못하면 "이미 실행 중입니다"를 알린다).
// 이 처리가 없으면 두 번째 실행이 OBS 용 포트(18765)를 잡지 못해 "각 소켓 주소는 하나만 사용할 수 있습니다"라는 실행 오류로 끝난다.
// 인자를 주고 켜는 개발용 검사는 여기를 거치지 않는다(포트를 바꿔 여러 개를 함께 돌린다).
static class SingleInstance
{
 [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr window);
 [DllImport("user32.dll")]static extern bool ShowWindowAsync(IntPtr window,int command);
 [DllImport("user32.dll")]static extern bool IsIconic(IntPtr window);
 const int Restore=9;
 /// <summary>뮤텍스의 이름. 자동 업데이트의 시험용 빌드는 설정에 적힌 이름을 붙여, 함께 켜져 있는 실제 프로그램과 겹치지 않게 한다.</summary>
 public static string MutexName(UpdateTestConfig? test)=>@"Local\DJMAXLocalRecords.SingleInstance"+(string.IsNullOrEmpty(test?.Instance)?"":"."+test!.Instance);
 static Mutex? held;   // 프로그램이 끝날 때까지 쥐고 있는다

 /// <summary>처음 켜진 것이면 true. 이미 켜져 있으면 그 창을 앞으로 가져오고 false.</summary>
 public static bool Claim()
 {
  held=new Mutex(true,MutexName(UpdateTestConfig.Load()),out bool first);
  if(first)return true;
  held.Dispose();held=null;
  // 먼저 켜진 쪽이 아직 뜨는 중일 수 있다(모델을 읽느라 창이 몇 초 뒤에 보인다). 잠깐 기다려 본다.
  for(int tries=0;tries<25;tries++)
  {
   if(BringForward())return false;
   Thread.Sleep(200);
  }
  GuideDialog.ShowAlone(StartGuides.AlreadyRunning);
  return false;
 }

 static bool BringForward()
 {
  using var me=Process.GetCurrentProcess();
  foreach(var other in Process.GetProcessesByName(me.ProcessName))
   using(other)
   {
    if(other.Id==me.Id||other.MainWindowHandle==IntPtr.Zero)continue;
    if(IsIconic(other.MainWindowHandle))ShowWindowAsync(other.MainWindowHandle,Restore);
    SetForegroundWindow(other.MainWindowHandle);
    return true;
   }
  return false;
 }
}
