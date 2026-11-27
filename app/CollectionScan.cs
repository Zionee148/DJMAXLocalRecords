namespace LadderRecords;
// 컬렉션 가져오기: 게임에 키를 보내며 첫 곡부터 마지막 곡까지 읽는 차례.
// 창도 Windows 호출도 여기에 없다: 화면·키·시계·앞 창을 IScanWorld 로 받는다(제품은 실제 게임을, 검사는 가짜 게임과 가짜 시계를 준다).
// 화면 한 장을 읽고 판단하는 것은 CollectionReader 다. 여기는 언제 잡고 언제 키를 보내고 언제 멈추는지만 정한다.
//
// 지키는 것:
//  - 키는 한 번만 보낸다. 안 움직였다고 다시 보내지 않는다(늦게 도착한 키와 겹치면 두 곡이 넘어간다). 끝이 아닌데 안 움직이면 멈춘다.
//  - 키를 보내기 직전마다 게임이 앞 창인지 본다. 앞 창이 아니거나 중지가 눌리면 누르고 있던 키를 떼고 그만둔다.
//  - 확실하지 않은 것은 값으로 쓰지 않고 확인할 것으로 넘긴다. 확인할 것이 허용 수를 넘는 순간 멈춘다.
//  - 여기서는 아무것도 저장하지 않는다. 끝까지 읽지 못한 결과(End 가 Done 이 아닌 것)로 기록을 만들지 않는 것은 받는 쪽의 일이다.

enum ScanKey{Up,Down}
/// <summary>읽기의 진행이 바깥에 기대는 것.</summary>
interface IScanWorld
{
 /// <summary>게임 화면 한 장(1920×1080). 받지 못했거나 읽을 수 있는 크기가 아니면 null.</summary>
 CollectionFrame? Capture();
 /// <summary>키를 누르거나(down) 뗀다.</summary>
 void Key(ScanKey key,bool down);
 /// <summary>지금 시각(초).</summary>
 double Now{get;}
 void Wait(double seconds);
 /// <summary>게임이 앞 창인가.</summary>
 bool Active{get;}
 /// <summary>사용자가 중지를 눌렀는가.</summary>
 bool StopAsked{get;}
}
/// <summary>읽기가 끝난 까닭. Done 만 끝까지 읽은 것이다.</summary>
enum ScanEnd
{
 Done,
 /// <summary>창 전환이나 중지 키. 준비 화면으로 돌아간다.</summary>
 Cancelled,
 /// <summary>화면을 받지 못했거나 읽을 수 있는 크기가 아니다.</summary>
 NoScreen,
 /// <summary>MUSIC DATA 화면이 아니다.</summary>
 WrongScreen,
 /// <summary>MUSIC DATA 화면인데 고른 필터가 ALL 이 아니다(그 필터의 곡만 읽힌다). 키를 보내지 않았다.</summary>
 NotAll,
 /// <summary>읽는 동안 고른 필터가 ALL 이 아니게 됐다(게임에서 Space 를 눌렀다). 끝까지 읽은 결과가 아니다.</summary>
 FilterChanged,
 /// <summary>시작 단축키를 한도(3초)가 지나도록 누르고 있었다. 키를 보내지 않았다(누른 채 보내는 ↑ 는 게임에 Ctrl+↑ 로 들어간다). 창이 정한다.</summary>
 KeysHeld,
 /// <summary>곡 목록이 비었다.</summary>
 Empty,
 /// <summary>첫 곡까지 올라갔는지 확인하지 못했다.</summary>
 NotFirst,
 /// <summary>다음 곡으로 옮겨졌는지 확인하지 못했다(같은 곡이 두 번 읽힌 것도 여기에 든다).</summary>
 NotMoved,
 /// <summary>제목은 정해졌는데 빈칸의 자리가 그 곡의 패턴과 다르다(앱에 없는 패턴).</summary>
 UnknownPattern,
 /// <summary>확인할 것이 허용 수를 넘었다.</summary>
 TooMany,
}
/// <summary>곡 하나의 결과. Judge.Song 이 없으면 곡을 정하지 못한 것이고 Sure 가 거짓인 칸은 값을 쓰지 않는다.</summary>
sealed class ScanItem
{
 public ShotRead Shot=null!;public SongJudge Judge=null!;
 /// <summary>칸마다 확실한가(곡을 정하지 못했어도 채운다).</summary>
 public bool[] Sure=new bool[16];
 /// <summary>목록에서 읽은 아티스트(이름이 같은 곡일 때만).</summary>
 public string? Artist;
 /// <summary>확인할 것의 수: 곡 선택 1, 확실하지 않은 칸마다 1.</summary>
 public int Questions;
 /// <summary>확인할 것이 있을 때 그 화면 전체(PNG), 아티스트를 읽으려던 화면(PNG).</summary>
 public byte[]? Screen,ArtistScreen;
 /// <summary>아티스트를 읽은 화면이 한 곡 위로 올라가 받은 것이다(마지막 곡). 아니면 다음 곡으로 옮겨 받은 것이다(캡처 보기의 장면 이름)</summary>
 public bool ArtistAfterUp;
 internal CollectionFrame? Frame,ArtistFrame;
 /// <summary>다시 잡아도 제목과 표가 이어진 두 화면에서 같지 않았다: 칸의 값을 확실하다고 하지 않는다</summary>
 internal bool Unsteady;
 /// <summary>흔들린 것이 제목이었다(곡을 정하지 않았다). 후보는 마지막에 본 제목에서 나온 것이라 실제 곡이 아닐 수 있다: 판독 검수에서 곡을 고르지 않고 모두 제외할 수 없다</summary>
 internal bool TitleUnsteady;
 /// <summary>그 곡을 읽는 동안 본 화면 가운데 서로 가장 다른 것(마지막 화면 밖. Pool 장까지). 결과를 닫을 때 확인할 것이 있으면 MoreShots 장을 골라 PNG 로 TriedScreens 에 옮기고 버린다</summary>
 internal List<CollectionFrame>? Tried;
 /// <summary>그 곡을 읽는 동안 확인할 만한 칸을 또렷하게(판독기가 확실하게) 보인 화면과 그 칸들(비트). 결과를 닫을 때 Tried 보다 먼저 고른다</summary>
 internal List<(CollectionFrame Frame,int Clear)>? Clear;
 /// <summary>캡처 보기에 '점수표(다시 잡은 n번째)'로 더하는 화면(PNG. 커서가 가린 칸을 앞서 잡은 화면으로 볼 수 있게)</summary>
 public List<byte[]> TriedScreens=new();
}
sealed class ScanResult
{
 public ScanEnd End;public string Detail="";public List<ScanItem> Items=new();
 public int Questions,Allowed;public double Seconds;
 /// <summary>마지막으로 확인한 곡의 이름(중단 화면이 보인다). 곡을 정했으면 곡 목록의 이름, 아니면 읽은 제목이다. 또렷하게 읽은 곡이 없으면 null</summary>
 public string? LastTitle;
 /// <summary>곡 선택의 확인과 점수 칸의 확인으로 나눈 수(허용 수를 넘어 멈췄을 때 보인다)</summary>
 public int SongQuestions=>Items.Count(i=>i.Judge.Song==null);
 public int CellQuestions=>Questions-SongQuestions;
 /// <summary>멈췄을 때 마지막으로 받은 화면들(오래된 것부터. 개발용 도구가 남긴다)</summary>
 internal List<CollectionFrame> LastFrames=new();
}

sealed class CollectionScan
{
 // 누름 시간과 기다림(초). 실제 게임에서 50ms 로 한 곡씩 빠짐없이 옮겨졌고, 첫 곡으로 올라갈 때 20ms 로 120번 빠짐이 없었다.
 // 누름은 20ms 로 둔다(20ms 로 한 실제 전곡 읽기에서도 곡이 빠지지 않았다). 키가 먹지 않으면 다시 보내지 않고 멈춘다(틀린 값이 남지 않는다)
 public const double Press=0.02,Poll=0.015;
 // 키를 보낸 뒤 이만큼 아무것도 바뀌지 않으면 옮겨지지 않은 것이다
 public const double MoveWait=0.7;
 // 확실하지 않으면 다시 잡는 횟수와 그 사이
 public const int Retakes=3;public const double RetakeWait=0.15;
 /// <summary>확인할 것이 남은 곡의 캡처에 더하는 다시 잡은 화면의 수</summary>
 public const int MoreShots=3;
 /// <summary>한 곡을 읽는 동안 잠깐 들고 있는 화면의 수(서로 가장 다른 것. 곡을 닫을 때 MoreShots 장을 고르고 버린다)</summary>
 public const int Pool=6;
 /// <summary>캡처를 고를 때 두 화소를 다르다고 보는 채널 차의 합(빛 번짐처럼 옅게 바뀐 것은 세지 않는다)</summary>
 public const int PixelGap=90;
 // 제목과 표가 이어진 두 번의 캡처에서 같기를 기다리는 한도, 목록이 멈추기를 기다리는 한도와 견주는 간격
 public const double StableWait=1.0,ListWait=1.0,ListStep=0.1;
 // 첫 곡으로: ↑ 를 짧게(FirstPress) 거듭 누른다. 누름이 시작되는 사이는 FirstCycle 보다 짧지 않게 한다(실제 게임에서 누름 20ms·한 번에 60ms 로 20번씩 여섯 번 빠짐이 없었다. 초당 16곡쯤.
 // 누른 채 두면 게임의 되풀이가 초당 8곡이라 이쪽을 쓴다). 이만큼 아무것도 바뀌지 않으면 그만 누른다. 올라가는 데 쓰는 한도
 public const double FirstPress=0.02,FirstCycle=0.06,FirstQuiet=0.6,FirstLimit=180;
 // 두 화면이 같은 곡인가: 제목과 표의 글자 그림(CollectionScreen 의 Sign. 빛 번짐에 흔들리지 않게 고른 것)에서 다른 화소가 SignSame 이하.
 // 목록은 다른 화소가 ListMoved 를 넘으면 움직인 것, ListStill 이하면 멈춘 것. 멈춘 화면끼리는 제목 15·표 10·목록 170 이하였고(실제 게임의 이어진 캡처. 목록의 170 은 어두운 그림자가 한 줄을 지나갈 때다),
 // 이웃한 곡은 제목 186 이상, 한 줄 옮겨진 목록은 3,400 이상이었다
 public const int SignSame=40,ListMoved=1000,ListStill=300;
 // 아티스트: 고른 이름의 어긋난 몫이 ArtistSure 이하이고 다른 후보와 ArtistGap 넘게 벌어져야 한다(멈춘 실제 화면에서 맞는 이름 0.03~0.19, 틀린 이름 0.33 이상). 줄의 자리는 위아래로 ArtistSearch 까지 본다
 public const double ArtistSure=0.25,ArtistGap=0.15;const int ArtistSearch=3;
 // 멈춘 뒤 사용자가 창을 바꾸는 중이었는지 볼 때까지 기다리는 시간
 public const double SwitchWait=0.3;
 // 고른 줄의 띠가 보이지 않으면 이만큼까지 목록이 멈춘 화면을 다시 본다. 검은색에 가까운 띠는 배경의 어두운 그림자가 뒤를 지날 때 위아래 가장자리가 보이지 않는다
 // (실제 게임의 보더리스 전곡 읽기: STOP 다음 곡 Story of Maple 의 띠를 1초 남짓 다시 봐도 찾지 못했다. 마지막 곡 ZET ~Mr.Funky Remix~ 의 띠도 어둡다)
 public const double BarWait=3.0;
 // 곡이 보이는가: 제목 줄이나 목록의 글자 그림(Sign)의 화소. 받아 둔 실제 화면에서 곡 제목이 있으면 수백 화소 넘게 나온다
 public const int ShownInk=100;

 readonly CollectionReader reader;readonly IScanWorld world;readonly ScanResult result=new();
 /// <summary>곡 하나를 읽을 때마다 지금까지 읽은 곡 수와 그 곡의 이름을 알린다(진행 화면). 첫 알림이 오기 전까지는 첫 곡으로 올라가는 중이다</summary>
 public Action<int,string>? Progress{get;set;}
 /// <summary>첫 곡으로 가기와 목록의 끝에서 무엇을 보고 정했는지 적는다(개발용 도구가 받는다. 제품에서는 비어 있다)</summary>
 public Action<string>? Trace{get;set;}
 /// <summary>곡 하나를 닫기 직전(화면이 아직 그 곡에 있다. 개발용 도구가 화면을 남긴다. 제품에서는 비어 있다)</summary>
 public Action<ScanItem>? Closing{get;set;}
 void Note(string line)=>Trace?.Invoke($"{world.Now:0.000} {line}");
 readonly Queue<CollectionFrame> recent=new();const int Recent=3;

 sealed class Stop:Exception{public readonly ScanEnd End;public Stop(ScanEnd end,string detail):base(detail){End=end;}}
 /// <summary>화면 한 장과, 움직였는지 견줄 때 쓰는 글자 그림들.</summary>
 sealed class Look
 {
  public readonly CollectionFrame Frame;public readonly Mask Title,Table;Mask? list;
  public Look(CollectionFrame frame){Frame=frame;Title=CollectionScreen.TitleSign(frame);Table=CollectionScreen.TableSign(frame);}
  /// <summary>목록의 글자 그림(크다. 볼 때만 만든다).</summary>
  public Mask List=>list??=CollectionScreen.ListSign(Frame);
 }

 public CollectionScan(CollectionReader reader,IScanWorld world,int allowed){this.reader=reader;this.world=world;result.Allowed=allowed;}
 /// <summary>허용 수: 곡 목록 전체의 패턴 수 × 0.2%, 소수점 아래 버림(12,468패턴이면 24).</summary>
 public static int Allowed(Catalog catalog)=>catalog.Songs.Sum(s=>s.Charts.Count())*2/1000;

 public ScanResult Run()
 {
  double start=world.Now;
  try
  {
   var opening=See();
   if(!CollectionScreen.IsMusicData(opening.Frame))throw new Stop(ScanEnd.WrongScreen,"MUSIC DATA 화면이 아니다");
   if(!CollectionScreen.IsAll(opening.Frame))throw new Stop(ScanEnd.NotAll,$"고른 필터가 ALL 이 아니다(그림과 다른 화소 {CollectionScreen.FilterDiffer(opening.Frame)})");
   GoFirst();
   while(true)
   {
    var item=ReadSong();bool needs=item.Judge.NeedsArtist&&item.Judge.Song==null;
    if(!needs)Close(item);
    string shown=item.Judge.Song?.Name??item.Shot.Title?.Text??"";if(shown.Length>0&&!item.Judge.TitleUnclear)result.LastTitle=shown;
    Progress?.Invoke(result.Items.Count+(needs?1:0),shown);
    // 곡을 정하지 못한 화면(이름이 같은 곡, 또렷하지 않은 제목)은 다음 곡의 제목과 표가 지금과 같을 수 있다: 목록까지 견준다
    bool moved=MoveDown(item.Judge.Song==null);
    // 이름이 같은 곡: 다음 곡으로 간 뒤 고른 줄의 바로 윗줄에서 아티스트를 읽는다. 마지막 곡이면 한 곡 올라가 바로 아랫줄에서 읽는다
    if(needs){if(moved)ReadArtist(item,CollectionScreen.ArtistAbove);else if(MoveUp()){item.ArtistAfterUp=true;ReadArtist(item,CollectionScreen.ArtistBelow);}Close(item);}
    if(!moved)break;
   }
   // 목록의 끝으로 보았다: 그 화면에서도 ALL 인가. 다음 곡으로 넘어가는 순간 필터를 바꾸면 바뀐 필터의 끝(곡 하나짜리 팩이면 화면이 그대로다)을
   // ALL 의 끝으로 볼 수 있다. 아니면 필터 변경으로 멈추고 끝까지 읽은 결과로 넘기지 않는다
   EnsureAll(See());
   result.End=ScanEnd.Done;
  }
  catch(Stop stop)
  {
   result.End=stop.End;result.Detail=stop.Message;Note($"멈춤 {stop.End}: {stop.Message}");if(Trace!=null)result.LastFrames=recent.ToList();
   // 창을 바꾸는 도중의 화면(Alt+Tab 의 창 고르기 등)은 곡이 바뀐 것이나 목록이 빈 것으로 보일 수 있다: 멈춘 뒤 잠시 기다려 사용자가 창을 바꿨으면 취소로 둔다
   if(stop.End is ScanEnd.NotMoved or ScanEnd.NotFirst or ScanEnd.Empty or ScanEnd.UnknownPattern or ScanEnd.FilterChanged)
   {
    world.Wait(SwitchWait);
    if(world.StopAsked||!world.Active){Note($"멈춘 뒤 {(world.StopAsked?"중지":"창 전환")}: 취소로 둔다");result.End=ScanEnd.Cancelled;result.Detail=world.StopAsked?"중지":"창 전환";}
   }
   // 창의 '중지' 단추를 마우스로 누르면 그 창이 먼저 앞 창이 되어 '창 전환'으로 멈출 수 있다: 잠시 뒤 중지가 눌려 있으면 중지로 둔다
   else if(stop.End==ScanEnd.Cancelled&&stop.Message=="창 전환")
   {
    world.Wait(SwitchWait);
    if(world.StopAsked){Note("창 전환 뒤 중지가 눌려 있음: 중지로 둔다");result.Detail="중지";}
   }
  }
  result.Seconds=world.Now-start;return result;
 }

 // ── 화면 ──
 void Check(){if(world.StopAsked)throw new Stop(ScanEnd.Cancelled,"중지");if(!world.Active)throw new Stop(ScanEnd.Cancelled,"창 전환");}
 Look See()
 {
  Check();var f=world.Capture()??throw new Stop(ScanEnd.NoScreen,"화면을 받지 못했거나 읽을 수 있는 크기가 아니다");
  if(Trace!=null){recent.Enqueue(f);while(recent.Count>Recent)recent.Dequeue();}
  return new(f);
 }
 /// <summary>
 /// 한 곡을 읽는 동안 본 화면 가운데 서로 가장 다른 것을 Pool 장까지 들고 있는다(제목·점수표의 글자 그림의 차이. 가득 차면 서로 가장 가까운 짝의 한쪽을 더 멀리 떨어진 새 화면으로 바꾼다).
 /// 시도마다의 마지막 화면만 남기면 커서가 비켜 점수가 보인 화면이 빠질 수 있다
 /// </summary>
 sealed class ShotPool
 {
  readonly List<Look> kept=new();readonly List<(Look Look,int Clear)> clear=new();
  public IReadOnlyList<Look> Looks=>kept;
  /// <summary>또렷한 칸을 새로 더하는 화면(MoreShots 장까지). 서로 가장 다른 것만 들면 커서가 같은 칸을 오갈 때 또렷한 화면이 밀려난다</summary>
  public IReadOnlyList<(Look Look,int Clear)> Clear=>clear;
  static int Gap(Look a,Look b)=>a.Title.Differ(b.Title)+a.Table.Differ(b.Table);
  /// <param name="cells">그 화면에서 확실하게 읽힌 칸(비트). 읽지 않았으면 0</param>
  public void Offer(Look look,int cells){KeepClear(look,cells);Keep(look);}
  /// <summary>또렷한 칸을 새로 더하면 든다(읽은 칸만. cells 가 0 이면 아무것도 하지 않는다)</summary>
  public void KeepClear(Look look,int cells)
  {
   if(cells!=0)
   {
    int covered=0;foreach(var c in clear)covered|=c.Clear;int adds=Bits(cells&~covered);
    if(adds>0)
    {
     if(clear.Count<MoreShots)clear.Add((look,cells));
     else
     {
      // 가득 찼으면 혼자만 덮는 칸이 가장 적은 화면보다 새로 더하는 칸이 많을 때 바꾼다
      int weakest=-1,least=int.MaxValue;
      for(int i=0;i<clear.Count;i++){int others=0;for(int j=0;j<clear.Count;j++)if(j!=i)others|=clear[j].Clear;int own=Bits(clear[i].Clear&~others);if(own<least){least=own;weakest=i;}}
      if(adds>least)clear[weakest]=(look,cells);
     }
    }
   }
  }
  /// <summary>서로 가장 다른 화면으로 든다</summary>
  void Keep(Look look)
  {
   int near=kept.Count==0?int.MaxValue:kept.Min(k=>Gap(k,look));if(near==0)return;
   if(kept.Count<Pool){kept.Add(look);return;}
   int worst=-1,worstGap=int.MaxValue;
   for(int i=0;i<kept.Count;i++){int g=int.MaxValue;for(int j=0;j<kept.Count;j++)if(i!=j)g=Math.Min(g,Gap(kept[i],kept[j]));if(g<worstGap){worstGap=g;worst=i;}}
   if(near>worstGap)kept[worst]=look;
  }
 }
 static int Bits(int mask)=>System.Numerics.BitOperations.PopCount((uint)mask);
 /// <summary>
 /// 캡처 보기에 더할 화면을 MoreShots 장까지 고른다. 먼저 확인할 칸(questioned)을 또렷하게 보인 화면을, 아직 덮지 않은 칸을 가장 많이 덮는 것부터.
 /// 남은 자리는 확인할 칸(곡을 정하지 못했으면 제목도)이 마지막 화면과 가장 다르게 보이는 화면으로(서로도 그 자리가 다른 것만)
 /// </summary>
 internal static List<CollectionFrame> Pick(IReadOnlyList<CollectionFrame> tried,IReadOnlyList<(CollectionFrame Frame,int Clear)> clear,CollectionFrame last,IReadOnlyList<Rectangle> regions,int questioned)
 {
  int Score(CollectionFrame a,CollectionFrame b)=>regions.Sum(r=>PixelsDiffer(a,b,r));
  var picked=new List<CollectionFrame>();int covered=0;
  while(picked.Count<MoreShots)
  {
   CollectionFrame? best=null;int gain=0;
   foreach(var (frame,cells) in clear){if(picked.Contains(frame))continue;int g=Bits(cells&questioned&~covered);if(g>gain){gain=g;best=frame;}}
   if(best==null)break;
   picked.Add(best);covered|=clear.First(c=>ReferenceEquals(c.Frame,best)).Clear;
  }
  foreach(var (frame,_) in tried.Where(f=>!picked.Contains(f)).Select(f=>(f,Score(f,last))).Where(x=>x.Item2>0).OrderByDescending(x=>x.Item2))
  {
   if(picked.Count>=MoreShots)break;
   if(picked.All(p=>Score(p,frame)>0))picked.Add(frame);
  }
  return picked;
 }
 /// <summary>확인할 칸(비트): 확실하지 않은, 빈칸이 아닌 칸</summary>
 internal static int Questioned(ScanItem item){int bits=0;for(int i=0;i<16;i++)if(!item.Sure[i]&&!item.Shot.Cells[i].Blank)bits|=1<<i;return bits;}
 /// <summary>그 화면에서 판독기가 확실하게 읽는 칸(비트). 사용자에게 보일 사진을 고를 때만 쓴다(점수의 확정은 ReadSong 의 판독 그대로다)</summary>
 int ClearCells(CollectionFrame frame)
 {
  var cells=new CellRead[16];Parallel.For(0,16,i=>cells[i]=reader.Cell(CollectionScreen.Cell(frame,i/4,i%4)));
  int bits=0;for(int i=0;i<16;i++)if(!cells[i].Blank&&CollectionReader.Certain(cells[i]))bits|=1<<i;return bits;
 }
 static int PixelsDiffer(CollectionFrame a,CollectionFrame b,Rectangle r)
 {
  int n=0;var x=a.Bytes;var y=b.Bytes;
  for(int row=r.Top;row<r.Bottom;row++){int i=(row*CollectionFrame.Width+r.Left)*4;for(int col=0;col<r.Width;col++,i+=4)if(Math.Abs(x[i]-y[i])+Math.Abs(x[i+1]-y[i+1])+Math.Abs(x[i+2]-y[i+2])>PixelGap)n++;}
  return n;
 }
 /// <summary>확인할 것이 있는 자리: 확실하지 않은 칸, 곡을 정하지 못했으면 제목</summary>
 internal static List<Rectangle> Regions(ScanItem item)
 {
  var regions=new List<Rectangle>();
  for(int i=0;i<16;i++)if(!item.Sure[i]&&!item.Shot.Cells[i].Blank)regions.Add(new(CollectionScreen.Columns[i%4]+CollectionScreen.CellLeft,CollectionScreen.ScoreRows[i/4],CollectionScreen.CellWidth,CollectionScreen.CellHeight));
  if(item.Judge.Song==null)regions.Add(new(CollectionScreen.TitleX,CollectionScreen.TitleY,CollectionScreen.TitleWidth,CollectionScreen.TitleHeight));
  return regions;
 }
 static bool Same(Look a,Look b)=>a.Title.Differ(b.Title)<=SignSame&&a.Table.Differ(b.Table)<=SignSame;
 /// <summary>제목·점수표·목록 가운데 하나라도 바뀌었는가. 목록은 옮겨진 뒤에도 0.2~0.5초 동안 미끄러지므로, 멈춘 화면(Settled)과 견줄 때만 "옮겨졌다"의 근거가 된다.</summary>
 static bool Moved(Look before,Look now)=>!Same(before,now)||before.List.Differ(now.List)>ListMoved;
 /// <summary>제목과 표가 이어진 두 번의 캡처에서 같을 때의 화면. 한도까지 같아지지 않으면 마지막 화면과 Still 거짓(그 화면으로 값을 확정하지 않는다).
 /// TitleStill 은 마지막 두 화면의 제목이 같았는가(거짓이면 흔들린 것이 제목이다: 곡도 정하지 않는다)</summary>
 (Look Look,bool Still,bool TitleStill) Stable(Action<Look>? seen=null){var a=See();seen?.Invoke(a);double until=world.Now+StableWait;while(true){world.Wait(Poll);var b=See();seen?.Invoke(b);if(Same(a,b))return(b,true,true);if(world.Now>=until)return(b,false,a.Title.Differ(b.Title)<=SignSame);a=b;}}
 /// <summary>고른 필터가 아직 ALL 인가. 한 화면이 어긋나면 RetakeWait 씩 쉬며 Retakes 번까지 다시 본다(지나가는 빛이나 커서). 끝내 ALL 이 아니면 멈춘다.
 /// 이것을 보지 않으면 읽는 동안 게임에서 Space 를 눌렀을 때 그 필터의 곡만 읽히고 전곡 완료로 끝난다</summary>
 void EnsureAll(Look look)
 {
  for(int i=0;!CollectionScreen.IsAll(look.Frame);i++)
  {
   if(i>=Retakes)throw new Stop(ScanEnd.FilterChanged,$"읽는 동안 고른 필터가 ALL 이 아니게 됐다(그림과 다른 화소 {CollectionScreen.FilterDiffer(look.Frame)})");
   world.Wait(RetakeWait);look=See();
  }
 }
 /// <summary>목록까지 멈춘 화면. 한도까지 멈추지 않으면 마지막 화면과 Still 거짓.</summary>
 (Look Look,bool Still) Settled(){var a=See();double until=world.Now+ListWait;while(true){world.Wait(ListStep);var b=See();if(Same(a,b)&&a.List.Differ(b.List)<=ListStill)return(b,true);if(world.Now>=until){Note($"목록이 멈추지 않은 채 한도: {Diff(a,b)}");return(b,false);}a=b;}}
 /// <summary>화면과 그 화면에서 찾은 고른 줄의 띠. 띠가 보이지 않으면 BarWait 까지 목록이 멈춘 화면을 다시 본다(배경의 그림자가 지나가기를 기다린다). 끝까지 보이지 않으면 띠는 -1.
 /// Still 은 돌려주는 화면이 목록까지 멈춘 화면인가(Settled 의 것. 처음 받은 화면이면 받은 그대로)</summary>
 (Look Look,int Bar,bool Still) WithBar((Look Look,bool Still) seen,string where)
 {
  var (look,still)=seen;int bar=CollectionScreen.Bar(look.Frame);if(bar>=0)return(look,bar,still);
  double begin=world.Now;
  while(bar<0&&world.Now-begin<BarWait){(look,still)=Settled();bar=CollectionScreen.Bar(look.Frame);}
  Note($"{where}: 띠가 보이지 않아 {world.Now-begin:0.00}초 다시 봄, 고른 줄 {bar}");
  return(look,bar,still);
 }
 /// <summary>화면에 곡이 보이는가(고른 줄의 띠가 없을 때 빈 목록인지 가른다). 제목 줄이나 목록에 글자가 ShownInk 화소 넘게 있다</summary>
 static bool Shown(Look look)=>look.Title.Count()>ShownInk||look.List.Count()>ShownInk;
 /// <summary>두 화면의 다른 화소(제목·표·목록). 기록용</summary>
 static string Diff(Look a,Look b)=>$"제목 {a.Title.Differ(b.Title)} 표 {a.Table.Differ(b.Table)} 목록 {a.List.Differ(b.List)}";

 // ── 키 ──
 void Tap(ScanKey key,double press=Press){Check();world.Key(key,true);try{world.Wait(press);}finally{world.Key(key,false);}}
 /// <summary>화면이 바뀌기를 MoveWait 까지 기다린다. list 가 거짓이면 제목과 표만 본다(옮기기 전의 화면이 목록이 미끄러지는 중에 잡힌 것일 때).</summary>
 bool Changed(Look before,bool list){double until=world.Now+MoveWait;while(world.Now<until){world.Wait(Poll);var now=See();if(list?Moved(before,now):!Same(before,now))return true;}return false;}
 /// <summary>한 곡 위로(마지막 곡의 아티스트를 아랫줄에서 읽으려고). 목록이 멈춘 화면과 견준다.</summary>
 bool MoveUp(){var before=Settled().Look;Tap(ScanKey.Up);return Changed(before,true);}
 /// <summary>다음 곡으로. 참이면 옮겨졌고 거짓이면 목록의 끝이다. 끝이 아닌데 옮겨지지 않았으면 멈춘다.
 /// 보통은 제목이나 표가 바뀐 것으로 안다(이름이 다른 곡은 제목이 다르다). careful 이면 목록이 멈추기를 기다렸다 보내고 목록이 바뀐 것도 근거로 삼는다.</summary>
 bool MoveDown(bool careful)
 {
  var before=careful?Settled().Look:See();Tap(ScanKey.Down);
  if(Changed(before,careful))return true;
  var (now,bar,_)=WithBar((See(),true),"목록의 끝");
  if(Trace!=null)Note($"↓ 뒤에 그대로(careful {careful}): 앞 화면과 {Diff(before,now)}, 고른 줄 {bar}, 아랫줄 {(bar<0?-1:CollectionScreen.RowBelow(now.Frame,bar))}");
  if(bar<0)throw new Stop(ScanEnd.NotMoved,"고른 줄을 찾지 못했다");
  if(CollectionScreen.RowBelow(now.Frame,bar)>=CollectionScreen.RowQuiet)throw new Stop(ScanEnd.NotMoved,"아래에 곡이 있는데 옮겨지지 않았다");
  // 아래가 비었다: 끝에서는 더 눌러도 해가 없다. 두 번 더 보내 그래도 그대로면 끝이다
  for(int i=0;i<2;i++){Tap(ScanKey.Down);if(Changed(before,careful)){if(Trace!=null)Note($"다시 누르자 바뀜: 앞 화면과 {Diff(before,See())}");throw new Stop(ScanEnd.NotMoved,"끝으로 보였는데 다시 누르자 옮겨졌다");}}
  return false;
 }
 /// <summary>↑ 를 짧게 거듭 눌러 첫 곡까지 올라간다. 올라가는 동안에는 읽지 않는다(건너뛰거나 키가 빠져도 해가 없다: 더 바뀌지 않을 때까지 누른다).
 /// 첫 곡에서는 ↑ 를 더 눌러도 목록이 돌지 않는다(실제 게임에서 본 것).</summary>
 void GoFirst()
 {
  var last=See();double begin=world.Now,quiet=world.Now;int taps=0,moves=0;
  if(Trace!=null)Note($"첫 곡으로: 시작 화면의 고른 줄 {CollectionScreen.Bar(last.Frame)}");
  while(world.Now-quiet<FirstQuiet)
  {
   if(world.Now-begin>FirstLimit)throw new Stop(ScanEnd.NotFirst,"첫 곡까지 올라가지 못했다");
   double started=world.Now;Tap(ScanKey.Up,FirstPress);taps++;
   var now=See();if(Moved(last,now)){if(Trace!=null&&moves<40)Note($"↑ {taps}번째 뒤 바뀜: {Diff(last,now)}, 고른 줄 {CollectionScreen.Bar(now.Frame)}");last=now;quiet=world.Now;moves++;}
   double rest=FirstCycle-(world.Now-started);if(rest>0)world.Wait(rest);
  }
  var (look,bar,still)=WithBar(Settled(),"첫 곡");
  EnsureAll(look);   // 첫 곡을 찾는 동안 필터가 바뀌지 않았는가
  if(Trace!=null)Note($"↑ {taps}번, 바뀐 것 {moves}번. 멈춘 화면의 고른 줄 {bar}, 윗줄 {(bar<0?-9:CollectionScreen.RowAbove(look.Frame,bar))}, 마지막으로 바뀐 화면과 {Diff(last,look)}");
  // 띠가 없어도 곡이 보이면 빈 목록이 아니다: 첫 곡에 왔는지 모르는 것이다(받아 둔 실제 화면에도 곡의 제목·표는 있고 목록만 비어 있는 한 장이 있다)
  if(bar<0)throw Shown(look)?new Stop(ScanEnd.NotFirst,$"곡이 보이는데 고른 줄을 찾지 못했다(제목 {look.Title.Count()}·목록 {look.List.Count()} 화소)"):new Stop(ScanEnd.Empty,"곡 목록에서 고른 줄을 찾지 못했다");
  // 목록이 멈추지 않은 화면으로는 띠 위에 곡이 없는지 정하지 않는다
  if(!still)throw new Stop(ScanEnd.NotFirst,"목록이 멈추지 않아 첫 곡인지 정하지 못했다");
  int above=CollectionScreen.RowAbove(look.Frame,bar);
  if(above<0||above>=CollectionScreen.RowQuiet)throw new Stop(ScanEnd.NotFirst,"고른 줄 위에 곡이 있다");
 }

 // ── 곡 하나 ──
 ScanItem ReadSong()
 {
  // 보통 곡은 두 번째 화면에서 멈춘다: 그 곡의 칸은 더 읽지 않는다(읽기 속도에 주는 영향을 멈추지 않는 곡으로 한정한다).
  // 셋째 화면이 오면(두 장으로 끝나지 않았다) 들고 있던 앞 두 장의 칸도 그때 읽는다: 점수가 앞 두 장에만 보이는 경우가 있다
  var pool=new ShotPool();int seen=0;var first=new List<Look>(2);
  void Offer(Look look)
  {
   seen++;
   if(seen<=2){first.Add(look);pool.Offer(look,0);return;}
   if(seen==3){foreach(var early in first)pool.KeepClear(early,ClearCells(early.Frame));first.Clear();}
   pool.Offer(look,ClearCells(look.Frame));
  }
  for(int attempt=0;;attempt++)
  {
   var (look,still,titleStill)=Stable(Offer);EnsureAll(look);var shot=reader.Read(look.Frame);var judge=reader.Judge(shot);var sure=CollectionReader.SureCells(shot);
   // 제목과 표가 이어진 두 화면에서 같아지지 않았으면 읽은 값이 아무리 또렷해도 좋은 화면이 아니다
   bool good=still&&!judge.TitleUnclear&&!judge.PatternsDiffer&&sure.All(s=>s)&&(judge.Song!=null||judge.NeedsArtist);
   if(!good&&attempt<Retakes){world.Wait(RetakeWait);continue;}
   // 제목은 또렷한데 빈칸의 자리가 어느 후보와도 맞지 않는다: 앱에 없는 패턴이다(제목이 또렷하지 않은 것은 확인할 것으로 넘긴다).
   // 멈추지 않은 화면에서는 정하지 않는다(확인할 것으로 넘긴다)
   if(still&&!judge.TitleUnclear&&judge.PatternsDiffer){result.LastTitle=shot.Title?.Text;throw new Stop(ScanEnd.UnknownPattern,$"'{shot.Title?.Text}' 의 빈칸 자리가 곡 목록의 패턴과 다르다");}
   if(!still)Note($"화면이 멈추지 않은 채 {Retakes+1}번: '{shot.Title?.Text}' 의 칸을 확인할 것으로 남긴다{(titleStill?"":". 흔들린 것이 제목이라 곡도 정하지 않는다")}");
   // 흔들린 것이 제목이면 마지막에 본 제목으로 곡을 정하지 않는다: 곡 선택 1건과 그 화면을 넘긴다. 제목은 같고 표만 흔들렸으면 곡은 그대로다
   if(!still&&!titleStill){judge.Song=null;judge.NeedsArtist=false;}
   return new ScanItem{Shot=shot,Judge=judge,Sure=sure,Frame=look.Frame,Unsteady=!still,TitleUnsteady=!still&&!titleStill,Tried=pool.Looks.Where(l=>!ReferenceEquals(l,look)).Select(l=>l.Frame).ToList(),Clear=pool.Clear.Where(c=>!ReferenceEquals(c.Look,look)).Select(c=>(c.Look.Frame,c.Clear)).ToList()};
  }
 }
 /// <summary>목록이 멈춘 화면에서 고른 줄의 위(또는 아래) 줄의 아티스트를 읽어 곡을 정한다. 읽지 못하면 그대로 둔다(곡 선택이 확인할 것으로 남는다).</summary>
 void ReadArtist(ScanItem item,int offset)
 {
  var names=item.Judge.Candidates.Select(s=>s.Artist).Distinct().ToList();
  for(int attempt=0;attempt<=Retakes&&item.Artist==null;attempt++)
  {
   if(attempt>0)world.Wait(RetakeWait);
   var (look,bar,still)=WithBar(Settled(),"아티스트");item.ArtistFrame=look.Frame;if(bar<0)break;   // BarWait 까지 기다려도 띠가 없으면 더 보지 않는다
   if(!still)continue;   // 목록이 멈추지 않은 화면으로는 곡을 정하지 않는다(다시 잡고, 끝까지 안 되면 곡 선택이 확인할 것으로 남는다)
   var letters=CollectionScreen.ListLetters(look.Frame);var costs=names.ToDictionary(n=>n,_=>1.0);
   for(int d=-ArtistSearch;d<=ArtistSearch;d++)
   {
    int top=bar+offset+d;if(top<0||top+CollectionScreen.ArtistHeight>CollectionScreen.ListHeight)continue;
    foreach(var (artist,cost) in reader.Artists(letters,top,names))if(cost<costs[artist])costs[artist]=cost;
   }
   var order=costs.OrderBy(c=>c.Value).ToList();
   if(order[0].Value<=ArtistSure&&(order.Count<2||order[1].Value-order[0].Value>=ArtistGap))item.Artist=order[0].Key;
  }
  if(item.Artist!=null)reader.Resolve(item.Shot,item.Judge,item.Artist);
 }
 /// <summary>곡 하나의 결과를 닫는다: 확인할 것을 세고, 있으면 화면을 PNG 로 남기고, 허용 수를 넘으면 멈춘다.</summary>
 void Close(ScanItem item)
 {
  var song=item.Judge.Song;if(song!=null)item.Sure=item.Judge.Sure;
  if(item.Unsteady)for(int i=0;i<16;i++)if(!item.Shot.Cells[i].Blank)item.Sure[i]=false;   // 멈추지 않은 화면의 칸은 모두 확인할 것으로
  // 같은 곡이 두 번 나오면 옮겨지지 않은 화면을 다시 읽었거나 곡을 잘못 정한 것이다: 어느 쪽이든 전곡을 빠짐없이 읽었다고 말할 수 없다
  if(song!=null&&result.Items.Any(i=>i.Judge.Song?.Id==song.Id)){if(Trace!=null&&item.Frame!=null){recent.Enqueue(item.Frame);while(recent.Count>Recent)recent.Dequeue();}throw new Stop(ScanEnd.NotMoved,$"같은 곡이 두 번 읽혔다: {song}");}
  item.Questions=(song==null?1:0)+item.Sure.Count(s=>!s);
  if(item.Questions>0){item.Screen=item.Frame?.ToPng();item.ArtistScreen=item.ArtistFrame?.ToPng();item.TriedScreens=item.Tried!=null&&item.Frame!=null?Pick(item.Tried,item.Clear??new(),item.Frame,Regions(item),Questioned(item)).Select(f=>f.ToPng()).ToList():new();}
  Closing?.Invoke(item);
  item.Frame=null;item.ArtistFrame=null;item.Tried=null;item.Clear=null;
  result.Items.Add(item);result.Questions+=item.Questions;
  if(result.Questions>result.Allowed)throw new Stop(ScanEnd.TooMany,$"확인 필요 {result.Questions}건 / 허용 {result.Allowed}건");
 }
}
