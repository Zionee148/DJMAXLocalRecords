using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
namespace LadderRecords;
// 개발용 검사(Debug 빌드에만 들어간다): 컬렉션 읽기의 진행(CollectionScan)을 가짜 게임과 가짜 시계로 본다. 키를 실제로 보내지 않고 게임도 필요 없다.
//
//   DJMAXLocalRecords.exe --collection-scan-check <받아 둔 화면의 폴더> <그 화면의 판독 목록 json> <첫 번호> <끝 번호> <결과 파일>
//   DJMAXLocalRecords.exe --collection-scan-stills <받아 둔 화면의 폴더> <그 화면의 판독 목록 json> <첫 번호> <끝 번호> <결과 파일>
//
// 받아 둔 화면은 --collection-capture 의 폴더, 판독 목록은 --collection-read-capture 가 결과 옆에 쓰는 json 이다(16칸이 견줄 값과 모두 같은 화면의 번호·곡·값).
// 첫 번호~끝 번호는 사용자가 첫 곡에서 마지막 곡까지 내려간 구간이다(목록의 차례를 여기서 얻는다). 둘 다 사용자의 기록이라 git 밖에 있다(이 PC 에서만 돈다).
//
// scan-check: 가짜 게임은 제목 줄과 점수표에 받아 둔 실제 화면의 조각을 놓고, 오른쪽 목록은 스스로 그린다(고른 줄의 띠, 줄마다의 재킷·제목 자리·아티스트 글).
//   목록의 실제 생김새(띠의 자리, 위아래 줄, 아티스트 줄)는 scan-stills 가 멈춘 실제 화면으로 따로 본다. 가짜 게임이 보는 것은 차례다:
//   키가 먹지 않음, 늦게 바뀜, 목록의 끝, 가려진 칸, 창 전환, 중지, 이름이 같은 곡, 앱에 없는 패턴, 허용 수.
// scan-stills: 멈춘 실제 화면마다 머리글 확인·고른 줄의 띠·위아래 줄의 유무·윗줄과 아랫줄의 아티스트를 제품의 코드로 읽어, 내려간 차례에서 얻은 답과 견준다.
[SupportedOSPlatform("windows10.0.19041")]
static class CollectionScanChecks
{
 internal sealed record FakeSong(Song Song,string TitlePath,string TablePath,Dictionary<string,string> Score)
 {
  public string Cell(int i)=>Score[$"{CollectionScreen.Buttons[i%4]}B {CollectionScreen.Patterns[i/4]}"];
 }
 const int TitleX=605,TitleY=262,TitleW=830,TitleH=72,TableX=605,TableY=455,TableW=820,TableH=237;

 /// <summary>가짜 게임. 시계는 기다린 만큼과 화면을 잡을 때마다(25ms) 간다.</summary>
 internal sealed class FakeGame:IScanWorld
 {
  public readonly List<FakeSong> Songs;readonly CollectionData data;
  public int At;int bar;double now,movedAt=-10;int movedBy,movedCapture;
  readonly List<(double When,int By)> queue=new();bool upHeld,downHeld,inactive,stop;double upNext;
  readonly Dictionary<string,byte[]> pieces=new();readonly Queue<string> pieceOrder=new();
  static readonly byte[] Blank=MakeBlank();
  // 고른 줄의 띠는 한 곡에 RowPitch 씩 움직이고, 목록 창의 위(BarTop)·아래(BarBottom)에 닿으면 거기에 서서 목록이 대신 밀린다(멈춘 실제 화면에서 본 자리)
  // 목록을 처음 열었을 때는 첫 곡의 띠가 24 에 선다(굴린 뒤에는 44. 실제 게임에서 본 자리): Fresh 이면 맨 위 자리를 24 로 둔다
  const int BarTop=44,BarBottom=514,FreshTop=24;int top=BarTop;
  public bool Fresh{set{top=value?FreshTop:BarTop;bar=Math.Min(top+CollectionScreen.RowPitch*At,BarBottom);}}
  // 대본(곡의 번호로 적는다)
  public HashSet<int> LostKey=new();                          // 이 곡에서 누른 ↓ 한 번은 먹지 않는다
  public Dictionary<int,double> Lag=new();                    // 이 곡에서 누른 ↓ 가 늦게 먹는다(초)
  public HashSet<int> Twice=new();                            // 이 곡에서 누른 ↓ 한 번에 두 곡이 넘어간다
  public Dictionary<int,(int Cell,double For)> Cover=new();   // 이 곡의 칸을 커서로 가린다(For 초 동안. 0 이면 계속)
  public HashSet<int> TitleCover=new(),ArtistCover=new();     // 제목 줄을 가린다 / 목록에서 이 곡의 아티스트 글을 가린다
  public Dictionary<int,int> TableOf=new();                   // 이 곡의 표 자리에 다른 곡의 표를 보인다
  public Dictionary<int,double> BarHidden=new();              // 이 곡을 고른 동안 띠를 바탕색으로 그린다(검은 띠 뒤로 배경의 그림자가 지날 때. 옮겨진 뒤 For 초 동안. 0 이면 계속)
  public double StopLater=-1;double inactiveAt;               // 앞 창이 아니게 된 뒤 이만큼 지나 중지가 눌린다(창의 '중지' 단추를 마우스로 누를 때: 누르는 순간 그 창이 앞 창이 된다)
  public int InactiveAt=-1,StopAt=-1;                         // 이 곡에 닿으면 앞 창이 아니게 된다 / 중지가 눌린다
  public bool NoHeader,NoList,NoScreen,UpDead;
  public bool NoFilter,NoSong;                                // 필터 자리에 ALL 이 없다(다른 필터) / 제목 줄을 그리지 않는다(목록도 함께 비울 때 빈 목록)
  public Dictionary<int,FakeSong> Flicker=new();               // 이 곡의 표 자리에 이 곡과 그 곡의 표를 번갈아 보인다(화면이 멈추지 않는다)
  public int FilterOffAt=-1;                                  // 이 곡부터 필터 자리에 ALL 이 없다(읽는 동안 Space 로 필터를 바꿨다)
  public Dictionary<int,double> FilterGone=new();              // 이 곡으로 옮겨진 뒤 이만큼(초) 필터 자리에 ALL 이 없다(지나가는 것)
  public int PackAt=-1;bool packed;                           // 이 곡에서 ↓ 를 누르는 순간 이 곡 하나뿐인 팩으로 필터가 바뀐다(필터 자리에 ALL 이 없고 목록에 이 곡만 남는다)
  public Dictionary<int,FakeSong> TitleFlicker=new();          // 이 곡의 제목 자리에 이 곡과 그 곡의 제목을 번갈아 보인다
  public HashSet<int> ListShake=new();                         // 이 곡을 고른 동안 목록의 줄이 위아래로 흔들린다(목록이 멈추지 않는다)
  public Dictionary<int,int> CursorPeek=new();                 // 이 곡으로 옮겨진 뒤 0.25초 동안은 커서가 다른 열의 칸들을 지나고(그 칸이 보인다), 그 뒤로는 그 칸 위에서 흔들린다
  public Dictionary<int,int> CursorEnter=new();                // 이 곡으로 옮겨진 뒤 캡처 두 장은 그 칸이 보이고, 넷은 커서가 오른쪽에서 그 칸 위로 들어오고, 그 뒤로는 그 칸 위에 멈춘다
  public Dictionary<int,(int Cell,int Clear)> CursorClearOnly=new(); // 이 곡으로 옮겨진 뒤 Clear 번째 캡처만 그 칸이 보인다. 나머지는 커서가 그 칸의 숫자 위를 오른쪽에서 미끄러져 들어와 멈춘다(늘 숫자를 가린다). 옮겨진 것을 알아채는 데 첫 캡처를 쓰므로 2 는 곡 읽기의 첫째 화면, 3 은 둘째 화면
  public Dictionary<int,int> CursorSweep=new();                // 이 곡을 고른 동안 커서가 그 칸의 줄을 좌우로 오간다(캡처 7장에 2장은 그 칸 밖)
  public HashSet<int> CursorMoving=new();                      // 이 곡을 고른 동안 커서가 점수표 위를 캡처마다 다른 칸으로 옮겨 다닌다(사용자가 커서를 움직일 때)
  // 기록
  public int Downs,Ups,UpsLater,KeysWhileInactive,Captures;public bool AnyHeld=>upHeld||downHeld;   // UpsLater: 처음 ↓ 를 보낸 뒤의 ↑

  public FakeGame(List<FakeSong> songs,CollectionData data,int start){Songs=songs;this.data=data;At=start;bar=Math.Min(BarTop+CollectionScreen.RowPitch*start,BarBottom);}
  static byte[] MakeBlank(){var b=new byte[CollectionFrame.Width*CollectionFrame.Height*4];for(int i=0;i<b.Length;i+=4){b[i]=73;b[i+1]=73;b[i+2]=76;b[i+3]=255;}return b;}

  public double Now=>now;
  public void Wait(double seconds){now+=seconds;}
  public bool Active{get{Advance();return!inactive;}}
  public bool StopAsked{get{Advance();return stop;}}
  public void Key(ScanKey key,bool down)
  {
   Advance();if(down&&inactive)KeysWhileInactive++;
   if(key==ScanKey.Down)
   {
    downHeld=down;if(!down)return;Downs++;
    if(At==PackAt||packed){packed=true;return;}
    if(LostKey.Remove(At)||NoList)return;
    queue.Add((now+(Lag.TryGetValue(At,out double lag)?lag:0.03),Twice.Contains(At)?2:1));
   }
   else
   {
    if(down&&!upHeld){Ups++;if(Downs>0)UpsLater++;if(!UpDead&&!NoList){queue.Add((now+0.03,-1));upNext=now+0.3;}}
    upHeld=down;
   }
  }
  /// <summary>지금까지 도착한 이동을 적용한다. ↑ 를 누르고 있으면 게임의 반복(초당 14곡)으로 올라간다.</summary>
  void Advance()
  {
   if(upHeld&&!UpDead&&!NoList)while(upNext<=now){queue.Add((upNext,-1));upNext+=1.0/14;}
   foreach(var move in queue.Where(m=>m.When<=now).OrderBy(m=>m.When).ToList())
   {
    queue.Remove(move);int to=Math.Clamp(At+move.By,0,Songs.Count-1);if(to==At)continue;
    bar=Math.Clamp(bar+CollectionScreen.RowPitch*(to-At),top,BarBottom);At=to;movedAt=move.When;movedBy=Math.Sign(move.By);movedCapture=Captures;
    if(At==InactiveAt){inactive=true;inactiveAt=move.When;}if(At==StopAt)stop=true;
   }
   if(inactive&&StopLater>=0&&now>=inactiveAt+StopLater)stop=true;
  }
  byte[] Piece(string path,int width,int height)
  {
   if(pieces.TryGetValue(path,out var have))return have;
   using var bitmap=new Bitmap(path);if(bitmap.Width!=width||bitmap.Height!=height)throw new InvalidDataException($"{path} 의 크기가 다릅니다.");
   var bytes=new byte[width*height*4];var bits=bitmap.LockBits(new Rectangle(0,0,width,height),System.Drawing.Imaging.ImageLockMode.ReadOnly,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
   try{for(int y=0;y<height;y++)System.Runtime.InteropServices.Marshal.Copy(bits.Scan0+y*bits.Stride,bytes,y*width*4,width*4);}finally{bitmap.UnlockBits(bits);}
   pieces[path]=bytes;pieceOrder.Enqueue(path);if(pieceOrder.Count>48)pieces.Remove(pieceOrder.Dequeue());
   return bytes;
  }
  static void Put(byte[] frame,byte[] piece,int x0,int y0,int width,int height){for(int y=0;y<height;y++)Buffer.BlockCopy(piece,y*width*4,frame,((y0+y)*CollectionFrame.Width+x0)*4,width*4);}
  static void Fill(byte[] frame,int x0,int y0,int width,int height,byte b,byte g,byte r)
  {
   for(int y=Math.Max(y0,0);y<Math.Min(y0+height,CollectionFrame.Height);y++)for(int x=Math.Max(x0,0);x<Math.Min(x0+width,CollectionFrame.Width);x++){int i=(y*CollectionFrame.Width+x)*4;frame[i]=b;frame[i+1]=g;frame[i+2]=r;}
  }

  public CollectionFrame? Capture()
  {
   now+=0.025;Advance();Captures++;if(NoScreen)return null;
   var frame=(byte[])Blank.Clone();var song=Songs[At];
   var title=song;if(TitleFlicker.TryGetValue(At,out var other2)&&Captures%2==0)title=other2;
   if(!NoSong)Put(frame,Piece(title.TitlePath,TitleW,TitleH),TitleX,TitleY,TitleW,TitleH);
   var table=Songs[TableOf.TryGetValue(At,out int other)?other:At];if(Flicker.TryGetValue(At,out var alt)&&Captures%2==0)table=alt;
   Put(frame,Piece(table.TablePath,TableW,TableH),TableX,TableY,TableW,TableH);
   bool gone=packed||FilterGone.TryGetValue(At,out double goneFor)&&now-movedAt<goneFor;
   if(!NoFilter&&!(FilterOffAt>=0&&At>=FilterOffAt)&&!gone)PaintAll(frame);
   if(TitleCover.Contains(At))Fill(frame,CollectionScreen.TitleX+20,CollectionScreen.TitleY+6,90,34,255,255,255);
   if(NoHeader)Fill(frame,CollectionScreen.HeaderX-10,CollectionScreen.HeaderY-2,CollectionScreen.HeaderWidth+20,CollectionScreen.HeaderHeight+4,30,30,30);
   if(Cover.TryGetValue(At,out var cover)&&(cover.For<=0||now-movedAt<cover.For))CollectionChecks.Cursor(frame,CollectionScreen.Columns[cover.Cell%4]+CollectionScreen.CellLeft+10,CollectionScreen.ScoreRows[cover.Cell/4]-4);
   if(CursorPeek.TryGetValue(At,out int peek))
   {
    // 커서(50×60)는 그 칸의 열과 아래 두 줄을 덮는다: 처음에는 다른 열에만 둔다
    if(now-movedAt<0.25){int column=(peek%4+1+Captures%3)%4,row=Captures%4;CollectionChecks.Cursor(frame,CollectionScreen.Columns[column]+CollectionScreen.CellLeft+10,CollectionScreen.ScoreRows[row]-4);}
    else CollectionChecks.Cursor(frame,CollectionScreen.Columns[peek%4]+CollectionScreen.CellLeft+10+Captures%3*4,CollectionScreen.ScoreRows[peek/4]-4);
   }
   if(CursorEnter.TryGetValue(At,out int enter)){int k=Captures-movedCapture;if(k>2)CollectionChecks.Cursor(frame,CollectionScreen.Columns[enter%4]+CollectionScreen.CellLeft+10+Math.Max(0,7-k)*15,CollectionScreen.ScoreRows[enter/4]-4);}
   if(CursorClearOnly.TryGetValue(At,out var only)){int k=Captures-movedCapture;if(k!=only.Clear)CollectionChecks.Cursor(frame,CollectionScreen.Columns[only.Cell%4]+CollectionScreen.CellLeft+10+Math.Max(0,6-k)*9,CollectionScreen.ScoreRows[only.Cell/4]-4);}
   if(CursorSweep.TryGetValue(At,out int sweep)){int[] offsets={-70,-40,-10,20,50,80,120};CollectionChecks.Cursor(frame,CollectionScreen.Columns[sweep%4]+CollectionScreen.CellLeft+offsets[Captures%7],CollectionScreen.ScoreRows[sweep/4]-4);}
   if(CursorMoving.Contains(At)){int c=Captures%16;CollectionChecks.Cursor(frame,CollectionScreen.Columns[c%4]+CollectionScreen.CellLeft+10,CollectionScreen.ScoreRows[c/4]-4);}
   if(!NoList)DrawList(frame);
   return new CollectionFrame(frame);
  }
  /// <summary>필터 자리의 금색 ALL(제품이 견주는 그림 그대로)</summary>
  internal static void PaintAll(byte[] frame)
  {
   var all=CollectionScreen.AllFilter;
   for(int y=0;y<all.Height;y++)for(int x=0;x<all.Width;x++)if(all[x,y]){int i=((CollectionScreen.FilterY+y)*CollectionFrame.Width+CollectionScreen.FilterX+x)*4;frame[i]=0;frame[i+1]=180;frame[i+2]=250;}
  }
  /// <summary>오른쪽 목록. 다른 줄은 고른 줄의 띠의 위아래로 RowPitch 마다 놓인다. 옮겨진 뒤 0.3초 동안은 줄들이 미끄러져 들어온다(실제 게임은 0.2~0.5초).</summary>
  void DrawList(byte[] frame)
  {
   int slide=(int)Math.Round(CollectionScreen.RowPitch*(1-Math.Min(1,(now-movedAt)/0.3)))*movedBy+(ListShake.Contains(At)?Captures%2*20:0);
   int x0=CollectionScreen.ListX,y0=CollectionScreen.ListY;
   for(int j=-9;j<=2;j++)
   {
    int i=At+j;if(j==0||i<0||i>=Songs.Count||packed)continue;
    int top=bar+CollectionScreen.RowPitch*j+slide;if(top<0||top>=CollectionScreen.BarRows)continue;   // 맨 아래에 걸친 줄도 그린다(실제 게임에서도 다음 줄의 윗부분이 보인다)
    Fill(frame,x0+10,y0+top,60,56,(byte)(120+i*37%120),(byte)(120+i*53%110),(byte)(130+i*71%100));                       // 재킷
    for(int b=0;b<10;b++)if((i>>b&1)!=0)Fill(frame,x0+80+b*12,y0+top+8,8,14,255,255,255);                             // 제목 자리(곡마다 다른 흰 무늬)
    if(!ArtistCover.Contains(i))DrawArtist(frame,Songs[i].Song.Artist,x0+CollectionScreen.ArtistLeft+2,y0+top+31);
   }
   bool hidden=BarHidden.TryGetValue(At,out double hide)&&(hide<=0||now-movedAt<hide);
   if(!hidden)Fill(frame,x0+73,y0+bar,289,CollectionScreen.BarHeight,20,20,20);                                       // 고른 줄의 띠(아티스트 글이 없다)
   for(int b=0;b<10;b++)if((At>>b&1)!=0)Fill(frame,x0+84+b*14,y0+bar+20,10,20,255,255,255);
  }
  void DrawArtist(byte[] frame,string text,int x0,int y0)
  {
   double cursor=double.NaN;int limit=CollectionScreen.ListX+CollectionScreen.ArtistLeft+CollectionScreen.ArtistWidth;
   foreach(string ch in GlyphSet.Letters(text))
   {
    var g=data.Artist.Get(ch);var p=g.Picture;
    if(double.IsNaN(cursor)){if(p.Width==0)continue;cursor=-g.Left;}
    int x=x0+(int)Math.Round(cursor,MidpointRounding.ToEven)+g.Left;
    for(int y=0;y<p.Height;y++)for(int k=0;k<p.Width;k++)if(p.Bits[y*p.Width+k]&&x+k<limit&&y0+y<CollectionFrame.Height){int i=((y0+y)*CollectionFrame.Width+x+k)*4;frame[i]=frame[i+1]=frame[i+2]=165;}
    cursor+=g.Advance;
   }
  }
 }

 internal static List<FakeSong> Load(Catalog catalog,string folder,string listPath,int first,int last)
 {
  var songs=new List<FakeSong>();
  foreach(var e in JsonSerializer.Deserialize<List<JsonElement>>(File.ReadAllText(listPath))!)
  {
   string n=e.GetProperty("n").GetString()!;int number=int.Parse(n);if(number<first||number>last)continue;
   string id=e.GetProperty("id").GetString()!;if(songs.Count>0&&songs[^1].Song.Id==id)continue;
   var score=e.GetProperty("score").EnumerateObject().ToDictionary(p=>p.Name,p=>p.Value.GetString()!);
   songs.Add(new(catalog.Songs.First(s=>s.Id==id),Path.Combine(folder,n+"-title.png"),Path.Combine(folder,n+"-table.png"),score));
  }
  return songs;
 }

 /// <summary>받아 둔 화면의 판독 목록에서 곡이 놓인 차례만 얻는다(조각 그림은 쓰지 않는다).</summary>
 internal static List<Song> Order(Catalog catalog,string listPath,int first,int last)=>Load(catalog,"",listPath,first,last).Select(s=>s.Song).ToList();

 public static void Run(Catalog catalog,string folder,string listPath,int first,int last,string reportPath)
 {
  var data=CollectionData.Load();var reader=new CollectionReader(catalog,data);var all=Load(catalog,folder,listPath,first,last);
  var notes=new List<string>();int failed=0;
  void Check(bool ok,string name){if(!ok)failed++;notes.Add((ok?"PASS: ":"FAIL: ")+name);}
  (ScanResult Result,FakeGame Game) Scan(List<FakeSong> songs,int start,int allowed,Action<FakeGame>? script=null)
  {
   var game=new FakeGame(songs,data,start);script?.Invoke(game);return(new CollectionScan(reader,game,allowed).Run(),game);
  }
  // 읽은 것이 가짜 게임의 곡들과 차례대로 맞는가: 정한 곡은 그 곡이어야 하고, 확실하다고 한 칸은 받아 둔 값과 같아야 한다
  string Wrong(ScanResult r,List<FakeSong> songs,int skip=-1)
  {
   var problems=new List<string>();int k=0;
   foreach(var item in r.Items)
   {
    if(k==skip)k++;if(k>=songs.Count){problems.Add("곡이 더 나옴");break;}
    var want=songs[k++];
    if(item.Judge.Song!=null&&item.Judge.Song.Id!=want.Song.Id)problems.Add($"{want.Song} 를 {item.Judge.Song} 로 정함");
    for(int i=0;i<16;i++)if(item.Sure[i]&&item.Shot.Cells[i].Text!=want.Cell(i))problems.Add($"{want.Song} 의 칸 {i}: '{item.Shot.Cells[i].Text}' ≠ '{want.Cell(i)}' 인데 확실하다고 함");
   }
   return problems.Count==0?"":" ["+string.Join("; ",problems.Take(5))+"]";
  }
  string Say(ScanResult r,FakeGame g)=>$"(끝 {r.End}{(r.Detail==""?"":" \""+r.Detail+"\"")}, 곡 {r.Items.Count}, 확인할 것 {r.Questions}, ↓ {g.Downs}번, ↑ {g.Ups}번, 화면 {g.Captures}장, 가짜 시계 {r.Seconds:0.0}초)";
  bool Clean(ScanResult r,List<FakeSong> songs)=>r.End==ScanEnd.Done&&r.Items.Count==songs.Count&&r.Questions==0&&r.Items.All(i=>i.Judge.Song!=null)&&Wrong(r,songs)=="";

  // 이름이 같은 곡의 쌍(붙어 나온다)과, 점수가 있는 칸을 가진 곡
  var pairs=Enumerable.Range(0,all.Count-1).Where(i=>data.TitleOf(all[i].Song)==data.TitleOf(all[i+1].Song)).ToList();
  int NumberCell(FakeSong s)=>Enumerable.Range(0,16).FirstOrDefault(i=>s.Cell(i).Length>1&&s.Cell(i).All(char.IsAsciiDigit),-1);
  string Blanks(FakeSong s)=>string.Concat(Enumerable.Range(0,16).Select(i=>s.Cell(i)==""?'.':'x'));
  Check(all.Count>=100&&pairs.Count>=1,$"받아 둔 화면에서 곡 {all.Count}개의 차례를 얻었다(이름이 같은 쌍 {pairs.Count})");
  Check(CollectionScan.Allowed(catalog)==catalog.Songs.Sum(s=>s.Charts.Count)*2/1000,$"허용 수 {CollectionScan.Allowed(catalog)} (곡 목록의 패턴 {catalog.Songs.Sum(s=>s.Charts.Count)}개의 0.2%)");
  if(all.Count<100||pairs.Count<1){File.WriteAllLines(reportPath,notes);Environment.ExitCode=2;return;}
  var part=all.GetRange(0,40);   // 짧은 대본은 앞의 40곡으로 돈다

  // 1. 전곡: 가운데에서 시작해 첫 곡으로 올라간 뒤 끝까지
  {
   var game=new FakeGame(all,data,all.Count/2);var told=new List<int>();
   var r=new CollectionScan(reader,game,24){Progress=(count,_)=>told.Add(count)}.Run();var g=game;
   Check(told.SequenceEqual(Enumerable.Range(1,all.Count)),$"곡을 읽을 때마다 읽은 곡 수를 알린다(1~{all.Count})");
   Check(Clean(r,all)&&g.Downs==all.Count+2&&g.KeysWhileInactive==0&&!g.AnyHeld,$"전곡 {all.Count}곡을 차례대로 모두 읽고 끝에서 멈춘다 {Say(r,g)}{Wrong(r,all)}");
   var named=r.Items.Where(i=>i.Artist!=null).ToList();
   Check(named.Count>=pairs.Count*2&&named.All(i=>i.Judge.Song?.Artist==i.Artist),$"이름이 같은 곡 {named.Count}개를 목록의 아티스트로 정했다");
   notes.Add($"      가운데({all.Count/2}번째)에서 첫 곡까지 ↑ {g.Ups}번(짧게 거듭 누름. 누름 {CollectionScan.FirstPress*1000:0}ms, 한 번에 {CollectionScan.FirstCycle*1000:0}ms 보다 짧지 않게)");
   notes.Add($"      곡마다 가짜 시계 {r.Seconds/Math.Max(1,r.Items.Count):0.000}초(누름 {CollectionScan.Press}초, 이동 0.03초, 목록이 멈추기까지 0.3초로 둔 가짜 게임에서)");
  }
  // 2. 짧은 목록
  foreach(int n in new[]{1,2,5,9,10})
  {
   var few=all.GetRange(0,n);var (r,g)=Scan(few,n-1,24);
   Check(Clean(r,few),$"곡이 {n}개인 목록 {Say(r,g)}{Wrong(r,few)}");
  }
  // 3. 키가 먹지 않음: 다시 보내지 않고 멈춘다
  {
   var (r,g)=Scan(part,0,24,x=>x.LostKey.Add(20));
   Check(r.End==ScanEnd.NotMoved&&r.Detail.StartsWith("아래에 곡이 있는데")&&r.Items.Count==21&&g.Downs==21&&Wrong(r,part)=="",$"21번째 곡에서 ↓ 가 먹지 않으면 다시 보내지 않고 멈춘다 {Say(r,g)}{Wrong(r,part)}");
  }
  // 4. 늦게 바뀜
  {
   var (r,g)=Scan(part,0,24,x=>{x.Lag[10]=0.5;x.Lag[11]=0.6;});
   Check(Clean(r,part),$"↓ 가 0.5~0.6초 늦게 먹어도 기다려서 읽는다 {Say(r,g)}{Wrong(r,part)}");
   (r,g)=Scan(part,0,24,x=>x.Lag[10]=1.0);
   Check(r.End==ScanEnd.NotMoved&&r.Detail.StartsWith("아래에 곡이 있는데")&&r.Items.Count==11&&g.Downs==11,$"↓ 가 {CollectionScan.MoveWait}초 안에 먹지 않으면 멈춘다 {Say(r,g)}");
  }
  // 5. 가려진 칸
  {
   var hit=Enumerable.Range(3,30).Where(i=>NumberCell(part[i])>=0).Take(2).ToList();
   var (r,g)=Scan(part,0,24,x=>{foreach(int i in hit)x.Cover[i]=(NumberCell(part[i]),0);});
   bool kept=r.End==ScanEnd.Done&&r.Items.Count==part.Count&&hit.All(i=>r.Items[i].Questions>0&&r.Items[i].Screen!=null)&&Enumerable.Range(0,part.Count).Where(i=>!hit.Contains(i)).All(i=>r.Items[i].Questions==0&&r.Items[i].Screen==null);
   Check(kept&&Wrong(r,part)=="",$"칸이 계속 가려진 두 곡은 확인할 것으로 넘기고 화면을 남기며, 틀린 값을 확실하다고 하지 않는다 {Say(r,g)} 곡마다 {string.Join("·",hit.Select(i=>r.Items.Count>i?r.Items[i].Questions:-1))}건{Wrong(r,part)}");
   (r,g)=Scan(part,0,24,x=>{foreach(int i in hit)x.Cover[i]=(NumberCell(part[i]),0.2);});
   Check(Clean(r,part),$"칸이 0.2초만 가려졌으면 다시 잡아서 읽는다 {Say(r,g)}{Wrong(r,part)}");
  }
  // 6. 허용 수
  {
   var (r,g)=Scan(part,0,5,x=>{for(int i=3;i<part.Count;i++)if(NumberCell(part[i])>=0)x.Cover[i]=(NumberCell(part[i]),0);});
   bool atOnce=r.End==ScanEnd.TooMany&&r.Questions>5&&r.Items.Count<part.Count&&r.Questions-r.Items[^1].Questions<=5;
   Check(atOnce&&Wrong(r,part)=="",$"확인할 것이 허용 수 5 를 넘는 순간 멈춘다 {Say(r,g)}{Wrong(r,part)}");
  }
  // 7. 창 전환과 중지
  {
   var (r,g)=Scan(part,0,24,x=>x.InactiveAt=15);
   Check(r.End==ScanEnd.Cancelled&&r.Detail=="창 전환"&&g.KeysWhileInactive==0&&!g.AnyHeld&&r.Items.Count<=16,$"읽는 도중 게임이 앞 창이 아니게 되면 그만두고 그 뒤로 키를 보내지 않는다 {Say(r,g)}");
   (r,g)=Scan(part,0,24,x=>{x.InactiveAt=15;x.StopLater=0.1;});
   Check(r.End==ScanEnd.Cancelled&&r.Detail=="중지"&&g.KeysWhileInactive==0&&!g.AnyHeld,$"창의 '중지' 단추를 마우스로 누르면(그 창이 먼저 앞 창이 된다) 창 전환이 아니라 중지로 둔다 {Say(r,g)}");
   (r,g)=Scan(part,0,24,x=>x.StopAt=15);
   Check(r.End==ScanEnd.Cancelled&&!g.AnyHeld&&r.Items.Count<=16&&g.Downs<=16,$"읽는 도중 중지가 눌리면 그만둔다 {Say(r,g)}");
   (r,g)=Scan(part,30,24,x=>x.InactiveAt=20);
   Check(r.End==ScanEnd.Cancelled&&g.KeysWhileInactive==0&&!g.AnyHeld&&g.Downs==0,$"첫 곡으로 올라가는 도중 앞 창이 아니게 되면 ↑ 를 그만 보내고 그만둔다 {Say(r,g)}");
  }
  // 8. 이름이 같은 곡
  {
   int p=pairs[0];var around=all.GetRange(Math.Max(0,p-12),Math.Min(all.Count,p+14)-Math.Max(0,p-12));int q=p-Math.Max(0,p-12);
   var (r,g)=Scan(around,0,24);
   Check(Clean(r,around)&&r.Items[q].Artist==around[q].Song.Artist&&r.Items[q+1].Artist==around[q+1].Song.Artist,$"'{data.TitleOf(all[p].Song)}' 의 두 곡을 아티스트로 정한다({around[q].Song.Artist} / {around[q+1].Song.Artist}) {Say(r,g)}{Wrong(r,around)}");
   (r,g)=Scan(around,0,24,x=>x.ArtistCover.Add(q));
   Check(r.End==ScanEnd.Done&&r.Items.Count==around.Count&&r.Items[q].Judge.Song==null&&r.Items[q].Questions==1&&r.Items[q].Screen!=null&&r.Items[q].ArtistScreen!=null&&r.Items[q+1].Judge.Song?.Id==around[q+1].Song.Id&&r.Questions==1&&Wrong(r,around)=="",
    $"첫째 곡의 아티스트 글이 가려지면 그 곡만 곡 선택 1건으로 넘기고 화면 둘을 남긴다 {Say(r,g)}{Wrong(r,around)}");
   var endsSecond=around.GetRange(0,q+2);(r,g)=Scan(endsSecond,0,24);
   Check(Clean(r,endsSecond)&&r.Items[^1].Artist==endsSecond[^1].Song.Artist&&g.UpsLater==1,$"마지막 곡이 이름이 같은 곡이면 한 곡 올라가 아랫줄에서 아티스트를 읽는다 {Say(r,g)}{Wrong(r,endsSecond)}");
   var endsFirst=around.GetRange(0,q+1);(r,g)=Scan(endsFirst,0,24);
   Check(Clean(r,endsFirst)&&r.Items[^1].Artist==endsFirst[^1].Song.Artist,$"쌍의 첫째 곡만 있는 목록(마지막 곡) {Say(r,g)}{Wrong(r,endsFirst)}");
   var startsSecond=around.GetRange(q+1,around.Count-q-1);(r,g)=Scan(startsSecond,0,24);
   Check(Clean(r,startsSecond)&&r.Items[0].Artist==startsSecond[0].Song.Artist,$"쌍의 둘째 곡만 있는 목록(첫 곡) {Say(r,g)}{Wrong(r,startsSecond)}");
   var alone=new List<FakeSong>{around[q]};(r,g)=Scan(alone,0,24);
   Check(r.End==ScanEnd.Done&&r.Items.Count==1&&r.Items[0].Judge.Song==null&&r.Questions==1,$"이름이 같은 곡 하나뿐인 목록은 아티스트를 볼 줄이 없어 곡 선택 1건으로 넘긴다 {Say(r,g)}");
  }
  // 9. 앱에 없는 패턴, 가려진 제목
  {
   int other=Enumerable.Range(0,part.Count).First(i=>Blanks(part[i])!=Blanks(part[12]));
   var (r,g)=Scan(part,0,24,x=>x.TableOf[12]=other);
   Check(r.End==ScanEnd.UnknownPattern&&r.Items.Count==12,$"제목은 또렷한데 빈칸의 자리가 그 곡의 패턴과 다르면 멈춘다 {Say(r,g)}");
   (r,g)=Scan(part,0,24,x=>x.TitleCover.Add(12));
   Check(r.End==ScanEnd.Done&&r.Items.Count==part.Count&&r.Items[12].Judge.Song==null&&r.Items[12].Judge.TitleUnclear&&r.Items[12].Questions>=1&&r.Items[12].Screen!=null&&r.Questions==r.Items[12].Questions&&Wrong(r,part)=="",
    $"제목이 가려진 곡은 곡 선택을 확인할 것으로 넘기고 계속 읽는다 {Say(r,g)}{Wrong(r,part)}");
  }
  // 10. 시작할 수 없는 화면
  {
   var (r,g)=Scan(part,5,24,x=>x.NoHeader=true);
   Check(r.End==ScanEnd.WrongScreen&&g.Downs+g.Ups==0,$"MUSIC DATA 화면이 아니면 키를 보내지 않고 그만둔다 {Say(r,g)}");
   (r,g)=Scan(part,5,24,x=>x.NoScreen=true);
   Check(r.End==ScanEnd.NoScreen&&g.Downs+g.Ups==0,$"화면을 받지 못하면 키를 보내지 않고 그만둔다 {Say(r,g)}");
   (r,g)=Scan(part,5,24,x=>{x.NoList=true;x.NoSong=true;});
   Check(r.End==ScanEnd.Empty&&g.Downs==0&&!g.AnyHeld,$"곡 목록이 비었으면(제목도 목록도 없다) 그만둔다 {Say(r,g)}");
   (r,g)=Scan(part,5,24,x=>x.NoList=true);
   Check(r.End==ScanEnd.NotFirst&&g.Downs==0&&!g.AnyHeld,$"곡의 제목·표는 보이는데 목록만 비었으면 빈 목록이 아니라 첫 곡인지 모르는 것으로 그만둔다 {Say(r,g)}");
   (r,g)=Scan(part,5,24,x=>x.UpDead=true);
   Check(r.End==ScanEnd.NotFirst&&g.Downs==0&&!g.AnyHeld&&r.Items.Count==0,$"↑ 가 먹지 않아 첫 곡에 닿지 못하면 읽지 않고 그만둔다 {Say(r,g)}");
  }
  // 11. 처음 연 목록(첫 곡의 띠가 24): 띠 위에 볼 자리가 18줄뿐이다(이 자리를 '목록 밖'으로 보면 첫 곡에서 멈춘다)
  {
   var (r,g)=Scan(part,0,24,x=>x.Fresh=true);
   Check(Clean(r,part),$"처음 연 목록의 첫 곡(띠 24)에서 시작해도 끝까지 읽는다 {Say(r,g)}{Wrong(r,part)}");
   (r,g)=Scan(part,4,24,x=>x.Fresh=true);
   Check(Clean(r,part)&&g.Ups>=4,$"처음 연 목록에서 네 곡 아래(띠 272)에서 시작해도 첫 곡으로 올라가 끝까지 읽는다 {Say(r,g)}{Wrong(r,part)}");
   (r,g)=Scan(part,1,24,x=>{x.Fresh=true;x.UpDead=true;});
   Check(r.End==ScanEnd.NotFirst&&g.Downs==0&&r.Items.Count==0,$"처음 연 목록의 둘째 곡(띠 86)에서 ↑ 가 먹지 않으면 윗곡을 보고 그만둔다 {Say(r,g)}");
  }
  // 12. 띠가 보이지 않음: 검은색에 가까운 띠 뒤로 배경의 어두운 그림자가 지나가면 띠의 가장자리가 보이지 않는다(실제 게임에서 본 것).
  // 띠를 찾아야 하는 자리(첫 곡, 이름이 같은 곡의 아티스트, 목록의 끝)에서는 BarWait 까지 기다린다
  {
   int p=pairs[0];var around=all.GetRange(Math.Max(0,p-12),Math.Min(all.Count,p+14)-Math.Max(0,p-12));int q=p-Math.Max(0,p-12);
   var (r0,_)=Scan(around,0,24);
   var (r,g)=Scan(around,0,24,x=>x.BarHidden[q+1]=1.5);
   Check(Clean(r,around)&&r.Items[q].Artist==around[q].Song.Artist,$"이름이 같은 곡의 다음 곡에서 띠가 1.5초 보이지 않아도 기다렸다 아티스트를 읽는다 {Say(r,g)}{Wrong(r,around)}");
   (r,g)=Scan(around,0,24,x=>x.BarHidden[q+1]=0);double more=r.Seconds-r0.Seconds;
   Check(r.End==ScanEnd.Done&&r.Items.Count==around.Count&&r.Items[q].Judge.Song==null&&r.Items[q].Questions==1&&r.Questions==1&&more>=CollectionScan.BarWait-0.1&&more<CollectionScan.BarWait+1&&Wrong(r,around)=="",
    $"띠가 계속 보이지 않으면 {CollectionScan.BarWait}초 기다린 뒤 그 곡만 곡 선택 1건으로 넘긴다(더 걸린 시간 {more:0.0}초) {Say(r,g)}{Wrong(r,around)}");
   (r,g)=Scan(part,0,24,x=>x.BarHidden[part.Count-1]=1.5);
   Check(Clean(r,part),$"마지막 곡에서 띠가 1.5초 보이지 않아도 기다렸다 끝을 안다 {Say(r,g)}{Wrong(r,part)}");
   (r,g)=Scan(part,0,24,x=>x.BarHidden[part.Count-1]=0);
   Check(r.End==ScanEnd.NotMoved&&r.Detail=="고른 줄을 찾지 못했다"&&r.Items.Count==part.Count&&Wrong(r,part)=="",$"마지막 곡에서 띠가 계속 보이지 않으면 기다린 뒤 멈춘다 {Say(r,g)}{Wrong(r,part)}");
   (r,g)=Scan(part,5,24,x=>x.BarHidden[0]=1.5);
   Check(Clean(r,part),$"첫 곡에서 띠가 1.5초 보이지 않아도 기다렸다 읽기 시작한다 {Say(r,g)}{Wrong(r,part)}");
   (r,g)=Scan(part,5,24,x=>x.BarHidden[0]=0);
   Check(r.End==ScanEnd.NotFirst&&g.Downs==0&&!g.AnyHeld&&r.Items.Count==0,$"첫 곡의 띠가 끝내 보이지 않아도 곡이 보이면 빈 목록이 아니라 첫 곡인지 모르는 것으로 그만둔다 {Say(r,g)}");
  }
  // 13. ALL 이 아닌 필터, 멈추지 않는 화면
  {
   var (r,g)=Scan(part,5,24,x=>x.NoFilter=true);
   Check(r.End==ScanEnd.NotAll&&g.Downs+g.Ups==0,$"고른 필터가 ALL 이 아니면 키를 보내지 않고 그만둔다 {Say(r,g)}");
   // 같은 제목에 빈칸의 자리가 같은 두 곡의 표를 번갈아 보이면(그대로 받으면 다른 곡의 점수가 이 곡의 확실한 값이 된다) 그 곡의 칸은 확실하다고 하지 않는다
   int i=Enumerable.Range(3,part.Count-6).First(k=>NumberCell(part[k])>=0&&all.Any(o=>o!=part[k]&&Blanks(o)==Blanks(part[k])&&Enumerable.Range(0,16).Any(c=>o.Cell(c)!=part[k].Cell(c))));
   var twin=all.First(o=>o!=part[i]&&Blanks(o)==Blanks(part[i])&&Enumerable.Range(0,16).Any(c=>o.Cell(c)!=part[i].Cell(c)));
   int cells=Enumerable.Range(0,16).Count(c=>part[i].Cell(c)!="");
   (r,g)=Scan(part,0,24,x=>x.Flicker[i]=twin);
   Check(r.End==ScanEnd.Done&&r.Items.Count==part.Count&&r.Items[i].Sure.Count(s=>s)==16-cells&&r.Items[i].Questions==cells&&r.Questions==cells&&r.Items[i].Screen!=null&&!r.Items[i].TitleUnsteady&&r.Items[i].TriedScreens.Count<=CollectionScan.MoreShots&&r.Items.Where(x=>x.Questions==0).All(x=>x.TriedScreens.Count==0)&&Wrong(r,part)=="",
    $"표가 멈추지 않는 곡(두 곡의 표가 번갈아)은 다시 잡아도 같으면 칸 {cells}개를 모두 확인할 것으로 넘기고 계속 읽는다 {Say(r,g)}{Wrong(r,part)}");
   // 흔들린 것이 제목이면 곡도 정하지 않는다(곡 선택 1건)
   var otherTitle=part.First(o=>data.TitleOf(o.Song)!=data.TitleOf(part[i].Song));
   (r,g)=Scan(part,0,24,x=>x.TitleFlicker[i]=otherTitle);
   Check(r.End==ScanEnd.Done&&r.Items.Count==part.Count&&r.Items[i].Judge.Song==null&&r.Items[i].Questions==1+cells&&r.Questions==1+cells&&r.Items[i].Screen!=null&&Wrong(r,part)==""
    &&r.Items[i].TitleUnsteady&&CollectionReview.From(r).Groups.Single(gr=>gr.Item==r.Items[i]).TitleShaky&&r.Items[i].TriedScreens.Count<=CollectionScan.MoreShots,
    $"제목이 멈추지 않는 곡(두 제목이 번갈아)은 곡을 정하지 않고 곡 선택 1건과 칸 {cells}개를 확인할 것으로 넘긴다. 판독 검수에서는 모두 제외로 넘길 수 없는 묶음이다 {Say(r,g)}{Wrong(r,part)}");
   // 커서가 점수표 위를 움직이는 곡은 다시 잡은 화면 가운데 마지막 화면과도 서로도 다른 것을 3장까지 캡처에 더한다
   (r,g)=Scan(part,0,24,x=>x.CursorMoving.Add(i));
   {
    var moving=r.Items[i];var shots=moving.TriedScreens;bool distinct=true;
    for(int x=0;x<shots.Count;x++){if(moving.Screen!=null&&shots[x].AsSpan().SequenceEqual(moving.Screen))distinct=false;for(int y=x+1;y<shots.Count;y++)if(shots[x].AsSpan().SequenceEqual(shots[y]))distinct=false;}
    Check(r.End==ScanEnd.Done&&moving.Unsteady&&moving.Questions==cells&&shots.Count is >=1 and <=CollectionScan.MoreShots&&distinct&&r.Items.Where(x=>x.Questions==0).All(x=>x.TriedScreens.Count==0)&&Wrong(r,part)=="",
     $"커서가 점수표 위를 움직이는 곡은 칸 {cells}개를 확인할 것으로 넘기고, 다시 잡은 화면 {shots.Count}장(마지막 화면과도 서로도 다름)을 캡처에 더한다 {Say(r,g)}{Wrong(r,part)}");
   }
   // 처음엔 커서가 다른 칸들을 지나 그 칸이 보이다가 끝에는 그 칸을 가린 채 흔들리는 곡. 마지막 화면은 가렸어도 더한 화면 가운데 그 칸을 읽을 수 있는 것이 있다
   {
    int peek=NumberCell(part[i]);
    (r,g)=Scan(part,0,24,x=>x.CursorPeek[i]=peek);
    var item=r.Items[i];int readable=0;
    CellRead Cell(byte[] png){using var bitmap=new Bitmap(new MemoryStream(png));return reader.Read(new CollectionFrame(bitmap)).Cells[peek];}
    foreach(var png in item.TriedScreens){var cell=Cell(png);if(CollectionReader.Certain(cell)&&cell.Text==part[i].Cell(peek))readable++;}
    var final=item.Screen==null?null:Cell(item.Screen);
    Check(r.End==ScanEnd.Done&&item.Unsteady&&final!=null&&!(CollectionReader.Certain(final)&&final.Text==part[i].Cell(peek))&&readable>0,
     $"커서가 비켰다가 다시 그 칸을 가린 곡: 마지막 화면은 가렸고, 더한 화면 {item.TriedScreens.Count}장 가운데 {readable}장에서 그 칸({part[i].Cell(peek)})을 확실하게 읽는다 {Say(r,g)}");
   }
   // 커서가 같은 칸의 줄을 좌우로 오가는 곡. 또렷했던 화면이 캡처에 남는다
   {
    int sweep=NumberCell(part[i]);
    (r,g)=Scan(part,0,24,x=>x.CursorSweep[i]=sweep);
    var item=r.Items[i];int readable=0;
    foreach(var png in item.TriedScreens){using var bitmap=new Bitmap(new MemoryStream(png));var cell=reader.Read(new CollectionFrame(bitmap)).Cells[sweep];if(CollectionReader.Certain(cell)&&cell.Text==part[i].Cell(sweep))readable++;}
    Check(r.End==ScanEnd.Done&&item.Unsteady&&readable>0&&r.Items.Where(x=>x.Questions==0).All(x=>x.TriedScreens.Count==0),
     $"커서가 같은 칸의 줄을 오가는 곡: 더한 화면 {item.TriedScreens.Count}장 가운데 {readable}장에서 그 칸({part[i].Cell(sweep)})을 확실하게 읽는다 {Say(r,g)}");
   }
   // 그 칸이 처음 두 장에만 보이고 커서가 들어와 멈추는 곡. 또렷했던 처음 화면이 캡처에 남는다
   {
    int enter=NumberCell(part[i]);
    (r,g)=Scan(part,0,24,x=>x.CursorEnter[i]=enter);
    var item=r.Items[i];int readable=0;
    foreach(var png in item.TriedScreens){using var bitmap=new Bitmap(new MemoryStream(png));var cell=reader.Read(new CollectionFrame(bitmap)).Cells[enter];if(CollectionReader.Certain(cell)&&cell.Text==part[i].Cell(enter))readable++;}
    Check(r.End==ScanEnd.Done&&item.Questions>0&&!item.Sure[enter]&&readable>0,
     $"그 칸이 처음 두 장에만 보이고 커서가 들어와 멈추는 곡: 더한 화면 {item.TriedScreens.Count}장 가운데 {readable}장에서 그 칸({part[i].Cell(enter)})을 확실하게 읽는다(확인할 것 {item.Questions}) {Say(r,g)}");
   }
   // 그 칸이 곡 읽기의 첫째 화면에만, 둘째 화면에만 보이는 곡: 더한 화면 가운데 그 칸을 읽을 수 있는 것이 남는지 본다
   foreach(int clear in new[]{2,3})
   {
    int cell=NumberCell(part[i]);
    (r,g)=Scan(part,0,24,x=>x.CursorClearOnly[i]=(cell,clear));
    var item=r.Items[i];int readable=0;
    foreach(var png in item.TriedScreens){using var bitmap=new Bitmap(new MemoryStream(png));var shown=reader.Read(new CollectionFrame(bitmap)).Cells[cell];if(CollectionReader.Certain(shown)&&shown.Text==part[i].Cell(cell))readable++;}
    Check(r.End==ScanEnd.Done&&item.Questions>0&&!item.Sure[cell]&&readable>0,
     $"그 칸이 곡 읽기의 {(clear==2?"첫째":"둘째")} 화면에만 보이는 곡: 더한 화면 {item.TriedScreens.Count}장 가운데 {readable}장에서 그 칸({part[i].Cell(cell)})을 확실하게 읽는다(확인할 것 {item.Questions}) {Say(r,g)}");
   }
   // 읽는 동안 필터가 ALL 이 아니게 되면 멈춘다. 잠깐 가려진 것은 다시 보고 이어 읽는다
   (r,g)=Scan(part,0,24,x=>x.FilterOffAt=10);
   Check(r.End==ScanEnd.FilterChanged&&r.Items.Count==10&&!g.AnyHeld&&Wrong(r,part)=="",$"읽는 동안 필터가 바뀌면(11번째 곡부터 ALL 이 아님) 그 자리에서 멈추고 끝까지 읽은 결과로 넘기지 않는다 {Say(r,g)}");
   (r,g)=Scan(part,0,24,x=>x.FilterOffAt=0);
   Check(r.End==ScanEnd.NotAll&&g.Downs+g.Ups==0,$"(처음부터 ALL 이 아니면 전처럼 시작하지 않는다) {Say(r,g)}");
   // 다음 곡으로 넘어가는 순간 곡 하나짜리 팩으로 바뀌면 화면이 그대로라 목록의 끝처럼 보인다: 끝으로 넘기기 전에 ALL 을 보고 멈춘다
   var four=part.GetRange(0,4);(r,g)=Scan(four,0,24,x=>x.PackAt=1);
   Check(r.End==ScanEnd.FilterChanged&&r.Items.Count==2&&!g.AnyHeld,$"네 곡 가운데 둘째 곡에서 ↓ 와 함께 곡 하나짜리 팩으로 바뀌면 끝까지 읽은 결과로 넘기지 않고 필터 변경으로 멈춘다 {Say(r,g)}");
   (r,g)=Scan(part,0,24,x=>x.FilterGone[10]=0.25);
   Check(Clean(r,part),$"필터 자리가 0.25초 가려졌다 돌아오면 다시 보고 이어 읽는다 {Say(r,g)}{Wrong(r,part)}");
   // 이름이 같은 곡의 아티스트를 읽을 때 목록이 멈추지 않으면 그 화면으로 곡을 정하지 않는다
   int p=pairs[0];var around=all.GetRange(Math.Max(0,p-12),Math.Min(all.Count,p+14)-Math.Max(0,p-12));int q=p-Math.Max(0,p-12);
   (r,g)=Scan(around,0,24,x=>x.ListShake.Add(q+1));
   Check(r.End==ScanEnd.Done&&r.Items.Count==around.Count&&r.Items[q].Judge.Song==null&&r.Items[q].Artist==null&&r.Items[q].Questions==1&&r.Questions==1&&Wrong(r,around)=="",
    $"이름이 같은 곡의 아티스트를 볼 목록이 멈추지 않으면 곡을 정하지 않고 곡 선택 1건으로 넘긴다 {Say(r,g)}{Wrong(r,around)}");
  }
  // 알려진 한계: 한 번 눌러 두 곡이 넘어가면 화면으로는 알 수 없다(실제 게임의 시험에서는 나오지 않았다)
  {
   var (r,g)=Scan(part,0,24,x=>x.Twice.Add(10));
   notes.Add($"알려진 한계: 11번째 곡에서 ↓ 한 번에 두 곡이 넘어가면 {(r.End==ScanEnd.Done&&r.Items.Count==part.Count-1?"알아채지 못하고 한 곡이 빠진 채 끝난다":"다르게 끝났다")} {Say(r,g)}{Wrong(r,part,11)}");
  }
  notes.Add(failed==0?"모두 통과":$"실패 {failed}");
  File.WriteAllLines(reportPath,notes);if(failed>0)Environment.ExitCode=2;
 }

 /// <summary>실제 화면을 고쳐 다른 필터를 고른 화면을 흉내 낸다: ALL 을 지운 것, ALL 을 오른쪽으로 옮긴 것, 다음 필터의 흐린 글자를 금색으로 바꿔 맨 앞에 놓은 것</summary>
 static IEnumerable<(string Name,CollectionFrame Frame)> OtherFilters(CollectionFrame real)
 {
  const int x0=CollectionScreen.FilterX,y0=CollectionScreen.FilterY,w=CollectionScreen.FilterWidth,h=CollectionScreen.FilterHeight;int W=CollectionFrame.Width;
  byte[] Erased(){var b=(byte[])real.Bytes.Clone();for(int y=y0;y<y0+h;y++)for(int x=x0;x<x0+w;x++){int i=(y*W+x)*4;b[i]=76;b[i+1]=73;b[i+2]=73;}return b;}
  yield return("ALL 을 지움",new CollectionFrame(Erased()));
  {var b=Erased();var all=CollectionScreen.AllFilter;for(int y=0;y<h;y++)for(int x=0;x<w-40;x++)if(all[x,y]){int i=((y0+y)*W+x0+x+40)*4;b[i]=0;b[i+1]=180;b[i+2]=250;}yield return("ALL 을 오른쪽으로 40",new CollectionFrame(b));}
  {
   var b=Erased();
   for(int y=y0;y<y0+h;y++)for(int x=1600;x<1740;x++){int i=(y*W+x)*4;if(real.Bytes[i+2]-real.Bytes[i]>20){int j=(y*W+x-77)*4;b[j]=0;b[j+1]=180;b[j+2]=250;}}
   yield return("다음 필터를 금색으로 맨 앞에",new CollectionFrame(b));
  }
 }
 /// <summary>멈춘 실제 화면으로 목록의 생김새를 읽는 코드를 본다.</summary>
 public static void Stills(Catalog catalog,string folder,string listPath,int first,int last,string reportPath)
 {
  var data=CollectionData.Load();var reader=new CollectionReader(catalog,data);
  var order=Load(catalog,folder,listPath,first,last).Select(s=>s.Song).ToList();var place=order.Select((s,i)=>(s.Id,i)).ToDictionary(p=>p.Id,p=>p.i);
  var shots=JsonSerializer.Deserialize<List<JsonElement>>(File.ReadAllText(listPath))!.ToDictionary(e=>e.GetProperty("n").GetString()!,e=>e.GetProperty("id").GetString()!);
  var names=catalog.Songs.Select(s=>s.Artist).Distinct().ToList();
  var lines=new List<string>();int stills=0,known=0,header=0,bars=0,endsRight=0,rows=0,rowsRight=0,rowsUnsure=0;double rightMax=0,gapMin=1;int belowMin=int.MaxValue,aboveMin=int.MaxValue,twinRows=0,twinRight=0;double readMs=0;
  int filterMax=0,allCount=0,negativeMin=int.MaxValue,shownCount=0,titleInkMin=int.MaxValue;var filterLines=new List<string>();
  (string N,string Song,int Bar,(Mask Title,Mask Table,Mask List) Sign)? before=null;int sameTitle=0,sameTable=0,sameList=0,samePairs=0,otherList=int.MaxValue,otherPairs=0;var signLines=new List<string>();
  foreach(string path in Directory.GetFiles(folder,"*-still.png").OrderBy(f=>f))
  {
   string n=Path.GetFileName(path)[..4];stills++;
   using var bitmap=new Bitmap(path);var frame=new CollectionFrame(bitmap);
   var watch=System.Diagnostics.Stopwatch.StartNew();reader.Read(frame);readMs+=watch.Elapsed.TotalMilliseconds;
   bool isData=CollectionScreen.IsMusicData(frame);if(isData)header++;
   // 필터: ALL 그림과 다른 화소. 다른 필터를 고른 화면은 이 화면을 고쳐 흉내 낸다:
   // ALL 을 지운 것, ALL 을 오른쪽으로 40 옮긴 것, 다음 필터(흐린 RESPECT V)를 금색으로 바꿔 맨 앞에 놓은 것
   {
    int f=CollectionScreen.FilterDiffer(frame);filterMax=Math.Max(filterMax,f);if(CollectionScreen.IsAll(frame))allCount++;else filterLines.Add($"{n} ALL 로 보지 않음(다른 화소 {f})");
    foreach(var (name,fake) in OtherFilters(frame)){int d=CollectionScreen.FilterDiffer(fake);negativeMin=Math.Min(negativeMin,d);if(CollectionScreen.IsAll(fake))filterLines.Add($"{n} {name}: ALL 로 봄(다른 화소 {d})");}
    var look=(Title:CollectionScreen.TitleSign(frame).Count(),List:CollectionScreen.ListSign(frame).Count());titleInkMin=Math.Min(titleInkMin,look.Title);
    if(look.Title>CollectionScan.ShownInk||look.List>CollectionScan.ShownInk)shownCount++;else filterLines.Add($"{n} 곡이 보이지 않는다고 봄(제목 {look.Title}·목록 {look.List} 화소)");
    if(CollectionScreen.Bar(frame)<0)filterLines.Add($"{n} 고른 줄 없음: 제목 {look.Title}·목록 {look.List} 화소");
   }
   // 움직였는지 견주는 글자 그림: 바로 앞의 멈춘 화면과 같은 곡·같은 띠이면 같은 화면이다(빛 번짐만 다르다). 그렇지 않으면 다른 화면이다
   {
    var sign=(Title:CollectionScreen.TitleSign(frame),Table:CollectionScreen.TableSign(frame),List:CollectionScreen.ListSign(frame));
    string song=shots.TryGetValue(n,out string? sid)?sid:"";int barNow=CollectionScreen.Bar(frame);
    if(before!=null&&song!="")
    {
     int t=before.Value.Sign.Title.Differ(sign.Title),b=before.Value.Sign.Table.Differ(sign.Table),l=before.Value.Sign.List.Differ(sign.List);
     if(before.Value.Song==song&&before.Value.Bar==barNow){sameTitle=Math.Max(sameTitle,t);sameTable=Math.Max(sameTable,b);sameList=Math.Max(sameList,l);samePairs++;if(t>CollectionScan.SignSame||b>CollectionScan.SignSame||l>CollectionScan.ListStill)signLines.Add($"{before.Value.N}·{n} 같은 화면인데 제목 {t}, 표 {b}, 목록 {l}");}
     else if(before.Value.Song!=song){otherList=Math.Min(otherList,l);otherPairs++;if(l<=CollectionScan.ListMoved)signLines.Add($"{before.Value.N}·{n} 다른 곡인데 목록 {l}");}
    }
    before=song==""?null:(n,song,barNow,sign);
   }
   int bar=CollectionScreen.Bar(frame);
   if(bar<0){lines.Add($"{n} 고른 줄을 찾지 못함(머리글 {(isData?"맞음":"다름")})");continue;}
   bars++;int below=CollectionScreen.RowBelow(frame,bar),above=CollectionScreen.RowAbove(frame,bar);
   if(!shots.TryGetValue(n,out string? id)||!place.TryGetValue(id,out int at)){lines.Add($"{n} 띠 {bar}, 아래 {below}, 위 {above} (차례를 모르는 화면)");continue;}
   known++;
   bool isLast=at==order.Count-1,isFirst=at==0;
   bool endOk=(below<CollectionScreen.RowQuiet)==isLast&&(above>=0&&above<CollectionScreen.RowQuiet)==isFirst;if(endOk)endsRight++;
   if(!isLast)belowMin=Math.Min(belowMin,below);if(!isFirst&&above>=0)aboveMin=Math.Min(aboveMin,above);
   var letters=CollectionScreen.ListLetters(frame);var said=new List<string>();
   foreach(var (offset,neighbour,label) in new[]{(CollectionScreen.ArtistAbove,at-1,"위"),(CollectionScreen.ArtistBelow,at+1,"아래")})
   {
    if(neighbour<0||neighbour>=order.Count)continue;
    var costs=names.ToDictionary(a=>a,_=>1.0);bool inside=false;
    for(int d=-3;d<=3;d++){int top=bar+offset+d;if(top<0||top+CollectionScreen.ArtistHeight>CollectionScreen.ListHeight)continue;inside=true;foreach(var (artist,cost) in reader.Artists(letters,top,names))if(cost<costs[artist])costs[artist]=cost;}
    if(!inside)continue;
    var best=costs.OrderBy(c=>c.Value).Take(2).ToList();string want=order[neighbour].Artist;rows++;
    bool sure=best[0].Value<=CollectionScan.ArtistSure&&best[1].Value-best[0].Value>=CollectionScan.ArtistGap;
    if(sure&&best[0].Key==want){rowsRight++;rightMax=Math.Max(rightMax,best[0].Value);gapMin=Math.Min(gapMin,best[1].Value-best[0].Value);}
    else if(!sure)rowsUnsure++;
    said.Add($"{label} '{want}' → '{best[0].Key}' {best[0].Value:0.000} (다음 '{best[1].Key}' {best[1].Value:0.000}){(sure?(best[0].Key==want?"":" 틀림"):" 확실하지 않음")}");
    // 제품이 하는 그대로: 이름이 같은 곡의 줄은 그 곡들의 아티스트끼리만 견준다
    var twins=catalog.Songs.Where(s=>data.TitleOf(s)==data.TitleOf(order[neighbour])).Select(s=>s.Artist).Distinct().ToList();
    if(twins.Count>1)
    {
     var pick=twins.Select(a=>(Artist:a,Cost:costs[a])).OrderBy(c=>c.Cost).ToList();twinRows++;
     bool twinSure=pick[0].Cost<=CollectionScan.ArtistSure&&pick[1].Cost-pick[0].Cost>=CollectionScan.ArtistGap;if(twinSure&&pick[0].Artist==want)twinRight++;
     said.Add($"[이름이 같은 곡의 줄: {string.Join(", ",pick.Select(c=>$"{c.Artist} {c.Cost:0.000}"))} → {(twinSure?(pick[0].Artist==want?"맞음":"틀림"):"정하지 못함")}]");
    }
   }
   lines.Add($"{n} {order[at]}: 띠 {bar}, 아래 {below}, 위 {above}{(endOk?"":" 끝의 판단이 다름")}. {string.Join(" / ",said)}");
  }
  var report=new StringBuilder();
  report.AppendLine($"화면 한 장을 읽는 데(제목·16칸·BEST·카테고리) 평균 {readMs/Math.Max(1,stills):0.0}ms");
  report.AppendLine($"멈춘 화면 {stills}: 머리글이 맞는 화면 {header}, 고른 줄을 찾은 화면 {bars}, 차례를 아는 화면 {known}");
  report.AppendLine($"첫 곡·마지막 곡의 판단이 맞은 화면 {endsRight}/{known}. 줄이 있을 때의 가장 작은 값: 아래 {(belowMin==int.MaxValue?0:belowMin)}, 위 {(aboveMin==int.MaxValue?0:aboveMin)} (문턱 {CollectionScreen.RowQuiet})");
  report.AppendLine($"윗줄·아랫줄의 아티스트 {rows}줄(곡 목록의 아티스트 {names.Count}명 모두를 후보로): 맞게 정한 줄 {rowsRight}, 확실하지 않다고 한 줄 {rowsUnsure}, 틀리게 정한 줄 {rows-rowsRight-rowsUnsure}. 맞은 줄의 어긋난 몫 최대 {rightMax:0.000}, 다음 후보와의 차 최소 {gapMin:0.000}");
  report.AppendLine($"그 가운데 이름이 같은 곡의 줄 {twinRows}: 그 곡들의 아티스트끼리 견줘(제품이 하는 그대로) 맞게 정한 줄 {twinRight}");
  report.AppendLine($"움직였는지 견주는 글자 그림: 같은 곡·같은 띠의 이어진 멈춘 화면 {samePairs}쌍에서 다른 화소 최대 제목 {sameTitle}·표 {sameTable}(문턱 {CollectionScan.SignSame})·목록 {sameList}(문턱 {CollectionScan.ListStill}). 다른 곡의 이어진 화면 {otherPairs}쌍에서 목록의 다른 화소 최소 {(otherList==int.MaxValue?0:otherList)}(문턱 {CollectionScan.ListMoved})");
  report.AppendLine($"필터: ALL 로 본 화면 {allCount}/{stills}, ALL 그림과 다른 화소 최대 {filterMax}(문턱 {CollectionScreen.FilterNear}). 다른 필터를 흉내 낸 화면 {stills*3}장에서 가장 작은 값 {negativeMin}");
  report.AppendLine($"곡이 보이는가(5번): 보인다고 본 화면 {shownCount}/{stills}, 제목 글자 그림의 화소 가장 작은 값 {titleInkMin}(문턱 {CollectionScan.ShownInk})");
  bool pass=header==stills&&endsRight==known&&rows-rowsRight-rowsUnsure==0&&twinRight==twinRows&&signLines.Count==0&&allCount==stills&&negativeMin>CollectionScreen.FilterNear&&shownCount==stills;
  report.AppendLine(pass?"통과: 머리글·끝의 판단이 모두 맞고, 틀리게 정한 아티스트가 없고, 이름이 같은 곡의 줄을 모두 맞게 정했다":"실패");
  foreach(string l in signLines)report.AppendLine(l);
  foreach(string l in filterLines)report.AppendLine(l);
  foreach(string l in lines)report.AppendLine(l);
  File.WriteAllText(reportPath,report.ToString());if(!pass)Environment.ExitCode=2;
 }
}
