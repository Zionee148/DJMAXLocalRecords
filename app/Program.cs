using System.Runtime.Versioning;
using System.Text.Json;
namespace LadderRecords;
[SupportedOSPlatform("windows10.0.19041")]
static class Program
{
 [STAThread]static void Main(string[] args){ApplicationConfiguration.Initialize();Theme.FixDefaultFont();try{
 if(UpdateProgram.Handle(args))return;   // 자동 업데이트의 교체나 복구를 맡아 켜진 것이면 그 일만 하고 끝낸다
 if(args.Length==0&&!SingleInstance.Claim())return;   // 이미 켜져 있으면 그 창을 앞으로 가져오고 끝낸다. 인자를 준 개발용 검사는 여러 개를 함께 돌리므로 거치지 않는다
#if DEVELOPMENT_CHECKS
 MainForm.UpdateFeatures=args.Length==0;   // 개발용 검사는 새 버전을 확인하지 않는다
#endif
 int port=UpdateTestConfig.Load()?.OverlayPort??18765;   // 시험용 빌드가 아니면 언제나 18765
 var catalog=JsonSerializer.Deserialize<Catalog>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"data/catalog.json")))!;
#if DEVELOPMENT_CHECKS
 if(args.Length==3&&args[0]=="--rate-leading-check"){Task.Run(()=>RateResponsivenessChecks.Digits(catalog,args[1],args[2])).GetAwaiter().GetResult();return;}
 if(args.Length==3&&args[0]=="--rate-responsive-check"){RateResponsivenessChecks.Responsiveness(catalog,args[1],args[2]);return;}
 if(args.Length==2&&args[0]=="--integrated-ui-check"){IntegratedUiChecks.Run(catalog,args[1]);return;}
 if(args.Length==3&&args[0]=="--rate-positions-check"){Task.Run(()=>IntegratedUiChecks.Positions(catalog,args[1],args[2])).GetAwaiter().GetResult();return;}
 if(args.Length==6&&args[0]=="--score-flow-check"){ScoreReplayChecks.Flow(catalog,args[1],args[2],args[3],int.Parse(args[4]),args[5]);return;}
 if(args.Length==2&&args[0]=="--score-diagnostics-check"){ScoreReplayChecks.Diagnostics(catalog,args[1]);return;}
 if(args.Length>=4&&args[0]=="--score-replay-check"){ScoreReplayChecks.Run(catalog,args[1],args[2],args[3],args.Length>4?int.Parse(args[4]):18790,args.Length>5?args[5]:"cancel");return;}
 if(args.Length==2&&args[0]=="--score-rate-domain"){ScoreRateChecks.Domain(catalog,args[1]);return;}
 if(args.Length==3&&args[0]=="--hud-rate-check"){Task.Run(()=>ScoreRateChecks.Rate(catalog,args[1],args[2])).GetAwaiter().GetResult();return;}
 if(args.Contains("--popup-fit-check")){OverlayPopupChecks.Run(catalog);return;}
 if(args.Contains("--dev-fixtures")){DevFixtureChecks.Run(catalog);return;}
 if(args.Length==2&&args[0]=="--audio-reference-build"){AudioReferenceChecks.Build(args[1]);return;}   // 동명곡 소리 기준을 만든다(tools\build_audio_references.py 가 부른다)
 if(args.Length==2&&args[0]=="--audio-reference-check"){AudioReferenceChecks.Check(args[1]);return;}
 if(args.Contains("--update-windows-check")){UpdateTests.Previews();return;}   // 자동 업데이트의 창을 그림으로 남긴다
 if(args.Contains("--update-official-check")){UpdateTests.Official();return;}   // 진짜 공식 저장소의 응답을 읽어 본다(인터넷을 쓴다)
 if(args.Contains("--release-feature-check")){ReleaseFeatureChecks.Run(catalog,args.Length>1?args[1]:DevFixtureChecks.VArchiveFolder);return;}
 if(args.Contains("--store-optimization-check")){StoreOptimizationChecks.Run(catalog);return;} if(args.Length>1&&args[0]=="--test-audio-tone"){ProcessAudioChecks.Tone(int.Parse(args[1]));return;} if(args.Contains("--process-audio-check")){ProcessAudioChecks.Run();return;} if(args.Contains("--ocr-optimization-check")){Task.Run(OcrOptimizationChecks.Run).GetAwaiter().GetResult();return;} if(args.Length>1&&args[0]=="--records-scroll-check"){RecordsScrollChecks.Run(catalog,args[1]);return;} if(args.Length>2&&args[0]=="--ellipsis-report"){OverlayReports.Write(catalog,JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(args[1])),args[2]);return;} if(args.Contains("--records-modal-check")){RecordsModalChecks.Run(catalog);return;} if(args.Length>1&&args[0]=="--records-responsiveness"){RecordsPerformanceChecks.Responsiveness(catalog,args[1]);return;} if(args.Length>1&&args[0]=="--records-performance"){RecordsPerformanceChecks.Run(catalog,args[1]);return;} if(args.Contains("--hotkey-flow-check")){HotkeyFlowChecks.Run(catalog,true);return;} if(args.Contains("--hotkey-probe")){HotkeyFlowChecks.Run(catalog);return;} if(args.Contains("--dialogs-check")){UiChecks.Run(catalog,args.Length>1?args[1]:null);return;} if(args.Contains("--overlay-check")){OverlayToolChecks.Run(catalog,args.Contains("smoke"));return;} if(args.Contains("--screen-fixtures")){Task.Run(()=>DetectionChecks.Fixtures(catalog)).GetAwaiter().GetResult();return;} if(args.Length>0&&args[0]=="--self-test"){SelfTests.Run(catalog);DetectionChecks.Run(catalog);return;} if(args.Length>1&&args[0]=="--audio-check"){DetectionChecks.Audio(args[1]);return;} if(args.Length>1&&args[0]=="--screen-check"){Task.Run(()=>DetectionChecks.Screen(catalog,args[1],args.Length>2?int.Parse(args[2]):0,args.Length>3?args[3]:"LADDERMATCH")).GetAwaiter().GetResult();return;} if(args.Contains("--audio-device-check")){using var device=new LoopbackAudio();Thread.Sleep(250);File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"audio-device-validation.json"),JsonSerializer.Serialize(new{device.Error,device.Seconds,device.TargetProcessId,device.Initialized}));return;} if(args.Contains("--steam-check")){using var steam=new SteamPresence();File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"steam-check.json"),JsonSerializer.Serialize(steam.Read()));return;}

 using var app=new MainForm(catalog,args.Contains("--ui-check")?18766:port); if(args.Length>0&&args[0]=="--ui-check"){app.Show();Application.DoEvents();using var confirm=new Confirmation("등록하시겠습니까?\nLa Campanella : Nu Rave · 4B · SC · 하드판정\n등록할 기록: 99.48%\n기존 기록: 99.25%");confirm.Show();Application.DoEvents();using(var confirmShot=new Bitmap(confirm.Width,confirm.Height)){confirm.DrawToBitmap(confirmShot,new Rectangle(0,0,confirmShot.Width,confirmShot.Height));confirmShot.Save(Path.Combine(AppContext.BaseDirectory,"confirmation-preview.png"));}confirm.Finish();using var shot=new Bitmap(app.Width,app.Height);app.DrawToBitmap(shot,new Rectangle(0,0,shot.Width,shot.Height));shot.Save(Path.Combine(AppContext.BaseDirectory,"ui-preview.png"));app.Close();return;} if(args.Length>0&&args[0]=="--preview-server"){using var closeTimer=new System.Windows.Forms.Timer(){Interval=60000};closeTimer.Tick+=(_,_)=>app.Close();closeTimer.Start();Application.Run(app);return;} Application.Run(app);
#else
 using var app=new MainForm(catalog,port);
 Application.Run(app);
#endif
 }catch(Exception e){
  Environment.ExitCode=1;
#if DEVELOPMENT_CHECKS
  if(args.Length>0){File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"test-error.txt"),e.ToString());return;}
#endif
  // 이 장치가 없는 이전 버전이 켜져 있으면 포트가 겹친다. 무엇을 하면 되는지 알 수 있게 적는다
  bool portTaken=e is System.Net.Sockets.SocketException{SocketErrorCode:System.Net.Sockets.SocketError.AddressAlreadyInUse};
  NoticeDialog.ShowAlone("실행 오류",portTaken?"OBS 연결에 쓰는 포트(18765)를 다른 프로그램이 이미 쓰고 있어 시작할 수 없습니다.\n이전 버전이 켜져 있다면 종료한 뒤 다시 실행해 주세요.":e.Message);
 }}
}





