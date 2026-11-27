using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
namespace LadderRecords;
// 개발용(Debug 빌드에만 들어간다): 컬렉션 가져오기 창을 보고 돌린다. 개인 기록은 쓰지 않는다. 결과 파일과 그림은 지정한 폴더에 쓰고,
// --collection-scan-window 의 완료 화면에서 적용하면 그 결과 폴더의 시험용 기록(records.json)을 갱신한다(다음 실행이 그 파일을 이어 쓴다).
//
//   DJMAXLocalRecords.exe --collection-window-previews <폴더> [받아 둔 화면의 폴더]
//      창의 단계마다(시작 대기·까닭 있는 시작 대기·첫 곡 찾는 중·점수 읽는 중·중단 셋·허용 수 초과·끝·전곡 읽기 완료·재확인·변경 0건) 라이트·다크 그림을 남긴다.
//      받아 둔 화면의 폴더를 주면 그 화면으로 판독 검수·곡 선택·캡처 보기의 그림도 남긴다(실제 점수가 보이므로 git 밖의 폴더에 둔다)
//   DJMAXLocalRecords.exe --collection-window-check <받아 둔 화면의 폴더> <판독 목록 json> <첫 번호> <끝 번호> <결과 파일>
//      창에 가짜 게임(CollectionScanChecks 의 것)을 물려 실제로 읽기를 돌리고, 끝·중단·취소·허용 수 초과 때 창이 어느 화면이 되는지 본다
//   DJMAXLocalRecords.exe --collection-scan-window <결과 폴더> [front|flash] [견줄 판독 목록 json] [첫 번호] [끝 번호]
//      실제 게임으로 읽는다(사람이 지켜볼 때만 돌린다). 창이 뜨면 게임의 ALL 목록에서 Ctrl+Enter. 끝까지 읽으면 결과 폴더에 읽은 값(result.json),
//      확인이 필요한 곡의 화면(PNG), 견줄 값과의 대조(report.txt)를 남긴다. 완료 화면에서 적용하면
//      결과 폴더의 시험용 기록(records.json)을 갱신한다. 사용자의 점수라 git 밖의 폴더에만 둔다
[SupportedOSPlatform("windows10.0.19041")]
static class CollectionWindowChecks
{
 static ScanResult Fake(ScanEnd end,int items,int questions,string? last,string detail="")=>new(){End=end,Detail=detail,Questions=questions,Allowed=24,LastTitle=last,Seconds=171,
  Items=Enumerable.Range(0,items).Select(_=>new ScanItem{Judge=new SongJudge()}).ToList()};   // 곡 수만 보이려고 채운다(빈 곡)

 public static void Previews(Catalog catalog,string folder,string? stills=null)
 {
  Directory.CreateDirectory(folder);bool dark=Theme.Dark;
  void Save(Form form,string name){for(int i=0;i<20;i++){Application.DoEvents();Thread.Sleep(5);}using var shot=new Bitmap(form.Width,form.Height);form.DrawToBitmap(shot,new Rectangle(Point.Empty,shot.Size));shot.Save(Path.Combine(folder,name+".png"));}
  try
  {
   foreach(bool mode in new[]{true,false})
   {
    Theme.Dark=mode;string end=mode?"-dark":"-light";
    var scenes=new (string Name,Action<CollectionWindow> Set)[]
    {
     ("01-ready",_=>{}),
     ("02-ready-reason",w=>w.ShowReady("다른 창으로 전환되어 가져오기가 취소되었습니다. 다시 시작하면 첫 곡부터 읽습니다.")),
     ("02b-ready-large",w=>w.ShowReady(CollectionWindow.WrongScreen(new Size(2560,1440)))),
     ("02c-ready-not-all",w=>w.ShowReady(CollectionWindow.NotAll)),
     ("02d-ready-keys-held",w=>w.ShowReady(CollectionWindow.KeysStillHeld)),
     ("02e-ready-stop-key",w=>w.ShowReady(CollectionWindow.StopKeyTaken)),
     ("02f-ready-filter-changed",w=>w.ShowReady(CollectionWindow.FilterChanged)),
     ("07b-stop-not-first",w=>w.Done(Fake(ScanEnd.NotFirst,0,0,null))),
     ("03-climbing",w=>w.Climbing()),
     ("04-reading",w=>{w.Climbing();w.Reading(18,"Basement");}),
     ("05-reading-long",w=>{w.Climbing();w.Reading(512,"카트라이더, 크레이지아케이드, 버블파이터 Main theme ~CHUCK Remix~");}),
     ("06-stop-move",w=>w.Done(Fake(ScanEnd.NotMoved,0,0,"Basement"))),
     ("07-stop-empty",w=>w.Done(Fake(ScanEnd.Empty,0,0,null))),
     ("08-stop-pattern",w=>w.Done(Fake(ScanEnd.UnknownPattern,0,0,"Bang!!"))),
     ("09-too-many",w=>{var r=Fake(ScanEnd.TooMany,0,25,"Basement");w.Done(r);}),
     ("10-finished",w=>{var r=Fake(ScanEnd.Done,846,3,"Zeroize");w.Done(r);}),
    };
    foreach(var (name,set) in scenes)
    {
     using var window=new CollectionWindow(catalog,"기본"){FinishToFront=false};
     window.Show();set(window);Save(window,name+end);
     window.Close();
    }
    // 전곡 읽기 완료: 목록은 Windows 의 목록(ListView)이라 창을 그림으로 옮기면 줄이 빠진다. 실제 화면을 찍는다
    var done=new (string Name,Action<CollectionWindow,RecordStore,Play>? Act)[]
    {
     ("11-complete",(_,_,_)=>{}),
     ("12-complete-higher",(w,_,_)=>{w.HeaderFor(DoneKind.Higher).Flip();w.TableFor(DoneKind.Higher).ToggleAt(0);}),
     ("13-complete-auto",(w,_,_)=>w.HeaderFor(DoneKind.Auto).Flip()),
     ("14-complete-adds",(w,_,_)=>w.AddFold.Flip()),
     ("15-recheck",(w,s,p)=>{w.HeaderFor(DoneKind.Higher).Flip();w.TableFor(DoneKind.Higher).ToggleAt(0);w.TableFor(DoneKind.Higher).ToggleAt(1);s.Edit(p.Seq,p.Score+200);w.Apply();}),
     ("16-nothing",null),
    };
    foreach(var (name,act) in done)
    {
     var (store,outcome,edit)=Sample(catalog,Path.Combine(Path.GetTempPath(),"djmax-done-previews-"+Guid.NewGuid().ToString("N")),act==null);
     using var window=new CollectionWindow(catalog,"기본",store){FinishToFront=false,TopMost=true};
     window.Show();window.ShowDoneForTest(outcome);act?.Invoke(window,store,edit);Shoot(window,Path.Combine(folder,name+end+".png"));
     File.AppendAllText(Path.Combine(folder,"fit.txt"),$"{name}{end}: {window.LastFit}"+Environment.NewLine);
     window.Close();
    }
    // 판독 검수: 받아 둔 실제 화면(stills)이 있으면 그 화면으로 확인할 것을 만들어 찍는다
    string why="";var sample=stills==null?null:ReviewSample(catalog,stills,out why);
    if(stills!=null)File.AppendAllText(Path.Combine(folder,"fit.txt"),$"판독 검수 그림: {(sample==null?"만들지 못함 "+why:sample.Items.Count+"곡")}"+Environment.NewLine);
    if(sample!=null)
    {
     var review=new (string Name,Action<CollectionWindow,ScanResult> Act)[]
     {
      ("17-review",(_,_)=>{}),
      ("18-review-notes",(w,r)=>{var g=w.ReviewForTest!.Groups.First(x=>x.NeedsSong&&x.Candidates.Count>0);w.Choose(g,r.Items.First(i=>i.Questions==0).Judge.Song);var line=w.LinesForTest.FirstOrDefault(l=>l.Input!=null);if(line!=null)line.Input!.Box.Text="0";}),
     };
     foreach(var (name,act) in review)
     {
      var store=new RecordStore(Path.Combine(Directory.CreateDirectory(Path.Combine(Path.GetTempPath(),"djmax-review-previews-"+Guid.NewGuid().ToString("N"))).FullName,"records.json"));
      using var window=new CollectionWindow(catalog,"기본",store){FinishToFront=false,TopMost=true};
      window.Show();window.ShowReviewForTest(sample);act(window,sample);Shoot(window,Path.Combine(folder,name+end+".png"));
      // 곡 선택 창과 캡처 보기 창(이 장면에서 한 번)
      if(name=="17-review")
      {
       var group=window.ReviewForTest!.Groups.First(x=>x.NeedsSong&&x.Candidates.Count>0);
       using(var picker=new SongPicker(catalog,group.Candidates,null){TopMost=true}){picker.Show(window);Shoot(picker,Path.Combine(folder,"19-picker"+end+".png"));picker.Search();picker.SearchBox.Text="al";Shoot(picker,Path.Combine(folder,"20-picker-search"+end+".png"));picker.Close();}
       var withArtist=window.ReviewForTest.Groups.First(x=>x.Item.ArtistScreen!=null);var row=withArtist.Rows[0];
       var shots=new List<CaptureViewer.Shot>{new(withArtist.Item.Screen!,"점수표",null),new(withArtist.Item.ArtistScreen!,"다음 곡으로 이동 후 · 오른쪽 곡 목록","이 장면의 점수표는 다른 곡입니다.")};
       using(var viewer=new CaptureViewer(row.Pattern,row.Kind==ReviewKind.Song&&int.TryParse(row.Read,out int v)?$"{v:N0}":"—",shots){TopMost=true})
       {
        viewer.Show(window);Shoot(viewer,Path.Combine(folder,"21-capture"+end+".png"));
        viewer.ClickAt(.62f,.46f);Shoot(viewer,Path.Combine(folder,"22-capture-zoom"+end+".png"));
        viewer.ClickAt(.5f,.5f);viewer.Go(1);Shoot(viewer,Path.Combine(folder,"23-capture-artist"+end+".png"));viewer.Close();
       }
      }
      window.Close();
     }
    }
   }
  }
  finally{Theme.Dark=dark;}
 }

 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool IsWindowEnabled(IntPtr window);
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr window,int message,IntPtr w,IntPtr l);
 /// <summary>
 /// --collection-main-check &lt;받아 둔 화면의 폴더&gt; &lt;판독 목록 json&gt; 133 985 &lt;결과 파일&gt;: 메인 창에서 컬렉션 가져오기를 연다. 단추의 자리와 모양, 감지 중이면 열지 않음,
 /// 모달로 떠서 메인 창이 막힘, 그동안 전역 단축키를 받지 않음, 가짜 게임으로 끝까지 읽고 적용하면 상태 줄의 건수, 되돌리기 확인의 글, 적용하지 않고 닫으면 상태 줄이 그대로,
 /// 단축키 설정 창의 구역. 빈 기록에서만 돈다(새 시험 폴더에 Debug 빌드를 복사해서 돌린다. 창이 여러 번 뜬다)
 /// </summary>
 public static void MainCheck(Catalog catalog,string folder,string listPath,int first,int last,string reportPath)
 {
  var data=CollectionData.Load();var part=CollectionScanChecks.Load(catalog,folder,listPath,first,last).GetRange(0,40);
  var notes=new List<string>();int failed=0;void Check(bool ok,string name){if(!ok)failed++;notes.Add((ok?"PASS: ":"FAIL: ")+name);}
  void Pump(int n=10){for(int i=0;i<n;i++){Application.DoEvents();Thread.Sleep(5);}}
  using(var main=new MainForm(catalog,18779,false){StartPosition=FormStartPosition.Manual,Location=new Point(60,40)})
  {
   main.Show();Pump(20);
   var store=main.StoreForTest;
   if(store.Data.Profiles.Any(p=>p.Plays.Count>0)){notes.Add("FAIL: 빈 기록에서만 돈다(새 시험 폴더에 Debug 빌드를 복사해서 돌린다)");File.WriteAllLines(reportPath,notes);Environment.ExitCode=2;return;}
   var button=main.CollectionButton!;var archive=button.Parent!.Controls.OfType<Button>().FirstOrDefault(b=>b.Text=="V아카이브 가져오기");
   Check(button.Visible&&button.Text=="컬렉션 가져오기"&&(string?)button.Tag=="default"&&button.Font.Bold&&archive!=null&&button.Right<=archive.Left,"메인 창 '가져오기' 줄의 왼쪽에 강조한 '컬렉션 가져오기'(V아카이브 가져오기 앞)");
   // 감지 중이면 열지 않고 상태 줄에 까닭
   bool opened=false;main.CollectionForTest=_=>opened=true;
   main.SetDetectingForTest(true);main.OpenCollectionForTest();string said=main.StatusText;main.SetDetectingForTest(false);
   Check(!opened&&!main.CollectionOpen&&said==MainForm.CollectionWhileDetecting,$"감지 중이면 열지 않고 상태 줄에 까닭 [{said}]");
   // 연다: 모달, 단축키 무시, 끝까지 읽고 적용
   string title="",statusBefore="";bool modal=false,ignored=false;var end=CollectionWindow.Stage.Ready;
   main.CollectionForTest=w=>
   {
    w.FinishToFront=false;w.WorldForTest=()=>new CollectionScanChecks.FakeGame(part,data,20);title=w.Text;
    w.Shown+=(_,_)=>w.BeginInvoke(()=>
    {
     Pump();modal=main.CollectionOpen&&!IsWindowEnabled(main.Handle);
     statusBefore=main.StatusText;SendMessage(main.Handle,0x0312,(IntPtr)106,IntPtr.Zero);Pump();ignored=!main.DetectingForTest&&main.StatusText==statusBefore;   // 106: 다섯째 단축키(감지 시작)
     w.Start();var clock=System.Diagnostics.Stopwatch.StartNew();
     while(w.Now is CollectionWindow.Stage.Climbing or CollectionWindow.Stage.Reading&&clock.Elapsed.TotalMinutes<10){Application.DoEvents();Thread.Sleep(2);}
     Pump();end=w.Now;
     if(end==CollectionWindow.Stage.Complete)w.Apply();else w.Close();
    });
   };
   main.OpenCollectionForTest();Pump(20);
   int rows=store.Active.Plays.Count(p=>p.Source==PlaySource.Collection);
   Check(title==$"{CollectionWindow.Title} · {store.Active.Name}"&&modal,$"메인 창에서 연 창은 지금 프로필 이름을 달고 모달로 뜬다(메인 창이 막혀 프로필을 바꿀 수 없다) [{title}]");
   Check(ignored,"창이 떠 있는 동안 메인 창의 전역 단축키(감지 시작)를 받지 않는다(상태 줄도 그대로)");
   Check(end==CollectionWindow.Stage.Complete&&!main.CollectionOpen&&rows>0&&store.Active.Undo.Count>0&&store.Active.Undo[^1].Kind==PlayOps.ImportCollection&&main.StatusText==$"컬렉션 기록을 가져왔습니다(추가 {rows:N0}건, 갱신 0건, 삭제 0건).",
    $"적용하면 창이 닫히고 상태 줄에 건수 [{main.StatusText}]");
   if(store.Active.Undo.Count>0)
   {
    var view=UndoView.Of(store.Active.Undo[^1],store.Active.Name);
    Check(view is {Question:"이번 가져오기를 되돌릴까요?",Work:"컬렉션 가져오기"}&&view.Counts.SequenceEqual(new[]{("삭제할 새 기록",rows)}),$"되돌리기 확인은 컬렉션 가져오기의 질문과 건수 [{string.Join(", ",view.Counts)}]");
    store.Undo();
   }
   Check(store.Active.Plays.Count==0,"되돌리기 한 번으로 빈 기록");
   var diag=Path.Combine(AppContext.BaseDirectory,"diagnostics");
   List<string> Scans()=>Directory.Exists(diag)?Directory.GetFiles(diag,"score-*.log").SelectMany(File.ReadAllLines).Where(l=>l.Contains("\"kind\":\"collection-scan\"")).ToList():new List<string>();
   // 진단 한 줄은 읽기의 스레드에서 와서 메인 창의 차례에 적힌다: 메시지를 돌리며 기다린다
   List<string> WaitScans(int count){var clock=System.Diagnostics.Stopwatch.StartNew();var got=Scans();while(got.Count<count&&clock.Elapsed.TotalSeconds<15){Application.DoEvents();Thread.Sleep(20);got=Scans();}return got;}
   var scans=WaitScans(1);
   Check(scans.Count==1&&scans[0].Contains("\"end\":\"Done\"")&&scans[0].Contains("\"songs\":40")&&!scans[0].Contains("Score"),$"읽기가 끝나면 진단 기록에 한 줄이 남는다 [{string.Join(" / ",scans)}]");
   // 적용하지 않고 닫으면 상태 줄을 바꾸지 않는다
   string kept=main.StatusText;main.CollectionForTest=w=>w.Shown+=(_,_)=>w.BeginInvoke(w.Close);main.OpenCollectionForTest();Pump();
   Check(main.StatusText==kept&&!main.CollectionOpen,"적용하지 않고 닫으면 상태 줄이 그대로");
   // 읽는 중에 창을 닫아도 진단 한 줄. 시작하자마자(첫 곡을 찾는 중) 닫는다.
   // 가짜 게임은 창을 닫아도 멈춤 요청을 받지 않아(실제 게임 쪽 객체만 받는다) 읽기가 끝까지 간다: 끝난 종류는 Done 이 맞고, 실제 게임에서는 Cancelled 다.
   // 가르는 것은 줄이 창을 닫은 뒤에 적혔는가다(닫힌 창이 결과 처리를 건너뛰면 줄이 남지 않는다)
   DateTime closedAt=DateTime.MaxValue;
   main.CollectionForTest=w=>{w.FinishToFront=false;w.WorldForTest=()=>new CollectionScanChecks.FakeGame(part,data,20);w.Shown+=(_,_)=>w.BeginInvoke(()=>{w.Start();w.Close();closedAt=DateTime.Now;});};
   main.OpenCollectionForTest();var closed=WaitScans(2);
   DateTime At(string line){int i=line.IndexOf("\"at\":\"")+6;return DateTime.ParseExact(line.Substring(i,23),"yyyy-MM-dd HH:mm:ss.fff",System.Globalization.CultureInfo.InvariantCulture);}
   Check(closed.Count==2&&At(closed[1])>=closedAt.AddMilliseconds(-1)&&!main.CollectionOpen,$"읽기를 시작하자마자 창을 닫아도, 읽기가 끝난 뒤 진단 기록에 한 줄이 남는다(닫은 {closedAt:HH:mm:ss.fff}) [{(closed.Count>1?closed[1]:"없음")}]");
   // 시작 키를 누른 채 멈춰도 허용 수를 적는다
   main.CollectionForTest=w=>{w.FinishToFront=false;w.WorldForTest=()=>new CollectionScanChecks.FakeGame(part,data,20);w.KeysHeldForTest=()=>true;w.Shown+=(_,_)=>w.BeginInvoke(()=>{w.Start();WaitScans(3);w.Close();});};
   main.OpenCollectionForTest();var held=WaitScans(3);
   Check(held.Count==3&&held[2].Contains("\"end\":\"KeysHeld\"")&&held[2].Contains("\"allowed\":24"),$"시작 키를 누른 채 멈춰도 진단 기록의 허용 수가 24 다 [{(held.Count>2?held[2]:"없음")}]");
   main.CollectionForTest=null;main.Close();
  }
  // 단축키 설정 창의 '컬렉션 가져오기' 구역(읽기 전용)
  using(var hotkeys=new HotkeyDialog(Hotkeys.Defaults()))
  {
   var texts=hotkeys.CollectionKeyTexts;
   Check(new[]{"가져오기 시작","Ctrl","+","Enter","읽기 중지","Backspace"}.All(texts.Contains)&&texts.Count==6,$"단축키 설정 창의 '컬렉션 가져오기' 구역: 가져오기 시작 Ctrl+Enter, 읽기 중지 Backspace [{string.Join(" ",texts)}]");
  }
  notes.Add(failed==0?"모두 통과":$"실패 {failed}");File.WriteAllLines(reportPath,notes);if(failed>0)Environment.ExitCode=2;
 }
 /// <summary>창에 가짜 게임을 물려 읽기를 끝까지 돌린다. 창의 스레드에서 메시지를 돌리며 끝나기를 기다린다</summary>
 public static void Check(Catalog catalog,string folder,string listPath,int first,int last,string reportPath)
 {
  var data=CollectionData.Load();var all=CollectionScanChecks.Load(catalog,folder,listPath,first,last);var part=all.GetRange(0,40);
  var notes=new List<string>();int failed=0;void Check(bool ok,string name){if(!ok)failed++;notes.Add((ok?"PASS: ":"FAIL: ")+name);}
  (CollectionWindow.Stage Stage,ScanResult? Result,string Shown,CollectionScanChecks.FakeGame Game,int Updates) Run(List<CollectionScanChecks.FakeSong> songs,int start,Action<CollectionScanChecks.FakeGame>? script=null,bool held=false)
  {
   var game=new CollectionScanChecks.FakeGame(songs,data,start);script?.Invoke(game);
   using var window=new CollectionWindow(catalog,"기본"){FinishToFront=false,WorldForTest=()=>game,KeysHeldForTest=held?()=>true:null};
   ScanResult? got=null;window.Finished+=r=>got=r;
   window.Show();for(int i=0;i<10;i++){Application.DoEvents();Thread.Sleep(5);}
   window.Start();int updates=0;var seen=new HashSet<string>();
   var clock=System.Diagnostics.Stopwatch.StartNew();
   while(window.Now is CollectionWindow.Stage.Climbing or CollectionWindow.Stage.Reading&&clock.Elapsed.TotalMinutes<10){Application.DoEvents();Thread.Sleep(2);string text=Texts(window);if(seen.Add(text))updates++;}
   for(int i=0;i<10;i++){Application.DoEvents();Thread.Sleep(5);}
   string shown=Texts(window);var stage=window.Now;window.Close();
   return(stage,got,shown,game,updates);
  }
  var (stage,result,shown,game,updates)=Run(part,20);
  Check(stage==CollectionWindow.Stage.Finished&&result!=null&&result.Items.Count==part.Count&&result.Questions==0&&shown.Contains($"{part.Count}곡을 읽었습니다."),$"40곡을 끝까지 읽으면 끝 화면이 되고 결과를 넘긴다(화면이 바뀐 횟수 {updates}) [{Short(shown)}]");
  (stage,result,shown,game,_)=Run(part,0,g=>g.LostKey.Add(20));
  Check(stage==CollectionWindow.Stage.Stopped&&result==null&&shown.Contains("다음 곡으로 이동했는지 확인할 수 없습니다.")&&shown.Contains("마지막으로 확인한 곡")&&shown.Contains(part[20].Song.Name),$"↓ 가 먹지 않으면 중단 화면에 마지막으로 확인한 곡 '{part[20].Song.Name}' 을 보인다 [{Short(shown)}]");
  (stage,result,shown,game,_)=Run(part,0,g=>g.StopAt=15);
  Check(stage==CollectionWindow.Stage.Ready&&result==null&&shown.Contains("읽기를 중지했습니다.")&&!game.AnyHeld,$"중지하면 시작 대기로 돌아가 까닭을 보인다 [{Short(shown)}]");
  (stage,result,shown,game,_)=Run(part,0,g=>g.InactiveAt=15);
  Check(stage==CollectionWindow.Stage.Ready&&shown.Contains("다른 창으로 전환되어")&&g(game),$"창을 바꾸면 시작 대기로 돌아가 까닭을 보인다 [{Short(shown)}]");
  static bool g(CollectionScanChecks.FakeGame x)=>x.KeysWhileInactive==0&&!x.AnyHeld;
  (stage,result,shown,game,_)=Run(part,0,x=>{for(int i=3;i<part.Count;i++){int cell=Enumerable.Range(0,16).FirstOrDefault(c=>part[i].Cell(c).Length>1,-1);if(cell>=0)x.Cover[i]=(cell,0);}});
  Check(stage==CollectionWindow.Stage.TooMany&&shown.Contains("확인 필요 25건(24건까지만 넘어가며 읽습니다)")&&shown.Contains("곡 확인 0건 · 점수 확인 25건"),$"확인할 것이 허용 수를 넘으면 그 건수를 보인다 [{Short(shown)}]");
  (stage,result,shown,game,_)=Run(part,5,x=>x.NoHeader=true);
  Check(stage==CollectionWindow.Stage.Ready&&shown.Contains("MUSIC DATA 화면을 찾지 못해")&&!shown.Contains("해상도")&&game.Downs+game.Ups==0,$"MUSIC DATA 화면이 아니면 키를 보내지 않고 시작 대기에 까닭을 보인다 [{Short(shown)}]");
  string large=CollectionWindow.WrongScreen(new Size(2560,1440));   // 가짜 게임은 화면을 줄이지 않는다: 글만 본다
  Check(large.Contains("ALL 목록을 열어 주세요")&&large.Contains("게임 설정에서 해상도를 1920×1080으로 바꿔 주세요")&&!large.Contains("2560×1440"),$"큰 16:9 를 줄여 읽은 화면이 MUSIC DATA 로 보이지 않으면 해상도도 안내한다 [{Short(large)}]");
  (stage,result,shown,game,_)=Run(part,5,x=>{x.NoList=true;x.NoSong=true;});
  Check(stage==CollectionWindow.Stage.Stopped&&shown.Contains("게임의 ALL 목록에서 곡을 찾지 못했습니다.")&&!shown.Contains("마지막으로 확인한 곡"),$"목록이 비었으면 중단 화면(곡 정보 줄 없음) [{Short(shown)}]");
  (stage,result,shown,game,_)=Run(part,5,x=>x.NoList=true);
  Check(stage==CollectionWindow.Stage.Stopped&&shown.Contains("첫 곡으로 이동했는지 확인할 수 없습니다.")&&!shown.Contains("곡을 찾지 못했습니다"),$"곡은 보이는데 고른 줄이 없으면 첫 곡을 모른다는 중단 화면 [{Short(shown)}]");
  // 시작하지 않는 경우(필터가 ALL 이 아님, 시작 키를 누르고 있음, 중지 단축키를 켤 수 없음): 키를 보내지 않고 시작 대기에 까닭을 보인다.
  // 읽는 동안 필터가 바뀐 때도 시작 대기로 돌아가 까닭을 보인다
  (stage,result,shown,game,_)=Run(part,5,x=>x.NoFilter=true);
  Check(stage==CollectionWindow.Stage.Ready&&shown.Contains(CollectionWindow.NotAll)&&game.Downs+game.Ups==0,$"고른 필터가 ALL 이 아니면 시작 대기에 까닭을 보인다 [{Short(shown)}]");
  (stage,result,shown,game,_)=Run(part,0,x=>x.FilterOffAt=10);
  Check(stage==CollectionWindow.Stage.Ready&&result==null&&shown.Contains(CollectionWindow.FilterChanged)&&!game.AnyHeld,$"읽는 동안 필터가 바뀌면 시작 대기로 돌아가 까닭을 보인다 [{Short(shown)}]");
  (stage,result,shown,game,_)=Run(part,5,held:true);
  Check(stage==CollectionWindow.Stage.Ready&&shown.Contains(CollectionWindow.KeysStillHeld)&&game.Downs+game.Ups==0,$"시작 키를 한도가 지나도록 누르고 있으면 키를 보내지 않고 시작 대기에 까닭을 보인다 [{Short(shown)}]");
  {
   // 다른 창이 Backspace 를 먼저 쥐고 있게 한다(이 검사 동안만)
   using var blocker=new Form();var taken=new ScanHotkeys(blocker.Handle);bool grabbed=taken.SetStop(true);
   try{(stage,result,shown,game,_)=Run(part,5);}finally{taken.Dispose();}
   Check(grabbed&&stage==CollectionWindow.Stage.Ready&&shown.Contains(CollectionWindow.StopKeyTaken)&&game.Captures==0&&game.Downs+game.Ups==0,$"중지 단축키를 켜지 못하면 읽기를 시작하지 않고 시작 대기에 까닭을 보인다 [{Short(shown)}]");
   (stage,result,shown,game,_)=Run(part,20);
   Check(stage==CollectionWindow.Stage.Finished,$"그 프로그램이 키를 놓으면 다시 시작할 수 있다 [{Short(shown)}]");
  }
  int other=Enumerable.Range(0,part.Count).First(i=>Enumerable.Range(0,16).Any(c=>(part[i].Cell(c)=="")!=(part[12].Cell(c)=="")));
  (stage,result,shown,game,_)=Run(part,0,x=>x.TableOf[12]=other);
  Check(stage==CollectionWindow.Stage.Stopped&&shown.Contains("프로그램의 곡 목록에 없는 패턴이 있습니다.")&&shown.Contains(data.TitleOf(part[12].Song))&&shown.Contains("업데이트 확인"),$"앱에 없는 패턴이면 그 곡과 업데이트 확인을 보인다 [{Short(shown)}]");
  // ── 전곡 읽기 완료: 가짜 게임으로 끝까지 읽은 뒤 완료 화면, 고르고 적용, 재확인, 저장 실패, 변경 0건, 취소 ──
  {
   string dir=Path.Combine(Path.GetTempPath(),"djmax-done-"+Guid.NewGuid().ToString("N"));
   RecordStore NewStore(string name){string folder=Path.Combine(dir,name);Directory.CreateDirectory(folder);return new RecordStore(Path.Combine(folder,"records.json"));}
   Selection Key(CollectionScanChecks.FakeSong song,int cell)=>new(song.Song.Id,CollectionScreen.Buttons[cell%4],CollectionScreen.Patterns[cell/4],"노말",0);
   int Cell(CollectionScanChecks.FakeSong song,Func<string,bool> want)=>Enumerable.Range(0,16).First(i=>want(song.Cell(i)));
   bool Number(string s)=>s.Length>1&&s.All(char.IsAsciiDigit);bool Score(string s)=>Number(s)&&int.Parse(s)<999000;   // 높은 판을 그 위에 둘 수 있는 점수
   // 끝까지 읽는 40곡의 칸으로 기록을 꾸민다: V아카이브 한 줄, 점수 칸보다 높은 수동 판, '-' 칸의 예전 컬렉션 줄
   var store=NewStore("scan");
   var withScore=part.Where(s=>Enumerable.Range(0,16).Any(i=>Score(s.Cell(i)))).ToList();var withNone=part.Where(s=>Enumerable.Range(0,16).Any(i=>s.Cell(i)=="-")).ToList();
   var vKey=Key(withScore[0],Cell(withScore[0],Score));store.Import(new Dictionary<Selection,decimal>{[vKey]=90.00m});
   var hKey=Key(withScore[1],Cell(withScore[1],Score));int hRead=int.Parse(withScore[1].Cell(Cell(withScore[1],Score)));var high=store.Add(hKey,Math.Min(1000000,hRead+1),new DateTime(2026,11,1,0,0,0,DateTimeKind.Utc),PlaySource.Manual);
   var nKey=Key(withNone[0],Cell(withNone[0],s=>s=="-"));var profile=store.Active;profile.Plays.Add(new Play(profile.NextSeq++,nKey.Key,950000,null,PlaySource.Collection));store.Save();
   int scoreCells=part.Sum(s=>Enumerable.Range(0,16).Count(i=>Number(s.Cell(i))));
   CollectionSaveResult? applied=null;
   using(var window=new CollectionWindow(catalog,"기본",store){FinishToFront=false,WorldForTest=()=>new CollectionScanChecks.FakeGame(part,data,20)})
   {
    window.Applied+=r=>applied=r;window.Show();for(int i=0;i<10;i++){Application.DoEvents();Thread.Sleep(5);}
    window.Start();var clock=System.Diagnostics.Stopwatch.StartNew();
    while(window.Now is CollectionWindow.Stage.Climbing or CollectionWindow.Stage.Reading&&clock.Elapsed.TotalMinutes<10){Application.DoEvents();Thread.Sleep(2);}
    for(int i=0;i<10;i++){Application.DoEvents();Thread.Sleep(5);}
    string text=window.DoneText;var higher=window.HeaderFor(DoneKind.Higher);
    Check(window.Now==CollectionWindow.Stage.Complete&&text.Contains($"추가 {scoreCells}건")&&text.Contains("삭제 예정 2건")&&window.HeaderFor(DoneKind.VArchive).Visible&&window.HeaderFor(DoneKind.Auto).Visible&&!window.HeaderFor(DoneKind.OldRate).Visible&&higher.Visible&&higher.Count==1&&higher.Check==CheckState.Unchecked&&window.BackFromDone is {Visible:false},
     $"끝까지 읽고 확인할 것이 없으면 전곡 읽기 완료: 추가 {scoreCells}건, 삭제 예정 2건(V아카이브·예전 컬렉션), 높은 노말 1건은 고르지 않음, 대상이 없는 항목은 숨김 [{text}]");
    higher.Flip();window.TableFor(DoneKind.Higher).ToggleAt(0);
    Check(window.OpenListForTest==(int)DoneKind.Higher&&window.DoneText.Contains("삭제 예정 3건")&&higher.Check==CheckState.Checked,$"항목을 펴고 줄을 고르면 삭제 예정이 3건 [{window.DoneText}]");
    window.HeaderFor(DoneKind.VArchive).Tick();
    Check(window.DoneText.Contains("삭제 예정 2건")&&window.HeaderFor(DoneKind.VArchive).Check==CheckState.Unchecked,$"V아카이브 삭제를 풀면 2건 [{window.DoneText}]");
    window.HeaderFor(DoneKind.VArchive).Tick();window.Apply();for(int i=0;i<10;i++){Application.DoEvents();Thread.Sleep(5);}
    Check(applied is {State:CollectionSave.Saved,Deleted:3}&&applied.Added==scoreCells&&window.IsDisposed&&!store.Active.Plays.Contains(high)&&!store.Active.Plays.Any(p=>p.Source==PlaySource.VArchive)&&store.Active.Plays.Count(p=>p.Source==PlaySource.Collection)==scoreCells,
     $"적용하면 한 번에 저장하고 창을 닫는다(추가 {applied?.Added}, 삭제 {applied?.Deleted})");
    store.Undo();
    Check(store.Active.Plays.Contains(high)&&store.Active.Plays.Count(p=>p.Source==PlaySource.VArchive)==1&&store.Active.Plays.Count(p=>p.Source==PlaySource.Collection)==1,"되돌리기 한 번으로 적용 전으로 돌아간다");
   }
   // 재확인·저장 실패·변경 0건·취소는 읽기 없이(같은 기록, 같은 읽은 값)
   CollectionOutcome For(RecordStore s){var o=new CollectionOutcome{ProfileId=s.Active.Id};o.Seen.UnionWith(catalog.Songs.Select(x=>x.Id));
    foreach(var song in part)for(int i=0;i<16;i++){string cell=song.Cell(i);if(cell=="")continue;o.Values[Key(song,i).Key]=cell=="-"?null:int.Parse(cell);}return o;}
   (CollectionWindow.Stage Stage,string Text,Guide? Said) Done(RecordStore s,Action<CollectionWindow> act)
   {
    Guide? said=null;var hook=GuideDialog.Hook;GuideDialog.Hook=g=>{said=g;return 0;};
    try
    {
     using var window=new CollectionWindow(catalog,"기본",s){FinishToFront=false};window.Show();for(int i=0;i<10;i++){Application.DoEvents();Thread.Sleep(5);}
     window.ShowDoneForTest(For(s));act(window);for(int i=0;i<10;i++){Application.DoEvents();Thread.Sleep(5);}
     var stage=window.IsDisposed?(CollectionWindow.Stage)(-1):window.Now;string text=window.IsDisposed?"":window.DoneText+" | "+Texts(window);if(!window.IsDisposed)window.Close();return(stage,text,said);
    }
    finally{GuideDialog.Hook=hook;}
   }
   // 재확인: 고른 수동 판의 점수가 검수 중에 바뀌면 저장하지 않고 띠를 보이며, 그 줄의 체크만 풀린다
   var low=store.Add(hKey,Math.Min(1000000,hRead+2),new DateTime(2026,11,2,0,0,0,DateTimeKind.Utc));   // 같은 패턴의 두 번째 높은 판
   var r1=Done(store,w=>{w.HeaderFor(DoneKind.Higher).Flip();w.TableFor(DoneKind.Higher).ToggleAt(0);w.TableFor(DoneKind.Higher).ToggleAt(1);store.Edit(high.Seq,Math.Min(1000000,hRead+3));w.Apply();});
   Check(r1.Stage==CollectionWindow.Stage.Complete&&r1.Text.Contains("검수 중 기록 1건이 바뀌었습니다.")&&r1.Text.Contains("바뀐 점수를 확인한 뒤"),$"검수 중에 고른 판의 점수가 바뀌면 저장하지 않고 띠를 보인다 [{Short(r1.Text)}]");
   var r2=Done(store,w=>{w.HeaderFor(DoneKind.Higher).Flip();w.TableFor(DoneKind.Higher).ToggleAt(0);w.TableFor(DoneKind.Higher).ToggleAt(1);var edited=store.Active.Plays.Single(p=>p.Seq==high.Seq);store.Edit(high.Seq,edited.Score==1000000?999999:edited.Score+1);w.Apply();
    var rows=w.TableFor(DoneKind.Higher).Rows;var changed=rows.Single(x=>x.Pick?.Seq==high.Seq);var kept=rows.Single(x=>x.Pick?.Seq==low.Seq);
    Check(w.OpenListForTest==(int)DoneKind.Higher&&changed.Was!=null&&!w.DoneChoices.Higher.Contains(changed.Pick!)&&w.DoneChoices.Higher.Contains(kept.Pick!),$"바뀐 줄이 든 항목이 펼쳐지고, 바뀐 줄의 체크만 풀린다(같은 패턴의 다른 줄은 그대로) [{changed.Was}]");
    w.Apply();});
   Check(r2.Stage==(CollectionWindow.Stage)(-1)&&!store.Active.Plays.Any(p=>p.Seq==low.Seq)&&store.Active.Plays.Any(p=>p.Seq==high.Seq),"재확인 뒤 다시 적용하면 남은 선택대로 저장된다");
   store.Undo();
   // 저장 실패: 안내('저장 실패', '돌아가기')를 띄우고 완료 화면과 고른 것이 남는다
   var r3=Done(store,w=>{w.HeaderFor(DoneKind.VArchive).Tick();store.Frozen=true;w.Apply();store.Frozen=false;});
   Check(r3.Stage==CollectionWindow.Stage.Complete&&r3.Said is {Headline:"저장 실패"}&&r3.Said.ButtonTexts.SequenceEqual(new[]{"돌아가기"})&&r3.Said.Plain().Contains("기존 기록은 그대로입니다.")&&r3.Said.Plain().Contains("‘적용’을 다시 눌러 주세요."),$"저장하지 못하면 안내를 띄우고 완료 화면에 남는다 [{r3.Said?.Plain().Replace('\n',' ')}]");
   // 변경 0건과 취소
   var same=NewStore("same");var applySame=Done(same,w=>w.Apply());
   var r4=Done(same,_=>{});
   Check(applySame.Stage==(CollectionWindow.Stage)(-1)&&r4.Stage==CollectionWindow.Stage.Nothing&&r4.Text.Contains("변경할 기록이 없습니다."),$"같은 결과를 다시 가져오면 '변경할 기록이 없습니다.' [{Short(r4.Text)}]");
   var r5=Done(store,w=>w.CancelDone());
   Check(r5.Stage==CollectionWindow.Stage.Ready,"완료 화면의 취소는 준비 화면으로");
   // 긴 목록: 재확인은 바뀐 줄이 보이게 굴리고, 창은 작업 영역 안에 들고, 줄의 글에 삭제 선택이 있다
   {
    var many=NewStore("many");var picks=new List<Play>();var t0=new DateTime(2026,11,3,0,0,0,DateTimeKind.Utc);
    foreach(var song in part)for(int i=0;i<16&&picks.Count<24;i++){string cell=song.Cell(i);if(Score(cell))picks.Add(many.Add(Key(song,i),int.Parse(cell)+1,t0.AddMinutes(picks.Count),PlaySource.Manual));}
    var r6=Done(many,w=>
    {
     w.HeaderFor(DoneKind.Higher).Flip();var table=w.TableFor(DoneKind.Higher);table.ToggleAt(0);table.ToggleAt(picks.Count-2);
     Check(table.Spoken(table.Rows[0]).EndsWith(", 삭제 선택됨")&&table.Spoken(table.Rows[1]).EndsWith(", 삭제 선택 안 됨"),$"줄의 글에 삭제 선택 상태가 있다(화면 읽기 프로그램) [{table.Spoken(table.Rows[0])}]");
     var held=table.ItemAt(0);table.ToggleAt(0);bool off=ReferenceEquals(held,table.ItemAt(0))&&held.Text.EndsWith(", 삭제 선택 안 됨");table.ToggleAt(0);
     Check(off&&held.Text.EndsWith(", 삭제 선택됨"),"이미 잡고 있는 줄도 체크를 바꾸면 새 글이 된다(같은 항목)");
     var target=table.Rows[table.Rows.Count-2].Pick!;many.Edit(target.Seq,target.Score+1);w.Apply();for(int i=0;i<20;i++){Application.DoEvents();Thread.Sleep(10);}
     int at=table.Rows.ToList().FindIndex(x=>x.Was!=null);var r=table.GetItemRect(at);var area=Screen.FromControl(w).WorkingArea;
     Check(at>=0&&r.Top>=0&&r.Bottom<=table.ClientSize.Height,$"재확인에서 긴 목록의 바뀐 줄({at+1}번째)이 보이게 굴린다");
     Check(w.Bounds.Bottom<=area.Bottom&&w.Bounds.Top>=area.Top,$"재확인 창이 작업 영역 안에 든다(창 {w.Bounds.Top}~{w.Bounds.Bottom}, 작업 영역 {area.Top}~{area.Bottom}, 배율 {Theme.Scale})");
     Check(table.Spoken(table.Rows[at]).Contains("검수 당시")&&table.Spoken(table.Rows[at]).Contains("현재"),$"바뀐 줄의 글에 검수 당시와 현재 값 [{table.Spoken(table.Rows[at])}]");
    });
    // 열려 있던 항목이 사라지면 표도 숨는다: 높은 기록이 V아카이브에서만 나오는 기록
    var onlyV=NewStore("only-v");var vk=Key(withScore[2],Cell(withScore[2],Score));onlyV.Import(new Dictionary<Selection,decimal>{[vk]=99.99m});
    var r7=Done(onlyV,w=>
    {
     w.HeaderFor(DoneKind.VArchive).Tick();var higher=w.HeaderFor(DoneKind.Higher);bool shown=higher.Visible;higher.Flip();bool open=w.TableFor(DoneKind.Higher).Visible;
     w.HeaderFor(DoneKind.VArchive).Tick();
     Check(shown&&open&&!higher.Visible&&!w.TableFor(DoneKind.Higher).Visible&&w.OpenListForTest==-1,"V아카이브 삭제를 다시 고르면 높은 기록 항목과 그 표가 함께 숨는다");
     w.HeaderFor(DoneKind.VArchive).Tick();bool closed=higher.Visible&&!higher.Open&&!w.TableFor(DoneKind.Higher).Visible;higher.Flip();
     Check(closed&&higher.Open&&w.TableFor(DoneKind.Higher).Visible,"다시 나타난 항목은 접힌 채이고 한 번 누르면 펴진다");
    });
   }
   try{Directory.Delete(dir,true);}catch(IOException){}catch(UnauthorizedAccessException){}
  }
  // ── 판독 검수: 가짜 게임으로 확인할 것을 만든다(흐린 칸 하나, 제목을 끝내 못 읽은 곡, 아티스트를 못 읽은 이름이 같은 곡) ──
  {
   int p=Enumerable.Range(0,all.Count-1).First(i=>data.TitleOf(all[i].Song)==data.TitleOf(all[i+1].Song));
   int from=Math.Max(0,p-6);var songs=all.GetRange(from,Math.Min(all.Count,p+6)-from);int twin=p-from;   // 이름이 같은 두 곡 가운데 앞의 것
   int blur=Enumerable.Range(0,songs.Count).First(i=>i!=twin&&i!=twin+1&&Enumerable.Range(0,16).Any(c=>songs[i].Cell(c).Length>1));int blurCell=Enumerable.Range(0,16).First(c=>songs[blur].Cell(c).Length>1);
   int unread=Enumerable.Range(0,songs.Count).First(i=>i!=twin&&i!=twin+1&&i!=blur&&i>0);
   string reviewDir=Path.Combine(Path.GetTempPath(),"djmax-review-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(reviewDir);var store=new RecordStore(Path.Combine(reviewDir,"records.json"));
   (CollectionWindow.Stage Stage,string Text) Review(Action<CollectionWindow>? act)
   {
    var hook=GuideDialog.Hook;GuideDialog.Hook=_=>0;
    try
    {
     var game=new CollectionScanChecks.FakeGame(songs,data,0);game.Cover[blur]=(blurCell,0);game.TitleCover.Add(unread);game.ArtistCover.Add(twin);
     using var window=new CollectionWindow(catalog,"기본",store){FinishToFront=false,WorldForTest=()=>game};
     window.Show();for(int i=0;i<10;i++){Application.DoEvents();Thread.Sleep(5);}
     window.Start();var clock=System.Diagnostics.Stopwatch.StartNew();
     while(window.Now is CollectionWindow.Stage.Climbing or CollectionWindow.Stage.Reading&&clock.Elapsed.TotalMinutes<10){Application.DoEvents();Thread.Sleep(2);}
     for(int i=0;i<10;i++){Application.DoEvents();Thread.Sleep(5);}
     act?.Invoke(window);for(int i=0;i<10;i++){Application.DoEvents();Thread.Sleep(5);}
     var stage=window.IsDisposed?(CollectionWindow.Stage)(-1):window.Now;string text=window.IsDisposed?"":window.ReviewText+" | "+window.DoneText;if(!window.IsDisposed)window.Close();return(stage,text);
    }
    finally{GuideDialog.Hook=hook;}
   }
   var r1=Review(w=>
   {
    var review=w.ReviewForTest!;var groups=review.Groups;
    var gBlur=groups.Single(g=>g.Fixed?.Id==songs[blur].Song.Id);var gUnread=groups.Single(g=>g.Item.Judge.TitleUnclear);var gTwin=groups.Single(g=>g.NeedsSong&&!g.Item.Judge.TitleUnclear);
    Check(w.Now==CollectionWindow.Stage.Review&&groups.Count==3&&gBlur.Rows.Single().Kind==ReviewKind.Score&&gTwin.Candidates.Any(s=>s.Id==songs[twin].Song.Id)&&w.ReviewText.Contains("확인 막힘")&&(gUnread.Candidates.Count>0||w.ReviewText.Contains(CollectionReview.NotFound)),
     $"확인할 것이 있으면 판독 검수: 흐린 칸은 '스코어' 줄, 제목을 못 읽은 곡은 후보 없이 안내, 이름이 같은 곡은 후보에서 고른다 [{w.ReviewText}]");
    var conf=w.ReviewConfirm;var reviewCancel=conf.Parent!.Controls.OfType<Button>().Single(b=>b.Text=="취소");
    Check(reviewCancel.Left-conf.Right is >=0 and <=12&&conf.Left>conf.Parent.Width/2,$"판독 검수의 '확인'·'취소'는 오른쪽에 나란히 있다 [확인 {conf.Left}~{conf.Right}, 취소 {reviewCancel.Left}, 줄 {conf.Parent.Width}]");
    var withNone=w.LinesForTest.First(l=>l.None!=null);
    Check(withNone.None!.BackColor.A==255&&withNone.None.BackColor==withNone.BackColor&&withNone.Exclude.BackColor==withNone.BackColor,"줄의 체크 상자는 줄과 같은 불투명 바탕이다(글자가 두 번 그려지지 않게)");
    // 곡 선택 창: 후보와 전체 곡에서 찾기
    w.PickerForTest=picker=>
    {
     bool candidates=picker.List.Songs.Any(s=>s.Id==songs[twin].Song.Id);picker.Search();picker.SearchBox.Text=songs[unread].Song.Name;
     Check(candidates&&picker.Searching&&picker.List.Songs.Any(s=>s.Id==songs[unread].Song.Id),$"곡 선택 창: 후보가 보이고, 전체 곡에서 찾기로 곡명을 치면 그 곡이 나온다(결과 {picker.List.Songs.Count}곡)");
    };
    w.LinesForTest.First(l=>l.Row.Group==gTwin&&l.Pick!=null).Pick!.PerformClick();w.PickerForTest=null;
    // 캡처 보기: 전체 → 누른 자리 확대 → 다시 누르면 전체, 아티스트 장면의 주의 글
    w.ViewerForTest=viewer=>
    {
     viewer.ClickAt(.9f,.5f);bool zoomed=viewer.Zoomed;viewer.ClickAt(.5f,.5f);bool back=!viewer.Zoomed;viewer.Go(1);
     Check(zoomed&&back&&viewer.SceneText.Contains("다음 곡으로 이동 후 · 오른쪽 곡 목록")&&viewer.NoteText=="이 장면의 점수표는 다른 곡입니다.",$"캡처 보기: 누르면 확대, 다시 누르면 전체, 아티스트 장면에는 주의 글 [{viewer.SceneText}]");
     viewer.ClickAt(.5f,.5f);bool escUnzoom=viewer.PressEscape()&&!viewer.Zoomed;bool escPassed=!viewer.PressEscape();
     Check(escUnzoom&&escPassed&&viewer.CancelButton is Button{DialogResult:DialogResult.Cancel,Text:"닫기"},"캡처 보기의 Esc: 확대면 전체로, 전체면 '닫기'가 받는다");
     bool takes=viewer.PictureTakesFocus;viewer.PressPicture(Keys.Enter);bool keyZoom=viewer.Zoomed;viewer.PressPicture(Keys.Space);
     Check(takes&&keyZoom&&!viewer.Zoomed,"캡처 보기의 그림 칸이 Tab 으로 초점을 받고 Enter 로 확대, Space 로 전체");
    };
    w.LinesForTest.First(l=>l.Row.Group==gTwin).CaptureButton.PerformClick();w.ViewerForTest=null;
    // 겹친 곡과 잘못된 점수
    w.Choose(gTwin,songs[0].Song);var blurLine=w.LinesForTest.Single(l=>l.Row.Group==gBlur);blurLine.Input!.Box.Text="0";
    Check(w.ReviewText.Contains("확인 막힘")&&w.LinesForTest.First(l=>l.Row.Group==gTwin).SongNote.Text.Contains(CollectionReview.AlreadyRead)&&blurLine.ScoreNote!.Text==CollectionReview.BadScore&&w.ReviewForTest!.Groups.SelectMany(g=>g.Rows).Any(r=>r.ScoreBad),
     $"이미 읽은 곡을 고르면 안내가 뜨고, 0 점은 안내와 함께 확인을 막는다 [{w.ReviewText}]");
    // 다 정하면 확인 → 완료 화면 → 적용
    w.Choose(gTwin,songs[twin].Song);w.Choose(gUnread,songs[unread].Song);blurLine.Input.Box.Text=songs[blur].Cell(blurCell);
    Check(w.ReviewText.StartsWith("모든 항목을 확인했습니다.")&&w.ReviewText.Contains("확인 됨"),$"다 정하면 확인할 수 있다 [{w.ReviewText}]");
    w.ConfirmReview();
    Check(w.Now==CollectionWindow.Stage.Complete,$"확인하면 전곡 읽기 완료 화면 [{w.DoneText}]");
    // 검수로 돌아가기: 검수에 적은 값과 고른 곡이 그대로 남고, 다시 확인하면 완료 화면
    bool backShown=w.BackFromDone is {Visible:true,Text:"검수로 돌아가기",Left:0};string typed=blurLine.Input.Box.Text;w.BackToReview();
    Check(backShown&&w.Now==CollectionWindow.Stage.Review&&blurLine.Input.Box.Text==typed&&gTwin.Chosen?.Id==songs[twin].Song.Id&&gUnread.Chosen?.Id==songs[unread].Song.Id&&w.ReviewText.Contains("확인 됨"),
     $"검수를 거쳐 온 완료 화면에 '검수로 돌아가기'가 있고, 누르면 적은 값과 고른 곡이 그대로인 판독 검수로 돌아간다 [{w.ReviewText}]");
    w.ConfirmReview();
    Check(w.Now==CollectionWindow.Stage.Complete&&w.BackFromDone is {Visible:true},$"다시 확인하면 완료 화면 [{w.DoneText}]");
    w.Apply();
   });
   int scores=songs.Sum(s=>Enumerable.Range(0,16).Count(i=>s.Cell(i).Length>1&&s.Cell(i).All(char.IsAsciiDigit)));
   var blurKey=new Selection(songs[blur].Song.Id,CollectionScreen.Buttons[blurCell%4],CollectionScreen.Patterns[blurCell/4],"노말",0).Key;
   Check(r1.Stage==(CollectionWindow.Stage)(-1)&&store.Active.Plays.Count(x=>x.Source==PlaySource.Collection)==scores&&store.Active.Plays.Any(x=>x.Key==blurKey&&x.Score==int.Parse(songs[blur].Cell(blurCell))),
    $"적용하면 검수한 값까지 모두 저장된다(컬렉션 줄 {store.Active.Plays.Count(x=>x.Source==PlaySource.Collection)}/{scores})");
   store.Undo();
   // 곡을 고르지 않고 이름이 같은 곡의 줄을 모두 제외하면 두 곡 모두 보호한다
   bool dropped=false;
   var r2=Review(w=>
   {
    var review=w.ReviewForTest!;var gTwin=review.Groups.Single(g=>g.NeedsSong&&!g.Item.Judge.TitleUnclear);
    foreach(var line in w.LinesForTest.Where(l=>l.Row.Group==gTwin))line.Exclude.Checked=true;
    Check(gTwin.Candidates.Count<2||w.ReviewText.Contains("후보"),$"후보 곡을 모두 보호한다는 안내 [{w.ReviewText}]");
    // 모두 제외한 묶음도 곡을 고를 수 있고, 이미 읽은 곡을 고르면 확인을 막는다
    var twinLines=w.LinesForTest.Where(l=>l.Row.Group==gTwin&&l.Pick!=null).ToList();bool pickOpen=twinLines.All(l=>l.Pick!.Enabled);
    w.Choose(gTwin,songs[0].Song);bool blocked=w.ReviewText.Contains("확인 막힘")&&twinLines[0].SongNote.Visible&&twinLines[0].SongNote.Text.Contains(CollectionReview.AlreadyRead);
    w.Choose(gTwin,null);
    Check(pickOpen&&blocked&&!w.ReviewForTest!.Duplicated&&!twinLines[0].SongNote.Visible,$"모두 제외한 묶음도 곡 선택 단추가 눌리고, 이미 읽은 곡을 고르면 확인을 막는다. 곡을 풀면 막힘이 풀린다 [{w.ReviewText}]");
    w.CancelReview();dropped=w.LinesForTest.Count==0&&w.ReviewForTest==null;
   });
   Check(r2.Stage==CollectionWindow.Stage.Ready,"판독 검수의 취소는 준비 화면으로");
   Check(dropped,"검수를 취소하면 검수 줄과 그 줄이 붙잡은 캡처도 버린다");
  }
  notes.Add(failed==0?"모두 통과":$"실패 {failed}");File.WriteAllLines(reportPath,notes);if(failed>0)Environment.ExitCode=2;
 }
 /// <summary>
 /// 창 그림에 쓸 판독 검수의 결과: 받아 둔 실제 화면에서 곡을 여섯 읽고 확인할 것을 만든다. 첫 곡은 점수 칸 하나를 흐린 것으로, 둘째 곡은 곡을 정하지 않은 것으로(후보는 그 곡과
 /// 패턴이 같은 다른 곡 하나), 셋째 곡은 제목을 끝내 읽지 못한 것으로(후보 없음) 바꾼다. 둘째 곡에는 아티스트를 읽은 화면으로 다음 화면을 붙인다
 /// </summary>
 static ScanResult? ReviewSample(Catalog catalog,string stills,out string why)
 {
  why="";
  var reader=new CollectionReader(catalog,CollectionData.Load());var items=new List<ScanItem>();var files=Directory.GetFiles(stills,"*-still.png").OrderBy(f=>f).ToList();
  for(int k=0;k<files.Count&&items.Count<6;k++)
  {
   using var bitmap=new Bitmap(files[k]);var frame=new CollectionFrame(bitmap);var shot=reader.Read(frame);var judge=reader.Judge(shot);
   if(judge.Song==null||!judge.Sure.All(s=>s)||items.Any(i=>i.Judge.Song!.Id==judge.Song.Id))continue;
   items.Add(new ScanItem{Shot=shot,Judge=judge,Sure=judge.Sure.ToArray(),Screen=File.ReadAllBytes(files[k]),ArtistScreen=k+1<files.Count?File.ReadAllBytes(files[k+1]):null});
  }
  if(items.Count<4){why=$"(화면 {files.Count}장에서 쓸 곡 {items.Count})";return null;}
  var blur=items[0];int cell=Enumerable.Range(0,16).First(i=>blur.Shot.Cells[i].Number);blur.Sure[cell]=false;blur.Questions=1;blur.ArtistScreen=null;
  var pick=items[1];var twin=catalog.Songs.FirstOrDefault(s=>s.Id!=pick.Judge.Song!.Id&&CollectionReview.Fits(s,pick));
  var undecided=new SongJudge{Song=null};undecided.Candidates.Add(pick.Judge.Song!);if(twin!=null)undecided.Candidates.Add(twin);
  items[1]=new ScanItem{Shot=pick.Shot,Judge=undecided,Sure=pick.Sure,Screen=pick.Screen,ArtistScreen=pick.ArtistScreen,Questions=1};
  var lost=items[2];items[2]=new ScanItem{Shot=lost.Shot,Judge=new SongJudge{Song=null,TitleUnclear=true},Sure=lost.Sure,Screen=lost.Screen,Questions=1};
  foreach(var item in items.Skip(3))item.ArtistScreen=null;
  var result=new ScanResult{End=ScanEnd.Done};result.Items.AddRange(items);result.Questions=items.Sum(i=>i.Questions);return result;
 }
 /// <summary>창을 실제 화면에서 찍는다(목록 컨트롤은 그림으로 옮기면 줄이 그려지지 않는다)</summary>
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool PrintWindow(IntPtr window,IntPtr dc,uint flags);
 static void Shoot(Form form,string path)
 {
  for(int i=0;i<40;i++){Application.DoEvents();Thread.Sleep(10);}
  // 다른 창이 위에 떠 있어도 찍히게 Windows 에 창의 내용을 그려 달라고 한다(PW_RENDERFULLCONTENT)
  var r=form.Bounds;using var shot=new Bitmap(r.Width,r.Height);
  using(var g=Graphics.FromImage(shot)){var dc=g.GetHdc();try{PrintWindow(form.Handle,dc,2);}finally{g.ReleaseHdc(dc);}}
  shot.Save(path);
 }
 /// <summary>
 /// 창 그림에 쓸 기록과 읽은 값: 곡 목록의 앞 여덟 곡(SC)으로 네 항목이 모두 보이게 꾸민다(V아카이브 둘, 옛 RATE 하나, 높은 수동 판과 노말에 반영된 하드 판,
 /// '-' 로 읽힌 예전 컬렉션 줄과 날짜 있는 같은 점수가 생긴 예전 컬렉션 줄, 새 점수 다섯). empty 면 바뀔 것이 없는 기록과 값
 /// </summary>
 static (RecordStore Store,CollectionOutcome Outcome,Play Edit) Sample(Catalog catalog,string folder,bool empty)
 {
  Directory.CreateDirectory(folder);var store=new RecordStore(Path.Combine(folder,"records.json"));
  var songs=catalog.Songs.Where(s=>s.Charts.Any(c=>c.Pattern=="SC")).Take(8).ToList();
  Selection K(int i,string judge="노말")=>new(songs[i].Id,songs[i].Charts.First(c=>c.Pattern=="SC").Button,"SC",judge,0);
  var o=new CollectionOutcome{ProfileId=store.Active.Id};o.Seen.UnionWith(catalog.Songs.Select(s=>s.Id));
  if(empty){o.Values[K(1).Key]=null;return(store,o,null!);}
  var t=new DateTime(2026,11,20,12,0,0,DateTimeKind.Utc);
  store.Import(new Dictionary<Selection,decimal>{[K(0)]=99.12m,[K(1)]=98.40m});
  var profile=store.Active;
  profile.Plays.Add(new Play(profile.NextSeq++,K(2).Key,975300,null,PlaySource.OldRate));
  profile.Plays.Add(new Play(profile.NextSeq++,K(5).Key,910600,null,PlaySource.Collection));
  profile.Plays.Add(new Play(profile.NextSeq++,K(6).Key,990000,null,PlaySource.Collection));
  store.Save();
  var edit=store.Add(K(3),999052,t,PlaySource.Manual);
  store.Add(K(4,"하드"),999231,t.AddHours(1));
  store.Add(K(6),995220,t.AddHours(2));
  o.Values[K(0).Key]=990500;o.Values[K(1).Key]=null;o.Values[K(2).Key]=978000;o.Values[K(3).Key]=997654;o.Values[K(4).Key]=998764;o.Values[K(5).Key]=null;o.Values[K(6).Key]=995220;o.Values[K(7).Key]=983086;
  return(store,o,edit);
 }
 static string Short(string text)=>text.Length>260?text[..260]+"…":text;
 /// <summary>창에서 지금 보이는 글(보이는 컨트롤만)</summary>
 static string Texts(Control root)
 {
  var parts=new List<string>();
  void Walk(Control c){if(!c.Visible)return;if(c is Label or WordLabel or Button&&c.Text.Length>0)parts.Add(c.Text);foreach(Control child in c.Controls)Walk(child);}
  foreach(Control c in root.Controls)Walk(c);return string.Join(" | ",parts);
 }

 /// <summary>실제 게임으로 읽는다(사람이 지켜볼 때만 돌린다)</summary>
 public static void Live(Catalog catalog,string folder,bool front,string? expectedPath,int first,int last)
 {
  var data=CollectionData.Load();
  var trace=new List<string>();
  // BEST 를 읽지 못한 곡의 화면을 20곡까지 남긴다(읽은 화면 best-NNNN.png 와, 큰 16:9 를 줄여 읽었으면 줄이기 전 best-NNNN-raw.png. 화면이 아직 그 곡에 있을 때 받는다)
  var keep=new List<(int N,byte[] Png,Bitmap? Raw)>();int closed=0;
  // 완료 화면의 적용은 결과 폴더의 시험용 기록 파일(records.json)에만 쓴다. 개인 기록은 쓰지 않는다. 같은 파일을 다음 실행이 이어 쓴다
  var store=new RecordStore(Path.Combine(folder,"records.json"));
  using var window=new CollectionWindow(catalog,"기본",store){FinishToFront=front,TraceForTest=line=>{lock(trace)trace.Add(line);}};
  window.Applied+=r=>File.WriteAllText(Path.Combine(folder,$"applied-{DateTime.Now:yyyyMMdd-HHmmss}.txt"),$"추가 {r.Added} · 갱신 {r.Updated} · 삭제 {r.Deleted}"+Environment.NewLine);
  window.ClosingForTest=item=>
  {
   closed++;if(item.Shot.Best!=null||item.Frame==null||keep.Count>=20)return;
   var raw=window.LastWorld is{Shrunk:true} game?RawShot(game.Window):null;lock(trace)keep.Add((closed,item.Frame.ToPng(),raw));
  };
  // 끝까지 읽었을 때뿐 아니라 멈추거나 취소되었을 때도 남긴다: 멈춘 까닭(trace.txt)과 마지막으로 받은 화면(last-N.png)
  window.EndedForTest+=result=>
  {
   string[] lines;List<(int N,byte[] Png,Bitmap? Raw)> kept;lock(trace){lines=trace.ToArray();trace.Clear();kept=keep.ToList();keep.Clear();}closed=0;
   try
   {
    string written=Write(catalog,data,folder,result,expectedPath,first,last,window.LastWorld);
    File.WriteAllLines(Path.Combine(written,"trace.txt"),lines);
    for(int i=0;i<result.LastFrames.Count;i++)File.WriteAllBytes(Path.Combine(written,$"last-{i+1}.png"),result.LastFrames[i].ToPng());
    if(window.LastWorld is{Shrunk:true} game)SaveRaw(game.Window,Path.Combine(written,"raw-end.png"));
    foreach(var k in kept){File.WriteAllBytes(Path.Combine(written,$"best-{k.N:0000}.png"),k.Png);if(k.Raw!=null){k.Raw.Save(Path.Combine(written,$"best-{k.N:0000}-raw.png"),System.Drawing.Imaging.ImageFormat.Png);k.Raw.Dispose();}}
   }
   catch(Exception error){MessageBox.Show(window,"결과를 쓰지 못했습니다: "+error.Message,"컬렉션 읽기(개발용)");}
  };
  Application.Run(window);
 }
 static string Write(Catalog catalog,CollectionData data,string parent,ScanResult result,string? expectedPath,int first,int last,CollectionGame? game)
 {
  string folder=Path.Combine(parent,DateTime.Now.ToString("yyyyMMdd-HHmmss"));Directory.CreateDirectory(folder);
  string Key(int i)=>$"{CollectionScreen.Buttons[i%4]}B {CollectionScreen.Patterns[i/4]}";
  // 숫자 칸이 표본과 다른 화소와 여유(화면이 얼마나 흐린지를 견주려고. 보더리스를 게임 해상도 1920·2560 으로 둔 두 시험을 견준다)
  static IEnumerable<CellRead> Numbers(ScanItem item)=>item.Shot.Cells.Where(c=>c.Number);
  static IEnumerable<int> Margins(ScanItem item)=>Numbers(item).Select(c=>c.Margin).Where(m=>m!=int.MaxValue);
  var rows=new List<object>();int n=0;
  foreach(var item in result.Items)
  {
   n++;
   rows.Add(new{n,id=item.Judge.Song?.Id,name=item.Judge.Song?.Name,artist=item.Judge.Song?.Artist,title=item.Shot.Title?.Text,read_artist=item.Artist,questions=item.Questions,best=item.Shot.Best,
    score=Enumerable.Range(0,16).ToDictionary(Key,i=>item.Shot.Cells[i].Text),sure=Enumerable.Range(0,16).Where(i=>!item.Sure[i]).Select(Key).ToArray(),
    title_cost=item.Shot.Title?.Cost,cell_cost=Numbers(item).Select(c=>c.Cost).DefaultIfEmpty(0).Max(),cell_margin=Margins(item).Cast<int?>().DefaultIfEmpty(null).Min()});
   if(item.Screen!=null)File.WriteAllBytes(Path.Combine(folder,$"{n:0000}-screen.png"),item.Screen);
   if(item.ArtistScreen!=null)File.WriteAllBytes(Path.Combine(folder,$"{n:0000}-artist.png"),item.ArtistScreen);
  }
  File.WriteAllText(Path.Combine(folder,"result.json"),JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));
  var report=new StringBuilder();
  report.AppendLine($"끝: {result.End}{(result.Detail.Length>0?" ("+result.Detail+")":"")}");
  report.AppendLine($"읽은 곡 {result.Items.Count}, 확인할 것 {result.Questions}(곡 선택 {result.SongQuestions} · 점수 {result.CellQuestions}), 허용 {result.Allowed}, 걸린 시간 {result.Seconds:0.0}초(곡마다 {result.Seconds/Math.Max(1,result.Items.Count):0.000}초)");
  report.AppendLine($"이름이 같은 곡을 아티스트로 정한 곡 {result.Items.Count(i=>i.Artist!=null)}. Windows 가 받지 않은 키 {game?.Refused??0}. 게임 화면 {game?.LastSize.Width}×{game?.LastSize.Height}{(game?.Shrunk==true?"(큰 16:9 를 줄여 읽음)":"")}");
  var costs=result.Items.SelectMany(Numbers).Select(c=>c.Cost).OrderBy(v=>v).ToList();var margins=result.Items.SelectMany(Margins).OrderBy(v=>v).ToList();
  var titles=result.Items.Where(i=>i.Shot.Title!=null).Select(i=>i.Shot.Title!.Cost).OrderBy(v=>v).ToList();
  if(costs.Count>0&&titles.Count>0)
   report.AppendLine($"숫자 칸 {costs.Count}: 표본과 다른 화소의 중앙값 {costs[costs.Count/2]}, 99% {costs[Math.Min(costs.Count-1,costs.Count*99/100)]}, 가장 큰 값 {costs[^1]}, 문턱 {CollectionReader.CellCost} 을 넘은 칸 {costs.Count(v=>v>CollectionReader.CellCost)}. "+
    $"여유의 가장 작은 값 {(margins.Count>0?margins[0]:-1)}(문턱 {CollectionReader.CellMargin}). 제목의 어긋난 몫 중앙값 {titles[titles.Count/2]:0.000}, 가장 큰 값 {titles[^1]:0.000}");
  if(expectedPath!=null)
  {
   var want=new Dictionary<string,JsonElement>();var order=new List<string>();
   foreach(var e in JsonSerializer.Deserialize<List<JsonElement>>(File.ReadAllText(expectedPath))!)
   {
    int number=int.Parse(e.GetProperty("n").GetString()!);if(number<first||number>last)continue;
    string id=e.GetProperty("id").GetString()!;if(!want.ContainsKey(id)){want[id]=e.GetProperty("score");order.Add(id);}
   }
   var got=result.Items.Where(i=>i.Judge.Song!=null).ToList();var seen=got.Select(i=>i.Judge.Song!.Id).ToHashSet();
   int same=0,differ=0,unsure=0;var lines=new List<string>();
   foreach(var item in got)
   {
    if(!want.TryGetValue(item.Judge.Song!.Id,out var score)){lines.Add($"견줄 값에 없는 곡 {item.Judge.Song}");continue;}
    bool all=true;
    for(int i=0;i<16;i++)
    {
     string value=score.GetProperty(Key(i)).GetString()!;
     if(!item.Sure[i]){unsure++;all=false;lines.Add($"확실하지 않은 칸 {item.Judge.Song} {Key(i)}: '{item.Shot.Cells[i].Text}' (견줄 값 '{value}')");continue;}
     if(item.Shot.Cells[i].Text!=value){differ++;all=false;lines.Add($"견줄 값과 다른 칸 {item.Judge.Song} {Key(i)}: '{item.Shot.Cells[i].Text}' ≠ '{value}'");}
    }
    if(all)same++;
   }
   var missing=order.Where(id=>!seen.Contains(id)).ToList();
   // 차례: 읽은 곡이 받아 둔 차례와 같은가(건너뛴 곡이 있는지)
   var readOrder=got.Select(i=>i.Judge.Song!.Id).ToList();int skips=0;
   for(int i=1;i<readOrder.Count;i++){int a=order.IndexOf(readOrder[i-1]),b=order.IndexOf(readOrder[i]);if(a>=0&&b>=0&&b!=a+1&&result.Items.Count(x=>x.Judge.Song==null)==0)skips++;}
   report.AppendLine($"견줄 값의 곡 {order.Count}: 16칸이 모두 같고 확실한 곡 {same}, 다른 칸 {differ}, 확실하지 않은 칸 {unsure}, 빠진 곡 {missing.Count}, 차례가 이어지지 않은 곳 {skips}");
   report.AppendLine("(견줄 값을 만든 뒤에 오른 점수는 '다른 칸'으로 나올 수 있다: 그 칸은 게임 화면에서 확인한다)");
   foreach(string id in missing.Take(40))report.AppendLine("빠진 곡 "+catalog.Songs.First(s=>s.Id==id));
   foreach(string l in lines.Take(300))report.AppendLine(l);
  }
  foreach(var item in result.Items.Where(i=>i.Judge.Song==null))report.AppendLine($"곡을 정하지 못함: 제목 '{item.Shot.Title?.Text}', 후보 {string.Join(" / ",item.Judge.Candidates.Select(s=>s.ToString()))}");
  File.WriteAllText(Path.Combine(folder,"report.txt"),report.ToString());
  return folder;
 }
 // 줄이기 전의 화면 한 장(큰 16:9 를 줄여 읽을 때. 줄이는 방법을 따로 견줘 보려고)
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool GetClientRect(IntPtr window,out RawRect rect);
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool ClientToScreen(IntPtr window,ref RawPoint point);
 struct RawRect{public int Left,Top,Right,Bottom;}
 struct RawPoint{public int X,Y;}
 static void SaveRaw(IntPtr window,string path){using var raw=RawShot(window);raw?.Save(path,System.Drawing.Imaging.ImageFormat.Png);}
 static Bitmap? RawShot(IntPtr window)
 {
  if(!GetClientRect(window,out var r)||r.Right<100||r.Bottom<100)return null;var point=new RawPoint();ClientToScreen(window,ref point);
  var raw=new Bitmap(r.Right,r.Bottom);using(var g=Graphics.FromImage(raw))g.CopyFromScreen(point.X,point.Y,0,0,raw.Size);return raw;
 }
}
