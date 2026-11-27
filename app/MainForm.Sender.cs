using System.Net.NetworkInformation;
namespace LadderRecords;

// 주 화면의 투컴 부분: '투컴 연결 허용'과 '연결 선택…', 허용을 켜면 원컴 주소 아래에 생기는 송출 PC용 주소 줄(가린 주소, 복사, 상태, '연결이 안 될 때')과 그 아래의 안내.
// 연결을 고르고 송출용 서버를 여는 일은 SenderLink.cs, 창은 SenderDialogs.cs.
// 주소와 연결 키는 화면에 보이지 않는다(게임 PC 의 화면도 방송에 잡힐 수 있다). '복사'만 실제 주소를 클립보드에 넣는다.
partial class MainForm
{
 public const string AddressChangedText="송출 PC용 주소가 바뀌었습니다. ‘복사’를 누른 뒤 메신저 등으로 송출 PC에 옮겨 OBS 브라우저 소스에 새 주소를 붙여 넣어 주세요.";
 public const string FirewallText="Windows 방화벽이 송출 연결을 막고 있습니다. 게임 PC의 방화벽 설정에서 선택한 네트워크 유형(개인 또는 공용)에 적용된 이 프로그램의 차단을 해제해 주세요.";
 SenderLink sender=null!;
 readonly CheckBox senderAllow=new(){Text="투컴 연결 허용",AutoSize=true};
 readonly InputBox senderAddress=new(100,"—",0,27);
 readonly Button senderCopy=new(){Text="복사",AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink},senderPick=new(){Text="연결 선택…",AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink};
 readonly Label senderStatus=new(){AutoSize=true,UseMnemonic=false,Tag="dim"};
 readonly LinkLabel senderHelp=new(){Text="연결이 안 될 때",AutoSize=true,LinkBehavior=LinkBehavior.AlwaysUnderline,UseMnemonic=false};
 readonly FlowLayoutPanel senderNotes=new(){AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,FlowDirection=FlowDirection.TopDown,WrapContents=false,Visible=false};
 Label noteChanged=null!,noteBlocked=null!,notePublic=null!;Control? senderRow;
 readonly System.Windows.Forms.Timer senderTimer=new(){Interval=500};
 volatile bool senderDirty;int senderTicks;bool firewallAsking;
 // 방화벽이 막고 있는가: true 는 막는 규칙을 읽어 확인했다, false 는 막지 않는 것을 확인했다, null 은 아직 모른다.
 // 한 번 확인한 차단은 같은 연결에서 풀린 것을 읽어 확인할 때까지 남긴다: 읽지 못했다거나('모름') 연결됐다는 것만으로 지우지 않는다
 // (게임 PC 에서 송출 주소를 열어 보면 방화벽을 거치지 않고 연결되므로, '연결됨'이 막히지 않았다는 증거가 못 된다)
 bool? firewallBlocked;string? firewallLink;Exception? senderSaveShown;
 int firewallAsked=-FirewallEvery;   // 마지막으로 방화벽을 읽기 시작한 때(senderTicks). 결과가 '모름'이어도 5초 뒤에 다시 읽는다(0.5초마다 읽지 않는다)
 const int FirewallEvery=10;         // 0.5초 주기 열 번 = 5초
 public const string StoppedUnsavedText="송출을 중지했습니다. 설정을 저장하지 못해 다음 실행 시 ‘투컴 연결 허용’이 다시 켜질 수 있습니다.";
 public const string UnsavedText="투컴 설정을 저장하지 못했습니다. 다시 실행한 뒤 설정을 확인해 주세요.";
 public const string RenewUnsavedText="새 주소를 저장하지 못해 기존 주소를 유지합니다.";

 // 주 화면을 만들 때 한 번: 저장된 설정으로 연결을 잇고 컨트롤을 준비한다. 줄에 올리는 것은 주 화면의 배치 코드가 한다
 void PrepareSender(SenderSettings? saved,int noteWidth)
 {
  sender=new SenderLink(server,saved,()=>NetLinks.Source(),_=>SaveOptions());
  senderAddress.Box.ReadOnly=true;senderAddress.Box.AccessibleName="송출 PC용 주소(가려짐)";senderAddress.Box.TabStop=false;
  senderStatus.Font=Theme.Px(AppFonts.Ui,9f);senderHelp.Font=Theme.Px(AppFonts.Ui,9f);senderStatus.AccessibleName="송출 연결 상태";
  Label Note(string text,string tag){var label=Boxes.Text(text,tag,9f,false,noteWidth);label.Margin=new(0,0,0,5);label.Visible=false;senderNotes.Controls.Add(label);return label;}
  noteBlocked=Note(FirewallText,"warn");noteChanged=Note(AddressChangedText,"warn");notePublic=Note(SenderLinkDialog.PublicText,"warn");
  senderAllow.Checked=sender.Allowed;
  senderAllow.CheckedChanged+=(_,_)=>Safe(()=>
  {
   sender.Allow(senderAllow.Checked);firewallBlocked=null;firewallLink=null;senderTicks=0;firewallAsked=-FirewallEvery;
   // 연결이 여럿인데 고른 적이 없으면 고르는 창을 바로 연다. 취소하면 '연결 선택 필요'로 남는다(허용은 켜진 채)
   if(sender.State==SenderState.ChooseLink&&Visible)PickSenderLink();
   ShowSender();
  });
  senderPick.Click+=(_,_)=>Safe(PickSenderLink);
  senderCopy.Click+=(_,_)=>Safe(CopySenderUrl);
  senderHelp.LinkClicked+=(_,_)=>Safe(()=>{using var help=new SenderHelpDialog(sender.State==SenderState.PortBusy,server.Port);help.ShowDialog(this);});
  NetworkChange.NetworkAddressChanged+=NoteLinksChanged;
  senderTimer.Tick+=(_,_)=>Safe(()=>
  {
   if(!sender.Allowed){ShowSender();return;}
   // 주소가 바뀌었다는 Windows 의 알림이 왔거나 2초가 지났으면 연결 목록을 다시 읽고 송출용 서버를 고른 연결의 지금 주소에 맞춰 연다(고른 연결이 돌아옴, 주소가 바뀜, 포트가 풀림)
   if(senderDirty||++senderTicks%4==0){senderDirty=false;sender.Refresh();}
   CheckFirewall();ShowSender();
  });
  sender.Refresh();ShowSender();senderTimer.Start();
 }
 void NoteLinksChanged(object? sender,EventArgs e)=>senderDirty=true;
 void StopSender(){senderTimer.Stop();NetworkChange.NetworkAddressChanged-=NoteLinksChanged;}

 void PickSenderLink()
 {
  using var dialog=new SenderLinkDialog(()=>NetLinks.Source(),sender.Settings.LinkId,()=>sender.CanRenew);
  // 새 키를 저장하지 못했으면 바꾸지 않는다: 기존 주소를 그대로 쓰고 그 사실을 알린다
  dialog.Renewed+=()=>{if(!sender.Renew()&&sender.SaveError is {} failed){senderSaveShown=failed;NoticeDialog.Show(dialog,"송출 PC용 주소 새로 만들기",RenewUnsavedText);}ShowSender();};
  if(dialog.ShowDialog(this)==DialogResult.OK&&dialog.Chosen is {} link)ApplySenderLink(link);
  ShowSender();
 }
 // 선택 창의 '적용'. 같은 연결을 다시 적용한 때에도 방화벽을 곧바로 다시 읽는다(방화벽을 풀고 확인하러 와서 누르는 단추다. 다른 연결로 바꾼 때는 CheckFirewall 이 처음부터 다시 본다).
 // 경과 횟수(senderTicks)는 0 으로 돌리지 않는다: 돌리면 마지막으로 읽은 때(firewallAsked)와 기준이 어긋나, 그때까지 켜 둔 시간만큼 다시 읽지 않는다
 void ApplySenderLink(NetLink link){sender.Choose(link);firewallAsked=senderTicks-FirewallEvery;}
 void CopySenderUrl()
 {
  if(sender.Url is not {} url)return;
  Clipboard.SetDataObject(SenderClipboard.Make(url),true);
  // 복사했다는 글에도 주소를 적지 않는다. '복사' 단추 위에 2초
  helpTip.Show(sender.AddressChanged?"새 주소를 복사했습니다.":"주소를 복사했습니다.",senderCopy,0,-senderCopy.Height-Theme.S(6),2000);
 }
 // 방화벽 규칙은 투컴을 켰을 때와, 연결 대기·차단이 이어지는 동안 5초마다 다시 읽는다(다른 스레드에서. 규칙이 수백 개라 0.1~0.2초가 든다). 연결됐으면 막힌 것이 아니다
 void CheckFirewall()
 {
  if(sender.Url==null||sender.Link is not {} link)return;   // 송출용 서버가 닫혀 있는 동안에는 읽지 않는다(확인해 둔 것은 그대로 둔다)
  if(firewallLink!=link.Id){firewallLink=link.Id;firewallBlocked=null;firewallAsked=senderTicks-FirewallEvery;}   // 다른 연결을 쓰게 됐으면 처음부터 다시 본다(곧바로 읽는다)
  // 받아 가고 있고 막힌 것을 확인한 적이 없으면 읽을 것이 없다. 그 밖에는(연결 대기, 연결 끊김, 차단을 확인한 뒤) 처음 한 번과 5초마다 읽는다
  if(sender.State==SenderState.Connected&&firewallBlocked!=true)return;
  if(firewallAsking||senderTicks-firewallAsked<FirewallEvery)return;
  firewallAsking=true;firewallAsked=senderTicks;string id=link.Id;int port=server.Port;var reader=SenderFirewall.Reader;
  Task.Run(()=>{bool? answer=null;try{answer=reader(id,port);}catch{}try{BeginInvoke(()=>{firewallAsking=false;if(firewallLink==id&&answer is bool known)firewallBlocked=known;ShowSender();});}catch(InvalidOperationException){}});
 }

 /// <summary>줄과 안내를 지금 상태에 맞춘다</summary>
 void ShowSender()
 {
  if(senderRow==null)return;
  sender.Confirm();
  bool on=sender.Allowed,ready=sender.Url!=null;var state=sender.State;
  bool blocked=ready&&firewallBlocked==true&&firewallLink==sender.Link?.Id,changed=sender.AddressChanged&&state!=SenderState.Connected;
  // 설정을 저장하지 못했으면 한 번 알린다(지금의 동작은 바뀐 대로다. 다음 실행에는 이전 설정으로 뜬다)
  if(sender.SaveError is {} failed&&!ReferenceEquals(failed,senderSaveShown)){senderSaveShown=failed;Alert(on?UnsavedText:StoppedUnsavedText);}   // 허용을 끈 때는 송출이 멈췄다는 것부터 알린다
  else if(sender.SaveError==null)senderSaveShown=null;
  string text=state switch{SenderState.NoLink=>"연결 없음",SenderState.ChooseLink=>"연결 선택 필요",SenderState.LinkGone=>"선택한 연결 없음",SenderState.PortBusy=>"포트 사용 중",SenderState.OpenFailed=>"주소 열기 실패",SenderState.Waiting=>"연결 대기",SenderState.Connected=>"연결됨",SenderState.Lost=>"연결 끊김",_=>""};
  // 짧은 상태 자리는 하나다: 방화벽 차단이 먼저, 그다음 주소 변경. 아래의 안내는 둘 다 보인다
  if(blocked)text="방화벽 차단";else if(changed)text="주소 변경";
  string tag=blocked||state is SenderState.PortBusy or SenderState.OpenFailed or SenderState.Lost?"bad":state==SenderState.Connected?"good":"dim";
  // 보임 여부는 견주지 않고 그대로 넣는다: 창이 아직 뜨지 않았을 때는 Visible 이 언제나 false 로 읽혀서, 견주면 숨겨야 할 것이 숨겨지지 않는다(같은 값을 넣는 것은 아무 일도 하지 않는다)
  senderRow.Visible=on;senderPick.Visible=on;
  string shown=ready?"주소 가려짐":"—";if(senderAddress.Box.Text!=shown)senderAddress.Box.Text=shown;
  if(senderCopy.Enabled!=ready)senderCopy.Enabled=ready;
  if(senderStatus.Text!=text)senderStatus.Text=text;
  if(!Equals(senderStatus.Tag,tag)){senderStatus.Tag=tag;senderStatus.ForeColor=Theme.For(senderStatus);}
  if(senderHelp.LinkColor!=Theme.Accent){senderHelp.LinkColor=Theme.Accent;senderHelp.ActiveLinkColor=Theme.Accent;senderHelp.VisitedLinkColor=Theme.Accent;}
  void Show(Label note,bool visible)=>note.Visible=visible;
  Show(noteBlocked,on&&blocked);Show(noteChanged,on&&changed);Show(notePublic,on&&sender.PublicNotice&&ready);
  senderNotes.Visible=on&&(blocked||changed||sender.PublicNotice&&ready);
 }
}

// 복사할 송출 주소를 클립보드에 넣을 꼴로 만든다. Windows 의 클립보드 기록(Win+V)과 다른 기기로의 동기화에서 빼 달라는 표시를 함께 넣는다
// (화면에서 주소를 가리는 것과 같은 까닭: 방송 중에 Win+V 를 눌러도 주소와 연결 키가 보이지 않게)
static class SenderClipboard
{
 public static readonly string[] Marks={"ExcludeClipboardContentFromMonitorProcessing","CanIncludeInClipboardHistory","CanUploadToCloudClipboard"};
 public static DataObject Make(string url)
 {
  var data=new DataObject();data.SetData(DataFormats.UnicodeText,true,url);
  foreach(string mark in Marks)data.SetData(mark,false,new MemoryStream(BitConverter.GetBytes(0)));   // 값 0 = 넣지 않는다
  return data;
 }
}
