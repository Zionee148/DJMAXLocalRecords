using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
namespace LadderRecords;

// 투컴: 송출 PC 가 오버레이를 받아 갈 연결을 고르고 기억하고, 송출용 서버(OverlayServer.Outer.cs)를 그 연결의 주소에 열어 두는 일.
// 화면(주 화면의 줄, 선택 창)은 이 파일에 없다.

/// <summary>송출 주소로 쓸 수 있는 연결 하나. Id 는 Windows 가 연결에 붙인 고유 번호(이름이나 주소가 바뀌어도 같다), Public 은 사설 대역이 아닌 주소인가</summary>
record NetLink(string Id,string Name,IPAddress Address,int Prefix,bool Public);

static class NetLinks
{
 /// <summary>사설 대역(10.x, 172.16~31.x, 192.168.x)이 아닌 IPv4 인가. 통신사가 나눠 쓰는 대역(100.64~127.x)도 같은 통신사의 다른 기기가 닿을 수 있어 '공인 IP'로 알린다</summary>
 public static bool IsPublic(IPAddress address)
 {
  var b=address.GetAddressBytes();
  return!(b[0]==10||b[0]==172&&b[1]>=16&&b[1]<=31||b[0]==192&&b[1]==168||b[0]==127);
 }
 /// <summary>
 /// 지금 쓸 수 있는 연결들: 켜져 있고, IPv4 주소가 있고(자동으로 붙은 169.254 는 제외), 기본 게이트웨이가 있는 것.
 /// 게이트웨이를 보는 까닭: Hyper-V·WSL·VirtualBox 의 내부용 가상 연결을 뺀다(그것들이 끼면 "후보가 하나면 바로 주소"가 깨진다).
 /// </summary>
 /// <summary>연결 목록을 읽는 자리(검사에서 꾸민 목록으로 바꿔 끼운다)</summary>
 public static Func<IReadOnlyList<NetLink>> Source=Read;
 public static IReadOnlyList<NetLink> Read()
 {
  var found=new List<NetLink>();
  try
  {
#if DEVELOPMENT_CHECKS
   // 개발용: 게이트웨이가 없는 연결도 보인다(가상 PC 를 내부 전용으로 붙여 시험할 때)
   bool everything=Environment.GetEnvironmentVariable("DJMAX_SENDER_ALL_LINKS")=="1";
#else
   const bool everything=false;
#endif
   foreach(var link in NetworkInterface.GetAllNetworkInterfaces())
   {
    if(link.OperationalStatus!=OperationalStatus.Up||link.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)continue;
    var properties=link.GetIPProperties();
    bool routed=properties.GatewayAddresses.Any(g=>g.Address.AddressFamily==AddressFamily.InterNetwork&&!g.Address.Equals(IPAddress.Any));
    if(!routed&&!everything)continue;
    var address=properties.UnicastAddresses.FirstOrDefault(a=>a.Address.AddressFamily==AddressFamily.InterNetwork&&!(a.Address.GetAddressBytes() is [169,254,..])&&!IPAddress.IsLoopback(a.Address));
    if(address==null)continue;
    found.Add(new NetLink(link.Id,link.Name,address.Address,address.PrefixLength,IsPublic(address.Address)));
   }
  }
  catch(NetworkInformationException){}
  return found;
 }
}

/// <summary>기억해 두는 것(preferences.json 의 Sender). Mark 는 마지막으로 내준 송출 주소의 지문이다(설정 파일에 IP 를 그대로 남기지 않는다)</summary>
record SenderSettings(bool Allowed=false,string? LinkId=null,string? LinkName=null,string? Key=null,string? Mark=null,bool Changed=false,bool PublicNoted=false);

enum SenderState
{
 Off,          // 투컴 연결 허용이 꺼져 있다
 NoLink,       // 연결 없음: 고른 연결이 없고 쓸 수 있는 연결도 없다
 ChooseLink,   // 연결 선택 필요: 후보가 여럿인데 아직 고르지 않았다
 LinkGone,     // 선택한 연결 없음: 고른 연결이 지금 없다(랜선을 뽑음, Wi-Fi 를 끔). 돌아오면 저절로 다시 쓴다
 PortBusy,     // 포트 사용 중: 다른 프로그램이 그 주소의 포트를 쓰고 있다(그렇게 확인된 때만). 풀리면 저절로 다시 연다
 OpenFailed,   // 주소 열기 실패: 그 밖의 까닭으로 그 주소의 포트를 열지 못했다. 다시 열어 본다
 Waiting,      // 연결 대기: 송출용 서버는 열려 있고 아직 받아 간 적이 없다
 Connected,    // 연결됨: 3초 안에 받아 갔다
 Lost,         // 연결 끊김: 받아 가다가 3초 넘게 없다
}

// 고른 연결과 송출용 서버의 상태를 맞춘다. 주 화면이 가끔(주소가 바뀌었다는 Windows 의 알림, 몇 초마다) Refresh 를 부르고, 화면을 그릴 때 State 를 읽는다.
// 연결 목록과 저장은 밖에서 넣어 준다(검사에서는 가짜 목록을 넣는다)
sealed class SenderLink
{
 readonly OverlayServer server;readonly Func<IReadOnlyList<NetLink>> links;readonly Action<SenderSettings> save;
 SenderSettings settings;bool gone;SenderState? trouble;   // trouble: 송출용 서버를 열지 못한 까닭(PortBusy 또는 OpenFailed)
 public SenderLink(OverlayServer server,SenderSettings? settings,Func<IReadOnlyList<NetLink>> links,Action<SenderSettings> save)
 {
  this.server=server;this.links=links;this.save=save;this.settings=settings??new();
  if(this.settings.Key!=null&&!OverlayServer.ValidKey(this.settings.Key))this.settings=this.settings with{Key=null,Mark=null,Changed=false};   // 손상된 키는 버리고 다시 만든다
 }
 public SenderSettings Settings=>settings;
 public bool Allowed=>settings.Allowed;
 /// <summary>지금 고른 연결(지금 쓸 수 있을 때만)</summary>
 public NetLink? Link{get;private set;}
 /// <summary>지금 쓸 수 있는 연결들(마지막으로 읽은 것)</summary>
 public IReadOnlyList<NetLink> Candidates{get;private set;}=Array.Empty<NetLink>();
 /// <summary>송출 PC 에 넣을 주소. 송출용 서버가 열려 있을 때만 있다</summary>
 public string? Url=>server.OuterAddress is {} address&&settings.Key is {} key?OverlayServer.OuterUrl(address,server.Port,key):null;
 /// <summary>기존 송출 주소가 바뀌었고 새 주소로 아직 연결되지 않았다(안내를 보인다)</summary>
 public bool AddressChanged=>settings.Allowed&&settings.Changed&&server.OuterAddress!=null;
 /// <summary>후보가 하나라 저절로 골라진 연결이 공인 주소다(주 화면에 한 번 알린다: 연결이 확인되거나 프로그램을 끌 때까지)</summary>
 public bool PublicNotice{get;private set;}

 // 설정을 바꾸고 저장한다. 저장하지 못해도(파일이 읽기 전용이거나 잠겨 있음) 지금의 동작은 바뀐 값대로 간다: 송출용 서버를 여닫는 일이 저장의 성패에 묶이지 않게.
 // 저장하지 못한 까닭은 SaveError 에 남고(주 화면이 한 번 알린다) 다음에 저장되면 지워진다
 void Store(SenderSettings next)
 {
  if(next==settings)return;settings=next;
  try{save(next);SaveError=null;}
  catch(Exception e)when(e is IOException or UnauthorizedAccessException){SaveError=e;}
 }
 /// <summary>마지막 저장이 실패했으면 그 까닭(다음 실행에는 이전 설정으로 뜬다)</summary>
 public Exception? SaveError{get;private set;}
 static string Mark(IPAddress address,int port,string key)=>Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes($"{address}:{port}/{key}")))[..16];

 /// <summary>지금의 상태</summary>
 public SenderState State
 {
  get
  {
   if(!settings.Allowed)return SenderState.Off;
   if(settings.LinkId==null)return Candidates.Count==0?SenderState.NoLink:SenderState.ChooseLink;
   if(gone)return SenderState.LinkGone;
   if(trouble is {} why)return why;
   if(server.OuterAddress==null)return SenderState.LinkGone;
   long seen=server.OuterSeenAt;
   return seen==0?SenderState.Waiting:Environment.TickCount64-seen<=3000?SenderState.Connected:SenderState.Lost;
  }
 }

 /// <summary>'투컴 연결 허용'을 켜거나 끈다. 고른 연결과 키는 기억한다.
 /// **투컴 연결을 끄면 설정을 저장하기 전에 송출용 서버를 닫는다**: 설정을 저장하지 못해도 송출은 곧바로 멈춘다</summary>
 public void Allow(bool on)
 {
  if(!on)
  {
   bool confirmed=server.OuterSeenAt>0;
   server.CloseOuter();Link=null;gone=false;trouble=null;PublicNotice=false;
   Store(settings with{Allowed=false,Changed=settings.Changed&&!confirmed});
   return;
  }
  Store(settings with{Allowed=true});Refresh();
 }

 /// <summary>연결을 고른다(선택 창의 적용)</summary>
 public void Choose(NetLink link){Store(settings with{LinkId=link.Id,LinkName=link.Name});PublicNotice=false;Refresh();}

 /// <summary>새로 만들 수 있는가: **지금 복사할 수 있는 송출 주소가 있을 때만**(송출용 서버가 열려 있다). 연결이 사라졌거나 포트를 열지 못한 동안에는 새 주소를 내줄 수 없으므로 만들지 않는다</summary>
 public bool CanRenew=>settings.Allowed&&Url!=null;
 /// <summary>연결 키를 새로 만든다(선택 창의 '송출 주소 새로 만들기'). 옛 주소의 조회는 곧바로 거절된다. 지금 송출 주소가 없으면 아무 일도 하지 않는다.
 /// **새 키를 설정에 저장한 뒤에만 바꾼다**: 저장하지 못한 채 바꾸면 화면에서 복사한 새 주소와 다음 실행이 읽는 주소가 달라져, 다시 켠 뒤에 새 주소는 끊기고 버렸다고 안 예전 주소가 되살아난다.
 /// 저장하지 못했으면 기존 주소를 그대로 두고 false 를 돌려준다(까닭은 SaveError)</summary>
 public bool Renew()
 {
  if(!CanRenew)return false;
  var before=settings;string key=OverlayServer.NewKey();
  Store(settings with{Key=key});
  if(SaveError!=null){settings=before;return false;}
  server.SetOuterKey(key);
  Refresh();return true;
 }

 /// <summary>송출용 서버를 열지 못한 까닭을 가른다: 다른 프로그램이 그 포트를 쓰는 중으로 확인된 때만 '포트 사용 중', 그 주소가 (방금) 없어졌으면 연결이 없는 것, 그 밖에는 '주소 열기 실패'</summary>
 public static SenderState Classify(SocketError error)=>error switch{SocketError.AddressAlreadyInUse=>SenderState.PortBusy,SocketError.AddressNotAvailable=>SenderState.LinkGone,_=>SenderState.OpenFailed};

 /// <summary>연결 목록을 다시 읽고 송출용 서버를 맞춘다: 고른 연결의 지금 주소에서 열려 있게 하고, 연결이 없으면 닫고, 주소가 바뀌었으면 다시 연다</summary>
 public void Refresh()
 {
  Confirm();   // 송출용 서버를 닫거나 다시 열기 전에: 방금까지의 주소로 연결이 확인됐던 것을 놓치지 않는다
  if(!settings.Allowed){server.CloseOuter();Link=null;gone=false;trouble=null;return;}
  Candidates=links();
  // 처음: 후보가 하나면 그것을 쓴다. 여럿이면 사용자가 고를 때까지 기다린다. 한 번 고른 뒤에는 그 연결이 사라져도 다른 연결로 옮기지 않는다
  if(settings.LinkId==null)
  {
   if(Candidates.Count!=1){server.CloseOuter();Link=null;gone=false;trouble=null;return;}
   var only=Candidates[0];Store(settings with{LinkId=only.Id,LinkName=only.Name});
   if(only.Public&&!settings.PublicNoted){PublicNotice=true;Store(settings with{PublicNoted=true});}
  }
  var link=Candidates.FirstOrDefault(c=>c.Id==settings.LinkId);
  if(link==null){server.CloseOuter();Link=null;trouble=null;gone=true;return;}
  gone=false;Link=link;if(link.Name!=settings.LinkName)Store(settings with{LinkName=link.Name});
  if(settings.Key==null)Store(settings with{Key=OverlayServer.NewKey()});
  if(server.OuterAddress==null||!server.OuterAddress.Equals(link.Address))
  {
   try{server.OpenOuter(link.Address,link.Prefix,settings.Key!);trouble=null;}
   catch(SocketException e)
   {
    // 다음 Refresh 에서 다시 열어 본다(포트가 풀리면 저절로 '연결 대기'가 된다)
    var why=Classify(e.SocketErrorCode);
    if(why==SenderState.LinkGone){gone=true;trouble=null;Link=null;}else trouble=why;
    return;
   }
  }
  // 주소는 그대로인데 서브넷의 범위만 바뀌었으면(공유기나 가상 네트워크의 설정) 받는 범위를 새 값으로 맞춘다. OBS 에 넣는 주소가 같으므로 송출용 서버를 다시 열거나 안내를 띄우지 않는다
  else if(server.OuterPrefix!=link.Prefix)server.SetOuterPrefix(link.Prefix);
  // 주소 변경: 전에 내준 주소가 있고 지금의 것과 다르면 안내를 켠다. 처음 생긴 주소는 그냥 적어 둔다
  string mark=Mark(link.Address,server.Port,settings.Key!);
  if(settings.Mark==null)Store(settings with{Mark=mark});
  else if(settings.Mark!=mark)Store(settings with{Mark=mark,Changed=true});
  Confirm();
 }
 /// <summary>지금 주소로 연결이 확인됐으면(키가 맞는 조회가 왔으면) 주소 변경 안내와 공인 IP 안내를 끈다. 복사만으로는 끄지 않는다</summary>
 public void Confirm(){if(server.OuterSeenAt>0){if(settings.Changed)Store(settings with{Changed=false});PublicNotice=false;}}
}
