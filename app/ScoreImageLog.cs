using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
namespace LadderRecords;

// SCORE 진단 그림 (기획서 v3 기능 7절). 사용자가 켰을 때만 남긴다. 기본은 꺼져 있다.
//
//   - 등록 키를 누른 순간의 게임 화면(screen.png)과, 그 뒤 점수를 읽는 데 쓴 화면의 점수 띠(frame-번호.png)를 요청마다 폴더 하나에 둔다.
//     폴더 이름은 "날짜-시각-request-번호"다. 글자 진단 기록(score-번호.log)의 request 번호와 맞춰 볼 수 있다.
//   - diagnostics\images 아래에 합쳐서 Limit(200MB)까지 남기고, 넘으면 오래된 요청부터 지운다.
//     지우는 것은 이 클래스가 만든 폴더(이름이 위 규칙에 맞는 것) 안의 그림뿐이다. 다른 파일은 건드리지 않는다.
//   - 그림을 만드는 일은 화면 스레드를 막지 않게 뒤에서 차례로 한다. 오류가 나면 메시지를 LastError 에 저장하고 계속한다. 그림은 어디로도 보내지 않는다.
sealed class ScoreImageLog
{
 public const long DefaultLimit=200L*1024*1024;
 static readonly Regex Ours=new(@"^\d{8}-\d{6}-request-\d+$",RegexOptions.Compiled);
 readonly string folder;readonly long limit;readonly object gate=new();Task queue=Task.CompletedTask;
 public string Folder=>folder;
 public string? LastError{get;private set;}
 public ScoreImageLog(string folder,long limit=DefaultLimit){this.folder=folder;this.limit=limit;}

 public static string RequestFolder(DateTime at,long request)=>$"{at:yyyyMMdd-HHmmss}-request-{request:D4}";

 // 점수 띠 한 장(BGRA). bgra 는 넘긴 뒤 바뀌지 않아야 한다(읽기만 한다).
 public void SaveBand(string request,int index,byte[] bgra,int width,int height,int stride)=>Enqueue(request,$"frame-{index:D3}.png",()=>{
  var image=new Bitmap(width,height,PixelFormat.Format32bppRgb);
  var data=image.LockBits(new Rectangle(0,0,width,height),ImageLockMode.WriteOnly,PixelFormat.Format32bppRgb);
  try{for(int y=0;y<height;y++)Marshal.Copy(bgra,y*stride,data.Scan0+y*data.Stride,width*4);}finally{image.UnlockBits(data);}
  return image;
 });
 // 등록 키를 누른 순간의 게임 화면. 넘겨받은 그림은 여기서 치운다.
 public void SaveScreen(string request,Bitmap screen)=>Enqueue(request,"screen.png",()=>screen);

 void Enqueue(string request,string name,Func<Bitmap> make)
 {
  lock(gate)queue=queue.ContinueWith(_=>{
   try{
    string target=Path.Combine(folder,request);Directory.CreateDirectory(target);
    using(var image=make())image.Save(Path.Combine(target,name),ImageFormat.Png);
    Trim(request);LastError=null;
   }catch(Exception e){LastError=e.Message;}
  },TaskScheduler.Default);
 }
 // 합계가 한도를 넘으면 오래된 요청부터 지운다(지금 쓰는 요청은 남긴다).
 void Trim(string current)
 {
  var requests=new DirectoryInfo(folder).EnumerateDirectories().Where(d=>Ours.IsMatch(d.Name)).OrderBy(d=>d.Name,StringComparer.Ordinal)
   .Select(d=>(Directory:d,Files:d.EnumerateFiles("*.png").Where(f=>f.Name=="screen.png"||f.Name.StartsWith("frame-")).ToList())).ToList();
  long total=requests.Sum(r=>r.Files.Sum(f=>f.Length));
  foreach(var (directory,files) in requests){
   if(total<=limit||directory.Name==current)break;
   foreach(var file in files){total-=file.Length;file.Delete();}
   if(!directory.EnumerateFileSystemInfos().Any())directory.Delete();
  }
 }
 // 지금까지 맡긴 그림이 다 저장될 때까지 기다릴 수 있게 한다(시험용).
 public Task Flush(){lock(gate)return queue;}
}
