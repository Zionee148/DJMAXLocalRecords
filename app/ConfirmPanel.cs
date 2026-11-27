using System.Drawing.Text;
namespace LadderRecords;

// 주 화면 오른쪽의 저장 확인: 목표·현재가 있던 자리(455×143)에 이번 기록과 기존 최고 기록, 그 아래에 저장·취소 단추를 보인다.
// SCORE 를 크게, RATE 를 그 아래에 둔다. 이번 기록의 색: 기존 기록 이상이면 연두, 낮으면 붉은색, 첫 기록이면 보통 글자색(낮은 점수도 저장할 수 있다).
// 점수와 단추 사이 8, 두 단추 사이 7. 단추에는 지금 지정된 단축키가 함께 보인다. 등록 확인을 따로 창으로 띄우지 않으므로 게임에서 포커스를 가져가지 않는다.
// 치수는 100% 때의 값이고 그릴 때 창의 크기 단계(Theme.S)만큼 키운다.
sealed class ConfirmPanel:Panel
{
 public readonly KeyButton Save=new("저장"){Tag="primary"},Cancel=new("취소");
 ConfirmView? shown;
 public ConfirmView? Shown=>shown;
 public ConfirmPanel()
 {
  SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
  Size=new(455,143);Margin=Padding.Empty;Visible=false;
  Controls.Add(Save);Controls.Add(Cancel);Place();
 }
 // 단추의 자리는 지금의 글자 크기를 따라 다시 잡는다(창이 뜰 때 글꼴이 커지면 단추의 크기가 바뀐다)
 float Unit=>Font.Size/Theme.TextPx(13f);
 void Place(){int top=(int)Math.Round(100*Unit);Save.Location=new(0,top);Cancel.Location=new(Save.Right+(int)Math.Round(7*Unit),top);}
 protected override void OnFontChanged(EventArgs e){base.OnFontChanged(e);Place();}
 protected override void OnLayout(LayoutEventArgs e){base.OnLayout(e);Place();}
 /// <summary>보일 것을 바꾼다. 같은 내용이면 다시 그리지 않는다(확인을 기다리는 동안 기존 기록이 바뀌면 그 값으로 다시 그린다)</summary>
 public void Set(ConfirmView view){if(view==shown)return;shown=view;Invalidate();}
 /// <summary>단추에 보일 단축키(지금 지정된 조합)</summary>
 public void Keys(string save,string cancel){if(Save.Key==save&&Cancel.Key==cancel)return;Save.Key=save;Cancel.Key=cancel;Place();}

 // 한 칸: 이름, 큰 값(SCORE 가 있으면 SCORE 와 "점", 없으면 RATE 나 "미등록"), 그 아래의 RATE
 void Column(Graphics g,int x,string name,string? score,string rate,Color ink)
 {
  TextRenderer.DrawText(g,name,Font,new Point(x,Theme.S(12)),Theme.Dim,TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix);
  using var strong=new SolidBrush(ink);using var soft=new SolidBrush(Theme.Dim);
  var big=new[]{MixedLine.Face(AppFonts.Latin,Theme.S(32))};float tight=Theme.S(-.9f);
  if(score!=null)
  {
   float end=MixedLine.Plain(score,big,tight).Draw(g,strong,x,Theme.S(63f));
   MixedLine.Plain("점",new[]{MixedLine.Face(AppFonts.Ui,Theme.S(13))}).Draw(g,strong,end+Theme.S(4f),Theme.S(63f));
   MixedLine.Plain(rate,new[]{MixedLine.Face(AppFonts.Latin,Theme.S(16))}).Draw(g,soft,x,Theme.S(88f));
  }
  else if(rate.EndsWith('%'))MixedLine.Plain(rate,big,tight).Draw(g,strong,x,Theme.S(63f));
  else MixedLine.Plain(rate,new[]{MixedLine.Face(AppFonts.Ui,Theme.S(21)),MixedLine.Face(AppFonts.Latin,Theme.S(21))}).Draw(g,strong,x,Theme.S(60f));
 }
 protected override void OnPaint(PaintEventArgs e)
 {
  var g=e.Graphics;var back=Parent?.BackColor??Theme.Side;g.Clear(back);if(BackColor!=back)BackColor=back;
  using(var line=new Pen(Theme.Line))g.DrawLine(line,0,Height-1,Width,Height-1);
  if(shown is not {} view)return;
  g.TextRenderingHint=TextRenderingHint.AntiAlias;
  Column(g,0,"이번 기록",view.NewScore,view.NewRate,view.Tone=="good"?Theme.Good:view.Tone=="bad"?Theme.Lower:Theme.Text);
  Column(g,Theme.S(232),"기존 최고 기록",view.OldScore,view.OldRate,Theme.Text);
  if(view.Note!=null)TextRenderer.DrawText(g,view.Note,Font,new Rectangle(Theme.S(232),Theme.S(74),Width-Theme.S(232),Theme.S(18)),Theme.Dim,TextFormatFlags.Right|TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix);
 }
}

// 이름과 단축키를 함께 보이는 단추(저장 Ctrl+F10). 겉모습은 다른 단추와 같이 Theme 가 그리고, 글자만 이 단추가 그린다. Text 는 이름 그대로다(화면 읽기 프로그램과 검사가 본다).
// 크기는 글자에 맞춰 스스로 정한다: 안쪽 여백 좌우 10, 이름과 단축키 사이 9, 높이 32(글자 13px 일 때의 값. 글자가 커지면 같은 비율로 커진다)
sealed class KeyButton:Button
{
 string key="";
 public KeyButton(string name)
 {
  Text=name;AutoSize=false;Margin=Padding.Empty;Font=Theme.Px(AppFonts.Ui,9.75f,FontStyle.Bold);Fit();
 }
 [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
 public string Key{get=>key;set{if(key==value)return;key=value;AccessibleDescription=value;Fit();Invalidate();}}
 float Unit=>Font.Size/Theme.TextPx(13f);
 int U(float px)=>(int)Math.Round(px*Unit);
 Font KeyFont=>MixedLine.Face(AppFonts.Latin,U(13));
 int NameWidth=>TextRenderer.MeasureText(Text,Font,Size.Empty,TextFormatFlags.NoPadding).Width;
 int KeyWidth=>key.Length==0?0:TextRenderer.MeasureText(key,KeyFont,Size.Empty,TextFormatFlags.NoPadding).Width;
 void Fit(){var size=new Size(U(10)+NameWidth+(key.Length==0?0:U(9)+KeyWidth)+U(10),U(32));MinimumSize=Size.Empty;MinimumSize=size;Size=size;}
 protected override void OnFontChanged(EventArgs e){base.OnFontChanged(e);Fit();}
 /// <summary>단추의 글자를 그린다(Theme.PaintButton 이 바탕을 그린 뒤에 부른다)</summary>
 public void DrawContent(Graphics g,Color ink)
 {
  const TextFormatFlags flags=TextFormatFlags.VerticalCenter|TextFormatFlags.Left|TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine;
  TextRenderer.DrawText(g,Text,Font,new Rectangle(U(10),0,NameWidth+2,Height),ink,flags);
  if(key.Length>0)TextRenderer.DrawText(g,key,KeyFont,new Rectangle(U(10)+NameWidth+U(9),0,KeyWidth+2,Height),ink,flags);
 }
}
