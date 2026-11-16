using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using DjmaxScore;
namespace LadderRecords;
[System.Runtime.Versioning.SupportedOSPlatform("windows10.0.19041")]
static class UiChecks
{
 [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")]static extern IntPtr GetActiveWindow();
 [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr hwnd,uint message,IntPtr wParam,IntPtr lParam);
 public static void Run(Catalog catalog,string? previewPath=null){
  using(var migration=new Hotkeys(IntPtr.Zero,false)){var old=Hotkeys.Defaults().Take(4).ToList();migration.Set(old);if(migration.Items.Count!=5||!migration.Items.Take(4).SequenceEqual(old))throw new Exception("Four-key migration");migration.Set(new List<Hotkey>{new("곡 수동 확정",Keys.F6,2),new("곡 고정 해제",Keys.F7,2)}.Concat(old).ToList());if(migration.Items.Count!=5||migration.Items[4].Name!="감지 시작")throw new Exception("Six-key migration");try{migration.Set(Enumerable.Repeat(old[0],5).ToList());throw new Exception("Duplicate accepted");}catch(Exception e)when(e.Message.Contains("중복")){} }
  var initial=JsonSerializer.SerializeToElement(new {Above="#B3FF68",Below="#FF5B69",Background="#10141E",Text="#DCE6F0",Font="맑은 고딕",HeaderSize=24,GoalSize=37,CurrentSize=21,TextTransparency=0,BackgroundTransparency=12});
  void Snapshot(Form form,string name){using(form){form.Show();Application.DoEvents();if(form is AppWindow&&SendMessage(form.Handle,0x007f,IntPtr.Zero,IntPtr.Zero)==IntPtr.Zero)throw new Exception($"Caption icon missing: {form.Text}");using var bitmap=new Bitmap(form.Width,form.Height);form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(AppContext.BaseDirectory,name+".png"));form.Close();}}
  Snapshot(new HotkeyDialog(Hotkeys.Defaults()),"hotkeys-preview");
  RecordsChecks.Run(catalog);RecordsSettingsChecks.Run(catalog);
  ManualFeatureChecks.Run(catalog);
  // 글자 크기 슬라이더: 저장되는 크기 값(이름이 Size 로 끝나는 것)마다 하나씩 있고 범위가 12~64 여야 한다. 몇 개인지와 놓인 차례는 정해 두지 않는다.
  // 슬라이더를 하나씩 움직여 어느 값이 따라 바뀌는지로 짝을 찾는다(정확히 한 값이 슬라이더의 값으로 바뀌어야 하고, 두 슬라이더가 같은 값을 바꾸면 안 된다).
  var design=new StyleDialog(initial,catalog,true);var sliders=design.Controls.OfType<FlowLayoutPanel>().SelectMany(p=>p.Controls.OfType<FlowLayoutPanel>()).SelectMany(p=>p.Controls.OfType<Panel>()).SelectMany(p=>p.Controls.OfType<TrackBar>()).ToArray();
  string[] sizes=design.Value.EnumerateObject().Where(p=>p.Name.EndsWith("Size")).Select(p=>p.Name).ToArray();
  if(sizes.Length==0||sliders.Length!=sizes.Length||sliders.Any(s=>s.Minimum!=12||s.Maximum!=64))throw new Exception("Font sliders missing or range incorrect");
  var sliderOf=new Dictionary<string,TrackBar>();
  foreach(var slider in sliders){var before=design.Value;int old=slider.Value;slider.Value=old==40?41:40;var after=design.Value;var moved=sizes.Where(k=>before.GetProperty(k).GetInt32()!=after.GetProperty(k).GetInt32()).ToArray();if(moved.Length!=1||after.GetProperty(moved[0]).GetInt32()!=slider.Value||!sliderOf.TryAdd(moved[0],slider))throw new Exception("Font slider not tied to exactly one saved size");slider.Value=old;}
  sliderOf["BadgeSize"].Value=28;if(design.Value.GetProperty("BadgeSize").GetInt32()!=28)throw new Exception("Badge slider not reflected in saved appearance");sliderOf["BadgeSize"].Value=24;int baseHeight=OverlayLayout.Height(design.Value,true);sliderOf["EventSize"].Value=64;if(OverlayLayout.Height(design.Value,true)!=baseHeight)throw new Exception("Event size expanded overlay height");sliderOf["EventSize"].Value=12;if(OverlayLayout.Height(design.Value,true)!=baseHeight)throw new Exception("Event size shrank overlay height");sliderOf["EventSize"].Value=24;Snapshot(design,"style-preview");
  // 미리보기 고르기: 항목을 이름으로 고르면 미리보기가 그 상태로 바뀌어야 한다. 항목 이름은 지금 디자인 설정 창의 것이다(창에 없는 이름이면 여기서 실패한다).
  void PreviewMode(string item,string name){var dialog=new StyleDialog(initial,catalog,true);var mode=dialog.Controls.OfType<FlowLayoutPanel>().SelectMany(p=>p.Controls.OfType<FlowLayoutPanel>()).SelectMany(p=>p.Controls.OfType<ComboBox>()).Single(c=>c.Items.Contains(item));mode.SelectedItem=item;if((string?)mode.SelectedItem!=item||dialog.Controls.OfType<FlowLayoutPanel>().SelectMany(p=>p.Controls.OfType<StylePreview>()).Single().Mode!=mode.SelectedIndex)throw new Exception("Preview mode not applied: "+item);Snapshot(dialog,name);}
  PreviewMode("등록 확인","pending-style-preview");PreviewMode("첫 기록 등록","first-record-style-preview");
  // 그림은 앱이 실제로 쓰는 모양(ConfirmView)의 확인 창으로 찍는다.
  Snapshot(new Confirmation(new ConfirmView("La Campanella : Nu Rave","4B","SC","하드판정","99.48%","994,812","99.25%","992,507","good")),"confirmation-preview");
  Confirmations(catalog);Notices();
  string optionFile=Path.Combine(AppContext.BaseDirectory,"test-data",Guid.NewGuid()+".preferences.json");Directory.CreateDirectory(Path.GetDirectoryName(optionFile)!);
  using var app=new MainForm(catalog,18766,false,optionFile);app.Show();Application.DoEvents();RescanChecks.Run(app,catalog);
  if(previewPath!=null){using var frame=new Bitmap(previewPath);typeof(MainForm).GetMethod("ShowImage",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(app,new object[]{frame});Application.DoEvents();}
  using(var shot=new Bitmap(app.Width,app.Height)){app.DrawToBitmap(shot,new Rectangle(Point.Empty,shot.Size));shot.Save(Path.Combine(AppContext.BaseDirectory,"ui-preview.png"));}
  // 창 모드 등록: 다른 창이 앞에 있을 때 등록하면 확인 창이 하나 뜨고 후보가 유지되는가. 앞 창은 이 검사가 띄운 같은 프로세스의 창이다(다른 프로세스인 게임이 앞에 있는 경우가 아니다).
  // 확인 창은 개발용 ShowRegistration 으로 띄운다. 창을 만들어 띄우는 코드는 실제 등록 경로(ShowPendingConfirmation)와 같은 모양이고, 실제 경로와 그 창의 단추·닫기·갱신은 Confirmations 가 본다.
  using var game=new Form(){Text="Focus test",Width=320,Height=180};game.Show();game.Activate();Application.DoEvents();var foreground=GetForegroundWindow();var active=GetActiveWindow();int forms=Application.OpenForms.Count;
  var field=typeof(MainForm).GetField("session",BindingFlags.NonPublic|BindingFlags.Instance)!;var session=(Session)field.GetValue(app)!;
  var song=catalog.Songs.First(s=>s.Charts.Count>0);var chart=song.Charts[0];session.Observe(new(song.Id,chart.Button,chart.Pattern,"노말",1),0);session.Lock();session.Current=99.48m;session.Running=true;
  typeof(MainForm).GetMethod("ShowRegistration",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(app,new object[]{true});Application.DoEvents();
  var passive=Application.OpenForms.OfType<Confirmation>().FirstOrDefault(f=>f.Visible);
  if(Application.OpenForms.Count!=forms+1||passive==null||session.Pending?.Accuracy!=99.48m)throw new Exception("Windowed registration did not show exactly one confirmation window, or lost snapshot");
  IntPtr confirmHandle=passive.Handle,frontShown=GetForegroundWindow(),activeShown=GetActiveWindow();
  typeof(MainForm).GetMethod("Decide",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(app,new object[]{false});if(session.Pending!=null||session.Locked==null)throw new Exception("Cancel lost locked chart");
  Application.DoEvents();IntPtr frontCancelled=GetForegroundWindow(),activeCancelled=GetActiveWindow();
  // 포커스(확인 창이 뜨고 닫힐 때 앞에 있던 창이 그대로인가)는 통과 조건에서 떼어, 잰 값만 결과 파일에 적는다. 전에는 위의 조건과 한데 묶인 실패 조건이었다.
  // 지금은 확인 창이 뜨면서 이 프로세스의 활성 창이 된다(리뷰 20261115-전체변경-UI포함-종합리뷰 4번. 사용자가 수정을 보류했다). 고쳐지면 다시 실패 조건으로 돌린다.
  // 시스템의 앞 창은 이 프로세스가 앞에 있을 때만 시험 창이 된다(뒤에서 돌리면 Windows 가 앞으로 나오게 두지 않는다). 그때는 앞 창이 바뀌지 않아도 포커스를 지켰다는 뜻이 아니므로, 이 프로세스 안의 활성 창도 함께 잰다.
  string Who(IntPtr h)=>(h==IntPtr.Zero?"없음":h==game.Handle?"시험 창":h==confirmHandle?"확인 창":h==app.Handle?"주 화면":"다른 창")+$"({h})";
  bool activeKept=activeShown==active,frontKept=frontShown==foreground&&frontCancelled==foreground,frontOurs=foreground==game.Handle;
  string focus="포커스 보존(창 모드 등록 확인 창): "+(!activeKept||!frontKept?"확인되지 않음(확인 창이 뜨면서 앞에 있던 창이 바뀜)":frontOurs?"이번 실행에서는 앞에 있던 창이 유지됨":"확인되지 않음(시험 창이 시스템의 앞 창이 되지 못함)")+". 통과 조건이 아니라 잰 값이다. 리뷰 20261115 종합리뷰 4번, 사용자가 수정을 보류."
   +$"\n  이 프로세스의 활성 창: 뜨기 전 {Who(active)} → 확인 창이 뜬 뒤 {Who(activeShown)} → 취소한 뒤 {Who(activeCancelled)}. 유지됨: {(activeKept?"예":"아니오")}"
   +$"\n  시스템의 앞 창: 뜨기 전 {Who(foreground)} → 확인 창이 뜬 뒤 {Who(frontShown)} → 취소한 뒤 {Who(frontCancelled)}. 유지됨: {(frontKept?"예":"아니오")}. 시험 창이 앞 창이었는가: {(frontOurs?"예":"아니오(이 줄로는 같은 프로세스의 앞 창을 가져가는지 알 수 없다)")}";
  // 전체화면 사용: 창을 만들지 않으므로 앞 창이 바뀔 일이 없다. 이것은 실패 조건으로 둔다. 위에서 확인 창이 앞 창을 가져갔다가 닫혔을 수 있어 앞 창을 다시 잡아 둔다.
  game.Activate();Application.DoEvents();foreground=GetForegroundWindow();
  var fullscreen=(CheckBox)typeof(MainForm).GetField("fullscreen",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(app)!;fullscreen.Checked=true;if(!UserOptions.Load(optionFile).Fullscreen)throw new Exception("Fullscreen setting not saved");
  foreach(bool byHotkey in new[]{true,false}){session.Current=99.48m;typeof(MainForm).GetMethod("ShowRegistration",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(app,new object[]{byHotkey});Application.DoEvents();if(Application.OpenForms.Count!=forms||GetForegroundWindow()!=foreground||session.Pending?.Accuracy!=99.48m)throw new Exception("Fullscreen registration created a window, stole focus or lost snapshot");typeof(MainForm).GetMethod("Decide",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(app,new object[]{false});}
  using(var reopened=new MainForm(catalog,18767,false,optionFile)){var restored=(CheckBox)typeof(MainForm).GetField("fullscreen",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(reopened)!;if(!restored.Checked)throw new Exception("Fullscreen setting not restored");}
  var preview=(PictureBox)typeof(MainForm).GetField("picture",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(app)!;var root=(ScrollableControl)app.Controls[0];bool PreviewEnabled()=>(bool)typeof(MainForm).GetMethod("ShouldUpdatePreview",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(app,null)!;root.ScrollControlIntoView(preview);Application.DoEvents();if(!PreviewEnabled())throw new Exception("Visible preview suppressed");app.WindowState=FormWindowState.Minimized;if(PreviewEnabled())throw new Exception("Minimized preview still updating");app.WindowState=FormWindowState.Normal;app.Hide();if(PreviewEnabled())throw new Exception("Hidden preview still updating");app.Show();app.MinimumSize=Size.Empty;app.ClientSize=new(480,200);root.AutoScrollPosition=Point.Empty;Application.DoEvents();if(PreviewEnabled())throw new Exception("Offscreen preview still updating");app.ClientSize=new(720,900);root.ScrollControlIntoView(preview);Application.DoEvents();if(!PreviewEnabled())throw new Exception("Preview did not resume after scrolling into view");
  fullscreen.Checked=false;if(UserOptions.Load(optionFile).Fullscreen)throw new Exception("Unchecked setting not saved");
  // 여기까지 왔으면 위의 실패 조건은 모두 지나온 것이다. 실제로 본 것만 적는다(포커스는 통과로 적지 않고 잰 값을 따로 적는다).
  File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"ui-validation.txt"),
   "PASS: 단축키 목록 옮기기(4개·6개 → 5개, 중복 거절); 단축키·디자인·등록 확인 창의 그림과 창 아이콘; 디자인 설정(크기 값마다 12~64 슬라이더 하나, 문구 크기를 바꿔도 오버레이 높이가 그대로, 미리보기 \"등록 확인\"·\"첫 기록 등록\" 고르기); "
   +"창 모드 등록(확인 창이 하나 뜨고 후보 99.48%를 유지, 취소하면 곡 고정이 남음); 전체화면 사용(두 번의 등록에 창을 만들지 않고 앞 창과 후보가 그대로, 설정이 저장되고 새 주 화면에서 복원); 게임 화면 미리 보기의 갱신 판단(보임·최소화·숨김·화면 밖·다시 보임)."
   +"\n함께 돈 검사(결과는 각 파일): 기록 목록 records-validation.txt, 목록 설정 records-settings-validation.txt, 수동 기록 manual-validation.txt, 실제 등록 확인 창의 단추·닫기·갱신 confirmation-validation.txt, 새로 감지하기 rescan-validation.txt."
   +"\n보지 않은 것: 등록 키부터 SCORE 답까지, 전역 단축키, 실제 게임, 다른 프로세스가 앞에 있을 때의 포커스."
   +"\n"+focus+"\n");
 }

 // 실제 등록 확인 창의 검사 (리뷰 20261115-전체변경-UI포함-종합리뷰 6번). Run 이 부른다. 결과는 confirmation-validation.txt.
 //
 // "전체화면 사용"을 끈 주 화면에 SCORE 모듈의 답(Accepted)을 만들어 넣는다. 그러면 앱이 답을 받았을 때의 코드(OnScoreAnswer → session.Begin → ShowPendingConfirmation)가
 // 확인 창을 실제로 띄운다. 그 창의 "예"·"아니요" 단추와 창의 X(닫기)를 누르고, 확인을 기다리는 동안 기존 기록을 바꿔 열린 창의 글이 따라 바뀌는지 본다.
 // 만든 시험이다: 후보와 기록은 만든 것이고, 기록은 test-data 아래의 임시 파일에 쓴다(주 화면의 store 를 바꿔 끼운다). Steam 곡 정보는 "곡 없음"으로 바꿔 끼워 게임 화면을 캡처하지 않는다.
 // 등록 키부터 답까지(RequestRegistration, SCORE 판독), 전역 단축키, 실제 게임은 지나가지 않는다. 확인 창이 포커스를 가져가는지도 여기서 보지 않는다(Run 이 잰다).
 sealed class Quiet(Catalog catalog,int port,string preferences):MainForm(catalog,port,false,preferences){protected override SteamSnapshot ReadSteamSnapshot()=>new(true,null,"확인 창 검사");}
 static IEnumerable<Control> Inside(Control parent)=>parent.Controls.Cast<Control>().SelectMany(c=>new[]{c}.Concat(Inside(c)));
 // 프로그램이 그리는 알림 창(NoticeDialog): 긴 글이 정한 폭 안에서 줄을 바꾸고, "확인"·닫기로 닫히고, 라이트·다크 그림을 남긴다.
 // 글은 실제로 뜨는 것 가운데 가장 긴 것(본 파일 없이 백업과 임시 파일이 남았을 때의 시작 알림)을 저장 계층에서 받아 쓴다. 결과는 notice-validation.txt
 static void Notices()
 {
  var notes=new List<string>();void Check(bool ok,string what,object? got=null){if(!ok)throw new Exception($"{what} (본 것: {got})");notes.Add("PASS: "+what);}
  string folder=Path.Combine(AppContext.BaseDirectory,"test-data",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);string path=Path.Combine(folder,"records.json");
  File.WriteAllText(path+".bak","{}");File.WriteAllText(path+".tmp","{");
  string guide="";try{_=new RecordStore(path);}catch(InvalidDataException e){guide=e.Message;}
  Check(guide.Contains("records.json.bak")&&guide.Contains(folder),"시작 알림의 글을 저장 계층에서 받음",guide);
  bool dark=Theme.Dark;
  try{
   foreach(bool mode in new[]{false,true}){
    Theme.Dark=mode;string name=mode?"다크":"라이트";
    using var notice=new NoticeDialog("실행 오류",guide);notice.Show();Application.DoEvents();
    var label=notice.Controls.OfType<FlowLayoutPanel>().Single().Controls.OfType<Label>().Single();
    var ok=notice.Controls.OfType<FlowLayoutPanel>().Single().Controls.OfType<FlowLayoutPanel>().Single().Controls.OfType<Button>().Single();
    int line=TextRenderer.MeasureText("가",label.Font).Height;
    // 글이 한 줄로 늘어나지도, 폭 밖으로 잘리지도 않아야 한다: 폭이 정한 값 안이고, 높이는 그 폭에서 줄을 바꿔 잰 값과 같다
    var need=TextRenderer.MeasureText(guide,label.Font,new Size(520,int.MaxValue),TextFormatFlags.WordBreak|TextFormatFlags.TextBoxControl|TextFormatFlags.NoPrefix);
    Check(label.Width<=520&&label.Height>=line*8,$"{name}: 긴 글이 정한 폭(520) 안에서 여러 줄로 놓임",$"{label.Width}x{label.Height}, 한 줄 {line}");
    Check(need.Width<=520&&label.Height>=need.Height-2,$"{name}: 글이 잘리지 않음(줄을 바꿔 잰 크기 이상)",$"글 칸 {label.Width}x{label.Height}, 필요한 크기 {need.Width}x{need.Height}");
    Check(label.Bottom<=ok.Parent!.Top&&notice.ClientSize.Width>=label.Right&&notice.ClientSize.Height>=ok.Parent.Bottom,$"{name}: 글과 단추가 겹치지 않고 창 안에 있음",$"{notice.ClientSize}");
    Check(ok.Text=="확인"&&notice.AcceptButton==ok&&notice.CancelButton==ok,$"{name}: 단추는 확인 하나이고 Enter·Esc 가 그것을 누름");
    Check(notice.BackColor==Theme.Window&&label.ForeColor==Theme.Text,$"{name}: 창과 글이 디자인 색",$"{notice.BackColor} {label.ForeColor}");
    using(var shot=new Bitmap(notice.Width,notice.Height)){notice.DrawToBitmap(shot,new Rectangle(Point.Empty,shot.Size));shot.Save(Path.Combine(AppContext.BaseDirectory,mode?"notice-preview-dark.png":"notice-preview.png"));}
    notice.Close();
   }
   Theme.Dark=dark;
   // 실제로 띄우는 길(ShowDialog): 확인을 누르면 닫히고, 닫기(X)로도 닫힌다. 주인 창은 이 검사가 띄운 창이다
   using var owner=new AppWindow(){Text="Notice owner",Width=420,Height=240};owner.Show();Application.DoEvents();
   foreach(bool byButton in new[]{true,false}){
    using var timer=new System.Windows.Forms.Timer(){Interval=150};bool seen=false;
    timer.Tick+=(_,_)=>{var open=Application.OpenForms.OfType<NoticeDialog>().FirstOrDefault(f=>f.Visible);if(open==null)return;timer.Stop();seen=open.Text=="기록 삭제"&&open.Owner==owner;if(byButton)((Button)open.AcceptButton!).PerformClick();else open.Close();};
    timer.Start();NoticeDialog.Show(owner,"기록 삭제","확인하는 동안 이 기록이 바뀌었습니다. 목록에서 다시 확인해 주세요.");
    Check(seen&&!Application.OpenForms.OfType<NoticeDialog>().Any(),byButton?"띄운 알림이 확인 단추로 닫힘":"띄운 알림이 닫기(X)로 닫힘");
   }
   owner.Close();
  }finally{Theme.Dark=dark;}
  File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"notice-validation.txt"),string.Join("\n",notes));
 }
 static void Confirmations(Catalog catalog)
 {
  const BindingFlags Flags=BindingFlags.NonPublic|BindingFlags.Instance;
  var notes=new List<string>();void Check(bool ok,string what,object? got=null){if(!ok)throw new Exception($"{what} (본 것: {got})");notes.Add("PASS: "+what);}
  string folder=Path.Combine(AppContext.BaseDirectory,"test-data",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
  string beside=Path.Combine(AppContext.BaseDirectory,"records.json");DateTime? besideAt=File.Exists(beside)?File.GetLastWriteTimeUtc(beside):null;
  using var app=new Quiet(catalog,18768,Path.Combine(folder,"preferences.json"));
  T Field<T>(string name)=>(T)typeof(MainForm).GetField(name,Flags)!.GetValue(app)!;
  object? Peek(string name)=>typeof(MainForm).GetField(name,Flags)!.GetValue(app);
  void Call(string name,params object[] values)=>typeof(MainForm).GetMethod(name,Flags)!.Invoke(app,values);
  var store=new RecordStore(Path.Combine(folder,"records.json"));typeof(MainForm).GetField("store",Flags)!.SetValue(app,store);Call("RefreshProfiles");
  app.Show();Application.DoEvents();
  var session=Field<Session>("session");var status=Field<Label>("status");var ticks=Field<System.Windows.Forms.Timer>("captureTimer");
  Check(!Field<CheckBox>("fullscreen").Checked&&ReferenceEquals(Field<RecordStore>("store"),store),"전제: 전체화면 사용이 꺼져 있고, 주 화면이 임시 기록 파일을 쓴다");
  var chart=new Selection(catalog.Songs.Single(s=>s.Name=="Fundamental").Id,8,"SC","노말",0);const string steamKey="FREESTYLE|Fundamental|8|SC";const int candidate=987117;
  session.Running=true;ticks.Start();   // 기록이 바뀐 것은 앱이 0.1초마다 도는 캡처 흐름(CaptureTick)이 알아챈다. 그 타이머를 그대로 돌린다
  bool Wait(Func<bool> done,int milliseconds){var watch=Stopwatch.StartNew();while(!done()&&watch.ElapsedMilliseconds<milliseconds){Application.DoEvents();Thread.Sleep(10);}return done();}
  // 모듈의 답을 만들어 넣고, 그래서 뜬 확인 창을 돌려준다(창은 하나여야 하고 주 화면이 들고 있는 그 창이어야 한다).
  Confirmation Offer()
  {
   var request=Activator.CreateInstance(typeof(MainForm).GetNestedType("RegistrationRequest",BindingFlags.NonPublic)!,Field<int>("epoch"),steamKey,chart,store.Active.Id,Stopwatch.GetTimestamp(),DateTime.UtcNow,RateSource.Unknown,"FREESTYLE",0,0,new CancellationTokenSource())!;
   typeof(MainForm).GetField("activeRegistration",Flags)!.SetValue(app,request);
   Call("OnScoreAnswer",request,new ScoreAnswer(1,0,steamKey,new RegistrationResult(1,1,RegistrationStatus.Accepted,"",candidate,candidate,null,null,0,0,0),0,0));
   Application.DoEvents();
   var open=Application.OpenForms.OfType<Confirmation>().Where(f=>f.Visible).ToArray();
   Check(open.Length==1&&ReferenceEquals(open[0],Peek("confirmation"))&&session.Pending is {RawScore:candidate,Accuracy:98.71m}&&session.Pending.Chart.Key==chart.Key&&status.Text=="기록 등록 확인 대기","답을 받으면 확인 창이 하나 뜨고 후보(987,117 · 98.71%)가 확인을 기다림",$"창 {open.Length}개 · {session.Pending} · {status.Text}");
   return open[0];
  }
  // 창에 보이는 두 칸의 글: [0] 등록할 기록, [1] 기존 기록 (이름 줄과 값 줄을 이어 붙인다)
  string[] Shown(Confirmation window){var table=Inside(window).OfType<TableLayoutPanel>().Single();return new[]{0,1}.Select(c=>table.GetControlFromPosition(c,0)!.Text+" "+table.GetControlFromPosition(c,1)!.Text).ToArray();}
  void Press(Confirmation window,string text)=>Inside(window).OfType<Button>().Single(b=>b.Text==text).PerformClick();
  bool Closed(Confirmation window)=>session.Pending==null&&window.IsDisposed&&Peek("confirmation")==null&&!Application.OpenForms.OfType<Confirmation>().Any();

  var window=Offer();var shown=Shown(window);
  Check(shown[0].Contains("987,117")&&shown[0].Contains("98.71%")&&shown[1].Contains("미등록"),"확인 창에 등록할 기록(987,117 · 98.71%)과 기존 기록(미등록)이 보임",string.Join(" / ",shown));
  long revision=store.Revision;Press(window,"아니요");
  Check(Closed(window)&&status.Text=="등록 취소","'아니요' 단추: 등록이 취소되고 창이 닫힘",status.Text);
  Check(store.Get(chart)==null&&store.Revision==revision,"'아니요' 단추: 기록이 생기지 않고 기록 파일도 쓰지 않음",store.Get(chart));

  window=Offer();Press(window,"예");
  Check(Closed(window),"'예' 단추: 확인 대기가 풀리고 창이 닫힘",status.Text);
  Check(store.Get(chart) is {Value:candidate,Accuracy:98.71m,PlayCount:1}&&status.Text=="최초 등록","'예' 단추: 후보가 저장됨(987,117 · 98.71% · 횟수 1)",$"{store.Get(chart)} · {status.Text}");

  window=Offer();shown=Shown(window);
  Check(shown[1].Contains("987,117")&&shown[1].Contains("98.71%"),"다음 확인 창의 기존 기록에 저장된 값(987,117 · 98.71%)이 보임",string.Join(" / ",shown));
  store.SetManual(chart,99.50m,3,rawScore:995034);
  bool redrawn=Wait(()=>Shown(window)[1].Contains("995,034"),3000);shown=Shown(window);
  Check(redrawn&&shown[1].Contains("99.50%"),"확인 대기 중에 기존 기록을 바꾸면(995,034 · 99.50% · 횟수 3) 열린 창의 기존 기록이 그 값으로 바뀜",string.Join(" / ",shown));
  Check(shown[0].Contains("987,117")&&shown[0].Contains("98.71%")&&session.Pending?.RawScore==candidate&&!window.IsDisposed&&ReferenceEquals(Peek("confirmation"),window)&&Application.OpenForms.OfType<Confirmation>().Count()==1,"그동안 후보(987,117)와 확인 창은 그대로임(창을 새로 만들지 않음)",string.Join(" / ",shown));
  Press(window,"예");
  Check(Closed(window)&&store.Get(chart) is {Value:995034,Accuracy:99.50m,PlayCount:4}&&status.Text=="기존 기록 유지","더 낮은 후보에 '예': 저장 순간의 최고 기록(995,034) 유지, 횟수만 4",$"{store.Get(chart)} · {status.Text}");

  window=Offer();var kept=store.Get(chart);revision=store.Revision;
  SendMessage(window.Handle,0x0112,(IntPtr)0xF060,IntPtr.Zero);   // 창의 X(닫기)를 누르면 가는 메시지(WM_SYSCOMMAND, SC_CLOSE)
  Check(Wait(()=>Closed(window),2000)&&status.Text=="등록 취소","창의 X(닫기): 등록이 취소되고 창이 닫힘",$"{session.Pending} · {status.Text}");
  Check(store.Get(chart)==kept&&store.Revision==revision,"창의 X(닫기): 기록과 횟수는 그대로(995,034 · 횟수 4)",store.Get(chart));

  ticks.Stop();session.Running=false;
  Check((File.Exists(beside)?File.GetLastWriteTimeUtc(beside):(DateTime?)null)==besideAt,"실행 파일 옆의 기록 파일(records.json)은 건드리지 않음");
  app.Close();
  File.WriteAllLines(Path.Combine(AppContext.BaseDirectory,"confirmation-validation.txt"),notes);
 }
}




