using System.Diagnostics;
using DjmaxScore;
namespace LadderRecords;

// SCORE 모듈의 일을 작업 스레드 하나에서 돌리고, 끝나면 화면 스레드에서 알린다 (ScoreLink 가 쓴다).
// 일은 한 번에 하나만 맡겨진다(ScoreLink 가 줄을 세운다). 일하다 난 오류는 삼키고 LastError 에 남긴다(SCORE 때문에 앱이 멈추면 안 된다).
sealed class ScoreRunner:IScoreRunner
{
 readonly SynchronizationContext ui;
 volatile bool busy;
 public ScoreRunner(SynchronizationContext ui)=>this.ui=ui;
 public long Now=>Stopwatch.GetTimestamp();
 public long TicksPerSecond=>Stopwatch.Frequency;
 public bool Busy=>busy;
 public string? LastError{get;private set;}
 public void Run(Action work,Action done)
 {
  busy=true;
  Task.Run(()=>{
   try{work();LastError=null;}
   catch(Exception e){LastError=e.Message;}
   finally{busy=false;}
   // 창이 닫힌 뒤에는 화면 스레드가 없을 수 있다. 그때는 알리지 못해도 된다.
   try{ui.Post(_=>{try{done();}catch(Exception e){LastError=e.Message;}},null);}catch(Exception){}
  });
 }
}

// 등록 답을 내지 못한 사유를 사용자에게 보여 줄 말로 바꾼다 (기획서 v3 기능 4절).
// Short 는 오버레이의 실패 문구 자리에 3초 보이는 짧은 사유, Advice 는 프로그램에 다음 요청이나 다음 곡까지 남는 다음 행동이다.
// 사용자의 다음 행동이 달라지는 것만 나눈다: 위치 미확정 / 플레이 중 근거 부족 / 0점 / 모듈을 쓸 수 없음 / 그 밖의 판독 실패. 자세한 사유는 진단 기록에 남는다.
static class ScoreFailure
{
 public static (string Short,string Advice) Describe(string reason)=>reason switch
 {
  "LocationUncertain" or "NoPlay"=>("점수 위치를 아직 찾지 못했습니다","점수판이 보이는지 확인하세요. 곡이 끝나 읽을 시간이 없다면 수동 기록을 이용하세요."),
  "NotEnoughPlayEvidence" or "RuleConflict"=>("플레이 중 확인한 점수가 부족합니다","이번 판은 수동 기록을 이용하세요. 다음에는 플레이를 시작하기 전부터 감지를 켜 두세요."),
  "ZeroScore"=>("0점은 자동으로 등록하지 않습니다","0점을 남기려면 수동 기록을 이용하세요."),
  "ModelUnavailable"=>("SCORE 자동 인식을 쓸 수 없습니다","수동 기록을 이용하세요. 까닭은 아래 SCORE 상태에 적혀 있습니다."),
  _=>("점수를 확인하지 못했습니다","점수판이 보이면 등록 키를 다시 누르거나 수동 기록을 이용하세요."),
 };
}
