using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using DjmaxScore;
namespace LadderRecords;

// 창의 바탕(제목 띠, 글꼴, 색)을 실제 화면에서 찍어 본다: --frame-check <dark|light>
// 주 화면과 알림 창·선택 창을 차례로 띄워 화면을 그대로 복사하고(그림 frame-*.png), 창과 내용의 자리를 frame-check.txt 에 적는다.
// 다른 화면 검사(--ui-check)의 그림은 프로그램이 창을 비트맵에 직접 그린 것이라 Windows 가 그리는 창 버튼과 띠가 나오지 않는다. 이 검사는 화면에 실제로 보이는 것을 남긴다.
// 창이 잠깐 화면 맨 위에 뜬다. 개인 기록을 쓰지 않는 폴더에서만 돌린다(포트 18775, 단축키를 등록하지 않는다).
[System.Runtime.Versioning.SupportedOSPlatform("windows10.0.19041")]
static class FrameChecks
{
 [StructLayout(LayoutKind.Sequential)]struct Rect{public int Left,Top,Right,Bottom;}
 [DllImport("dwmapi.dll")]static extern int DwmGetWindowAttribute(IntPtr window,int attribute,out Rect value,int size);
 [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr window,int message,IntPtr w,IntPtr l);
 [DllImport("user32.dll")]static extern bool RegisterHotKey(IntPtr window,int id,uint modifiers,uint key);
 [DllImport("user32.dll")]static extern bool UnregisterHotKey(IntPtr window,int id);
 // 다른 창이 먼저 잡은 조합을 주면 Windows 가 거절하고, 어느 동작인지가 전해지는가(단축키 설정 창이 그 줄을 짚는 근거).
 // 평소 쓰는 조합과 겹치지 않게 Ctrl+Alt+Shift 조합으로 본다. 그 조합조차 누가 쓰고 있으면 확인하지 못한 것으로 적는다
 static string RealRefusal()
 {
  using var other=new Form();using var mine=new Form();
  if(!RegisterHotKey(other.Handle,1,7|0x4000,(uint)Keys.F1))return "hotkey-refusal: 확인하지 못함(Ctrl+Alt+Shift+F1 을 다른 프로그램이 쓰고 있다)";
  try
  {
   using var keys=new Hotkeys(mine.Handle);
   var wanted=Hotkeys.Defaults().Select(k=>k with{Modifiers=7}).ToList();wanted[2]=wanted[2] with{Key=Keys.F1};
   try{keys.Set(wanted);return "hotkey-refusal: 틀림(거절되지 않았다)";}
   catch(HotkeyTaken taken){return taken.Index==2?"hotkey-refusal: 맞음(셋째 동작이 거절됨: "+taken.Message+")":"hotkey-refusal: 틀림(거절된 동작 "+taken.Index+")";}
  }
  finally{UnregisterHotKey(other.Handle,1);}
 }
 // 단축키를 적용했는데 설정 파일을 쓰지 못하면 적용 전 조합으로 되돌아가는가(켜 둔 동안의 키와 파일의 키가 달라지지 않게). 쓸 수 있게 한 뒤에는 그대로 적용되는가.
 // 주 화면이 쓰는 설정 파일의 자리를 이 검사의 파일로 바꿔 놓고 본다(단축키 설정 창의 '적용'이 부르는 것과 같은 메서드)
 static string SaveFailure(MainForm main)
 {
  var hidden=BindingFlags.Instance|BindingFlags.NonPublic;
  string file=Path.Combine(AppContext.BaseDirectory,"frame-check-hotkeys.json");
  var keys=(Hotkeys)typeof(MainForm).GetField("hotkeys",hidden)!.GetValue(main)!;
  typeof(MainForm).GetField("hotkeysPath",hidden)!.SetValue(main,file);
  var apply=typeof(MainForm).GetMethod("ApplyHotkeys",hidden)!;
  var before=keys.Items.ToList();string saved=JsonSerializer.Serialize(before);
  if(File.Exists(file))File.SetAttributes(file,FileAttributes.Normal);
  File.WriteAllText(file,saved);
  var wanted=before.ToList();wanted[0]=wanted[0] with{Key=Keys.K};
  File.SetAttributes(file,FileAttributes.ReadOnly);
  try
  {
   var error=(Exception?)apply.Invoke(main,new object[]{wanted});
   bool kept=keys.Items.SequenceEqual(before)&&File.ReadAllText(file)==saved;
   File.SetAttributes(file,FileAttributes.Normal);
   var second=(Exception?)apply.Invoke(main,new object[]{wanted});
   bool applied=second==null&&keys.Items.SequenceEqual(wanted)&&File.ReadAllText(file)==JsonSerializer.Serialize(wanted);
   return error!=null&&kept&&applied?"hotkey-save-failure: 맞음(쓰지 못할 때 예전 조합 그대로, 쓸 수 있게 한 뒤에는 적용됨. 알림: "+error.Message+")":$"hotkey-save-failure: 틀림(error={error?.Message} kept={kept} applied={applied} second={second?.Message})";
  }
  finally{File.SetAttributes(file,FileAttributes.Normal);}
 }
 public static void Run(Catalog catalog,string mode)
 {
  Theme.Dark=mode!="light";
  // 글꼴 파일을 새로 읽어 싣는 데 걸리는 시간(이 프로세스가 이미 실은 것과 따로 잰다)
  var clock=System.Diagnostics.Stopwatch.StartNew();string fontFolder=Path.Combine(AppContext.BaseDirectory,"fonts");using(var again=new System.Drawing.Text.PrivateFontCollection())if(Directory.Exists(fontFolder))foreach(var file in Directory.GetFiles(fontFolder,"*.ttf"))again.AddFontFile(file);double loadMs=clock.Elapsed.TotalMilliseconds;
  var lines=new List<string>{$"mode={mode} bar={AppWindow.BarHeight} fonts=[{string.Join(", ",AppFonts.Loaded.OrderBy(n=>n))}] font-load-ms={loadMs:F0} chosen=[일반 {AppFonts.Ui.Name}, 제목 {AppFonts.Head.Name}, 영문 {AppFonts.Latin.Name}, 그 밖 {AppFonts.Wide.Name}]"};   // 글꼴 폴더 없이 돌리면 Windows 의 글꼴이 골라져야 한다
  string options=Path.Combine(AppContext.BaseDirectory,"frame-check-preferences.json");
  new UserOptions(false,false,Theme.Dark).Save(options);
  void Shot(Form form,string name)
  {
   form.TopMost=true;form.Activate();
   var until=DateTime.UtcNow.AddMilliseconds(900);while(DateTime.UtcNow<until){Application.DoEvents();Thread.Sleep(15);}
   DwmGetWindowAttribute(form.Handle,9,out var seen,Marshal.SizeOf<Rect>());DwmGetWindowAttribute(form.Handle,5,out var buttons,Marshal.SizeOf<Rect>());
   var area=Rectangle.FromLTRB(seen.Left,seen.Top,seen.Right,seen.Bottom);
   using(var shot=new Bitmap(area.Width,area.Height)){using(var g=Graphics.FromImage(shot))g.CopyFromScreen(area.Location,Point.Empty,area.Size);shot.Save(Path.Combine(AppContext.BaseDirectory,$"frame-{name}-{mode}.png"),ImageFormat.Png);}
   var first=form.Controls.Cast<Control>().FirstOrDefault();
   string Hit(int x,int y)=>((long)SendMessage(form.Handle,0x84,IntPtr.Zero,(IntPtr)((y<<16)|(x&0xFFFF)))).ToString();
   lines.Add($"{name}: window={form.Width}x{form.Height} seen={area.Width}x{area.Height} client={form.ClientSize.Width}x{form.ClientSize.Height} padding-top={form.Padding.Top} first-child={first?.GetType().Name}@{first?.Bounds} buttons=({buttons.Left},{buttons.Top},{buttons.Right},{buttons.Bottom})"
    +$" hit-bar={Hit(form.Left+form.Width/3,form.Top+AppWindow.BarHeight/2)} hit-body={Hit(form.Left+form.Width/3,form.Top+AppWindow.BarHeight+40)} hit-close={Hit(form.Left+buttons.Right-12,form.Top+(buttons.Top+buttons.Bottom)/2)}");
  }
  using(var main=new MainForm(catalog,18775,false,options))
  {
   main.StartPosition=FormStartPosition.Manual;main.Location=new Point(60,40);main.Show();Shot(main,"main");
   // 곡 정보 카드: 짧은 이름, 줄여서 다 보이는 이름, 끝이 줄어드는 이름, 한자가 든 이름. 이름마다 맞춘 글자 크기와 말줄임 여부를 적는다
   var hidden=BindingFlags.Instance|BindingFlags.NonPublic;
   T Part<T>(string name)=>(T)typeof(MainForm).GetField(name,hidden)!.GetValue(main)!;
   var card=Part<SongCard>("songTitle");var goal=Part<BigValue>("goalValue");var current=Part<BigValue>("currentValue");
   void Song(string title,string button,string pattern,string judge,string goalText,string currentText,string? tone,string name)
   {
    card.Text="";card.Text=title;Part<Chip>("buttonChip").Set(button);Part<Chip>("patternChip").Set(pattern);Part<Chip>("judgeChip").Set(judge);
    goal.Text=goalText;current.Tag=tone;current.ForeColor=Theme.For(current);current.Text=currentText;current.Invalidate();
    Shot(main,name);lines[^1]=$"{name}: title-px={card.TitlePx} cut={card.Cut} spacing={SongCard.TitleSpacing} text={title}";
   }
   Part<Label>("status").Text="프리스타일 · 노말판정";
   Song("Sweet On You","8B","SC","노말판정","99.90%","99.95%","good","main-title-short");
   Song("어릴적 할머니가 들려주신 옛 전설","6B","MAXIMUM","하드판정","99.42%","98.07%","bad","main-title-fit");
   Song("Tekken Tag Tournament Piano Intro -Massive True Mix-","4B","NORMAL","맥스판정","미등록","100.00%",null,"main-title-cut");
   Song("小緋縅(kohiodoshi)","5B","HARD","노말판정","97.31%","—",null,"main-title-kanji");
   // 글자 사이를 좁히지 않은 모습(디자인 기준은 −0.025em. 견줘 볼 그림이다)
   float spacing=SongCard.TitleSpacing;SongCard.TitleSpacing=0;
   Song("Sweet On You","8B","SC","노말판정","99.90%","99.95%","good","main-title-short-plain");
   Song("어릴적 할머니가 들려주신 옛 전설","6B","MAXIMUM","하드판정","99.42%","98.07%","bad","main-title-fit-plain");
   SongCard.TitleSpacing=spacing;
   // 연출의 한 순간(진행도 40% 에 붙잡아 찍는다): 감지 시작의 게임 화면 테두리, 곡 정보가 온 순간(타이핑·꼬리표·카드 아래의 빛), 그 판의 첫 SCORE, 등록 요청의 빛
   {
    void Do(string name,params object[] values)=>typeof(MainForm).GetMethod(name,hidden)!.Invoke(main,values);
    var frames=TypingFrames.Of("아침형 인간");var(gap,total)=TypingFrames.Timing(frames.Count);
    var longFrames=TypingFrames.Of(OverlayText.ReferenceTitle);var(longGap,longTotal)=TypingFrames.Timing(longFrames.Count);
    lines.Add($"typing: '아침형 인간' 장면 {frames.Count}개({string.Join(" ",frames.Take(5))} …), 사이 {gap*1000:F0}ms, 다 쳐지는 때 {(frames.Count-1)*gap:F2}초, 끝 {total:F2}초 · 긴 영문 곡명 장면 {longFrames.Count}개, 다 쳐지는 때 {(longFrames.Count-1)*longGap:F2}초, 끝 {longTotal:F2}초 · 동작 줄이기 {Motion.Reduced}");
    Motion.Frozen=.4;
    card.Text="";Part<Session>("session").Running=true;
    Do("TraceGame");Do("TraceScore");Do("Scan");
    Do("ShowSong",catalog.Songs.First(s=>s.Name=="어릴적 할머니가 들려주신 옛 전설"),"6B","MAXIMUM","하드판정",true);
    Shot(main,"main-motion");lines[^1]="main-motion: 진행도 40% 에 붙잡은 연출 넷(그림)";
    Motion.Frozen=null;Do("StopMotion");Part<Session>("session").Running=false;
   }
   // 아래의 안내 자리: 다섯 가지 등록 실패 안내가 창 안에 끝까지 들어가는가(내용의 높이 762), SCORE 상태가 여러 줄일 때의 모습
   var advice=Part<Label>("scoreAdvice");var box=Part<AdviceBox>("adviceBox");var scoreStatus=Part<Label>("scoreStatus");var root=main.Controls[0];
   foreach(string reason in new[]{"LocationUncertain","NotEnoughPlayEvidence","ZeroScore","ModelUnavailable",""})
   {
    var(brief,next)=ScoreFailure.Describe(reason);advice.Text=$"등록 실패: {brief}. {next}";Application.DoEvents();
    int bottom=root.PointToClient(box.Parent!.PointToScreen(new Point(0,box.Bottom))).Y;
    lines.Add($"advice[{reason}]: top={box.Top} height={box.Height} lines={box.Lines} bottom-in-content={bottom} fits={bottom<=Theme.S(762)} text={advice.Text}");
   }
   Song("Sweet On You","8B","SC","노말판정","99.90%","99.95%","good","main-advice");
   advice.Text="";
   scoreStatus.Text="SCORE 위치 고정\n게임 화면 1600×900 · SCORE 자동 인식은 1920×1080 게임 화면을 기준으로 개발되었습니다. 낮은 해상도에서는 숫자 인식이 불안정할 수 있으며, 다른 해상도와 화면 비율은 충분히 검증되지 않았습니다. 게임 해상도를 1920×1080으로 권장합니다.";
   Song("Sweet On You","8B","SC","노말판정","99.90%","99.95%","good","main-score-note");
   var note=Part<Label>("scoreNote");lines.Add($"score-note: top={note.Top} height={note.Height} bottom={note.Bottom} visible={note.Visible}");
   scoreStatus.Text="SCORE 위치 탐색 중";
   // 저장 확인: SCORE 모듈의 답을 만들어 넣어 실제 흐름으로 띄운다(기록은 이 검사의 임시 파일에 쓴다). 기존 기록이 없을 때, 낮을 때, 높을 때
   {
    string temp=Path.Combine(AppContext.BaseDirectory,"frame-check-records.json");foreach(string end in new[]{"",".bak",".tmp"})File.Delete(temp+end);   // 지난 실행의 것을 치운다(백업만 남아 있으면 저장소가 열리지 않는다)
    var store=new RecordStore(temp);typeof(MainForm).GetField("store",hidden)!.SetValue(main,store);typeof(MainForm).GetMethod("RefreshProfiles",hidden)!.Invoke(main,null);
    var session=Part<Session>("session");session.Running=true;
    var sweet=catalog.Songs.First(s=>s.Name=="Sweet On You");var chart=new Selection(sweet.Id,8,"SC","노말",0);const string key="FREESTYLE|Sweet On You|8|SC";
    void Confirm(int? old,string name)
    {
     if(old is int value)store.SetManual(chart,ScoreValue.Rate(value),1,rawScore:value);
     var request=Activator.CreateInstance(typeof(MainForm).GetNestedType("RegistrationRequest",BindingFlags.NonPublic)!,Part<int>("epoch"),key,chart,store.Active.Id,System.Diagnostics.Stopwatch.GetTimestamp(),DateTime.UtcNow,RateSource.Unknown,"FREESTYLE",0,0,new CancellationTokenSource())!;
     typeof(MainForm).GetField("activeRegistration",hidden)!.SetValue(main,request);
     typeof(MainForm).GetMethod("OnScoreAnswer",hidden)!.Invoke(main,new object[]{request,new ScoreAnswer(1,0,key,new RegistrationResult(1,1,RegistrationStatus.Accepted,"",999511,999511,null,null,0,0,0),0,0)});
     Shot(main,name);var panel=Part<ConfirmPanel>("confirmPanel");var layer=Part<FadeLayer>("fadeLayer");
     string fade=layer.Visible?$" 전환 그림이 덮인 동안의 마우스 판정 {(long)SendMessage(layer.Handle,0x84,IntPtr.Zero,IntPtr.Zero)}(-1 이면 아래의 단추가 받는다)":"";
     lines[^1]=$"{name}: panel={panel.Bounds} visible={panel.Visible} save={panel.Save.Bounds} '{panel.Save.Key}' cancel={panel.Cancel.Bounds} '{panel.Cancel.Key}' status={Part<Label>("status").Text}{fade}";
     typeof(MainForm).GetMethod("Decide",hidden)!.Invoke(main,new object[]{false});
    }
    // 저장 확인이 나타나는 도중(진행도 75%: 목표·현재가 사라진 뒤 저장 확인이 절반쯤 나타난 때)
    Motion.Frozen=.75;Confirm(null,"main-confirm-fade");Motion.Frozen=null;typeof(MainForm).GetMethod("StopMotion",hidden)!.Invoke(main,null);
    Confirm(null,"main-confirm-first");Confirm(999034,"main-confirm-higher");Confirm(999824,"main-confirm-lower");
    session.Running=false;
   }
   using(var notice=new NoticeDialog("실행 오류","기록 파일을 읽을 수 없습니다.\n파일 위치를 확인한 뒤 다시 실행하세요."){StartPosition=FormStartPosition.Manual,Location=new Point(120,120)}){notice.Show(main);Shot(notice,"notice");}
   void Open(Form dialog,string name){using(dialog){dialog.StartPosition=FormStartPosition.Manual;dialog.Location=new Point(120,120);dialog.Show(main);Shot(dialog,name);}}
   // 공통 안내창: 켤 때의 안내(이미 실행 중, 포트, 그 밖의 오류, 기록 파일, 기록 안내), 고르는 창, 놓을 수 있는 것 전부(경로 상자·절차·주소·펼치는 줄)를 접었을 때와 폈을 때
   {
    string recordsPath=Path.Combine("C:\\Games\\DJMAXLocalRecords",UserFiles.FolderName,"records.json");
    Open(new GuideDialog(StartGuides.AlreadyRunning),"guide-running");
    Open(new GuideDialog(StartGuides.Error(new System.Net.Sockets.SocketException(10048))),"guide-port");
    Open(new GuideDialog(StartGuides.Error(new FileNotFoundException("곡 목록 파일(data/catalog.json)을 찾을 수 없습니다."))),"guide-error");
    Open(new GuideDialog(StartGuides.Error(new UserFilesException(RecordStore.Missing(recordsPath,new(){".bak",".tmp"})))),"guide-records");
    Open(new GuideDialog(StartGuides.Error(new UserFilesException(RecordStore.Newer(recordsPath,3)))),"guide-newer");
    Open(new GuideDialog(StartGuides.Records(UserFiles.ChangedText)),"guide-notice");
    Open(new GuideDialog(new Guide("이전 버전 복구","이전 버전으로 복구할까요?",new GuidePart[]{new GuideText("v1.1.0 으로 돌아갑니다.","strong"),new GuideText("플레이 기록과 설정은 그대로입니다.","dim")},new[]{"복구","취소"},1)),"guide-choice");
    var full=new Guide("업데이트 실패","업데이트를 마치지 못했습니다",new GuidePart[]
    {
     new GuideText("기존 파일을 되돌리는 데도 실패해 두 버전의 프로그램 파일이 섞여 있을 수 있습니다.","strong"),new GuideText("플레이 기록과 설정은 그대로입니다.","dim"),new GuideText("아래 백업에서 프로그램 파일을 직접 복구해 주세요.","strong"),
     new GuidePaths(("복사할 백업 폴더","D:\\DJMAXLocalRecords\\update\\backup\\v1.1.0-20261121-183409\\program"),("붙여넣을 프로그램 폴더","D:\\DJMAXLocalRecords")),
     new GuideSteps("디맥 개인 기록 저장 오버레이를 모두 종료합니다.","백업 폴더 안의 모든 파일과 폴더를 프로그램 폴더에 복사하고, 이름이 같은 파일은 덮어씁니다.","프로그램 폴더에서 DJMAXLocalRecords.exe를 실행합니다."),
     new GuideLink("자세한 복구 안내","https://github.com/Zionee148/DJMAXLocalRecords"),
     new GuideFold("오류 내용",new GuideText("파일 교체 중 오류\n다른 프로그램에서 파일을 사용하고 있습니다."),new GuideText("기존 파일 복원 중 오류\n파일에 접근할 수 없습니다.")),
    });
    using(var guide=new GuideDialog(full){StartPosition=FormStartPosition.Manual,Location=new Point(120,60)})
    {
     guide.Show(main);Shot(guide,"guide-full");int closed=guide.Height,wide=guide.Width;
     guide.Folds[0].Flip();Shot(guide,"guide-full-open");int opened=guide.Height;var area=Screen.FromControl(guide).WorkingArea;
     lines.Add($"guide-dialog: {(opened>closed&&guide.Width==wide&&guide.Bottom<=area.Bottom?"맞음":"틀림")}(너비 {wide} · 단계 {Theme.Scale}. 접었을 때 높이 {closed}, '오류 내용'을 펴면 {opened} 로 늘고 너비는 그대로, 아래 끝 {guide.Bottom} 이 작업 영역 {area.Bottom} 안)");
    }
   }
   // 켜져 있는 프로그램이 띄우는 업데이트·복구 창: 새 버전 안내(짧은·긴 패치노트), 진행 창(다운로드·백업·교체로 넘기기 직전), 업데이트 실패(다운로드·이름 충돌·목록),
   // 작업 중 안내, 복구 확인(같은 폴더·옛 자리), 백업 없음, 지난 업데이트 중단(복구 방법을 편 것), 복구를 시작하지 못함. 글이 잘리지 않는가(첫 줄이 한 줄에, 글이 창 안에)
   {
    var now=new Version(1,1,0);string app="D:\\DJMAXLocalRecords";
    var shortRelease=new ReleaseInfo(new Version(1,1,1),"## 기록 히스토리\n곡과 패턴별로 저장한 점수를 확인할 수 있습니다.\n\n## 디자인\n설정 창과 기록 목록의 디자인을 통일했습니다.",UpdateSource.Official.PagePrefix+"tag/v1.1.1",UpdateChecker.AssetName(new Version(1,1,1)),UpdateSource.Official.DownloadPrefix+"v1.1.1/x.zip",134_217_728,new string('a',64));
    var clipped=new List<string>();
    // 글을 가진 컨트롤이 제 부모의 안에 다 들어가 있는가(오른쪽·아래가 잘리지 않았는가)
    void Fits(Form form,string name)
    {
     static IEnumerable<Control> Each(Control top){foreach(Control child in top.Controls){yield return child;foreach(var inner in Each(child))yield return inner;}}
     foreach(var c in Each(form).Where(c=>c.Visible&&c.Parent!=null&&c.Text.Length>0&&c is Label or Button or SectionHeader or WrapBox or WordLabel))
      if(c.Right>c.Parent!.ClientSize.Width+1||c is SectionHeader header&&header.TextWidth>header.Width+2)clipped.Add($"{name}:{c.Text[..Math.Min(12,c.Text.Length)]}");
    }
    void Show(Form dialog,string name,Action<Form>? before=null){using(dialog){dialog.StartPosition=FormStartPosition.Manual;dialog.Location=new Point(120,60);dialog.Show(main);before?.Invoke(dialog);Shot(dialog,name);Fits(dialog,name);}}
    Show(new UpdateDialog(shortRelease,UpdateSource.Official),"update-offer");
    bool scrolled=false;
    Show(new UpdateDialog(shortRelease with{Notes=string.Join("\n",Enumerable.Range(1,40).Select(i=>$"- 고친 것 {i}: 긴 패치노트의 한 줄"))},UpdateSource.Official),"update-offer-long",dialog=>scrolled=((UpdateDialog)dialog).NotesArea.Scrolling);
    int[] heights=new int[3];bool locked=false;
    using(var gate=new ManualResetEventSlim())
    using(var progress=new UpdateProgressDialog(now,new Version(1,1,1)){StartPosition=FormStartPosition.Manual,Location=new Point(120,60)})
    {
     progress.Work=async(dialog,cancel)=>await Task.Run(()=>gate.Wait());
     progress.Show(main);Application.DoEvents();
     progress.Report(UpdatePrepare.Download,"48.0 / 128.0 MB",.375);Shot(progress,"update-progress-download");Fits(progress,"update-progress-download");heights[0]=progress.Height;
     progress.Report(UpdatePrepare.Backup,"기록과 설정을 백업하는 중",null);Shot(progress,"update-progress-backup");heights[1]=progress.Height;
     locked=progress.LockCancel();progress.Report(UpdatePrepare.Replace,"새 버전 설치를 시작하는 중",null);Shot(progress,"update-progress-locked");Fits(progress,"update-progress-locked");heights[2]=progress.Height;
     gate.Set();for(int i=0;i<200&&progress.Visible;i++){Application.DoEvents();Thread.Sleep(10);}
    }
    Show(new GuideDialog(UpdateGuides.Failed(new UpdateException("받는 도중 연결이 끊겼습니다(파일을 끝까지 받지 못했습니다).",UpdateTrouble.Network),now)),"update-failed-network");
    Show(new GuideDialog(UpdateGuides.Failed(new UpdateException("이 프로그램의 것이 아닌 같은 이름의 파일이나 폴더가 있습니다:\n  "+app+"\\overlay.html\n  "+app+"\\data\\new-in-111.txt",UpdateTrouble.Clash,app,new[]{app+"\\overlay.html",app+"\\data\\new-in-111.txt"}),now)),"update-failed-clash");
    Show(new GuideDialog(UpdateGuides.Failed(new UpdateException("지금 설치된 프로그램의 파일 목록(update-manifest.json)이 없거나 읽히지 않습니다.",UpdateTrouble.NoList),now)),"update-failed-nolist",dialog=>((GuideDialog)dialog).Folds[0].Flip());
    Show(new GuideDialog(UpdateGuides.Busy(UpdateBusy.Detecting,false)),"update-busy");
    Show(new GuideDialog(UpdateGuides.Busy(UpdateBusy.Scoring,true)),"restore-busy");
    Show(new GuideDialog(UpdateGuides.RestoreAsk(new Version(1,2,1),"1.2.0","2026-11-21 18:34:09")),"restore-ask");
    Show(new GuideDialog(UpdateGuides.RestoreAsk(new Version(1,2,0),"1.1.0","2026-11-21 18:34:09")),"restore-ask-old");
    Show(new GuideDialog(UpdateGuides.NoBackup),"restore-none");
    string backup=Path.Combine(AppContext.BaseDirectory,"frame-check-backup","v1.1.0-20261121-183409");Directory.CreateDirectory(UpdateBackup.Program(backup));File.WriteAllText(Path.Combine(UpdateBackup.Program(backup),UpdateManifest.Executable),"exe");
    try
    {
     Show(new GuideDialog(UpdateGuides.Interrupted(backup,app)),"update-interrupted");
     Show(new GuideDialog(UpdateGuides.Interrupted(backup,app)),"update-interrupted-open",dialog=>((GuideDialog)dialog).Folds[0].Flip());
     Show(new GuideDialog(UpdateGuides.RestoreNotStarted(new UpdateException("복구 과정이 40초 동안 응답하지 않았습니다.",UpdateTrouble.HandOver),backup,app)),"restore-not-started");
    }
    finally{try{Directory.Delete(Path.Combine(AppContext.BaseDirectory,"frame-check-backup"),true);}catch(IOException){}}
    bool steady=heights[0]==heights[1]&&heights[1]==heights[2];
    lines.Add($"update-windows: {(scrolled&&steady&&locked&&clipped.Count==0?"맞음":"틀림")}(긴 패치노트는 칸 안에서 굴림 {scrolled}, 진행 창의 높이가 다운로드·백업·잠금에서 같음 {string.Join("/",heights)}, 잠금 {locked}, 잘린 글 {(clipped.Count==0?"없음":string.Join(", ",clipped))})");
    // 교체·복구를 맡은 프로그램의 창: 설치 진행(교체), 업데이트 도중의 복구, 복구 진행, 새 버전 시작 실패의 선택(끝남·편 것, 켜져 있음),
    // 교체 실패(복구함·섞임), 파일을 바꾸기 전에 멈춤, 복구 완료·실패 셋, 직접 실행, 백업을 직접 열었을 때 둘, 임시 파일을 직접 열었을 때, 백업의 복구 실패 넷
    clipped.Clear();int[] installHeights=new int[2];
    string swapBackup=Path.Combine(AppContext.BaseDirectory,"frame-check-backup","v1.1.0-20261121-183409");Directory.CreateDirectory(UpdateBackup.Program(swapBackup));File.WriteAllText(Path.Combine(UpdateBackup.Program(swapBackup),UpdateManifest.Executable),"exe");
    try
    {
     // 컨트롤의 자리를 실제 화면에서 찍어 바탕과 다른 화소(글자)가 있는가
     static bool Inked(Control control)
     {
      var r=control.RectangleToScreen(control.ClientRectangle);if(r.Width<=0||r.Height<=0)return false;
      using var shot=new Bitmap(r.Width,r.Height);using(var g=Graphics.FromImage(shot))g.CopyFromScreen(r.Location,Point.Empty,r.Size);
      var back=shot.GetPixel(0,0);int inked=0;
      for(int y=0;y<shot.Height;y++)for(int x=0;x<shot.Width;x++){var p=shot.GetPixel(x,y);if(Math.Abs(p.R-back.R)+Math.Abs(p.G-back.G)+Math.Abs(p.B-back.B)>90)inked++;}
      return inked>20;
     }
     void Install(InstallWindow window,string name,Action<InstallWindow> show)
     {
      using var gate=new ManualResetEventSlim();
      using(window)
      {
       window.Work=w=>gate.Wait();window.StartPosition=FormStartPosition.Manual;window.Location=new Point(120,60);window.Show();Application.DoEvents();
       show(window);Application.DoEvents();Shot(window,name);Fits(window,name);if(!Inked(window.Detail))clipped.Add(name+": 상세 글이 화면에 그려지지 않음");
       gate.Set();for(int i=0;i<200&&window.Visible;i++){Application.DoEvents();Thread.Sleep(10);}
      }
     }
     Install(new InstallWindow("업데이트",UpdateGuides.InstallHead,"v1.1.0","v1.1.1",UpdatePrepare.Stages,UpdatePrepare.Replace,UpdateGuides.InstallWait),"install-swap",w=>{w.Stage(UpdatePrepare.Replace,UpdateGuides.Swapping);installHeights[0]=w.Height;});
     Install(new InstallWindow("업데이트",UpdateGuides.InstallHead,"v1.1.0","v1.1.1",UpdatePrepare.Stages,UpdatePrepare.Replace,UpdateGuides.InstallWait),"install-restoring",w=>{w.Restoring("v1.1.1","v1.1.0");w.Stage(UpdatePrepare.Replace,UpdateGuides.RollingBack);installHeights[1]=w.Height;});
     Install(new InstallWindow("이전 버전 복구",UpdateGuides.RestoreHead,"v1.1.1","v1.1.0",new[]{"교체","재실행"},0,UpdateGuides.RestoreWait),"restore-progress",w=>w.Stage(0,UpdateGuides.RestoreSwapping));
     var swap=new SwapResult(false,false,"다른 프로그램에서 파일을 사용하고 있습니다.","파일에 접근할 수 없습니다.");
     Show(new GuideDialog(UpdateGuides.StartFailed(false,"1.1.0","1.1.1",swapBackup)),"start-failed");
     Show(new GuideDialog(UpdateGuides.StartFailed(false,"1.1.0","1.1.1",swapBackup)),"start-failed-open",dialog=>((GuideDialog)dialog).Folds[0].Flip());
     Show(new GuideDialog(UpdateGuides.StartFailed(true,"1.1.0","1.1.1",swapBackup)),"start-slow");
     Show(new GuideDialog(UpdateGuides.SwapRolledBack("다른 프로그램에서 파일을 사용하고 있습니다.","1.1.0")),"swap-rolled-back");
     Show(new GuideDialog(UpdateGuides.SwapBroken(swap,swapBackup,app)),"swap-broken",dialog=>((GuideDialog)dialog).Folds[0].Flip());
     Show(new GuideDialog(UpdateGuides.SwapNotStarted(UpdateGuides.SwapStop.Mismatch,"1.1.0")),"swap-not-started");
     Show(new GuideDialog(UpdateGuides.RolledBack("1.1.0")),"rolled-back");
     Show(new GuideDialog(UpdateGuides.RollbackBlocked(swapBackup)),"rollback-blocked");
     Show(new GuideDialog(UpdateGuides.RollbackKeptNew("파일에 접근할 수 없습니다.","1.1.1",swapBackup)),"rollback-kept-new");
     Show(new GuideDialog(UpdateGuides.RollbackBroken(swap,swapBackup,app)),"rollback-broken");
     Show(new GuideDialog(UpdateGuides.DirectRun(UpdateGuides.RestoreTitle,app)),"direct-run");
     Show(new GuideDialog(UpdateGuides.BackupWhileRunning),"backup-running");
     Show(new GuideDialog(UpdateGuides.RestoreAskDirect("v1.1.1","1.1.0","2026-11-21 18:34:09")),"backup-restore-ask");
     Show(new GuideDialog(UpdateGuides.StagingOpened),"staging-opened");
     Show(new GuideDialog(UpdateGuides.RestoreFailed(UpdateGuides.RestoreStop.Restored,swapBackup,app,swap:new SwapResult(false,true,"다른 프로그램에서 파일을 사용하고 있습니다."){InUse=true})),"restore-failed-in-use");
     Show(new GuideDialog(UpdateGuides.RestoreFailed(UpdateGuides.RestoreStop.Running,swapBackup,app,running:true)),"restore-failed-running");
     Show(new GuideDialog(UpdateGuides.RestoreFailed(UpdateGuides.RestoreStop.Clash,swapBackup,app,clash:new[]{app+"\\overlay.html"})),"restore-failed-clash");
     Show(new GuideDialog(UpdateGuides.RestoreFailed(UpdateGuides.RestoreStop.Broken,swapBackup,app,swap:swap)),"restore-failed-broken",dialog=>((GuideDialog)dialog).Folds[0].Flip());
    }
    finally{try{Directory.Delete(Path.Combine(AppContext.BaseDirectory,"frame-check-backup"),true);}catch(IOException){}}
    lines.Add($"swap-windows: {(installHeights[0]==installHeights[1]&&clipped.Count==0?"맞음":"틀림")}(설치 진행과 업데이트 도중의 복구의 높이가 같음 {string.Join("/",installHeights)}, 잘린 글 {(clipped.Count==0?"없음":string.Join(", ",clipped))})");
   }
   // 진단 그림 저장의 설명을 편 주 화면(줄 아래에 펴진다)
   {
    var badge=Part<CheckBox>("scoreImagesOption").Parent!.Controls.OfType<HelpBadge>().Single();badge.Press(Keys.Space);Shot(main,"main-images-help");
    bool open=main.ImagesHelpShown,calm=!main.LeftScrolled;badge.Press(Keys.Space);
    lines.Add($"main-images-help: {(open&&calm&&!main.ImagesHelpShown?"맞음":"틀림")}(물음표를 누르면 줄 아래에 설명이 펴지고 다시 누르면 접힘. 투컴 안내가 없으면 펴도 왼쪽 칸을 굴리지 않음 {calm})");
   }
   Open(new NameDialog("새 프로필","프로필 이름","","확인"),"profile-new");
   Open(new NameDialog("프로필 복사","복사본 이름","기본 복사","복사","기본"),"profile-copy");
   Open(new NameDialog("프로필 이름 변경","새 이름","기본","변경"),"profile-rename");
   Open(new ProfileDeleteDialog("래더 연습",128),"profile-delete");
   Open(new ProfileDeleteDialog("이름이 아주 긴 프로필 이름이 아주 긴 프로필 이름이 아주 긴 프로필",0),"profile-delete-long");
   // 기록 히스토리에서 여는 창들: 안내, 한 판 수정, 삭제 확인
   {
    var play=new Play(7,"frame-check|8|SC|하드",998156,new DateTime(2026,11,20,10,28,44,DateTimeKind.Utc),PlaySource.Play,NormalLink.Linked);
    Open(new PlayNotice("기록 삭제","기록이 바뀌어 삭제를 멈췄습니다.","기록 히스토리에서 변경된 내용을 확인한 뒤 삭제할 기록을 다시 선택해 주세요."),"play-notice");
    Open(new EditPlayDialog("Sweet On You","8B","SC","하드",play,true),"play-edit");
    Open(new DeletePlayDialog(new("Sweet On You","8B","SC","하드",new[]{play},false,_=>new[]{"노말"})),"play-delete");
    Open(new DeletePlayDialog(new("카트라이더, 크레이지아케이드, 버블파이터 Main theme ~CHUCK Remix~","8B","MAXIMUM","노말",new[]{play},true,_=>Array.Empty<string>())),"play-delete-long");
   }
   // 수동 기록·되돌리기·가져오기의 창들: 수동 기록 설정(새 플레이, 직접 적는 시각과 펼친 도움말, 교체, 교체할 판 없음), 두 값을 위아래로 보이는 안내, 일괄 삭제 확인,
   // 되돌리기 확인(한 판, 수정, 노말 반영이 함께 사라지는 판, RATE 만 남은 줄, 가져오기, 일괄 삭제), V아카이브 가져오기(닉네임, 결과, 펼친 목록, 바뀔 것 없음, 불러오지 못함)
   {
    string folder=Path.Combine(AppContext.BaseDirectory,"frame-check-manual");Directory.CreateDirectory(folder);foreach(string old in Directory.GetFiles(folder))File.Delete(old);
    var store=new RecordStore(Path.Combine(folder,"records.json"));var at=new DateTime(2026,11,20,10,28,44,DateTimeKind.Utc);var culture=System.Globalization.CultureInfo.CurrentCulture;
    var sweet=catalog.Songs.First(s=>s.Name=="Sweet On You");var normal=new Selection(sweet.Id,8,"SC","노말",0);var hard=normal with{Judge="하드"};
    var morning=catalog.Songs.First(s=>s.Name=="아침형 인간");var fetched=new Selection(morning.Id,8,"SC","노말",0);
    var kart=catalog.Songs.First(s=>s.Name==OverlayText.KartTitle);var tekken=catalog.Songs.First(s=>s.Name==OverlayText.ReferenceTitle);
    store.Add(normal,999511,at);var recent=store.Add(normal,998870,at.AddHours(8));var hardPlay=store.Add(hard,998156,at.AddHours(9));
    store.Import(new Dictionary<Selection,decimal>{[fetched]=99.68m});
    RecordDialog.Clock=()=>new DateTime(2026,11,20,10,10,30,DateTimeKind.Local);
    try
    {
     RecordDialog Manual(Selection chart,int? score){var dialog=new RecordDialog(catalog,store,chart);if(score is int typed)dialog.Input.ScoreBox.Text=typed.ToString("N0",culture);return dialog;}
     var fresh=Manual(normal,997654);bool startsRight=fresh.BestShown=="999,511점 (99.95%)"&&fresh.UseNow.Checked&&!fresh.ReplaceOption.Checked&&fresh.SaveButton.Enabled&&!fresh.PracticeShown&&fresh.DeleteArchiveButton.Enabled;
     Open(fresh,"manual-new");
     var timed=Manual(hard,998870);timed.UseNow.Checked=false;timed.PracticeOption.Checked=true;timed.ToggleHelp();bool timeRight=timed.TimeBox.Text=="2026-11-20 10:10:30"&&timed.PracticeShown&&timed.HelpShown;
     Open(timed,"manual-time");
     var swap=Manual(hard,998870);swap.ReplaceOption.Checked=true;bool swapRight=swap.FixedTime==hardPlay.At!.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")&&!swap.PracticeOption.Enabled;
     Open(swap,"manual-replace");
     var none=Manual(fetched,null);bool noneRight=none.BestShown=="996,800점 (99.68% · 환산)"&&!none.ReplaceOption.Enabled&&none.TargetShown==ReplaceTargetView.None&&!none.SaveButton.Enabled;
     Open(none,"manual-none");
     lines.Add($"manual-dialog: {(startsRight&&timeRight&&swapRight&&noneRight?"맞음":"틀림")}(새 플레이는 최고 기록과 저장 시각 사용으로 시작 {startsRight}, 체크를 풀면 지금 시각이 채워지고 하드판정에 연습 칸이 보임 {timeRight}, 교체는 원래 시각을 보이고 연습 칸을 잠금 {swapRight}, 가져온 기록뿐이면 환산이라고 적고 교체·저장이 잠김 {noneRight})");
    }
    finally{RecordDialog.Clock=null;}
    Open(new PlayNotice("수동 기록 설정","입력하는 동안 기록이 바뀌었습니다.","입력한 점수를 저장하려면 수동 기록 설정에서 ‘저장’을 다시 눌러 주세요.",new[]{NoticeRow.Best("현재 최고 기록",store.Best(normal.Key)),NoticeRow.Entered("입력한 점수",997654)}),"manual-changed");
    Open(new PlayNotice("수동 기록 설정","입력하는 동안 기록이 바뀌었습니다.","입력한 점수를 저장하려면 수동 기록 설정에서 ‘저장’을 다시 눌러 주세요.",new[]{NoticeRow.Best("현재 최고 기록",store.Best(fetched.Key)),NoticeRow.Entered("입력한 점수",997654)}),"manual-changed-rate");
    Open(new PlayNotice("수동 기록 설정","입력하는 동안 프로필이 바뀌었습니다.","점수는 저장되지 않았습니다. 수동 기록 설정을 닫고, 기록을 남길 프로필에서 다시 열어 주세요.",new[]{NoticeRow.Word("입력을 시작한 프로필","기본"),NoticeRow.Word("현재 프로필","연습")}),"manual-profile");
    Open(new PlayNotice("수동 기록 설정","입력하는 동안 교체할 플레이가 바뀌었습니다.","교체할 플레이를 확인한 뒤 ‘저장’을 다시 눌러 주세요.",new[]{NoticeRow.Target("현재 교체할 플레이",recent),NoticeRow.Entered("입력한 점수",997654)}),"manual-target");
    Open(new PlayNotice("V아카이브 가져오기","기록을 가져오는 동안 프로필이 바뀌었습니다.","불러온 기록은 저장되지 않았습니다.\n메인 창에서 기록을 받을 프로필을 선택한 뒤 ‘V아카이브 가져오기’를 다시 눌러 주세요.",new[]{NoticeRow.Word("가져오기를 시작한 프로필","기본"),NoticeRow.Word("현재 프로필","연습")}),"import-profile");
    Open(new BulkDeleteDialog("기본",846),"bulk-delete");
    // 되돌리기 확인
    UndoDialog Undo(string kind,params PlayChange[] changes)=>new(UndoView.Of(new Operation(kind,at,99,changes.ToList()),"기본"),catalog);
    var saved=new Play(11,normal.Key,998771,new DateTime(2026,11,21,9,42,16,DateTimeKind.Utc),PlaySource.Play);var linked=new Play(12,hard.Key,998300,saved.At,PlaySource.Play,NormalLink.Linked);
    var rateOnly=new Play(13,fetched.Key,996800,null,PlaySource.VArchive);var longPlay=new Play(14,$"{kart.Id}|5|HARD|노말",987650,saved.At,PlaySource.Manual);
    Open(Undo(PlayOps.Add,new PlayChange(null,saved)),"undo-add");
    Open(Undo(PlayOps.Edit,new PlayChange(saved with{Score=998711},saved)),"undo-edit");
    Open(Undo(PlayOps.Add,new PlayChange(null,linked)),"undo-linked");
    Open(Undo(PlayOps.Unlink,new PlayChange(linked,linked with{Normal=NormalLink.Cancelled})),"undo-unlink");
    Open(Undo(PlayOps.Delete,new PlayChange(rateOnly,null)),"undo-rate");
    Open(Undo(PlayOps.Delete,new PlayChange(longPlay,null)),"undo-long");
    Open(Undo(PlayOps.Add,new PlayChange(null,saved with{Key=$"{catalog.Songs.First(s=>s.Name=="Alone").Id}|4|HARD|하드"})),"undo-samename");   // 이름이 같은 곡은 아티스트를 붙인다
    Open(Undo(PlayOps.ImportVArchive,Enumerable.Range(0,182).Select(n=>new PlayChange(null,rateOnly with{Seq=100+n})).Concat(Enumerable.Range(0,24).Select(n=>new PlayChange(rateOnly with{Seq=400+n},rateOnly with{Seq=400+n,Score=997000}))).ToArray()),"undo-import");
    Open(Undo(PlayOps.DeleteVArchive,Enumerable.Range(0,846).Select(n=>new PlayChange(rateOnly with{Seq=100+n},null)).ToArray()),"undo-bulk");
    // V아카이브 가져오기: 응답은 합성한 것을 준다(V아카이브에 묻지 않는다)
    try
    {
     var listed=new[]{(tekken,99.54m),(kart,99.88m),(morning,99.12m),(catalog.Songs.First(s=>s.Name.StartsWith("小緋縅")),97.46m)}.ToDictionary(pair=>new Selection(pair.Item1.Id,pair.Item1.Charts[0].Button,pair.Item1.Charts[0].Pattern,"노말",0),pair=>pair.Item2);
     store.Import(new Dictionary<Selection,decimal>{[listed.Keys.ElementAt(2)]=98m});
     Exception? failure=null;Dictionary<Selection,decimal> answer=listed;
     ImportDialog.Fetcher=(_,_)=>failure!=null?Task.FromException<Dictionary<Selection,decimal>>(failure):Task.FromResult(answer);
     ImportDialog Import(){var dialog=new ImportDialog(catalog,store){StartPosition=FormStartPosition.Manual,Location=new Point(120,120)};dialog.Show(main);return dialog;}
     void Fetch(ImportDialog dialog){dialog.NicknameBox.Text="RESPECT";dialog.FetchButton.PerformClick();dialog.Pending.GetAwaiter().GetResult();dialog.Location=new Point(120,120);}
     using(var dialog=Import())
     {
      Shot(dialog,"import-ask");int askWide=dialog.ClientSize.Width;
      Fetch(dialog);Shot(dialog,"import-result");int resultWide=dialog.ClientSize.Width;
      dialog.Fold.Flip();dialog.Location=new Point(120,120);Shot(dialog,"import-list");
      bool sized=askWide==Theme.S(428)&&resultWide==Theme.S(578),counted=dialog.Counts=="추가 3건 | 갱신 1건"&&dialog.Table.Rows.Count==4&&dialog.Table.HeaderDraws>0;
      lines.Add($"import-dialog: {(sized&&counted?"맞음":"틀림")}(닉네임을 받을 때 너비 {askWide}, 결과를 보일 때 {resultWide} · 단계 {Theme.Scale}. 건수 '{dialog.Counts}', 목록 {dialog.Table.Rows.Count}줄, 머리글을 직접 그림 {dialog.Table.HeaderDraws}번)");
     }
     using(var dialog=Import()){answer=new();Fetch(dialog);Shot(dialog,"import-empty");}
     using(var dialog=Import()){failure=new HttpRequestException("Response status code does not indicate success: 404 (Not Found).",null,System.Net.HttpStatusCode.NotFound);Fetch(dialog);Shot(dialog,"import-failed");}
    }
    finally{ImportDialog.Fetcher=null;}
   }
   // 기록 목록과 히스토리를 실제 화면으로 본다(머리글과 줄을 직접 그리는 표는 창을 그림으로 옮기는 방식(DrawToBitmap)으로는 머리글이 그대로 찍히지 않는다)
   {
    string folder=Path.Combine(AppContext.BaseDirectory,"frame-check-history");Directory.CreateDirectory(folder);foreach(string old in Directory.GetFiles(folder))File.Delete(old);
    var store=new RecordStore(Path.Combine(folder,"records.json"));var at=new DateTime(2026,11,20,9,0,0,DateTimeKind.Utc);
    var song=catalog.Songs.Single(s=>s.Name=="Fundamental");var hard=new Selection(song.Id,8,"SC","하드",1);var normal=hard with{Judge="노말"};
    store.Add(hard,998156,at);   // 노말 기록이 없을 때의 하드 판: 노말에도 보인다
    int[] scores={993900,995400,994300,996200,995700,961490,997300,998200,999511,997900,999511,998870};
    for(int i=0;i<scores.Length;i++)store.Add(normal,scores[i],at.AddMinutes(20+i*17));
    var imports=new Dictionary<Selection,decimal>{[normal]=99.54m};int more=0;
    foreach(var other in catalog.Songs.Where(s=>s.Name.Length>30).Take(4)){var c=other.Charts[0];store.Add(new(other.Id,c.Button,c.Pattern,more%2==0?"맥스":"노말",1),970734+more*4321,at.AddHours(-more));imports[new(other.Id,c.Button,c.Pattern,"노말",1)]=97.07m+more;more++;}
    // 일시 미상 기록만 있는 판정(V아카이브로만 들어온 패턴)과, 범위 밖 PB 의 예가 될 패턴
    var spare=song.Charts.Where(c=>!(c.Button==8&&c.Pattern=="SC")).Take(2).ToArray();var far=new Selection(song.Id,spare[0].Button,spare[0].Pattern,"노말",1);var only=new Selection(song.Id,spare[1].Button,spare[1].Pattern,"노말",1);imports[only]=99.20m;
    store.Import(imports);
    // 범위 밖 PB 의 예: 날짜 있는 다섯 판(934,100~941,800)과 컬렉션의 997,200(범위 밖 PB 를 재현하려고 컬렉션 출처의 줄을 직접 넣는다)
    foreach(var (score,i) in new[]{934100,937642,936218,941800,940523}.Select((score,i)=>(score,i)))store.Add(far,score,at.AddHours(30+i));
    store.Active.Plays.Add(new Play(store.Active.NextSeq++,far.Key,997200,null,PlaySource.Collection));store.Save();
    using var records=new RecordsDialog(catalog,store){StartPosition=FormStartPosition.Manual,Location=new Point(40,40)};
    records.Show(main);Shot(records,"records");
    var grid=(RecordsView)typeof(RecordsDialog).GetField("grid",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(records)!;
    grid.SelectIndex(Enumerable.Range(0,grid.RowCount).First(i=>grid.ItemAt(i)!.Chart.Key==normal.Key));records.OpenHistory();
    var history=records.History!;history.Location=new Point(70,70);Shot(history,"history");
    bool picked=history.Table.Picked?.Play.Seq==history.Model.Dated[^1].Seq&&history.Table.Rows.Count==13&&history.Table.Rows.Count(r=>r.Tie)==2&&history.Table.Rows.Single(r=>r.Pb).Play.Seq==history.Model.Pb!.Seq&&history.Model.Undated.Count==1;
    lines.Add($"history-open: {(picked?"맞음":"틀림")}(뜬 직후 가장 최근 판이 표에서 골라져 있음, 열세 판·동점 둘·PB 하나·일시 미상 한 줄. 고른 줄 {history.Table.Picked?.Play.Seq}, 줄 {history.Table.Rows.Count})");
    // 그래프: 열세 판이라 가장 최근 열두 판이 보인다. 7회(961,490)는 낮은 판이라 마름모, PB 는 동점 둘 가운데 뒤의 판
    var graph=history.Graph;bool plotted=graph.Visible&&graph.Total==13&&graph.Start==1&&graph.Points.Count==12&&graph.Points.Where(p=>p.Low).Select(p=>p.Play.Score).SequenceEqual(new[]{961490})&&graph.Points.Single(p=>p.Pb).Play.Seq==history.Model.Pb!.Seq&&graph.Axis is {Bottom:990000,Top:1000000}&&history.RangeBox.Visible;
    lines.Add($"history-graph: {(plotted?"맞음":"틀림")}(열세 판 가운데 최근 열두 판, 낮은 판 하나는 마름모, PB 한 판, 세로축 990,000~1,000,000. 지금 {graph.Axis.Bottom}~{graph.Axis.Top}, 처음 {graph.Start}, 점 {graph.Points.Count})");
    graph.Hover(graph.SpotOf(graph.Points.Count-2));Shot(history,"history-tip");graph.Hover(new Point(2,2));
    history.RangeBox.SelectedIndex=1;Shot(history,"history-zoom");history.RangeBox.SelectedIndex=0;
    history.Fold.Flip();Shot(history,"history-folded");history.Fold.Flip();
    // 일시 미상 PB 가 날짜 있는 판보다 50,000 이상 높은 판정(범위 밖 PB), 일시 미상 기록만 있는 판정
    void Show(Selection chart,string name){grid.SelectIndex(Enumerable.Range(0,grid.RowCount).First(i=>grid.ItemAt(i)!.Chart.Key==chart.Key));records.OpenHistory();Shot(history,name);}
    Show(far,"history-outside");bool away=GraphMath.PbOutside(graph.Axis)&&graph.Total==5&&history.RangeBox.Visible&&graph.Axis is {Bottom:932000,Top:942000};
    Show(only,"history-undated-only");bool lone=graph.Total==0&&graph.Points.Count==0&&history.Table.Rows.Single().Group!=null;
    lines.Add($"history-graph-undated: {(away&&lone?"맞음":"틀림")}(범위 밖 PB {away}, 일시 미상 기록만 {lone})");
    bool drawn=grid.HeaderDraws>=grid.Columns.Count&&history.Table.HeaderDraws>=2&&grid.SelectedRecord!=null;
    lines.Add($"table-headers: {(drawn?"맞음":"틀림")}(두 표의 머리글을 직접 그림: 목록 {grid.HeaderDraws}번, 히스토리 {history.Table.HeaderDraws}번. 목록은 뜬 뒤 한 줄이 골라져 있음)");
    records.Close();
   }
   Open(new HotkeyDialog(Hotkeys.Defaults()),"hotkeys");
   var same=Hotkeys.Defaults();same[1]=same[1] with{Key=Keys.F8};
   Open(new HotkeyDialog(same),"hotkeys-same");
   // 다른 프로그램이 쓰는 조합: 적용을 누르면 첫 동작이 거절된 것으로 답하게 해 본다
   var refused=new HotkeyDialog(Hotkeys.Defaults(),_=>new HotkeyTaken(0,"등록: 다른 프로그램과 충돌합니다."));
   refused.Shown+=(_,_)=>((Button)refused.AcceptButton!).PerformClick();
   Open(refused,"hotkeys-taken");
   lines.Add(SaveFailure(main));
   // 앱 표시 크기: 선택 상자는 언제나 보이고 다섯 단계가 있다. 처음 정해진 숫자가 설정 파일에 적혀 있다. 다른 것을 고르면 바로 적히고 재실행 안내가 뜨며, 원래대로 돌리면 안내가 사라진다
   {
    var choice=Part<ThemedCombo>("scaleChoice");var sizeNote=Part<Label>("scaleNote");int started=Array.IndexOf(Theme.Steps,Theme.Chosen),other=started==0?1:0;var kept=UserOptions.Load(options);
    bool listed=choice.Items.Count==5&&choice.SelectedIndex==started&&choice.Visible,saved=kept.Scale==Theme.Chosen&&kept.Small==(Theme.Chosen==100),told=sizeNote.Text==MainForm.ScaleNote(Theme.Chosen,Theme.Chosen,Theme.Shown);
    choice.SelectedIndex=other;var changed=UserOptions.Load(options);bool asked=sizeNote.Text=="다시 실행하면 적용됩니다"&&changed.Scale==Theme.Steps[other]&&changed.Small==(Theme.Steps[other]==100);
    Shot(main,"main-scale");
    choice.SelectedIndex=started;bool back=sizeNote.Text==MainForm.ScaleNote(Theme.Chosen,Theme.Chosen,Theme.Shown)&&UserOptions.Load(options).Scale==Theme.Chosen;
    lines.Add($"main-scale: {(listed&&saved&&told&&asked&&back?"맞음":"틀림")}(선택 상자에 다섯 단계가 있고 처음 정해진 {Theme.Chosen}% 가 골라져 있음 {listed}, 그 숫자가 설정 파일에 적혀 있음 {saved}, 옆의 글이 규칙대로 '{MainForm.ScaleNote(Theme.Chosen,Theme.Chosen,Theme.Shown)}' {told}, 다른 것을 고르면 적히고 재실행 안내가 뜸 {asked}, 원래대로 돌리면 돌아옴 {back}. 지금 그리는 단계 {Theme.Shown}%)");
   }
   // 상태 줄의 조작 오류: 떠 있는 동안에는 감지 상태가 덮지 않고, 시간이 지나면 감지 상태가 보이고, 사용자가 한 다른 일의 글은 바로 보인다
   {
    var status=Part<Label>("status");void Call(string name,string text)=>typeof(MainForm).GetMethod(name,hidden)!.Invoke(main,new object[]{text});
    const string error="Chrome을 찾을 수 없습니다. Chrome을 설치하거나 기본 브라우저를 바꾼 뒤 다시 눌러 주세요.";
    Call("Alert",error);Call("Detected","Steam 곡 정보를 기다리고 있습니다.");bool held=status.Text==error;
    Shot(main,"main-alert");
    typeof(MainForm).GetField("alertUntil",hidden)!.SetValue(main,0L);Call("Detected","Steam 곡 정보를 기다리고 있습니다.");bool released=status.Text=="Steam 곡 정보를 기다리고 있습니다.";
    Call("Alert",error);status.Text="등록 취소";Call("Detected","다음 곡을 기다리고 있습니다.");bool replaced=status.Text=="다음 곡을 기다리고 있습니다.";
    status.Text="저장 확인 대기";Call("Alert",error);typeof(MainForm).GetMethod("AlertExpired",hidden)!.Invoke(main,null);bool restored=status.Text=="저장 확인 대기";
    double seconds=(double)typeof(MainForm).GetMethod("AlertSeconds",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,new object[]{error})!;
    // 조작 오류가 떠 있을 때 감지를 중지하면: 상태 줄이 곧바로 '감지 중지'이고, 오류의 시간이 지나도 오류 전의 글로 돌아가지 않는다
    Button? Named(Control parent,string text){foreach(Control c in parent.Controls){if(c is Button b&&b.Text==text)return b;if(Named(c,text) is Button inner)return inner;}return null;}
    status.Text="저장 확인 대기";Call("Alert",error);Named(main,"중지 / 숨기기")!.PerformClick();bool stopped=status.Text=="감지 중지";
    typeof(MainForm).GetMethod("AlertExpired",hidden)!.Invoke(main,null);bool stayed=status.Text=="감지 중지";
    // 저장 확인을 기다리는 동안 같은 자리에서 새 판이 열리면: 0.1초 주기 한 번에 첫 SCORE 테두리 연출이 켜지고, 다음 주기에 다시 켜지지 않는다
    bool traced,once;
    {
     var session=Part<Session>("session");var link=Part<ScoreLink>("scoreLink");var ticks=Part<System.Windows.Forms.Timer>("timer");
     var song=catalog.Songs.First(s=>s.Name=="Sweet On You");
     void Tick()=>typeof(System.Windows.Forms.Timer).GetMethod("OnTick",hidden)!.Invoke(ticks,new object[]{EventArgs.Empty});
     object? Run()=>typeof(MainForm).GetField("scoreTrace",hidden)!.GetValue(main);
     typeof(ScoreLink).GetProperty("Location")!.SetValue(link,new LocationUpdate(0,LocationState.Locked,0,0,null,null,0));
     session.Running=true;session.Begin(Part<RecordStore>("store").Active.Id,measuredAccuracy:99.5m,chart:new Selection(song.Id,8,"SC","노말",0));
     typeof(MainForm).GetField("tracedSerial",hidden)!.SetValue(main,link.PlaySerial);
     typeof(ScoreLink).GetProperty("PlaySerial")!.SetValue(link,link.PlaySerial+1);
     var before=Run();Tick();var first=Run();traced=Part<int>("tracedSerial")==link.PlaySerial&&session.Pending!=null&&(Motion.Reduced||first!=null&&!ReferenceEquals(before,first));
     Tick();once=ReferenceEquals(first,Run());
     session.Cancel();session.Running=false;typeof(ScoreLink).GetProperty("Location")!.SetValue(link,null);typeof(MainForm).GetMethod("StopMotion",hidden)!.Invoke(main,null);
    }
    lines.Add($"status-stop: {(stopped&&stayed?"맞음":"틀림")}(조작 오류가 떠 있을 때 중지하면 곧바로 '감지 중지' {stopped}, 오류의 시간이 지난 뒤에도 '감지 중지' {stayed})");
    lines.Add($"pending-trace: {(traced&&once?"맞음":"틀림")}(저장 확인 대기 중 새 판의 첫 SCORE 테두리 연출이 주기 한 번에 켜짐 {traced}, 다음 주기에 다시 켜지지 않음 {once})");
    lines[^3]=$"status-alert: {(held&&released&&replaced&&restored?"맞음":"틀림")}(떠 있는 동안 유지 {held}, 시간이 지나면 감지 상태 {released}, 다른 글이 적힌 뒤에는 막지 않음 {replaced}, 감지가 쓰지 않을 때는 시간이 지나면 그 전의 글 {restored}. 이 글 {error.Length}자는 {seconds:F1}초)";
   }
  }
  lines.Add(RealRefusal());
  // 크게 쓰던 기록 목록 창: 기억해 둔 크기(작업 영역만큼)가 단계만큼 커져도 화면의 작업 영역 안에 뜨는가
  {
   string folder=Path.Combine(AppContext.BaseDirectory,"frame-check-records-window");Directory.CreateDirectory(folder);foreach(string old in Directory.GetFiles(folder))File.Delete(old);
   var area=Screen.PrimaryScreen!.WorkingArea;string view=Path.Combine(folder,"view.json");
   new RecordsViewSettings(){Width=area.Width-200,Height=area.Height-150}.Save(view);
   using var owner=new Form(){StartPosition=FormStartPosition.Manual,Location=new Point(80,60),Size=new Size(400,300),ShowInTaskbar=false};owner.Show();
   using var records=new RecordsDialog(catalog,new RecordStore(Path.Combine(folder,"records.json")),view);
   records.Show(owner);Application.DoEvents();
   var shown=Screen.FromControl(records).WorkingArea;bool inside=shown.Contains(records.Bounds);
   lines.Add($"records-fit: {(inside?"맞음":"틀림")}(단계 {Theme.Scale}, 기억한 크기 {area.Width-200}×{area.Height-150}, 뜬 창 {records.Bounds}, 작업 영역 {shown})");
   records.Close();owner.Close();
  }
  // 오버레이 디자인의 적용: 설정 파일을 쓰지 못하면 실행 중인 색도 바뀌지 않고(오버레이에 가는 값이 그대로), 쓸 수 있게 한 뒤에는 파일과 실행 중인 값이 함께 바뀐다
  {
   string file=Path.Combine(AppContext.BaseDirectory,"frame-check-appearance.json");if(File.Exists(file)){File.SetAttributes(file,FileAttributes.Normal);File.Delete(file);}
   File.WriteAllText(file,"{\"Above\":\"#112233\",\"Font\":\"궁서\"}");File.SetAttributes(file,FileAttributes.ReadOnly);
   using var main=new MainForm(catalog,18776,false,options);var hidden=BindingFlags.Instance|BindingFlags.NonPublic;
   string Now()=>((System.Text.Json.JsonElement)typeof(MainForm).GetField("appearance",hidden)!.GetValue(main)!).GetProperty("Above").GetString()!;
   string before=Now();var chosen=System.Text.Json.JsonSerializer.SerializeToElement(new{Above="#0A141E",Below="#FF707B",Text="#DCE6F0",Background="#10141E",TextTransparency=0,BackgroundTransparency=5});
   Exception? error=null;try{typeof(MainForm).GetMethod("ApplyAppearance",hidden)!.Invoke(main,new object[]{chosen,file});}catch(TargetInvocationException e){error=e.InnerException;}
   bool kept=error!=null&&Now()==before&&File.ReadAllText(file).Contains("#112233");
   File.SetAttributes(file,FileAttributes.Normal);typeof(MainForm).GetMethod("ApplyAppearance",hidden)!.Invoke(main,new object[]{chosen,file});
   bool applied;using(var written=System.Text.Json.JsonDocument.Parse(File.ReadAllText(file)))applied=Now()=="#0A141E"&&written.RootElement.GetProperty("Above").GetString()=="#0A141E"&&written.RootElement.GetProperty("Font").GetString()=="궁서";
   File.Delete(file);
   lines.Add($"appearance-save-failure: {(kept&&applied?"맞음":"틀림")}(쓰지 못할 때 실행 중인 색과 파일이 그대로 {kept}, 쓸 수 있게 한 뒤에는 둘 다 바뀌고 예전 글꼴 값이 남음 {applied}. 알림: {error?.Message})");
  }
  // 오버레이 디자인 설정 창: 스크롤 없이 화면 안에 뜨는가, 상태를 고르면 미리보기의 연출이 돌고 끝나면 멈추는가. 연출 도중의 실제 화면도 남긴다
  {
   using var owner=new Form(){StartPosition=FormStartPosition.Manual,Location=new Point(80,60),Size=new Size(400,300),ShowInTaskbar=false};owner.Show();
   using var style=new StyleDialog(BadgeDesign.Defaults,true);
   style.Show(owner);Shot(style,"style");
   static IEnumerable<Control> All(Control top){foreach(Control child in top.Controls){yield return child;foreach(var inner in All(child))yield return inner;}}
   var preview=All(style).OfType<StylePreview>().Single();var state=All(style).OfType<ComboBox>().Single(c=>c.Items.Contains("최고 기록 경신"));
   var area=Screen.FromControl(style).WorkingArea;bool fits=area.Contains(style.Bounds)&&!style.AutoScroll&&!All(style).OfType<ScrollableControl>().Any(c=>c.AutoScroll);
   void Wait(int ms){var until=DateTime.UtcNow.AddMilliseconds(ms);while(DateTime.UtcNow<until){Application.DoEvents();Thread.Sleep(5);}}
   void Grab(string name){DwmGetWindowAttribute(style.Handle,9,out var seen,Marshal.SizeOf<Rect>());var r=Rectangle.FromLTRB(seen.Left,seen.Top,seen.Right,seen.Bottom);using var shot=new Bitmap(r.Width,r.Height);using(var g=Graphics.FromImage(shot))g.CopyFromScreen(r.Location,Point.Empty,r.Size);shot.Save(Path.Combine(AppContext.BaseDirectory,$"frame-{name}-{mode}.png"),ImageFormat.Png);}
   bool before=preview.Playing;
   state.SelectedItem="등록 확인";bool started=preview.Playing&&preview.Mode==6;Wait(430);Grab("style-confirm-0.43s");Wait(1200);bool ended=!preview.Playing;
   state.SelectedItem="최고 기록 경신";Wait(330);Grab("style-best-0.33s");Wait(1700);Grab("style-best-2.0s");bool running=preview.Playing;
   state.SelectedItem="점수 확인 중";Wait(450);Grab("style-check-0.45s");
   state.SelectedItem="달성";Wait(330);Grab("style-above-0.33s");
   lines.Add($"style-fit: {(fits?"맞음":"틀림")}(단계 {Theme.Scale}, 뜬 창 {style.Bounds}, 작업 영역 {area}, 스크롤 없음)");
   lines.Add($"style-replay: {(!before&&started&&ended&&running?"맞음":"틀림")}(처음에는 멈춘 모습 {!before}, 상태를 고르면 연출 시작 {started}, 다 돌면 멈춤 {ended}, 저장 결과는 2초 뒤에도 도는 중 {running})");
   style.Close();owner.Close();
  }
  // 투컴의 연결 선택 창과 '새로 만들기' 확인 창: IP 는 가려져 뜨고 'IP 주소 보기'를 켠 동안에만 보인다, 고르기 전에는 '적용'을 쓸 수 없다,
  // 공인 주소인 연결을 고르면 안내가 보인다, 연결이 없으면 빈 목록의 안내가 보이고 연결이 생기면 열려 있는 목록에 나타난다
  {
   using var owner=new Form(){StartPosition=FormStartPosition.Manual,Location=new Point(80,60),Size=new Size(400,300),ShowInTaskbar=false};owner.Show();
   static IEnumerable<Control> All(Control top){foreach(Control child in top.Controls){yield return child;foreach(var inner in All(child))yield return inner;}}
   var links=new List<NetLink>{new("A","이더넷",System.Net.IPAddress.Parse("192.168.0.24"),24,false),new("B","Wi-Fi",System.Net.IPAddress.Parse("192.168.10.32"),24,false),new("P","이더넷 2",System.Net.IPAddress.Parse("203.0.113.7"),24,true)};
   void Wait(int ms){var until=DateTime.UtcNow.AddMilliseconds(ms);while(DateTime.UtcNow<until){Application.DoEvents();Thread.Sleep(5);}}
   Control[] Rows(Form f)=>All(f).Where(c=>c.GetType().Name=="LinkRow").ToArray();
   void Click(Control c)=>typeof(Control).GetMethod("OnClick",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(c,new object[]{EventArgs.Empty});
   Button Named(Form f,string text)=>All(f).OfType<Button>().Single(b=>b.Text==text);
   Label Saying(Form f,string start)=>All(f).OfType<Label>().Single(l=>l.Text.StartsWith(start));
   // 처음 고를 때(고른 것 없음)
   using(var first=new SenderLinkDialog(()=>links.ToList(),null,()=>false))
   {
    first.Show(owner);Shot(first,"sender-links-first");
    bool blank=!Named(first,"적용").Enabled&&first.Chosen==null&&!Named(first,"주소 새로 만들기…").Enabled&&!Saying(first,"이 연결은 공인 IP").Visible&&Rows(first).Length==3;
    Click(Rows(first)[1]);Wait(100);bool picked=Named(first,"적용").Enabled&&first.Chosen?.Id=="B"&&!Saying(first,"이 연결은 공인 IP").Visible;
    Click(Rows(first)[2]);Wait(100);bool warned=first.Chosen?.Id=="P"&&Saying(first,"이 연결은 공인 IP").Visible;Shot(first,"sender-links-public");
    All(first).OfType<CheckBox>().Single().Checked=true;Wait(100);Shot(first,"sender-links-reveal");
    lines.Add($"sender-dialog: {(blank&&picked&&warned?"맞음":"틀림")}(처음에는 고른 것이 없고 적용·새로 만들기를 쓸 수 없음 {blank}, 줄을 누르면 적용할 수 있음 {picked}, 공인 주소인 연결을 고르면 안내가 보임 {warned})");
    first.Close();
   }
   // 기억한 연결로 다시 열기, 연결이 없을 때, 연결이 생겼을 때
   bool renewable=true;
   using(var again=new SenderLinkDialog(()=>links.ToList(),"A",()=>renewable))
   {
    again.Show(owner);Wait(200);bool remembered=again.Chosen?.Id=="A"&&Named(again,"적용").Enabled&&Named(again,"주소 새로 만들기…").Enabled&&!All(again).OfType<CheckBox>().Single().Checked;
    // 창이 열려 있는 사이에 복사할 주소가 없어지면(연결이 사라짐, 포트를 열지 못함) '새로 만들기'가 꺼지고, 돌아오면 다시 켜진다
    renewable=false;Wait(1300);bool renewOff=!Named(again,"주소 새로 만들기…").Enabled;renewable=true;Wait(1300);bool renewOn=Named(again,"주소 새로 만들기…").Enabled;
    Shot(again,"sender-links-again");
    var kept=links.ToList();links.Clear();Wait(1300);
    bool emptied=Rows(again).Length==0&&Saying(again,"사용할 수 있는 연결이 없습니다").Visible&&!Named(again,"적용").Enabled;Shot(again,"sender-links-empty");
    links.AddRange(kept.Take(1));Wait(1300);bool returned=Rows(again).Length==1&&!Saying(again,"사용할 수 있는 연결이 없습니다").Visible&&again.Chosen?.Id=="A"&&Named(again,"적용").Enabled;
    lines.Add($"sender-dialog-live: {(remembered&&emptied&&returned&&renewOff&&renewOn?"맞음":"틀림")}(다시 열면 기억한 줄이 골라져 있고 IP 는 가려져 있음 {remembered}, 연결이 없어지면 안내가 보이고 적용할 수 없음 {emptied}, 연결이 생기면 열려 있는 목록에 나타남 {returned}, 복사할 주소가 없어지면 '새로 만들기'가 꺼짐 {renewOff}, 돌아오면 켜짐 {renewOn})");
    again.Close();
   }
   using(var renew=new RenewDialog()){renew.Show(owner);Shot(renew,"sender-renew");lines.Add($"sender-renew: {(renew.AcceptButton is Button {Text:"취소"}&&renew.CancelButton is Button {Text:"취소"}?"맞음":"틀림")}(처음 선택과 Esc 가 취소)");renew.Close();}
   owner.Close();
  }
  // 투컴의 주 화면 줄: 허용을 켜면 송출 주소 줄이 생기고, 주소와 키는 화면 어디에도 보이지 않으며, 상태와 안내가 차례대로 바뀐다.
  // 연결 목록과 방화벽 읽기는 꾸민 것으로 바꿔 끼운다(송출용 서버는 이 PC 안의 루프백 주소에 열린다. Windows 방화벽 창이 뜨지 않는다)
  {
   var realLinks=NetLinks.Source;var realReader=SenderFirewall.Reader;
   var links=new List<NetLink>{new("{11111111-1111-1111-1111-111111111111}","이더넷",System.Net.IPAddress.Parse("127.0.0.2"),8,false)};bool? blockedNow=null;int reads=0;
   NetLinks.Source=()=>links.ToList();SenderFirewall.Reader=(_,_)=>{Interlocked.Increment(ref reads);return blockedNow;};
   string senderOptions=Path.Combine(AppContext.BaseDirectory,"frame-check-sender-preferences.json");File.Delete(senderOptions);new UserOptions(false,false,Theme.Dark).Save(senderOptions);
   try
   {
    using var main=new MainForm(catalog,18777,false,senderOptions);main.StartPosition=FormStartPosition.Manual;main.Location=new Point(60,40);main.Show();
    var hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    T Part<T>(string name)=>(T)typeof(MainForm).GetField(name,hidden)!.GetValue(main)!;
    static IEnumerable<Control> All(Control top){foreach(Control child in top.Controls){yield return child;foreach(var inner in All(child))yield return inner;}}
    bool Until(Func<bool> done,int ms){var until=DateTime.UtcNow.AddMilliseconds(ms);while(DateTime.UtcNow<until){Application.DoEvents();if(done())return true;Thread.Sleep(10);}return done();}
    var allow=Part<CheckBox>("senderAllow");var row=Part<Control>("senderRow");var status=Part<Label>("senderStatus");var address=Part<InputBox>("senderAddress");var copy=Part<Button>("senderCopy");var pick=Part<Button>("senderPick");
    var changedNote=Part<Label>("noteChanged");var blockedNote=Part<Label>("noteBlocked");var link=Part<SenderLink>("sender");
    int Ask(System.Net.IPAddress to){try{using var client=new System.Net.Sockets.TcpClient();client.Connect(to,18777);var stream=client.GetStream();stream.ReadTimeout=3000;stream.Write(System.Text.Encoding.ASCII.GetBytes($"GET /o/{link.Settings.Key}/state HTTP/1.1\r\n\r\n"));using var reader=new StreamReader(stream);string? first=reader.ReadLine();return first==null?0:int.Parse(first.Split(' ')[1]);}catch(Exception e)when(e is IOException or System.Net.Sockets.SocketException){return 0;}}
    bool off=!row.Visible&&!pick.Visible&&!allow.Checked;
    allow.Checked=true;
    bool waiting=Until(()=>status.Text=="연결 대기",3000)&&row.Visible&&pick.Visible&&address.Box.Text=="주소 가려짐"&&copy.Enabled;
    // 주소와 키는 화면의 어떤 글에도 없다. 설정 파일에는 IP 원문이 없다(연결 키는 다음 실행에 같은 주소를 쓰려고 설정 파일에 보관한다)
    string key=link.Settings.Key!;bool unseen=!All(main).Any(c=>c.Text.Contains(key)||c.Text.Contains("127.0.0.2"))&&File.ReadAllText(senderOptions) is var saved&&saved.Contains("\"Allowed\":true")&&!saved.Contains("127.0.0");
    Shot(main,"main-sender-waiting");
    // 방화벽을 처음부터 읽지 못하는 동안('모름'): 0.5초마다 다시 읽지 않는다. 켠 때 한 번과 그 뒤 5초마다(6.5초 동안 두세 번. 0.5초마다 읽으면 그보다 훨씬 많다)
    {var until=DateTime.UtcNow.AddMilliseconds(6500);while(DateTime.UtcNow<until){Application.DoEvents();Thread.Sleep(20);}}
    int unknownReads=Volatile.Read(ref reads);bool paced=unknownReads>=2&&unknownReads<=3&&status.Text=="연결 대기"&&!blockedNote.Visible;blockedNow=false;
    bool connected=Ask(links[0].Address)==200&&Until(()=>status.Text=="연결됨",3000);
    // 주소가 바뀜 → 방화벽 차단이 겹침 → 차단이 풀림 → 새 주소로 연결됨
    links[0]=links[0] with{Address=System.Net.IPAddress.Parse("127.0.0.3")};
    bool changed=Until(()=>status.Text=="주소 변경"&&changedNote.Visible,5000)&&!blockedNote.Visible&&address.Box.Text=="주소 가려짐";
    Shot(main,"main-sender-changed");
    blockedNow=true;bool both=Until(()=>status.Text=="방화벽 차단"&&blockedNote.Visible&&changedNote.Visible,9000)&&copy.Enabled;
    Shot(main,"main-sender-blocked");
    // 투컴 안내 둘에 진단 그림 설명까지 펴면 왼쪽 칸의 가운데만 굴린다. 첫 제목과 '앱 표시 크기' 줄은 칸 안에 그대로 보이고,
    // Tab 으로 가려진 줄에 가면 그 줄이 보이게 굴린다
    bool pinned,followed,hiddenFirst,eased;
    {
     typeof(MainForm).GetMethod("ShowImagesHelp",hidden)!.Invoke(main,new object[]{true});Until(()=>main.LeftScrolled,2000);
     var leftColumn=Part<FlowLayoutPanel>("leftColumn");var scaleRow=Part<Control>("scaleRow");var leftScroll=Part<VerticalScroller>("leftScroll");var scaleBox=Part<ThemedCombo>("scaleChoice");
     var columnView=leftColumn.RectangleToScreen(leftColumn.ClientRectangle);
     var firstHead=leftColumn.Controls.OfType<SectionHeader>().First();
     pinned=main.LeftScrolled&&columnView.Contains(scaleBox.RectangleToScreen(scaleBox.ClientRectangle))&&columnView.Contains(scaleRow.RectangleToScreen(scaleRow.ClientRectangle))&&firstHead.Text=="감지 설정"&&columnView.Contains(firstHead.RectangleToScreen(firstHead.ClientRectangle));
     var top=Part<Button>("refreshButton");Rectangle View()=>leftScroll.RectangleToScreen(leftScroll.ClientRectangle);Rectangle Of(Control c)=>c.RectangleToScreen(c.ClientRectangle);
     leftScroll.AutoScrollPosition=new Point(0,100000);Application.DoEvents();hiddenFirst=!View().Contains(Of(top));
     main.Activate();Application.DoEvents();top.Select();top.Focus();Until(()=>View().Contains(Of(top)),1500);followed=View().Contains(Of(top));
     Shot(main,"main-sender-help");
     typeof(MainForm).GetMethod("ShowImagesHelp",hidden)!.Invoke(main,new object[]{false});Application.DoEvents();
     eased=!main.ImagesHelpShown&&columnView.Contains(scaleRow.RectangleToScreen(scaleRow.ClientRectangle));
    }
    lines.Add($"main-left-scroll: {(pinned&&hiddenFirst&&followed&&eased?"맞음":"틀림")}(투컴 안내 둘과 진단 그림 설명을 함께 펴면 가운데만 굴리고 첫 제목과 '앱 표시 크기' 줄은 칸 안에 보임 {pinned}, 끝까지 굴리면 맨 위 줄이 가려짐 {hiddenFirst}, Tab 으로 그 줄의 '목록 갱신'에 가면 보이게 굴림 {followed}, 설명을 접어도 '앱 표시 크기' 줄이 칸 안에 있음 {eased})");
    blockedNow=false;bool freed=Until(()=>status.Text=="주소 변경"&&!blockedNote.Visible&&changedNote.Visible,9000);
    bool settled=Ask(links[0].Address)==200&&Until(()=>status.Text=="연결됨"&&!changedNote.Visible,4000);
    // 이번에는 주소가 먼저 연결된다: 주소 변경 안내만 사라지고, 확인해 둔 방화벽 안내는 남는다. 방화벽을 읽지 못한 동안에도 남고, 풀린 것을 읽어야 사라진다
    links[0]=links[0] with{Address=System.Net.IPAddress.Parse("127.0.0.4")};blockedNow=true;
    bool both2=Until(()=>status.Text=="방화벽 차단"&&blockedNote.Visible&&changedNote.Visible,12000);
    bool keptBlock=Ask(links[0].Address)==200&&Until(()=>!changedNote.Visible,4000)&&status.Text=="방화벽 차단"&&blockedNote.Visible;
    blockedNow=null;var hold=DateTime.UtcNow.AddMilliseconds(6500);bool unknownKept=true;while(DateTime.UtcNow<hold){Application.DoEvents();Thread.Sleep(20);if(!blockedNote.Visible||status.Text!="방화벽 차단")unknownKept=false;}
    // 몇 분 켜 둔 뒤(경과 횟수 600 = 5분. 방금 읽었다) 차단을 풀고 같은 연결을 다시 적용한다: 곧바로 다시 읽어 안내가 사라진다(경과 횟수를 0 으로 돌리면 켜 둔 5분만큼 읽지 않는다)
    typeof(MainForm).GetField("senderTicks",hidden)!.SetValue(main,600);typeof(MainForm).GetField("firewallAsked",hidden)!.SetValue(main,600);
    blockedNow=false;typeof(MainForm).GetMethod("ApplySenderLink",hidden)!.Invoke(main,new object[]{link.Link!});
    bool reapplied=Until(()=>!blockedNote.Visible,2000);
    blockedNow=false;bool released=Until(()=>!blockedNote.Visible,9000)&&Ask(links[0].Address)==200&&Until(()=>status.Text=="연결됨",3000);
    // 고른 연결이 사라짐 → 돌아옴
    var keptLink=links[0];links.Clear();bool gone=Until(()=>status.Text=="연결 없음"||status.Text=="선택한 연결 없음",5000)&&status.Text=="선택한 연결 없음"&&address.Box.Text=="—"&&!copy.Enabled;
    links.Add(keptLink);bool back=Until(()=>status.Text=="연결 대기"&&copy.Enabled,5000);
    // 설정 파일을 쓸 수 없게 해 두고 허용을 끈다: 송출은 곧바로 멈추고(송출용 서버가 닫힌다) 저장하지 못했다는 것이 상태 줄에 뜬다
    File.SetAttributes(senderOptions,FileAttributes.ReadOnly);
    allow.Checked=false;bool closed=Until(()=>!row.Visible,2000)&&!pick.Visible&&Ask(keptLink.Address)==0;
    bool told=Part<Label>("status").Text==MainForm.StoppedUnsavedText;File.SetAttributes(senderOptions,FileAttributes.Normal);
    // 앱 표시 크기를 저장하지 못하면 선택과 옆의 글이 마지막으로 저장된 값으로 돌아가고, 상태 줄에 저장하지 못한 것이 뜬다
    bool scaleKept;
    {
     var scaleBox=Part<ThemedCombo>("scaleChoice");var note=Part<Label>("scaleNote");int before=scaleBox.SelectedIndex;string kept=File.ReadAllText(senderOptions);
     File.SetAttributes(senderOptions,FileAttributes.ReadOnly);
     try{scaleBox.SelectedIndex=(before+1)%Theme.Steps.Length;Application.DoEvents();}
     finally{File.SetAttributes(senderOptions,FileAttributes.Normal);}
     scaleKept=scaleBox.SelectedIndex==before&&note.Text==MainForm.ScaleNote(Theme.Steps[before],Theme.Chosen,Theme.Shown)&&File.ReadAllText(senderOptions)==kept&&Part<Label>("status").Text.Length>0;
    }
    // '게임 화면을 확인할 수 없습니다'는 감지가 이어져도 정한 시간 동안 남고, 같은 오류가 다시 와도 시간을 처음부터 세지 않으며, 시간이 지나면 그동안의 가장 새 감지 상태를 보인다
    bool held;
    {
     var statusLine=Part<Label>("status");void Call(string name,params object[] args)=>typeof(MainForm).GetMethod(name,hidden)!.Invoke(main,args);
     Call("Hold",MainForm.NoGameScreen);long until=(long)typeof(MainForm).GetField("alertUntil",hidden)!.GetValue(main)!;
     Call("Detected","감지 상태 1");bool stays=statusLine.Text==MainForm.NoGameScreen;
     Thread.Sleep(30);Call("Hold",MainForm.NoGameScreen);bool same=((long)typeof(MainForm).GetField("alertUntil",hidden)!.GetValue(main)!)==until;
     Call("Detected","감지 상태 2");Call("AlertExpired");bool latest=statusLine.Text=="감지 상태 2";
     held=stays&&same&&latest;
    }
    lines.Add($"main-sender: {(off&&waiting&&unseen&&paced&&connected&&changed&&both&&freed&&settled&&both2&&keptBlock&&unknownKept&&reapplied&&released&&gone&&back&&closed&&told?"맞음":"틀림")}(꺼져 있으면 줄이 없음 {off}, 켜면 '주소 가려짐'·'연결 대기' {waiting}, 화면에는 IP·키가 보이지 않고 설정 파일에는 IP 원문이 없음 {unseen}, 방화벽을 처음부터 읽지 못해도 5초마다만 읽음 {paced}({unknownReads}번), 받아 가면 '연결됨' {connected}, 주소가 바뀌면 '주소 변경'과 안내 {changed}, 차단이 겹치면 '방화벽 차단'과 두 안내 {both}, 차단이 풀리면 '주소 변경'만 {freed}, 새 주소로 연결되면 안내가 사라짐 {settled}, 다시 겹침 {both2}, 주소가 먼저 연결되면 주소 변경 안내만 사라지고 방화벽 안내는 남음 {keptBlock}, 방화벽을 읽지 못하는 동안에도 남음 {unknownKept}, 5분 켜 둔 뒤 같은 연결을 다시 적용하면 2초 안에 다시 읽어 안내가 사라짐 {reapplied}, 풀린 것을 읽으면 사라짐 {released}, 고른 연결이 사라지면 '선택한 연결 없음'·'—' {gone}, 돌아오면 '연결 대기' {back}, 설정 파일을 쓸 수 없어도 끄면 줄이 사라지고 닿지 않음 {closed}, 송출을 중지했고 저장하지 못했다고 알림 {told})");
    lines.Add($"main-scale-unsaved: {(scaleKept?"맞음":"틀림")}(설정 파일을 쓸 수 없을 때 앱 표시 크기를 바꾸면 선택과 옆의 글이 저장된 값으로 돌아가고 파일은 그대로)");
    lines.Add($"main-hold: {(held?"맞음":"틀림")}(게임 화면을 확인할 수 없다는 안내는 감지 상태가 들어와도 남고, 같은 오류가 다시 와도 시간을 다시 세지 않으며, 시간이 지나면 가장 새 감지 상태를 보임)");
    main.Close();
   }
   finally{NetLinks.Source=realLinks;SenderFirewall.Reader=realReader;if(File.Exists(senderOptions)){File.SetAttributes(senderOptions,FileAttributes.Normal);File.Delete(senderOptions);}}
   using var owner=new Form(){StartPosition=FormStartPosition.Manual,Location=new Point(80,60),Size=new Size(400,300),ShowInTaskbar=false};owner.Show();
   using(var help=new SenderHelpDialog(false,18765)){help.Show(owner);Shot(help,"sender-help");help.Close();}
   using(var help=new SenderHelpDialog(true,18765)){help.Show(owner);Shot(help,"sender-help-port");help.Close();}
   owner.Close();
  }
  File.WriteAllLines(Path.Combine(AppContext.BaseDirectory,"frame-check.txt"),lines);
 }
}
