using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
namespace LadderRecords;

// 투컴: 송출 PC 의 OBS 가 오버레이를 받아 가는 송출용 서버.
// 로컬 서버(127.0.0.1)와 따로 연다. 사용자가 고른 연결의 IPv4 주소 하나에만 묶고, 포트는 로컬 서버와 같다. '투컴 연결 허용'이 꺼져 있으면 열지 않는다.
// 다른 PC 에서 오는 연결을 받으므로 받는 범위를 좁힌다(공인 주소인 연결을 골라도 프로그램이 스스로 좁힌다):
//   - 고른 연결과 같은 서브넷에서 온 연결만 받는다. 그 밖에서 온 연결은 답 없이 끊는다
//   - 연결 키(무작위 128비트, 16진수 32자)가 든 길만 답한다: /o/<키>/ (오버레이 페이지), /o/<키>/state, /o/<키>/fonts/<이름>. 그 밖에는 모두 404 다.
//     곡 이름 목록(/titles), 팝업 크기 알림(/popup-fit), 키 없는 페이지는 바깥에서 받지 않는다
//   - GET 만, 요청은 2KB 까지, 동시에 16개까지, 요청을 읽는 데 3초까지
// 키는 평문으로 오간다(HTTPS 는 쓰지 않는다. OBS 에서 인증서 경고를 풀 수 없다). 나가는 것은 방송 화면에 그대로 보이는 내용(곡 이름, 점수, 오버레이 색)이다.
partial class OverlayServer
{
 public const int OuterRequestLimit=2048,OuterConnectionLimit=16;
 readonly object outerGate=new();
 TcpListener? outer;IPAddress? outerAddress;int outerPrefix;byte[] outerKey=Array.Empty<byte>();byte[]? outerHtml;
 long outerSeen;int outerBusy;
 long outerEpoch;   // 송출용 서버를 열거나 닫거나 키를 바꿀 때마다 는다. 그 전에 시작된 조회가 끝나면서 새 주소·새 키의 것으로 세이지 않게 한다

 /// <summary>새 연결 키</summary>
 public static string NewKey()=>Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
 public static bool ValidKey(string? key)=>key is {Length:32}&&key.All(c=>c is>='0' and<='9' or>='a' and<='f');
 /// <summary>송출용 서버가 묶여 있는 주소(닫혀 있으면 null)</summary>
 public IPAddress? OuterAddress{get{lock(outerGate)return outer==null?null:outerAddress;}}
 /// <summary>키가 맞는 상태 조회가 송출용 서버로 마지막에 온 때(Environment.TickCount64). 서버를 열거나 키를 바꾼 뒤로 온 적이 없으면 0</summary>
 public long OuterSeenAt=>Interlocked.Read(ref outerSeen);
 /// <summary>받는 범위(같은 서브넷으로 치는 앞 비트의 수)</summary>
 public int OuterPrefix{get{lock(outerGate)return outerPrefix;}}
 /// <summary>받는 범위만 바꾼다(주소는 그대로이고 서브넷의 범위만 바뀌었을 때). 서버를 다시 열지 않고 연결 확인도 그대로 둔다</summary>
 public void SetOuterPrefix(int prefixLength){lock(outerGate)outerPrefix=Math.Clamp(prefixLength,0,32);}
 /// <summary>송출 PC 에 넣을 주소</summary>
 public static string OuterUrl(IPAddress address,int port,string key)=>$"http://{address}:{port}/o/{key}/";

 /// <summary>송출용 서버를 연다(열려 있으면 닫고 다시 연다). 그 주소의 포트를 열 수 없으면 SocketException 이다(다른 프로그램이 쓰는 중이면 AddressAlreadyInUse)</summary>
 public void OpenOuter(IPAddress address,int prefixLength,string key)
 {
  if(address.AddressFamily!=AddressFamily.InterNetwork)throw new ArgumentException("IPv4 주소만 쓴다",nameof(address));
  if(!ValidKey(key))throw new ArgumentException("연결 키의 꼴이 아니다",nameof(key));
  lock(outerGate)
  {
   CloseOuterCore();
   var made=new TcpListener(address,Port);made.Start();
   outer=made;outerAddress=address;outerPrefix=Math.Clamp(prefixLength,0,32);outerKey=Encoding.ASCII.GetBytes(key);outerEpoch++;
   _=Task.Run(()=>OuterLoop(made));
  }
 }
 /// <summary>연결 키를 바꾼다. 옛 키의 조회는 곧바로 404 가 된다</summary>
 public void SetOuterKey(string key)
 {
  if(!ValidKey(key))throw new ArgumentException("연결 키의 꼴이 아니다",nameof(key));
  lock(outerGate){outerKey=Encoding.ASCII.GetBytes(key);outerEpoch++;Interlocked.Exchange(ref outerSeen,0);}
 }
 public void CloseOuter(){lock(outerGate)CloseOuterCore();}
 void CloseOuterCore(){try{outer?.Stop();}catch(SocketException){}outer=null;outerAddress=null;outerEpoch++;Interlocked.Exchange(ref outerSeen,0);}

 async Task OuterLoop(TcpListener door)
 {
  while(true)
  {
   TcpClient client;try{client=await door.AcceptTcpClientAsync();}catch{return;}
   _=HandleOuter(door,client);
  }
 }
 /// <summary>두 IPv4 주소가 앞의 prefix 비트까지 같은가</summary>
 public static bool SameSubnet(IPAddress one,IPAddress other,int prefix)
 {
  if(one.AddressFamily!=AddressFamily.InterNetwork||other.AddressFamily!=AddressFamily.InterNetwork)return false;
  var a=one.GetAddressBytes();var b=other.GetAddressBytes();
  uint x=(uint)(a[0]<<24|a[1]<<16|a[2]<<8|a[3]),y=(uint)(b[0]<<24|b[1]<<16|b[2]<<8|b[3]),mask=prefix<=0?0:uint.MaxValue<<(32-Math.Min(prefix,32));
  return(x&mask)==(y&mask);
 }
 async Task HandleOuter(TcpListener door,TcpClient client)
 {
  using(client)
  {
   IPAddress? home;int prefix;byte[] key;long epoch;
   lock(outerGate){if(!ReferenceEquals(outer,door))return;home=outerAddress;prefix=outerPrefix;key=outerKey;epoch=outerEpoch;}
   if(client.Client.RemoteEndPoint is not IPEndPoint remote||home==null||!SameSubnet(remote.Address,home,prefix))return;   // 다른 서브넷: 답 없이 끊는다
   if(Interlocked.Increment(ref outerBusy)>OuterConnectionLimit){Interlocked.Decrement(ref outerBusy);return;}
   try
   {
    var stream=client.GetStream();using var limit=new CancellationTokenSource(TimeSpan.FromSeconds(3));
    var buffer=new byte[OuterRequestLimit];int filled=0,end=-1;
    while(end<0&&filled<buffer.Length)
    {
     int read=await stream.ReadAsync(buffer.AsMemory(filled),limit.Token);if(read==0)return;
     filled+=read;end=buffer.AsSpan(0,filled).IndexOf("\r\n\r\n"u8);
    }
    if(end<0)return;   // 2KB 안에 끝나지 않는 요청은 받지 않는다
    string line=Encoding.ASCII.GetString(buffer,0,buffer.AsSpan(0,filled).IndexOf("\r\n"u8));
    await AnswerOuter(stream,line,key,epoch);
   }
   catch{}
   finally{Interlocked.Decrement(ref outerBusy);}
  }
 }
 // 요청 줄 "GET /o/<키>… HTTP/1.1" 에 답한다
 async Task AnswerOuter(NetworkStream stream,string line,byte[] key,long epoch)
 {
  byte[] body=Array.Empty<byte>();string status="404 Not Found",type="text/plain",extra="",cache="no-store";
  var parts=line.Split(' ');
  if(parts.Length==3&&parts[0]=="GET"&&parts[1].StartsWith("/o/",StringComparison.Ordinal)&&parts[1].Length>=35&&key.Length==32)
  {
   string given=parts[1].Substring(3,32),rest=parts[1][35..];
   if(CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(given),key))
   {
    if(rest==""){status="302 Found";extra=$"Location: /o/{given}/\r\n";}   // 끝의 / 가 빠진 주소: 페이지가 제 아래의 길(state, fonts)을 찾게 / 가 붙은 주소로 보낸다
    else if(rest=="/"){status="200 OK";type="text/html; charset=utf-8";body=outerHtml??=Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(html).Replace("url(/fonts/","url(fonts/"));}
    else if(rest=="/state")
    {
     status="200 OK";type="application/json; charset=utf-8";body=Volatile.Read(ref state);extra=NowHeader();
    }
    else if(rest.StartsWith("/fonts/",StringComparison.Ordinal)&&FontNamed(rest[7..]) is {} font){status="200 OK";type=font.Type;body=font.Bytes;cache="max-age=86400";}
   }
  }
  // 답하기 직전에 한 번 더 본다: 이 조회가 시작된 뒤에 송출용 서버가 닫혔거나 다시 열렸거나 키가 바뀌었으면(옛 주소·옛 키의 조회다) 주지 않고, '온 때'도 적지 않는다.
  // 그러지 않으면 키를 새로 만든 바로 뒤에 옛 조회가 끝나면서 새 주소로 연결된 것처럼 세인다
  lock(outerGate)
  {
   if(outerEpoch!=epoch){body=Array.Empty<byte>();status="404 Not Found";type="text/plain";extra="";cache="no-store";}
   else if(status=="200 OK"&&type.StartsWith("application/json",StringComparison.Ordinal))Interlocked.Exchange(ref outerSeen,Environment.TickCount64);
  }
  await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\nCache-Control: {cache}\r\n{extra}Connection: close\r\n\r\n"));
  await stream.WriteAsync(body);
 }
 // 상태 응답에 서버의 지금 시각을 함께 보낸다. 오버레이 페이지는 저장 결과의 시간(문구 1.5초, 상세 6초)을 앱이 준 저장 시각으로 세는데,
 // 페이지를 연 PC 의 시계가 이 PC 와 다르면(투컴) 어긋난다. 페이지는 이 값과 제 시계의 차이만큼 고쳐서 센다. 본문에 넣지 않는 까닭: 본문이 조회마다 달라지면 페이지가 매번 다시 그린다
 static string NowHeader()=>$"X-Now: {DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}\r\n";
}
