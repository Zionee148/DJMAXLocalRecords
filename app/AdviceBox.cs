namespace LadderRecords;

// 주 화면 오른쪽 아래의 등록 실패 안내: 옅은 바탕, 왼쪽에 3px 띠, 사유(첫 문장)는 굵게, 다음 행동은 보통 굵기로 이어 쓴다.
// 글은 app\ScoreRunner.cs 의 것을 그대로 받는다("등록 실패: 사유. 다음 행동").
// 글은 문장 끝까지 보인다: 낱말 사이에서만 줄을 바꾸고(한글 낱말을 가운데에서 끊지 않는다) 줄 수만큼 높이가 늘어난다. 곡 이름의 한 줄 말줄임 규칙을 쓰지 않는다.
// 치수는 글자 13px 일 때의 값이다(안쪽 여백 위아래 8·좌우 11, 줄 높이 20). 글자가 커지면 같은 비율로 커진다.
sealed class AdviceBox:Control
{
 string lead="",rest="";Font? bold;
 readonly List<(string Text,bool Bold,int X,int Y)> words=new();
 public AdviceBox()
 {
  SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
  TabStop=false;Visible=false;Margin=Padding.Empty;AccessibleRole=AccessibleRole.Alert;
 }
 float Unit=>Font.Size/Theme.TextPx(13f);
 int U(float px)=>(int)Math.Round(px*Unit);
 Font Strong=>bold??=Theme.Bold(Font);
 /// <summary>보일 글. 첫 문장(". " 앞까지)이 사유다. 빈 글이면 숨긴다</summary>
 public void Set(string text)
 {
  int cut=text.IndexOf(". ",StringComparison.Ordinal);
  (lead,rest)=cut<0?(text,""):(text[..(cut+1)],text[(cut+2)..]);
  Text=text;AccessibleName=text;Arrange();Visible=text.Length>0;Invalidate();
 }
 protected override void OnFontChanged(EventArgs e){base.OnFontChanged(e);bold?.Dispose();bold=null;Arrange();}
 protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);Arrange();}
 int arrangedWidth=-1;string arrangedText="";float arrangedUnit;
 // 낱말을 줄에 놓는다. 너비·글·글자 크기가 그대로면 다시 하지 않는다. 높이는 줄 수에 맞춘다
 void Arrange()
 {
  if(arrangedWidth==Width&&arrangedText==Text&&arrangedUnit==Unit)return;
  arrangedWidth=Width;arrangedText=Text;arrangedUnit=Unit;words.Clear();
  int left=U(3)+U(11),right=Width-U(11),line=U(20),x=left,y=U(8);
  const TextFormatFlags plain=TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine;
  void Put(string part,bool strong)
  {
   var font=strong?Strong:Font;int gap=TextRenderer.MeasureText(" ",font,Size.Empty,plain).Width;
   foreach(string word in part.Split(' ',StringSplitOptions.RemoveEmptyEntries))
   {
    int wide=TextRenderer.MeasureText(word,font,Size.Empty,plain).Width;
    if(x>left&&x+wide>right){x=left;y+=line;}
    words.Add((word,strong,x,y));x+=wide+gap;
   }
  }
  Put(lead,true);Put(rest,false);
  int height=words.Count==0?0:y+line+U(8);
  if(Height!=height)Height=height;
 }
 /// <summary>지금의 너비에서 그 글이 차지할 높이(창의 높이가 가장 긴 안내까지 들어가는지 볼 때 쓴다)</summary>
 public int Lines=>words.Count==0?0:words.Select(w=>w.Y).Distinct().Count();
 protected override void OnPaint(PaintEventArgs e)
 {
  var g=e.Graphics;g.Clear(Theme.WarnBack);
  using(var band=new SolidBrush(Theme.Dark?Color.FromArgb(0xF5,0xB8,0x63):Color.FromArgb(0xA8,0x6C,0x18)))g.FillRectangle(band,0,0,U(3),Height);
  const TextFormatFlags plain=TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine;
  int line=U(20);
  foreach(var (text,strong,x,y) in words)
  {
   var font=strong?Strong:Font;int tall=TextRenderer.MeasureText(text,font,Size.Empty,plain).Height;
   TextRenderer.DrawText(g,text,font,new Point(x,y+(line-tall)/2),Theme.Warn,plain);
  }
 }
}
