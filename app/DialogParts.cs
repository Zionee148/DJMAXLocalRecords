using System.Drawing.Drawing2D;
namespace LadderRecords;

// 세부 창에 함께 쓰는 부품. 수치는 디자인 기준의 100% 때 값이다.

// 빈 칸에 옅은 안내 글(Hint)을 보이는 글 칸. Windows 가 그리는 안내 글(PlaceholderText)은 색을 정할 수 없고 다크에서는 바탕보다 어두워 잘 읽히지 않는다
// 그래서 직접 그린다: 글이 없고 초점이 없을 때만 보인다
sealed class HintBox:TextBox
{
 string hint="";
 [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
 public string Hint{get=>hint;set{hint=value??"";if(IsHandleCreated)Invalidate();}}
 /// <summary>지금 안내 글이 보이는가</summary>
 internal bool HintShown=>hint.Length>0&&!Focused&&TextLength==0;
 protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);if(hint.Length>0)Invalidate();}
 protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);if(hint.Length>0)Invalidate();}
 protected override void WndProc(ref Message m)
 {
  base.WndProc(ref m);
  if(m.Msg!=0x000F||!HintShown||!IsHandleCreated)return;
  using var g=Graphics.FromHwnd(Handle);
  TextRenderer.DrawText(g,hint,Font,new Rectangle(1,0,ClientSize.Width-1,ClientSize.Height),Theme.Mix(Theme.Dim,BackColor,.45),BackColor,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix|TextFormatFlags.NoPadding|TextFormatFlags.EndEllipsis);
 }
}

// 한 줄 입력칸: 높이 34(주 화면의 주소 칸은 27), 옅은 바탕, 테두리 한 줄. 초점이 있으면 강조색 외곽선 두 줄.
sealed class InputBox:Panel
{
 public readonly HintBox Box=new(){BorderStyle=BorderStyle.None,Tag="bare"};
 public InputBox(int width,string text="",int maxLength=0,int height=34)
 {
  SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
  Size=new(width,height);Margin=Padding.Empty;Tag="input";
  Box.Font=Theme.Px(AppFonts.Ui,height<34?9f:10.5f);Box.Text=text;if(maxLength>0)Box.MaxLength=maxLength;
  Box.Width=width-22;Box.Location=new(11,(height-Box.PreferredHeight)/2);Box.Anchor=AnchorStyles.Left|AnchorStyles.Right|AnchorStyles.Top;
  Controls.Add(Box);
  Box.GotFocus+=(_,_)=>Invalidate();Box.LostFocus+=(_,_)=>Invalidate();
  // 글 칸의 높이는 글꼴이 정한다. 상자나 글꼴이 커지면 세로 가운데에 다시 놓는다
  void Center(){int top=(Height-Box.Height)/2;if(Box.Top!=top)Box.Top=top;}
  Box.SizeChanged+=(_,_)=>Center();SizeChanged+=(_,_)=>Center();
  Click+=(_,_)=>Box.Focus();Cursor=Cursors.IBeam;
 }
 protected override void OnPaint(PaintEventArgs e)
 {
  var g=e.Graphics;g.Clear(Parent?.BackColor??Theme.Window);g.SmoothingMode=SmoothingMode.AntiAlias;
  Box.BackColor=Theme.Field;Box.ForeColor=Theme.Text;
  bool focus=Box.Focused;
  using var shape=BadgeDesign.Rounded(new RectangleF(focus?1:0,focus?1:0,Width-(focus?3:1),Height-(focus?3:1)),3);
  using(var fill=new SolidBrush(Theme.Field))g.FillPath(fill,shape);
  using var pen=new Pen(focus?Theme.Fill:Theme.Line,focus?2:1);g.DrawPath(pen,shape);
 }
}

// 테두리가 있는 옅은 상자(삭제할 프로필, 대상 기록 등을 보여 준다)
sealed class FieldPanel:Panel
{
 bool keyed;
 /// <summary>테두리를 강조색으로 그린다(고른 선택지의 상자)</summary>
 public void Key(bool on){if(keyed==on)return;keyed=on;Invalidate();}
 public FieldPanel(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);Margin=Padding.Empty;Tag="fieldbox";}
 protected override void OnPaint(PaintEventArgs e)
 {
  var g=e.Graphics;g.Clear(Parent?.BackColor??Theme.Window);g.SmoothingMode=SmoothingMode.AntiAlias;BackColor=Theme.Field;
  using var shape=BadgeDesign.Rounded(new RectangleF(0,0,Width-1,Height-1),3);
  using(var fill=new SolidBrush(Theme.Field))g.FillPath(fill,shape);using var pen=new Pen(keyed?Theme.Accent:Theme.Line);g.DrawPath(pen,shape);
 }
}

// 낱말 사이에서만 줄을 바꾸는 글. Windows 의 Label 은 한글을 글자 사이에서도 줄을 바꿔 "…그대로 사용합니" / "다." 처럼 낱말을 가운데에서 꺾는다
// (주 화면의 등록 실패 안내 AdviceBox 와 같은 방식). 너비는 정해 주고 높이는 줄 수에 맞춘다. "\n" 은 줄을 바꾼다.
// 한 낱말이 너비보다 길면 그 낱말만 글자 사이에서 꺾는다(빈칸 없는 긴 경로·주소는 이것 대신 WrapBox 를 쓴다).
// 글은 창 글자(Text)로도 원래 그대로 갖는다: 시험 도구(tools\update_test.py)가 자식 창의 글자로 창을 읽는다. 크기 단계만큼 글꼴과 너비가 커지면 다시 놓는다
sealed class WordLabel:Control
{
 readonly List<(string Text,int X,int Y)> words=new();
 string tone;
 /// <summary>"" 보통 · "strong" 굵게(글자색은 보통) · "dim" 옅게 · "bad"(취소·실패의 까닭) · "accent"</summary>
 [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
 public string Tone{get=>tone;set{tone=value??"";Invalidate();}}
 public WordLabel(string text,int width,float points,string tone="")
 {
  SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
  this.tone=tone??"";TabStop=false;Margin=Padding.Empty;AccessibleRole=AccessibleRole.StaticText;
  Font=Theme.Px(AppFonts.Ui,points,this.tone=="strong"?FontStyle.Bold:FontStyle.Regular);
  Text=text;AccessibleName=text;Width=width;Arrange();
 }
 const TextFormatFlags Plain=TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine;
 int arrangedWidth=-1;string arrangedText="";Font? arrangedFont;
 /// <summary>줄 수(개발용 검사)</summary>
 internal int Lines=>words.Count==0?0:words.Select(word=>word.Y).Distinct().Count();
 /// <summary>놓인 낱말들(개발용 검사: 한 낱말이 두 줄에 걸치지 않는가)</summary>
 internal IReadOnlyList<(string Text,int X,int Y)> Placed=>words;
 protected override void OnFontChanged(EventArgs e){base.OnFontChanged(e);Arrange();}
 protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);Arrange();}
 protected override void OnTextChanged(EventArgs e){base.OnTextChanged(e);AccessibleName=Text;Arrange();}
 // 낱말을 줄에 놓는다. 너비·글·글꼴이 그대로면 다시 하지 않는다. 높이는 줄 수에 맞춘다(줄 높이는 Label 과 같은 글꼴의 높이)
 void Arrange()
 {
  if(Width<=0||arrangedWidth==Width&&arrangedText==Text&&ReferenceEquals(arrangedFont,Font))return;
  arrangedWidth=Width;arrangedText=Text;arrangedFont=Font;words.Clear();
  int line=Font.Height,space=TextRenderer.MeasureText(" ",Font,Size.Empty,Plain).Width,y=0;
  foreach(string paragraph in Text.Replace("\r\n","\n").Split('\n'))
  {
   // 줄 앞의 빈칸만큼 들여 쓴다(패치노트의 하위 목록이 상위 목록과 같은 자리에 그려지지 않게). '· '로 시작하면 다음 줄은 가운뎃점 뒤에 맞춘다
   int spaces=paragraph.Length-paragraph.TrimStart(' ').Length,indent=Math.Min(Width/2,spaces*space);
   string body=paragraph.TrimStart(' ');
   int hang=indent+(body.StartsWith("· ",StringComparison.Ordinal)?TextRenderer.MeasureText("·",Font,Size.Empty,Plain).Width+space:0);
   if(hang>Width/2)hang=indent;
   int x=indent;bool first=true;
   foreach(string word in body.Split(' ',StringSplitOptions.RemoveEmptyEntries))
   {
    int wide=TextRenderer.MeasureText(word,Font,Size.Empty,Plain).Width;
    if(!first&&x+wide>Width){x=hang;y+=line;}
    first=false;
    if(x+wide<=Width){words.Add((word,x,y));x+=wide+space;continue;}
    // 줄보다 긴 낱말: 글자 사이에서 꺾는다
    int start=0;
    while(start<word.Length)
    {
     int end=start+1;
     while(end<word.Length&&TextRenderer.MeasureText(word[start..(end+1)],Font,Size.Empty,Plain).Width<=Width-x)end++;
     string piece=word[start..end];words.Add((piece,x,y));
     x+=TextRenderer.MeasureText(piece,Font,Size.Empty,Plain).Width+space;start=end;
     if(start<word.Length){x=hang;y+=line;}
    }
   }
   y+=line;
  }
  if(Height!=y)Height=y;
  Invalidate();   // 높이가 그대로여도 글이 바뀌었으면 다시 그린다(진행 창의 상세 글)
 }
 protected override void OnPaint(PaintEventArgs e)
 {
  var g=e.Graphics;g.Clear(Parent?.BackColor??Theme.Window);
  var ink=tone switch{"dim"=>Theme.Dim,"bad"=>Theme.Bad,"accent"=>Theme.Accent,_=>Theme.Text};
  foreach(var (text,x,y) in words)TextRenderer.DrawText(g,text,Font,new Point(x,y),ink,Plain);
 }
}

// 세로로만 굴리는 칸. 내용이 화면(또는 정해 둔 자리)보다 길 때만 굴린다(Scrolling). 창과 본문에 스크롤 막대를 두지 않는 것이 기준이고,
// 작업 영역을 넘는 경우에만 첫 안내 줄과 아래 단추는 그대로 두고 그 사이를 굴린다.
// 가로 막대는 두지 않는다. 안의 컨트롤에 키보드 초점이 오면(Tab) 그 컨트롤이 보이게 굴린다
sealed class VerticalScroller:Panel
{
 public VerticalScroller(){Margin=Padding.Empty;TabStop=false;}
 [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
 public bool Scrolling
 {
  get=>AutoScroll;
  set
  {
   if(AutoScroll==value)return;
   if(value){AutoScroll=false;HorizontalScroll.Maximum=0;HorizontalScroll.Visible=false;HorizontalScroll.Enabled=false;VerticalScroll.Visible=true;AutoScroll=true;}
   else{AutoScrollPosition=Point.Empty;AutoScroll=false;}
  }
 }
 protected override void OnControlAdded(ControlEventArgs e){base.OnControlAdded(e);if(e.Control!=null)Follow(e.Control);}
 // 초점이 들어오는 컨트롤마다(안쪽 것까지): 바깥 묶음부터 차례로 Enter 가 오므로 마지막에 초점을 받은 컨트롤이 보이게 된다
 void Follow(Control control)
 {
  control.Enter+=(_,_)=>{if(AutoScroll)ScrollControlIntoView(control);};
  control.ControlAdded+=(_,e)=>{if(e.Control!=null)Follow(e.Control);};
  foreach(Control child in control.Controls)Follow(child);
 }
}

static class Parts
{
 // 사선이 붙은 제목 한 줄(가로선 없음)과, 같은 줄 오른쪽 끝의 옅은 안내
 public static Control TitleLine(string title,int width,string? hint=null)
 {
  var line=new Panel(){Size=new(width,22),Margin=Padding.Empty};
  var head=new SectionHeader(title){Rule=false,Location=Point.Empty};head.Width=head.TextWidth+2;line.Controls.Add(head);
  if(hint!=null){var note=Boxes.Text(hint,"dim",9f);note.AutoSize=true;line.Controls.Add(note);note.Location=new(width-note.PreferredWidth,(22-note.PreferredHeight)/2+1);}
  return line;
 }
 // 창의 아래 오른쪽에 놓는 단추 줄. 단추의 너비는 70 이상, 사이는 6
 public static Control Footer(int width,params Button[] buttons)
 {
  var row=new Panel(){Size=new(width,27),Margin=Padding.Empty};int right=width;
  foreach(var button in buttons.Reverse())
  {
   button.AutoSize=false;button.MinimumSize=new(70,27);button.Font=Theme.Px(AppFonts.Ui,9.75f,button.Tag is "primary" or "danger" or "default"?FontStyle.Bold:FontStyle.Regular);
   button.Size=new(Math.Max(70,TextRenderer.MeasureText(button.Text,button.Font).Width+22),27);
   button.Location=new(right-button.Width,0);right=button.Left-6;row.Controls.Add(button);
  }
  // 탭 차례는 왼쪽 단추부터
  for(int i=0;i<buttons.Length;i++)buttons[i].TabIndex=i;
  return row;
 }
 public static Label Value(string text,float points=12f)=>new(){Text=text,AutoSize=true,UseMnemonic=false,Margin=Padding.Empty,Font=Theme.Px(AppFonts.Ui,points,FontStyle.Bold)};
}
