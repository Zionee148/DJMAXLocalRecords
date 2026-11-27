using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
namespace LadderRecords;
[SupportedOSPlatform("windows10.0.19041")]
partial class MainForm:AppWindow
{
 readonly Catalog catalog;readonly RecordStore store=UserFiles.Open(UserFiles.At(UserFiles.Records));readonly Session session=new();readonly Recognizer recognition;readonly OverlayServer server;readonly SteamPresence steam=new();readonly JudgeGate judgeGate=new();LoopbackAudio? audio;SteamChart? steamChart;Song? resolvedSong;double audioAttempt;string? registeredSteamKey;readonly Dictionary<string,float[][]> audioReferences=new();
 readonly ComboBox profiles=new ThemedCombo(),gear=new ThemedCombo(),windows=new ThemedCombo(); Selection? displayedSelection;
 // Recognition uses the original LEFT=0, RIGHT=1, CENTER=2 values.
 int GearIndex=>gear.SelectedItem switch{"RIGHT"=>1,"CENTER"=>2,_=>0};
 readonly ToolTip helpTip=new(){AutoPopDelay=20000,InitialDelay=350,ReshowDelay=150};
 readonly string optionsPath;bool keptFullscreen;SenderSettings? savedSender;   // 예전 설정 파일의 '전체화면 사용' 값. 쓰지 않고 그대로 다시 적는다(UserOptions 의 설명)
 readonly CheckBox showCurrent=new(){Text="현재 점수 표시",Checked=true,AutoSize=true};
 const int MainWidth=Theme.MainWidth,MainHeight=Theme.MainHeight;   // 주 화면의 내용 크기(제목 띠 제외. 배율 100% 때의 값. 창의 크기 단계만큼 커진다)
 readonly Label status=new(){AutoSize=true,UseMnemonic=false};readonly PictureBox picture=new(){Width=455,Height=256,SizeMode=PictureBoxSizeMode.Zoom,BackColor=Color.Black};
 readonly System.Windows.Forms.Timer timer=new(){Interval=100};readonly Hotkeys hotkeys;bool syncing,busy;HudSample? lastHudRate;RateSource rateSource;bool importing,collecting;int epoch;JsonElement appearance;long eventId;object? lastEvent;Selection? savedChart;decimal? savedAccuracy;bool eventEnded;string? finishedKey;RecordsDialog? recordsDialog;bool recordsVisible;
 record HudSample(string ChartKey,decimal Value,long CapturedAt);
 record WindowItem(IntPtr Handle,string Name){public override string ToString()=>Name;}
 [DllImport("user32.dll")]static extern bool GetClientRect(IntPtr h,out Rect r);[DllImport("user32.dll")]static extern bool ClientToScreen(IntPtr h,ref Pt p);[DllImport("user32.dll")]static extern bool IsIconic(IntPtr h);
 [StructLayout(LayoutKind.Sequential)]struct Rect{public int Left,Top,Right,Bottom;}[StructLayout(LayoutKind.Sequential)]struct Pt{public int X,Y;}
 public MainForm(Catalog catalog,int port=18765,bool registerHotkeys=true,string? preferencesPath=null,List<Hotkey>? initialHotkeys=null)
 {
  this.catalog=catalog;
  InitScore();
 optionsPath=preferencesPath??UserFiles.At("preferences.json");
  {
   // 설정 파일이 없을 때 정해진 테마(새로 설치했으면 다크, 쓰던 사람이면 라이트. UserOptions.Load)는 바로 적어 둔다. 기록 파일이 생긴 뒤에 켜도 같은 테마로 뜨게
   bool first=!File.Exists(optionsPath);var options=UserOptions.Load(optionsPath);keptFullscreen=options.Fullscreen;savedSender=options.Sender;scoreImagesOption.Checked=options.DiagnosticImages;Theme.Dark=options.Dark;
   // 앱 표시 크기의 숫자도 처음 정해진 값을 바로 적어 둔다(그 뒤에 Windows 배율이 바뀌어도 이 값이 남는다). 이 프로그램이 뜰 때 정한 값(Theme.Chosen)이다
   scaleChoice.Items.AddRange(Theme.Steps.Select(step=>(object)$"{step}%").ToArray());scaleChoice.SelectedIndex=Math.Max(0,Array.IndexOf(Theme.Steps,Theme.Chosen));
   if(first||options.Scale!=Theme.Chosen)try{(options with{Scale=Theme.Chosen,Small=Theme.Chosen==100}).Save(optionsPath);}catch(IOException){}catch(UnauthorizedAccessException){}
  }
  session.Valid=c=>catalog.Songs.Any(s=>s.Id==c.SongId&&s.Charts.Contains(new(c.Button,c.Pattern)));recognition=new(catalog);server=new(port,OverlayText.Titles(catalog));LoadAppearance();Text="디맥 개인 기록 저장 오버레이";
  // 주 화면은 크기를 바꿀 수 없는 창이다(최소화와 닫기만). 치수는 디자인 기준의 100% 때 값이다.
  // 창의 높이는 처음부터 정해 둔다: 왼쪽에 투컴 주소 한 줄이 더 생기고 오른쪽 아래에 가장 긴 등록 실패 안내가 함께 떠도 창과 미리 보기의 자리가 그대로이게
  FormBorderStyle=FormBorderStyle.FixedSingle;MaximizeBox=false;ClientSize=new(MainWidth,MainHeight);Font=Theme.Px(AppFonts.Ui,9.75f);hotkeys=new(Handle,registerHotkeys);HandleCreated+=(_,_)=>{if(hotkeys!=null)try{hotkeys.Rebind(Handle);}catch(Exception e){status.Text=e.Message;}};HandleDestroyed+=(_,_)=>hotkeys?.Detach();
  // 왼쪽 칸에는 조작을 쓰는 순서대로 묶어 두고, 오른쪽 칸에는 곡 정보·게임 화면·SCORE·상태를 보여 준다. 색과 단추 모양은 Theme 가 적용한다
  const int LeftWidth=536,Inner=506,NameWidth=92,InputWidth=406,X=17,RightInner=455;   // 조작 칸은 415 에서 1 을 남긴다(창이 125% 로 커질 때 반올림으로 오른쪽 끝의 단추가 1px 잘리지 않게)
  var root=new Panel(){Dock=DockStyle.Fill,AutoScroll=true,AutoScrollMinSize=new(MainWidth,MainHeight),Margin=Padding.Empty};
  // 컨트롤을 하나 추가할 때마다 창 전체의 레이아웃을 다시 계산하지 않게, 모두 추가할 때까지 멈췄다가 끝에 한 번만 계산한다(그러지 않으면 부품을 만드는 데 1.5초가 든다. 잰 값)
  SuspendLayout();root.SuspendLayout();Controls.Add(root);scrollArea=root;
  // 왼쪽 칸: 첫 제목 '감지 설정'과 맨 아래 '앱 표시 크기' 줄은 제자리에 두고, 그 사이의 설정이 칸보다 길어지면(투컴 안내 둘과 진단 그림 설명을 함께 편 때 등) 그 사이만 굴린다.
  // 넘치지 않으면 굴리지 않고 설정들의 자리도 그대로다. 오른쪽 칸(미리 보기, 저장·취소)과 창의 크기는 그대로다
  var left=new FlowLayoutPanel(){Bounds=new(0,0,LeftWidth,MainHeight),FlowDirection=FlowDirection.TopDown,WrapContents=false,Margin=Padding.Empty,Padding=new(15,17,0,0)};leftColumn=left;
  leftBody.FlowDirection=FlowDirection.TopDown;leftBody.WrapContents=false;leftBody.AutoSize=true;leftBody.AutoSizeMode=AutoSizeMode.GrowAndShrink;leftBody.Margin=Padding.Empty;
  leftScroll.Controls.Add(leftBody);
  FlowLayoutPanel into=left;
  var right=new SidePanel(){Bounds=new(LeftWidth,0,MainWidth-LeftWidth,MainHeight),Margin=Padding.Empty};rightPanel=right;right.Overlay=DrawScan;
  left.SuspendLayout();right.SuspendLayout();root.Controls.Add(left);root.Controls.Add(right);
  // 묶음 제목(아래로 6), 묶음 사이 18. 줄은 높이 33: 이름 84 + 사이 7 + 조작 칸. 조작 칸의 단추는 높이 27, 사이 4
  void Group(string title){into.Controls.Add(new SectionHeader(title){Size=new(Inner,20),Margin=new(0,into==left&&left.Controls.Count==0?0:18,0,6)});}
  void Row(string text,params Control[] controls)
  {
   var row=new Panel(){Size=new(Inner,33),Margin=Padding.Empty};
   row.Controls.Add(new Label(){Text=text,Bounds=new(0,0,NameWidth+7,33),TextAlign=ContentAlignment.MiddleLeft,Tag="dim",UseMnemonic=false,Font=Theme.Px(AppFonts.UiSemi,9.75f)});   // 이름은 조작 칸과의 사이(7)까지 쓴다(글자가 커질 때 한 줄에 남게)
   var input=new FlowLayoutPanel(){AutoSize=true,WrapContents=false,Margin=Padding.Empty,Padding=Padding.Empty,Location=new(NameWidth+7,3),MinimumSize=new(0,27)};
   // 단추보다 낮은 것(체크 상자, 글, 도움말 표시)은 줄의 가운데에 놓는다
   foreach(var c in controls){c.Margin=new(0,0,4,0);if(c is CheckBox or Label or HelpBadge)c.Anchor=AnchorStyles.None;}
   // 단추가 없는 줄(창 크기)에서도 체크 상자와 글이 줄의 가운데에 오게, 너비가 없는 단추 높이의 기둥을 먼저 넣는다(낮은 것들은 줄에서 가장 높은 것의 가운데에 맞춰진다)
   input.Controls.Add(new Panel(){Size=new(0,27),Margin=Padding.Empty});
   input.Controls.AddRange(controls);row.Controls.Add(input);into.Controls.Add(row);
  }
  // 조작 칸의 너비를 다 쓰는 줄: 첫 컨트롤이 남는 너비를 차지하고 나머지는 제 크기대로 오른쪽에 붙는다(게임 창 목록, OBS 주소)
  Control Stretch(Control wide,params Control[] rest)
  {
   var table=new TableLayoutPanel(){Size=new(InputWidth,27),ColumnCount=rest.Length+1,RowCount=1,Margin=Padding.Empty,Padding=Padding.Empty};
   table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));foreach(var _ in rest)table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));table.RowStyles.Add(new RowStyle(SizeType.Absolute,27));
   wide.Margin=Padding.Empty;wide.Anchor=AnchorStyles.Left|AnchorStyles.Right;table.Controls.Add(wide,0,0);
   for(int i=0;i<rest.Length;i++){rest[i].Margin=new(4,0,0,0);rest[i].Anchor=AnchorStyles.Left;table.Controls.Add(rest[i],i+1,0);}
   return table;
  }
  Button Button(string text,Action action){var b=new Button(){Text=text,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink};b.Click+=(_,_)=>Safe(action);return b;}   // 짧은 이름의 단추가 Windows 의 기본 너비(75)로 늘어나지 않게
  foreach(var c in new[]{profiles,gear,windows}){c.DropDownStyle=ComboBoxStyle.DropDownList;((ThemedCombo)c).Roomy=true;}
  gear.Width=145;profiles.Width=140;profiles.DropDownWidth=280;
  gear.Items.AddRange(new object[]{"LEFT","CENTER","RIGHT"});gear.SelectedIndex=0;
  // 진단 그림 저장의 설명. 물음표를 누르면 그 줄 아래에 펴지고, 다시 누르거나 Esc 로 닫는다. 마우스를 옮겨도 그대로 읽을 수 있다. 체크 상자의 값은 건드리지 않는다
  var imagesHelp=new HelpBadge(){AccessibleName="진단 그림 저장 도움말",AccessibleDescription=ImagesHelpText};imagesHelp.MakePressable();
  imagesHelp.Click+=(_,_)=>ShowImagesHelp(!imagesHelpOpen);   // 펴져 있는지는 따로 기억한 값으로 본다(컨트롤의 Visible 은 창이 뜨기 전에는 언제나 거짓이다)
  Group("감지 설정");
  left.Controls.Add(leftScroll);into=leftBody;
  var refresh=Button("목록 갱신",RefreshWindows);refreshButton=refresh;
  Row("게임 창",Stretch(windows,refresh));
  Row("기어 위치",gear);
  // '화면 모드' 줄과 '전체화면 사용' 체크 상자는 없다. 저장 확인을 주 화면 오른쪽에서 하므로(창을 따로 띄우지 않는다) 창모드와 전체화면이 같은 흐름이다
  var start=Button("감지 시작",StartDetection);start.Tag="primary";
  Row("감지",start,Button("중지 / 숨기기",()=>{StopMotion();CancelPending();timer.Stop();captureTimer.Stop();epoch++;session.Running=false;EndScorePlay("감지 중지");ResetRecognition();Settle("감지 중지");RefreshState();}),Button("새로 감지하기",()=>Action(3)));
  Group("기록");
  Row("기록 프로필",profiles,Button("새로",()=>ProfileAction(false)),Button("복사",()=>ProfileAction(true)),Button("이름 변경",()=>{EnsureProfileFree();var name=NameDialog.Ask(this,"프로필 이름 변경","새 이름",store.Active.Name,"변경");if(name!=null){EnsureProfileFree();store.Rename(name);RefreshProfiles();}}),Button("삭제",()=>{EnsureProfileFree();if(ProfileDeleteDialog.Ask(this,store.Active.Name,store.Active.Plays.Count)){EnsureProfileFree();store.Delete();RefreshProfiles();}}));
  var register=Button("등록",()=>Action(2));register.Tag="primary";registerButton=register;
  Row("등록",register,Button("직전 저장 되돌리기",UndoLast));
  Row("기록 관리",Button("등록한 기록 보기",BrowseRecords),Button("수동 기록 설정",ManageRecord));
  // '컬렉션 가져오기'는 이 줄의 왼쪽에 강조색 테두리·옅은 바탕·굵은 글자로 둔다
  var collect=Button("컬렉션 가져오기",ImportCollection);collect.Tag="default";collect.Font=Theme.Px(AppFonts.Ui,9.75f,FontStyle.Bold);collectButton=collect;
  Row("가져오기",collect,Button("V아카이브 가져오기",ImportArchive));
  Group("오버레이와 설정");
  var address=new InputBox(InputWidth,OverlayUrl,0,27);address.Box.ReadOnly=true;address.Box.AccessibleName="OBS 브라우저 주소";
  Row("OBS 브라우저",Stretch(address,Button("오버레이 팝업",OpenOverlayPopup)));
  // 투컴: 허용을 켜면 원컴 주소 아래에 송출 PC용 주소 줄과 안내가 생기고 아래의 줄들이 그만큼 내려간다(MainForm.Sender.cs)
  PrepareSender(savedSender,InputWidth);
  Row("송출 PC용 주소",Stretch(senderAddress,senderCopy,senderStatus,senderHelp));senderRow=into.Controls[into.Controls.Count-1];
  senderNotes.Margin=new(NameWidth+7,0,0,2);into.Controls.Add(senderNotes);
  Row("",senderAllow,senderPick);
  ShowSender();
  Row("설정",Button("단축키 설정",()=>{using var d=new HotkeyDialog(hotkeys.Items.Count==5?hotkeys.Items:Hotkeys.Defaults(),ApplyHotkeys);d.ShowDialog(this);}),Button("오버레이 디자인 설정",EditAppearance),showCurrent);
  Row("진단 기록",Button("진단 기록 폴더 열기",()=>{try{OpenDiagnostics();}catch(UnauthorizedAccessException){throw new Exception(NoDiagnosticsAccess);}}),scoreImagesOption,imagesHelp);
  {
   imagesHelpBox.Margin=new(NameWidth+7,2,0,6);imagesHelpBox.Padding=new(11,8,11,9);imagesHelpBox.Visible=false;
   foreach(string paragraph in ImagesHelpText.Split('\n')){var text=new WordLabel(paragraph,InputWidth-22,9f,"dim"){Margin=new(0,imagesHelpBox.Controls.Count==0?0:5,0,0)};imagesHelpBox.Controls.Add(text);}   // 낱말 사이에서만 줄을 바꾼다
   into.Controls.Add(imagesHelpBox);
  }
  Row("업데이트",UpdateRow());   // MainForm.Update.cs
  // 앱 표시 크기: 다섯 단계에서 고른다(Windows 배율이 100% 여도 보인다). 고른 것은 다음 실행부터 쓴다. 옆의 글은 ScaleNote
  into=left;Row("앱 표시 크기",scaleChoice,scaleNote);ShowScaleNote();scaleRow=left.Controls[left.Controls.Count-1];
  leftBody.SizeChanged+=(_,_)=>FitLeft();
  // 오른쪽 칸: 자리를 정해 놓는다(등록 전·저장 확인·실패 사이를 오가도 미리 보기와 SCORE, 상태 줄의 자리가 같아야 한다). 위에서부터
  //   17 곡 정보와 테마 전환(27) · 56 곡 정보 카드(108) · 164 목표와 현재(143. 저장 확인이 이 자리에 뜬다) · 321 게임 화면(256) · 610 SCORE(43) · 662 상태 줄 · 688 실패 안내
  var songName=new SectionHeader("곡 정보"){Rule=false,Location=new(X,20)};songName.Size=new(songName.TextWidth+2,22);songHead=songName;modeSwitch.Location=new(X+RightInner-modeSwitch.Width,17);
  songTitle=new SongCard(helpTip){Text="—"};songTitle.Bounds=new(X,56,RightInner,108);
  var chips=new FlowLayoutPanel(){AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,WrapContents=false,Margin=Padding.Empty,Location=new(SongCard.Band+SongCard.Side,69)};chips.Controls.AddRange(new Control[]{buttonChip,patternChip,judgeChip});songTitle.Controls.Add(chips);
  Label Caption(string text,int x,int y)=>new(){Text=text,AutoSize=true,Tag="dim",Location=new(x,y),Margin=Padding.Empty,UseMnemonic=false,Font=Theme.Px(AppFonts.UiSemi,9.75f)};
  goalValue.Location=new(X+2,198);currentValue.Location=new(X+233,198);
  // 저장 확인은 목표·현재와 같은 자리에 뜬다(둘 가운데 하나만 보인다. ShowConfirmArea). 단추는 단축키와 같은 길로 간다(저장 4, 취소 5)
  var goalName=Caption("목표",X+2,176);var currentName=Caption("현재",X+233,176);var readoutLine=new Panel(){Tag="line",Bounds=new(X,251,RightInner,1)};
  readout=new Control[]{goalName,currentName,goalValue,currentValue,readoutLine};
  fadeLayer.Bounds=new(X,164,RightInner,143);picture.Paint+=(_,e)=>{if(gameTraceAt>=0)Motion.DrawTrace(e.Graphics,picture.ClientRectangle,gameTraceAt,Theme.Stripe);};
  confirmPanel.Location=new(X,164);confirmPanel.Save.Click+=(_,_)=>Safe(()=>Action(4));confirmPanel.Cancel.Click+=(_,_)=>Safe(()=>Action(5));
  picture.Bounds=new(X,346,RightInner,256);
  // SCORE 줄: 이름, 상태 한 줄, 오른쪽에 찾은 자리의 그림. 상태 글과 그림은 MainForm.Score.cs 가 고친다. 상태 글이 여러 줄이면(해상도 안내 등) 첫 줄만 이 줄에 두고 나머지는 아래의 안내 자리에 보인다
  var strip=new StripPanel(){Bounds=new(X,610,RightInner,43)};scoreStrip=strip;
  var scoreName=new Label(){Text="SCORE",AutoSize=true,Font=Theme.Px(AppFonts.Latin,9.75f,FontStyle.Bold),UseMnemonic=false};scoreName.Location=new(10,(43-scoreName.PreferredHeight)/2+1);
  scorePicture.Bounds=new(RightInner-11-124,8,124,27);
  var scoreLine=new Label(){AutoSize=false,AutoEllipsis=true,Tag="dim",Font=Theme.Px(AppFonts.Ui,9f),TextAlign=ContentAlignment.MiddleLeft,UseMnemonic=false,AccessibleName="SCORE 상태"};
  scoreLine.Bounds=new(10+scoreName.PreferredWidth+7,5,scorePicture.Left-9-(10+scoreName.PreferredWidth+7),33);   // 상자의 테두리와 그 안쪽을 도는 빛(안쪽 2~5px)을 덮지 않게 위아래로 5 씩 들인다
  strip.Controls.AddRange(new Control[]{scoreName,scoreLine,scorePicture});
  void SplitScore()
  {
   string all=scoreStatus.Text;int cut=all.IndexOf('\n');scoreLine.Text=cut<0?all:all[..cut];scoreNote.Text=cut<0?"":all[(cut+1)..];
   TraceFirstScore();
  }
  scoreStatus.TextChanged+=(_,_)=>SplitScore();
  statusDot.Location=new(X,666);status.Location=new(X+14,662);status.Font=Theme.Px(AppFonts.Ui,9f);status.Tag="dim";status.Margin=Padding.Empty;Steady(status,RightInner-14);status.MinimumSize=new(0,status.Font.Height);
  // 등록 실패 안내: 글은 MainForm.Score.cs 가 scoreAdvice 에 적는다(다음 등록 요청이나 다음 곡까지 남는다). 그 글을 상자(AdviceBox)로 보인다. 높이는 줄 수에 맞춰 늘어난다
  adviceBox.Bounds=new(X,688,RightInner,56);scoreAdvice.TextChanged+=(_,_)=>adviceBox.Set(scoreAdvice.Text);
  scoreNote.Location=new(X,688);Steady(scoreNote,RightInner);
  // 안내 자리: 등록 실패 안내가 먼저다. 없을 때만 SCORE 상태의 나머지 줄을 보인다. 상태 줄이 두 줄이 되면 그만큼 내려간다
  void PlaceNotes(){int top=Math.Max((int)Math.Round(688*Unit),status.Bottom+(int)Math.Round(6*Unit));if(adviceBox.Top!=top)adviceBox.Top=top;if(scoreNote.Top!=top)scoreNote.Top=top;bool note=scoreNote.Text.Length>0&&scoreAdvice.Text.Length==0;if(scoreNote.Visible!=note)scoreNote.Visible=note;ShowStatusKind();}
  status.SizeChanged+=(_,_)=>PlaceNotes();scoreAdvice.TextChanged+=(_,_)=>PlaceNotes();scoreNote.TextChanged+=(_,_)=>PlaceNotes();
  right.Controls.AddRange(new Control[]{songName,modeSwitch,songTitle,goalName,currentName,goalValue,currentValue,readoutLine,confirmPanel,fadeLayer,Caption("게임 화면",X,321),picture,strip,statusDot,status,adviceBox,scoreNote});
  SplitScore();PlaceNotes();
  left.ResumeLayout(true);right.ResumeLayout(true);root.ResumeLayout(true);ResumeLayout(true);FitLeft();
  modeSwitch.Toggled+=()=>Safe(()=>{SaveOptions();Theme.ApplyAll();});
  Load+=(_,_)=>{
   // 화면의 작업 영역이 창보다 작으면(작은 화면에서 큰 배율을 쓸 때) 창을 그만큼 줄이고 안에서 스크롤한다. 보통은 정해 둔 크기 그대로다
   var area=Screen.FromControl(this).WorkingArea;
   int frameWidth=Width-ClientSize.Width,frameHeight=Height-ClientSize.Height+BarHeight;   // 제목 띠는 본문 영역 안에 있다. 내용이 쓸 수 있는 높이에서 뺀다
   int wide=Theme.S(MainWidth),tall=Theme.S(MainHeight);root.AutoScrollMinSize=new(wide,tall);PlaceNotes();FitLeft();   // 창이 뜰 때 내용이 단계만큼 커졌다
   if(wide>area.Width-frameWidth||tall>area.Height-frameHeight)ClientSize=new(Math.Min(wide,area.Width-frameWidth),Math.Min(tall,area.Height-frameHeight));
  };
  profiles.SelectedIndexChanged+=(_,_)=>{if(!syncing&&profiles.SelectedItem is PlayProfile p)Safe(()=>{try{EnsureProfileFree();store.Select(p.Id);}catch{RefreshProfiles();throw;}RefreshState();});};   // 전환이 막혔거나 전환의 저장이 실패하면 고르는 칸을 실제 프로필로 되돌린다(보이는 프로필과 기록이 들어가는 프로필이 달라지지 않게)
  scoreImagesOption.CheckedChanged+=(_,_)=>Safe(()=>SaveOptions());scaleChoice.SelectedIndexChanged+=(_,_)=>{if(!scaleReverting)Safe(PickScale);};showCurrent.CheckedChanged+=(_,_)=>RefreshState();gear.SelectedIndexChanged+=(_,_)=>{epoch++;if(session.Pending==null){EndScorePlay("기어 위치 변경");ResetRecognition();}RefreshState();};windows.SelectedIndexChanged+=(_,_)=>{epoch++;if(session.Pending==null){EndScorePlay("게임 창 변경");ResetRecognition();}};
  RefreshProfiles();RefreshWindows();LoadAppearance();try{hotkeys.Set(initialHotkeys??(File.Exists(UserFiles.At("hotkeys.json"))?JsonSerializer.Deserialize<List<Hotkey>>(File.ReadAllText(UserFiles.At("hotkeys.json")))!:Hotkeys.Defaults()));}catch(Exception e){status.Text=e is HotkeyTaken?e.Message+" 지금은 단축키가 모두 꺼져 있습니다.":e.Message;}

  captureTimer.Tick+=async(_,_)=>await CaptureTick();timer.Tick+=async(_,_)=>{TraceFirstScore();await Tick();};FormClosed+=(_,_)=>{StopSender();StopMotion();alertTimer?.Dispose();epoch++;session.Running=false;timer.Stop();captureTimer.Stop();recordsDialog?.Dispose();hotkeys.Dispose();server.Dispose();recognition.Dispose();steam.Dispose();audio?.Dispose();picture.Image?.Dispose();scorePicture.Image?.Dispose();latestFrame?.Dispose();CancelAnalysis();scoreLink.Dispose();captureTimer.Dispose();helpTip.Dispose();};RefreshState();
  Shown+=(_,_)=>BeginInvoke(StartUpdateFeatures);
  // 첫 전환이 알릴 것(옛 자리의 기록이 달라졌다)을 주 화면이 뜬 뒤에 한 번 보여 준다. 사용자가 본 뒤에야 "알렸음"으로 적는다(그 전에 켜지지 못하면 다음에 다시 알린다)
  Shown+=(_,_)=>BeginInvoke(()=>{if(UserFiles.Pending is {Notice:{} notice} pending){UserFiles.Pending=pending with{Notice=null};GuideDialog.Show(this,StartGuides.Records(notice));UserFiles.Shown(AppContext.BaseDirectory,pending.NoticeKey);}});
 }
 void Safe(Action action){try{action();}catch(Exception e){Alert(e.Message);}}   // 조작이 실패한 까닭은 상태 줄에 몇 초 동안 남는다(Alert. MainForm.Look.cs)
 // 단축키 설정 창의 '적용': Windows 에 등록하고 설정 파일에 적는다. 둘 다 돼야 적용된 것이다.
 // 등록이 거절되면 Hotkeys.Set 이 예전 조합으로 되돌린다. 등록은 됐는데 파일을 쓰지 못하면 여기서 예전 조합으로 되돌린다(켜 둔 동안의 키와 다시 켰을 때의 키가 달라지지 않게).
 // 못 했으면 그 까닭을 돌려준다(창이 닫히지 않고 그 글을 보인다)
 Exception? ApplyHotkeys(List<Hotkey> value)
 {
  var before=hotkeys.Items.ToList();
  try{hotkeys.Set(value);}catch(Exception e){return e;}
  try{File.WriteAllText(hotkeysPath,JsonSerializer.Serialize(value));return null;}
  catch(Exception e)
  {
   try{if(before.Count==5)hotkeys.Set(before);}catch{}
   return new Exception("단축키 설정 파일을 저장하지 못해 적용하지 않았습니다. 단축키는 바꾸기 전 그대로입니다. ("+e.Message+")");
  }
 }
 string hotkeysPath=UserFiles.At("hotkeys.json");   // 개발용 검사가 다른 파일로 바꿔 본다
 protected override void WndProc(ref Message m){if(m.Msg==0x0312){int id=m.WParam.ToInt32()-100;Safe(()=>Action(id,true));}base.WndProc(ref m);}
 void Action(int i,bool fromHotkey=false){if(updating||collecting)return;   // 업데이트를 시작한 뒤와 컬렉션 가져오기 창이 떠 있는 동안에는 단축키로 들어오는 조작을 받지 않는다(알림도 없다)
  if(i==4){if(session.Pending!=null)Decide(true);return;}if(i==5){if(activeRegistration!=null){CancelAnalysis();RefreshState();}else if(session.Pending!=null)Decide(false);return;}if(activeRegistration!=null)throw new Exception("점수 확인이 끝나거나 취소한 뒤에 다시 시도해 주세요.");if(session.Pending!=null)throw new Exception("저장 확인에서 ‘저장’이나 ‘취소’를 먼저 눌러 주세요.");switch(i){case 2:RequestRegistration();break;case 6:StartDetection();break;case 3:RestartDetection();break;}RefreshState();}
 void RestartDetection(){finishedKey=registeredSteamKey=null;EndScorePlay("새로 감지하기");ResetRecognition();session.Running=true;UpdateScoreStatus();epoch++;timer.Start();captureTimer.Start();status.Text="새로 감지하고 있습니다.";}   // 꺼져 있다가 켜면 SCORE 줄의 '탐색 대기'를 바로 고친다
 void ResetRecognition(){latestFrame?.Dispose();latestFrame=null;latestFrameAt=0;latestFrameKey=null;scorePicture.Image?.Dispose();scorePicture.Image=null;drawnArea=null;UpdateScoreStatus();lastHudRate=null;rateSource=RateSource.Unknown;audio?.Dispose();audio=null;steamChart=null;resolvedSong=null;audioAttempt=0;judgeGate.Reset();recognition.ResetContext();session.Next();}
 internal static Bitmap CaptureFrame(IntPtr handle,out Size size){if(IsIconic(handle)||!GetClientRect(handle,out var r)||r.Right<100||r.Bottom<100)throw new Exception(NoGameScreen);size=new(r.Right,r.Bottom);var pt=new Pt();ClientToScreen(handle,ref pt);var raw=new Bitmap(r.Right,r.Bottom);
  try{using(var g=Graphics.FromImage(raw))g.CopyFromScreen(pt.X,pt.Y,0,0,raw.Size);
   if(raw.Width==1920&&raw.Height==1080)return raw;
   using(raw)return Normalize(raw);
  }catch{raw.Dispose();throw;}}
 void StartDetection(){if(session.Pending!=null)throw new Exception("저장 확인에서 ‘저장’이나 ‘취소’를 먼저 눌러 주세요.");if(session.Running)return;session.Running=true;finishedKey=registeredSteamKey=null;epoch++;EndScorePlay("감지 시작");ResetRecognition();timer.Start();captureTimer.Start();RefreshState();TraceGame();}
 decimal? HeldHudRate()=>rateSource==RateSource.Hud&&lastHudRate?.ChartKey==session.Locked?.Key?lastHudRate?.Value:null;
 protected virtual SteamSnapshot ReadSteamSnapshot()=>steam.Read();
 protected virtual Task<ScreenReading> ReadRegistration(string mode)=>ReadScreen(mode);
 protected virtual Bitmap CaptureGameFrame(IntPtr handle){var frame=CaptureFrame(handle,out var size);Interlocked.Exchange(ref captureSize,((long)size.Width<<32)|(uint)size.Height);return frame;}
 async Task<ScreenReading> ReadScreen(string mode){if(windows.SelectedItem is not WindowItem window)throw new Exception(NoGameScreen);int gearIndex=GearIndex;long capturedAt=0;bool cached=latestFrame!=null&&latestFrameKey==steamChart?.Key&&Stopwatch.GetElapsedTime(latestFrameAt).TotalSeconds<.4;capturedAt=cached?latestFrameAt:Stopwatch.GetTimestamp();using var frame=cached?(Bitmap)latestFrame!.Clone():await Task.Run(()=>CaptureGameFrame(window.Handle));if(IsDisposed||Disposing)throw new OperationCanceledException();if(ShouldUpdatePreview()){var previewSize=picture.ClientSize;var preview=await Task.Run(()=>MakePreview(frame,previewSize));SetPreview(preview);}bool scoreLocated=ScoreLocated;
 var reading=await Task.Run(async()=>{
  return await recognition.Read(frame,mode,gearIndex,scoreLocated);
 });
 RefreshScorePreview(frame);return reading with{CapturedAt=capturedAt};}
#if DEVELOPMENT_CHECKS
 void ShowRegistration(bool fromHotkey)=>ShowMeasuredRegistration(null,null);
#endif
 void ShowMeasuredRegistration(DateTime? measuredAt,double? rateAge,decimal? measuredAccuracy=null)
 {
  session.Begin(store.Active.Id,measuredAt,rateAge,measuredAccuracy);pendingPlaySerial=scoreLink.PlaySerial;
  pendingNote=rateAge is double age?$"{age:F1}초 전 인식값":null;
  status.Text="저장 확인 대기";
  RefreshState();
 }
 // 상태 줄에 띄우는 조작 오류의 글
 internal const string NoGameScreen="게임 화면을 확인할 수 없습니다. 선택한 게임 창이 열려 있고 화면에 보이는지 확인해 주세요.",NoDiagnosticsAccess="진단 기록 폴더를 열 권한이 없습니다. 폴더에 접근할 권한을 확인해 주세요.";
 void Decide(bool yes){try{if(session.Pending==null)return;if(yes){var old=session.Pending!;var done=session.Confirm(store);string result=done.Text;decimal? previous=done.Previous is {} before?ScoreValue.Rate(before.Score):null;finishedKey=old.Chart.Key;registeredSteamKey=old.SteamKey??steamChart?.Key;registeredPlaySerial=pendingPlaySerial;savedChart=old.Chart;savedAccuracy=old.Accuracy;eventEnded=false;audio?.Dispose();audio=null;lastEvent=new{id=++eventId,text=result,song=OverlayText.Title(catalog,catalog.Songs.Single(s=>s.Id==old.Chart.SongId)),button=old.Chart.Button,pattern=old.Chart.Pattern,judge=old.Chart.Judge,value=old.Accuracy,score=done.Play.Score,previous,previousScore=done.Previous?.Score,kind=done.Kind,at=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()};epoch++;status.Text=result;}else{session.Cancel();status.Text="등록 취소";}RefreshState();}catch(RecordFileChangedException e){Alert(e.Message);}catch(Exception e){int cut=e.Message.IndexOf("\n\n",StringComparison.Ordinal);status.Text="저장 실패: "+(cut<0?e.Message:e.Message[..cut]);}}   // 빈 줄 뒤(보관하려던 파일의 경로 등)는 상태 줄에 적지 않는다
 void CancelPending(){CancelAnalysis();session.Cancel();}
 string Describe(Selection c){var song=catalog.Songs.Single(s=>s.Id==c.SongId);return $"{song.Name} ({song.Artist}) · {c.Button}B · {c.Pattern} · {c.Judge}판정";}
 void SyncSelection(Selection c){displayedSelection=c;cardChart=c;var song=catalog.Songs.Single(s=>s.Id==c.SongId);ShowSong(song,$"{c.Button}B",c.Pattern,c.Judge+"판정");}
 void RefreshState(){bool blocked=activeRegistration!=null||session.Pending!=null;gear.Enabled=windows.Enabled=profiles.Enabled=!blocked;
  // 점수를 확인하거나 저장 확인을 기다리는 동안에는 프로필 줄과 등록 단추를 누를 수 없게 보인다(눌러도 막히던 것을 보이는 것과 맞춘다). 기록 목록 창이 떠 있는 동안에도 프로필 줄은 막힌다
  if(profiles.Parent is {} profileRow&&profileRow.Enabled!=(!blocked&&!recordsVisible))profileRow.Enabled=!blocked&&!recordsVisible;if(registerButton is {} register&&register.Enabled==blocked)register.Enabled=!blocked;
  // 게임 창의 목록을 다시 읽으면 창 선택이 바뀐 것으로 처리돼 진행 중인 등록 요청이 버려진다. 그동안에는 그 단추도 막는다
  if(refreshButton is {} listing&&listing.Enabled==blocked)listing.Enabled=!blocked;
  var c=session.Locked??session.Candidate;
  // 목표는 카드에 보이는 곡의 지금 기록이다. 저장한 직후에는 다음 판을 받으려고 고정된 곡이 비지만 카드에는 그 곡이 그대로 보이므로, 카드의 곡(cardChart)으로 다시 읽는다(저장한 값이 목표로 보인다)
  var shown=c??cardChart;ShowStats(shown==null?null:store.BestRate(shown),session.Current);TraceFirstScore();ShowConfirmArea();ShowStatusKind();
  // 오버레이의 플레이 화면에 보일 곡. 저장하면 다음 판을 받으려고 곡의 고정이 풀리지만(같은 판을 두 번 등록하지 않게 하는 것은 그대로다), 게임이 아직 그 판에 있는 동안에는
  // 방금 저장한 곡과 저장이 반영된 개인 최고, 방금 저장한 점수를 보낸다. 그래야 저장 결과가 끝난 뒤 오버레이가 감지 대기가 아니라 그 곡의 화면으로 돌아간다.
  // 게임이 그 곡에서 벗어나면(Steam 의 곡 정보가 비거나 바뀌면) 저장한 판의 표시가 풀려 감지 대기나 다음 곡으로 넘어간다
  var kept=session.Locked==null&&session.Pending==null&&session.Running&&savedChart!=null&&registeredSteamKey!=null&&steamChart?.Key==registeredSteamKey&&registeredPlaySerial==scoreLink.PlaySerial?savedChart:null;
  var onAir=session.Locked??kept;decimal? airGoal=onAir==null?null:store.BestRate(onAir),airCurrent=kept!=null?savedAccuracy:session.Current;
  // 저장 결과를 끝내는 때: 새 등록이 실제로 시작되거나(점수 확인·저장 확인) 다른 곡의 감지가 확정되면 끝낸다. 한 번 끝낸 결과는 새 요청을 취소해도 다시 보이지 않는다.
  // 오버레이 페이지도 같은 규칙으로 스스로 끝내지만 그것은 열려 있는 페이지만 안다. 새로 연 페이지(팝업을 다시 열거나 OBS 소스를 새로 고침)도 알도록 여기서 정해 보낸다
  if(lastEvent!=null&&!eventEnded&&(session.Pending!=null||analysisState!=null||session.Locked!=null&&session.Locked.Key!=savedChart?.Key))eventEnded=true;
  server.Set(new{visible=session.Running,label=onAir==null?"악곡을 감지하고 있습니다":OverlayText.Describe(catalog,onAir),goal=airGoal, current=airCurrent,showCurrent=showCurrent.Checked,appearance,overlayTitle=onAir==null?null:OverlayText.Title(catalog,catalog.Songs.Single(s=>s.Id==onAir.SongId)),overlayDetails=onAir==null?null:$"{onAir.Button}B · {onAir.Pattern} · {onAir.Judge}판정",confirmation=session.Pending==null?null:new { label=OverlayText.Describe(catalog,session.Pending.Chart),song=OverlayText.Title(catalog,catalog.Songs.Single(s=>s.Id==session.Pending.Chart.SongId)),button=session.Pending.Chart.Button,pattern=session.Pending.Chart.Pattern,judge=session.Pending.Chart.Judge,accuracy=session.Pending.Accuracy,score=session.Pending.RawScore??ScoreValue.FromAccuracy(session.Pending.Accuracy),rateAge=session.Pending.RateAge,previous=store.BestRate(session.Pending.Chart),confirm=Hotkeys.Format(hotkeys.Items[2]),cancel=Hotkeys.Format(hotkeys.Items[3]) },analysis=analysisState,tone=airGoal==null||airCurrent==null?"unknown":airCurrent>=airGoal?"above":"below",lastEvent,eventOver=eventEnded});}
 void RefreshProfiles(){syncing=true;try{profiles.Items.Clear();profiles.Items.AddRange(store.Data.Profiles.Cast<object>().ToArray());profiles.SelectedItem=store.Active;}finally{syncing=false;}RefreshState();}
 void ProfileAction(bool copy){EnsureProfileFree();var name=copy?NameDialog.Ask(this,"프로필 복사","복사본 이름",store.Active.Name+" 복사","복사",store.Active.Name):NameDialog.Ask(this,"새 프로필","프로필 이름","","확인");if(name!=null){EnsureProfileFree();store.Create(name,copy);RefreshProfiles();}}
 string OverlayUrl=>$"http://127.0.0.1:{server.Port}/";
 async void OpenOverlayPopup()
 {
  try{await OverlayPopup.Show(server,appearance,showCurrent.Checked);}
  catch(Exception e){if(!IsDisposed&&!Disposing)Alert(e.Message);}
 }
 // V아카이브 가져오기: 닉네임을 받고, 불러오고, 바뀔 것을 보이고, 가져오는 것까지 한 창이 한다(ImportDialog.cs).
 // 그 창이 떠 있는 동안에는 업데이트·복구를 시작하지 않는다(importing. MainForm.Update.cs)
 void ImportArchive()
 {
  if(importing)return;
  importing=true;
  try{using var dialog=new ImportDialog(catalog,store);dialog.ShowDialog(this);if(dialog.Imported is int count){var step=store.Active.Undo.LastOrDefault();int fresh=step is {Kind:PlayOps.ImportVArchive}&&step.Plays.Count==count?step.Plays.Count(p=>p.Before==null):-1;status.Text=fresh<0?$"V아카이브 기록 {count:N0}건을 가져왔습니다.":$"V아카이브 기록 {count:N0}건을 가져왔습니다(추가 {fresh:N0}건, 갱신 {count-fresh:N0}건).";}}   // 추가·갱신은 방금 한 가져오기 작업(되돌리기의 마지막 단계)에서 센다
  finally{importing=false;if(!IsDisposed&&!Disposing)RefreshState();}
 }
 // 컬렉션 가져오기.
 // 모달로 띄운다: 그동안 프로필 전환·새로·복사·이름 변경·삭제와 메인 창의 다른 조작이 함께 막힌다. 감지 중이거나 점수 확인·저장 확인을 기다리는 중이면 열지 않고 까닭을 상태 줄에 둔다.
 // 창이 떠 있는 동안에는 전역 단축키 다섯을 받지 않는다(Action). 적용하면 창이 닫히고 상태 줄에 건수를 적는다. 적용하지 않고 닫으면 상태 줄을 바꾸지 않는다
 internal const string CollectionWhilePending="점수 확인과 저장 확인이 끝난 뒤 컬렉션 가져오기를 열어 주세요.",CollectionWhileDetecting="‘중지 / 숨기기’로 감지를 멈춘 뒤 컬렉션 가져오기를 열어 주세요.";
 Button? collectButton;
 /// <summary>개발용 검사: 메인 창이 만든 컬렉션 가져오기 창을 띄우기 직전에 받는다(가짜 게임을 물리고 차례를 돌린다)</summary>
 internal Action<CollectionWindow>? CollectionForTest=null;
 internal string StatusText=>status.Text;internal RecordStore StoreForTest=>store;internal bool DetectingForTest=>session.Running;
 internal void SetDetectingForTest(bool on)=>session.Running=on;
 internal bool CollectionOpen=>collecting;internal Button? CollectionButton=>collectButton;
 internal void OpenCollectionForTest()=>Safe(ImportCollection);
 void ImportCollection()
 {
  if(collecting||importing)return;
  if(activeRegistration!=null||session.Pending!=null)throw new Exception(CollectionWhilePending);   // 이것을 먼저 본다: 감지를 멈추면 저장 확인도 취소된다
  if(session.Running)throw new Exception(CollectionWhileDetecting);
  collecting=true;
  try
  {
   using var window=new CollectionWindow(catalog,store.Active.Name,store);
   window.Applied+=result=>Settle(CollectionDoneView.AppliedText(result));
   // 읽기가 끝나거나 멈출 때마다 진단 기록에 한 줄(시각·종류·곡 수·확인할 것·마지막 곡·내부 까닭. 화면과 점수는 적지 않는다)
   // 읽기의 스레드에서 온다(창이 닫혔어도 온다): 이 창의 스레드로 옮겨 적는다
   window.ScanEnded+=result=>{var line=new{end=result.End.ToString(),songs=result.Items.Count,questions=result.Questions,allowed=result.Allowed,last=result.LastTitle,detail=result.Detail,seconds=Math.Round(result.Seconds,1)};try{BeginInvoke(()=>scoreLog.Write("collection-scan",line));}catch(InvalidOperationException){}};
   CollectionForTest?.Invoke(window);
   window.ShowDialog(this);
  }
  finally{collecting=false;if(!IsDisposed&&!Disposing)RefreshState();}
 }
 // 직전 저장 되돌리기: 무엇이 되돌려지는지 보이고 묻는다(UndoDialog.cs). 되돌렸으면 화면을 다시 그린다
 void UndoLast(){if(UndoDialog.Ask(this,catalog,store))RefreshState();}
 void BrowseRecords(){if(recordsDialog is {IsDisposed:false,Visible:true}){recordsDialog.Activate();return;}if(recordsDialog==null||recordsDialog.IsDisposed||recordsDialog.ProfileId!=store.Active.Id){recordsDialog?.Dispose();var dialog=new RecordsDialog(catalog,store,UserFiles.At("records-view.json")){KeepAlive=true};recordsDialog=dialog;dialog.VisibleChanged+=(_,_)=>{recordsVisible=dialog.Visible;if(!IsDisposed&&!Disposing)RefreshState();};}recordsDialog.PrepareForShow();recordsDialog.Show(this);}
 // 수동 기록 설정: 저장도 가져온 기록의 일괄 삭제도 그 창이 스스로 한다(RecordDialog.cs). 취소로 닫아도 일괄 삭제는 했을 수 있으므로 닫히면 언제나 다시 그린다
 void ManageRecord(){using var d=new RecordDialog(catalog,store,session.Locked??session.Candidate??displayedSelection);d.ShowDialog(this);RefreshState();}
 void LoadAppearance()=>appearance=OverlayStyle.Load(UserFiles.At("appearance.json"));
 // 네 색과 두 투명도만 바꿔 쓴다. 예전 판의 글꼴·글자 크기는 파일에 있던 그대로 남는다(OverlayStyle.Save)
 void EditAppearance(){using var d=new StyleDialog(appearance,showCurrent.Checked,hotkeys.Items,catalog);if(d.ShowDialog(this)==DialogResult.OK)ApplyAppearance(d.Value,UserFiles.At("appearance.json"));}
 // 파일에 쓴 뒤에 실행 중인 값을 바꾼다. 쓰지 못하면(예외가 상태 줄에 뜬다) 실행 중인 색도 그대로 둔다: 파일과 방송 화면이 어긋나지 않게
 void ApplyAppearance(JsonElement chosen,string path){OverlayStyle.Save(path,chosen);appearance=chosen;RefreshState();}
 // 프로세스마다 주 창을 조회하면(Process.MainWindowHandle) 그때마다 화면의 모든 창을 훑어 프로세스 300개에 0.3초가 든다(잰 값).
 // 창을 한 번만 훑어 프로세스별 주 창을 모은다. 주 창의 기준은 .NET 과 같다: 주인이 없고 보이는 최상위 창 가운데 맨 앞의 것. 목록의 순서는 프로세스 목록(Process.GetProcesses)의 순서다.
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
 // 여기서 적는 상태 글은 감지 흐름의 것이라 Detected 로 적는다(조작 오류가 떠 있는 몇 초 동안에는 덮지 않는다)
 async Task Tick(){if(busy||activeRegistration!=null||!session.Running)return;busy=true;int captureEpoch=epoch;try{
  var snapshot=ReadSteamSnapshot();if(session.Pending!=null)return;
  if(!snapshot.Available){session.Current=HeldHudRate();judgeGate.Observe(null,0);Detected(snapshot.Detail);RefreshState();return;}
  if(snapshot.Chart==null){registeredSteamKey=finishedKey=null;if(steamChart!=null)ResetRecognition();session.Current=null;Detected(snapshot.Detail);RefreshState();return;}
  var chart=snapshot.Chart;
  if(registeredSteamKey==chart.Key&&registeredPlaySerial==scoreLink.PlaySerial){session.Current=null;Detected("다음 곡을 기다리고 있습니다.");RefreshState();return;}
  if(steamChart?.Key!=chart.Key){ResetRecognition();steamChart=chart;registeredSteamKey=finishedKey=null;var candidates=SteamSongs.Find(catalog,chart);if(candidates.Count==1){resolvedSong=candidates[0];ShowSteamChart(chart,resolvedSong);}else if(candidates.Count>1){audio=new LoopbackAudio();Detected("동명곡 소리 확인 중");}}   // 하나도 없으면 아래에서 알린다
  double now=Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency;
  if(resolvedSong==null){var candidates=SteamSongs.Find(catalog,chart);if(candidates.Count==0){Detected($"곡 목록에서 이 곡이나 패턴을 찾지 못했습니다: {chart.Name} {chart.Button}B {chart.Pattern}. ‘업데이트 확인’으로 새 버전이 있는지 확인해 주세요.");RefreshState();return;}
   if(audio?.Error!=null){Detected(audio.Error);RefreshState();return;}
   if(audio!=null&&audio.Seconds>=8&&now-audioAttempt>=1){audioAttempt=now;var clip=audio.Snapshot();try{
    var cached=new Dictionary<string,float[][]>(audioReferences);var analysis=await Task.Run(()=>{var references=candidates.ToDictionary(candidate=>candidate.Id,candidate=>cached.TryGetValue(candidate.Id,out var reference)?reference:AudioIdentity.Load(Path.Combine(AppContext.BaseDirectory,"data","audio",candidate.SourceId+".bin")));return (References:references,Decision:AudioIdentity.Decide(AudioIdentity.Features(clip),references));});if(captureEpoch!=epoch)return;foreach(var pair in analysis.References)audioReferences[pair.Key]=pair.Value;var decision=analysis.Decision;
    if(decision.SongId!=null){resolvedSong=candidates.Single(s=>s.Id==decision.SongId);ShowSteamChart(chart,resolvedSong);audio.Dispose();audio=null;}else Detected("동명곡 소리 재확인 중 · 1초마다 비교");
   }catch(Exception e){Detected("동명곡 소리 확인: "+e.Message);}}
   else Detected($"동명곡 소리 확인 중 · {audio?.Seconds??0:F1}/8초");
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
   session.Current=reading.Rate??HeldHudRate();Detected(reading.Detail);
  }
  else{session.Current=null;if(resolvedSong!=null)Detected("곡 정보 고정 · 판정 1초 확인 중 · "+reading.Detail);}
  RefreshState();
 }catch(Exception e){if(captureEpoch!=epoch||IsDisposed||Disposing)return;session.Current=HeldHudRate();judgeGate.Observe(null,0);if(e.Message==NoGameScreen)Hold(NoGameScreen);else Detected(e.Message);RefreshState();}finally{FinishReading();}}
 void ShowSteamChart(SteamChart chart,Song song){cardChart=null;ShowSong(song,$"{chart.Button}B",chart.Pattern,"확인 중");}   // 판정이 확정되기 전에는 어느 기록과 견줄지 모른다
}
