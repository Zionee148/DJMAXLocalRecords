using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
namespace LadderRecords;
[SupportedOSPlatform("windows10.0.19041")]
partial class MainForm:AppWindow
{
 readonly Catalog catalog;readonly RecordStore store=new(Path.Combine(AppContext.BaseDirectory,"records.json"));readonly Session session=new();readonly Recognizer recognition;readonly OverlayServer server;readonly SteamPresence steam=new();readonly JudgeGate judgeGate=new();LoopbackAudio? audio;SteamChart? steamChart;Song? resolvedSong;double audioAttempt;string? registeredSteamKey;readonly Dictionary<string,float[][]> audioReferences=new();
 readonly ComboBox profiles=new ThemedCombo(),gear=new ThemedCombo(),windows=new ThemedCombo(); Selection? displayedSelection;
 // Recognition uses the original LEFT=0, RIGHT=1, CENTER=2 values.
 int GearIndex=>gear.SelectedItem switch{"RIGHT"=>1,"CENTER"=>2,_=>0};
 readonly ToolTip helpTip=new(){AutoPopDelay=20000,InitialDelay=350,ReshowDelay=150};
 readonly string optionsPath;readonly CheckBox fullscreen=new(){Text="전체화면 사용",AutoSize=true};readonly CheckBox showCurrent=new(){Text="현재 정확도 표시",Checked=true,AutoSize=true};
 readonly Label status=new(){AutoSize=true,MaximumSize=new(480,0)};readonly PictureBox picture=new(){Width=480,Height=270,SizeMode=PictureBoxSizeMode.Zoom,BackColor=Color.Black};
 readonly System.Windows.Forms.Timer timer=new(){Interval=100};readonly Hotkeys hotkeys;bool syncing,busy;HudSample? lastHudRate;RateSource rateSource;bool importing;int epoch;Confirmation? confirmation;JsonElement appearance;long eventId;object? lastEvent;string? finishedKey;RecordsDialog? recordsDialog;bool recordsVisible;
 record HudSample(string ChartKey,decimal Value,long CapturedAt);
 record WindowItem(IntPtr Handle,string Name){public override string ToString()=>Name;}
 [DllImport("user32.dll")]static extern bool GetClientRect(IntPtr h,out Rect r);[DllImport("user32.dll")]static extern bool ClientToScreen(IntPtr h,ref Pt p);[DllImport("user32.dll")]static extern bool IsIconic(IntPtr h);
 [StructLayout(LayoutKind.Sequential)]struct Rect{public int Left,Top,Right,Bottom;}[StructLayout(LayoutKind.Sequential)]struct Pt{public int X,Y;}
 public MainForm(Catalog catalog,int port=18765,bool registerHotkeys=true,string? preferencesPath=null,List<Hotkey>? initialHotkeys=null)
 {
  this.catalog=catalog;
  InitScore();
 optionsPath=preferencesPath??Path.Combine(AppContext.BaseDirectory,"preferences.json");{var options=UserOptions.Load(optionsPath);fullscreen.Checked=options.Fullscreen;scoreImagesOption.Checked=options.DiagnosticImages;Theme.Dark=options.Dark;}session.Valid=c=>catalog.Songs.Any(s=>s.Id==c.SongId&&s.Charts.Contains(new(c.Button,c.Pattern)));recognition=new(catalog);server=new(port,OverlayText.Titles(catalog));LoadAppearance();Text="디맥 개인 기록 저장 오버레이";ClientSize=new(720,600);Font=Theme.Px("맑은 고딕",10);hotkeys=new(Handle,registerHotkeys);HandleCreated+=(_,_)=>{if(hotkeys!=null)try{hotkeys.Rebind(Handle);}catch(Exception e){status.Text=e.Message;}};HandleDestroyed+=(_,_)=>hotkeys?.Detach();
  // 두 칸(사용자 결정, 2026-11-15). 왼쪽은 조작을 쓰는 순서대로 묶고, 오른쪽은 곡 정보·게임 화면·SCORE·상태를 보여 준다. 색과 단추 모양은 Theme 가 입힌다
  const int ContentWidth=480,LeftWidth=540;
  var root=new TableLayoutPanel(){Dock=DockStyle.Fill,AutoScroll=true,ColumnCount=2,RowCount=1,Padding=Padding.Empty};root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
  // 부품을 하나 붙일 때마다 창 전체의 배치를 다시 잡지 않게, 다 만들 때까지 멈췄다가 끝에 한 번만 잡는다(잰 값: 1.5초 → docs\기록\최적화.md)
  SuspendLayout();root.SuspendLayout();Controls.Add(root);scrollArea=root;
  var left=new FlowLayoutPanel(){AutoSize=true,FlowDirection=FlowDirection.TopDown,WrapContents=false,Margin=Padding.Empty,Padding=new(18,12,18,12)};
  var right=new SidePanel(){AutoSize=true,FlowDirection=FlowDirection.TopDown,WrapContents=false,Margin=Padding.Empty,Padding=new(19,12,18,12),Dock=DockStyle.Fill};
  left.SuspendLayout();right.SuspendLayout();root.Controls.Add(left,0,0);root.Controls.Add(right,1,0);
  void Group(string title){left.Controls.Add(new SectionHeader(title){Width=LeftWidth,Margin=new(0,left.Controls.Count==0?0:12,0,2)});}
  void Row(string text,params Control[] controls){var row=new TableLayoutPanel(){AutoSize=true,ColumnCount=2,RowCount=1,Margin=new(0,1,0,1),Padding=Padding.Empty};row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,104));row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));var label=new Label(){Text=text,Width=104,Height=30,TextAlign=ContentAlignment.MiddleLeft,Anchor=AnchorStyles.Left,Margin=Padding.Empty,Tag="dim"};var input=new FlowLayoutPanel(){AutoSize=true,WrapContents=false,Margin=Padding.Empty,Padding=Padding.Empty,Anchor=AnchorStyles.Left};input.Controls.AddRange(controls);row.Controls.Add(label,0,0);row.Controls.Add(input,1,0);left.Controls.Add(row);}
  // 글 칸에 디자인 색의 테두리를 두른다(Windows 가 그리는 테두리는 색을 바꿀 수 없다)
  Control Bordered(TextBox box,int width){box.BorderStyle=BorderStyle.None;box.Width=width-16;box.Margin=Padding.Empty;box.Location=new(7,6);var inner=new Panel(){Tag="field",AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Padding=new(7,6,7,5),Margin=Padding.Empty,Location=new(1,1)};inner.Controls.Add(box);var outer=new Panel(){Tag="border",AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Padding=new(1),Margin=new(3)};outer.Controls.Add(inner);return outer;}
  Button Button(string text,Action action){var b=new Button(){Text=text,AutoSize=true};b.Click+=(_,_)=>Safe(action);return b;}
  foreach(var c in new[]{profiles,gear,windows}){c.DropDownStyle=ComboBoxStyle.DropDownList;c.Width=170;}
  gear.Items.AddRange(new object[]{"LEFT","CENTER","RIGHT"});gear.SelectedIndex=0;
  const string fullscreenExplanation="전체화면으로 플레이할 때 기록 등록 확인창이 뜨면서 게임에서 다른 창으로 전환되는 것을 막는 옵션입니다.\n이 옵션을 켜면 OBS 오버레이 또는 오버레이 팝업에서 점수를 확인한 뒤, 등록·취소 단축키를 사용해야 합니다.";
  var fullscreenHelp=new HelpBadge(){AccessibleName="전체화면 사용 도움말",AccessibleDescription=fullscreenExplanation};
  helpTip.SetToolTip(fullscreenHelp,fullscreenExplanation);
  const string imagesExplanation="SCORE 인식에 문제가 있을 때만 켜세요.\n등록 키를 누른 순간의 게임 화면과, 그 뒤 점수를 읽는 데 쓴 화면을 그림으로 남깁니다.\n그림은 진단 기록 폴더의 images 아래에 200MB까지 남고, 넘으면 오래된 것부터 지워집니다. 어디로도 보내지 않습니다.";
  var imagesHelp=new HelpBadge(){AccessibleName="진단 그림 저장 도움말",AccessibleDescription=imagesExplanation};helpTip.SetToolTip(imagesHelp,imagesExplanation);
  Group("감지 설정");
  windows.Width=320;Row("게임 창",windows,Button("목록 갱신",RefreshWindows));
  Row("기어 위치",gear);Row("화면 모드",fullscreen,fullscreenHelp);
  var start=Button("감지 시작",StartDetection);start.Tag="primary";
  Row("감지",start,Button("중지 / 숨기기",()=>{CancelPending();timer.Stop();captureTimer.Stop();epoch++;session.Running=false;EndScorePlay("감지 중지");ResetRecognition();RefreshState();}),Button("새로 감지하기",()=>Action(3)));
  Group("기록");
  profiles.Width=140;Row("기록 프로필",profiles,Button("새로",()=>ProfileAction(false)),Button("복사",()=>ProfileAction(true)),Button("이름 변경",()=>{EnsureProfileFree();var name=Ask("프로필 이름",store.Active.Name);if(name!=null){EnsureProfileFree();store.Rename(name);RefreshProfiles();}}),Button("삭제",()=>{EnsureProfileFree();if(ChoiceDialog.Ask(this,"프로필 삭제","현재 프로필의 기록을 삭제할까요?",new InfoBox(){Controls={Boxes.Stats(12.5f,("프로필",store.Active.Name,null),("기록",$"{store.Active.Records.Count:N0}건",null))}},null,null)==DialogResult.Yes){EnsureProfileFree();store.Delete();RefreshProfiles();}}));foreach(var b in profiles.Parent!.Controls.OfType<Button>()){b.AutoSizeMode=AutoSizeMode.GrowAndShrink;b.Padding=new(3,0,3,0);}
  var register=Button("등록",()=>Action(2));register.Tag="primary";
  Row("등록",register,Button("직전 저장 되돌리기",UndoWithDetails));
  Row("기록 관리",Button("등록한 기록 보기",BrowseRecords),Button("수동 기록 설정",ManageRecord));
  Group("오버레이와 설정");
  Row("OBS 브라우저",Bordered(new TextBox(){Text=OverlayUrl,ReadOnly=true},290),Button("오버레이 팝업",OpenOverlayPopup));
  showCurrent.Margin=new(8,7,3,0);scoreImagesOption.Margin=new(8,7,3,0);fullscreen.Margin=new(3,5,3,0);Row("오버레이",Button("디자인 설정",EditAppearance),showCurrent);
  Row("설정",Button("단축키 설정",()=>{using var d=new HotkeyDialog(hotkeys.Items.Count==5?hotkeys.Items:Hotkeys.Defaults());if(d.ShowDialog(this)==DialogResult.OK){hotkeys.Set(d.Value);File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"hotkeys.json"),JsonSerializer.Serialize(d.Value));}}),Button("V아카이브 가져오기",ImportArchive));
  Row("진단 기록",Button("진단 기록 폴더 열기",OpenDiagnostics),scoreImagesOption,imagesHelp);
  Row("업데이트",UpdateRow());   // MainForm.Update.cs
  var songHead=new Panel(){Width=ContentWidth,Height=26,Margin=Padding.Empty};var songName=new SectionHeader("곡 정보"){Rule=false,Location=new(0,2)};songName.Width=songName.TextWidth+2;stateChip.Location=new(songName.Width+8,2);stateChip.Margin=Padding.Empty;modeSwitch.Location=new(ContentWidth-modeSwitch.Width,1);songHead.Controls.AddRange(new Control[]{songName,stateChip,modeSwitch});right.Controls.Add(songHead);
  right.Controls.Add(songTitle);
  var chips=new FlowLayoutPanel(){AutoSize=false,Width=ContentWidth,Height=24,WrapContents=false,Margin=Padding.Empty};chips.Controls.AddRange(new Control[]{buttonChip,patternChip,judgeChip});right.Controls.Add(chips);
  var stats=new TableLayoutPanel(){AutoSize=true,ColumnCount=2,RowCount=2,Margin=new(0,8,0,10)};stats.Controls.Add(new Label(){Text="목표",AutoSize=true,Tag="dim",Margin=new(0,0,28,0),Font=Theme.Px("맑은 고딕",9f)},0,0);stats.Controls.Add(new Label(){Text="현재",AutoSize=true,Tag="dim",Margin=Padding.Empty,Font=Theme.Px("맑은 고딕",9f)},1,0);goalValue.Margin=new(0,0,28,0);stats.Controls.Add(goalValue,0,1);stats.Controls.Add(currentValue,1,1);right.Controls.Add(stats);
  right.Controls.Add(new SectionHeader("게임 화면"){Rule=false,Quiet=true,Width=ContentWidth,Margin=Padding.Empty});picture.Margin=new(0,2,0,10);right.Controls.Add(picture);
  // SCORE 미리 보기와 그 글은 MainForm.Score.cs 에 있다. 이 칸의 너비에 맞추는 것만 여기서 한다
  var scoreHead=new FlowLayoutPanel(){AutoSize=true,WrapContents=false,Margin=Padding.Empty};var scoreName=new SectionHeader("SCORE"){Rule=false,Quiet=true,Margin=Padding.Empty};scoreName.Width=scoreName.TextWidth+10;scoreStatus.Margin=new(0,3,0,0);Steady(scoreStatus,ContentWidth-scoreName.Width);scoreHead.Controls.Add(scoreName);scoreHead.Controls.Add(scoreStatus);right.Controls.Add(scoreHead);scorePicture.Size=new(ContentWidth,ContentWidth*68/640);scorePicture.Margin=new(0,2,0,10);right.Controls.Add(scorePicture);
  right.Controls.Add(new Panel(){Tag="line",Width=ContentWidth,Height=1,Margin=new(0,2,0,8)});status.Margin=Padding.Empty;Steady(status,ContentWidth);right.Controls.Add(status);scoreAdvice.Tag="warn";scoreAdvice.Margin=new(0,4,0,0);Steady(scoreAdvice,ContentWidth);right.Controls.Add(scoreAdvice);
  status.MinimumSize=new(0,status.Font.Height);
  left.ResumeLayout(true);right.ResumeLayout(true);root.ResumeLayout(true);ResumeLayout(true);
  modeSwitch.Toggled+=()=>Safe(()=>{SaveOptions();Theme.ApplyAll();});
  Load+=(_,_)=>{
   var area=Screen.FromControl(this).WorkingArea;
   int frameWidth=Width-ClientSize.Width,frameHeight=Height-ClientSize.Height;
   // 처음 뜨는 크기는 뜬 뒤에 길어지는 글(SCORE 상태 두 줄, 상태 두 줄, 가장 긴 등록 실패 안내)이 들어갈 만큼으로 잡는다.
   // 뜰 때의 짧은 글에 맞추면 글이 길어지는 순간 내용이 창보다 커져 세로 스크롤이 생기고, 그 막대가 너비를 먹어 가로 스크롤까지 생긴다.
   // 게임 화면이 1920×1080 이 아닐 때 SCORE 상태에 붙는 긴 안내까지는 넣지 않는다(그때는 스크롤이 생길 수 있다).
   // 글을 하나 바꿀 때마다 배치를 다시 잡지 않게, 셋을 다 바꾼 뒤 한 번 잡고 크기를 읽는다. 되돌릴 때도 같다.
   var growing=new[]{scoreStatus,status,scoreAdvice};var kept=growing.Select(l=>l.Text).ToArray();
   var held=Theme.Hold(root);
   scoreStatus.Text="가\n가";status.Text="가\n가";
   scoreAdvice.Text=new[]{"LocationUncertain","NotEnoughPlayEvidence","ZeroScore","ModelUnavailable",""}.Select(r=>{var(brief,next)=ScoreFailure.Describe(r);return $"등록 실패: {brief}. {next}";}).MaxBy(t=>t.Length)!;
   Theme.Release(held);
   var wanted=root.PreferredSize;
   held=Theme.Hold(root);
   for(int i=0;i<growing.Length;i++)growing[i].Text=kept[i];
   Theme.Release(held);
   MinimumSize=new(Math.Min(720,area.Width),Math.Min(480,area.Height));
   ClientSize=new(Math.Min(wanted.Width,area.Width-frameWidth),Math.Min(wanted.Height,area.Height-frameHeight));
  };
  profiles.SelectedIndexChanged+=(_,_)=>{if(!syncing&&profiles.SelectedItem is Profile p)Safe(()=>{try{EnsureProfileFree();}catch{RefreshProfiles();throw;}store.Select(p.Id);RefreshState();});};
  scoreImagesOption.CheckedChanged+=(_,_)=>Safe(()=>SaveOptions());fullscreen.CheckedChanged+=(_,_)=>Safe(()=>{SaveOptions();if(fullscreen.Checked){confirmation?.Finish();confirmation=null;}RefreshState();});showCurrent.CheckedChanged+=(_,_)=>RefreshState();gear.SelectedIndexChanged+=(_,_)=>{epoch++;if(session.Pending==null){EndScorePlay("기어 위치 변경");ResetRecognition();}RefreshState();};windows.SelectedIndexChanged+=(_,_)=>{epoch++;if(session.Pending==null){EndScorePlay("게임 창 변경");ResetRecognition();}};
  RefreshProfiles();RefreshWindows();LoadAppearance();try{hotkeys.Set(initialHotkeys??(File.Exists(Path.Combine(AppContext.BaseDirectory,"hotkeys.json"))?JsonSerializer.Deserialize<List<Hotkey>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"hotkeys.json")))!:Hotkeys.Defaults()));}catch(Exception e){status.Text=e.Message;}

  captureTimer.Tick+=async(_,_)=>await CaptureTick();timer.Tick+=async(_,_)=>await Tick();FormClosed+=(_,_)=>{epoch++;session.Running=false;timer.Stop();captureTimer.Stop();recordsDialog?.Dispose();hotkeys.Dispose();server.Dispose();recognition.Dispose();steam.Dispose();audio?.Dispose();picture.Image?.Dispose();scorePicture.Image?.Dispose();latestFrame?.Dispose();CancelAnalysis();scoreLink.Dispose();captureTimer.Dispose();helpTip.Dispose();confirmation?.Finish();};RefreshState();
  Shown+=(_,_)=>BeginInvoke(StartUpdateFeatures);
 }
 void Safe(Action action){try{action();}catch(Exception e){status.Text=e.Message;}}
 protected override void WndProc(ref Message m){if(m.Msg==0x0312){int id=m.WParam.ToInt32()-100;Safe(()=>Action(id,true));}base.WndProc(ref m);}
 void Action(int i,bool fromHotkey=false){if(updating)return;   // 업데이트를 시작한 뒤에는 단축키로 들어오는 조작을 받지 않는다
  if(i==4){if(session.Pending!=null)Decide(true);return;}if(i==5){if(activeRegistration!=null){CancelAnalysis();RefreshState();}else if(session.Pending!=null)Decide(false);return;}if(activeRegistration!=null)throw new Exception("점수 확인 또는 취소를 먼저 완료하세요.");if(session.Pending!=null)throw new Exception("저장 확인/취소를 먼저 선택하세요.");switch(i){case 2:RequestRegistration();break;case 6:StartDetection();break;case 3:RestartDetection();break;}RefreshState();}
 void RestartDetection(){finishedKey=registeredSteamKey=null;EndScorePlay("새로 감지하기");ResetRecognition();session.Running=true;epoch++;timer.Start();captureTimer.Start();status.Text="새로 감지하고 있습니다.";}
 void ResetRecognition(){latestFrame?.Dispose();latestFrame=null;latestFrameAt=0;latestFrameKey=null;scorePicture.Image?.Dispose();scorePicture.Image=null;drawnArea=null;UpdateScoreStatus();lastHudRate=null;rateSource=RateSource.Unknown;audio?.Dispose();audio=null;steamChart=null;resolvedSong=null;audioAttempt=0;judgeGate.Reset();recognition.ResetContext();session.Next();}
 static Bitmap CaptureFrame(IntPtr handle,out Size size){if(IsIconic(handle)||!GetClientRect(handle,out var r)||r.Right<100||r.Bottom<100)throw new Exception("게임 창을 확인하세요.");size=new(r.Right,r.Bottom);var pt=new Pt();ClientToScreen(handle,ref pt);var raw=new Bitmap(r.Right,r.Bottom);
  try{using(var g=Graphics.FromImage(raw))g.CopyFromScreen(pt.X,pt.Y,0,0,raw.Size);
   if(raw.Width==1920&&raw.Height==1080)return raw;
   using(raw)return Normalize(raw);
  }catch{raw.Dispose();throw;}}
 void StartDetection(){if(session.Pending!=null)throw new Exception("저장 확인/취소를 먼저 선택하세요.");if(session.Running)return;session.Running=true;finishedKey=registeredSteamKey=null;epoch++;EndScorePlay("감지 시작");ResetRecognition();timer.Start();captureTimer.Start();RefreshState();}
 decimal? HeldHudRate()=>rateSource==RateSource.Hud&&lastHudRate?.ChartKey==session.Locked?.Key?lastHudRate?.Value:null;
 protected virtual SteamSnapshot ReadSteamSnapshot()=>steam.Read();
 protected virtual Task<ScreenReading> ReadRegistration(string mode)=>ReadScreen(mode);
 protected virtual Bitmap CaptureGameFrame(IntPtr handle){var frame=CaptureFrame(handle,out var size);Interlocked.Exchange(ref captureSize,((long)size.Width<<32)|(uint)size.Height);return frame;}
 async Task<ScreenReading> ReadScreen(string mode){if(windows.SelectedItem is not WindowItem window)throw new Exception("게임 창을 확인하세요.");int gearIndex=GearIndex;long capturedAt=0;bool cached=latestFrame!=null&&latestFrameKey==steamChart?.Key&&Stopwatch.GetElapsedTime(latestFrameAt).TotalSeconds<.4;capturedAt=cached?latestFrameAt:Stopwatch.GetTimestamp();using var frame=cached?(Bitmap)latestFrame!.Clone():await Task.Run(()=>CaptureGameFrame(window.Handle));if(IsDisposed||Disposing)throw new OperationCanceledException();if(ShouldUpdatePreview()){var previewSize=picture.ClientSize;var preview=await Task.Run(()=>MakePreview(frame,previewSize));SetPreview(preview);}bool scoreLocated=ScoreLocated;
 var reading=await Task.Run(async()=>{
  return await recognition.Read(frame,mode,gearIndex,scoreLocated);
 });
 if(scoreLink.Available)
  reading=reading with{Detail=reading.Detail+(ScoreLocated?" · SCORE 위치 확인됨":" · SCORE 위치 탐색 중")};
 RefreshScorePreview(frame);return reading with{CapturedAt=capturedAt};}
#if DEVELOPMENT_CHECKS
 void ShowRegistration(bool fromHotkey)=>ShowMeasuredRegistration(null,null);
#endif
 void ShowMeasuredRegistration(DateTime? measuredAt,double? rateAge,decimal? measuredAccuracy=null)
 {
  var snapshot=session.Begin(store.Active.Id,measuredAt,rateAge,measuredAccuracy);pendingPlaySerial=scoreLink.PlaySerial;
  if(!fullscreen.Checked){
   confirmation=new Confirmation(PendingView(snapshot.Chart,snapshot.Accuracy,null,rateAge is double age?$"{age:F1}초 전 인식값":null));
   confirmation.Decision=Decide;
   confirmation.Show();
  }
  status.Text="기록 등록 확인 대기";
  RefreshState();
 }
 void Decide(bool yes){try{if(session.Pending==null)return;if(yes){var old=session.Pending!;var previousRecord=store.Get(old.Chart);var previous=previousRecord?.Accuracy;var result=session.Confirm(store);finishedKey=old.Chart.Key;registeredSteamKey=old.SteamKey??steamChart?.Key;registeredPlaySerial=pendingPlaySerial;audio?.Dispose();audio=null;lastEvent=new{id=++eventId,text=result,song=OverlayText.Title(catalog,catalog.Songs.Single(s=>s.Id==old.Chart.SongId)),button=old.Chart.Button,pattern=old.Chart.Pattern,judge=old.Chart.Judge,value=old.Accuracy,score=old.RawScore??ScoreValue.FromAccuracy(old.Accuracy),previous,previousScore=previousRecord?.Value,kind=previousRecord==null?"first":(old.RawScore??ScoreValue.FromAccuracy(old.Accuracy))>previousRecord.Value?"best":"kept",at=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()};epoch++;status.Text=result;}else{session.Cancel();status.Text="등록 취소";}confirmation?.Finish();confirmation=null;RefreshState();}catch(Exception e){status.Text="저장 실패: "+e.Message;}}
 void CancelPending(){CancelAnalysis();session.Cancel();confirmation?.Finish();confirmation=null;}
 string Describe(Selection c){var song=catalog.Songs.Single(s=>s.Id==c.SongId);return $"{song.Name} ({song.Artist}) · {c.Button}B · {c.Pattern} · {c.Judge}판정";}
 void SyncSelection(Selection c){displayedSelection=c;var song=catalog.Songs.Single(s=>s.Id==c.SongId);ShowSong(song,$"{c.Button}B",c.Pattern,c.Judge+"판정");}
 void RefreshState(){bool blocked=activeRegistration!=null||session.Pending!=null;gear.Enabled=windows.Enabled=profiles.Enabled=!blocked;var c=session.Locked??session.Candidate;var record=c==null?null:store.Get(c);ShowStats(record?.Accuracy,session.Current,session.Locked!=null,c!=null);server.Set(new{visible=session.Running,label=session.Locked==null?"악곡을 감지하고 있습니다":OverlayText.Describe(catalog,session.Locked),goal=session.Locked==null?null:record?.Accuracy, current=session.Current,showCurrent=showCurrent.Checked,appearance,overlayTitle=session.Locked==null?null:OverlayText.Title(catalog,catalog.Songs.Single(s=>s.Id==session.Locked.SongId)),overlayDetails=session.Locked==null?null:$"{session.Locked.Button}B · {session.Locked.Pattern} · {session.Locked.Judge}판정",confirmation=session.Pending==null?null:new { label=OverlayText.Describe(catalog,session.Pending.Chart),song=OverlayText.Title(catalog,catalog.Songs.Single(s=>s.Id==session.Pending.Chart.SongId)),button=session.Pending.Chart.Button,pattern=session.Pending.Chart.Pattern,judge=session.Pending.Chart.Judge,accuracy=session.Pending.Accuracy,score=session.Pending.RawScore??ScoreValue.FromAccuracy(session.Pending.Accuracy),rateAge=session.Pending.RateAge,previous=store.Get(session.Pending.Chart)?.Accuracy,confirm=Hotkeys.Format(hotkeys.Items[2]),cancel=Hotkeys.Format(hotkeys.Items[3]) },analysis=analysisState,tone=record==null||session.Current==null?"unknown":session.Current>=record.Accuracy?"above":"below",lastEvent});}
 void RefreshProfiles(){syncing=true;try{profiles.Items.Clear();profiles.Items.AddRange(store.Data.Profiles.Cast<object>().ToArray());profiles.SelectedItem=store.Active;}finally{syncing=false;}RefreshState();}
 void ProfileAction(bool copy){EnsureProfileFree();var name=Ask(copy?"복사할 이름":"새 프로필",copy?store.Active.Name+" 복사":"");if(name!=null){EnsureProfileFree();store.Create(name,copy);RefreshProfiles();}}
 string? Ask(string title,string initial){using var f=new AppWindow(){Text=title,ClientSize=new(390,120),StartPosition=FormStartPosition.CenterParent};var t=new TextBox(){Text=initial,Location=new(15,15),Width=350,MaxLength=40};var ok=new Button(){Text="확인",Location=new(170,65),DialogResult=DialogResult.OK};var cancel=new Button(){Text="취소",Location=new(270,65),DialogResult=DialogResult.Cancel};f.Controls.AddRange(new Control[]{t,ok,cancel});f.AcceptButton=ok;f.CancelButton=cancel;return f.ShowDialog(this)==DialogResult.OK?t.Text:null;}
 string OverlayUrl=>$"http://127.0.0.1:{server.Port}/";
 async void OpenOverlayPopup()
 {
  try{await OverlayPopup.Show(server,appearance,showCurrent.Checked);}
  catch(Exception e){if(!IsDisposed&&!Disposing)status.Text=e.Message;}
 }
 async void ImportArchive()
 {
  if(importing)return;
  var nickname=Ask("V아카이브 닉네임","");
  if(string.IsNullOrWhiteSpace(nickname))return;
  var profileId=store.Active.Id;
  importing=true;
  try{
   status.Text="V아카이브 4B·5B·6B·8B 기록을 불러오는 중입니다.";
   var scores=await VArchiveImport.Fetch(catalog,nickname.Trim());
   if(IsDisposed||Disposing)return;
   if(store.Active.Id!=profileId)
    throw new Exception("선택한 프로필이 바뀌었습니다. 가져오기를 다시 눌러 주세요.");
   var changes=store.PreviewImport(scores);
   var added=changes.Count(change=>change.Before==null);
   if(changes.Count==0){
    status.Text="V아카이브에서 가져올 새 기록이나 경신 기록이 없습니다.";
    NoticeDialog.Show(this,"V아카이브 가져오기",status.Text);
    return;
   }
   if(ChoiceDialog.Ask(this,"V아카이브 가져오기",null,new InfoBox(){Controls={Boxes.Stats(12.5f,("프로필",store.Active.Name,null),("새 등록",$"{added:N0}건",null),("최고 점수 경신",$"{changes.Count-added:N0}건",null))}},"V아카이브는 상세 점수 정보를 제공하지 않아, 정확도(%)를 기준으로 십의 자리 이하를 버린 점수로 환산합니다. 실제 게임 점수와 차이가 있을 수 있습니다.","노말 판정 기록으로 가져올까요?")!=DialogResult.Yes){
    status.Text="V아카이브 가져오기를 취소했습니다.";
    return;
   }
   if(store.Active.Id!=profileId)
    throw new Exception("선택한 프로필이 바뀌었습니다. 가져오기를 다시 눌러 주세요.");
   int imported=store.Import(scores);
   status.Text=$"V아카이브 기록 {imported:N0}건을 반영했습니다.";
   RefreshState();
  }catch(Exception e){if(!IsDisposed&&!Disposing){status.Text="V아카이브 가져오기 실패 · "+e.Message;NoticeDialog.Show(this,"V아카이브 가져오기",status.Text);}}
  finally{importing=false;}
 }
 void BrowseRecords(){if(recordsDialog is {IsDisposed:false,Visible:true}){recordsDialog.Activate();return;}if(recordsDialog==null||recordsDialog.IsDisposed||recordsDialog.ProfileId!=store.Active.Id){recordsDialog?.Dispose();var dialog=new RecordsDialog(catalog,store,Path.Combine(AppContext.BaseDirectory,"records-view.json")){KeepAlive=true};recordsDialog=dialog;dialog.VisibleChanged+=(_,_)=>{recordsVisible=dialog.Visible;if(!IsDisposed&&!Disposing){profiles.Parent!.Enabled=!recordsVisible;if(!recordsVisible)RefreshState();}};}recordsDialog.PrepareForShow();recordsDialog.Show(this);}
 void ManageRecord(){using var d=new RecordDialog(catalog,store,session.Locked??session.Candidate??displayedSelection);if(d.ShowDialog(this)==DialogResult.OK){store.SetManual(d.Chart,d.Value,d.PlayCount,rawScore:d.RawScore);RefreshState();}}
 void LoadAppearance()
 {
  appearance=BadgeDesign.Defaults;
  string path=Path.Combine(AppContext.BaseDirectory,"appearance.json");
  if(!File.Exists(path))return;
  try{
   using var saved=JsonDocument.Parse(File.ReadAllText(path));
   var values=appearance.EnumerateObject().ToDictionary(p=>p.Name,p=>(object)(p.Value.ValueKind==JsonValueKind.String?p.Value.GetString()!:p.Value.GetInt32()));
   foreach(string key in values.Keys.ToArray()){
    if(!saved.RootElement.TryGetProperty(key,out var item))continue;
    if(values[key] is int&&item.ValueKind==JsonValueKind.Number&&item.TryGetInt32(out int number))
     values[key]=key.EndsWith("Size")?Math.Clamp(number,12,64):Math.Clamp(number,0,100);
    else if(values[key] is string&&item.ValueKind==JsonValueKind.String&&item.GetString() is string value
     &&(key=="Font"&&!string.IsNullOrWhiteSpace(value)||System.Text.RegularExpressions.Regex.IsMatch(value,"^#[0-9a-fA-F]{6}$")))
     values[key]=value;
   }
   appearance=JsonSerializer.SerializeToElement(values);
  }catch(JsonException){}catch(IOException){}
 }
 void EditAppearance(){using var d=new StyleDialog(appearance,catalog,showCurrent.Checked,hotkeys.Items);if(d.ShowDialog(this)==DialogResult.OK){appearance=d.Value;File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"appearance.json"),appearance.GetRawText());RefreshState();}}
 // 프로세스마다 주 창을 물으면(Process.MainWindowHandle) 그때마다 화면의 모든 창을 훑어 프로세스 300개에 0.3초가 든다(잰 값. docs\기록\최적화.md).
 // 창을 한 번만 훑어 프로세스별 주 창을 모은다. 주 창의 기준은 .NET 과 같다: 주인이 없고 보이는 최상위 창 가운데 맨 앞의 것. 목록의 순서도 전처럼 프로세스 순서다.
 delegate bool EachWindow(IntPtr h,IntPtr l);
 [DllImport("user32.dll")]static extern bool EnumWindows(EachWindow each,IntPtr l);[DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr h,out uint process);[DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr h);[DllImport("user32.dll")]static extern IntPtr GetWindow(IntPtr h,uint relation);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetWindowTextLength(IntPtr h);[DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetWindowText(IntPtr h,System.Text.StringBuilder text,int max);
 internal static Dictionary<int,IntPtr> MainWindows(){var found=new Dictionary<int,IntPtr>();EnumWindows((h,_)=>{if(GetWindow(h,4)==IntPtr.Zero&&IsWindowVisible(h)){GetWindowThreadProcessId(h,out uint process);found.TryAdd((int)process,h);}return true;},IntPtr.Zero);return found;}
 internal static string WindowTitle(IntPtr h){int length=GetWindowTextLength(h);if(length<=0)return"";var text=new System.Text.StringBuilder(length+1);GetWindowText(h,text,text.Capacity);return text.ToString();}
 void RefreshWindows(){windows.Items.Clear();int best=-1;var main=MainWindows();foreach(var process in Process.GetProcesses())try{using(process){if(process.Id==Environment.ProcessId||!main.TryGetValue(process.Id,out var handle))continue;int index=windows.Items.Add(new WindowItem(handle,WindowTitle(handle)));if(process.ProcessName.Equals("DJMAX RESPECT V",StringComparison.OrdinalIgnoreCase))best=index;}}catch{}if(windows.Items.Count>0)windows.SelectedIndex=best>=0?best:0;}
 static Bitmap Normalize(Bitmap raw){var b=new Bitmap(1920,1080);using var g=Graphics.FromImage(b);g.DrawImage(raw,0,0,1920,1080);return b;}
 static Bitmap MakePreview(Bitmap source,Size size){var image=new Bitmap(Math.Max(1,size.Width),Math.Max(1,size.Height));using var graphics=Graphics.FromImage(image);graphics.DrawImage(source,new Rectangle(Point.Empty,image.Size));return image;}
 bool ShouldUpdatePreview()=>!recordsVisible&&!IsDisposed&&!Disposing&&Visible&&WindowState!=FormWindowState.Minimized&&picture.Visible&&picture.Parent!=null&&OnScreen(picture);
 void SetPreview(Bitmap image){if(!ShouldUpdatePreview()){image.Dispose();return;}var old=picture.Image;picture.Image=image;old?.Dispose();}
 void ShowImage(Bitmap b){if(recordsVisible)return;SetPreview(MakePreview(b,picture.ClientSize));}
 async Task Tick(){if(busy||activeRegistration!=null||!session.Running)return;busy=true;int captureEpoch=epoch;try{
  var snapshot=ReadSteamSnapshot();if(session.Pending!=null)return;
  if(!snapshot.Available){session.Current=HeldHudRate();judgeGate.Observe(null,0);status.Text=snapshot.Detail;RefreshState();return;}
  if(snapshot.Chart==null){registeredSteamKey=finishedKey=null;if(steamChart!=null)ResetRecognition();session.Current=null;status.Text=snapshot.Detail;RefreshState();return;}
  var chart=snapshot.Chart;
  if(registeredSteamKey==chart.Key&&registeredPlaySerial==scoreLink.PlaySerial){session.Current=null;status.Text="다음 곡을 기다리고 있습니다.";RefreshState();return;}
  if(steamChart?.Key!=chart.Key){ResetRecognition();steamChart=chart;registeredSteamKey=finishedKey=null;var candidates=SteamSongs.Find(catalog,chart);if(candidates.Count==1){resolvedSong=candidates[0];ShowSteamChart(chart,resolvedSong);}else if(candidates.Count>1){audio=new LoopbackAudio();status.Text="동명곡 소리 확인 중";}else status.Text="곡 목록에서 스팀 곡 정보를 찾지 못했습니다: "+chart.Name;}
  double now=Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency;
  if(resolvedSong==null){var candidates=SteamSongs.Find(catalog,chart);if(candidates.Count==0){status.Text="곡 목록에 없는 스팀 곡: "+chart.Name;RefreshState();return;}
   if(audio?.Error!=null){status.Text=audio.Error;RefreshState();return;}
   if(audio!=null&&audio.Seconds>=8&&now-audioAttempt>=1){audioAttempt=now;var clip=audio.Snapshot();try{
    var cached=new Dictionary<string,float[][]>(audioReferences);var analysis=await Task.Run(()=>{var references=candidates.ToDictionary(candidate=>candidate.Id,candidate=>cached.TryGetValue(candidate.Id,out var reference)?reference:AudioIdentity.Load(Path.Combine(AppContext.BaseDirectory,"data","audio",candidate.SourceId+".bin")));return (References:references,Decision:AudioIdentity.Decide(AudioIdentity.Features(clip),references));});if(captureEpoch!=epoch)return;foreach(var pair in analysis.References)audioReferences[pair.Key]=pair.Value;var decision=analysis.Decision;
    if(decision.SongId!=null){resolvedSong=candidates.Single(s=>s.Id==decision.SongId);ShowSteamChart(chart,resolvedSong);audio.Dispose();audio=null;}else status.Text="동명곡 소리 재확인 중 · 1초마다 비교";
   }catch(Exception e){status.Text="동명곡 소리 확인: "+e.Message;}}
   else status.Text=$"동명곡 소리 확인 중 · {audio?.Seconds??0:F1}/8초";
  }
  var reading=await ReadScreen(chart.Mode);if(captureEpoch!=epoch||activeRegistration!=null)return;
  bool sourceChanged=reading.Source!=RateSource.Unknown&&rateSource!=RateSource.Unknown&&reading.Source!=rateSource;
  bool hudJudgeChanged=reading.Source==RateSource.Hud&&reading.Judge!=null&&judgeGate.Confirmed!=null&&reading.Judge!=judgeGate.Confirmed;
  if(sourceChanged||hudJudgeChanged){if(sourceChanged)EndScorePlay("RATE 출처 변경");lastHudRate=null;judgeGate.Reset();session.Next();}
  if(reading.Source!=RateSource.Unknown)rateSource=reading.Source;
  string? judge=judgeGate.Observe(reading.Judge,Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency);
  if(resolvedSong!=null&&judge!=null){
   var selected=new Selection(resolvedSong.Id,chart.Button,chart.Pattern,judge,0);
   session.Detect(selected);SyncSelection(selected);
   if(reading.Source==RateSource.Hud&&reading.Rate is decimal valid)
    lastHudRate=new(selected.Key,valid,reading.CapturedAt);
   session.Current=reading.Rate??HeldHudRate();status.Text=reading.Detail;
  }
  else{session.Current=null;if(resolvedSong!=null)status.Text="곡 정보 고정 · 판정 1초 확인 중 · "+reading.Detail;}
  RefreshState();
 }catch(Exception e){if(captureEpoch!=epoch||IsDisposed||Disposing)return;session.Current=HeldHudRate();judgeGate.Observe(null,0);status.Text=e.Message;RefreshState();}finally{FinishReading();}}
 void ShowSteamChart(SteamChart chart,Song song){ShowSong(song,$"{chart.Button}B",chart.Pattern,"확인 중");}
}
