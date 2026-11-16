using System.Text.Json;
namespace LadderRecords;

// One normalized HUD font. References contain glyph features, never a previous RATE value.
sealed class HudRateDigits
{
 readonly Dictionary<int,double[][]> bank;
 public HudRateDigits(string path)=>bank=JsonSerializer.Deserialize<Dictionary<int,double[][]>>(File.ReadAllText(path))!;
 public record Reading(decimal Value,double Quality);
 public static (double[] Vector,double Norm) Feature(Bitmap image,int x,int y)=>Feature(new PixelPatch(image,x,y,x+8,y+15),x,y);
 // 화소는 PixelPatch 가 한꺼번에 읽어 둔 것을 본다(GetPixel 을 글자마다 144번 부르던 것과 값이 같다)
 static (double[] Vector,double Norm) Feature(PixelPatch image,int x,int y)
 {
  var pixels=new double[144];var rows=new double[16];var cols=new double[9];double mean=0;
  for(int yy=0;yy<16;yy++)for(int xx=0;xx<9;xx++){
   double v=image.Sum(x+xx,y+yy)/3d;pixels[yy*9+xx]=v;rows[yy]+=v/9;cols[xx]+=v/16;mean+=v/144;
  }
  double norm=0;for(int yy=0;yy<16;yy++)for(int xx=0;xx<9;xx++){int i=yy*9+xx;pixels[i]-=rows[yy]+cols[xx]-mean;norm+=pixels[i]*pixels[i];}
  norm=Math.Sqrt(norm);for(int i=0;i<pixels.Length;i++)pixels[i]/=Math.Max(1e-9,norm);
  return(pixels,norm);
 }
 (int Digit,double Quality,bool Accepted,bool Blank) Digit(PixelPatch frame,int x,int y)
 {
  var (feature,norm)=Feature(frame,x,y);if(norm<12)return(0,0,false,true);
  var scores=new List<(int Digit,double Score)>();
  foreach(var (digit,samples) in bank){double best=0;foreach(var sample in samples){double dot=0;for(int i=0;i<144;i++)dot+=sample[i]*feature[i];best=Math.Max(best,Math.Abs(dot));}scores.Add((digit,best));}
  var ranked=scores.OrderByDescending(s=>s.Score).ToArray();double margin=ranked[0].Score-ranked[1].Score;
  return(ranked[0].Digit,ranked[0].Score,ranked[0].Score>=.45&&margin>=.10,false);
 }
 public Reading? Read(Bitmap frame,int decimalX,int top)
 {
  // 아홉 자리(±1)에서 글자 다섯을 읽는 데 쓰는 화소를 한 번에 읽어 둔다: 가로 decimalX-28 ~ +22, 세로 top-1 ~ +16
  var pixels=new PixelPatch(frame,decimalX-28,top-1,decimalX+22,top+16);
  Reading? best=null;
  foreach(int dy in new[]{0,-1,1})foreach(int dx in new[]{0,-1,1}){
   var read=ReadAt(pixels,decimalX+dx,top+dy);if(read==null)continue;
   double quality=read.Quality-(.012*(Math.Abs(dx)+Math.Abs(dy)));
   if(best==null||quality>best.Quality)best=read with{Quality=quality};
  }
  return best;
 }
 Reading? ReadAt(PixelPatch frame,int decimalX,int top)
 {
   var values=new[]{-27,-18,-9,4,13}.Select(x=>Digit(frame,decimalX+x,top)).ToArray();
   int first=values[0].Accepted&&values[0].Digit==1&&values[1].Accepted&&values[1].Digit==0&&values[2].Accepted&&values[2].Digit==0?0
    :values[1].Accepted&&values[1].Digit!=0&&values[2].Accepted?1:values[1].Blank&&values[2].Accepted?2:-1;
   if(first<0||values.Skip(first).Any(v=>!v.Accepted))return null;
   int integer=0;for(int i=first;i<3;i++)integer=integer*10+values[i].Digit;
   decimal rate=integer+(values[3].Digit*10+values[4].Digit)/100m;if(rate>100)return null;
   return new(rate,values.Skip(first).Average(v=>v.Quality));
 }
 public Point? Locate(Bitmap frame,int decimalX,int top,decimal expected)
 {
  var pixels=new PixelPatch(frame,decimalX-30,top-4,decimalX+24,top+19);   // 가로 ±3, 세로 ±4 를 훑는 데 쓰는 화소
  Point? point=null;double quality=0;
  for(int dy=-4;dy<=4;dy++)for(int dx=-3;dx<=3;dx++){
   var read=ReadAt(pixels,decimalX+dx,top+dy);
   if(read?.Value==expected&&read.Quality>quality){quality=read.Quality;point=new(decimalX+dx,top+dy);}
  }return point;
 }
 public static bool HasLeadingInk(Bitmap frame,int decimalX,int top,int integerDigits)
  =>integerDigits<3&&Feature(frame,decimalX-(integerDigits+1)*9,top).Norm>=12;
}
