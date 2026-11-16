using System.Numerics;
namespace LadderRecords;
record AudioDecision(string? SongId,double Score,double Margin);
static class AudioIdentity
{
 public const int Rate=11025,Size=2048,Hop=512,Bands=48,Dimensions=96;
 static readonly double[] Window=Enumerable.Range(0,Size).Select(i=>.5-.5*Math.Cos(2*Math.PI*i/(Size-1))).ToArray();
 static readonly int[][] Bins=Enumerable.Range(0,Bands).Select(b=>Enumerable.Range(0,Size/2+1).Where(k=>k*(double)Rate/Size>=80*Math.Pow(5000d/80,b/(double)Bands)&&k*(double)Rate/Size<80*Math.Pow(5000d/80,(b+1d)/Bands)).ToArray()).ToArray();
 public static float[][] Features(float[] samples){
  if(samples.Length<Rate*6||samples.Any(v=>!float.IsFinite(v)))throw new InvalidDataException("소리가 충분하지 않습니다.");
  double mean=samples.Average(v=>(double)v),rms=Math.Sqrt(samples.Average(v=>(double)v*v));if(rms<.0002)throw new InvalidDataException("게임 소리가 너무 작거나 무음입니다.");
  int count=(samples.Length-Size)/Hop+1;var result=new float[count][];double[] previous=new double[Bands];
  for(int frame=0;frame<count;frame++){
   var fft=new Complex[Size];for(int i=0;i<Size;i++)fft[i]=new((samples[frame*Hop+i]-mean)*Window[i],0);Transform(fft);
   double[] band=new double[Bands];for(int b=0;b<Bands;b++){double power=0;foreach(int k in Bins[b])power+=fft[k].Real*fft[k].Real+fft[k].Imaginary*fft[k].Imaginary;band[b]=Math.Log(1+power/Bins[b].Length);}
   double center=band.Average();for(int b=0;b<Bands;b++)band[b]-=center;Normalize(band);var delta=new double[Bands];if(frame>0)for(int b=0;b<Bands;b++)delta[b]=band[b]-previous[b];Normalize(delta);
   var combined=new double[Dimensions];for(int b=0;b<Bands;b++){combined[b]=band[b]*.85;combined[Bands+b]=delta[b]*.35;}Normalize(combined);result[frame]=combined.Select(v=>(float)v).ToArray();previous=band;
  }return result;
 }
 static void Normalize(double[] v){double norm=Math.Max(1e-7,Math.Sqrt(v.Sum(x=>x*x)));for(int i=0;i<v.Length;i++)v[i]/=norm;}
 static void Transform(Complex[] a){for(int i=1,j=0;i<a.Length;i++){int bit=a.Length>>1;for(; (j&bit)!=0;bit>>=1)j^=bit;j^=bit;if(i<j)(a[i],a[j])=(a[j],a[i]);}for(int n=2;n<=a.Length;n<<=1){var step=Complex.FromPolarCoordinates(1,-2*Math.PI/n);for(int i=0;i<a.Length;i+=n){var w=Complex.One;for(int j=0;j<n/2;j++){var even=a[i+j];var odd=w*a[i+j+n/2];a[i+j]=even+odd;a[i+j+n/2]=even-odd;w*=step;}}}}
 public static float[][] Load(string path){using var reader=new BinaryReader(File.OpenRead(path));if(reader.ReadInt32()!=0x3146474C)throw new InvalidDataException("소리 기준 파일 오류");int frames=reader.ReadInt32(),dims=reader.ReadInt32();if(frames<100||frames>20000||dims!=Dimensions)throw new InvalidDataException("소리 기준 크기 오류");var data=new float[frames][];for(int i=0;i<frames;i++){data[i]=new float[dims];for(int j=0;j<dims;j++){float v=reader.ReadSingle();if(!float.IsFinite(v))throw new InvalidDataException("소리 기준 값 오류");data[i][j]=v;}}return data;}
 public static double Compare(float[][] query,float[][] reference){if(query.Length>reference.Length||query.Length<100)return -1;double best=-1;for(int offset=0;offset<=reference.Length-query.Length;offset++){double total=0;for(int t=0;t<query.Length;t++){var a=query[t];var b=reference[offset+t];int k=0;float dot=0;for(;k<=Dimensions-Vector<float>.Count;k+=Vector<float>.Count)dot+=Vector.Dot(new Vector<float>(a,k),new Vector<float>(b,k));for(;k<Dimensions;k++)dot+=a[k]*b[k];total+=dot;}best=Math.Max(best,total/query.Length);}return best;}
 public static AudioDecision Decide(float[][] query,IReadOnlyDictionary<string,float[][]> references){if(references.Count<2)return new(null,0,0);var ranked=references.Select(p=>(Id:p.Key,Score:Compare(query,p.Value))).OrderByDescending(r=>r.Score).ToArray();double margin=ranked[0].Score-ranked[1].Score;return new(ranked[0].Score>=.60&&margin>=.12?ranked[0].Id:null,ranked[0].Score,margin);}
 internal static float[] ReadWav(string path){using var r=new BinaryReader(File.OpenRead(path));if(new string(r.ReadChars(4))!="RIFF")throw new Exception("PCM WAV 필요");r.ReadInt32();r.ReadBytes(4);int rate=0,channels=0,bits=0;while(r.BaseStream.Position<r.BaseStream.Length){string chunk=new(r.ReadChars(4));int size=r.ReadInt32();long end=r.BaseStream.Position+size;if(chunk=="fmt "){if(r.ReadInt16()!=1)throw new Exception("PCM 필요");channels=r.ReadInt16();rate=r.ReadInt32();r.ReadInt32();r.ReadInt16();bits=r.ReadInt16();}else if(chunk=="data"){if(rate!=Rate||channels!=1||bits!=16)throw new Exception("11025Hz mono PCM16 필요");var samples=new float[size/2];for(int i=0;i<samples.Length;i++)samples[i]=r.ReadInt16()/32768f;return samples;}r.BaseStream.Position=end+(size%2);}throw new Exception("WAV data 없음");}
}
