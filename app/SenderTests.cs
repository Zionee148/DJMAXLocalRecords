#if DEVELOPMENT_CHECKS
using System.Net;
using System.Net.Sockets;
using System.Text;
namespace LadderRecords;

// 투컴의 연결 고르기·기억·주소 변경·키 새로 만들기의 검사(자체 검사 --self-test 가 부른다. 개발용 빌드에만 들어간다).
// 실제 연결 목록 대신 꾸민 목록을 넣고, 송출용 서버는 이 PC 안의 다른 루프백 주소(127.0.0.x)에 연다(루프백이라 Windows 방화벽 창이 뜨지 않는다).
// 실제 네트워크와 다른 기기는 보지 않는다. 결과 sender-validation.txt
[System.Runtime.Versioning.SupportedOSPlatform("windows10.0.19041")]
static class SenderTests
{
 const int Port=18782;
 public static void Run()
 {
  var notes=new List<string>();
  void Check(bool condition,string name){if(!condition)throw new Exception("Sender: "+name);notes.Add("PASS: "+name);}
  using var server=new OverlayServer(Port);
  NetLink Link(string id,string name,int last,bool open=false)=>new(id,name,IPAddress.Parse("127.0.0."+last),8,open);
  NetLink ether=Link("A","이더넷",2),wifi=Link("B","Wi-Fi",3),moved=Link("B","Wi-Fi",5),outside=Link("P","이더넷 2",4,true);
  var now=new List<NetLink>();SenderSettings? saved=null;int saves=0;bool unwritable=false;
  SenderLink Make(SenderSettings? from)=>new(server,from,()=>now.ToList(),s=>{if(unwritable)throw new IOException("설정 파일을 쓸 수 없음(시험)");saved=s;saves++;});
  // 송출 PC 의 조회 흉내: 그 주소의 송출용 서버에 상태를 물어 답의 번호를 받는다(닿지 않으면 0)
  int Ask(IPAddress address,string? key)
  {
   try
   {
    using var client=new TcpClient();client.Connect(address,Port);var stream=client.GetStream();stream.ReadTimeout=3000;
    stream.Write(Encoding.ASCII.GetBytes($"GET /o/{key}/state HTTP/1.1\r\n\r\n"));
    using var reader=new StreamReader(stream,Encoding.ASCII);string? line=reader.ReadLine();return line==null?0:int.Parse(line.Split(' ')[1]);
   }
   catch(Exception e)when(e is IOException or SocketException){return 0;}
  }

  // 공인 IP 의 범위: 사설 대역이 아니면 모두
  Check(!NetLinks.IsPublic(IPAddress.Parse("192.168.1.2"))&&!NetLinks.IsPublic(IPAddress.Parse("10.1.1.1"))&&!NetLinks.IsPublic(IPAddress.Parse("172.16.0.1"))&&!NetLinks.IsPublic(IPAddress.Parse("172.31.255.1"))
   &&NetLinks.IsPublic(IPAddress.Parse("172.32.0.1"))&&NetLinks.IsPublic(IPAddress.Parse("100.64.0.1"))&&NetLinks.IsPublic(IPAddress.Parse("8.8.8.8"))&&NetLinks.IsPublic(IPAddress.Parse("192.169.0.1")),"private ranges are not public, everything else is");
  _=NetLinks.Read();notes.Add("PASS: reading this PC's links does not fail");

  // 처음: 꺼져 있다. 켰는데 연결이 없으면 '연결 없음', 연결이 하나 생기면 저절로 골라 '연결 대기'
  var link=Make(null);
  Check(link.State==SenderState.Off&&server.OuterAddress==null&&link.Url==null,"off by default, no door");
  link.Allow(true);
  Check(link.State==SenderState.NoLink&&saved is {Allowed:true,LinkId:null}&&server.OuterAddress==null,"allowed with no link: 'no link', allowance remembered");
  now.Add(ether);link.Refresh();
  Check(link.State==SenderState.Waiting&&Equals(server.OuterAddress,ether.Address)&&saved is {LinkId:"A",LinkName:"이더넷",Changed:false}&&OverlayServer.ValidKey(saved.Key)&&saved.Mark!=null&&!link.AddressChanged&&!link.PublicNotice,"a single link is chosen by itself: waiting, key made, no change notice");
  string key=saved!.Key!;
  Check(link.Url==$"http://127.0.0.2:{Port}/o/{key}/","the address to copy");
  Check(!saved.Mark!.Contains("127")&&saved.ToString().IndexOf("127.0.0",StringComparison.Ordinal)<0,"the settings do not hold the IP");
  // 연결됨 → 3초 넘게 없으면 연결 끊김
  Check(Ask(ether.Address,key)==200&&link.State==SenderState.Connected,"a keyed status request makes it 'connected'");
  Thread.Sleep(3200);
  Check(link.State==SenderState.Lost,"no request for over 3 seconds: 'lost'");
  Check(Ask(ether.Address,key)==200&&link.State==SenderState.Connected,"connected again on the next request");

  // 처음인데 후보가 여럿: 고를 때까지 '연결 선택 필요'. 고르면 그 연결에 연다
  now.Clear();now.Add(ether);now.Add(wifi);
  link=Make(null);link.Allow(true);
  Check(link.State==SenderState.ChooseLink&&server.OuterAddress==null&&link.Url==null&&link.Candidates.Count==2,"several links and no choice: 'choose a link', no door");
  link.Choose(wifi);
  Check(link.State==SenderState.Waiting&&Equals(server.OuterAddress,wifi.Address)&&saved is {LinkId:"B",Changed:false},"the chosen link is used");
  key=saved!.Key!;
  // 고른 연결이 사라짐: 다른 연결이 남아 있어도 옮기지 않는다. 돌아오면 저절로 다시 쓰고, 주소가 같으면 안내가 없다
  now.Remove(wifi);link.Refresh();
  Check(link.State==SenderState.LinkGone&&server.OuterAddress==null&&link.Url==null&&saved is {LinkId:"B"}&&Ask(ether.Address,key)==0,"the chosen link is gone: 'chosen link missing', not moved to the other link");
  now.Add(wifi);link.Refresh();
  Check(link.State==SenderState.Waiting&&saved!.Key==key&&!link.AddressChanged&&Ask(wifi.Address,key)==200,"the link returns with the same address: same key, no change notice");
  // 주소가 바뀜: 안내가 켜지고, 새 주소로 연결이 확인될 때까지 남는다(다시 켜도 남는다)
  link.Refresh();   // 방금의 조회로 연결이 확인됐다
  now.Remove(wifi);now.Add(moved);link.Refresh();
  Check(Equals(server.OuterAddress,moved.Address)&&link.AddressChanged&&saved is {Changed:true}&&link.State==SenderState.Waiting&&saved.Key==key,"the address changed: notice on, key kept");
  var restarted=Make(saved);restarted.Refresh();
  Check(restarted.AddressChanged&&restarted.State==SenderState.Waiting&&saved!.Key==key,"after a restart the notice is still on");
  Check(Ask(moved.Address,key)==200,"the new address answers");
  restarted.Refresh();
  Check(!restarted.AddressChanged&&saved is {Changed:false}&&restarted.State==SenderState.Connected,"connected on the new address: notice off");
  link=restarted;
  // 다시 켜도 같은 연결·같은 키. 허용을 끄면 송출용 서버가 닫히고, 다시 켜면 같은 주소다(안내 없음)
  link.Allow(false);
  Check(link.State==SenderState.Off&&server.OuterAddress==null&&Ask(moved.Address,key)==0&&saved is {Allowed:false,LinkId:"B"}&&saved.Key==key,"turning it off closes the door and keeps the choice and the key");
  link.Allow(true);
  Check(link.State==SenderState.Waiting&&saved!.Key==key&&!link.AddressChanged,"turning it on again: same address, no notice");
  // 연결 키 새로 만들기: 옛 주소는 곧바로 거절, 고른 연결은 그대로, 안내가 켜진다
  Check(link.CanRenew&&link.Renew()&&saved!.Key!=key&&OverlayServer.ValidKey(saved.Key)&&saved.LinkId=="B"&&link.AddressChanged,"a new key: choice kept, change notice on");
  Check(Ask(moved.Address,key)==404&&Ask(moved.Address,saved!.Key)==200,"the old key is refused at once, the new one answers");
  // 연결이 확인된 바로 뒤에 연결이 사라졌다 돌아와도(그 사이에 다시 읽은 적이 없어도) 안내는 꺼진 채로 남는다
  now.Remove(moved);link.Refresh();now.Add(moved);link.Refresh();
  Check(!link.AddressChanged&&link.State==SenderState.Waiting&&saved is {Changed:false},"connected with the new key, then the link drops and returns: notice stays off");
  Check(!Make(null).CanRenew&&!Make(new SenderSettings(true,"B","Wi-Fi")).CanRenew,"no address yet: nothing to renew");
  // 지금 복사할 주소가 없으면(고른 연결이 사라짐) 새로 만들 수 없다: 옛 주소만 못 쓰게 되고 새 주소를 내줄 수 없으므로. 키도 그대로다
  {string before=saved!.Key!;now.Remove(moved);link.Refresh();
   Check(link.State==SenderState.LinkGone&&!link.CanRenew&&!link.Renew()&&saved.Key==before,"no address to copy right now (link gone): nothing is renewed");
   now.Add(moved);link.Refresh();Check(link.CanRenew&&link.State==SenderState.Waiting,"the link returns: renewing is possible again");}
  // 주소는 그대로이고 서브넷의 범위만 바뀜: 받는 범위가 새 값이 되고(이 검사의 연결은 범위 밖이 돼 답이 없다), 주소 변경 안내는 없다. 되돌리면 다시 받는다
  {var narrow=moved with{Prefix=32};now.Remove(moved);now.Add(narrow);link.Refresh();
   Check(server.OuterPrefix==32&&Ask(moved.Address,saved!.Key)==0&&!link.AddressChanged&&link.State==SenderState.Waiting,"same address, narrower subnet: the new range applies, no change notice");
   now.Remove(narrow);now.Add(moved);link.Refresh();Check(server.OuterPrefix==8&&Ask(moved.Address,saved.Key)==200,"the range widens back");link.Refresh();}
  // 허용을 끌 때 설정을 쓰지 못해도 송출은 곧바로 멈춘다. 저장하지 못한 까닭이 남고, 다시 쓸 수 있게 되면 지워진다
  {unwritable=true;link.Allow(false);
   Check(link.State==SenderState.Off&&server.OuterAddress==null&&Ask(moved.Address,saved!.Key)==0&&link.SaveError is IOException&&saved.Allowed,"turning it off stops the door even when the settings cannot be written");
   unwritable=false;link.Allow(true);Check(link.SaveError==null&&link.State==SenderState.Waiting&&saved is {Allowed:true},"writable again: the error clears");
   unwritable=true;now.Remove(moved);now.Add(wifi);link.Refresh();Check(Equals(server.OuterAddress,wifi.Address)&&link.SaveError!=null,"an unwritable settings file does not stop the door from following the link");
   unwritable=false;now.Remove(wifi);now.Add(moved);link.Refresh();}
  // 새로 만들 때 새 키를 저장하지 못하면 바꾸지 않는다: 기존 주소가 그대로 답하고, 주소 변경 안내도 켜지지 않는다. 쓸 수 있게 되면 새로 만들어진다
  {string before=saved!.Key!;bool noticed=link.AddressChanged;unwritable=true;
   Check(link.CanRenew&&!link.Renew()&&link.SaveError is IOException&&link.Settings.Key==before&&saved.Key==before&&link.Url!.Contains(before)&&Ask(moved.Address,before)==200&&link.AddressChanged==noticed,"a new key that cannot be saved is not used: the old address keeps answering");
   unwritable=false;
   Check(link.Renew()&&link.SaveError==null&&saved!.Key!=before&&link.Settings.Key==saved.Key&&Ask(moved.Address,before)==404&&Ask(moved.Address,saved.Key)==200,"writable again: the new key is saved first and then used");
   link.Refresh();}
  // 송출용 서버를 열지 못한 까닭: 다른 프로그램이 쓰는 중으로 확인된 때만 '포트 사용 중'
  Check(SenderLink.Classify(SocketError.AddressAlreadyInUse)==SenderState.PortBusy&&SenderLink.Classify(SocketError.AddressNotAvailable)==SenderState.LinkGone
   &&SenderLink.Classify(SocketError.AccessDenied)==SenderState.OpenFailed&&SenderLink.Classify(SocketError.NetworkDown)==SenderState.OpenFailed,"only a confirmed 'in use' is 'port in use'; other failures are 'could not open'");
  // 포트를 다른 프로그램이 쓰는 중: '포트 사용 중'. 풀리면 다시 허용하지 않아도 '연결 대기'
  var busyLink=Link("C","로컬 영역 연결",6);var taken=new TcpListener(busyLink.Address,Port);taken.Start();
  now.Clear();now.Add(busyLink);
  link=Make(null);link.Allow(true);
  Check(link.State==SenderState.PortBusy&&link.Url==null&&saved is {LinkId:"C",Allowed:true}&&!link.CanRenew,"the port is taken: 'port in use', choice kept, nothing to renew");
  taken.Stop();link.Refresh();
  Check(link.State==SenderState.Waiting&&link.Url!=null,"the port is free again: waiting without allowing again");
  // 공인 주소인 연결이 저절로 골라지면 한 번 알린다(연결이 확인될 때까지). 다음 실행에는 다시 알리지 않는다. 직접 고른 때는 선택 창이 알린다
  now.Clear();now.Add(outside);
  link=Make(null);link.Allow(true);
  Check(link.State==SenderState.Waiting&&link.PublicNotice&&saved is {PublicNoted:true},"a public link chosen by itself: noticed once");
  Check(!Make(saved).PublicNotice,"not noticed again on the next start");
  Check(Ask(outside.Address,saved!.Key)==200,"the public link answers");link.Refresh();
  Check(!link.PublicNotice,"connected: the public notice is gone");
  now.Add(ether);link=Make(null);link.Allow(true);link.Choose(outside);
  Check(!link.PublicNotice&&link.Link is {Public:true},"a public link chosen by hand: the main window does not repeat the notice");
  // 손상된 키는 버리고 새로 만든다
  now.Clear();now.Add(ether);
  link=Make(new SenderSettings(true,"A","이더넷","not-a-key","x",true));link.Refresh();
  Check(OverlayServer.ValidKey(saved!.Key)&&link.State==SenderState.Waiting,"a broken saved key is replaced");
  link.Allow(false);

  // 방화벽 차단의 판단: 이 실행 파일의 들어오는 연결을 막는 켜진 규칙이 그 네트워크 유형에 걸려 있을 때만
  {
   string exe=Path.Combine(AppContext.BaseDirectory,"DJMAXLocalRecords.exe");const int port=18765;
   SenderFirewall.Rule Rule(string? app,bool enabled=true,bool inbound=true,bool block=true,int profiles=SenderFirewall.Public,int protocol=6,string? ports=null,bool narrowed=false)=>new(app,enabled,inbound,block,profiles,protocol,ports,narrowed);
   bool Blocked(params SenderFirewall.Rule[] rules)=>SenderFirewall.Blocks(rules,exe,SenderFirewall.Public,port);
   Check(Blocked(Rule(exe))&&Blocked(Rule(exe.ToUpperInvariant()))&&Blocked(Rule(exe,profiles:0x7FFFFFFF))&&Blocked(Rule(exe,protocol:256))&&Blocked(Rule(exe,ports:"*"))&&Blocked(Rule(exe,ports:"80, 18000-19000"))&&Blocked(Rule(exe,ports:"18765")),"a block rule for this program on this network type is a block");
   Check(!Blocked()&&!Blocked(Rule(exe,enabled:false))&&!Blocked(Rule(exe,inbound:false))&&!Blocked(Rule(exe,block:false))&&!Blocked(Rule(exe,profiles:SenderFirewall.Private))&&!Blocked(Rule(exe,protocol:17))
    &&!Blocked(Rule(exe,ports:"80,443"))&&!Blocked(Rule(Path.Combine(AppContext.BaseDirectory,"other.exe")))&&!Blocked(Rule(null))&&!Blocked(Rule("")),"other rules are not a block: off, outbound, allow, another network type, UDP, other ports, another program, no program");
   Check(SenderFirewall.Blocks(new[]{Rule(exe,profiles:SenderFirewall.Private|SenderFirewall.Domain)},exe,SenderFirewall.Private,port),"a rule on the private type blocks a private network");
   // 특정 연결·연결 종류·주소에만 걸린 규칙은 이 연결이 막혔다는 근거로 쓰지 않는다
   Check(!Blocked(Rule(exe,narrowed:true))&&Blocked(Rule(exe,narrowed:true),Rule(exe)),"a rule limited to some interface or address is not used as proof");
   Check(!SenderFirewall.IsNarrowed(null,null,null,null)&&!SenderFirewall.IsNarrowed(Array.Empty<object>(),"All","*","*")&&!SenderFirewall.IsNarrowed(null,""," * ","")
    &&SenderFirewall.IsNarrowed(new object[]{"이더넷"},"All","*","*")&&SenderFirewall.IsNarrowed(null,"Wireless","*","*")&&SenderFirewall.IsNarrowed(null,"All","192.168.0.5","*")&&SenderFirewall.IsNarrowed(null,"All","*","LocalSubnet"),"which rules count as limited");
   // 규칙을 읽지 못한 것이 있으면 "막지 않음"이라고 답하지 않는다: 막는 규칙이 따로 확인되면 막힘, 아니면 모름
   {SenderFirewall.Seen Got(SenderFirewall.Rule rule)=>new(rule);var unread=new SenderFirewall.Seen(null);
    bool? Decided(params SenderFirewall.Seen[] seen)=>SenderFirewall.Decide(seen,exe,SenderFirewall.Public,port);
    Check(Decided()==false&&Decided(Got(Rule(exe,narrowed:true)))==false&&Decided(Got(Rule(exe,ports:"80,443")))==false&&Decided(Got(Rule(exe)))==true,"every rule read: blocked or not blocked");
    Check(Decided(unread)==null&&Decided(unread,Got(Rule(exe,narrowed:true)))==null&&Decided(Got(Rule(exe,ports:"80,443")),unread)==null,"a rule that could not be read and no confirmed block: unknown, not 'not blocked'");
    Check(Decided(unread,Got(Rule(exe)))==true&&Decided(Got(Rule(exe)),unread)==true,"a confirmed block stays a block next to an unreadable rule");}
   notes.Add("INFO: this PC, read only: "+SenderFirewall.Probe());
   // 클립보드에 넣을 꼴: 글은 주소 그대로, 클립보드 기록과 기기 간 동기화에서 빼 달라는 표시 셋이 함께 든다(실제 클립보드는 건드리지 않는다)
   var data=SenderClipboard.Make("http://127.0.0.2:18765/o/0123456789abcdef0123456789abcdef/");
   Check((string?)data.GetData(DataFormats.UnicodeText)=="http://127.0.0.2:18765/o/0123456789abcdef0123456789abcdef/"&&SenderClipboard.Marks.All(mark=>data.GetDataPresent(mark)&&data.GetData(mark) is MemoryStream{Length:4}),"the copied address carries the marks that keep it out of the clipboard history");
  }
  // 설정 파일: 투컴 값이 함께 저장되고, 예전 파일(값이 없음)은 꺼진 것으로 읽힌다
  string file=Path.Combine(AppContext.BaseDirectory,"test-data",Guid.NewGuid()+".preferences.json");Directory.CreateDirectory(Path.GetDirectoryName(file)!);
  new UserOptions(false,true,true,false,saved).Save(file);var back=UserOptions.Load(file);
  Check(back.Sender==saved&&back.DiagnosticImages&&back.Dark,"the sender settings round-trip in the options file");
  File.WriteAllText(file,"{\"Fullscreen\":true,\"DiagnosticImages\":false,\"Dark\":true}");
  Check(UserOptions.Load(file).Sender==null,"an old options file has no sender settings");
  File.Delete(file);
  File.WriteAllLines(Path.Combine(AppContext.BaseDirectory,"sender-validation.txt"),notes);
 }
}
#endif
