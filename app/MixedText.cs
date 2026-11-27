using System.Drawing.Text;
using System.Runtime.InteropServices;
namespace LadderRecords;

// 한 줄에 글꼴을 섞어 그린다: 글자마다 앞선 글꼴부터 그 글자가 있는 첫 글꼴을 쓴다(브라우저의 font-family 차례와 같은 규칙).
// 곡 이름처럼 영문·한글·한자가 섞인 글에 쓴다(영문·숫자 Oxanium, 한글 SUITE, 그 밖은 Noto Sans JP).
// 글꼴마다 글자의 높낮이가 달라서, 위쪽이 아니라 글자가 놓이는 밑줄(baseline)을 맞춰 그린다.
sealed class MixedLine
{
 [DllImport("gdi32.dll",CharSet=CharSet.Unicode)]static extern uint GetGlyphIndicesW(IntPtr dc,string text,int count,[Out]ushort[] indices,uint flags);
 [DllImport("gdi32.dll")]static extern IntPtr SelectObject(IntPtr dc,IntPtr item);
 [DllImport("gdi32.dll")]static extern bool DeleteObject(IntPtr item);
 static readonly StringFormat Typo=new(StringFormat.GenericTypographic){FormatFlags=StringFormat.GenericTypographic.FormatFlags|StringFormatFlags.MeasureTrailingSpaces};
 static readonly Dictionary<(string Family,int Code),bool> known=new();
 static readonly Dictionary<(FontFamily Family,int Px,FontStyle Style),Font> faces=new();   // 글꼴 집합은 이름이 같아도 꺼낸 모음이 다르면 다른 것이다(AppFonts.LatinRegular)
 static readonly Bitmap scratch=new(1,1);
 static readonly Graphics measure=Ready(Graphics.FromImage(scratch));
 static Graphics Ready(Graphics g){g.TextRenderingHint=TextRenderingHint.AntiAlias;return g;}

 /// <summary>같은 글꼴·크기는 한 번만 만들어 두고 쓴다(곡이 바뀔 때마다 다시 만들지 않는다)</summary>
 public static Font Face(FontFamily family,int px,FontStyle style=FontStyle.Bold)
 {
  var key=(family,px,style);
  if(!faces.TryGetValue(key,out var font))faces[key]=font=new Font(family,px,family.IsStyleAvailable(style)?style:FontStyle.Regular,GraphicsUnit.Pixel);
  return font;
 }
 // 글꼴에 그 글자가 있는가. Windows 가 다른 글꼴로 바꿔 그려 주기 전의 답이다. 글꼴 집합마다 글자마다 한 번만 묻는다
 static bool Has(Font font,string element)
 {
  var key=(font.FontFamily.Name,char.ConvertToUtf32(element,0));
  if(known.TryGetValue(key,out bool has))return has;
  if(element.Length>1)return known[key]=false;   // 두 칸짜리 글자(이모지 등)는 묻는 방법이 없어 마지막 글꼴에 맡긴다
  IntPtr dc=measure.GetHdc(),handle=font.ToHfont(),old=SelectObject(dc,handle);
  try{var indices=new ushort[1];GetGlyphIndicesW(dc,element,1,indices,1);return known[key]=indices[0]!=0xFFFF;}
  finally{SelectObject(dc,old);DeleteObject(handle);measure.ReleaseHdc(dc);}
 }

 public readonly List<(string Text,Font Font)> Runs=new();
 public float Width{get;private set;}
 public float Spacing{get;private set;}   // 글자 사이를 좁히거나 넓히는 값(픽셀). 0 이면 글꼴이 정한 그대로
 public int Px{get;private set;}
 public bool Cut{get;private set;}       // 끝을 "…" 으로 줄였는가

 static List<string> Elements(string text){var list=new List<string>();var e=System.Globalization.StringInfo.GetTextElementEnumerator(text);while(e.MoveNext())list.Add((string)e.Current);return list;}
 static float Advance(string text,Font font)=>measure.MeasureString(text,font,PointF.Empty,Typo).Width;
 static MixedLine Build(string text,Font[] chain,float spacing)
 {
  var line=new MixedLine{Spacing=spacing};var run=new System.Text.StringBuilder();Font? current=null;float width=0;int count=0;
  foreach(string element in Elements(text))
  {
   // 빈칸은 앞 글자의 글꼴을 따른다(글꼴이 바뀌는 자리마다 빈칸의 너비가 달라지지 않게)
   Font pick=element==" "&&current!=null?current:chain.FirstOrDefault(f=>Has(f,element))??chain[^1];
   if(pick!=current&&run.Length>0){line.Runs.Add((run.ToString(),current!));run.Clear();}
   current=pick;run.Append(element);count++;
   if(spacing!=0)width+=Advance(element,pick);
  }
  if(run.Length>0)line.Runs.Add((run.ToString(),current!));
  line.Width=spacing!=0?width+spacing*Math.Max(0,count-1):line.Runs.Sum(r=>Advance(r.Text,r.Font));
  return line;
 }
 /// <summary>
 /// 한 줄에 맞춘다: 기본 크기로 재 보고, 넘치면 한 줄에 다 들어가는 가장 큰 크기(정수 픽셀)까지 줄인다. 하한은 기본 크기의 70% 를 올림한 값이다.
 /// 하한에서도 넘칠 때만 끝에 "…" 을 붙인다. 글자 수로 크기를 미리 정하지 않는다(실제로 그리는 글꼴로 잰다).
 /// </summary>
 /// <param name="chain">그 크기의 글꼴들(앞선 것부터 쓴다)</param>
 /// <param name="spacingEm">글자 사이(글자 크기에 대한 비율. 곡 이름은 SongCard.TitleSpacing 의 −0.025)</param>
 public static MixedLine Fit(string text,Func<int,Font[]> chain,int basePx,float room,float spacingEm=0)
 {
  int floor=(int)Math.Ceiling(basePx*.7);
  for(int px=basePx;px>=floor;px--){var line=Build(text,chain(px),spacingEm*px);if(line.Width<=room||text.Length==0){line.Px=px;return line;}}
  var fonts=chain(floor);var elements=Elements(text);
  for(int keep=elements.Count-1;keep>0;keep--)
  {
   var line=Build(string.Concat(elements.Take(keep)).TrimEnd()+"…",fonts,spacingEm*floor);
   if(line.Width<=room){line.Px=floor;line.Cut=true;return line;}
  }
  var last=Build("…",fonts,0);last.Px=floor;last.Cut=true;return last;
 }
 /// <summary>크기를 줄이지 않고 한 줄에 맞춘다: 넘치면 끝을 "…" 으로 줄인다(보조 창의 곡명은 기본 글자 크기로 한 줄 말줄임한다)</summary>
 public static MixedLine Clip(string text,Font[] chain,float room,float spacing=0)
 {
  var whole=Build(text,chain,spacing);whole.Px=(int)Math.Round(chain[0].Size);
  if(whole.Width<=room||text.Length==0)return whole;
  var elements=Elements(text);int low=0,high=elements.Count-1;MixedLine? kept=null;
  while(low<=high)
  {
   int middle=(low+high)/2;var line=Build(string.Concat(elements.Take(middle)).TrimEnd()+"…",chain,spacing);
   if(line.Width<=room){kept=line;low=middle+1;}else high=middle-1;
  }
  kept??=Build("…",chain,0);kept.Px=whole.Px;kept.Cut=true;return kept;
 }
 /// <summary>줄이지 않고 그 크기 그대로 잰다</summary>
 public static MixedLine Plain(string text,Font[] chain,float spacing=0){var line=Build(text,chain,spacing);line.Px=(int)Math.Round(chain[0].Size);return line;}

 static float Ascent(Font f)=>f.Size*f.FontFamily.GetCellAscent(f.Style)/f.FontFamily.GetEmHeight(f.Style);
 /// <summary>x 에서 시작해 글자의 밑줄을 baseline 에 맞춰 그린다. 그린 뒤의 x 를 돌려준다</summary>
 public float Draw(Graphics g,Brush ink,float x,float baseline)
 {
  foreach(var (text,font) in Runs)
  {
   float top=baseline-Ascent(font);
   if(Spacing==0){g.DrawString(text,font,ink,x,top,Typo);x+=Advance(text,font);continue;}
   foreach(string element in Elements(text)){g.DrawString(element,font,ink,x,top,Typo);x+=Advance(element,font)+Spacing;}
  }
  return x;
 }
}
