using System.Drawing.Text;
using System.Text.Json;
namespace LadderRecords;

static class OverlayText
{
 public const string ReferenceTitle="Tekken Tag Tournament Piano Intro -Massive True Mix-";
 public const string KartTitle="카트라이더, 크레이지아케이드, 버블파이터 Main theme ~CHUCK Remix~";
 public static string Title(Catalog catalog,Song song)=>catalog.Songs.Count(s=>s.Name.Trim().Equals(song.Name.Trim(),StringComparison.OrdinalIgnoreCase))>1?$"{song.Name} ({song.Artist})":song.Name;
 public static string[] Titles(Catalog catalog)=>catalog.Songs.Select(s=>Title(catalog,s)).ToArray();
 public static string Describe(Catalog catalog,Selection c)=>$"{Title(catalog,catalog.Songs.Single(s=>s.Id==c.SongId))} · {c.Button}B · {c.Pattern} · {c.Judge}판정";
 public static float Measure(string text,Font font){using var b=new Bitmap(1,1);using var g=Graphics.FromImage(b);return g.MeasureString(text,font,PointF.Empty,StringFormat.GenericTypographic).Width;}
 public static string DisplayTitle(string title,Font font,int? width=null){if(width==null||Measure(title,font)<=width)return title;var elements=System.Globalization.StringInfo.ParseCombiningCharacters(title);string Short(int n)=>(n==elements.Length?title:title[..elements[n]])+"...";int lo=0,hi=elements.Length;while(lo<hi){int mid=(lo+hi+1)/2;if(Measure(Short(mid),font)<=width)lo=mid;else hi=mid-1;}return Short(lo);}
 public static int Width(JsonElement a,string[] titles)=>OverlayLayout.Width(BadgeDesign.Complete(a),titles);
 public static string Middle(string text,Font font,int width){using var b=new Bitmap(1,1);using var g=Graphics.FromImage(b);bool Fits(string t)=>g.MeasureString(t,font,PointF.Empty,StringFormat.GenericTypographic).Width<=width;if(Fits(text))return text;var elements=System.Globalization.StringInfo.ParseCombiningCharacters(text);string Short(int count){if(count==0)return "…";int left=(int)Math.Ceiling(count*.5),right=count-left;return text[..elements[left]]+"…"+(right==0?"":text[elements[elements.Length-right]..]);}int lo=0,hi=Math.Max(0,elements.Length-1);while(lo<hi){int mid=(lo+hi+1)/2;if(Fits(Short(mid)))lo=mid;else hi=mid-1;}return Short(lo);}

}
class CompactDialog:AppWindow
{
 protected readonly FlowLayoutPanel Body=new(){AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new(14)};
 public CompactDialog(string title){Text=title;Font=Theme.Px("맑은 고딕",10);AutoSize=true;AutoSizeMode=AutoSizeMode.GrowAndShrink;FormBorderStyle=FormBorderStyle.FixedSingle;MaximizeBox=false;MinimizeBox=false;StartPosition=FormStartPosition.CenterParent;Controls.Add(Body);}
 protected void Row(string name,params Control[] items){var row=new FlowLayoutPanel(){AutoSize=true,WrapContents=false,Margin=new(0,3,0,3)};row.Controls.Add(new Label(){Text=name,Width=170,Height=30,TextAlign=ContentAlignment.MiddleLeft,Tag="dim"});row.Controls.AddRange(items);Body.Controls.Add(row);}
 protected void Section(string title,int width){Body.Controls.Add(new SectionHeader(title){Width=width,Margin=new(0,Body.Controls.Count==0?0:10,0,2)});}
 protected void Buttons(string accept="적용"){var row=new FlowLayoutPanel(){AutoSize=true,WrapContents=false,Margin=new(0,10,0,0)};var ok=new Button(){Text=accept,AutoSize=true,DialogResult=DialogResult.OK};var cancel=new Button(){Text="취소",AutoSize=true,DialogResult=DialogResult.Cancel};row.Controls.AddRange(new Control[]{ok,cancel});Body.Controls.Add(row);AcceptButton=ok;CancelButton=cancel;}
}
class HotkeyDialog:CompactDialog
{
 readonly List<(ComboBox Key,CheckBox Ctrl,CheckBox Alt,CheckBox Shift)> rows=new();
 public List<Hotkey> Value=>rows.Select((r,i)=>new Hotkey(Hotkeys.Defaults()[i].Name,(Keys)r.Key.SelectedItem!,(uint)((r.Ctrl.Checked?2:0)|(r.Alt.Checked?1:0)|(r.Shift.Checked?4:0)))).ToList();
 public HotkeyDialog(List<Hotkey> values):base("단축키 설정"){Section("단축키",430);
  foreach(var item in values){var key=new ThemedCombo(){DropDownStyle=ComboBoxStyle.DropDownList,Width=75};key.Items.AddRange(Enumerable.Range((int)Keys.F1,12).Select(k=>(object)(Keys)k).Concat(Enumerable.Range((int)Keys.A,26).Select(k=>(object)(Keys)k)).ToArray());key.SelectedItem=item.Key;var ctrl=new CheckBox(){Text="Ctrl",Checked=(item.Modifiers&2)!=0,AutoSize=true};var alt=new CheckBox(){Text="Alt",Checked=(item.Modifiers&1)!=0,AutoSize=true};var shift=new CheckBox(){Text="Shift",Checked=(item.Modifiers&4)!=0,AutoSize=true};rows.Add((key,ctrl,alt,shift));Row(item.Name,key,ctrl,alt,shift);}
  Body.Controls.Add(new Label(){Text="확인·취소는 기록 등록 대기 중에만 작동합니다.",AutoSize=true,Tag="dim",Margin=new(0,8,0,0)});Buttons();
 }
}
// 기록 등록 확인 창에 보일 것. 스코어는 SCORE 로 읽은 등록에만 있다. Tone 은 등록할 기록의 색("good"·"bad"·없음)
record ConfirmView(string Title,string Button,string Pattern,string Judge,string NewRate,string? NewScore,string OldRate,string? OldScore,string? Tone,string? Note=null);
class Confirmation:CompactDialog
{
 protected override bool ShowWithoutActivation=>true;
 protected override CreateParams CreateParams{get{var p=base.CreateParams;p.ExStyle|=0x08000000;return p;}}
 protected override void WndProc(ref Message m){if(m.Msg==0x21){m.Result=(IntPtr)3;return;}base.WndProc(ref m);}
 public Action<bool>? Decision;
 readonly Label message=new(){AutoSize=true,MaximumSize=new(520,0)};
 public void SetText(string text){if(message.Text!=text)message.Text=text;}
 readonly FlowLayoutPanel holder=new(){AutoSize=true,FlowDirection=FlowDirection.TopDown,WrapContents=false,Margin=Padding.Empty};ConfirmView? shown;
 // 곡 이름, 꼬리표, 등록할 기록과 기존 기록(큰 숫자)으로 보여 준다. 같은 내용이면 다시 그리지 않는다
 public void Set(ConfirmView view)
 {
  if(view==shown)return;shown=view;holder.SuspendLayout();foreach(var old in holder.Controls.Cast<Control>().ToArray())old.Dispose();holder.Controls.Clear();
  var title=Boxes.Text(view.Title,null,13f,true,500);title.Margin=new(0,8,0,0);holder.Controls.Add(title);holder.Controls.Add(Boxes.Chips(view.Button,view.Pattern,view.Judge));
  holder.Controls.Add(Boxes.Stats(16.5f,("등록할 기록"+(view.NewScore==null?"":"  "+view.NewScore),view.NewRate,view.Tone),("기존 기록"+(view.OldScore==null?"":"  "+view.OldScore),view.OldRate,null)));
  if(view.Note!=null){var note=Boxes.Text(view.Note,"dim",9f);note.Margin=new(0,4,0,0);holder.Controls.Add(note);}
  holder.ResumeLayout();if(IsHandleCreated)Theme.Apply(this);
 }
 public Confirmation(ConfirmView view):this("등록하시겠습니까?"){Body.Controls.Add(holder);Body.Controls.SetChildIndex(holder,1);Set(view);}
 public Confirmation(string text):base("기록 등록 확인"){
  ShowInTaskbar=false;TopMost=true;StartPosition=FormStartPosition.Manual;Location=new(30,30);
  message.Text=text;Body.Controls.Add(message);
  var row=new FlowLayoutPanel(){AutoSize=true,WrapContents=false,Margin=new(0,10,0,0)};var yes=new Button(){Text="예",AutoSize=true,Tag="primary"};var no=new Button(){Text="아니요",AutoSize=true};yes.Click+=(_,_)=>Decision?.Invoke(true);no.Click+=(_,_)=>Decision?.Invoke(false);row.Controls.AddRange(new Control[]{yes,no});Body.Controls.Add(row);
  FormClosing+=(_,e)=>{if(e.CloseReason==CloseReason.UserClosing&&Decision!=null){e.Cancel=true;BeginInvoke(()=>Decision?.Invoke(false));}};
 }
 public void Finish(){Decision=null;Close();}
}
class StyleDialog:CompactDialog
{
 readonly Dictionary<string,object> data;readonly ComboBox font=new ThemedCombo(){Width=320,DropDownStyle=ComboBoxStyle.DropDownList};readonly Dictionary<string,Func<int>> numbers=new();readonly StylePreview preview;readonly Label size=new(){AutoSize=true,Tag="dim"};readonly string[] titles;
 public JsonElement Value{get{data["Font"]=font.Text;foreach(var n in numbers)data[n.Key]=n.Value();return JsonSerializer.SerializeToElement(data);}}
 public StyleDialog(JsonElement initial,Catalog catalog,bool showCurrent,List<Hotkey>? shortcuts=null):base("디자인 설정"){
  initial=BadgeDesign.Complete(initial);titles=OverlayText.Titles(catalog);data=initial.EnumerateObject().ToDictionary(p=>p.Name,p=>p.Value.ValueKind==JsonValueKind.String?(object)p.Value.GetString()!:p.Value.GetInt32());
  using(var installed=new InstalledFontCollection())font.Items.AddRange(installed.Families.Select(f=>(object)f.Name).ToArray());if(!font.Items.Contains((string)data["Font"]))font.Items.Add(data["Font"]);font.SelectedItem=data["Font"];Section("글꼴과 색",590);Row("글꼴",font);
  foreach(var (key,name) in new[]{("Above","달성 색상"),("Below","미달성 색상"),("Text","기본 글자 색상"),("Background","배경 색상")}){
   var button=new Button(){Tag="swatch",Text=(string)data[key],Width=140,Height=28,BackColor=ColorTranslator.FromHtml((string)data[key]),ForeColor=Color.White,UseVisualStyleBackColor=false};button.ForeColor=button.BackColor.GetBrightness()>.5?Color.Black:Color.White;
   button.Click+=(_,_)=>{using var d=new ColorDialog(){Color=ColorTranslator.FromHtml((string)data[key]),FullOpen=true};if(d.ShowDialog(this)==DialogResult.OK){data[key]="#"+d.Color.R.ToString("X2")+d.Color.G.ToString("X2")+d.Color.B.ToString("X2");button.Text=(string)data[key];button.BackColor=d.Color;button.ForeColor=d.Color.GetBrightness()>.5?Color.Black:Color.White;UpdatePreview();}};Row(name,button);
  }
  Section("글자 크기와 투명도",590);
  foreach(var (key,name,min,max) in new[]{("HeaderSize","곡 제목 글자 크기",8,96),("BadgeSize","버튼·난이도·판정 크기",8,96),("EventSize","등록·경신 문구 크기",8,96),("ResultRateSize","결과 레이트 크기",12,96),("ResultScoreSize","결과 스코어 크기",12,96),("GoalSize","최고 점수 글자 크기",8,96),("CurrentSize","현재 점수 글자 크기",8,96),("TextTransparency","글자 투명도 (%)",0,100),("BackgroundTransparency","배경 투명도 (%)",0,100)}){
      if(key.EndsWith("Size")){var slider=new TrackBar(){Minimum=12,Maximum=64,Value=Math.Clamp((int)data[key],12,64),Width=230,Height=30,AutoSize=false,TickStyle=TickStyle.None,SmallChange=1,LargeChange=4};var label=new Label(){Text=slider.Value.ToString(),Width=38,Height=30,TextAlign=ContentAlignment.MiddleLeft};numbers[key]=()=>slider.Value;slider.ValueChanged+=(_,_)=>{label.Text=slider.Value.ToString();UpdatePreview();};var sliderRow=new Panel(){Width=280,Height=30,Margin=Padding.Empty};slider.Location=Point.Empty;label.Location=new Point(235,0);sliderRow.Controls.AddRange(new Control[]{slider,label});Row(name,sliderRow);}else{var n=new NumericUpDown(){Minimum=min,Maximum=max,Value=Math.Clamp((int)data[key],min,max),Width=100};numbers[key]=()=>(int)n.Value;n.ValueChanged+=(_,_)=>UpdatePreview();Row(name,n);}
  }
  Section("미리보기",590);var mode=new ThemedCombo(){Width=170,DropDownStyle=ComboBoxStyle.DropDownList};mode.Items.AddRange(new object[]{"달성","미달성","미등록","악곡 감지 중","점수 확인 중","점수 확인 실패","등록 확인","첫 기록 등록","최고 기록 경신","최고 기록 유지"});mode.SelectedIndex=0;Row("미리보기",mode);
  preview=new StylePreview(()=>Value,titles,showCurrent,shortcuts){Width=570,Height=170};mode.SelectedIndexChanged+=(_,_)=>{preview.Mode=mode.SelectedIndex;preview.Restart();};
  var previewCurrent=new CheckBox(){Text="현재 점수 표시",AutoSize=true,Checked=showCurrent};previewCurrent.CheckedChanged+=(_,_)=>{preview.ShowCurrent=previewCurrent.Checked;UpdatePreview();};var replay=new Button(){Text="효과 다시 보기",AutoSize=true};replay.Click+=(_,_)=>preview.Restart();Row("미리보기 표시",previewCurrent,replay);
  var pb=new ThemedCombo(){DropDownStyle=ComboBoxStyle.DropDownList,Width=65};pb.Items.AddRange(new object[]{"4B","5B","6B","8B"});pb.SelectedItem="8B";var pp=new ThemedCombo(){DropDownStyle=ComboBoxStyle.DropDownList,Width=110};pp.Items.AddRange(new object[]{"NORMAL","HARD","MAXIMUM","SC"});pp.SelectedItem="SC";var pj=new ThemedCombo(){DropDownStyle=ComboBoxStyle.DropDownList,Width=90};pj.Items.AddRange(new object[]{"노말","하드","맥스"});pj.SelectedItem="하드";foreach(var c in new[]{pb,pp,pj})c.SelectedIndexChanged+=(_,_)=>{preview.ButtonText=pb.Text;preview.PatternText=pp.Text;preview.JudgeText=pj.Text;preview.Invalidate();};Row("미리보기 조건",pb,pp,pj);Body.Controls.Add(preview);Body.Controls.Add(size);font.SelectedIndexChanged+=(_,_)=>UpdatePreview();Buttons();UpdatePreview();AutoSize=false;AutoScroll=true;FormBorderStyle=FormBorderStyle.Sizable;MinimumSize=new(620,500);ClientSize=new(650,Math.Min(940,Screen.FromControl(this).WorkingArea.Height-90));
 }
 void UpdatePreview(){if(preview==null)return;var a=Value;int width=OverlayText.Width(a,titles);int height=OverlayLayout.Height(a,preview.ShowCurrent);preview.Height=(int)Math.Ceiling(height*Math.Min(1,preview.Width/(double)width))+4;preview.Invalidate();size.Text=$"OBS 소스 권장 크기: 너비 {OverlayLayout.ObsWidth} × 높이 {OverlayLayout.ObsHeight}\n등록·경신 효과는 같은 영역에 표시됩니다.\n미리보기는 축소됩니다. 투명도 100% = 완전 투명";}
}





