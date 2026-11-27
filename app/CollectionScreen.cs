using System.Drawing.Imaging;
using System.Runtime.InteropServices;
namespace LadderRecords;
// 컬렉션 가져오기: 게임의 COLLECTION › PLAY INFO › MUSIC DATA 화면에서 읽을 자리와, 그 자리에서 글자로 볼 화소를 고르는 법.
// 좌표는 1920×1080 기준이다. 다른 크기의 화면은 받지 않는다(늘린 그림은 글자의 화소가 달라진다).
// 자리와 문턱은 실제 게임을 녹화한 영상 둘로 정했다. 문턱을 바꾸면 tools\build_collection_data.py 의 같은 값도 바꾼다.

/// <summary>화면 한 장의 화소(BGRA).</summary>
sealed class CollectionFrame
{
 public const int Width=1920,Height=1080;
 public readonly byte[] Bytes;
 /// <summary>BGRA 로 이어진 화소(1920×1080×4)를 그대로 쓴다. 검사가 녹화의 프레임을 넣을 때 쓴다.</summary>
 public CollectionFrame(byte[] bgra){if(bgra.Length!=Width*Height*4)throw new ArgumentException("화면의 크기가 1920×1080 이 아닙니다.");Bytes=bgra;}
 public CollectionFrame(Bitmap bitmap)
 {
  if(bitmap.Width!=Width||bitmap.Height!=Height)throw new ArgumentException("화면의 크기가 1920×1080 이 아닙니다.");
  Bytes=new byte[Width*Height*4];
  var data=bitmap.LockBits(new Rectangle(0,0,Width,Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
  try{for(int y=0;y<Height;y++)Marshal.Copy(data.Scan0+y*data.Stride,Bytes,y*Width*4,Width*4);}finally{bitmap.UnlockBits(data);}
 }
 /// <summary>세 색 가운데 가장 어두운 값. 흰 글자는 세 색이 모두 밝다.</summary>
 public int Low(int x,int y){int i=(y*Width+x)*4;return Math.Min(Bytes[i],Math.Min(Bytes[i+1],Bytes[i+2]));}
 /// <summary>세 색 가운데 가장 밝은 값. 색 글자는 한 색만 밝다.</summary>
 public int High(int x,int y){int i=(y*Width+x)*4;return Math.Max(Bytes[i],Math.Max(Bytes[i+1],Bytes[i+2]));}
 /// <summary>화면 전체를 PNG 로(검수 창이 보일 캡처. 메모리에만 둔다).</summary>
 public byte[] ToPng()
 {
  using var bitmap=new Bitmap(Width,Height,PixelFormat.Format32bppRgb);var data=bitmap.LockBits(new Rectangle(0,0,Width,Height),ImageLockMode.WriteOnly,PixelFormat.Format32bppRgb);
  try{for(int y=0;y<Height;y++)Marshal.Copy(Bytes,y*Width*4,data.Scan0+y*data.Stride,Width*4);}finally{bitmap.UnlockBits(data);}
  using var stream=new MemoryStream();bitmap.Save(stream,ImageFormat.Png);return stream.ToArray();
 }
 public Bitmap ToBitmap()
 {
  var bitmap=new Bitmap(Width,Height,PixelFormat.Format32bppArgb);var data=bitmap.LockBits(new Rectangle(0,0,Width,Height),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
  try{for(int y=0;y<Height;y++)Marshal.Copy(Bytes,y*Width*4,data.Scan0+y*data.Stride,Width*4);}finally{bitmap.UnlockBits(data);}
  return bitmap;
 }
}

static class CollectionScreen
{
 // 가운데 위: 고른 곡의 제목 한 줄(흰 글자). 긴 제목은 오른쪽 끝 근처에서 잘린다
 public const int TitleX=605,TitleY=284,TitleWidth=828,TitleHeight=50,TitleWhite=185;
 // 제목 위의 카테고리 한 줄(색 글자)
 public const int CategoryX=605,CategoryY=266,CategoryWidth=420,CategoryHeight=20,CategoryMargin=8,CategoryOpen=7,CategoryRise=24;
 // HIGH SCORE 표: 열은 4B·5B·6B·8B, 행은 NORMAL·HARD·MAXIMUM·SC
 public static readonly int[] Columns={930,1054,1178,1302};public static readonly int[] ScoreRows={477,501,525,549};
 public static readonly int[] Buttons={4,5,6,8};public static readonly string[] Patterns={"NORMAL","HARD","MAXIMUM","SC"};
 public const int CellLeft=6,CellWidth=112,CellHeight=21,CellWhite=170;
 // 표 왼쪽의 BEST(그 곡의 가장 높은 SCORE. 글자가 더 크다)
 public const int BestX=613,BestY=497,BestWidth=122,BestHeight=26;
 // 오른쪽 곡 목록. 아티스트 줄은 회색 글자이고 배경에 밝고 어두운 띠가 지나가, 굵은 바탕을 빼고 고른다
 public const int ListX=1475,ListY=330,ListWidth=365,ListHeight=595,ListOpen=9,ListRiseLow=28,ListRiseHigh=130,ListWhite=215;
 public const int ArtistLeft=78,ArtistWidth=285,ArtistHeight=26;

 public static Mask Title(CollectionFrame f)=>Bright(f,TitleX,TitleY,TitleWidth,TitleHeight,TitleWhite);
 /// <summary>HIGH SCORE 의 칸 하나. row 는 NORMAL·HARD·MAXIMUM·SC, column 은 4B·5B·6B·8B 의 차례.</summary>
 public static Mask Cell(CollectionFrame f,int row,int column)=>Bright(f,Columns[column]+CellLeft,ScoreRows[row],CellWidth,CellHeight,CellWhite);
 public static Mask Best(CollectionFrame f)=>Bright(f,BestX,BestY,BestWidth,BestHeight,CellWhite);
 static Mask Bright(CollectionFrame f,int x0,int y0,int width,int height,int white){var m=new Mask(width,height);for(int y=0;y<height;y++)for(int x=0;x<width;x++)m.Bits[y*width+x]=f.Low(x0+x,y0+y)>white;return m;}

 /// <summary>카테고리 줄에서 글자로 볼 화소. 글자의 색이 여러 가지라 가장 밝은 색에서 굵은 바탕을 빼고 고른다.</summary>
 public static Mask Category(CollectionFrame f)
 {
  int width=CategoryWidth,height=CategoryHeight+2*CategoryMargin,y0=CategoryY-CategoryMargin;
  var high=new int[width*height];for(int y=0;y<height;y++)for(int x=0;x<width;x++)high[y*width+x]=f.High(CategoryX+x,y0+y);
  var back=Opened(high,width,height,CategoryOpen);var m=new Mask(CategoryWidth,CategoryHeight);
  for(int y=0;y<CategoryHeight;y++)for(int x=0;x<width;x++){int i=(y+CategoryMargin)*width+x;m.Bits[y*width+x]=high[i]-back[i]>CategoryRise;}
  return m;
 }
 /// <summary>오른쪽 목록에서 아티스트 글자로 볼 화소(목록 전체의 크기).</summary>
 public static Mask ListLetters(CollectionFrame f)=>ListLetters(ListLow(f));
 /// <summary>오른쪽 목록의 화소마다 세 색 가운데 가장 어두운 값(나중에 글자를 고를 수 있게 따로 들고 있을 때 쓴다).</summary>
 public static byte[] ListLow(CollectionFrame f){var low=new byte[ListWidth*ListHeight];for(int y=0;y<ListHeight;y++)for(int x=0;x<ListWidth;x++)low[y*ListWidth+x]=(byte)f.Low(ListX+x,ListY+y);return low;}
 public static Mask ListLetters(byte[] listLow)
 {
  var low=new int[listLow.Length];for(int i=0;i<low.Length;i++)low[i]=listLow[i];
  var back=Opened(low,ListWidth,ListHeight,ListOpen);var m=new Mask(ListWidth,ListHeight);
  for(int i=0;i<low.Length;i++){int rise=low[i]-back[i];m.Bits[i]=rise>ListRiseLow&&rise<ListRiseHigh&&low[i]<ListWhite;}
  return m;
 }
 /// <summary>목록의 흰 글자(제목들). 목록이 아직 움직이는지 볼 때 쓴다.</summary>
 public static Mask ListBright(CollectionFrame f)=>Bright(f,ListX,ListY,ListWidth,ListHeight,200);
 /// <summary>목록의 한 줄(위쪽 y 부터 ArtistHeight 만큼)에서 아티스트 글이 놓이는 자리만 잘라 낸다.</summary>
 public static Mask ArtistBand(Mask letters,int top){var m=new Mask(ArtistWidth,ArtistHeight);for(int y=0;y<ArtistHeight;y++)for(int x=0;x<ArtistWidth;x++)m.Bits[y*ArtistWidth+x]=letters[ArtistLeft+x,top+y];return m;}

 // ── 읽기의 진행(CollectionScan)이 보는 것. 자리와 문턱은 실제 게임에서 받아 둔 화면(멈춘 화면 77장)으로 정했다 ──
 // HIGH SCORE 표의 머리글(금색 글자 'HIGH SCORE'). 곡이 바뀌어도 그대로다: 이 화면이 MUSIC DATA 인지 볼 때 쓴다. 받아 둔 화면에서 다른 화소는 가장 많을 때 31 이었다(바탕의 빛 번짐)
 public const int HeaderX=620,HeaderY=457,HeaderWidth=96,HeaderHeight=14,HeaderNear=120;
 static readonly Mask Header=Mask.Unpack("AAAAAAAAAAAAAAAAAAABAAABACAEAAAAYGcP5gYH4fw/h/D+YGccZgYMQ4xxxzjgYGc4BgYMBwDg5hjAYGcwBgYOBgDAZhjAf+cwB/4PhgDAZjj8f+cw5/4DxgDAZ/D8cGcw5gYA5gDAZ+DgYGcwZgYAZgDg5jDAYGc4ZgYIZwhhxjjAYGcf5gYPw/x/hhj+YGcHxgYHgPAfBhz+AAAAAAAAAAAAAAAA",HeaderWidth,HeaderHeight);
 public static bool IsMusicData(CollectionFrame f)=>GoldDiffer(f,HeaderX,HeaderY,Header)<=HeaderNear;
 // 오른쪽 목록 위의 필터('Space ALL | 다음 필터'). 고른 필터가 맨 앞에 금색 글자로 보이고 다음 필터는 흐리다. ALL 이 아닌 필터로 읽으면 그 필터의 곡만 읽혀
 // 나머지 곡의 기록이 '이번 목록에 없는 곡'으로 셈해진다. 그림은 ALL 을 고른 실제 화면 319장(창 모드·전체 화면·큰 16:9 를 줄인 것)의
 // 다수결로 만들었다. 그 화면들에서 다른 화소는 가장 많을 때 33 이었다. ALL 의 금색 글자는 299 화소라 금색 글자가 없거나 자리·모양이 다르면 그보다 훨씬 많이 다르다
 public const int FilterX=1522,FilterY=302,FilterWidth=90,FilterHeight=26,FilterNear=100;
 internal static readonly Mask AllFilter=Mask.Unpack("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAHgPAeAAAAAAAAAAPwPAeAAAAAAAAAAPwPAeAAAAAAAAAAP4PAeAAAAAAAAAAf4PAeAAAAAAAAAAe4PAeAAAAAAAAAAc8PAeAAAAAAAAAA88PAeAAAAAAAAAA8ePAeAAAAAAAAAB8+PAeAAAAAAAAAB/+PAeAAAAAAAAAB//PAeAAAAAAAAAD//PAeAAAAAAAAADwPP/f+AAAAAAAAHwHv/f+AAAAAAAAHgHv/f+AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",FilterWidth,FilterHeight);
 /// <summary>고른 필터가 ALL 인가(MUSIC DATA 화면에서만 뜻이 있다)</summary>
 public static bool IsAll(CollectionFrame f)=>FilterDiffer(f)<=FilterNear;
 internal static int FilterDiffer(CollectionFrame f)=>GoldDiffer(f,FilterX,FilterY,AllFilter);
 /// <summary>그 자리의 금색 글자(빨강이 140 넘고 파랑보다 70 넘게 큰 화소)가 그림과 다른 화소의 수</summary>
 static int GoldDiffer(CollectionFrame f,int x0,int y0,Mask want)
 {
  int differ=0;
  for(int y=0;y<want.Height;y++)for(int x=0;x<want.Width;x++){int i=((y0+y)*CollectionFrame.Width+x0+x)*4;bool gold=f.Bytes[i+2]>140&&f.Bytes[i+2]-f.Bytes[i]>70;if(gold!=want[x,y])differ++;}
  return differ;
 }
 // 움직였는지 견줄 때 쓰는 글자 그림. 화면 바탕에는 노란 빛 번짐이 천천히 지나가고(화소 값이 70 까지 오른다), 밝기 문턱으로 고른 글자 그림은 그때마다 가장자리가 달라진다
 // (같은 곡의 멈춘 화면끼리 제목 120·목록 370 화소까지 달랐다). 그래서 화소가 둘레의 바탕에서 흰색 쪽으로 반 넘게 갔는지로 고른다: 같은 화면끼리 제목 11·표 7·목록 46 이하, 이웃한 다른 곡은 제목 186 이상·한 줄 옮겨진 목록은 3,400 이상이었다.
 // 실제 게임의 이어진 캡처에서는 어두운 그림자가 목록의 한 줄을 지나갈 때 목록이 170 까지 달랐다(제목 15·표 10 이하).
 // 읽는 데는 쓰지 않는다(읽기는 위의 밝기 문턱 그림으로 한다)
 public const int SignMargin=8,SignOpen=9;
 public static Mask TitleSign(CollectionFrame f)=>Sign(f,TitleX,TitleY,TitleWidth,TitleHeight);
 /// <summary>HIGH SCORE 표의 16칸을 한 장으로.</summary>
 public static Mask TableSign(CollectionFrame f)=>Sign(f,Columns[0],ScoreRows[0],Columns[3]+CellLeft+CellWidth-Columns[0],ScoreRows[3]+CellHeight-ScoreRows[0]);
 public static Mask ListSign(CollectionFrame f)=>Sign(f,ListX,ListY,ListWidth,ListHeight);
 static Mask Sign(CollectionFrame f,int x0,int y0,int width,int height)
 {
  int m=SignMargin,w=width+2*m,h=height+2*m;var low=new int[w*h];
  for(int y=0;y<h;y++)for(int x=0;x<w;x++)low[y*w+x]=f.Low(x0-m+x,y0-m+y);
  var back=Opened(low,w,h,SignOpen);var mask=new Mask(width,height);
  for(int y=0;y<height;y++)for(int x=0;x<width;x++){int i=(y+m)*w+x+m;mask.Bits[y*width+x]=2*(low[i]-back[i])>255-back[i];}
  return mask;
 }
 // 고른 줄의 띠: 목록의 x BarLeft~BarRight, 높이 BarHeight. 띠의 그림은 곡마다 다르고 바탕에는 빛 번짐이 지나가므로 색으로 찾지 않는다:
 // 띠의 위·아래 가장자리는 가로로 길게 이어진 경계이고(빛 번짐은 매끄러워 경계가 되지 않는다) 두 경계가 BarHeight 만큼 떨어져 있다.
 // 띠는 늘 맨 아래에 있지 않다(위로 갔다 오면 가운데에도 선다). 줄 사이는 RowPitch.
 public const int BarLeft=76,BarRight=360,BarHeight=60,RowPitch=62,BarEdge=10,BarRows=630;
 // 두 경계의 몫의 평균이 BarNeed 이상이고 약한 쪽도 BarWeak 이상이어야 한다. 어두운 띠의 아랫변이 바탕의 어두운 그림자와 겹치면 그 변의 반쯤만 경계로 남는다
 // (실제 게임의 한 화면에서 윗변 0.87·아랫변 0.52). 멈춘 실제 화면 81장에서 이 규칙에 드는 자리는 화면마다 띠 하나뿐이었다
 public const double BarNeed=0.6,BarWeak=0.45;
 /// <summary>고른 줄의 띠의 첫 줄(목록 그림의 y). 찾지 못하면 -1(목록이 비었거나 움직이는 중이다).</summary>
 public static int Bar(CollectionFrame f)
 {
  var edge=new double[BarRows-1];   // edge[i]: 줄 i 와 i+1 사이에서 색이 급하게 바뀐 화소의 몫
  for(int i=0;i<edge.Length;i++){int n=0;for(int x=BarLeft;x<BarRight;x++)if(Jump(f,ListX+x,ListY+i,ListX+x,ListY+i+1)>BarEdge)n++;edge[i]=(double)n/(BarRight-BarLeft);}
  int top=-1;double best=0;
  for(int i=0;i+BarHeight<edge.Length;i++){double a=edge[i],b=edge[i+BarHeight],s=(a+b)/2;if(Math.Min(a,b)>=BarWeak&&s>=BarNeed&&s>best){best=s;top=i+1;}}
  return top;
 }
 // 띠 바로 아래·위의 줄에 곡이 있는가: 그 자리에서 가로로 급하게 바뀌는 화소를 센다(재킷과 글자). 받아 둔 화면에서 곡이 없으면 0, 있으면 아래 149 이상(흐리게 보이는 다음 줄)·위 667 이상이었다
 public const int RowQuiet=40;const int RowLeft=10,RowRight=361,RowLook=24,RowJump=8;
 /// <summary>띠 아래의 줄에서 급하게 바뀌는 화소 수. RowQuiet 아래면 줄이 없다(마지막 곡).</summary>
 public static int RowBelow(CollectionFrame f,int bar)=>RowInk(f,ListY+bar+BarHeight+4);
 /// <summary>띠 위의 줄에서 급하게 바뀌는 화소 수. RowQuiet 아래면 줄이 없다(첫 곡). 볼 자리가 목록 위로 나가면 목록 안의 부분만 본다(그 부분이 RowLook 의 절반보다 낮으면 -1).
 /// 첫 곡의 띠는 목록을 처음 열었을 때 24, 목록을 굴린 뒤에는 44 에 선다(실제 게임). 24 이면 띠 위에 18줄만 남는데, 윗곡이 있다면 그 줄의 아래쪽(재킷과 아티스트)이 거기 보인다:
 /// 멈춘 실제 화면 76장에서 그 자리의 값은 윗곡이 있을 때 554 이상, 첫 곡에서 0 이었다</summary>
 public static int RowAbove(CollectionFrame f,int bar){int top=Math.Max(0,bar-RowLook-6),height=bar-6-top;return height<RowLook/2?-1:RowInk(f,ListY+top,height);}
 static int RowInk(CollectionFrame f,int y0,int height=RowLook){int n=0;for(int y=y0;y<y0+height;y++)for(int x=ListX+RowLeft;x+2<ListX+RowRight;x++)if(Jump(f,x,y,x+2,y)>RowJump)n++;return n;}
 static int Jump(CollectionFrame f,int x0,int y0,int x1,int y1){int a=(y0*CollectionFrame.Width+x0)*4,b=(y1*CollectionFrame.Width+x1)*4;return Math.Max(Math.Abs(f.Bytes[a]-f.Bytes[b]),Math.Max(Math.Abs(f.Bytes[a+1]-f.Bytes[b+1]),Math.Abs(f.Bytes[a+2]-f.Bytes[b+2])));}
 // 띠를 기준으로 한 아티스트 줄의 자리: 바로 윗줄은 띠의 첫 줄에서 ArtistAbove, 바로 아랫줄은 ArtistBelow(줄 하나는 RowPitch, 줄 안에서 아티스트 글은 31 아래)
 public const int ArtistAbove=-31,ArtistBelow=93;

 /// <summary>k×k 보다 가는 밝은 것(글자)을 지운 바탕. 가장 어두운 값으로 좁힌 뒤 가장 밝은 값으로 다시 넓힌다(열림 연산). 그림 밖은 셈에 넣지 않는다.</summary>
 static int[] Opened(int[] values,int width,int height,int k)=>Slide(Slide(values,width,height,k,true),width,height,k,false);
 static int[] Slide(int[] values,int width,int height,int k,bool min)
 {
  // 줄마다 따로 셈한다(화면을 받을 때마다 부른다: 나눠 동시에 해 기다림을 줄인다. 결과는 같다)
  int r=k/2;var rows=new int[values.Length];var result=new int[values.Length];
  Parallel.For(0,height,y=>{for(int x=0;x<width;x++){int v=values[y*width+x];for(int d=-r;d<=r;d++){int xx=x+d;if(xx<0||xx>=width)continue;int o=values[y*width+xx];if(min?o<v:o>v)v=o;}rows[y*width+x]=v;}});
  Parallel.For(0,height,y=>{for(int x=0;x<width;x++){int v=rows[y*width+x];for(int d=-r;d<=r;d++){int yy=y+d;if(yy<0||yy>=height)continue;int o=rows[yy*width+x];if(min?o<v:o>v)v=o;}result[y*width+x]=v;}});
  return result;
 }
}
