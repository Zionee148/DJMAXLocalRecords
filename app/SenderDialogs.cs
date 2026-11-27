using System.Drawing.Drawing2D;
namespace LadderRecords;

// 투컴의 창들.
// 창 너비 442(100% 때의 값), 본문 좌우 여백 18.

// '송출 PC용 연결 선택': 게임 PC 의 연결 가운데 송출 PC 와 같은 공유기에 연결된 것을 고른다.
// IP 는 창을 열 때마다 가려져 있고 'IP 주소 보기'를 켠 동안에만 보인다(게임 PC 의 화면도 방송에 잡힐 수 있다). 고른 연결은 '적용'을 눌러야 바뀐다.
// 연결이 없으면 빈 목록의 안내가 보이고, 연결이 생기면 열려 있는 목록에도 나타난다(1초마다 다시 읽는다).
class SenderLinkDialog:CompactDialog
{
 public const int ContentWidth=406;
 public const string NoLinkText="사용할 수 있는 연결이 없습니다. 랜선을 연결하거나 Wi-Fi를 켜 주세요.";
 public const string PublicText="이 연결은 공인 IP를 사용합니다. 같은 통신사 네트워크의 다른 기기도 연결을 시도할 수 있으므로, 복사한 주소는 본인의 송출 PC에서만 사용해 주세요.";
 readonly Func<IReadOnlyList<NetLink>> links;readonly Panel list=new(){Width=ContentWidth,Margin=Padding.Empty};
 readonly Label empty=Boxes.Text(NoLinkText,"dim",9.75f,false,ContentWidth),notice=Boxes.Text(PublicText,"warn",9.75f,false,ContentWidth);
 readonly CheckBox reveal=new(){Text="IP 주소 보기",AutoSize=true,Margin=new(0,8,0,0)};
 readonly Button apply=new(){Text="적용",Tag="primary",DialogResult=DialogResult.OK};
 readonly System.Windows.Forms.Timer watch=new(){Interval=1000};
 readonly Func<bool> canRenew;readonly Button renew=new(){Text="주소 새로 만들기…",AutoSize=false,Size=new(150,27),Location=new(18,11),TabIndex=2};
 string? picked;IReadOnlyList<NetLink> shown=Array.Empty<NetLink>();
 /// <summary>고른 연결('적용'으로 닫았을 때)</summary>
 public NetLink? Chosen=>shown.FirstOrDefault(l=>l.Id==picked);
 /// <summary>'주소 새로 만들기'를 확인했을 때 불린다</summary>
 public event Action? Renewed;

 /// <param name="current">지금 고른 연결의 번호(다시 열면 그 줄이 골라져 있다). 없으면 아무것도 고르지 않은 채로 뜬다</param>
 /// <param name="canRenew">지금 복사할 수 있는 송출 주소가 있는가('주소 새로 만들기…'를 쓸 수 있는가). 창이 열려 있는 동안에도 1초마다 다시 묻고, 확인을 누른 뒤에도 한 번 더 묻는다</param>
 public SenderLinkDialog(Func<IReadOnlyList<NetLink>> links,string? current,Func<bool> canRenew):base("송출 PC용 연결 선택")
 {
  this.links=links;this.canRenew=canRenew;picked=current;Body.Padding=Padding.Empty;
  var top=new FlowLayoutPanel(){AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new(18,16,18,14),Margin=Padding.Empty};
  var title=Parts.TitleLine("어떤 연결을 사용할까요?",ContentWidth);title.Margin=new(0,0,0,8);top.Controls.Add(title);
  var lead=Boxes.Text("게임 PC의 연결 중에서 송출 PC와 같은 공유기에 연결된 항목을 선택해 주세요.",null,9.75f,false,ContentWidth);lead.Margin=new(0,0,0,12);top.Controls.Add(lead);
  var head=new Panel(){Size=new(ContentWidth,20),Margin=Padding.Empty};
  head.Controls.Add(new Label(){Text="연결 이름",Tag="dim",AutoSize=false,Bounds=new(32,0,180,20),TextAlign=ContentAlignment.MiddleLeft,Font=Theme.Px(AppFonts.Ui,9f)});
  head.Controls.Add(new Label(){Text="게임 PC의 IP 주소",Tag="dim",AutoSize=false,Bounds=new(ContentWidth-180-12,0,180,20),TextAlign=ContentAlignment.MiddleRight,Font=Theme.Px(AppFonts.Ui,9f)});
  top.Controls.Add(head);top.Controls.Add(list);
  empty.Margin=new(0,6,0,6);top.Controls.Add(empty);
  top.Controls.Add(reveal);
  notice.Margin=new(0,10,0,0);top.Controls.Add(notice);
  var kept=Boxes.Text("선택한 연결은 다음 실행에도 사용합니다.","dim",9.75f,false,ContentWidth);kept.Margin=new(0,10,0,0);top.Controls.Add(kept);
  Body.Controls.Add(top);
  // 아래의 단추 띠: 왼쪽에 '주소 새로 만들기…', 오른쪽에 적용·취소
  const int band=442;
  var footer=new Panel(){Size=new(band,1+10+27+10),Margin=Padding.Empty,Tag="field"};
  footer.Controls.Add(new Panel(){Bounds=new(0,0,band,1),Tag="line"});
  var cancel=new Button(){Text="취소",DialogResult=DialogResult.Cancel,AutoSize=false,Size=new(70,27),Location=new(band-18-70,11),TabIndex=1};
  apply.AutoSize=false;apply.Size=new(70,27);apply.Location=new(band-18-70-6-70,11);apply.TabIndex=0;
  footer.Controls.Add(renew);footer.Controls.Add(apply);footer.Controls.Add(cancel);Body.Controls.Add(footer);
  AcceptButton=apply;CancelButton=cancel;
  reveal.CheckedChanged+=(_,_)=>{foreach(var row in list.Controls.OfType<LinkRow>())row.Reveal=reveal.Checked;};
  // 확인 창이 떠 있는 사이에 연결이 사라졌을 수 있다. 확인을 누른 때에도 주소가 있을 때만 만든다(없으면 옛 주소만 못 쓰게 되고 새 주소를 복사할 수 없다)
  renew.Click+=(_,_)=>{if(canRenew()&&RenewDialog.Ask(this)&&canRenew())Renewed?.Invoke();Review();};
  watch.Tick+=(_,_)=>Fill();
  Fill();
  Shown+=(_,_)=>watch.Start();FormClosed+=(_,_)=>watch.Stop();
 }
 protected override void Dispose(bool disposing){if(disposing)watch.Dispose();base.Dispose(disposing);}

 // 연결 목록을 다시 읽어 줄을 맞춘다(달라졌을 때만 다시 만든다). 고른 줄은 그대로 둔다
 void Fill()
 {
  var now=links();
  if(now.Count!=shown.Count||!now.Zip(shown).All(pair=>pair.First==pair.Second)||list.Controls.Count==0&&now.Count>0)
  {
   shown=now;
   list.SuspendLayout();foreach(Control old in list.Controls.Cast<Control>().ToArray())old.Dispose();list.Controls.Clear();
   float unit=Unit;int height=(int)Math.Round(40*unit),gap=(int)Math.Round(6*unit),y=0;
   foreach(var link in now)
   {
    var row=new LinkRow(link){Bounds=new(0,y,list.Width,height),Picked=link.Id==picked,Reveal=reveal.Checked};
    if(unit!=1f)row.Font=new Font(row.Font.FontFamily,row.Font.Size*unit,row.Font.Style,row.Font.Unit);
    row.Chose+=()=>{picked=link.Id;foreach(var other in list.Controls.OfType<LinkRow>())other.Picked=other.Link.Id==picked;Review();};
    list.Controls.Add(row);y+=height+gap;
   }
   list.Height=Math.Max(0,y-gap);list.ResumeLayout();
  }
  Review();
 }
 void Review()
 {
  var chosen=Chosen;
  empty.Visible=shown.Count==0;list.Visible=shown.Count>0;
  notice.Visible=chosen is {Public:true};
  apply.Enabled=chosen!=null;
  bool can=canRenew();if(renew.Enabled!=can)renew.Enabled=can;
 }

 // 연결 한 줄: 둥근 고름 표시, 이름, (공인 주소면) '공인 IP', 오른쪽에 '가려짐' 또는 IP. 줄 어디를 눌러도 고른다. 고른 줄은 강조색 테두리
 sealed class LinkRow:Control
 {
  public readonly NetLink Link;bool picked,reveal;
  public event Action? Chose;
  public LinkRow(NetLink link)
  {
   Link=link;
   SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.Selectable,true);
   TabStop=true;Cursor=Cursors.Hand;Margin=Padding.Empty;Font=Theme.Px(AppFonts.Ui,10.5f,FontStyle.Bold);
   AccessibleRole=AccessibleRole.RadioButton;AccessibleName=link.Name+(link.Public?" (공인 IP)":"");
  }
  [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public bool Picked{get=>picked;set{if(picked==value)return;picked=value;Invalidate();}}
  [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public bool Reveal{get=>reveal;set{if(reveal==value)return;reveal=value;Invalidate();}}
  protected override void OnClick(EventArgs e){base.OnClick(e);Focus();Chose?.Invoke();}
  protected override bool IsInputKey(Keys keyData)=>keyData is Keys.Up or Keys.Down||base.IsInputKey(keyData);
  protected override void OnKeyDown(KeyEventArgs e)
  {
   base.OnKeyDown(e);
   if(e.KeyCode==Keys.Space){e.Handled=true;Chose?.Invoke();}
   else if(e.KeyCode is Keys.Up or Keys.Down&&Parent is {} parent)
   {
    var rows=parent.Controls.OfType<LinkRow>().ToList();int next=rows.IndexOf(this)+(e.KeyCode==Keys.Down?1:-1);
    if(next>=0&&next<rows.Count){rows[next].Focus();rows[next].Chose?.Invoke();}
    e.Handled=true;
   }
  }
  protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);Invalidate();}
  protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);Invalidate();}
  protected override void OnPaint(PaintEventArgs e)
  {
   var g=e.Graphics;g.Clear(Parent?.BackColor??Theme.Window);g.SmoothingMode=SmoothingMode.AntiAlias;
   float k=Height/40f;bool strong=picked||Focused&&ShowFocusCues;
   using(var shape=BadgeDesign.Rounded(new RectangleF(strong?1:0,strong?1:0,Width-(strong?3:1),Height-(strong?3:1)),4*k))
   {
    using var fill=new SolidBrush(Theme.Field);g.FillPath(fill,shape);using var pen=new Pen(picked?Theme.Accent:strong?Theme.Dim:Theme.Line,strong?2:1);g.DrawPath(pen,shape);
   }
   // 고름 표시
   float dot=15*k,left=12*k,top=(Height-dot)/2;
   using(var ring=new Pen(picked?Theme.Fill:Theme.Dim,1.5f*k))g.DrawEllipse(ring,left,top,dot,dot);
   if(picked){using var inner=new SolidBrush(Theme.Fill);g.FillEllipse(inner,left+4*k,top+4*k,dot-8*k,dot-8*k);}
   int x=(int)Math.Round(left+dot+10*k);
   var name=TextRenderer.MeasureText(g,Link.Name,Font,Size.Empty,TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix);
   using var small=new Font(AppFonts.UiMedium,Font.Size*12f/14f,FontStyle.Regular,GraphicsUnit.Pixel);
   string side=reveal?Link.Address.ToString():"가려짐";
   var sideSize=TextRenderer.MeasureText(g,side,small,Size.Empty,TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix);
   int right=Width-(int)Math.Round(12*k)-sideSize.Width;
   TextRenderer.DrawText(g,side,small,new Rectangle(right,0,sideSize.Width+2,Height),Theme.Dim,TextFormatFlags.VerticalCenter|TextFormatFlags.Left|TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix);
   int tagWidth=0;
   if(Link.Public)
   {
    // '공인 IP' 표시(이름 오른쪽의 작은 꼬리표)
    var tagSize=TextRenderer.MeasureText(g,"공인 IP",small,Size.Empty,TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix);tagWidth=tagSize.Width+(int)Math.Round(12*k);
   }
   int room=right-x-(int)Math.Round(10*k)-(tagWidth>0?tagWidth+(int)Math.Round(8*k):0);
   TextRenderer.DrawText(g,Link.Name,Font,new Rectangle(x,0,Math.Max(10,room),Height),Theme.Text,TextFormatFlags.VerticalCenter|TextFormatFlags.Left|TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix|TextFormatFlags.EndEllipsis);
   if(tagWidth>0)
   {
    var box=new RectangleF(x+Math.Min(name.Width,room)+8*k,(Height-20*k)/2,tagWidth,20*k);
    using var shape=BadgeDesign.Rounded(box,3*k);using var back=new SolidBrush(Theme.WarnBack);g.FillPath(back,shape);
    TextRenderer.DrawText(g,"공인 IP",small,Rectangle.Round(box),Theme.Warn,TextFormatFlags.VerticalCenter|TextFormatFlags.HorizontalCenter|TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix);
   }
  }
 }
}

// '송출 PC용 주소를 새로 만들까요?': 연결 키를 바꾸는 확인. 처음 선택은 취소다(처음 초점이 '취소'라 연 직후의 Enter 와 Esc·닫기는 취소). '새로 만들기'를 직접 골라야 바뀐다
class RenewDialog:ChoiceDialog
{
 public const string Lead="새로 만들면 지금 쓰는 주소로는 더 이상 연결할 수 없습니다. 새 주소를 복사해 메신저 등으로 송출 PC에 옮긴 뒤 OBS 브라우저 소스에 붙여 넣어 주세요.";
 public RenewDialog():base("송출 PC용 주소 새로 만들기")
 {
  const int width=NameDialog.ContentWidth;
  Body.Padding=new(18,16,18,16);
  var title=Parts.TitleLine("송출 PC용 주소를 새로 만들까요?",width);title.Margin=new(0,0,0,12);Body.Controls.Add(title);
  var text=Boxes.Text(Lead,null,9.75f,false,width);text.Margin=new(0,0,0,18);Body.Controls.Add(text);
  var renew=new Button(){Text="새로 만들기",DialogResult=DialogResult.Yes,Tag="danger"};var cancel=new Button(){Text="취소",DialogResult=DialogResult.No,Tag="primary"};
  Body.Controls.Add(Parts.Footer(width,renew,cancel));
  AcceptButton=cancel;CancelButton=cancel;Shown+=(_,_)=>ActiveControl=cancel;
 }
 public static bool Ask(IWin32Window owner){using var dialog=new RenewDialog();return dialog.ShowDialog(owner)==DialogResult.Yes;}
}

// '연결이 안 될 때': 읽기만 하는 도움말.
// 연결 대기 중에 저절로 뜨지 않는다. 주 화면의 '연결이 안 될 때'를 눌렀을 때만 뜬다. 다른 프로그램이 송출용 포트를 쓰고 있을 때는 그 안내가 맨 위에 보인다
class SenderHelpDialog:CompactDialog
{
 public const string FirewallLine="게임 PC의 Windows 방화벽에서 DJMAXLocalRecords가 지금 연결의 네트워크 유형(개인 또는 공용)에 허용되어 있는지 확인합니다.";
 public static string PortLine(int port)=>$"다른 프로그램이 송출용 포트({port})를 사용하고 있습니다. 그 프로그램을 끄면 이 프로그램이 저절로 다시 연결을 기다립니다.";
 public SenderHelpDialog(bool portBusy,int port):base("연결이 안 될 때")
 {
  const int width=SenderLinkDialog.ContentWidth;
  Body.Padding=new(18,16,18,16);
  if(portBusy){var busy=Boxes.Text(PortLine(port),"warn",9.75f,false,width);busy.Margin=new(0,0,0,14);Body.Controls.Add(busy);}
  var title=Parts.TitleLine("먼저 송출 PC에서 주소를 열어 보세요",width);title.Margin=new(0,0,0,8);Body.Controls.Add(title);
  var lead=Boxes.Text("게임 PC에서 ‘송출 PC용 주소’를 복사한 뒤 메신저 등으로 송출 PC에 옮겨 웹 브라우저 주소창에 붙여 넣습니다.",null,9.75f,false,width);lead.Margin=new(0,0,0,14);Body.Controls.Add(lead);
  void Group(string name,params string[] items)
  {
   Body.Controls.Add(new SectionHeader(name){Width=width,Margin=new(0,0,0,6),Font=Theme.Px(AppFonts.Head,9.75f,FontStyle.Bold)});
   foreach(string item in items)
   {
    var row=new FlowLayoutPanel(){AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,WrapContents=false,Margin=new(0,0,0,5)};
    var dot=Boxes.Text("•","dim",9.75f);dot.Margin=new(8,0,8,0);row.Controls.Add(dot);row.Controls.Add(Boxes.Text(item,null,9.75f,false,width-8-8-dot.PreferredWidth));
    Body.Controls.Add(row);
   }
  }
  Group("브라우저에서 열릴 때","송출 PC의 OBS에서 브라우저 소스 속성을 열고 URL에 방금 확인한 주소를 넣습니다.","방송에 쓰는 장면에서 해당 소스의 눈 아이콘이 켜져 있는지 확인합니다.");
  Body.Controls.Add(new Panel(){Size=new(width,8),Margin=Padding.Empty});
  Group("브라우저에서도 열리지 않을 때","두 PC가 같은 공유기에 연결되어 있는지 확인합니다.","게임 PC의 ‘연결 선택…’에서 송출 PC와 같은 공유기에 연결된 이더넷 또는 Wi-Fi를 선택합니다. 주소가 바뀌면 새 주소를 복사합니다.",FirewallLine);
  var close=new Button(){Text="닫기",DialogResult=DialogResult.OK,Tag="default"};
  var footer=Parts.Footer(width,close);footer.Margin=new(0,12,0,0);Body.Controls.Add(footer);
  AcceptButton=close;CancelButton=close;Shown+=(_,_)=>ActiveControl=close;
 }
}
