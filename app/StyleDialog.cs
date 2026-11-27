using System.Drawing.Drawing2D;
using System.Text.Json;
namespace LadderRecords;

// 오버레이 디자인 설정 창.
// 왼쪽에 네 색(달성·미달성·기본 글자·배경)과 글자·배경의 투명도, 오른쪽에 미리보기(열 가지 상태, 버튼·난이도·판정, 현재 점수 보기, 효과 다시 보기), 아래에 적용·취소.
// 미리보기의 예시 곡과 패턴은 창을 열 때 곡 목록에서 한 번 무작위로 뽑는다(곡 목록이 없으면 '아침형 인간' 8B SC 하드판정).
// 패턴은 그 곡에 실제로 있는 버튼·난이도에서, 판정은 셋 가운데서 뽑는다. 뽑힌 값은 버튼·난이도·판정 칸에 보이고 바꿀 수 있다. 점수는 뽑지 않는다(달성·미달성의 뜻이 흐려지지 않게).
// 글꼴과 글자 크기는 정해진 배치로 고정이라 고르는 칸이 없다. 예전 판이 저장한 그 값들은 건드리지 않고 그대로 돌려준다(OverlayStyle).
// 치수는 100% 때의 값이다: 내용 너비 832, 본문 여백 20, 왼쪽 270 · 사이 26 · 오른쪽 496, 칸과 단추의 높이 30.
class StyleDialog:CompactDialog
{
 const int Wide=832,Edge=20,LeftWidth=270,Between=26,RightWidth=StylePreview.BaseWidth,RowHeight=30;
 readonly JsonElement original;
 readonly Dictionary<string,ColorSwatch> colors=new();
 readonly Dictionary<string,PercentBox> percents=new();
 readonly StylePreview preview;
 static readonly string[] ButtonCounts={"4B","5B","6B","8B"},Patterns={"NORMAL","HARD","MAXIMUM","SC"},Judges={"노말","하드","맥스"};
 /// <summary>고른 값. 네 색과 두 투명도만 바뀌고 받은 값의 다른 키는 그대로다</summary>
 public JsonElement Value=>OverlayStyle.With(original,colors.ToDictionary(c=>c.Key,c=>c.Value.Code),percents.ToDictionary(p=>p.Key,p=>p.Value.Value));
 // 미리보기에는 적용하기 전의 값(치고 있는 숫자 포함)을 바로 보인다
 JsonElement Draft=>OverlayStyle.With(original,colors.ToDictionary(c=>c.Key,c=>c.Value.Code),percents.ToDictionary(p=>p.Key,p=>p.Value.Shown));

 /// <param name="catalog">예시 곡을 뽑을 곡 목록. 없으면 기본 예시('아침형 인간' 8B SC 하드판정)를 보인다</param>
 public StyleDialog(JsonElement initial,bool showCurrent,List<Hotkey>? shortcuts=null,Catalog? catalog=null):base("오버레이 디자인 설정")
 {
  original=initial.Clone();Body.Padding=Padding.Empty;
  // 예시: 패턴이 있는 곡 하나, 그 곡의 패턴 하나, 판정 하나
  string exampleSong=StylePreview.DefaultSong,exampleButton="8B",examplePattern="SC",exampleJudge="하드";
  if(catalog?.Songs.Where(s=>s.Charts.Any(c=>ButtonCounts.Contains(c.Button+"B")&&Patterns.Contains(c.Pattern))).ToArray() is {Length:>0} songs)
  {
   var song=songs[Random.Shared.Next(songs.Length)];var charts=song.Charts.Where(c=>ButtonCounts.Contains(c.Button+"B")&&Patterns.Contains(c.Pattern)).ToArray();var chart=charts[Random.Shared.Next(charts.Length)];
   exampleSong=OverlayText.Title(catalog,song);exampleButton=chart.Button+"B";examplePattern=chart.Pattern;exampleJudge=Judges[Random.Shared.Next(Judges.Length)];
  }
  static Label Name(string text){var label=Boxes.Text(text,null,10.5f);label.AutoSize=false;label.Size=new(150,RowHeight);label.TextAlign=ContentAlignment.MiddleLeft;return label;}
  static SectionHeader Head(string text,int width)=>new(text){Width=width,Keyed=true,Font=Theme.Px(AppFonts.Head,11.25f,FontStyle.Bold)};

  // ── 왼쪽: 색상, 투명도 ──
  var left=new Panel(){Location=new(Edge,Edge),Size=new(LeftWidth,340),TabIndex=0};
  left.Controls.Add(Head("색상",LeftWidth));
  int y=35;
  foreach(var (key,name) in OverlayStyle.Colors)
  {
   var label=Name(name);label.Location=new(0,y);
   var swatch=new ColorSwatch(name,ColorTranslator.FromHtml(OverlayStyle.ColorOf(initial,key))){Location=new(LeftWidth-ColorSwatch.BaseWidth,y)};
   swatch.Changed+=(_,_)=>preview?.Invalidate();
   colors[key]=swatch;left.Controls.Add(label);left.Controls.Add(swatch);y+=RowHeight+9;
  }
  y+=27-9;
  var second=Head("투명도",LeftWidth);second.Location=new(0,y);left.Controls.Add(second);y+=35;
  foreach(var (key,name) in OverlayStyle.Percents)
  {
   var label=Name(name);label.Location=new(0,y);
   var box=new PercentBox(name,OverlayStyle.PercentOf(initial,key)){Location=new(LeftWidth-PercentBox.BaseWidth,y)};
   box.Changed+=(_,_)=>preview?.Invalidate();
   percents[key]=box;left.Controls.Add(label);left.Controls.Add(box);y+=RowHeight+9;
  }
  var hint=Boxes.Text("100%로 설정하면 완전히 투명해집니다.","dim",9f,false,LeftWidth);hint.Location=new(0,y);left.Controls.Add(hint);

  // ── 오른쪽: 미리보기 ──
  var right=new Panel(){Location=new(Edge+LeftWidth+Between,Edge),Size=new(RightWidth,440),TabIndex=1};
  right.Controls.Add(Head("미리보기",RightWidth));
  ThemedCombo Pick(string name,int width,string chosen,params string[] items)
  {
   var combo=new ThemedCombo(){DropDownStyle=ComboBoxStyle.DropDownList,Width=width,Tallness=RowHeight,Roomy=true,AccessibleName=name};
   combo.Items.AddRange(items);combo.SelectedItem=chosen;return combo;
  }
  // 이름과 목록 상자를 한 줄에 놓고 그 줄의 오른쪽 끝을 돌려준다
  int Place(string name,ThemedCombo combo,int x,int top,int gap)
  {
   var label=Boxes.Text(name,null,10.5f);label.Location=new(x,top+(RowHeight-label.PreferredHeight)/2);right.Controls.Add(label);
   combo.Location=new(x+label.PreferredWidth+gap,top);right.Controls.Add(combo);return combo.Right;
  }
  var mode=Pick("미리보기 상태",150,"달성","달성","미달성","미등록","악곡 감지 중","점수 확인 중","점수 확인 실패","등록 확인","첫 기록 등록","최고 기록 경신","최고 기록 유지");
  Place("상태",mode,0,35,10);
  var buttons=Pick("미리보기 버튼",62,exampleButton,ButtonCounts);var pattern=Pick("미리보기 난이도",108,examplePattern,Patterns);var judge=Pick("미리보기 판정",70,exampleJudge,Judges);
  int x=Place("버튼",buttons,0,75,5);x=Place("난이도",pattern,x+12,75,5);Place("판정",judge,x+12,75,5);
  var current=new CheckBox(){Text="현재 점수 보기",AutoSize=true,Checked=showCurrent,Font=Theme.Px(AppFonts.Ui,10.5f)};
  current.Location=new(0,115+(RowHeight-current.PreferredSize.Height)/2);right.Controls.Add(current);
  var replay=new Button(){Text="효과 다시 보기",Size=new(118,RowHeight)};replay.Location=new(RightWidth-replay.Width,115);right.Controls.Add(replay);
  preview=new StylePreview(()=>Draft,showCurrent,shortcuts){Location=new(0,155),SongText=exampleSong,ButtonText=exampleButton,PatternText=examplePattern,JudgeText=exampleJudge};right.Controls.Add(preview);
  var size=Boxes.Text("OBS 권장 크기","dim",9f);size.Location=new(0,preview.Bottom+8);right.Controls.Add(size);
  var pixels=new Label(){Text=$"{OverlayLayout.ObsWidth} × {OverlayLayout.ObsHeight}",AutoSize=true,UseMnemonic=false,Margin=Padding.Empty,Font=Theme.Px(AppFonts.Latin,9f,FontStyle.Bold)};
  pixels.Location=new(size.PreferredWidth+5,size.Top);right.Controls.Add(pixels);
  right.Height=size.Top+size.PreferredHeight;

  mode.SelectedIndexChanged+=(_,_)=>{preview.Mode=mode.SelectedIndex;preview.Play(true);};   // 상태를 고르면 그 상태가 될 때의 연출을 한 번 보인다
  foreach(var combo in new[]{buttons,pattern,judge})combo.SelectedIndexChanged+=(_,_)=>{preview.ButtonText=buttons.Text;preview.PatternText=pattern.Text;preview.JudgeText=judge.Text;preview.Invalidate();};
  current.CheckedChanged+=(_,_)=>{preview.ShowCurrent=current.Checked;preview.Invalidate();};
  replay.Click+=(_,_)=>preview.Play(true);

  var main=new Panel(){Size=new(Wide,Edge+Math.Max(left.Height,right.Height)+Edge),Margin=Padding.Empty,TabIndex=0};
  main.Controls.Add(left);main.Controls.Add(right);Body.Controls.Add(main);

  // ── 아래: 적용, 취소 ──
  var footer=new Panel(){Size=new(Wide,1+10+RowHeight+10),Margin=Padding.Empty,Tag="field",TabIndex=1};
  footer.Controls.Add(new Panel(){Bounds=new(0,0,Wide,1),Tag="line"});
  var cancel=new Button(){Text="취소",DialogResult=DialogResult.Cancel,Size=new(72,RowHeight),Location=new(Wide-18-72,11),TabIndex=1};
  var ok=new Button(){Text="적용",DialogResult=DialogResult.OK,Tag="primary",Size=new(72,RowHeight),Location=new(Wide-18-72-8-72,11),TabIndex=0};
  footer.Controls.Add(ok);footer.Controls.Add(cancel);Body.Controls.Add(footer);
  AcceptButton=ok;CancelButton=cancel;
  preview.Play(false);
 }
}

// 색 하나를 고르는 칸: 색 조각과 색 코드(#RRGGBB). 누르면(또는 Space·Enter) Windows 의 색 고르기 창이 뜬다. 너비 112, 높이 30(100% 때의 값)
sealed class ColorSwatch:Control
{
 public const int BaseWidth=112;
 Color value;
 public event EventHandler? Changed;
 public ColorSwatch(string name,Color initial)
 {
  SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.Selectable,true);
  Size=new(BaseWidth,30);TabStop=true;Margin=Padding.Empty;Cursor=Cursors.Hand;AccessibleName=name;AccessibleRole=AccessibleRole.PushButton;
  Font=Theme.Px(AppFonts.Latin,9.75f,FontStyle.Bold);value=Color.FromArgb(255,initial);
 }
 [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
 public Color Value{get=>value;set{var next=Color.FromArgb(255,value);if(next.ToArgb()==this.value.ToArgb())return;this.value=next;Invalidate();Changed?.Invoke(this,EventArgs.Empty);}}
 public string Code=>"#"+value.R.ToString("X2")+value.G.ToString("X2")+value.B.ToString("X2");
 void Choose(){using var dialog=new ColorDialog(){Color=value,FullOpen=true};if(dialog.ShowDialog(FindForm())==DialogResult.OK)Value=dialog.Color;}
 protected override void OnClick(EventArgs e){base.OnClick(e);Focus();Choose();}
 protected override bool IsInputKey(Keys keyData)=>keyData==Keys.Enter||base.IsInputKey(keyData);   // Enter 가 창의 '적용'으로 넘어가지 않게
 protected override void OnKeyDown(KeyEventArgs e){base.OnKeyDown(e);if(e.KeyCode is Keys.Space or Keys.Enter){e.Handled=true;Choose();}}
 protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);Invalidate();}
 protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);Invalidate();}
 protected override void OnChangeUICues(UICuesEventArgs e){base.OnChangeUICues(e);Invalidate();}
 protected override void OnPaint(PaintEventArgs e)
 {
  var g=e.Graphics;g.Clear(Parent?.BackColor??Theme.Window);g.SmoothingMode=SmoothingMode.AntiAlias;
  float k=Height/30f;bool focus=Focused&&ShowFocusCues;   // 초점 표시는 키보드로 옮겨 왔을 때만 보인다(단추와 같은 규칙)
  using(var shape=BadgeDesign.Rounded(new RectangleF(focus?1:0,focus?1:0,Width-(focus?3:1),Height-(focus?3:1)),4*k))
  {
   using var fill=new SolidBrush(Theme.Field);g.FillPath(fill,shape);using var pen=new Pen(focus?Theme.Accent:Theme.Line,focus?2:1);g.DrawPath(pen,shape);
  }
  var block=new RectangleF(9*k,(Height-17*k)/2,23*k,17*k);
  using(var shape=BadgeDesign.Rounded(block,3*k)){using var fill=new SolidBrush(value);g.FillPath(fill,shape);using var pen=new Pen(Color.FromArgb(136,136,136,136));g.DrawPath(pen,shape);}
  int text=(int)Math.Round(block.Right+8*k);
  TextRenderer.DrawText(g,Code,Font,new Rectangle(text,0,Width-text,Height),Theme.Text,TextFormatFlags.VerticalCenter|TextFormatFlags.Left|TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix);
 }
}

// 0~100 의 숫자 칸과 "%". 너비 96, 높이 30(100% 때의 값). 치는 동안의 값(Shown)을 미리보기가 바로 쓴다
sealed class PercentBox:Panel
{
 public const int BaseWidth=96;
 public readonly NumericUpDown Number=new(){Minimum=0,Maximum=100,BorderStyle=BorderStyle.None,Tag="bare",Margin=Padding.Empty};
 public event EventHandler? Changed;
 /// <summary>확정된 값(칸을 벗어나거나 적용할 때 0~100 으로 맞춰진다)</summary>
 public int Value=>(int)Number.Value;
 /// <summary>지금 칸에 보이는 값. 숫자가 아닌 것을 치는 동안에는 마지막으로 읽은 값이다</summary>
 public int Shown{get;private set;}
 public PercentBox(string name,int value)
 {
  SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
  Size=new(BaseWidth,30);Margin=Padding.Empty;Font=Theme.Px(AppFonts.Ui,9.75f);
  Number.Font=Theme.Px(AppFonts.Latin,10.5f,FontStyle.Bold);Number.Value=Shown=Math.Clamp(value,0,100);Number.AccessibleName=name+" (%)";
  Number.Width=62;Number.Location=new(8,(Height-Number.Height)/2);Controls.Add(Number);
  void Center(){int top=(Height-Number.Height)/2;if(Number.Top!=top)Number.Top=top;}
  Number.SizeChanged+=(_,_)=>Center();SizeChanged+=(_,_)=>Center();
  void Read(){if(!int.TryParse(Number.Text,out int typed))return;typed=Math.Clamp(typed,0,100);if(typed==Shown)return;Shown=typed;Changed?.Invoke(this,EventArgs.Empty);}
  Number.TextChanged+=(_,_)=>Read();Number.ValueChanged+=(_,_)=>Read();
  Number.GotFocus+=(_,_)=>Invalidate();Number.LostFocus+=(_,_)=>Invalidate();Number.Enter+=(_,_)=>Invalidate();Number.Leave+=(_,_)=>Invalidate();
 }
 protected override void OnPaint(PaintEventArgs e)
 {
  var g=e.Graphics;g.Clear(Parent?.BackColor??Theme.Window);g.SmoothingMode=SmoothingMode.AntiAlias;BackColor=Theme.Field;
  float k=Height/30f;bool focus=Number.ContainsFocus;
  using(var shape=BadgeDesign.Rounded(new RectangleF(focus?1:0,focus?1:0,Width-(focus?3:1),Height-(focus?3:1)),4*k))
  {
   using var fill=new SolidBrush(Theme.Field);g.FillPath(fill,shape);using var pen=new Pen(focus?Theme.Accent:Theme.Line,focus?2:1);g.DrawPath(pen,shape);
  }
  TextRenderer.DrawText(g,"%",Font,new Rectangle(Number.Right+(int)Math.Round(4*k),0,Width-Number.Right,Height),Theme.Dim,TextFormatFlags.VerticalCenter|TextFormatFlags.Left|TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix);
 }
}
