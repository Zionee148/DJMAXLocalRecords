using System.Drawing.Drawing2D;
using System.Text.RegularExpressions;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
namespace LadderRecords;
enum RateSource { Unknown, Hud, Comparison }
record ScreenReading(string? Judge,decimal? Rate,string Detail,RateSource Source=RateSource.Unknown,long CapturedAt=0);
[System.Runtime.Versioning.SupportedOSPlatform("windows10.0.19041")]
class Recognizer:IDisposable
{
 readonly OcrEngine? engine=OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US"))??OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("ko-KR"));
 RateSource source;
 internal RateSource CurrentSource=>source;readonly Lazy<System.Reflection.MethodInfo> comparisonRate=new(()=>CompatibilityLibrary.GetType("DjmaxGoal.RateReader").GetMethod("Read",System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static)!);
 readonly System.Collections.Concurrent.ConcurrentDictionary<int,Rectangle> hudAreas=new(),comparisonAreas=new();
 readonly HudRateDigits? digits=File.Exists(Path.Combine(AppContext.BaseDirectory,"data/hud-rate-digits.json"))?new(Path.Combine(AppContext.BaseDirectory,"data/hud-rate-digits.json")):null;
 double judgeAt;string? cachedJudge,cachedMode;int cachedGear=-1;
 internal Rectangle? HudArea(int gear)=>hudAreas.TryGetValue(gear,out var area)?area:null;
public string Detail{get;private set;}="";
 public Recognizer(Catalog catalog){}
 public void ResetContext(){hudAreas.Clear();comparisonAreas.Clear();judgeAt=0;cachedJudge=cachedMode=null;cachedGear=-1;}
 record Word(string Text,double X,double Y,double Width,double Height);
 async Task<List<Word>> Words(Bitmap frame,Rectangle area,int scale,bool binary=false){
  if(engine==null)throw new Exception("Windows 글자 인식(OCR)을 쓸 수 없습니다. Windows 설정의 언어에 영어(미국)를 추가해 주세요.");area=Rectangle.Intersect(area,new(0,0,frame.Width,frame.Height));if(area.Width<1||area.Height<1)return new();using var image=new Bitmap(area.Width*scale+24,area.Height*scale+24);using(var g=Graphics.FromImage(image)){g.Clear(binary?Color.White:Color.Black);g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.DrawImage(frame,new Rectangle(12,12,area.Width*scale,area.Height*scale),area,GraphicsUnit.Pixel);}
  using var bitmap=OcrPixels.Create(image,binary);var result=await engine.RecognizeAsync(bitmap);return result.Lines.SelectMany(l=>l.Words.Select(w=>new Word(w.Text,area.X+(w.BoundingRect.X-12)/scale,area.Y+(w.BoundingRect.Y-12)/scale,w.BoundingRect.Width/scale,w.BoundingRect.Height/scale))).ToList();
 }
 static string Letters(string s)=>Regex.Replace(s.ToUpperInvariant(),"[^A-Z]","");
 internal static decimal? ParseRate(string s){s=s.Replace('O','0').Replace('o','0').Replace('I','1').Replace('l','1').Replace(',','.');var m=Regex.Match(s,@"(?<![\d.])((?:100|[1-9]\d|\d)\.\d{2})\s*%");return m.Success&&decimal.TryParse(m.Groups[1].Value,System.Globalization.NumberStyles.AllowDecimalPoint,System.Globalization.CultureInfo.InvariantCulture,out var v)&&v<=100?v:null;}
 static (decimal Value,Rectangle Area)? FindRate(List<Word> words){
  foreach(var label in words){
   if(Letters(label.Text)!="RATE")continue;
   var right=words.Where(w=>w.X>=label.X+label.Width-2&&w.X<label.X+230&&Math.Abs((w.Y+w.Height/2)-(label.Y+label.Height/2))<Math.Max(9,label.Height*1.3)).OrderBy(w=>w.X).ToArray();
   var value=ParseRate(string.Join("",right.Select(w=>w.Text)));
   if(value==null||right.Length==0)continue;
   int count=value>=100?3:value>=10?2:1;
   int decimalX=(int)Math.Round(right[0].X)+count*9;
   return(value.Value,new Rectangle(decimalX-28,(int)Math.Round(right[0].Y)-3,78,24));
  }return null;
 }
 async Task<decimal?> ReadHudArea(Bitmap frame,Rectangle area)
 {
  area=Rectangle.Intersect(area,new(0,0,frame.Width,frame.Height));if(area.Width<60||area.Height<20)return null;
  using var patch=frame.Clone(area,System.Drawing.Imaging.PixelFormat.Format24bppRgb);
  PixelPatch.Stretch(patch);
  var text=string.Join("",(await Words(patch,new(0,0,patch.Width,patch.Height),6)).Select(w=>w.Text));
  var match=Regex.Match(text.Replace('O','0').Replace('o','0').Replace('I','1').Replace('l','1').Replace(',','.'),@"(?<![\d.])((?:100|[1-9]\d|\d)\.\d{2})(?!\d)");
  decimal? ocr=match.Success?ParseRate(match.Groups[1].Value+"% "):null;
  int dx=area.X+28,top=area.Y+3;
  // A surviving prefix must never be silently discarded as a shorter integer.
  if(ocr is <10&&HudRateDigits.HasLeadingInk(frame,dx,top,ocr>=100?3:ocr>=10?2:1))ocr=null;
  var glyph=digits?.Read(frame,dx,top);
  if(ocr!=null&&glyph!=null&&ocr!=glyph.Value){
   var raw=string.Join("",(await Words(frame,area,4)).Select(w=>w.Text));
   if(ParseRate(raw)==ocr)return ocr;return null;
  }
  return ocr??glyph?.Value;
 }
 public async Task<decimal?> ReadRate(Bitmap frame,int gear){
  if(hudAreas.TryGetValue(gear,out var locked))return await ReadHudArea(frame,locked);
  int center=gear==1?1600:gear==2?960:320;var area=new Rectangle(center-130,0,290,1080);
  for(int top=0;top<1080;top+=250){
   var found=FindRate(await Words(frame,new(area.X,top,area.Width,Math.Min(300,1080-top)),3,false));
   if(found==null)found=FindRate(await Words(frame,new(area.X,top,area.Width,Math.Min(300,1080-top)),3,true));
   if(found!=null){var rect=found.Value.Area;var position=digits?.Locate(frame,rect.X+28,rect.Y+3,found.Value.Value);if(position!=null)rect=new(position.Value.X-28,position.Value.Y-3,78,24);hudAreas[gear]=rect;return await ReadHudArea(frame,rect);}
  }return null;
 }
 async Task<decimal?> ReadComparisonLocked(Bitmap frame,int gear){
  int panel=gear==2?80:740;
  var area=comparisonAreas.GetValueOrDefault(gear,new Rectangle(panel+(gear==1?265:24),770,150,58));
  foreach(bool binary in new[]{false,true}){
   var words=await Words(frame,area,3,binary);
   foreach(var word in words){var value=ParseRate(word.Text);if(value==null)continue;
    comparisonAreas.TryAdd(gear,new Rectangle(area.X,Math.Clamp((int)word.Y-12,765,780),area.Width,65));return value;
   }
  }
  // Keep the established reader as a fallback; its field choice remains LEFT/RIGHT/CENTER.
  var fallback=ReadComparisonRate(frame,gear);if(fallback!=null)comparisonAreas.TryAdd(gear,area);return fallback;
 }
 async Task<string> Text(Bitmap frame,Rectangle r,int scale=3)=>string.Join(" ",(await Words(frame,r,scale)).Select(w=>w.Text));
 static int Colored(Bitmap frame,Rectangle r){int count=0;for(int y=r.Top;y<r.Bottom;y++)for(int x=r.Left;x<r.Right;x++){var c=frame.GetPixel(x,y);if(c.R>110&&c.GetSaturation()>.55)count++;}return count;}
 static string Judged(string? judge)=>judge==null?"판정 확인 중":judge+"판정";
 public async Task<string?> ReadJudge(Bitmap frame,string mode,int gear){
  source=RateSource.Unknown;
  if(mode=="FREESTYLE"){source=RateSource.Hud;Detail="프리스타일 · 노말판정";return "노말";}
  int panel=gear==2?80:740,red=0;for(int y=105;y<150;y+=3)for(int x=panel+10;x<panel+430;x+=3){var c=frame.GetPixel(x,y);if(c.R>140&&c.R>c.G*1.8&&c.R>c.B*1.3)red++;}
  if(red>=1200){
   string round=Letters(await Text(frame,new(panel+100,100,250,60),2));
   if(round.Contains("ROUND")){
    source=RateSource.Comparison;
    string badge=Letters(await Text(frame,new(panel+120,665,200,35),3));
    string? judge=badge.Contains("MAX")?"맥스":badge.Contains("HARD")?"하드":Colored(frame,new(panel+130,673,180,17))<80?"노말":null;
    Detail="본매치/버서스 비교 패널 · "+Judged(judge);   // 읽은 영문 글자가 아니라 판단한 판정을 적는다(상태 줄에 보인다)
    return judge;
   }
  }
  if(mode=="LADDERMATCH"){
   int x=gear==1?20:1660;
   string widget=Letters(await Text(frame,new(x,280,240,190),2));
   if(widget.Contains("LADDERMATCH")&&(widget.Contains("SEARCHING")||widget.Contains("RECORDSAVE"))){
    source=RateSource.Hud;
    int badgeColor=Colored(frame,new(x+10,405,215,40));
    string? judge=widget.Contains("HARD")||widget.Contains("ARDJUDGEMENT")&&badgeColor>=60?"하드":badgeColor<60?"노말":null;
    Detail="래더 연습모드 · "+Judged(judge);
    return judge;
   }
   Detail="래더 연습모드 표시 확인 중";
   return null;
  }
  if(red>400){Detail="비교 패널 확인 중";return null;}
  if(mode=="OPENMATCH"){source=RateSource.Hud;Detail="오픈매치 단일 기어 · 노말판정";return "노말";}
  Detail="모드 확인 중";return null;
 }
 public decimal? ReadComparisonRate(Bitmap frame,int gear){try{var value=(decimal?)comparisonRate.Value.Invoke(null,new object[]{frame,gear==2,gear==1});return value>=0&&value<=100?value:null;}catch{return null;}}
 public async Task<ScreenReading> Read(Bitmap b,string mode,int gear,bool scoreAreaReady=false){
  double now=System.Diagnostics.Stopwatch.GetTimestamp()/(double)System.Diagnostics.Stopwatch.Frequency;
  string? judge;
  if(cachedJudge!=null&&cachedMode==mode&&cachedGear==gear&&now-judgeAt<.4)judge=cachedJudge;
  else{judge=await ReadJudge(b,mode,gear);cachedJudge=judge;cachedMode=mode;cachedGear=gear;judgeAt=now;}
  string detail=Detail;
  bool comparison=source==RateSource.Comparison;
  if(judge==null&&!comparison){
   if(hudAreas.ContainsKey(gear))return new(null,await ReadRate(b,gear),detail+" · RATE 읽는 중",RateSource.Hud);
   return new(null,null,detail,source);
  }
  var rate=comparison?await ReadComparisonLocked(b,gear):await ReadRate(b,gear);
  if(mode=="OPENMATCH"&&source==RateSource.Hud&&rate==null&&!scoreAreaReady){judge=null;source=RateSource.Unknown;}
  return new(judge,rate,detail+(rate==null?(comparison?" · 비교 패널 RATE 읽는 중":" · RATE 읽는 중"):""),source);
 }

 public void Dispose(){}
}
