using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
namespace LadderRecords;
record Hotkey(string Name,Keys Key,uint Modifiers);
class Hotkeys:IDisposable
{
 [DllImport("user32.dll")]static extern bool RegisterHotKey(IntPtr h,int id,uint mod,uint key);
 [DllImport("user32.dll")]static extern bool UnregisterHotKey(IntPtr h,int id);
 IntPtr handle;readonly bool enabled;readonly List<int> registered=new();
 public List<Hotkey> Items{get;private set;}=Defaults();
 public static List<Hotkey> Defaults()=>new(){new("등록",Keys.F8,2),new("새로 감지하기",Keys.F9,2),new("저장 확인",Keys.F10,2),new("저장 취소",Keys.F11,2),new("감지 시작",Keys.F12,2)};
 public static string Format(Hotkey k)=>((k.Modifiers&2)!=0?"Ctrl+":"")+((k.Modifiers&1)!=0?"Alt+":"")+((k.Modifiers&4)!=0?"Shift+":"")+k.Key;
 public Hotkeys(IntPtr handle,bool enabled=true){this.handle=handle;this.enabled=enabled;}
 public void Set(List<Hotkey> items){if(items.Count==6)items=items.Skip(2).ToList();if(items.Count==4)items=items.Concat(new[]{Defaults()[4]}).ToList();if(items.Count!=5||items.Select(i=>(i.Key,i.Modifiers)).Distinct().Count()!=5)throw new Exception("단축키가 중복됩니다.");items=items.Select((item,index)=>item with{Name=Defaults()[index].Name}).ToList();var old=Items.ToList();bool restore=registered.Count>0;Clear();try{Register(items);Items=items;}catch{Clear();if(restore)Register(old);throw;}}
 void Register(List<Hotkey> items){if(!enabled)return;for(int i=0;i<items.Count;i++){if(!RegisterHotKey(handle,102+i,items[i].Modifiers|0x4000,(uint)items[i].Key))throw new Exception(items[i].Name+": 다른 프로그램과 충돌합니다.");registered.Add(102+i);}}
 public void Detach()=>Clear();
 public void Rebind(IntPtr value){Clear();handle=value;Register(Items);}
 void Clear(){foreach(var id in registered)UnregisterHotKey(handle,id);registered.Clear();}public void Dispose()=>Clear();
}
class OverlayServer:IDisposable
{
 readonly byte[] titles,html;readonly TcpListener listener;byte[] state="{}"u8.ToArray();public int Port{get;}
 volatile PopupFit? popupFit;
 internal PopupFit? PopupFit{get=>popupFit;set=>popupFit=value;}
 public OverlayServer(int port,string[]? songTitles=null){titles=JsonSerializer.SerializeToUtf8Bytes(songTitles??Array.Empty<string>());html=File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"overlay.html"));Port=port;listener=new(IPAddress.Loopback,port);listener.Start();_=Task.Run(Loop);}
 public void Set(object value)=>Volatile.Write(ref state,JsonSerializer.SerializeToUtf8Bytes(value));
 async Task Loop(){while(true){TcpClient c;try{c=await listener.AcceptTcpClientAsync();}catch{return;}_=Handle(c);}}
 async Task Handle(TcpClient c){using(c)try{using var stream=c.GetStream();using var reader=new StreamReader(stream,Encoding.UTF8,false,1024,true);string? first=await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3));while(!string.IsNullOrEmpty(await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3)))){}bool api=first?.StartsWith("GET /state ")==true;bool catalog=first?.StartsWith("GET /titles ")==true;bool fit=first?.StartsWith("GET /popup-fit/")==true;if(fit)ReadPopupFit(first!);var bytes=fit?Array.Empty<byte>():catalog?titles:api?Volatile.Read(ref state):html;var head=Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: {(api||catalog?"application/json":"text/html")}; charset=utf-8\r\nContent-Length: {bytes.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");await stream.WriteAsync(head);await stream.WriteAsync(bytes);}catch{}}
 void ReadPopupFit(string request)
 {
  var fit=PopupFit;
  if(fit==null)return;
  var prefix="GET /popup-fit/"+fit.Id+"/";
  if(!request.StartsWith(prefix,StringComparison.Ordinal))return;
  int end=request.IndexOf(' ',prefix.Length);
  if(end<0)return;
  var fields=request[prefix.Length..end].Split('/');
  if(fields.Length!=6)return;
  var sizes=new int[6];
  for(int i=0;i<sizes.Length;i++)
   if(!int.TryParse(fields[i],out sizes[i])||sizes[i]<0||sizes[i]>16384)return;
  if(sizes[0]==0||sizes[1]==0||sizes[4]==0||sizes[5]==0||sizes[2]>=sizes[0]||sizes[3]>=sizes[1])return;
  fit.Ready.TrySetResult(new(sizes[0],sizes[1],sizes[2],sizes[3],sizes[4],sizes[5]));
 }
 public void Dispose()=>listener.Stop();
}



