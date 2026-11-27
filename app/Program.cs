using System.Runtime.Versioning;
using System.Text.Json;
namespace LadderRecords;
[SupportedOSPlatform("windows10.0.19041")]
static class Program
{
 [STAThread]static void Main(string[] args){ApplicationConfiguration.Initialize();Theme.FixDefaultFont();try{
 // 창의 크기 단계를 정한다(첫 창을 만들기 전에 한 번). 설정 파일을 읽지 못하면 기본(Windows 배율을 따름)이다. 자동 업데이트를 맡아 켜진 것은 프로그램 폴더의 설정으로 다시 정한다(UpdateProgram)
 // 테마도 여기서 맞춘다: 주 화면이 생기기 전에 뜨는 안내(이미 켜져 있음, 실행 오류)가 사용자의 테마로 보이게
 {var options=new UserOptions();try{options=UserOptions.Load(UserFiles.Existing(AppContext.BaseDirectory,"preferences.json"));}catch{}Theme.Dark=options.Dark;Theme.DecideScale(options);}
 if(UpdateProgram.Handle(args))return;   // 자동 업데이트의 교체나 복구를 맡아 켜진 것이면 그 일만 하고 끝낸다
 if(args.Length==0&&!SingleInstance.Claim())return;   // 이미 켜져 있으면 그 창을 앞으로 가져오고 끝낸다. 인자를 준 개발용 검사는 여러 개를 함께 돌리므로 거치지 않는다
#if DEVELOPMENT_CHECKS
 MainForm.UpdateFeatures=args.Length==0;   // 개발용 검사는 새 버전을 확인하지 않는다
#endif
 int port=UpdateTestConfig.Load()?.OverlayPort??18765;   // 시험용 빌드가 아니면 언제나 18765
 var catalog=JsonSerializer.Deserialize<Catalog>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"data/catalog.json")))!;
 // 개인 파일의 자리('UserData')를 준비한다. v1.1.x 까지 실행 파일 옆에 두던 파일은 여기서 한 번 옮겨진다(옛 파일은 그대로 둔다). 이 뒤로는 개인 파일을 새 자리에서만 읽고 쓴다.
 // 업데이트·복구를 맡아 켜진 것은 위에서 끝났으므로 여기에 오지 않는다(임시 폴더나 백업 폴더에서는 옮기지 않는다).
 // 프로그램 자료(곡 목록)를 읽은 뒤에 한다: 프로그램 파일이 깨져 켜지지 못하는 배포본이 개인 파일부터 옮겨 놓지 않게
 {var prepared=UserFiles.Prepare(AppContext.BaseDirectory);UserFiles.Pending=args.Length==0?prepared:prepared with{Store=null};}   // 미리 열어 본 기록은 보통의 실행에서만 주 화면에 넘긴다(개발용 검사는 그 뒤에 파일을 바꾸기도 한다)
 // 창 크기는 주 화면이 읽는 설정(새 자리의 것)으로 정한다. 맨 위에서 정한 것은 전환 전에 띄울 수 있는 창(업데이트·복구, 시작 알림)을 위한 값이다
 {var options=new UserOptions();try{options=UserOptions.Load(UserFiles.At("preferences.json"));}catch{}Theme.DecideScale(options);}
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
 if(args.Length==2&&args[0]=="--play-format-dry"){PlayStoreChecks.Dry(args[1]);return;}   // 실제 형식 1 파일을 사본으로 올려 본다(원본은 읽기만 한다)
 if(args.Contains("--play-store-measure")){PlayStoreChecks.Run();return;}   // 형식 2 의 기록 파일을 줄 수를 늘려 가며 저장하고 읽는 시간과 크기를 잰다
 if(args.Contains("--steam-presence-check")){SteamPresenceChecks.Run();return;}   // 이 PC 의 실제 Steam 으로: 게임이 꺼져 있으면 접속하지 않는지, 끊으면 실행 표시가 돌아오는지
 if(args.Length==2&&args[0]=="--lag-check"){LagChecks.Run(catalog,args[1]);return;}   // 창이 뜨거나 바뀌는 순간의 화면을 빠르게 이어 찍는다
 if(args.Length==2&&args[0]=="--frame-check"){FrameChecks.Run(catalog,args[1]);return;}
 if(args.Length is >=2 and <=4&&args[0]=="--public-shots"){string? Given(int i)=>args.Length>i&&args[i]!="-"?args[i]:null;PublicShotChecks.Run(catalog,args[1],Given(2),Given(3));return;}   // 공개 안내에 싣는 창 그림을 꾸민 기록으로 찍는다   // 제목 띠·글꼴·색을 실제 화면에서 찍어 본다
 if(args.Length==3&&args[0]=="--popup-browser-check"){OverlayPopupChecks.Browser(catalog,args[1],args[2]);return;}   // 실제 Chrome·Edge 로 오버레이 팝업을 열어 본다
 if(args.Contains("--dev-fixtures")){DevFixtureChecks.Run(catalog);return;}
 if(args.Length>=2&&args[0]=="--collection-capture"){CollectionChecks.Capture(args[1],args.Length>2?int.Parse(args[2]):300);return;}   // 사용자가 게임에서 곡을 넘기는 동안 화면을 받아 둔다(키를 보내지 않는다)
 if(args.Length==6&&args[0]=="--collection-capture-video"){CollectionChecks.CaptureFromVideo(args[1],int.Parse(args[2]),int.Parse(args[3]),int.Parse(args[4]),args[5]);return;}   // 받아 두기의 흐름을 녹화로 본다
 if(args.Length==4&&args[0]=="--collection-read-capture"){CollectionChecks.ReadCapture(catalog,args[1],args[2],args[3]);return;}   // 받아 둔 화면을 제품의 코드로 읽어 견준다
 if(args.Length==3&&args[0]=="--collection-read-pngs"){CollectionChecks.ReadPngs(catalog,args[1],args[2]);return;} if(args.Length==3&&args[0]=="--collection-read-timing"){CollectionChecks.ReadTiming(catalog,args[1],args[2]);return;}   // --collection-read-pngs: 1920×1080 PNG 를 읽어 BEST·칸의 여유를 적는다(줄이는 방법 견주기). --collection-read-timing: 멈춘 화면으로 읽기의 부분마다 걸리는 시간을 잰다(속도 줄이기)
 if(args.Length>=4&&args[0]=="--collection-artist-check"){CollectionChecks.Artists(catalog,args[1],args[2],args[3..]);return;}   // 목록이 멈춘 화면에서 후보 아티스트를 가려 본다
 if(args.Length is 2 or 3&&args[0]=="--collection-window-previews"){CollectionWindowChecks.Previews(catalog,args[1],args.Length==3?args[2]:null);return;}   // 컬렉션 가져오기 창의 단계마다 그림을 남긴다
 if(args.Length==6&&args[0]=="--collection-main-check"){CollectionWindowChecks.MainCheck(catalog,args[1],args[2],int.Parse(args[3]),int.Parse(args[4]),args[5]);return;}   // 메인 창에서 컬렉션 가져오기를 연다(빈 기록의 새 시험 폴더에서. 창이 뜬다)
 if(args.Length==6&&args[0]=="--collection-window-check"){CollectionWindowChecks.Check(catalog,args[1],args[2],int.Parse(args[3]),int.Parse(args[4]),args[5]);return;}   // 창에 가짜 게임을 물려 읽기를 돌려 본다
 if(args.Length>=2&&args[0]=="--collection-scan-window"){CollectionWindowChecks.Live(catalog,args[1],args.Length<3||args[2]!="flash",args.Length>3?args[3]:null,args.Length>4?int.Parse(args[4]):0,args.Length>5?int.Parse(args[5]):int.MaxValue);return;}   // 실제 게임으로 읽는다(사람이 지켜볼 때만 돌린다). 읽은 점수와 캡처를 결과 폴더에 남기고, 완료 화면에서 적용하면 그 폴더의 시험용 기록(records.json)을 갱신한다. 개인 기록은 쓰지 않는다
 if(args.Length==2&&args[0]=="--collection-keys-self"){CollectionKeyChecks.Self(args[1]);return;}   // 키 보내기의 Windows 호출과 단축키 등록을 이 도구의 창으로 본다(게임이 필요 없다)
 if(args.Length is 5 or 6&&args[0]=="--collection-keys"){CollectionKeyChecks.Run(catalog,args[1],int.Parse(args[2]),int.Parse(args[3]),args[4],args.Length==6&&args[5]=="short");return;}   // 실제 게임에 ↑·↓ 를 보내 보고 화면이 따라오는 것을 잰다(사람이 지켜볼 때만 돌린다). 기록을 바꾸지 않고, 잰 결과를 결과 폴더에 쓴다
 if(args.Length==6&&args[0]=="--collection-scan-check"){CollectionScanChecks.Run(catalog,args[1],args[2],int.Parse(args[3]),int.Parse(args[4]),args[5]);return;}   // 컬렉션 읽기의 진행을 가짜 게임과 가짜 시계로 본다(키를 보내지 않는다)
 if(args.Length==6&&args[0]=="--collection-scan-stills"){CollectionScanChecks.Stills(catalog,args[1],args[2],int.Parse(args[3]),int.Parse(args[4]),args[5]);return;}   // 멈춘 실제 화면에서 고른 줄·목록의 끝·위아래 줄의 아티스트를 읽어 본다
 if(args.Length>=6&&args[0]=="--collection-read-check"){CollectionChecks.Read(catalog,args[1],int.Parse(args[2]),int.Parse(args[3]),args[4],args[5],args[6..]);return;}   // 컬렉션 화면을 읽는 코드에 녹화를 넣어 견줄 값과 견준다
 if(args.Length==3&&args[0]=="--collection-apply-measure"){CollectionApplyTests.Measure(catalog,args[1],args[2]);return;}   // 실제로 읽은 값으로 컬렉션 가져오기의 셈·저장·되돌리기를 잰다(합성 프로필)
 if(args.Length==2&&args[0]=="--catalog-update-check"){CatalogUpdateChecks.Run(catalog,args[1]);return;}   // 곡 목록을 바꾼 뒤 늘어난 곡·패턴을 앱이 쓰는지 본다(tools\update_catalog.py 의 다음 차례)
 if(args.Length==2&&args[0]=="--audio-reference-build"){AudioReferenceChecks.Build(args[1]);return;}   // 동명곡 식별용 음향 기준을 만든다(tools\build_audio_references.py 가 부른다)
 if(args.Length==2&&args[0]=="--audio-reference-check"){AudioReferenceChecks.Check(args[1]);return;}
 if(args.Contains("--update-windows-check")){UpdateTests.Previews();return;}   // 자동 업데이트의 창을 그림으로 남긴다
 if(args.Contains("--update-official-check")){UpdateTests.Official();return;}   // 진짜 공식 저장소의 응답을 읽어 본다(인터넷을 쓴다)
 if(args.Contains("--release-feature-check")){ReleaseFeatureChecks.Run(catalog,args.Length>1?args[1]:DevFixtureChecks.VArchiveFolder);return;}
 if(args.Contains("--store-optimization-check")){StoreOptimizationChecks.Run(catalog);return;} if(args.Length>1&&args[0]=="--test-audio-tone"){ProcessAudioChecks.Tone(int.Parse(args[1]));return;} if(args.Contains("--process-audio-check")){ProcessAudioChecks.Run();return;} if(args.Contains("--ocr-optimization-check")){Task.Run(OcrOptimizationChecks.Run).GetAwaiter().GetResult();return;} if(args.Length>1&&args[0]=="--records-scroll-check"){RecordsScrollChecks.Run(catalog,args[1]);return;} if(args.Length>2&&args[0]=="--ellipsis-report"){OverlayReports.Write(catalog,JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(args[1])),args[2]);return;} if(args.Contains("--records-modal-check")){RecordsModalChecks.Run(catalog);return;} if(args.Length>1&&args[0]=="--records-responsiveness"){RecordsPerformanceChecks.Responsiveness(catalog,args[1]);return;} if(args.Length>1&&args[0]=="--records-performance"){RecordsPerformanceChecks.Run(catalog,args[1]);return;} if(args.Contains("--hotkey-flow-check")){HotkeyFlowChecks.Run(catalog,true);return;} if(args.Contains("--hotkey-probe")){HotkeyFlowChecks.Run(catalog);return;} if(args.Contains("--dialogs-check")){UiChecks.Run(catalog,args.Length>1?args[1]:null);return;} if(args.Contains("--overlay-check")){OverlayToolChecks.Run(catalog,args.Contains("smoke"));return;} if(args.Contains("--screen-fixtures")){Task.Run(()=>DetectionChecks.Fixtures(catalog)).GetAwaiter().GetResult();return;} if(args.Length>0&&args[0]=="--self-test"){SelfTests.Run(catalog);DetectionChecks.Run(catalog);return;} if(args.Length>1&&args[0]=="--audio-check"){DetectionChecks.Audio(args[1]);return;} if(args.Length>1&&args[0]=="--screen-check"){Task.Run(()=>DetectionChecks.Screen(catalog,args[1],args.Length>2?int.Parse(args[2]):0,args.Length>3?args[3]:"LADDERMATCH")).GetAwaiter().GetResult();return;} if(args.Contains("--audio-device-check")){using var device=new LoopbackAudio();Thread.Sleep(250);File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"audio-device-validation.json"),JsonSerializer.Serialize(new{device.Error,device.Seconds,device.TargetProcessId,device.Initialized}));return;} if(args.Contains("--steam-check")){using var steam=new SteamPresence();File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"steam-check.json"),JsonSerializer.Serialize(steam.Read()));return;}

 using var app=new MainForm(catalog,args.Contains("--ui-check")?18766:port); if(args.Length>0&&args[0]=="--ui-check"){app.Show();Application.DoEvents();using var shot=new Bitmap(app.Width,app.Height);app.DrawToBitmap(shot,new Rectangle(0,0,shot.Width,shot.Height));shot.Save(Path.Combine(AppContext.BaseDirectory,"ui-preview.png"));app.Close();return;} if(args.Length>0&&args[0]=="--preview-server"){using var closeTimer=new System.Windows.Forms.Timer(){Interval=60000};closeTimer.Tick+=(_,_)=>app.Close();closeTimer.Start();Application.Run(app);return;} Application.Run(app);
#else
 using var app=new MainForm(catalog,port);
 Application.Run(app);
#endif
 }catch(Exception e){
  Environment.ExitCode=1;
#if DEVELOPMENT_CHECKS
  if(args.Length>0){File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"test-error.txt"),e.ToString());return;}
#endif
  // 이 장치가 없는 이전 버전이 켜져 있으면 포트가 겹친다. 무엇을 하면 되는지 알 수 있게 적는다(글은 StartGuides)
  GuideDialog.ShowAlone(StartGuides.Error(e));
 }}
}





