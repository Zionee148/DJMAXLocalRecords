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
  ManualFeatureChecks.Run(catalog);ImportDialogChecks.Run(catalog);GuideDialogChecks.Run();UpdateGuideChecks.Run();
  // 오버레이 디자인 설정 창: 고르는 것은 네 색과 두 투명도뿐이다(글꼴·글자 크기 칸이 없다). 받은 값의 다른 키는 그대로 돌려준다.
  // 빠졌거나 읽을 수 없는 색·투명도는 기본값으로 뜬다(initial 에는 미달성 색이 틀린 글자로, 글자 투명도가 빠진 채로 들어 있다)
  static IEnumerable<Control> All(Control top){foreach(Control child in top.Controls){yield return child;foreach(var inner in All(child))yield return inner;}}
  var broken=JsonSerializer.SerializeToElement(new {Above="#12AB34",Below="red",Background="#10141E",Text="#DCE6F0",Font="궁서",HeaderSize=24,GoalSize=37,CurrentSize=21,BackgroundTransparency=250,Extra="keep"});
  var design=new StyleDialog(broken,true);
  if(All(design).Any(c=>c is TrackBar)||All(design).OfType<ComboBox>().Any(c=>c.Items.Count>20))throw new Exception("Font or size controls remain in the design window");
  var swatches=All(design).OfType<ColorSwatch>().ToArray();var boxes=All(design).OfType<PercentBox>().ToArray();
  if(swatches.Length!=4||boxes.Length!=2)throw new Exception("Design window must have four colors and two transparencies");
  var opened=design.Value;
  if(opened.GetProperty("Above").GetString()!="#12AB34"||opened.GetProperty("Below").GetString()!="#FF707B"||opened.GetProperty("TextTransparency").GetInt32()!=0||opened.GetProperty("BackgroundTransparency").GetInt32()!=100)throw new Exception("Design defaults for missing or unreadable values");
  if(opened.GetProperty("Font").GetString()!="궁서"||opened.GetProperty("HeaderSize").GetInt32()!=24||opened.GetProperty("Extra").GetString()!="keep"||opened.TryGetProperty("BadgeSize",out _))throw new Exception("Design window changed keys it does not own");
  swatches.Single(s=>s.AccessibleName=="배경 색상").Value=Color.FromArgb(0x20,0x30,0x40);boxes.Single(p=>p.Number.AccessibleName!.StartsWith("글자 투명도")).Number.Value=35;
  if(design.Value.GetProperty("Background").GetString()!="#203040"||design.Value.GetProperty("TextTransparency").GetInt32()!=35)throw new Exception("Design choices not reflected in the value");
  // 치는 동안의 숫자는 미리보기에 바로 쓰이고(0~100 으로 맞춰서), 숫자가 아니면 마지막 값을 둔다
  var typing=boxes.Single(p=>p.Number.AccessibleName!.StartsWith("배경 투명도"));typing.Number.Text="7";if(typing.Shown!=7)throw new Exception("Typed transparency not shown");typing.Number.Text="700";if(typing.Shown!=100)throw new Exception("Typed transparency not clamped");typing.Number.Text="x";if(typing.Shown!=100)throw new Exception("Unreadable transparency changed the preview");
  design.Dispose();
  Snapshot(new StyleDialog(initial,true),"style-preview");
  Snapshot(new StyleDialog(initial,true,null,catalog),"style-preview-random");   // 곡 목록에서 뽑은 예시(열 때마다 다르다)
  // 저장: 네 색과 두 투명도만 바꿔 쓰고, 파일에 있던 다른 키와 값은 그대로 둔다. 읽기: 빠진 것·읽을 수 없는 것은 기본값, 투명도는 0~100
  string styleFile=Path.Combine(AppContext.BaseDirectory,"test-data",Guid.NewGuid()+".appearance.json");Directory.CreateDirectory(Path.GetDirectoryName(styleFile)!);
  File.WriteAllText(styleFile,"{\"Font\":\"궁서\",\"HeaderSize\":96,\"Above\":\"#000000\",\"Below\":7,\"TextTransparency\":-3,\"BackgroundTransparency\":\"x\",\"Mine\":[1,2]}");
  var loaded=OverlayStyle.Load(styleFile);
  if(loaded.GetProperty("Above").GetString()!="#000000"||loaded.GetProperty("Below").GetString()!="#FF707B"||loaded.GetProperty("Text").GetString()!="#DCE6F0"||loaded.GetProperty("TextTransparency").GetInt32()!=0||loaded.GetProperty("BackgroundTransparency").GetInt32()!=5||loaded.GetProperty("Font").GetString()!="궁서")throw new Exception("Appearance load rules");
  using(var edit=new StyleDialog(loaded,true)){All(edit).OfType<ColorSwatch>().Single(s=>s.AccessibleName=="달성 색상").Value=Color.FromArgb(1,2,3);OverlayStyle.Save(styleFile,edit.Value);}
  using(var written=JsonDocument.Parse(File.ReadAllText(styleFile))){var w=written.RootElement;
   if(w.GetProperty("Above").GetString()!="#010203"||w.GetProperty("Below").GetString()!="#FF707B"||w.GetProperty("TextTransparency").GetInt32()!=0||w.GetProperty("BackgroundTransparency").GetInt32()!=5)throw new Exception("Appearance save values");
   if(w.GetProperty("Font").GetString()!="궁서"||w.GetProperty("HeaderSize").GetInt32()!=96||w.GetProperty("Mine").GetArrayLength()!=2||w.TryGetProperty("GoalSize",out _))throw new Exception("Appearance save must keep the other keys as they were");}
  File.WriteAllText(styleFile,"not json");if(OverlayStyle.Load(styleFile).GetRawText()!=BadgeDesign.Defaults.GetRawText())throw new Exception("Unreadable appearance file must fall back to defaults");
  OverlayStyle.Save(styleFile,BadgeDesign.Defaults);using(var fresh=JsonDocument.Parse(File.ReadAllText(styleFile)))if(fresh.RootElement.EnumerateObject().Count()!=6)throw new Exception("A new appearance file holds only the six settings");
  File.Delete(styleFile);
  // 예시 곡과 패턴: 곡 목록을 주면 창을 열 때 한 번 무작위로 뽑는다. 곡은 목록의 것이고(오버레이가 보이는 이름 그대로), 버튼·난이도는 그 곡에 실제로 있는 패턴이며, 뽑힌 값이 고르는 칸에 보인다.
  // 여러 번 열면 다른 곡이 나오고, 곡 목록을 주지 않으면 기본 예시(StylePreview.DefaultSong 8B SC 하드판정)다
  {
   var seen=new HashSet<string>();
   for(int round=0;round<40;round++)
   {
    using var random=new StyleDialog(initial,true,null,catalog);var shown=All(random).OfType<StylePreview>().Single();
    if(!catalog.Songs.Any(s=>OverlayText.Title(catalog,s)==shown.SongText))throw new Exception("Example song is not from the catalog: "+shown.SongText);
    if(!catalog.Songs.Where(s=>OverlayText.Title(catalog,s)==shown.SongText).Any(s=>s.Charts.Any(c=>c.Button+"B"==shown.ButtonText&&c.Pattern==shown.PatternText)))throw new Exception($"Example pattern does not exist: {shown.SongText} {shown.ButtonText} {shown.PatternText}");
    if(shown.JudgeText is not("노말" or "하드" or "맥스"))throw new Exception("Example judge");
    var picked=All(random).OfType<ComboBox>().Select(c=>c.Text).ToHashSet();
    if(!picked.Contains(shown.ButtonText)||!picked.Contains(shown.PatternText)||!picked.Contains(shown.JudgeText))throw new Exception("Example pattern not shown in the pickers");
    seen.Add(shown.SongText);
   }
   if(seen.Count<10)throw new Exception("Example song is not random: "+seen.Count);
   using var plain=new StyleDialog(initial,true);var fixedOne=All(plain).OfType<StylePreview>().Single();
   if(fixedOne.SongText!=StylePreview.DefaultSong||fixedOne.ButtonText!="8B"||fixedOne.PatternText!="SC"||fixedOne.JudgeText!="하드")throw new Exception("Default example changed");
  }
  // 미리보기 고르기: 항목을 이름으로 고르면 미리보기가 그 상태로 바뀌어야 한다. 항목 이름은 지금 디자인 설정 창의 것이다(창에 없는 이름이면 여기서 실패한다).
  void PreviewMode(string item,string name){var dialog=new StyleDialog(initial,true);var mode=All(dialog).OfType<ComboBox>().Single(c=>c.Items.Contains(item));mode.SelectedItem=item;var shown=All(dialog).OfType<StylePreview>().Single();if((string?)mode.SelectedItem!=item||shown.Mode!=mode.SelectedIndex)throw new Exception("Preview mode not applied: "+item);shown.Play(false);Snapshot(dialog,name);}
  PreviewMode("등록 확인","pending-style-preview");PreviewMode("첫 기록 등록","first-record-style-preview");
  Confirmations(catalog);Notices();
  string optionFile=Path.Combine(AppContext.BaseDirectory,"test-data",Guid.NewGuid()+".preferences.json");Directory.CreateDirectory(Path.GetDirectoryName(optionFile)!);
  // 예전 설정 파일에 '전체화면 사용'이 켜져 있어도 같은 흐름이어야 한다(그 값은 쓰지 않고, 다시 적을 때 그대로 둔다)
  new UserOptions(true,false,false).Save(optionFile);
  using var app=new MainForm(catalog,18766,false,optionFile);app.Show();Application.DoEvents();RescanChecks.Run(app,catalog);
  if(previewPath!=null){using var frame=new Bitmap(previewPath);typeof(MainForm).GetMethod("ShowImage",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(app,new object[]{frame});Application.DoEvents();}
  using(var shot=new Bitmap(app.Width,app.Height)){app.DrawToBitmap(shot,new Rectangle(Point.Empty,shot.Size));shot.Save(Path.Combine(AppContext.BaseDirectory,"ui-preview.png"));}
  // 등록: 다른 창이 앞에 있을 때 등록해도 창을 새로 만들지 않고, 앞에 있던 창이 그대로이고, 저장 확인이 주 화면 안에 뜨는가. 앞 창은 이 검사가 띄운 같은 프로세스의 창이다(다른 프로세스인 게임이 앞에 있는 경우가 아니다).
  // 후보는 개발용 ShowRegistration 으로 만든다. 실제 등록 경로(OnScoreAnswer → ShowPendingConfirmation)와 저장 확인의 단추·갱신은 Confirmations 가 본다.
  using var game=new Form(){Text="Focus test",Width=320,Height=180};game.Show();game.Activate();Application.DoEvents();var foreground=GetForegroundWindow();var active=GetActiveWindow();int forms=Application.OpenForms.Count;
  var field=typeof(MainForm).GetField("session",BindingFlags.NonPublic|BindingFlags.Instance)!;var session=(Session)field.GetValue(app)!;
  var panel=(ConfirmPanel)typeof(MainForm).GetField("confirmPanel",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(app)!;
  var song=catalog.Songs.First(s=>s.Charts.Count>0);var chart=song.Charts[0];session.Observe(new(song.Id,chart.Button,chart.Pattern,"노말",1),0);session.Lock();session.Current=99.48m;session.Running=true;
  foreach(bool byHotkey in new[]{true,false})
  {
   session.Current=99.48m;typeof(MainForm).GetMethod("ShowRegistration",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(app,new object[]{byHotkey});Application.DoEvents();
   if(Application.OpenForms.Count!=forms||GetForegroundWindow()!=foreground||GetActiveWindow()!=active||session.Pending?.Accuracy!=99.48m||!panel.Visible||panel.Shown?.NewRate!="99.48%")throw new Exception("Registration created a window, stole focus, lost the snapshot, or did not show the save confirmation in the main window");
   typeof(MainForm).GetMethod("Decide",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(app,new object[]{false});Application.DoEvents();
   if(session.Pending!=null||session.Locked==null||panel.Visible||GetForegroundWindow()!=foreground||GetActiveWindow()!=active)throw new Exception("Cancel lost locked chart, left the save confirmation open, or moved focus");
  }
  // 다른 설정을 고쳐 적어도 예전의 '전체화면 사용' 값은 파일에 그대로 남는다
  var images=(CheckBox)typeof(MainForm).GetField("scoreImagesOption",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(app)!;images.Checked=true;
  {var kept=UserOptions.Load(optionFile);if(!kept.Fullscreen||!kept.DiagnosticImages)throw new Exception("Old fullscreen value not kept, or the changed option not saved");}
  images.Checked=false;
  var preview=(PictureBox)typeof(MainForm).GetField("picture",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(app)!;var root=(ScrollableControl)app.Controls[0];bool PreviewEnabled()=>(bool)typeof(MainForm).GetMethod("ShouldUpdatePreview",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(app,null)!;root.ScrollControlIntoView(preview);Application.DoEvents();if(!PreviewEnabled())throw new Exception("Visible preview suppressed");app.WindowState=FormWindowState.Minimized;if(PreviewEnabled())throw new Exception("Minimized preview still updating");app.WindowState=FormWindowState.Normal;app.Hide();if(PreviewEnabled())throw new Exception("Hidden preview still updating");app.Show();app.MinimumSize=Size.Empty;app.ClientSize=new(480,200);root.AutoScrollPosition=Point.Empty;Application.DoEvents();if(PreviewEnabled())throw new Exception("Offscreen preview still updating");app.ClientSize=new(720,900);root.ScrollControlIntoView(preview);Application.DoEvents();if(!PreviewEnabled())throw new Exception("Preview did not resume after scrolling into view");
  // 여기에 도달했다면 위의 실패 조건에 하나도 걸리지 않은 것이다. 실제로 확인한 항목만 적는다(포커스는 통과로 적지 않고 측정값을 따로 적는다).
  File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"ui-validation.txt"),
   "PASS: 단축키 목록 옮기기(4개·6개 → 5개, 중복 거절); 단축키·디자인 창의 그림과 창 아이콘; 디자인 설정(고르는 것이 네 색과 두 투명도뿐, 틀린 값은 기본값, 다른 키는 그대로 돌려주고 파일에도 그대로 남음, 예시 곡과 패턴은 곡 목록에서 뽑음, 미리보기 \"등록 확인\"·\"첫 기록 등록\" 고르기); "
   +"등록(두 번의 등록에 창을 새로 만들지 않고, 같은 프로세스의 앞 창과 활성 창이 그대로이고, 저장 확인이 주 화면 안에 뜨고 후보 99.48%를 유지, 취소하면 곡 고정이 남음. 예전 설정 파일의 '전체화면 사용'이 켜진 채로 돌렸다); 다른 설정을 저장해도 예전의 '전체화면 사용' 값이 파일에 남음; 게임 화면 미리 보기의 갱신 판단(보임·최소화·숨김·화면 밖·다시 보임)."
   +"\n함께 실행한 검사(결과는 각 파일): 기록 목록 records-validation.txt, 목록 설정 records-settings-validation.txt, 수동 기록 manual-validation.txt, 주 화면의 저장 확인(단추·단축키와 같은 길·기존 기록 갱신) confirmation-validation.txt, 새로 감지하기 rescan-validation.txt."
   +"\n검사 범위 밖의 항목: 등록 키 입력부터 SCORE 모듈의 답까지, 전역 단축키, 실제 게임, 다른 프로세스가 앞에 있을 때의 포커스.\n");
 }

 // 주 화면의 저장 확인의 검사. Run 이 부른다. 결과는 confirmation-validation.txt.
 //
 // 주 화면에 SCORE 모듈의 답(Accepted)을 만들어 넣는다. 그러면 앱이 답을 받았을 때의 코드(OnScoreAnswer → session.Begin → ShowPendingConfirmation)가
 // 주 화면 오른쪽에 저장 확인을 보인다. 저장·취소 단추와 단축키가 가는 길(Action 4·5)을 누르고, 확인을 기다리는 동안 기존 기록을 바꿔 보이는 값이 따라 바뀌는지 본다.
 // 합성 자료로 하는 시험이다: 후보와 기록은 검사가 만든 값이고, 기록은 test-data 아래의 임시 파일에 쓴다(주 화면의 store 를 임시 저장소로 교체한다). Steam 곡 정보는 "곡 없음"을 돌려주게 재정의해 게임 화면을 캡처하지 않는다.
 // 등록 키 입력부터 답까지(RequestRegistration, SCORE 판독), 전역 단축키, 실제 게임은 이 검사의 범위 밖이다.
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
    var need=TextRenderer.MeasureText(guide,label.Font,new Size(Theme.S(520),int.MaxValue),TextFormatFlags.WordBreak|TextFormatFlags.TextBoxControl|TextFormatFlags.NoPrefix);
    Check(label.Width<=Theme.S(520)&&label.Height>=line*8,$"{name}: 긴 글이 정한 폭(520) 안에서 여러 줄로 놓임",$"{label.Width}x{label.Height}, 한 줄 {line}");
    Check(need.Width<=Theme.S(520)&&label.Height>=need.Height-2,$"{name}: 글이 잘리지 않음(줄을 바꿔 잰 크기 이상)",$"글 칸 {label.Width}x{label.Height}, 필요한 크기 {need.Width}x{need.Height}");
    Check(label.Bottom<=ok.Parent!.Top&&notice.ClientSize.Width>=label.Right&&notice.ClientSize.Height>=ok.Parent.Bottom,$"{name}: 글과 단추가 겹치지 않고 창 안에 있음",$"{notice.ClientSize}");
    Check(ok.Text=="확인"&&notice.AcceptButton==ok&&notice.CancelButton==ok,$"{name}: 단추는 확인 하나이고 Enter·Esc 가 그것을 누름");
    Check(notice.BackColor==Theme.Window&&label.ForeColor==Theme.Text,$"{name}: 창과 글이 디자인 색",$"{notice.BackColor} {label.ForeColor}");
    using(var shot=new Bitmap(notice.Width,notice.Height)){notice.DrawToBitmap(shot,new Rectangle(Point.Empty,shot.Size));shot.Save(Path.Combine(AppContext.BaseDirectory,mode?"notice-preview-dark.png":"notice-preview.png"));}
    notice.Close();
   }
   Theme.Dark=dark;
   // 실제로 띄우는 경로(ShowDialog): 확인을 누르면 닫히고, 닫기(X)로도 닫힌다. 소유자 창(Owner)은 이 검사가 띄운 창이다
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
  string beside=UserFiles.At("records.json");DateTime? besideAt=File.Exists(beside)?File.GetLastWriteTimeUtc(beside):null;
  using var app=new Quiet(catalog,18768,Path.Combine(folder,"preferences.json"));
  T Field<T>(string name)=>(T)typeof(MainForm).GetField(name,Flags)!.GetValue(app)!;
  void Call(string name,params object[] values)=>typeof(MainForm).GetMethod(name,Flags)!.Invoke(app,values);
  var store=new RecordStore(Path.Combine(folder,"records.json"));typeof(MainForm).GetField("store",Flags)!.SetValue(app,store);Call("RefreshProfiles");
  app.Show();Application.DoEvents();
  var session=Field<Session>("session");var status=Field<Label>("status");var ticks=Field<System.Windows.Forms.Timer>("captureTimer");var keys=Field<Hotkeys>("hotkeys");
  var panel=Field<ConfirmPanel>("confirmPanel");var head=Field<SectionHeader>("songHead");var goal=Field<BigValue>("goalValue");var card=Field<SongCard>("songTitle");var register=Field<Button>("registerButton");var listing=Field<Button>("refreshButton");
  Check(ReferenceEquals(Field<RecordStore>("store"),store)&&!panel.Visible&&head.Text=="곡 정보"&&goal.Visible,"전제: 주 화면이 임시 기록 파일을 쓰고, 저장 확인이 보이지 않고 목표·현재가 보인다");
  var song=catalog.Songs.Single(s=>s.Name=="Fundamental");var chart=new Selection(song.Id,8,"SC","노말",0);const string steamKey="FREESTYLE|Fundamental|8|SC";const int candidate=987117;
  session.Running=true;ticks.Start();   // 기록이 바뀐 것은 앱이 0.1초마다 도는 캡처 흐름(CaptureTick)이 알아챈다. 그 타이머를 그대로 돌린다
  bool Wait(Func<bool> done,int milliseconds){var watch=Stopwatch.StartNew();while(!done()&&watch.ElapsedMilliseconds<milliseconds){Application.DoEvents();Thread.Sleep(10);}return done();}
  int forms=Application.OpenForms.Count;
  // 모듈의 답을 만들어 넣는다. 창이 새로 생기지 않고 주 화면 오른쪽에 저장 확인이 떠야 한다
  void Offer()
  {
   var request=Activator.CreateInstance(typeof(MainForm).GetNestedType("RegistrationRequest",BindingFlags.NonPublic)!,Field<int>("epoch"),steamKey,chart,store.Active.Id,Stopwatch.GetTimestamp(),DateTime.UtcNow,RateSource.Unknown,"FREESTYLE",0,0,new CancellationTokenSource())!;
   typeof(MainForm).GetField("activeRegistration",Flags)!.SetValue(app,request);
   Call("OnScoreAnswer",request,new ScoreAnswer(1,0,steamKey,new RegistrationResult(1,1,RegistrationStatus.Accepted,"",candidate,candidate,null,null,0,0,0),0,0));
   Application.DoEvents();
   int before=Field<int>("epoch");listing.PerformClick();Application.DoEvents();
   Check(!listing.Enabled&&Field<int>("epoch")==before&&session.Pending!=null,"저장 확인을 기다리는 동안 '목록 갱신'은 누를 수 없고, 눌러도 요청이 그대로다",$"누를 수 있음 {listing.Enabled} · 번호 {before}→{Field<int>("epoch")}");
   Check(Application.OpenForms.Count==forms&&panel.Visible&&head.Text=="저장 확인"&&!goal.Visible&&!register.Enabled&&card.Text==OverlayText.Title(catalog,song)
    &&session.Pending is {RawScore:candidate,Accuracy:98.71m}&&session.Pending.Chart.Key==chart.Key&&status.Text=="저장 확인 대기",
    "답을 받으면 창을 새로 만들지 않고 주 화면 오른쪽이 '저장 확인'으로 바뀌어 후보(987,117 · 98.71%)가 확인을 기다림(곡 정보 카드는 요청한 곡, 등록 단추는 누를 수 없음)",
    $"창 {Application.OpenForms.Count}/{forms} · 보임 {panel.Visible} · 제목 {head.Text} · {session.Pending} · {status.Text} · 카드 {card.Text}");
  }
  // 보이는 두 칸의 글: [0] 이번 기록, [1] 기존 최고 기록
  string[] Shown()=>new[]{panel.Shown!.NewScore+" "+panel.Shown.NewRate+" "+panel.Shown.Tone,panel.Shown.OldScore+" "+panel.Shown.OldRate};
  void Press(string text)=>Inside(panel).OfType<Button>().Single(b=>b.Text==text).PerformClick();
  bool Closed()=>session.Pending==null&&!panel.Visible&&head.Text=="곡 정보"&&goal.Visible&&register.Enabled&&Application.OpenForms.Count==forms;

  Offer();var shown=Shown();
  Check(shown[0].Contains("987,117")&&shown[0].Contains("98.71%")&&shown[1].Contains("미등록")&&panel.Shown!.Tone==null,"저장 확인에 이번 기록(987,117 · 98.71%)과 기존 최고 기록(미등록)이 보임. 첫 기록은 보통 글자색",string.Join(" / ",shown));
  Check(panel.Save.Key==Hotkeys.Format(keys.Items[2])&&panel.Cancel.Key==Hotkeys.Format(keys.Items[3])&&panel.Save.Key.Length>0,"저장·취소 단추에 지금 지정된 단축키가 보임",panel.Save.Key+" / "+panel.Cancel.Key);
  using(var shot=new Bitmap(app.Width,app.Height)){app.DrawToBitmap(shot,new Rectangle(Point.Empty,shot.Size));shot.Save(Path.Combine(AppContext.BaseDirectory,"confirmation-preview.png"));}
  long revision=store.Revision;Press("취소");
  Check(Closed()&&status.Text=="등록 취소","'취소' 단추: 등록이 취소되고 곡 정보와 목표·현재로 돌아감",status.Text);
  Check(store.Get(chart)==null&&store.Revision==revision,"'취소' 단추: 기록이 생기지 않고 기록 파일도 쓰지 않음",store.Get(chart));

  Offer();Press("저장");
  Check(Closed(),"'저장' 단추: 확인 대기가 풀리고 곡 정보와 목표·현재로 돌아감",status.Text);
  Check(store.Get(chart) is {Value:candidate,Accuracy:98.71m,PlayCount:1}&&status.Text=="최초 등록","'저장' 단추: 후보가 저장됨(987,117 · 98.71% · 횟수 1)",$"{store.Get(chart)} · {status.Text}");
  Check(goal.Text=="98.71%"&&card.Text==OverlayText.Title(catalog,song)&&listing.Enabled,"저장한 뒤 같은 곡의 카드에서 목표가 저장한 값(98.71%)으로 보임('미등록'이 아니다)",$"목표 {goal.Text} · 카드 {card.Text}");

  Offer();shown=Shown();
  Check(shown[1].Contains("987,117")&&shown[1].Contains("98.71%")&&shown[0].EndsWith("good"),"다음 저장 확인의 기존 최고 기록에 저장된 값(987,117 · 98.71%)이 보임. 같은 점수는 연두",string.Join(" / ",shown));
  store.SetManual(chart,99.50m,3,rawScore:995034);
  bool redrawn=Wait(()=>Shown()[1].Contains("995,034"),3000);shown=Shown();
  Check(redrawn&&shown[1].Contains("99.50%")&&shown[0].EndsWith("bad"),"확인 대기 중에 더 높은 한 판(995,034 · 99.50%)이 들어오면 보이는 기존 최고 기록이 그 값으로 바뀌고 이번 기록이 낮은 색이 됨",string.Join(" / ",shown));
  Check(shown[0].Contains("987,117")&&shown[0].Contains("98.71%")&&session.Pending?.RawScore==candidate&&panel.Visible&&Application.OpenForms.Count==forms,"그동안 후보(987,117)와 저장 확인은 그대로임",string.Join(" / ",shown));
  int seqAtSave=store.Active.NextSeq;Press("저장");
  Check(Closed()&&store.Get(chart) is {Value:995034,Accuracy:99.50m,PlayCount:3}&&status.Text=="기존 기록 유지","더 낮은 후보에 '저장': 저장 순간의 최고 기록(995,034) 유지, 이번 판도 남아 판 수 3",$"{store.Get(chart)} · {status.Text}");
  Check(store.Active.Plays[^1] is {Source:PlaySource.Play} lowerSaved&&lowerSaved.Seq==seqAtSave&&lowerSaved.Score==candidate&&lowerSaved.Key==chart.Key,"그 낮은 판은 후보의 점수(987,117) 그대로 등록 출처의 새 줄로 남음(같은 점수의 앞선 판이 아니라 이번에 더한 줄)",store.Active.Plays[^1].ToString());
  Check(goal.Text=="99.50%","기존 기록을 유지한 뒤의 목표는 그 최고 기록(99.50%)",goal.Text);

  // 단축키가 가는 길(Action 4 = 저장 확인, 5 = 저장 취소. 전역 단축키 자체는 이 검사의 범위 밖이다)
  Offer();var keptRecord=store.Get(chart);revision=store.Revision;
  Call("Action",5,true);Application.DoEvents();
  Check(Closed()&&status.Text=="등록 취소"&&store.Get(chart)==keptRecord&&store.Revision==revision,"취소 단축키의 길: 등록이 취소되고 기록과 판 수는 그대로(995,034 · 판 수 3)",$"{session.Pending} · {status.Text} · {store.Get(chart)}");
  Offer();Call("Action",4,true);Call("Action",4,true);Application.DoEvents();
  Check(Closed()&&store.Get(chart) is {Value:995034,PlayCount:4},"저장 단축키의 길을 두 번 눌러도 같은 요청은 한 번만 저장됨(판 수 4)",store.Get(chart));

  ticks.Stop();session.Running=false;
  Check((File.Exists(beside)?File.GetLastWriteTimeUtc(beside):(DateTime?)null)==besideAt,"‘UserData’의 기록 파일(records.json)은 건드리지 않음");
  app.Close();
  File.WriteAllLines(Path.Combine(AppContext.BaseDirectory,"confirmation-validation.txt"),notes);
 }
}




