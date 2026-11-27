using System.Runtime.Versioning;
using System.Text;
namespace LadderRecords;
// 개발용 도구(Debug 빌드에만 들어간다): 실제 게임에 ↑·↓ 를 보내 보고 화면이 어떻게 따라오는지 잰다(키만 보내는 시험). 기록을 바꾸지 않는다.
// 잰 결과(keys-<시각>.txt)와 따져 볼 화면(PNG)을 결과 폴더에 쓴다.
//
//   DJMAXLocalRecords.exe --collection-keys <판독 목록 json> <첫 번호> <끝 번호> <결과 폴더> [short]
//
// short 를 주면 짧은 시험이다(40초쯤): ①을 8초로 늘리고 ③을 지금 자리에서 25번, ⑥만 한다(②④⑤는 하지 않는다). 따져 볼 화면은 결과 파일 옆에 PNG 로 남긴다.
// 판독 목록과 번호는 --collection-scan-check 와 같다(사용자의 목록에서 곡이 놓인 차례를 얻는다. 몇 곡이 옮겨졌는지를 제목으로 센다).
// 게임을 COLLECTION › PLAY INFO › MUSIC DATA 화면에 두고 실행한 뒤 게임 창을 앞에 두면 시작한다. 다른 창을 앞에 두면 바로 멈춘다. 2분쯤 걸린다.
// 하는 일(차례대로): ① 멈춘 화면이 캡처마다 얼마나 다른지 ② ↑ 를 누른 채 첫 곡으로 ③ ↓ 를 한 번씩 40번(바뀌는 데 걸린 시간, 목록이 멈추는 데 걸린 시간, 띠의 자리, 윗줄의 아티스트)
// ④ ↓ 를 빠르게 20번씩(누름 시간과 사이를 줄여 가며. 보낸 수와 옮겨진 곡 수) ⑤ ↑ 를 1초 누르고 뗀 뒤 더 움직이는지 ⑥ 시작·중지 단축키(Ctrl+Enter·Backspace)를 등록한 채 그 키를 보냈을 때 게임도 받는지.
[SupportedOSPlatform("windows10.0.19041")]
static class CollectionKeyChecks
{
 sealed class Halt:Exception{public Halt(string message):base(message){}}
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
 [System.Runtime.InteropServices.DllImport("kernel32.dll")]static extern uint GetCurrentThreadId();
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool AttachThreadInput(uint from,uint to,bool attach);
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr window);
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool BringWindowToTop(IntPtr window);
 /// <summary>보이지 않는 창. 단축키의 알림을 받는다.</summary>
 sealed class Listener:Form
 {
  public volatile int Starts,Stops;
  protected override void WndProc(ref Message m){if(m.Msg==ScanHotkeys.Message){if((int)m.WParam==ScanHotkeys.StartId)Starts++;else if((int)m.WParam==ScanHotkeys.StopId)Stops++;}base.WndProc(ref m);}
 }
 sealed record Seen(CollectionFrame Frame,Mask Title,Mask Table,Mask List);

 /// <summary>게임 없이 보는 것: 키 보내기의 Windows 호출과 단축키 등록이 뜻대로 되는가. 이 도구의 창을 앞에 두고 그 창에 보낸다(다른 창에는 보내지 않는다. 앞 창이 되지 못하면 보내지 않고 그렇게 적는다).
 ///   DJMAXLocalRecords.exe --collection-keys-self 결과 파일</summary>
 public static void Self(string reportPath)
 {
  var notes=new List<string>();int failed=0;void Check(bool ok,string name){if(!ok)failed++;notes.Add((ok?"PASS: ":"FAIL: ")+name);}
  using var probe=new Probe{Text="컬렉션 키 보내기 자체 확인",Width=360,Height=120,StartPosition=FormStartPosition.CenterScreen,TopMost=true};
  probe.Show();probe.Activate();var game=new CollectionGame(probe.Handle);
  void Pump(double seconds){double until=game.Now+seconds;while(game.Now<until){Application.DoEvents();Thread.Sleep(5);}}
  // 다른 프로그램이 띄운 창은 Windows 가 앞에 세워 주지 않는다: 지금의 앞 창과 입력을 잠깐 이어 앞으로 나온다(이 확인이 도는 몇 초 동안 앞 창이 이 도구가 된다)
  int fronts=0;
  void Front()
  {
   Pump(0.1);if(game.Active)return;fronts++;
   uint other=GetWindowThreadProcessId(GetForegroundWindow(),out _),me=GetCurrentThreadId();
   AttachThreadInput(me,other,true);try{SetForegroundWindow(probe.Handle);BringWindowToTop(probe.Handle);}finally{AttachThreadInput(me,other,false);}
   double waited=game.Now;while(!game.Active&&game.Now-waited<2)Pump(0.05);
  }
  Front();
  if(!game.Active){notes.Add("확인하지 못함: 이 도구의 창이 앞 창이 되지 못했다(키를 보내지 않았다)");File.WriteAllLines(reportPath,notes);Environment.ExitCode=3;return;}
  (int Message,int Key,int Scan,bool Extended)[] Got(Action send,bool front=true){if(front)Front();probe.Seen.Clear();send();Pump(0.25);return probe.Seen.ToArray();}
  var down=Got(()=>{game.Key(ScanKey.Down,true);Pump(0.05);game.Key(ScanKey.Down,false);});
  Check(down.SequenceEqual(new[]{(0x100,0x28,0x50,true),(0x101,0x28,0x50,true)}),$"↓ 를 누르고 떼면 창이 아래 화살표(스캔 코드 0x50, 확장 키)의 누름과 뗌을 한 번씩 받는다 [{string.Join(" ",down)}]");
  var up=Got(()=>{game.Key(ScanKey.Up,true);Pump(0.05);game.Key(ScanKey.Up,false);});
  Check(up.SequenceEqual(new[]{(0x100,0x26,0x48,true),(0x101,0x26,0x48,true)}),$"↑ 도 같다(0x48) [{string.Join(" ",up)}]");
  using(var hot=new ScanHotkeys(probe.Handle))
  {
   Check(hot.SetStart(true)&&hot.SetStop(true),"시작(Ctrl+Enter)·중지(Backspace) 단축키가 등록된다");
   var start=Got(()=>{game.Send(CollectionGame.ControlKey,false,true);game.Send(CollectionGame.EnterKey,false,true);Pump(0.05);game.Send(CollectionGame.EnterKey,false,false);game.Send(CollectionGame.ControlKey,false,false);});
   Check(start.Count(m=>m.Message==ScanHotkeys.Message&&m.Key==ScanHotkeys.StartId)==1&&!start.Any(m=>m.Message==0x100&&m.Key==0x0D),$"Ctrl+Enter 를 보내면 단축키 알림이 한 번 오고 창은 Enter 의 누름을 받지 않는다 [{string.Join(" ",start)}]");
   var stop=Got(()=>{game.Send(CollectionGame.BackspaceKey,false,true);Pump(0.05);game.Send(CollectionGame.BackspaceKey,false,false);});
   Check(stop.Count(m=>m.Message==ScanHotkeys.Message&&m.Key==ScanHotkeys.StopId)==1&&!stop.Any(m=>m.Message==0x100&&m.Key==0x08),$"Backspace 를 보내면 단축키 알림이 한 번 오고 창은 Backspace 의 누름을 받지 않는다 [{string.Join(" ",stop)}]");
   Check(hot.SetStop(false),"중지 단축키를 끈다");
   var plain=Got(()=>{game.Send(CollectionGame.BackspaceKey,false,true);Pump(0.05);game.Send(CollectionGame.BackspaceKey,false,false);});
   Check(plain.Any(m=>m.Message==0x100&&m.Key==0x08)&&!plain.Any(m=>m.Message==ScanHotkeys.Message),$"끈 뒤에는 Backspace 가 창으로 간다(가로채지 않는다) [{string.Join(" ",plain)}]");
  }
  Check(CollectionGame.Elevated(probe.Handle)==Environment.IsPrivilegedProcess&&!game.KeysBlocked,$"이 프로그램의 창이 관리자 권한인지 읽는다({(Environment.IsPrivilegedProcess?"예":"아니오")}). 권한이 같으면 키가 막히지 않는다고 본다");
  Check(game.Refused==0,$"Windows 가 받지 않은 키 {game.Refused}");
  notes.Add($"      (앞으로 나온 횟수 {fronts})");
  probe.Hide();var away=Got(()=>game.Key(ScanKey.Down,true),false);game.Key(ScanKey.Down,false);
  Check(game.Refused==1&&away.Length==0,"창이 앞 창이 아니면 누름을 보내지 않는다");
  notes.Add(failed==0?"모두 통과":$"실패 {failed}");File.WriteAllLines(reportPath,notes);if(failed>0)Environment.ExitCode=2;
 }
 /// <summary>받은 키와 단축키 알림을 적어 두는 창.</summary>
 sealed class Probe:Form
 {
  public readonly List<(int Message,int Key,int Scan,bool Extended)> Seen=new();
  protected override void WndProc(ref Message m)
  {
   if(m.Msg is 0x100 or 0x101){long l=(long)m.LParam;Seen.Add((m.Msg,(int)m.WParam,(int)(l>>16&0xFF),(l>>24&1)!=0));}
   else if(m.Msg==ScanHotkeys.Message)Seen.Add((m.Msg,(int)m.WParam,0,false));
   base.WndProc(ref m);
  }
 }

 public static void Run(Catalog catalog,string listPath,int first,int last,string folder,bool quick)
 {
  var order=CollectionScanChecks.Order(catalog,listPath,first,last);
  var window=CollectionGame.Find();
  if(window==IntPtr.Zero){MessageBox.Show("게임 창을 찾지 못했습니다. 게임을 켠 뒤 다시 실행해 주세요.","키 보내기 시험");return;}
  Directory.CreateDirectory(folder);string path=Path.Combine(folder,$"keys-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
  using var listener=new Listener();_=listener.Handle;
  var worker=new Thread(()=>
  {
   string end;
   try{end=Work(catalog,order,new CollectionGame(window),listener,path,quick);}catch(Exception e){end="도구가 멈췄습니다: "+e.Message;try{File.AppendAllText(path,"\n도구의 오류: "+e+"\n");}catch{}}
   listener.BeginInvoke(()=>{System.Media.SystemSounds.Asterisk.Play();MessageBox.Show(end+"\n\n"+path,"키 보내기 시험");Application.ExitThread();});
  }){IsBackground=true};
  worker.Start();Application.Run();
 }

 static string Work(Catalog catalog,List<Song> order,CollectionGame game,Listener listener,string path,bool quick)
 {
  var data=CollectionData.Load();var reader=new CollectionReader(catalog,data);var log=new StringBuilder();
  void Say(string line){log.AppendLine(line);}
  // 따져 볼 화면을 결과 파일 옆에 남긴다(사용자의 점수가 든 화면이다: git 밖의 폴더에만 둔다)
  void Keep(CollectionFrame f,string name){try{File.WriteAllBytes(Path.ChangeExtension(path,null)+"-"+name+".png",f.ToPng());}catch{}}
  var artists=catalog.Songs.Select(s=>s.Artist).Distinct().ToList();
  Seen See()
  {
   if(!game.Active)throw new Halt("게임이 앞 창이 아니게 되어 멈췄다");
   var f=game.Capture()??throw new Halt($"화면을 받지 못했거나 1920×1080 이 아니다({game.LastSize.Width}×{game.LastSize.Height})");
   return new(f,CollectionScreen.TitleSign(f),CollectionScreen.TableSign(f),CollectionScreen.ListSign(f));
  }
  static bool Same(Seen a,Seen b)=>a.Title.Differ(b.Title)<=CollectionScan.SignSame&&a.Table.Differ(b.Table)<=CollectionScan.SignSame;
  // 목록까지 멈춘 화면(0.1초 떨어진 두 화면이 같을 때)
  Seen Settle(){var a=See();double until=game.Now+2;while(true){game.Wait(0.1);var b=See();if((Same(a,b)&&a.List.Differ(b.List)<=CollectionScan.ListStill)||game.Now>=until)return b;a=b;}}
  void Tap(ScanKey key,double press){if(!game.Active)throw new Halt("게임이 앞 창이 아니게 되어 멈췄다");game.Key(key,true);try{game.Wait(press);}finally{game.Key(key,false);}}
  // 화면의 제목이 차례의 몇째 곡인가(이름이 같은 곡은 near 에 가까운 쪽). 모르면 -1
  int Where(CollectionFrame f,int near)
  {
   var title=reader.Read(f).Title;if(title==null||title.Cost>CollectionReader.TitleSure)return -1;
   var names=title.Near.Select(n=>n.Text).ToHashSet();
   return Enumerable.Range(0,order.Count).Where(i=>names.Contains(data.TitleOf(order[i]))).DefaultIfEmpty(-1).MinBy(i=>i<0?int.MaxValue:Math.Abs(i-near));
  }
  string Stats(List<double> v)=>v.Count==0?"없음":$"가장 짧게 {v.Min()*1000:0}ms, 가운데 {v.OrderBy(x=>x).ElementAt(v.Count/2)*1000:0}ms, 가장 길게 {v.Max()*1000:0}ms";

  string end="끝났습니다.";
  try
  {
   Say($"키 보내기 시험 {DateTime.Now:yyyy-MM-dd HH:mm:ss}. 차례를 아는 곡 {order.Count}");
   Say($"게임이 관리자 권한인가: {CollectionGame.Elevated(game.Window) switch{true=>"예",false=>"아니오",_=>"알 수 없음"}}. 이 도구가 관리자 권한인가: {(Environment.IsPrivilegedProcess?"예":"아니오")}. 키가 막힐 것으로 보는가: {(game.KeysBlocked?"예":"아니오")}");
   double waited=game.Now;while(!game.Active){if(game.Now-waited>120)throw new Halt("2분 동안 게임 창이 앞에 오지 않았다");game.Wait(0.1);}
   game.Wait(1.5);
   var look=See();
   Say($"MUSIC DATA 화면인가: {(CollectionScreen.IsMusicData(look.Frame)?"예":"아니오")}");
   if(!CollectionScreen.IsMusicData(look.Frame))throw new Halt("MUSIC DATA 화면이 아니라 키를 보내지 않았다");

   // ① 멈춘 화면이 캡처마다 얼마나 다른가(움직였는지 견주는 글자 그림의 문턱: 제목·표 40, 목록 100·400), 그 동안 읽은 값이 그대로인가
   {
    var firstSeen=Settle();var prev=firstSeen;double t0=game.Now,lastAt=t0,gapMax=0,span=quick?8:3;int n=0,title=0,table=0,list=0,titleFirst=0,tableFirst=0,listFirst=0,reads=0,readsSame=0;
    string Key(CollectionFrame f){var shot=reader.Read(f);return shot.Title?.Text+"|"+string.Join(",",shot.Cells.Select(c=>c.Text))+"|"+shot.Best;}
    string firstKey=Key(firstSeen.Frame);t0=game.Now;lastAt=t0;
    while(game.Now-t0<span)
    {
     var s=See();n++;gapMax=Math.Max(gapMax,game.Now-lastAt);
     title=Math.Max(title,prev.Title.Differ(s.Title));table=Math.Max(table,prev.Table.Differ(s.Table));list=Math.Max(list,prev.List.Differ(s.List));
     titleFirst=Math.Max(titleFirst,firstSeen.Title.Differ(s.Title));tableFirst=Math.Max(tableFirst,firstSeen.Table.Differ(s.Table));listFirst=Math.Max(listFirst,firstSeen.List.Differ(s.List));
     if(n%12==0){reads++;if(Key(s.Frame)==firstKey)readsSame++;else Keep(s.Frame,$"idle-{n}");}
     prev=s;lastAt=game.Now;
    }
    Say($"① 멈춘 화면 {span:0}초: {n}번 잡음(잡는 사이 가장 길게 {gapMax*1000:0}ms). 이어진 두 화면의 다른 화소(가장 큰 값): 제목 {title}, 표 {table}, 목록 {list}. 첫 화면과 견줘: 제목 {titleFirst}, 표 {tableFirst}, 목록 {listFirst}. 그 사이 {reads}번 읽어 첫 화면과 값이 같았던 것 {readsSame}");
   }

   // ② ↑ 를 누른 채 첫 곡으로
   int at=Where(Settle().Frame,0);
   if(!quick)
   {
    int from=Where(See().Frame,0);double t0=game.Now,quiet=t0;int changes=0;var lastSeen=See();
    game.Key(ScanKey.Up,true);
    try{while(game.Now-quiet<CollectionScan.FirstQuiet){if(game.Now-t0>CollectionScan.FirstLimit)throw new Halt("3분 안에 첫 곡에 닿지 못했다");game.Wait(0.03);var s=See();if(!Same(lastSeen,s)){changes++;lastSeen=s;quiet=game.Now;}}}
    finally{game.Key(ScanKey.Up,false);}
    double took=quiet-t0;var top=Settle();int bar=CollectionScreen.Bar(top.Frame);at=Where(top.Frame,0);
    Say($"② ↑ 를 누른 채: {from}번째 곡에서 시작해 {took:0.0}초 동안 화면이 {changes}번 바뀌고 멈췄다(초당 {(took>0?(from>0?from:changes)/took:0):0.0}곡). 멈춘 곳: 차례의 {at}번째 '{(at>=0?order[at].Name:"?")}', 띠 {bar}, 띠 위의 줄 {(bar>=0?CollectionScreen.RowAbove(top.Frame,bar):-1)}(0 이어야 첫 곡)");
    if(at!=0)
    {
     // 누른 채로는 올라가지 않았다(보낸 키는 Windows 가 되풀이해 주지 않는다. 게임이 스스로 되풀이하지 않으면 한 번만 움직인다): 짧게 여러 번 눌러 올라간다
     t0=game.Now;quiet=t0;int taps=0;lastSeen=See();
     while(game.Now-quiet<CollectionScan.FirstQuiet){if(game.Now-t0>CollectionScan.FirstLimit)throw new Halt("3분 안에 첫 곡에 닿지 못했다");Tap(ScanKey.Up,0.03);taps++;game.Wait(0.04);var s=See();if(!Same(lastSeen,s)){lastSeen=s;quiet=game.Now;}}
     top=Settle();bar=CollectionScreen.Bar(top.Frame);at=Where(top.Frame,0);
     Say($"   누른 채로는 첫 곡에 닿지 못해 ↑ 를 짧게 {taps}번 눌렀다({game.Now-t0:0.0}초). 멈춘 곳: 차례의 {at}번째, 띠 {bar}, 띠 위의 줄 {(bar>=0?CollectionScreen.RowAbove(top.Frame,bar):-1)}");
    }
    if(at!=0)throw new Halt("첫 곡에 닿지 못했다(또는 이 목록의 첫 곡이 차례의 첫 곡과 다르다)");
   }

   // ③ ↓ 를 한 번씩 40번
   {
    var moved=new List<double>();var settled=new List<double>();var bars=new List<int>();int wrongTitle=0,artistRight=0,artistWrong=0,artistUnsure=0,unsureCells=0,notMoved=0;
    int start=at,steps=quick?25:40;
    for(int k=1;k<=steps;k++)
    {
     var before=Settle();double t=game.Now;Tap(ScanKey.Down,CollectionScan.Press);
     double took=-1;Seen cur=before;while(game.Now-t<1.5){cur=See();if(!Same(before,cur)){took=game.Now-t;break;}}
     if(took<0){notMoved++;Say($"   {k}: 1.5초 안에 제목과 표가 바뀌지 않았다(목록의 다른 화소 {before.List.Differ(cur.List)})");at=Where(Settle().Frame,at);continue;}
     double lastChange=game.Now;var prev=cur;
     while(game.Now-lastChange<0.25&&game.Now-t<3){game.Wait(0.02);var s=See();if(prev.List.Differ(s.List)>CollectionScan.ListStill||!Same(prev,s))lastChange=game.Now;prev=s;}
     moved.Add(took);settled.Add(lastChange-t);
     int bar=CollectionScreen.Bar(prev.Frame);bars.Add(bar);int now=Where(prev.Frame,at+1);
     if(bar<0)Keep(prev.Frame,$"step-{k}-nobar");
     var shot=reader.Read(prev.Frame);int unsure=CollectionReader.SureCells(shot).Count(s=>!s);unsureCells+=unsure;
     string row="띠를 찾지 못함";
     if(bar>=0&&now>0)
     {
      var letters=CollectionScreen.ListLetters(prev.Frame);var costs=artists.ToDictionary(a=>a,_=>1.0);
      for(int d=-3;d<=3;d++){int y=bar+CollectionScreen.ArtistAbove+d;if(y<0||y+CollectionScreen.ArtistHeight>CollectionScreen.ListHeight)continue;foreach(var (artist,cost) in reader.Artists(letters,y,artists))if(cost<costs[artist])costs[artist]=cost;}
      var best=costs.OrderBy(c=>c.Value).Take(2).ToList();string want=order[now-1].Artist;
      bool sure=best[0].Value<=CollectionScan.ArtistSure&&best[1].Value-best[0].Value>=CollectionScan.ArtistGap;
      if(!sure)artistUnsure++;else if(best[0].Key==want)artistRight++;else artistWrong++;
      if(best[0].Key!=want)Keep(prev.Frame,$"step-{k}-artist");
      row=$"윗줄 '{best[0].Key}' {best[0].Value:0.000}(다음 {best[1].Value:0.000}){(best[0].Key==want?"":" 기대 '"+want+"'")}{(sure?"":" 확실하지 않음")}";
     }
     if(now!=at+1)wrongTitle++;
     Say($"   {k}: 바뀜 {took*1000:0}ms, 목록 멈춤 {(lastChange-t)*1000:0}ms, 띠 {bar}, 위 {(bar>=0?CollectionScreen.RowAbove(prev.Frame,bar):-1)}, 아래 {(bar>=0?CollectionScreen.RowBelow(prev.Frame,bar):-1)}, 차례의 {now}번째 '{(now>=0?order[now].Name:"?")}'{(now==at+1?"":" ← 기대는 "+(at+1)+"번째")}, 확실하지 않은 칸 {unsure}, {row}");
     if(now>=0)at=now;
    }
    Say($"③ ↓ 를 한 번씩 {steps}번({start}번째 곡에서. 누름 {CollectionScan.Press*1000:0}ms): 한 곡씩 옮겨지지 않은 것 {wrongTitle}, 1.5초 안에 바뀌지 않은 것 {notMoved}. 바뀌는 데 {Stats(moved)}. 목록이 멈추는 데 {Stats(settled)}");
    Say($"   띠의 자리: {string.Join(" ",bars)}");
    Say($"   윗줄의 아티스트(곡 목록의 {artists.Count}명 모두를 후보로): 맞게 정함 {artistRight}, 확실하지 않음 {artistUnsure}, 틀리게 정함 {artistWrong}. 확실하지 않은 칸 모두 {unsureCells}");
   }

   // ④ ↓ 를 빠르게 20번씩
   foreach(var (press,gap) in quick?Array.Empty<(double,double)>():new[]{(0.05,0.15),(0.05,0.10),(0.03,0.10),(0.03,0.07),(0.03,0.05),(0.02,0.04)})
   {
    int from=Where(Settle().Frame,at);double t=game.Now;
    for(int i=0;i<20;i++){Tap(ScanKey.Down,press);game.Wait(gap);}
    double took=game.Now-t;game.Wait(1.0);int to=Where(Settle().Frame,from+20);
    Say($"④ 누름 {press*1000:0}ms·사이 {gap*1000:0}ms 로 ↓ 20번({took:0.0}초): {(from>=0&&to>=0?(to-from)+"곡 옮겨짐":"곡을 알아보지 못함")}({from} → {to}){(to-from==20?"":"  ← 20 이 아니다")}");
    if(to>=0)at=to;
   }

   // ⑤ ↑ 를 1초 누르고 뗀 뒤 더 움직이는가
   if(!quick)
   {
    int from=Where(Settle().Frame,at);game.Key(ScanKey.Up,true);try{game.Wait(1.0);}finally{game.Key(ScanKey.Up,false);}
    game.Wait(0.3);int soon=Where(See().Frame,at);game.Wait(1.0);int later=Where(Settle().Frame,at);
    Say($"⑤ ↑ 를 1초 누르고 뗌: {from} → 뗀 0.3초 뒤 {soon} → 1.3초 뒤 {later}. {(soon==later?"뗀 뒤로 더 움직이지 않았다":"뗀 뒤에도 움직였다")}(1초에 {from-later}곡)");
    if(later>=0)at=later;
   }

   // ⑥ 시작·중지 단축키를 등록한 채 그 키를 보냈을 때 게임도 받는가
   {
    bool startOn=false,stopOn=false;ScanHotkeys? hot=null;
    listener.Invoke(()=>{hot=new ScanHotkeys(listener.Handle);startOn=hot.SetStart(true);stopOn=hot.SetStop(true);});
    try
    {
     Say($"⑥ 단축키 등록: Ctrl+Enter {(startOn?"됨":"안 됨(다른 프로그램이 쓰는 중)")}, Backspace {(stopOn?"됨":"안 됨(다른 프로그램이 쓰는 중)")}");
     var before=Settle();string Key(CollectionFrame f){var shot=reader.Read(f);return shot.Title?.Text+"|"+string.Join(",",shot.Cells.Select(c=>c.Text))+"|"+shot.Best;}
     string beforeKey=Key(before.Frame);Keep(before.Frame,"hotkey-0-before");
     string Tell(Seen after)=>$"게임 화면: {(CollectionScreen.IsMusicData(after.Frame)?"MUSIC DATA 그대로":"MUSIC DATA 가 아니게 됨")}, 읽은 제목과 16칸 {(Key(after.Frame)==beforeKey?"그대로":"바뀜")}(글자 그림의 다른 화소: 제목 {before.Title.Differ(after.Title)}, 표 {before.Table.Differ(after.Table)}, 목록 {before.List.Differ(after.List)})";
     if(startOn)
     {
      game.Send(CollectionGame.ControlKey,false,true);game.Send(CollectionGame.EnterKey,false,true);game.Wait(0.05);game.Send(CollectionGame.EnterKey,false,false);game.Send(CollectionGame.ControlKey,false,false);
      game.Wait(1.0);var after=Settle();Keep(after.Frame,"hotkey-1-ctrl-enter");
      Say($"   Ctrl+Enter 를 보냄: 이 도구가 받은 횟수 {listener.Starts}. {Tell(after)}");
      if(!CollectionScreen.IsMusicData(after.Frame))throw new Halt("Ctrl+Enter 를 게임이 받아 화면이 바뀌었다");
     }
     if(stopOn)
     {
      game.Send(CollectionGame.BackspaceKey,false,true);game.Wait(0.05);game.Send(CollectionGame.BackspaceKey,false,false);
      game.Wait(1.0);var after=Settle();Keep(after.Frame,"hotkey-2-backspace");
      Say($"   Backspace 를 보냄: 이 도구가 받은 횟수 {listener.Stops}. {Tell(after)}");
     }
    }
    finally{listener.Invoke(()=>hot?.Dispose());}
   }
   Say($"Windows 가 받지 않은 키 {game.Refused}");
  }
  catch(Halt halt){Say("멈춤: "+halt.Message);end="도중에 멈췄습니다: "+halt.Message;}
  finally
  {
   game.Key(ScanKey.Up,false);game.Key(ScanKey.Down,false);game.Send(CollectionGame.EnterKey,false,false);game.Send(CollectionGame.ControlKey,false,false);game.Send(CollectionGame.BackspaceKey,false,false);
   File.WriteAllText(path,log.ToString());
  }
  return end;
 }
}
