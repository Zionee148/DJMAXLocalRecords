using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
namespace LadderRecords;
[System.Runtime.Versioning.SupportedOSPlatform("windows10.0.19041")]
static class OcrPixels
{
 public static byte[] Read(Bitmap image,bool binary=false){int rowBytes=checked(image.Width*4);var pixels=new byte[checked(rowBytes*image.Height)];var bits=image.LockBits(new Rectangle(Point.Empty,image.Size),ImageLockMode.ReadOnly,PixelFormat.Format32bppPArgb);try{for(int y=0;y<image.Height;y++)Marshal.Copy(IntPtr.Add(bits.Scan0,y*bits.Stride),pixels,y*rowBytes,rowBytes);}finally{image.UnlockBits(bits);}if(binary)for(int offset=0;offset<pixels.Length;offset+=4){var color=Color.FromArgb(pixels[offset+2],pixels[offset+1],pixels[offset]);byte value=color.R>=130&&color.G>=130&&color.B>=130&&color.GetSaturation()<.45?(byte)0:(byte)255;pixels[offset]=pixels[offset+1]=pixels[offset+2]=value;pixels[offset+3]=255;}return pixels;}
 public static SoftwareBitmap Create(Bitmap image,bool binary=false){using var writer=new DataWriter();writer.WriteBytes(Read(image,binary));return SoftwareBitmap.CreateCopyFromBuffer(writer.DetachBuffer(),BitmapPixelFormat.Bgra8,image.Width,image.Height,BitmapAlphaMode.Premultiplied);}
}
