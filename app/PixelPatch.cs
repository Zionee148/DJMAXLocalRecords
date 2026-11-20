using System.Drawing.Imaging;
using System.Runtime.InteropServices;
namespace LadderRecords;

// 그림의 한 영역을 LockBits 로 한 번에 읽어 둔다. Bitmap.GetPixel·SetPixel 은 부를 때마다 그림을 잠갔다 푼다.
// RATE 를 읽을 때마다(0.1초) GetPixel 을 1만 번 가까이 불러 코어 하나의 3% 쯤을 썼다(잰 값. docs\기록\최적화.md).
// 읽는 값은 GetPixel 과 같다(같은지는 실제 화면 그림으로 견줘 확인했다).
sealed class PixelPatch
{
 readonly int left,top,width,imageWidth,imageHeight;readonly byte[] data;   // 한 화소에 B,G,R,A

 /// <summary>그림에서 (x0,y0)부터 (x1,y1)까지(양 끝 포함)를 읽어 둔다. 그림 밖으로 나간 자리는 가장 가까운 가장자리 화소로 본다(GetPixel 을 쓰던 코드가 좌표를 그렇게 잘랐다).</summary>
 public PixelPatch(Bitmap image,int x0,int y0,int x1,int y1)
 {
  imageWidth=image.Width;imageHeight=image.Height;
  left=Math.Clamp(x0,0,imageWidth-1);top=Math.Clamp(y0,0,imageHeight-1);
  int right=Math.Clamp(x1,0,imageWidth-1),bottom=Math.Clamp(y1,0,imageHeight-1);
  width=right-left+1;int height=bottom-top+1;data=new byte[width*height*4];
  var locked=image.LockBits(new Rectangle(left,top,width,height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
  try{for(int row=0;row<height;row++)Marshal.Copy(locked.Scan0+row*locked.Stride,data,row*width*4,width*4);}
  finally{image.UnlockBits(locked);}
 }

 /// <summary>(x,y) 화소의 R+G+B. 읽어 둔 범위 안의 좌표만 넘겨야 한다(범위 밖은 확인하지 않는다).</summary>
 public int Sum(int x,int y)
 {
  int i=((Math.Clamp(y,0,imageHeight-1)-top)*width+Math.Clamp(x,0,imageWidth-1)-left)*4;
  return data[i]+data[i+1]+data[i+2];
 }

 /// <summary>24bpp 그림을 그 자리에서 회색으로 바꾼다: 밝기 (R×3+G×6+B)/10 의 120~235 를 0~255 로 편다(RATE 조각을 OCR 에 넘기기 전의 손질).</summary>
 public static void Stretch(Bitmap patch)
 {
  var locked=patch.LockBits(new Rectangle(0,0,patch.Width,patch.Height),ImageLockMode.ReadWrite,PixelFormat.Format24bppRgb);
  try
  {
   var row=new byte[patch.Width*3];
   for(int y=0;y<patch.Height;y++)
   {
    IntPtr at=locked.Scan0+y*locked.Stride;Marshal.Copy(at,row,0,row.Length);
    for(int i=0;i<row.Length;i+=3){int v=Math.Clamp(((row[i+2]*3+row[i+1]*6+row[i])/10-120)*255/115,0,255);row[i]=row[i+1]=row[i+2]=(byte)v;}
    Marshal.Copy(row,0,at,row.Length);
   }
  }
  finally{patch.UnlockBits(locked);}
 }
}
