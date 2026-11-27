namespace LadderRecords;

// 주 화면의 오른쪽 칸에 보이는 곡 정보(곡 이름, 꼬리표, 목표·현재)와 라이트·다크 전환.
partial class MainForm
{
 // 곡 정보 카드(곡 이름 한 줄과 꼬리표 셋). 곡 이름을 맞추는 규칙은 SongCard 에 있다. 줄인 이름의 풍선은 도움말 풍선(helpTip)을 같이 쓴다
 readonly SongCard songTitle;
 readonly Chip buttonChip=new(),patternChip=new(),judgeChip=new();
 readonly BigValue goalValue=new(),currentValue=new();
 readonly ModeSwitch modeSwitch=new();
 readonly StatusDot statusDot=new();
 readonly AdviceBox adviceBox=new();
 // 상태 줄의 조작 오류(단추나 단축키로 한 일이 실패한 까닭. 예: 오버레이 팝업을 열지 못함)는 몇 초 동안 감지 상태보다 먼저 보인다.
 // 그러지 않으면 감지가 켜져 있는 동안 상태 줄이 0.1초마다 새 감지 상태로 덮여 오류를 읽을 수 없다.
 // 그동안에도 감지는 계속하고, 시간이 지나면 그때의 감지 상태를 보인다. 사용자가 다른 일을 해서 상태 줄이 바뀌면(등록, 저장, 취소 등) 그 글이 바로 보인다.
 // 보이는 시간은 글의 길이를 따른다: 4초에 글자마다 0.06초를 더하고 10초를 넘기지 않는다(스무 자 5초쯤, 예순 자 8초쯤). SCORE 등록 실패의 사유와 다음 행동은 따로 아래 상자에 다음 등록 요청이나 다음 곡까지 남는다
 long alertUntil;string? alertText;
 bool AlertShown=>alertText!=null&&status.Text==alertText&&System.Diagnostics.Stopwatch.GetTimestamp()<alertUntil;
 static double AlertSeconds(string text)=>Math.Clamp(4+text.Length*.06,4,10);
 // 시간이 지나면 오류가 뜨기 전의 글로 돌린다. 감지가 돌고 있으면 곧 새 감지 상태가 적히지만, 점수를 확인하는 동안과 저장 확인을 기다리는 동안, 감지를 멈춘 동안에는 감지 흐름이 상태 줄을 쓰지 않는다
 System.Windows.Forms.Timer? alertTimer;string alertBefore="";
 void Alert(string text)
 {
  if(!AlertShown){alertBefore=status.Text;detectedMeanwhile=null;}   // 오류 위에 오류가 뜨면 처음의 글을 기억해 둔다
  double seconds=AlertSeconds(text);
  alertText=text;alertUntil=System.Diagnostics.Stopwatch.GetTimestamp()+(long)(seconds*System.Diagnostics.Stopwatch.Frequency);status.Text=text;ShowStatusKind();
  if(alertTimer==null){alertTimer=new System.Windows.Forms.Timer();alertTimer.Tick+=(_,_)=>AlertExpired();}
  alertTimer.Stop();alertTimer.Interval=(int)(seconds*1000)+30;alertTimer.Start();
 }
 void AlertExpired()
 {
  alertTimer?.Stop();
  if(alertText==null||status.Text!=alertText)return;   // 그 사이에 다른 글이 적혔으면 그 글을 그대로 둔다
  // 그동안 감지 흐름이 적으려던 글이 있으면 그 가장 새 것을, 없으면 오류가 뜨기 전의 글을 보인다
  alertText=null;status.Text=detectedMeanwhile??alertBefore;detectedMeanwhile=null;ShowStatusKind();
 }
 string? detectedMeanwhile;
 /// <summary>감지 흐름에서 생긴 오류 가운데 읽을 시간을 줄 것(게임 화면을 확인할 수 없음). 같은 글이 떠 있는 동안 다시 와도 시간을 처음부터 세지 않는다</summary>
 void Hold(string text){if(AlertShown&&alertText==text)return;Alert(text);}
 /// <summary>사용자가 한 일로 상태가 끝난 것을 적는다(감지 중지). 떠 있던 조작 오류를 버린다: 그 오류의 시간이 지나도 오류가 뜨기 전의 글(저장 확인 대기 등)로 돌아가지 않는다</summary>
 void Settle(string text){alertText=null;detectedMeanwhile=null;alertTimer?.Stop();status.Text=text;ShowStatusKind();}
 /// <summary>감지 흐름의 상태 글. 조작 오류가 떠 있는 동안에는 적지 않는다(지나간 감지 상태는 버린다. 다음 것이 곧 온다)</summary>
 void Detected(string text){if(AlertShown){detectedMeanwhile=text;return;}alertText=null;detectedMeanwhile=null;status.Text=text;}
 // 저장 확인(주 화면 오른쪽). 목표·현재(readout)와 같은 자리에 번갈아 보인다
 readonly ConfirmPanel confirmPanel=new();SectionHeader? songHead;Control[] readout=Array.Empty<Control>();Button? registerButton;bool confirmShown;string? pendingNote;
 // SCORE 상태의 둘째 줄부터(해상도 안내, 자동 인식을 쓸 수 없을 때의 안내). 등록 실패 안내가 없을 때 그 자리에 보인다
 readonly Label scoreNote=new(){Text="",Tag="dim",UseMnemonic=false,Visible=false,Margin=Padding.Empty,Font=Theme.Px(AppFonts.Ui,9f)};
 Control? scrollArea;   // 주 화면 전체를 담고 스크롤되는 패널. 미리 보기가 화면 밖으로 밀렸는지 볼 때 쓴다

 // 같은 이름의 곡이 있으면 아티스트를 괄호로 붙인다(오버레이와 같은 규칙)
 // 새 곡의 정보가 처음 보일 때(이름이 바뀔 때)는 연출과 함께 보인다. 같은 곡이 다시 적힐 때(판정이 확정될 때, 저장·취소 뒤에 돌아올 때)는 연출하지 않는다
 void ShowSong(Song song,string button,string pattern,string judge,bool reveal=true)
 {
  string title=OverlayText.Title(catalog,song);bool fresh=songTitle.Text!=title;
  if(fresh){songTitle.StopReveal();songTitle.Text=title;}
  buttonChip.Set(button);patternChip.Set(pattern);judgeChip.Set(judge);
  if(fresh&&reveal&&session.Running)RevealSong();
 }

 // ── 연출 (바탕은 Motion.cs. 동작 줄이기가 켜져 있으면 모두 건너뛴다) ─────────────
 //   감지 시작: 게임 화면의 안쪽 테두리를 빛이 한 바퀴(0.78초)         곡 정보 도착: 곡명 타이핑, 꼬리표가 0.155초 간격으로, 카드 아래로 빛(0.92초)
 //   그 판의 첫 SCORE: SCORE 줄의 안쪽 테두리를 빛이 한 바퀴(0.62초)   등록 요청: 목표·현재 아래로 빛이 한 번(0.72초)
 //   후보 도착: 목표·현재가 흐려지고(0.085초) 저장 확인이 나타남(0.17초)   저장·취소: 저장 확인이 흐려지고(0.085초) 목표·현재가 나타남(0.18초)
 // 감지를 멈추면 돌던 연출도 끝낸다. 연출은 보이는 것만 바꾼다(값과 상태는 연출이 시작될 때 이미 끝의 것이다)
 readonly FadeLayer fadeLayer=new();SidePanel? rightPanel;StripPanel? scoreStrip;
 Motion.Run? gameTrace,scoreTrace,chipsRun,scanRun;double gameTraceAt=-1,scanAt=-1;object? scannedRequest;int tracedSerial=-1;
 Button? refreshButton;
 Selection? cardChart;   // 곡 정보 카드에 보이는 곡·버튼·난이도·판정(판정이 확정되기 전에는 null). 목표를 이것의 기록으로 읽는다
 // 그 판에서 SCORE 의 자리가 고정돼 있는 것을 처음 본 때 한 번: SCORE 줄의 테두리를 빛이 돈다. 판은 판 번호로 가린다(같은 자리에서 다시 시작한 판은 상태 글이 바뀌지 않는다)
 // 부르는 곳: SCORE 상태 글이 바뀔 때, 화면을 새로 그릴 때, 0.1초 주기(저장 확인을 기다리는 동안에는 앞의 둘이 오지 않는다. 그동안 같은 자리에서 새 판이 열려도 그때 켠다)
 void TraceFirstScore(){if(!session.Running||!ScoreLocated||scoreLink.PlaySerial==tracedSerial)return;tracedSerial=scoreLink.PlaySerial;TraceScore();}
 void StopMotion(){Motion.Stop(gameTrace);Motion.Stop(scoreTrace);Motion.Stop(chipsRun);Motion.Stop(scanRun);songTitle.StopReveal();fadeLayer.Cancel();}
 void TraceGame(){Motion.Stop(gameTrace);gameTrace=Motion.Start(.78,t=>{gameTraceAt=t>=1?-1:t;picture.Invalidate();});}
 void TraceScore(){Motion.Stop(scoreTrace);scoreTrace=Motion.Start(.62,t=>scoreStrip?.SetTrace(t>=1?-1:t));}
 void RevealSong()
 {
  songTitle.Reveal();Motion.Stop(chipsRun);var chips=new[]{buttonChip,patternChip,judgeChip};
  // 첫 꼬리표는 곡명의 첫 글자와 함께, 다음 둘은 0.155초·0.310초 뒤에. 하나가 나타나는 데 0.15초
  chipsRun=Motion.Start(.46,t=>{for(int i=0;i<chips.Length;i++)chips[i].SetReveal(t>=1?1:Motion.EaseOut(Math.Clamp((t*.46-.155*i)/.15,0,1)));});
 }
 Rectangle ScanTrack=>readout.Length<5?Rectangle.Empty:new Rectangle(readout[4].Left,readout[4].Top-Theme.S(2),readout[4].Width,Theme.S(2));   // 목표·현재 아래의 가로줄 바로 위
 void Scan(){Motion.Stop(scanRun);scanRun=Motion.Start(.72,t=>{scanAt=t>=1?-1:t;rightPanel?.Invalidate(Rectangle.Inflate(ScanTrack,0,1));});}
 void DrawScan(Graphics g)
 {
  if(scanAt<0||confirmShown)return;var track=ScanTrack;if(track.IsEmpty)return;
  double opacity=scanAt<.22?scanAt/.22:scanAt>.72?(1-scanAt)/.28:1;var stripe=Theme.Stripe;
  Motion.DrawGlint(g,track,track.Width*.36f,(float)(-1+3.8*Motion.EaseInOut(scanAt)),new[]{Color.FromArgb(0,stripe),stripe,Color.FromArgb(0,stripe)},new[]{0f,.5f,1f},opacity);
 }
 // 목표·현재와 저장 확인이 바뀌는 자리의 지금 모습(전환에 쓴다). 연출을 하지 않을 때나 창이 보이지 않을 때는 null
 Bitmap? CaptureSlot()
 {
  if(Motion.Reduced||rightPanel==null||!IsHandleCreated||!Visible||WindowState==FormWindowState.Minimized||!session.Running)return null;   // 감지를 멈춘 뒤(중지 / 숨기기)에는 끝의 모습을 바로 보인다
  fadeLayer.Cancel();
  try
  {
   using var whole=new Bitmap(rightPanel.Width,rightPanel.Height);rightPanel.DrawToBitmap(whole,new Rectangle(Point.Empty,whole.Size));
   var slot=Rectangle.Intersect(fadeLayer.Bounds,new Rectangle(Point.Empty,whole.Size));
   return slot.Width>0&&slot.Height>0?whole.Clone(slot,whole.PixelFormat):null;
  }
  catch(Exception){return null;}   // 그림을 만들지 못하면 연출 없이 바꾼다
 }
 // "현재"는 목표 이상이면 연두, 미만이면 빨강. 견줄 것이 없으면 보통 글자색
 void ShowStats(decimal? goal,decimal? current)
 {
  string goalText=goal==null?"미등록":goal.Value.ToString("F2")+"%",currentText=current==null?"—":current.Value.ToString("F2")+"%";
  if(goalValue.Text!=goalText)goalValue.Text=goalText;
  string? tone=goal==null||current==null?null:current>=goal?"good":"bad";
  if(currentValue.Text!=currentText||(string?)currentValue.Tag!=tone){currentValue.Tag=tone;currentValue.ForeColor=Theme.For(currentValue);currentValue.Text=currentText;currentValue.Invalidate();}
 }
 // 저장 확인을 기다리는 동안: 제목이 '저장 확인'으로 바뀌고, 곡 정보 카드는 등록을 요청한 판의 곡으로 고정되고, 목표·현재 자리에 이번 기록과 기존 최고 기록, 저장·취소가 보인다.
 // 그동안 다음 곡이 감지돼도 보이는 것은 저장·취소할 때까지 요청한 판이다. 기존 최고 기록은 기록이 바뀌면 그 값으로 다시 그린다(저장은 그 순간의 기록과 견준다).
 // 저장하거나 취소하면 곡 정보와 목표·현재로 돌아간다. 창을 따로 띄우지 않으므로 주 화면이 가려져 있어도 게임에서 포커스를 가져가지 않는다(그때는 오버레이를 보고 단축키로 정한다).
 void ShowConfirmArea()
 {
  var pending=session.Pending;bool confirming=pending!=null;
  // 등록을 누른 순간(점수 확인이 시작될 때) 한 번: 목표·현재 아래로 빛이 지난다
  if(activeRegistration!=null&&!ReferenceEquals(activeRegistration,scannedRequest)){scannedRequest=activeRegistration;if(!confirmShown)Scan();}
  if(pending!=null)
  {
   if(hotkeys.Items.Count==5)confirmPanel.Keys(Hotkeys.Format(hotkeys.Items[2]),Hotkeys.Format(hotkeys.Items[3]));
   confirmPanel.Set(PendingView(pending.Chart,pending.Accuracy,pending.RawScore,pendingNote));
  }
  if(confirmShown==confirming||songHead==null)return;
  var before=CaptureSlot();Motion.Stop(scanRun);
  confirmShown=confirming;
  foreach(var part in readout)part.Visible=!confirming;confirmPanel.Visible=confirming;
  songHead.Text=confirming?"저장 확인":"곡 정보";songHead.Width=songHead.TextWidth+2;songHead.Invalidate();
  if(pending!=null){var song=catalog.Songs.Single(s=>s.Id==pending.Chart.SongId);cardChart=pending.Chart;ShowSong(song,$"{pending.Chart.Button}B",pending.Chart.Pattern,pending.Chart.Judge+"판정",false);}
  else
  {
   pendingNote=null;if(displayedSelection!=null){var back=displayedSelection;var song=catalog.Songs.Single(s=>s.Id==back.SongId);cardChart=back;ShowSong(song,$"{back.Button}B",back.Pattern,back.Judge+"판정",false);}
   // 카드의 곡이 정해졌으니 목표를 그 곡의 지금 기록으로 다시 적는다(저장했으면 저장한 값이 보인다)
   if((session.Locked??session.Candidate??cardChart) is {} now)ShowStats(store.BestRate(now),session.Current);
  }
  // 앞의 모습이 흐려지고 뒤의 모습이 나타난다(저장 확인으로 0.085 + 0.17초, 곡 정보로 돌아올 때 0.085 + 0.18초)
  if(before!=null){var after=CaptureSlot();if(after!=null)fadeLayer.Play(before,after,.085,confirming?.17:.18);else before.Dispose();}
 }
 // 상태 줄 앞의 점: 등록 실패 안내나 조작 오류가 떠 있으면 빨강, 점수를 확인하거나 저장 확인을 기다리는 중이면 테마색, 곡이 고정돼 감지 중이면 연두, 그 밖에는 옅은 색
 void ShowStatusKind()
 {
  if(server==null)return;   // 창을 만드는 도중
  statusDot.Kind=scoreAdvice.Text.Length>0||AlertShown?"bad":activeRegistration!=null||session.Pending!=null?"stripe":session.Running&&session.Locked!=null?"good":"dim";
 }
 // 저장 확인에 보일 것. '기존 최고 기록'은 지금 저장돼 있는 값이다(확인을 기다리는 동안 기록이 바뀌면 다시 그린다. 저장은 그 순간의 값과 견준다).
 // 등록할 기록의 색: 기존 기록 이상이면 연두, 미만이면 빨강(스코어가 있으면 스코어로, 없으면 퍼센트를 스코어로 바꿔 견준다)
 ConfirmView PendingView(Selection chart,decimal accuracy,int? rawScore,string? note=null)
 {
  var old=store.Best(chart.Key);var song=catalog.Songs.Single(s=>s.Id==chart.SongId);
  string? tone=old==null?null:(rawScore??ScoreValue.FromAccuracy(accuracy))>=old.Score?"good":"bad";
  return new(OverlayText.Title(catalog,song),$"{chart.Button}B",chart.Pattern,chart.Judge+"판정",$"{accuracy:F2}%",rawScore?.ToString("N0"),old==null?"미등록":$"{ScoreValue.Rate(old.Score):F2}%",old!=null&&rawScore!=null?old.Score.ToString("N0"):null,tone,note);
 }
 // 자주 바뀌는 글(상태, SCORE 상태)은 너비를 고정하고, 줄 수가 달라질 때만 높이를 고친다.
 // 크기를 글에 맞추게 두면(AutoSize) 글이 바뀔 때마다 창 전체의 레이아웃을 다시 계산한다. 그 일이 한 번에 수십 ms 걸리고,
 // 화면 캡처가 같은 화면 스레드에서 0.1초마다 돌기 때문에 캡처가 밀린다(SCORE 쪽 시험에서 초당 캡처가 준 것으로 드러났다).
 static void Steady(Label label,int width)
 {
  label.AutoSize=false;label.MaximumSize=Size.Empty;label.MinimumSize=Size.Empty;label.Width=width;
  void Fit(){int height=label.GetPreferredSize(new Size(label.Width,0)).Height;if(label.Height!=height)label.Height=height;}   // 너비와 글꼴은 창이 뜰 때 커질 수 있다
  label.TextChanged+=(_,_)=>Fit();label.FontChanged+=(_,_)=>Fit();Fit();
 }
 void SaveOptions()=>new UserOptions(keptFullscreen,scoreImagesOption.Checked,Theme.Dark,ChosenScale==100,sender?.Settings??savedSender,ChosenScale).Save(optionsPath);
 // 앱 표시 크기: 100·125·150·175·200% 에서 고른다. 다시 켤 때 적용된다(열려 있는 창의 글꼴과 자리를 모두 다시 만들어야 해서).
 // 예전의 '작게 보기' 체크 상자를 대신한다(켜 두었던 사람은 100% 로 이어받는다. Theme.FirstChoice)
 readonly ThemedCombo scaleChoice=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=76,Roomy=true,AccessibleName="앱 표시 크기"};
 // 마지막으로 설정 파일에 적은 앱 표시 크기. 고른 것을 저장하지 못하면 이 값으로 선택을 돌린다(저장하지 못했는데 '다시 실행하면 적용됩니다'가 남지 않게)
 int savedScale=Theme.Chosen;bool scaleReverting;
 void PickScale()
 {
  try{SaveOptions();savedScale=ChosenScale;}
  catch
  {
   scaleReverting=true;
   try{int index=Array.IndexOf(Theme.Steps,savedScale);if(index>=0)scaleChoice.SelectedIndex=index;}
   finally{scaleReverting=false;}
   throw;
  }
  finally{ShowScaleNote();}
 }
 // 왼쪽 칸(MainForm 의 생성자): 첫 제목 아래와 '앱 표시 크기' 줄 사이의 설정. 칸에 다 들어가지 않으면 그 사이만 굴린다
 readonly FlowLayoutPanel leftBody=new();readonly VerticalScroller leftScroll=new();FlowLayoutPanel? leftColumn;Control? scaleRow;
 /// <summary>왼쪽 칸의 가운데를 굴리는 중인가(개발용 검사)</summary>
 internal bool LeftScrolled=>leftScroll.Scrolling;
 void FitLeft()
 {
  if(leftColumn==null||scaleRow==null)return;
  // 굴리는 칸의 너비는 왼쪽 칸의 오른쪽 끝까지(굴릴 때 세로 막대가 그 여백에 놓인다). 높이는 내용의 높이와 '앱 표시 크기' 줄 위까지 남은 높이 가운데 작은 것
  int full=leftBody.Height,room=leftColumn.ClientSize.Height-leftScroll.Top-scaleRow.Height-scaleRow.Margin.Vertical-Theme.S(6);
  bool scroll=full>room&&room>0;
  leftScroll.Scrolling=scroll;
  var size=new Size(leftColumn.ClientSize.Width-leftColumn.Padding.Left,scroll?room:full);if(leftScroll.Size!=size)leftScroll.Size=size;
 }
 readonly Label scaleNote=new(){AutoSize=true,Tag="dim",UseMnemonic=false};
 // 진단 그림 저장의 설명(두 문단)과 그것을 펴 보이는 상자
 internal const string ImagesHelpText="SCORE 인식 문제를 확인할 때 켜 주세요. 등록 키를 누른 순간의 게임 화면과 점수를 읽는 데 사용한 화면을 이 PC에 저장합니다.\n진단 기록 폴더의 images에 최대 200MB까지 보관하며, 용량을 넘으면 오래된 그림부터 지웁니다. 그림은 자동 전송되지 않습니다.";
 readonly FlowLayoutPanel imagesHelpBox=new(){AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,FlowDirection=FlowDirection.TopDown,WrapContents=false,Tag="field"};
 /// <summary>진단 그림 저장의 설명이 펴져 있는가</summary>
 internal bool ImagesHelpShown=>imagesHelpOpen;
 bool imagesHelpOpen;
 void ShowImagesHelp(bool open){imagesHelpOpen=open;imagesHelpBox.Visible=open;}
 // Esc: 설명이 펴져 있으면 닫는다(다른 뜻의 Esc 는 이 창에 없다)
 protected override bool ProcessCmdKey(ref Message msg,Keys keyData){if(keyData==Keys.Escape&&imagesHelpOpen){ShowImagesHelp(false);return true;}return base.ProcessCmdKey(ref msg,keyData);}
 /// <summary>선택 상자에서 고른 단계(퍼센트)</summary>
 int ChosenScale=>scaleChoice.SelectedIndex>=0?Theme.Steps[scaleChoice.SelectedIndex]:Theme.Chosen;
 /// <summary>
 /// 선택 상자 옆의 글: 이번 실행을 시작할 때와 다른 것을 골라 둔 동안에는 '다시 실행하면 적용됩니다',
 /// 고른 그대로인데 화면이 작아 더 낮은 단계로 그리고 있으면 '이 화면에서는 N%로 표시됩니다', 고른 것과 보이는 것이 같으면 없다
 /// </summary>
 internal static string ScaleNote(int picked,int started,int shown)=>picked!=started?"다시 실행하면 적용됩니다":shown!=started?$"이 화면에서는 {shown}%로 표시됩니다":"";
 void ShowScaleNote(){string text=ScaleNote(ChosenScale,Theme.Chosen,Theme.Shown);if(scaleNote.Text!=text)scaleNote.Text=text;}
 bool OnScreen(Control control)=>scrollArea==null||control.RectangleToScreen(control.ClientRectangle).IntersectsWith(scrollArea.RectangleToScreen(scrollArea.ClientRectangle));
}
