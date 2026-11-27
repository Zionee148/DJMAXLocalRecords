using System.Diagnostics;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
namespace LadderRecords;

// 창이 뜨거나 바뀌는 순간의 화면을 빠르게 이어 찍는다: --lag-check <dark|light>
// Windows 가 그린 기본 모양이 잠깐 먼저 보이는지(프로그램이 그린 모양이 조금 뒤에 보이는지) 확인하려고, 창이 뜨거나 바뀌는 순간을 연속 촬영한다.
// 장면마다 일을 시작한 순간부터 0.4초쯤 그 자리를 이어 찍어 lag-<장면>-<몇째>-<ms>.png 로 남기고, lag-check.txt 에 장면마다 "처음과 다른 그림이 마지막으로 나온 때"를 적는다.
// 실제 화면을 찍는다(창이 화면 맨 위에 뜨고, 단추에 올리는 장면에서는 마우스가 잠깐 그 단추로 옮겨진다). 개인 기록을 쓰지 않는 폴더에서만 돌린다(포트 18777)
[System.Runtime.Versioning.SupportedOSPlatform("windows10.0.19041")]
static class LagChecks
{
 [DllImport("user32.dll")]static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")]static extern bool GetCursorPos(out Point point);
 public static void Run(Catalog catalog,string mode)
 {
  Theme.Dark=mode!="light";var lines=new List<string>{$"mode={mode} scale={Theme.Scale} system={Theme.SystemScale}"};
  string options=Path.Combine(AppContext.BaseDirectory,"lag-check-preferences.json");new UserOptions(false,false,Theme.Dark).Save(options);
  var hidden=BindingFlags.Instance|BindingFlags.NonPublic;
  // 한 장면: act 를 하고 그 뒤 area 를 이어 찍는다. 찍은 그림마다 바로 앞 그림과 다른지 보고, 마지막으로 달라진 때를 적는다
  void Scene(string name,Func<Rectangle> area,Action act,int milliseconds=420)
  {
   // 찍는 것은 다른 스레드가 한다: 콤보 목록을 펴는 것처럼 화면 스레드가 애니메이션이 끝날 때까지 돌아오지 않는 일도 그동안의 화면이 찍힌다
   var frames=new List<(double At,Bitmap Shot)>();Rectangle where=Rectangle.Empty;bool done=false;var clock=Stopwatch.StartNew();
   var camera=new Thread(()=>
   {
    while(!Volatile.Read(ref done))
    {
     var spot=where;if(spot.Width<=0||spot.Height<=0){Thread.Sleep(1);continue;}
     var shot=new Bitmap(spot.Width,spot.Height,PixelFormat.Format24bppRgb);double at=clock.Elapsed.TotalMilliseconds;
     try{using var g=Graphics.FromImage(shot);g.CopyFromScreen(spot.Location,Point.Empty,spot.Size);}catch{shot.Dispose();continue;}
     lock(frames)frames.Add((at,shot));
    }
   }){IsBackground=true};
   where=area();camera.Start();clock.Restart();act();
   while(clock.Elapsed.TotalMilliseconds<milliseconds){Application.DoEvents();if(where.IsEmpty)where=area();Thread.Sleep(1);}
   Volatile.Write(ref done,true);camera.Join();
   double last=0;int changes=0;string previous="";var kept=new List<int>();
   for(int i=0;i<frames.Count;i++)
   {
    string sum=Sum(frames[i].Shot);if(sum!=previous){if(i>0){last=frames[i].At;changes++;}kept.Add(i);previous=sum;}
   }
   foreach(int i in kept)frames[i].Shot.Save(Path.Combine(AppContext.BaseDirectory,$"lag-{name}-{kept.IndexOf(i):00}-{frames[i].At:000}ms-{mode}.png"),ImageFormat.Png);
   // 밝은 화소(세 색이 모두 232 이상)의 몫. 다크에서 프로그램이 그린 모습에는 글자 말고는 밝은 것이 없다: 처음과 끝보다 이 몫이 눈에 띄게 큰 그림이 "Windows 의 하얀 바탕이 먼저 보인" 순간이다
   var bright=frames.Select(frame=>Bright(frame.Shot)).ToList();double rest=bright.Count==0?0:Math.Max(bright[0],bright[^1]);
   var flash=Enumerable.Range(0,frames.Count).Where(i=>bright[i]>rest+.03).ToList();
   lines.Add($"{name}: 그림 {frames.Count}장({frames.Count*1000.0/milliseconds:F0}장/초), 서로 다른 모습 {kept.Count}가지, 마지막으로 바뀐 때 {last:F0}ms · 바뀐 때들 [{string.Join(", ",kept.Select(i=>frames[i].At.ToString("F0")))}]");
   lines.Add(flash.Count==0?$"{name}: 밝은 바탕이 먼저 보인 그림 없음(밝은 화소의 몫 처음 {bright.FirstOrDefault():P1}, 끝 {bright.LastOrDefault():P1}, 가장 큰 값 {bright.DefaultIfEmpty().Max():P1})"
    :$"{name}: **밝은 바탕이 보인 그림 {flash.Count}장** {frames[flash[0]].At:F0}~{frames[flash[^1]].At:F0}ms(밝은 화소의 몫 가장 큰 값 {flash.Max(i=>bright[i]):P1}, 처음 {bright[0]:P1}, 끝 {bright[^1]:P1})");
   if(flash.Count>0){int worst=flash.OrderByDescending(i=>bright[i]).First();frames[worst].Shot.Save(Path.Combine(AppContext.BaseDirectory,$"lag-{name}-flash-{frames[worst].At:000}ms-{mode}.png"),ImageFormat.Png);}
   foreach(var frame in frames)frame.Shot.Dispose();
  }
  using var main=new MainForm(catalog,18777,false,options){StartPosition=FormStartPosition.Manual,Location=new Point(60,40)};
  T Part<T>(string name)=>(T)typeof(MainForm).GetField(name,hidden)!.GetValue(main)!;
  // 1. 주 화면이 뜬다
  Scene("main-open",()=>new Rectangle(60,40,(int)(1040*Theme.Scale),(int)(800*Theme.Scale)),()=>{main.Show();main.TopMost=true;},900);
  var settle=Stopwatch.StartNew();while(settle.ElapsedMilliseconds<600){Application.DoEvents();Thread.Sleep(10);}
  string folder=Path.Combine(AppContext.BaseDirectory,"lag-check-data");Directory.CreateDirectory(folder);foreach(string old in Directory.GetFiles(folder))File.Delete(old);
  var store=new RecordStore(Path.Combine(folder,"records.json"));var song=catalog.Songs.First(s=>s.Name=="Sweet On You");var chart=new Selection(song.Id,8,"SC","하드",0);store.Add(chart,998156,DateTime.UtcNow.AddDays(-1));
  // 2. 세부 창이 뜬다(수동 기록 설정: 체크 상자·콤보·단추가 다 있다)
  {
   using var dialog=new RecordDialog(catalog,store,chart){StartPosition=FormStartPosition.Manual,Location=new Point(140,120)};
   Scene("dialog-open",()=>new Rectangle(140,120,(int)(566*Theme.Scale),(int)(560*Theme.Scale)),()=>dialog.Show(main),700);
   settle.Restart();while(settle.ElapsedMilliseconds<400){Application.DoEvents();Thread.Sleep(10);}
   // 3. 그 창의 콤보 목록을 편다
   var judges=dialog.Judges;var below=dialog.RectangleToScreen(new Rectangle(dialog.PointToClient(judges.PointToScreen(Point.Empty)),new Size(judges.Width,judges.Height+Theme.S(120))));
   Scene("combo-drop",()=>below,()=>judges.DroppedDown=true);
   judges.DroppedDown=false;settle.Restart();while(settle.ElapsedMilliseconds<300){Application.DoEvents();Thread.Sleep(10);}
   // 4. 단추에 마우스를 올린다(마우스를 잠깐 옮겼다가 제자리로 돌린다)
   GetCursorPos(out var home);
   try
   {
    // 콤보(목록을 펴는 단추)에 올린다
    var hoverCombo=dialog.Patterns;var comboSpot=hoverCombo.RectangleToScreen(hoverCombo.ClientRectangle);
    Scene("combo-hover",()=>Rectangle.Inflate(comboSpot,4,4),()=>SetCursorPos(comboSpot.Left+comboSpot.Width/2,comboSpot.Top+comboSpot.Height/2));
    SetCursorPos(home.X,home.Y);settle.Restart();while(settle.ElapsedMilliseconds<300){Application.DoEvents();Thread.Sleep(10);}
    Scene("combo-leave",()=>Rectangle.Inflate(comboSpot,4,4),()=>{SetCursorPos(comboSpot.Left+comboSpot.Width/2,comboSpot.Top+comboSpot.Height/2);var wait=Stopwatch.StartNew();while(wait.ElapsedMilliseconds<150){Application.DoEvents();Thread.Sleep(5);}SetCursorPos(home.X,home.Y);},500);
    var button=dialog.SaveButton.Parent!.Controls.OfType<Button>().First(b=>b.Text=="취소");var spot=button.RectangleToScreen(button.ClientRectangle);
    Scene("button-hover",()=>Rectangle.Inflate(spot,6,6),()=>SetCursorPos(spot.Left+spot.Width/2,spot.Top+spot.Height/2));
    SetCursorPos(home.X,home.Y);settle.Restart();while(settle.ElapsedMilliseconds<250){Application.DoEvents();Thread.Sleep(10);}
    // 5. 체크 상자에 마우스를 올린다
    var check=dialog.UseNow;var over=check.RectangleToScreen(check.ClientRectangle);
    Scene("check-hover",()=>Rectangle.Inflate(over,6,6),()=>SetCursorPos(over.Left+over.Width/2,over.Top+over.Height/2));
    // 6. 체크 상자를 누른다(시각 칸이 나타난다)
    Scene("check-click",()=>dialog.Bounds,()=>check.Checked=!check.Checked);
   }
   finally{SetCursorPos(home.X,home.Y);}
   dialog.Close();
  }
  // 기록 목록 창이 뜬다(표가 있다)
  {
   using var list=new RecordsDialog(catalog,store){StartPosition=FormStartPosition.Manual,Location=new Point(120,100)};list.PrepareForShow();
   Scene("records-open",()=>new Rectangle(120,100,(int)(900*Theme.Scale),(int)(560*Theme.Scale)),()=>list.Show(main),800);
   list.Close();
  }
  // 7. 주 화면의 콤보 목록(기어 위치)
  {
   var gear=Part<ThemedCombo>("gear");var below=new Rectangle(gear.PointToScreen(Point.Empty),new Size(gear.Width,gear.Height+Theme.S(110)));
   Scene("main-combo",()=>below,()=>gear.DroppedDown=true);gear.DroppedDown=false;
  }
  File.WriteAllLines(Path.Combine(AppContext.BaseDirectory,$"lag-check-{mode}.txt"),lines);
 }
 static double Bright(Bitmap shot)
 {
  var data=shot.LockBits(new Rectangle(Point.Empty,shot.Size),ImageLockMode.ReadOnly,PixelFormat.Format24bppRgb);
  try
  {
   var bytes=new byte[data.Stride*data.Height];Marshal.Copy(data.Scan0,bytes,0,bytes.Length);long count=0;
   for(int y=0;y<data.Height;y++)for(int x=0,at=y*data.Stride;x<data.Width;x++,at+=3)if(bytes[at]>=232&&bytes[at+1]>=232&&bytes[at+2]>=232)count++;
   return count/(double)(data.Width*data.Height);
  }
  finally{shot.UnlockBits(data);}
 }
 static string Sum(Bitmap shot)
 {
  var data=shot.LockBits(new Rectangle(Point.Empty,shot.Size),ImageLockMode.ReadOnly,PixelFormat.Format24bppRgb);
  try{var bytes=new byte[data.Stride*data.Height];Marshal.Copy(data.Scan0,bytes,0,bytes.Length);return Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(bytes));}
  finally{shot.UnlockBits(data);}
 }
}
