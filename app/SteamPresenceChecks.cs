using System.Diagnostics;
using Microsoft.Win32;
namespace LadderRecords;
// Steam 이 디맥을 "실행 중"으로 보는지를 이 PC 의 실제 Steam 으로 본다(--steam-presence-check. 이 프로그램은 게임이 켜져 있을 때만 Steam 에 접속한다).
// Steam 은 게임마다 실행 표시를 레지스트리(HKCU\Software\Valve\Steam\Apps\960170 의 Running)에 적는다. 그 값을 읽기만 한다.
// 차례: ① 게임이 꺼져 있을 때 접속하지 않고 표시가 0 인 채인가 ② 게임이 켜진 것으로 치고 접속하면 표시가 1 이 되는가(이 PC 에서 표시로 확인할 수 있는지)
//       ③ 게임이 꺼진 것으로 치면 접속을 끊고, 이 프로그램이 켜져 있는 채로 표시가 0 으로 돌아오는가
// ②·③ 동안 몇 초쯤 Steam 에 디맥이 실행 중으로 뜬다. 게임이 켜져 있으면 돌리지 않는다. 결과는 steam-presence-validation.txt
static class SteamPresenceChecks
{
 const string Key=@"HKEY_CURRENT_USER\Software\Valve\Steam\Apps\960170";
 static int Running()=>Registry.GetValue(Key,"Running",null) is int value?value:-1;
 public static void Run()
 {
  var notes=new List<string>();
  void Done()=>File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"steam-presence-validation.txt"),string.Join("\n",notes));
  bool Wait(Func<bool> done,int ms){var until=DateTime.UtcNow.AddMilliseconds(ms);while(DateTime.UtcNow<until){if(done())return true;Thread.Sleep(200);}return done();}
  var game=Process.GetProcessesByName("DJMAX RESPECT V");bool playing=game.Length>0;foreach(var process in game)process.Dispose();
  if(playing){notes.Add("SKIP: 게임이 켜져 있어 볼 수 없다. 게임을 끈 뒤에 다시 돌린다");Done();return;}
  int before=Running();notes.Add($"INFO: 시작할 때 Steam 의 실행 표시(Running) = {before} (-1 은 값이 없음)");
  if(before!=0){notes.Add("SKIP: 실행 표시가 0 이 아니다(Steam 이 없거나, 다른 것이 디맥의 번호로 접속해 있다). 볼 수 없다");Done();return;}

  using var presence=new SteamPresence{GameRunning=()=>false};
  var idle=presence.Read();Thread.Sleep(3000);presence.Read();
  notes.Add($"{(idle.Detail==SteamPresence.WaitingText&&!presence.Connected&&Running()==0?"PASS":"FAIL")}: 게임이 꺼져 있으면 Steam 에 접속하지 않고, 3초 뒤에도 Steam 의 실행 표시가 0 이다(접속 {presence.Connected}, 표시 {Running()}, 상태 글 '{idle.Detail}')");

  presence.GameRunning=()=>true;Thread.Sleep(1100);
  SteamSnapshot joined=presence.Read();Wait(()=>{if(!presence.Connected)joined=presence.Read();return presence.Connected;},12000);
  if(!presence.Connected){notes.Add("SKIP: Steam 에 접속하지 못해 나머지를 볼 수 없다: "+joined.Detail);Done();return;}
  bool shown=Wait(()=>Running()==1,10000);
  notes.Add($"INFO: 디맥의 번호로 접속함. Steam 의 실행 표시가 1 이 됨 = {shown}");

  presence.GameRunning=()=>false;Thread.Sleep(1100);var watch=Stopwatch.StartNew();presence.Read();
  notes.Add($"{(!presence.Connected?"PASS":"FAIL")}: 게임이 꺼지면 접속을 끊는다");
  if(shown){bool cleared=Wait(()=>Running()==0,20000);notes.Add($"{(cleared?"PASS":"FAIL")}: 접속을 끊으면 이 프로그램이 켜져 있는 채로 Steam 의 실행 표시가 0 으로 돌아온다({watch.Elapsed.TotalSeconds:0.0}초 안, 표시 {Running()})");}
  else notes.Add("INFO: 이 PC 에서는 접속해도 실행 표시가 1 이 되지 않았다. 끊은 뒤에 표시가 돌아오는지는 이 값으로 확인하지 못했다");
  Done();
 }
}
