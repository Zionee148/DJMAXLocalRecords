using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using DjmaxScore;
namespace LadderRecords;

// 공개 안내에 싣는 프로그램 창 그림을 찍는다: --public-shots <저장할 폴더> [받아 둔 컬렉션 화면의 폴더|-] [게임 화면 PNG|-]
// 꾸민 기록으로 장면을 만들고 화면을 그대로 복사한다(다크 테마, 지금의 앱 표시 크기). 창이 화면 맨 위에 여러 번 떴다 사라진다.
//   01-main                  메인 창: 게임 창을 고르고 감지 중인 곡(Sweet On You 8B SC 노말판정)의 목표와 현재
//   06-program-confirmation  메인 창의 저장 확인: 같은 곡의 등록 후보 997,851 과 기존 최고 기록 999,511
//   07-records               기록 목록을 하드·맥스판정으로 거른 모습
//   09-design-settings       오버레이 디자인 설정
//   10-diagnostics-controls  진단 기록 줄과 펼친 진단 그림 저장 설명
//   11-play-history          한 패턴의 플레이 히스토리(그래프의 점 하나를 눌러 표에서 고른 모습)
//   12-collection-review     판독 검수와 캡처 보기(받아 둔 컬렉션 화면을 줄 때만. 화면 오른쪽 위의 프로필 자리는 흐리게 가린다)
//   13-stream-address        투컴 연결 허용, 가려진 송출 PC용 주소와 연결 상태
// 게임 화면 PNG 를 주면 메인 창의 게임 화면과 SCORE 자리에 넣는다.
// 개인 기록을 쓰지 않는 폴더에서만 돌린다(기록은 저장할 폴더 안에 새로 만드는 work 에 쓴다. 단축키를 등록하지 않는다. 투컴은 이 PC 안의 루프백 주소에만 연다)
// work 가 이미 있으면 지우지 않고 public-shots-stopped.txt 에 까닭을 남기고 멈춘다(끝 코드 1)
[System.Runtime.Versioning.SupportedOSPlatform("windows10.0.19041")]
static class PublicShotChecks
{
 [StructLayout(LayoutKind.Sequential)]struct Rect{public int Left,Top,Right,Bottom;}
 [DllImport("dwmapi.dll")]static extern int DwmGetWindowAttribute(IntPtr window,int attribute,out Rect value,int size);
 [DllImport("user32.dll")]static extern bool PrintWindow(IntPtr window,IntPtr dc,uint flags);
 const int Port=18783;
 static readonly BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
 static void Wait(int ms){var until=DateTime.UtcNow.AddMilliseconds(ms);while(DateTime.UtcNow<until){Application.DoEvents();Thread.Sleep(10);}}
 static bool Until(Func<bool> done,int ms){var until=DateTime.UtcNow.AddMilliseconds(ms);while(DateTime.UtcNow<until){Application.DoEvents();if(done())return true;Thread.Sleep(10);}return done();}
 static IEnumerable<Control> All(Control top){foreach(Control child in top.Controls){yield return child;foreach(var inner in All(child))yield return inner;}}
 /// <summary>창이 화면에 보이는 테두리(그림자 제외)</summary>
 static Rectangle Seen(Form form){DwmGetWindowAttribute(form.Handle,9,out var r,Marshal.SizeOf<Rect>());return Rectangle.FromLTRB(r.Left,r.Top,r.Right,r.Bottom);}
 /// <summary>창을 맨 위로 올리고 다 그려지기를 기다린 뒤 화면에서 그대로 복사한다</summary>
 static Bitmap Grab(Form form,int settle=900)
 {
  form.TopMost=true;form.Activate();Wait(settle);
  var area=Seen(form);var shot=new Bitmap(area.Width,area.Height);
  using(var g=Graphics.FromImage(shot))g.CopyFromScreen(area.Location,Point.Empty,area.Size);
  return shot;
 }
 /// <summary>다른 창에 가려져 있어도 창의 내용을 그려 받는다(겹친 두 창을 한 그림으로 맞출 때)</summary>
 static Bitmap Print(Form form)
 {
  var shot=new Bitmap(form.Width,form.Height);
  using(var g=Graphics.FromImage(shot)){var dc=g.GetHdc();try{PrintWindow(form.Handle,dc,2);}finally{g.ReleaseHdc(dc);}}
  return shot;
 }
 /// <summary>창 그림 안에서 컨트롤들이 차지하는 자리(여백을 붙인다)</summary>
 static Rectangle Around(Form form,int sides,int ends,params Control[] parts)
 {
  var seen=Seen(form);Rectangle? all=null;
  foreach(var part in parts){var r=part.RectangleToScreen(part.ClientRectangle);all=all is {} a?Rectangle.Union(a,r):r;}
  var area=all!.Value;area.Offset(-seen.Left,-seen.Top);area.Inflate(Theme.S(sides),Theme.S(ends));
  area.Intersect(new Rectangle(Point.Empty,seen.Size));return area;
 }
 static void Save(Bitmap image,string folder,string name,List<string> notes){image.Save(Path.Combine(folder,name+".png"),ImageFormat.Png);notes.Add($"{name}: {image.Width}x{image.Height}");image.Dispose();}
 static void SaveCut(Bitmap image,Rectangle area,string folder,string name,List<string> notes){using(image){Save(image.Clone(area,image.PixelFormat),folder,name,notes);}}

 /// <summary>목록·히스토리·메인 창에 쓸 기록. 모두 한 프로필(기본)이다</summary>
 static Selection Seed(Catalog catalog,RecordStore store,Selection sweet)
 {
  var day=new DateTime(2026,11,10,10,0,0,DateTimeKind.Utc);   // 한국 시각 19시. 판마다 이날로부터 며칠 뒤 저녁의 몇 분 뒤에 둔다
  Selection Chart(string name,int button,string pattern,string judge)=>new(catalog.Songs.First(s=>s.Name==name).Id,button,pattern,judge,0);
  store.Add(sweet,999511,new DateTime(2026,11,16,12,42,0,DateTimeKind.Utc));
  // 히스토리를 보일 패턴: 보름 남짓 동안의 열 판(낮은 판 하나, PB 하나)
  var kamui=Chart("Kamui",8,"SC","하드");
  int[] scores={991420,993105,992268,994880,981712,995630,996214,995902,997385,996940};
  int[] days={0,1,3,4,6,8,9,11,13,15},minutes={12,47,95,23,140,66,8,151,34,102};
  for(int i=0;i<scores.Length;i++)store.Add(kamui,scores[i],day.AddDays(days[i]).AddMinutes(minutes[i]));
  // 목록에 함께 보일 하드·맥스판정 기록
  var others=new (string Name,int Button,string Pattern,string Judge,int Score,int Day,int Minute)[]
  {
   ("Fundamental",8,"SC","맥스",994212,14,128),("SON OF SUN",5,"SC","하드",992880,12,75),("Lisrim",8,"MAXIMUM","맥스",998640,12,41),
   ("Brain Storm",6,"MAXIMUM","하드",999104,10,163),("Ladymade Star",4,"SC","하드",996372,9,96),("Nevermind",8,"SC","맥스",989945,7,58),
   ("Alone",5,"MAXIMUM","하드",998021,5,119),("Urban Night",8,"MAXIMUM","맥스",997760,3,31),("Sweet On You",6,"SC","하드",995318,2,84),
  };
  foreach(var o in others)store.Add(Chart(o.Name,o.Button,o.Pattern,o.Judge),o.Score,day.AddDays(o.Day).AddMinutes(o.Minute));
  return kamui;
 }

 /// <summary>받아 둔 컬렉션 화면에서 판독 검수에 보일 결과를 만든다: 점수 칸이 가장 많이 찬 곡의 칸 하나를 확인할 점수로, 다음 곡을 곡 선택으로</summary>
 static (ScanResult Result,ScanItem Score)? Review(Catalog catalog,string stills,List<string> notes)
 {
  var reader=new CollectionReader(catalog,CollectionData.Load());var found=new List<(ScanItem Item,int Filled)>();
  foreach(string file in Directory.GetFiles(stills,"*-still.png").OrderBy(f=>f))
  {
   using var bitmap=new Bitmap(file);if(bitmap.Width!=CollectionFrame.Width||bitmap.Height!=CollectionFrame.Height)continue;
   var shot=reader.Read(new CollectionFrame(bitmap));var judge=reader.Judge(shot);
   if(judge.Song==null||!judge.Sure.All(s=>s)||found.Any(f=>f.Item.Judge.Song!.Id==judge.Song.Id))continue;
   found.Add((new ScanItem{Shot=shot,Judge=judge,Sure=judge.Sure.ToArray(),Screen=Masked(bitmap)},shot.Cells.Count(c=>c.Number)));
  }
  var order=found.OrderByDescending(f=>f.Filled).ToList();
  notes.Add($"collection: 화면에서 읽은 곡 {found.Count}, 점수 칸이 가장 많이 찬 곡 {(order.Count>0?order[0].Item.Judge.Song!.Name+" "+order[0].Filled+"칸":"없음")}");
  if(order.Count<2)return null;
  var score=order[0].Item;int cell=Enumerable.Range(0,16).Last(i=>score.Shot.Cells[i].Number);score.Sure[cell]=false;score.Questions=1;
  var pick=order[1].Item;var twin=catalog.Songs.FirstOrDefault(s=>s.Id!=pick.Judge.Song!.Id&&CollectionReview.Fits(s,pick));
  var undecided=new SongJudge{Song=null};undecided.Candidates.Add(pick.Judge.Song!);if(twin!=null)undecided.Candidates.Add(twin);
  var song=new ScanItem{Shot=pick.Shot,Judge=undecided,Sure=pick.Sure,Screen=pick.Screen,Questions=1};
  var result=new ScanResult{End=ScanEnd.Done};result.Items.Add(score);result.Items.Add(song);result.Items.AddRange(order.Skip(2).Take(2).Select(f=>f.Item));
  result.Questions=result.Items.Sum(i=>i.Questions);return (result,score);
 }
 /// <summary>화면 오른쪽 위의 프로필(닉네임과 레벨) 자리를 굵은 모자이크로 가린 PNG</summary>
 static byte[] Masked(Bitmap frame)
 {
  using var copy=new Bitmap(frame);var plate=new Rectangle(1505,20,335,82);const int cell=14;
  using(var small=new Bitmap((plate.Width+cell-1)/cell,(plate.Height+cell-1)/cell))
  {
   using(var g=Graphics.FromImage(small)){g.InterpolationMode=InterpolationMode.HighQualityBilinear;g.DrawImage(frame,new Rectangle(Point.Empty,small.Size),plate,GraphicsUnit.Pixel);}
   using var g2=Graphics.FromImage(copy);g2.InterpolationMode=InterpolationMode.NearestNeighbor;g2.PixelOffsetMode=PixelOffsetMode.Half;
   g2.DrawImage(small,plate,new Rectangle(Point.Empty,small.Size),GraphicsUnit.Pixel);
  }
  using var stream=new MemoryStream();copy.Save(stream,ImageFormat.Png);return stream.ToArray();
 }

 public static void Run(Catalog catalog,string folder,string? stills,string? gameShot)
 {
  Directory.CreateDirectory(folder);Theme.Dark=true;
  var notes=new List<string>{$"scale={Theme.Shown}% bar={AppWindow.BarHeight} fonts=[{AppFonts.Ui.Name}, {AppFonts.Head.Name}, {AppFonts.Latin.Name}]"};
  string work=Path.Combine(folder,"work");
  if(Directory.Exists(work))
  {   // 다른 작업의 자료일 수 있어 지우지 않는다: 찍지 않고 까닭만 남긴다
   File.WriteAllLines(Path.Combine(folder,"public-shots-stopped.txt"),new[]{$"멈춤: 작업 폴더가 이미 있다: {work}","그 안의 자료를 지우지 않으려고 찍지 않았다. 새 저장 폴더를 주거나, 그 폴더가 필요 없으면 직접 지운 뒤 다시 돌린다."});
   Environment.ExitCode=1;return;
  }
  Directory.CreateDirectory(work);
  var store=new RecordStore(Path.Combine(work,"records.json"));
  var sweetSong=catalog.Songs.First(s=>s.Name=="Sweet On You");var sweet=new Selection(sweetSong.Id,8,"SC","노말",0);
  var kamui=Seed(catalog,store,sweet);
  string options=Path.Combine(work,"preferences.json");new UserOptions(false,false,true).Save(options);
  // 투컴: 연결 목록과 방화벽 읽기를 꾸민 것으로 바꿔 끼운다(송출용 서버는 이 PC 안의 루프백 주소에 열려 Windows 방화벽 창이 뜨지 않는다)
  var realLinks=NetLinks.Source;var realReader=SenderFirewall.Reader;
  NetLinks.Source=()=>new List<NetLink>{new("{11111111-1111-1111-1111-111111111111}","이더넷",System.Net.IPAddress.Parse("127.0.0.2"),8,false)};
  SenderFirewall.Reader=(_,_)=>false;
  try
  {
   using var main=new MainForm(catalog,Port,false,options){StartPosition=FormStartPosition.Manual,Location=new Point(40,40)};
   main.Show();Wait(400);
   T Part<T>(string name)=>(T)typeof(MainForm).GetField(name,Hidden)!.GetValue(main)!;
   object? Do(string name,params object?[] values)=>typeof(MainForm).GetMethod(name,Hidden)!.Invoke(main,values);
   typeof(MainForm).GetField("store",Hidden)!.SetValue(main,store);Do("RefreshProfiles");
   // 개발용 포트 대신 기본 주소를 보인다(보이는 글만 바꾼다)
   All(main).OfType<InputBox>().First(b=>b.Box.AccessibleName=="OBS 브라우저 주소").Box.Text="http://127.0.0.1:18765/";
   var windows=Part<ComboBox>("windows");windows.Items.Clear();
   windows.Items.Add(Activator.CreateInstance(typeof(MainForm).GetNestedType("WindowItem",BindingFlags.NonPublic)!,IntPtr.Zero,"DJMAX RESPECT V")!);windows.SelectedIndex=0;
   // 개발용 빌드의 업데이트 줄 대신 배포판이 켤 때 확인한 뒤의 모습을 보인다
   Part<Button>("updateCheck").Enabled=true;Do("Note","v1.2.0 · 최신 버전","dim");
   // 01: 감지 중인 곡
   var session=Part<Session>("session");session.Running=true;
   Do("ShowSong",sweetSong,"8B","SC","노말판정",false);
   session.Detect(sweet);session.Current=99.78m;Do("Detected","프리스타일 · 노말판정");Do("RefreshState");
   if(gameShot!=null)
   {
    using var screen=new Bitmap(gameShot);
    Part<PictureBox>("picture").Image=new Bitmap(screen);
    Part<PictureBox>("scorePicture").Image=screen.Clone(new Rectangle(306,958,108,32),PixelFormat.Format24bppRgb);
   }
   Part<Label>("scoreStatus").Text="SCORE 위치 고정";
   Save(Grab(main,1200),folder,"01-main",notes);
   // 06: 같은 곡의 저장 확인(실제 흐름으로 SCORE 모듈의 답을 넣는다)
   {
    const string key="FREESTYLE|Sweet On You|8|SC";
    var request=Activator.CreateInstance(typeof(MainForm).GetNestedType("RegistrationRequest",BindingFlags.NonPublic)!,Part<int>("epoch"),key,sweet,store.Active.Id,System.Diagnostics.Stopwatch.GetTimestamp(),DateTime.UtcNow,RateSource.Unknown,"FREESTYLE",0,0,new CancellationTokenSource())!;
    typeof(MainForm).GetField("activeRegistration",Hidden)!.SetValue(main,request);
    Do("OnScoreAnswer",request,new ScoreAnswer(1,0,key,new RegistrationResult(1,1,RegistrationStatus.Accepted,"",997851,997851,null,null,0,0,0),0,0));
    Part<Label>("scoreStatus").Text="SCORE 위치 고정";
    Save(Grab(main,1800),folder,"06-program-confirmation",notes);
    Do("Decide",false);Wait(600);
   }
   // 10: 진단 기록 줄과 펼친 설명
   {
    var option=Part<CheckBox>("scoreImagesOption");var badge=option.Parent!.Controls.OfType<HelpBadge>().Single();badge.Press(Keys.Space);Wait(300);
    var name=All(main).OfType<Label>().First(l=>l.Text=="진단 기록");var open=All(main).OfType<Button>().First(b=>b.Text=="진단 기록 폴더 열기");
    var shot=Grab(main,600);SaveCut(shot,Around(main,12,4,name,open,option,badge,Part<Control>("imagesHelpBox")),folder,"10-diagnostics-controls",notes);
    badge.Press(Keys.Space);Wait(300);
   }
   // 07·11: 기록 목록(하드·맥스판정)과 한 패턴의 히스토리
   {
    using var records=new RecordsDialog(catalog,store,Path.Combine(work,"records-view.json")){StartPosition=FormStartPosition.Manual,Location=new Point(40,40)};
    records.Show(main);Wait(500);
    var judge=(ComboBox)typeof(RecordsDialog).GetField("judge",Hidden)!.GetValue(records)!;judge.SelectedItem="하드·맥스판정";Wait(400);
    var grid=(RecordsView)typeof(RecordsDialog).GetField("grid",Hidden)!.GetValue(records)!;grid.SelectIndex(0);
    Save(Grab(records),folder,"07-records",notes);
    grid.SelectIndex(Enumerable.Range(0,grid.RowCount).First(i=>grid.ItemAt(i)!.Chart.Key==kamui.Key));records.OpenHistory();
    var history=records.History!;history.Location=new Point(70,50);Wait(500);
    var graph=history.Graph;int pb=Enumerable.Range(0,graph.Points.Count).First(i=>graph.Points[i].Pb);graph.Press(graph.SpotOf(pb));Wait(300);
    notes.Add($"history: 판 {graph.Total}, 점 {graph.Points.Count}, 고른 줄 {history.Table.Picked?.Play.Score}");
    Save(Grab(history),folder,"11-play-history",notes);
    history.Close();records.Close();Wait(300);
   }
   // 09: 오버레이 디자인 설정(미리보기는 기본 예시 곡)
   using(var style=new StyleDialog(BadgeDesign.Defaults,true){StartPosition=FormStartPosition.Manual,Location=new Point(60,40)})
   {
    style.Show(main);Save(Grab(style,1500),folder,"09-design-settings",notes);style.Close();
   }
   // 12: 판독 검수와 캡처 보기(검수 창 위에 캡처 보기 창을 겹친 한 그림)
   if(stills!=null&&Review(catalog,stills,notes) is {} chosen)
   {
    var (result,score)=chosen;
    var temp=new RecordStore(Path.Combine(work,"collection-records.json"));
    using var window=new CollectionWindow(catalog,"기본",temp){FinishToFront=false,TopMost=true,StartPosition=FormStartPosition.Manual,Location=new Point(40,40)};
    window.Show();window.ShowReviewForTest(result);Wait(800);
    var group=window.ReviewForTest!.Groups.First(g=>g.Item==score);var row=group.Rows[0];
    using var viewer=new CaptureViewer(row.Pattern,"—",new[]{new CaptureViewer.Shot(score.Screen!,"점수표",null)}){TopMost=true,StartPosition=FormStartPosition.Manual};
    viewer.Location=new Point(window.Left+Theme.S(250),window.Top+Theme.S(250));viewer.Show(window);Wait(900);
    using var back=Print(window);using var front=Print(viewer);
    var all=Rectangle.Union(window.Bounds,viewer.Bounds);var canvas=new Bitmap(all.Width,all.Height,PixelFormat.Format32bppArgb);
    using(var g=Graphics.FromImage(canvas)){g.Clear(Color.Transparent);g.DrawImage(back,window.Left-all.Left,window.Top-all.Top);g.DrawImage(front,viewer.Left-all.Left,viewer.Top-all.Top);}
    Save(canvas,folder,"12-collection-review",notes);
    notes.Add($"collection-review: 확인할 점수 {score.Judge.Song!.Name} {row.Pattern}");
    viewer.Close();window.Close();
   }
   // 13: 투컴 허용과 송출 PC용 주소(이 PC 안에서 한 번 받아 가 '연결됨'으로)
   {
    var allow=Part<CheckBox>("senderAllow");var status=Part<Label>("senderStatus");var link=Part<SenderLink>("sender");
    allow.Checked=true;bool waiting=Until(()=>status.Text=="연결 대기",4000);
    int Ask(){try{using var client=new System.Net.Sockets.TcpClient();client.Connect(System.Net.IPAddress.Parse("127.0.0.2"),Port);var stream=client.GetStream();stream.ReadTimeout=3000;stream.Write(System.Text.Encoding.ASCII.GetBytes($"GET /o/{link.Settings.Key}/state HTTP/1.1\r\n\r\n"));using var reader=new StreamReader(stream);string? first=reader.ReadLine();return first==null?0:int.Parse(first.Split(' ')[1]);}catch(Exception e)when(e is IOException or System.Net.Sockets.SocketException){return 0;}}
    bool connected=Ask()==200&&Until(()=>status.Text=="연결됨",3000);
    notes.Add($"sender: 연결 대기 {waiting}, 연결됨 {connected}, 주소 칸 '{Part<InputBox>("senderAddress").Box.Text}'");
    var obsName=All(main).OfType<Label>().First(l=>l.Text=="OBS 브라우저");var popup=All(main).OfType<Button>().First(b=>b.Text=="오버레이 팝업");
    var shot=Grab(main,800);
    SaveCut(shot,Around(main,12,4,obsName,popup,Part<Control>("senderRow"),allow,Part<Button>("senderPick")),folder,"13-stream-address",notes);
    allow.Checked=false;Wait(500);
   }
   session.Running=false;
  }
  finally{NetLinks.Source=realLinks;SenderFirewall.Reader=realReader;}
  File.WriteAllLines(Path.Combine(folder,"public-shots.txt"),notes);
 }
}
