using System.Drawing.Text;
namespace LadderRecords;

// 한 줄에 크기·굵기·색이 다른 글 조각을 밑줄(baseline)을 맞춰 그린다. 기록 창들이 직접 그리는 자리(목록의 칸, 히스토리의 표와 머리)에 쓴다.
// 조각마다 글꼴은 '그 글자가 있는 첫 글꼴'의 규칙으로 고른다(MixedLine). 그래서 숫자 조각에 한글이 섞여도(예: "99.54% · 환산") 한글은 일반 글자의 글꼴로 그려진다.
// 글자 크기는 디자인 기준의 px 로 적는다. 창의 글자 배수(Theme.TextScale)와 크기 단계(Theme.Scale)는 여기서 곱한다.
readonly record struct InkPart(string Text,Font[] Chain,Color Color,float Before=0);

static class Ink
{
 static int Px(float px)=>Math.Max(1,(int)Math.Round(Theme.S(Theme.TextPx(px))));
 /// <summary>숫자·영문은 Oxanium, 한글은 일반 글자(SUIT). 굵지 않은 글자는 둘 다 500 이다(Oxanium Medium, SUIT Medium)</summary>
 public static Font[] Number(float px,bool bold=true)
 {
  int size=Px(px);var style=bold?FontStyle.Bold:FontStyle.Regular;
  return new[]{MixedLine.Face(bold?AppFonts.Latin:AppFonts.LatinMedium,size,style),MixedLine.Face(bold?AppFonts.Ui:AppFonts.UiMedium,size,style),MixedLine.Face(AppFonts.Wide,size,style)};
 }
 /// <summary>곡 이름: 영문·숫자 Oxanium, 한글 SUITE, 그 밖은 Noto Sans JP (굵게)</summary>
 public static Font[] Title(float px){int size=Px(px);return new[]{MixedLine.Face(AppFonts.Latin,size),MixedLine.Face(AppFonts.Head,size),MixedLine.Face(AppFonts.Wide,size)};}
 /// <summary>제목 글자: SUITE (굵게)</summary>
 public static Font[] Head(float px){int size=Px(px);return new[]{MixedLine.Face(AppFonts.Head,size),MixedLine.Face(AppFonts.Wide,size)};}
 /// <summary>일반 글자의 600(SUIT SemiBold): 그래프의 작은 제목과 안내</summary>
 public static Font[] Semi(float px){int size=Px(px);return new[]{MixedLine.Face(AppFonts.UiSemi,size,FontStyle.Regular),MixedLine.Face(AppFonts.Wide,size,FontStyle.Regular)};}
 /// <summary>아티스트 이름: SUIT 의 600. 디자인 기준은 500 인데 옅은 색이라 가늘게 보여 한 단계 올린다</summary>
 public static Font[] Artist(float px)=>Semi(px);
 /// <summary>일반 글자: SUIT(보통은 500, 굵게는 700)</summary>
 public static Font[] Body(float px,bool bold=false)
 {
  int size=Px(px);var style=bold?FontStyle.Bold:FontStyle.Regular;
  return new[]{MixedLine.Face(bold?AppFonts.Ui:AppFonts.UiMedium,size,style),MixedLine.Face(AppFonts.Wide,size,style)};
 }
 static float Ascent(Font f)=>f.Size*f.FontFamily.GetCellAscent(f.Style)/f.FontFamily.GetEmHeight(f.Style);
 static float Descent(Font f)=>f.Size*f.FontFamily.GetCellDescent(f.Style)/f.FontFamily.GetEmHeight(f.Style);
 /// <summary>그 글꼴의 글자가 세로 가운데(middle)에 보이게 하는 밑줄의 자리. 대문자·숫자의 높이를 기준으로 맞춘다(글꼴의 위아래 여백은 글꼴마다 달라서 쓰지 않는다)</summary>
 public static float Baseline(float middle,Font[] chain)=>middle+chain[0].Size*.36f;
 public static float Width(IReadOnlyList<InkPart> parts){float width=0;foreach(var part in parts)width+=part.Before*Theme.Scale+MixedLine.Plain(part.Text,part.Chain).Width;return width;}
 /// <summary>x 에서 시작해 차례로 그린다. 그린 뒤의 x 를 돌려준다</summary>
 public static float Draw(Graphics g,IReadOnlyList<InkPart> parts,float x,float baseline)
 {
  var hint=g.TextRenderingHint;g.TextRenderingHint=TextRenderingHint.AntiAlias;
  foreach(var part in parts){using var ink=new SolidBrush(part.Color);x=MixedLine.Plain(part.Text,part.Chain).Draw(g,ink,x+part.Before*Theme.Scale,baseline);}
  g.TextRenderingHint=hint;return x;
 }
 /// <summary>정렬 방향의 작은 세모(머리글의 글 뒤). 글꼴에 ▲▼ 가 없을 수 있어 직접 그린다</summary>
 public static void Arrow(Graphics g,float x,float middle,bool down,Color color)
 {
  float wide=Theme.S(8f),tall=Theme.S(5f),top=middle-tall/2f;
  var smoothing=g.SmoothingMode;g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
  using(var fill=new SolidBrush(color))g.FillPolygon(fill,down?new[]{new PointF(x,top),new PointF(x+wide,top),new PointF(x+wide/2f,top+tall)}:new[]{new PointF(x,top+tall),new PointF(x+wide,top+tall),new PointF(x+wide/2f,top)});
  g.SmoothingMode=smoothing;
 }
 /// <summary>오른쪽 끝을 right 에 맞춰 그린다</summary>
 public static void DrawRight(Graphics g,IReadOnlyList<InkPart> parts,float right,float baseline)=>Draw(g,parts,right-Width(parts),baseline);
 /// <summary>한 줄의 글을 room 안에 그린다(넘치면 끝을 "…" 으로 줄인다). 줄였는지를 돌려준다</summary>
 public static bool DrawClipped(Graphics g,string text,Font[] chain,Color color,float x,float baseline,float room)
 {
  var line=MixedLine.Clip(text,chain,room);
  var hint=g.TextRenderingHint;g.TextRenderingHint=TextRenderingHint.AntiAlias;
  using(var ink=new SolidBrush(color))line.Draw(g,ink,x,baseline);
  g.TextRenderingHint=hint;return line.Cut;
 }

 // 기록의 점수를 그리는 조각들. 큰 SCORE, 작은 '점', 옅은 RATE(환산이면 '· 환산'이 붙는다)
 public static List<InkPart> Score(Play play,float scorePx,float ratePx,Color score,bool brackets,bool bold=true)=>Score(play.Score,PlaySource.Exact(play.Source),scorePx,ratePx,score,brackets,bold);
 public static List<InkPart> Score(int value,bool exact,float scorePx,float ratePx,Color score,bool brackets,bool bold=true)
 {
  string rate=ScoreText.Rate(value)+(exact?"":" · 환산");
  return new()
  {
   new($"{value:N0}",Number(scorePx,bold),score),
   new("점",Body(Math.Max(10f,scorePx*.72f),bold),score,1),
   new(brackets?$"({rate})":rate,Number(ratePx,false),Theme.Dim,6),
  };
 }
}
