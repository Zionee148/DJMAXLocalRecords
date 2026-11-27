using System.Runtime.InteropServices;
namespace LadderRecords;

// 투컴: Windows 방화벽이 이 프로그램의 들어오는 연결을 막고 있는지 읽는다.
// 바깥 주소에서 처음 들으면 Windows 가 허용 창을 띄우는데, 거기서 취소하면 차단 규칙이 남고 창은 다시 뜨지 않는다. 연결이 오지 않았다는 사실만으로는 방화벽이 막았는지 알 수 없다.
// 그래서 그 규칙을 읽어, 지금 연결에 걸리는 차단 규칙이 확인되면 '방화벽 차단'을 알린다. **읽기만 한다. 규칙을 만들거나 고치지 않는다**(고치는 데는 관리자 권한이 들고, 보안 설정은 사용자가 정한다).
// 알리는 조건: 고른 연결의 지금 네트워크 유형(개인·공용·도메인)에서 방화벽이 켜져 있고, 이 실행 파일의 들어오는 연결을 막는 켜진 규칙이 그 유형에 걸려 있고,
// **그 규칙이 특정 연결·연결 종류·주소에만 걸린 것이 아닐 때만**(다른 연결에만 걸린 규칙을 보고 이 연결이 막혔다고 하지 않는다. 범위를 가릴 수 없는 규칙은 근거로 쓰지 않는다).
// 규칙을 읽지 못했거나 연결의 유형을 알 수 없으면 막혔다고 하지 않는다.
static class SenderFirewall
{
 /// <summary>방화벽 규칙 하나에서 보는 것. Profiles 는 유형의 비트(도메인 1, 개인 2, 공용 4. 모든 유형은 0x7FFFFFFF).
 /// Narrowed 는 규칙이 특정 연결(어댑터)·연결 종류(유선·무선 등)·이 PC 의 주소·상대의 주소에만 걸려 있는가</summary>
 public record Rule(string? Application,bool Enabled,bool Inbound,bool Block,int Profiles,int Protocol,string? LocalPorts,bool Narrowed=false);
 /// <summary>규칙의 범위 칸들이 "모두"가 아닌가</summary>
 public static bool IsNarrowed(object? interfaces,string? interfaceTypes,string? localAddresses,string? remoteAddresses)
 {
  static bool Open(string? text)=>string.IsNullOrWhiteSpace(text)||text.Trim() is "*" or "All";
  return interfaces is Array{Length:>0}||!Open(interfaceTypes)||!Open(localAddresses)||!Open(remoteAddresses);
 }
 public const int Domain=1,Private=2,Public=4;

 /// <summary>이 규칙들 가운데 그 실행 파일의 그 포트로 들어오는 TCP 연결을, 그 네트워크 유형에서 막는 것이 있는가</summary>
 public static bool Blocks(IEnumerable<Rule> rules,string executable,int profile,int port)
 {
  string wanted=Path.GetFullPath(executable);
  foreach(var rule in rules)
  {
   if(!rule.Enabled||!rule.Inbound||!rule.Block||rule.Narrowed||string.IsNullOrEmpty(rule.Application)||(rule.Profiles&profile)==0)continue;
   if(rule.Protocol is not(6 or 256))continue;   // TCP 또는 모든 프로토콜
   string path;try{path=Path.GetFullPath(Environment.ExpandEnvironmentVariables(rule.Application));}catch(Exception e)when(e is ArgumentException or NotSupportedException or PathTooLongException){continue;}
   if(!string.Equals(path,wanted,StringComparison.OrdinalIgnoreCase))continue;
   if(!string.IsNullOrEmpty(rule.LocalPorts)&&rule.LocalPorts!="*"&&!rule.LocalPorts.Split(',').Any(part=>Covers(part.Trim(),port)))continue;
   return true;
  }
  return false;
 }
 static bool Covers(string part,int port)
 {
  int dash=part.IndexOf('-');
  if(dash>0)return int.TryParse(part[..dash],out int low)&&int.TryParse(part[(dash+1)..],out int high)&&port>=low&&port<=high;
  return int.TryParse(part,out int one)&&one==port;
 }

 /// <summary>검사에서 바꿔 끼우는 자리: (고른 연결의 고유 번호, 포트) → 막혔는가(모르면 null)</summary>
 public static Func<string,int,bool?> Reader=Read;

 /// <summary>규칙 하나를 읽은 결과: 읽은 규칙, 또는 읽지 못함(Rule 이 null). 이 프로그램과 상관없다고 확인된 규칙은 목록에 넣지 않는다</summary>
 public readonly record struct Seen(Rule? Rule);
 /// <summary>
 /// 읽은 것들로 판정한다: 막는 규칙이 하나라도 확인되면 막힘(true). 없는데 읽지 못한 규칙이 있으면 모름(null): 그 규칙이 막는 규칙일 수 있다.
 /// 모두 읽었고 막는 규칙이 없으면 막지 않음(false). 읽지 못한 것을 건너뛰고 "막지 않음"이라고 답하지 않는다
 /// </summary>
 public static bool? Decide(IEnumerable<Seen> seen,string executable,int profile,int port)
 {
  var all=seen.ToList();
  if(Blocks(all.Where(s=>s.Rule!=null).Select(s=>s.Rule!),executable,profile,port))return true;
  return all.Any(s=>s.Rule==null)?null:false;
 }
 static bool SamePath(string? application,string executable)
 {
  if(string.IsNullOrEmpty(application))return false;
  try{return string.Equals(Path.GetFullPath(Environment.ExpandEnvironmentVariables(application)),Path.GetFullPath(executable),StringComparison.OrdinalIgnoreCase);}
  catch(Exception e)when(e is ArgumentException or NotSupportedException or PathTooLongException){return false;}
 }

 // Windows 에서 읽는다. 어디서든 실패하면 null(모름)
 static bool? Read(string linkId,int port)
 {
  try
  {
   int? profile=ProfileOf(linkId);if(profile==null)return null;
   var policyType=Type.GetTypeFromProgID("HNetCfg.FwPolicy2");if(policyType==null)return null;
   dynamic policy=Activator.CreateInstance(policyType)!;
   if(!(bool)policy.FirewallEnabled[profile.Value])return false;   // 그 유형에서 방화벽이 꺼져 있으면 규칙이 있어도 막지 않는다
   string executable=Environment.ProcessPath??Application.ExecutablePath;
   var seen=new List<Seen>();
   foreach(dynamic rule in policy.Rules)
   {
    // 값싼 칸부터 차례로 읽어 상관없는 규칙(꺼짐, 나가는 방향, 허용, 다른 프로그램, 다른 유형)을 먼저 뺀다.
    // 상관없다고 확인되기 전에 읽지 못한 규칙은 "읽지 못함"으로 남긴다(이 프로그램을 막는 규칙일 수 있다)
    try
    {
     if(!(bool)rule.Enabled||(int)rule.Direction!=1||(int)rule.Action!=0)continue;
     string? application=(string?)rule.ApplicationName;if(!SamePath(application,executable))continue;
     int profiles=(int)rule.Profiles;if((profiles&profile.Value)==0)continue;
     seen.Add(new Seen(new Rule(application,true,true,true,profiles,(int)rule.Protocol,(string?)rule.LocalPorts,
      IsNarrowed((object?)rule.Interfaces,(string?)rule.InterfaceTypes,(string?)rule.LocalAddresses,(string?)rule.RemoteAddresses))));
    }
    catch(Exception e)when(e is COMException or InvalidCastException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException){seen.Add(new Seen(null));}
   }
   return Decide(seen,executable,profile.Value,port);
  }
  catch(Exception e)when(e is COMException or InvalidCastException or UnauthorizedAccessException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or ArgumentException or TypeLoadException){return null;}
 }
 // 그 연결이 지금 놓인 네트워크의 유형. Windows 의 네트워크 목록에서 그 연결을 찾아 읽는다(0 공용, 1 개인, 2 도메인).
 // 이름으로 부르는 방식(IDispatch)으로는 연결의 고유 번호(GUID)를 받지 못해서(범위 오류가 난다) 인터페이스를 직접 적어 부른다. 메서드의 차례는 Windows 의 netlistmgr.idl 그대로다
 static int? ProfileOf(string linkId)
 {
  if(!Guid.TryParse(linkId,out Guid wanted))return null;
  var managerType=Type.GetTypeFromCLSID(new Guid("DCB00C01-570F-4A9B-8D69-199FDBA5723B"));if(managerType==null)return null;
  var manager=(INetworkListManager)Activator.CreateInstance(managerType)!;
  var all=manager.GetNetworkConnections();
  while(all.Next(1,out var connection,out uint got)==0&&got==1&&connection!=null)
  {
   if(connection.GetAdapterId()!=wanted)continue;
   return connection.GetNetwork().GetCategory() switch{0=>Public,1=>Private,2=>Domain,_=>(int?)null};
  }
  return null;
 }
 [ComImport,Guid("DCB00000-570F-4A9B-8D69-199FDBA5723B"),InterfaceType(ComInterfaceType.InterfaceIsDual)]
 interface INetworkListManager
 {
  [return:MarshalAs(UnmanagedType.Interface)] object GetNetworks(int flags);
  [return:MarshalAs(UnmanagedType.Interface)] object GetNetwork(Guid id);
  IEnumNetworkConnections GetNetworkConnections();
 }
 [ComImport,Guid("DCB00006-570F-4A9B-8D69-199FDBA5723B"),InterfaceType(ComInterfaceType.InterfaceIsDual)]
 interface IEnumNetworkConnections
 {
  [return:MarshalAs(UnmanagedType.Interface)] object NewEnum();
  [PreserveSig] int Next(uint count,out INetworkConnection? item,out uint fetched);
 }
 [ComImport,Guid("DCB00005-570F-4A9B-8D69-199FDBA5723B"),InterfaceType(ComInterfaceType.InterfaceIsDual)]
 interface INetworkConnection
 {
  INetwork GetNetwork();
  bool IsConnectedToInternet{get;}
  bool IsConnected{get;}
  int GetConnectivity();
  Guid GetConnectionId();
  Guid GetAdapterId();
 }
 [ComImport,Guid("DCB00002-570F-4A9B-8D69-199FDBA5723B"),InterfaceType(ComInterfaceType.InterfaceIsDual)]
 interface INetwork
 {
  [return:MarshalAs(UnmanagedType.BStr)] string GetName();
  void SetName([MarshalAs(UnmanagedType.BStr)] string name);
  [return:MarshalAs(UnmanagedType.BStr)] string GetDescription();
  void SetDescription([MarshalAs(UnmanagedType.BStr)] string description);
  Guid GetNetworkId();
  int GetDomainType();
  IEnumNetworkConnections GetNetworkConnections();
  void GetTimeCreatedAndConnected(out uint createdLow,out uint createdHigh,out uint connectedLow,out uint connectedHigh);
  bool IsConnectedToInternet{get;}
  bool IsConnected{get;}
  int GetConnectivity();
  int GetCategory();
 }
#if DEVELOPMENT_CHECKS
 /// <summary>이 PC 에서 실제로 읽어 본 결과(검사 기록용): 연결마다 유형을 읽었는가와 막혔다고 나오는가. 주소와 이름은 적지 않는다</summary>
 public static string Probe()
 {
  var links=NetLinks.Read();
  return $"연결 {links.Count}개: "+string.Join(", ",links.Select(l=>{int? p=null;string error="";try{p=ProfileOf(l.Id);}catch(Exception e){error=e.GetType().Name;}return $"유형 {(p==null?"모름"+error:p==Public?"공용":p==Private?"개인":"도메인")}/막힘 {Read(l.Id,18765)?.ToString()??"모름"}";}));
 }
#endif
}
