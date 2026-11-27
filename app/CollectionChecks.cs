using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
namespace LadderRecords;
// 개발용 검사(Debug 빌드에만 들어간다): 컬렉션 화면을 읽는 제품의 코드(CollectionReader)에 녹화를 넣어 본다.
//
//   DJMAXLocalRecords.exe --collection-read-check <녹화> <첫 프레임> <끝 프레임> <견줄 read.json> <결과 파일> [rate=black] [cover=N]
//
// 녹화를 ffmpeg 로 풀어 화면 한 장씩 넣고, 곡마다 읽은 16칸을 견줄 값(Python 도구가 녹화에서 따로 읽은 read.json)과 견준다.
//   rate=black  게임의 RATE 표 자리를 검게 칠하고 읽는다. 제품은 RATE 를 읽지 않으므로 결과가 같아야 한다.
//   cover=N     점수가 있는 칸 N 개 위에 마우스 커서 모양을 덮는다. 틀린 값을 확실하다고 하지 않고 확실하지 않은 칸으로 넘겨야 한다.
// 통과 기준: 정상 자료에서 오독 0·누락 0·검수 이관 0. 견줄 값은 개인 기록이라 git 밖에 있다(그 자료가 있는 PC 에서만 돈다).
// 곡을 옮긴 것은 제목 그림이나 칸의 그림이 두 프레임 이어서 달라진 것으로 본다(녹화는 사용자가 키를 누른 채 지나간 것이다. 제품이 키를 보내며 읽는 흐름(CollectionScan)은 이 검사에 들지 않는다).
// 제목이 둘 이상의 곡을 가리키면(이름이 같은 곡) 제품의 판독은 곡을 정하지 않는다: 이 검사는 두 곡 뒤의 화면에서 그 곡의 줄(고른 줄의 둘째 윗줄)의 아티스트를 읽어 정한다.
// 예상한 목록에 없는 곡이나 떨어져서 다시 나온 곡이 하나라도 있으면 실패다. 두 프레임 확인은 서로 다른 두 프레임이 같은 값을 냈을 때만 센다.
[SupportedOSPlatform("windows10.0.19041")]
static class CollectionChecks
{
 const int TitleChanged=60,CellChanged=25;
 sealed record FrameRead(int Index,Mask Title,Mask[] Cells,Mask Best,Mask Category,byte[]? List=null);
 sealed record Visit(int Start,int End,SongJudge Judge,ShotRead Shot,bool Twice,byte[]? List=null);
 // 누른 채 지나가는 녹화에서 고른 줄의 윗줄들이 놓이는 자리(목록 그림의 y. 줄 사이는 62): 바로 윗줄 507, 그 위로 62 씩. 목록이 움직이는 중이라 위아래로 반 줄까지 훑는다
 const int RowAbove1=507,RowSearch=31;const double ArtistSure=0.25,ArtistGap=0.15;

 public static void Read(Catalog catalog,string video,int first,int last,string expectedPath,string reportPath,string[] options)
 {
  bool blackRate=options.Contains("rate=black");int cover=options.Where(o=>o.StartsWith("cover=")).Select(o=>int.Parse(o[6..])).FirstOrDefault();
  var reader=new CollectionReader(catalog,CollectionData.Load());
  var expected=JsonSerializer.Deserialize<List<JsonElement>>(File.ReadAllText(expectedPath))!.ToDictionary(e=>e.GetProperty("id").GetString()!,e=>e.GetProperty("score"));
  var covered=new HashSet<(int Frame,int Cell)>();int coverLeft=cover,numbersSeen=0,spot=0;
  var visits=new List<Visit>();var current=new List<FrameRead>();int pending=0;
  void Close(){if(current.Count>0)visits.Add(Decide(reader,current));current=new();}
  foreach(var (index,bytes) in Frames(video,first,last))
  {
   if(blackRate)Fill(bytes,925,575,500,120,0,0,0);
   var frame=new CollectionFrame(bytes);
   if(coverLeft>0)   // 여섯 번째로 만나는 점수 칸마다 커서를 덮는다(한 곡에 머무는 프레임 모두에 같은 칸)
   {
    for(int cell=0;cell<16&&coverLeft>0;cell++)
    {
     var read=reader.Cell(CollectionScreen.Cell(frame,cell/4,cell%4));if(!read.Number)continue;
     bool again=covered.Contains((index-1,cell));
     if(!again&&++numbersSeen%6!=0)continue;
     if(!again){spot++;coverLeft--;}
     Cursor(bytes,CollectionScreen.Columns[cell%4]+CollectionScreen.CellLeft+4+spot*7%48,CollectionScreen.ScoreRows[cell/4]-4+spot*3%10);covered.Add((index,cell));   // 커서의 자리를 칸마다 바꾼다(숫자 여섯의 여러 곳)
    }
   }
   var now=new FrameRead(index,CollectionScreen.Title(frame),Enumerable.Range(0,16).Select(i=>CollectionScreen.Cell(frame,i/4,i%4)).ToArray(),CollectionScreen.Best(frame),CollectionScreen.Category(frame),CollectionScreen.ListLow(frame));
   if(current.Count==0){current.Add(now);continue;}
   if(Changed(current[0],now)){if(++pending>=2){var moved=current[^1];current.RemoveAt(current.Count-1);Close();current.Add(moved);current.Add(now);pending=0;continue;}}else pending=0;
   current.Add(now);
  }
  Close();
  // 잡음으로 한 곡이 둘로 나뉜 방문을 합친다: 이웃한 두 방문이 같은 곡이면 한 곡이다(더 오래 머문 쪽의 값을 쓴다).
  // 곡을 아직 정하지 않은 방문(이름이 같은 곡)은 제목과 16칸이 모두 같을 때만 합친다(이름이 같은 두 곡은 붙어 나오고 표가 다르다)
  static bool SameSong(Visit a,Visit b)=>a.Judge.Song!=null?a.Judge.Song.Id==b.Judge.Song?.Id:b.Judge.Song==null&&a.Shot.Title?.Text==b.Shot.Title?.Text&&a.Shot.Title!=null&&a.Shot.Cells.Select(c=>c.Text).SequenceEqual(b.Shot.Cells.Select(c=>c.Text));
  var songs=new List<Visit>();
  foreach(var v in visits){if(songs.Count>0&&SameSong(songs[^1],v)){var a=songs[^1];var keep=v.End-v.Start>a.End-a.Start?v:a;songs[^1]=keep with{Start=a.Start,End=v.End};}else songs.Add(v);}
  // 이름이 같은 곡: 그 곡들을 지나 세 곡 뒤의 화면에서 윗줄들의 아티스트를 읽어 정한다(방금 지나간 줄에는 색 띠가 남는다. 안 되면 두 곡 뒤, 한 곡 뒤).
  // 이름이 같은 곡은 붙어 나온다: 붙어 있는 방문을 한 묶음으로 보고, 줄의 자리(목록이 움직이는 중이라 위아래로 어긋난다)와 줄마다의 아티스트를 함께 맞춘다.
  int byArtist=0;double artistMax=0,otherMin=1;
  for(int i=0;i<songs.Count;)
  {
   if(!songs[i].Judge.NeedsArtist){i++;continue;}
   int j=i;while(j+1<songs.Count&&songs[j+1].Judge.NeedsArtist&&songs[j+1].Shot.Title?.Text==songs[i].Shot.Title?.Text)j++;
   int from=i,to=j;i=j+1;
   int count=to-from+1;var names=songs[from].Judge.Candidates.Select(s=>s.Artist).Distinct().ToList();if(names.Count<count)continue;
   foreach(int ahead in new[]{3,2,1})
   {
   if(to+ahead>=songs.Count||songs[to+ahead].List==null)continue;
   var letters=CollectionScreen.ListLetters(songs[to+ahead].List!);int row=RowAbove1-62*(ahead-1);
   // 줄의 어긋남과 줄마다의 아티스트를 함께 고른다: 묶음의 마지막 방문이 row, 그 앞의 방문은 62 씩 위다
   double bestTotal=double.MaxValue,secondTotal=double.MaxValue;string[]? picked=null;double worst=0;
   for(int shift=-RowSearch;shift<=RowSearch;shift++)
   {
    var costs=new List<Dictionary<string,double>>();bool inside=true;
    for(int m=0;m<count;m++){int y=row+shift-62*(count-1-m);if(y<0||y+CollectionScreen.ArtistHeight>CollectionScreen.ListHeight){inside=false;break;}costs.Add(reader.Artists(letters,y,names).ToDictionary(c=>c.Artist,c=>c.Cost));}
    if(!inside)continue;
    foreach(var order in Orders(names,count))
    {
     double total=0,high=0;for(int m=0;m<count;m++){double c=costs[m][order[m]];total+=c;high=Math.Max(high,c);}
     if(total<bestTotal){if(picked==null||!picked.SequenceEqual(order))secondTotal=bestTotal;bestTotal=total;picked=order;worst=high;}
     else if(total<secondTotal&&!picked!.SequenceEqual(order))secondTotal=total;
    }
   }
   if(picked==null||worst>ArtistSure||secondTotal-bestTotal<ArtistGap)continue;
   for(int m=0;m<count;m++){var v=songs[from+m];reader.Resolve(v.Shot,v.Judge,picked[m]);if(v.Judge.Song!=null)byArtist++;}
   artistMax=Math.Max(artistMax,worst);if(secondTotal<double.MaxValue)otherMin=Math.Min(otherMin,secondTotal-bestTotal);
   break;
   }
  }

  // 누른 채 지나가는 압축된 녹화에서는 목록이 움직이는 중이라 짧은 아티스트(hYO 같은 것)가 읽히지 않는 쌍이 있다(멈춘 실제 화면에서는 읽힌다: --collection-artist-check).
  // 그런 곡은 빈칸의 자리·카테고리로 한 곡이 남을 때 그 곡으로 값을 견주고, 아티스트로 정한 곡과 따로 센다. 제품의 판독은 이런 곡을 스스로 정하지 않는다.
  // 아티스트가 또렷하게 읽혔는데 빈칸의 자리·카테고리와 어긋난 곡(ArtistDiffers)은 여기서 덮지 않는다: 정하지 못한 곡으로 남아 검사가 실패한다
  int bySupport=0,conflicts=songs.Count(v=>v.Judge.ArtistDiffers);foreach(var v in songs)if(v.Judge.NeedsArtist&&v.Judge.Song==null&&!v.Judge.ArtistDiffers&&v.Judge.Likely.Count==1){reader.Resolve(v.Shot,v.Judge,v.Judge.Likely[0].Artist);if(v.Judge.Song!=null)bySupport++;}
  var report=new StringBuilder();int wrong=0,unsureSongs=0,unsureCells=0,equal=0,coveredWrong=0,coveredUnsure=0,once=0,extra=0,repeated=0;
  double titleMax=0;int costMax=0,marginMin=int.MaxValue;var clean=new List<(int Cost,int Margin,string Text)>();var seen=new HashSet<string>();var lines=new List<string>();
  foreach(var v in songs)
  {
   var j=v.Judge;
   if(j.Song==null){unsureSongs++;lines.Add($"곡을 정하지 못함 {v.Start}~{v.End}: 제목 '{v.Shot.Title?.Text}' {v.Shot.Title?.Cost:0.000}, 후보 {string.Join(" / ",j.Candidates.Select(s=>s.ToString()))}{(j.TitleUnclear?" (제목이 또렷하지 않음)":"")}{(j.NeedsArtist?" (아티스트로 정하지 못함)":"")}{(j.ArtistDiffers?" (아티스트와 빈칸의 자리가 어긋남)":"")}{(j.PatternsDiffer?" (빈칸의 자리가 다름)":"")}");continue;}
   if(!v.Twice)once++;
   if(!seen.Add(j.Song.Id)){repeated++;lines.Add($"떨어져서 다시 나온 곡 {j.Song} {v.Start}~{v.End}");}
   titleMax=Math.Max(titleMax,v.Shot.Title!.Cost);
   if(!expected.TryGetValue(j.Song.Id,out var want)){extra++;lines.Add($"견줄 값에 없는 곡 {j.Song} {v.Start}~{v.End}");continue;}
   bool same=true;
   for(int i=0;i<16;i++)
   {
    string key=$"{CollectionScreen.Buttons[i%4]}B {CollectionScreen.Patterns[i/4]}",value=want.GetProperty(key).GetString()!;var cell=v.Shot.Cells[i];
    bool hidden=Enumerable.Range(v.Start,v.End-v.Start+1).Any(f=>covered.Contains((f,i)));
    if(hidden)lines.Add($"가린 칸 {j.Song} {key}: '{cell.Text}' 다른 화소 {cell.Cost} 여유 {cell.Margin} → {(cell.Text==value?"맞음":"틀림 ("+value+")")}{(j.Sure[i]?"":", 확실하지 않음")}");
    if(!j.Sure[i]){unsureCells++;if(hidden)coveredUnsure++;else lines.Add($"확실하지 않은 칸 {j.Song} {key}: '{cell.Text}' 다른 화소 {cell.Cost} 여유 {cell.Margin} (견줄 값 '{value}')");same=false;continue;}
    if(cell.Text!=value){wrong++;same=false;if(hidden)coveredWrong++;lines.Add($"틀린 값 {j.Song} {key}: '{cell.Text}' ≠ '{value}'{(hidden?" (가린 칸)":"")}");continue;}
    if(cell.Number){costMax=Math.Max(costMax,cell.Cost);marginMin=Math.Min(marginMin,cell.Margin);clean.Add((cell.Cost,cell.Margin,cell.Text));}
   }
   if(same)equal++;
  }
  var missing=expected.Keys.Where(id=>!seen.Contains(id)).Select(id=>catalog.Songs.First(s=>s.Id==id).ToString()).ToList();
  report.AppendLine($"녹화 {Path.GetFileName(video)} 프레임 {first}~{last}{(blackRate?" (RATE 표를 검게 칠함)":"")}{(cover>0?$" (점수 칸 {cover}개에 커서를 덮음)":"")}");
  report.AppendLine($"방문 {visits.Count} → 곡 {songs.Count}. 정한 곡 {seen.Count}, 견줄 값의 곡 {expected.Count}, 16칸이 모두 같고 확실한 곡 {equal}");
  report.AppendLine($"틀린 값 {wrong}, 빠진 곡 {missing.Count}, 정하지 못한 곡 {unsureSongs}, 확실하지 않은 칸 {unsureCells}, 서로 다른 두 프레임이 같은 값을 내지 못한 곡 {once}, 견줄 값에 없는 곡 {extra}, 떨어져서 다시 나온 곡 {repeated}");
  report.AppendLine($"이름이 같은 곡: 아티스트로 정한 곡 {byArtist}{(byArtist>0?$"(고른 아티스트의 어긋난 몫 최대 {artistMax:0.000}, 다르게 골랐을 때와의 차 최소 {otherMin:0.00})":"")}, 아티스트를 읽지 못해 빈칸의 자리·카테고리로 견준 곡 {bySupport}, 읽은 아티스트가 빈칸의 자리·카테고리와 어긋난 곡 {conflicts}");
  if(cover>0)report.AppendLine($"가린 칸: 확실하지 않은 것으로 넘어간 칸 {coveredUnsure}, 틀린 값을 확실하다고 한 칸 {coveredWrong}, 가렸는데도 맞게 읽힌 칸 {cover-coveredUnsure-coveredWrong}");
  report.AppendLine($"맞은 것의 범위: 제목의 어긋난 몫 최대 {titleMax:0.000}, 점수 칸의 다른 화소 최대 {costMax}, 점수 칸의 여유 최소 {(marginMin==int.MaxValue?0:marginMin)}");
  if(clean.Count>0)report.AppendLine($"맞게 읽힌 점수 칸 {clean.Count}: 다른 화소가 10 넘는 칸 {clean.Count(c=>c.Cost>10)}, 20 넘는 칸 {clean.Count(c=>c.Cost>20)}, 40 넘는 칸 {clean.Count(c=>c.Cost>40)}. 가장 큰 것: {string.Join(", ",clean.OrderByDescending(c=>c.Cost).Take(12).Select(c=>$"{c.Text}({c.Cost})"))}. 여유가 가장 작은 것: {string.Join(", ",clean.OrderBy(c=>c.Margin).Take(12).Select(c=>$"{c.Text}({c.Margin})"))}");
  bool pass=wrong==0&&missing.Count==0&&unsureSongs==0&&extra==0&&repeated==0&&(cover>0?coveredWrong==0:unsureCells==0&&once==0);
  report.AppendLine(cover>0?(coveredWrong==0?"통과: 가린 칸에서 틀린 값을 쓰지 않았다":"실패: 가린 칸에서 틀린 값을 확실하다고 했다"):pass?"통과: 오독 0, 누락 0, 검수 이관 0":"실패");
  foreach(string m in missing.Take(40))report.AppendLine("빠진 곡 "+m);
  foreach(string l in lines.Take(200))report.AppendLine(l);
  File.WriteAllText(reportPath,report.ToString());
  if(!pass)Environment.ExitCode=2;
 }

 /// <summary>목록이 멈춘 화면 한 장(1920×1080 PNG)에서 후보 아티스트마다 가장 잘 겹치는 줄을 찾고, 그 줄에 다른 후보를 댔을 때의 값을 적는다.
 ///   DJMAXLocalRecords.exe --collection-artist-check 화면.png 결과 파일 아티스트 …
 /// 제품은 고른 줄의 띠를 기준으로 정한 자리(CollectionScreen.ArtistAbove·ArtistBelow) 둘레만 읽는다. 여기서는 목록 전체를 훑는다.</summary>
 public static void Artists(Catalog catalog,string picture,string reportPath,string[] names)
 {
  var reader=new CollectionReader(catalog,CollectionData.Load());
  using var bitmap=new Bitmap(picture);var letters=CollectionScreen.ListLetters(new CollectionFrame(bitmap));
  var report=new StringBuilder();
  foreach(string name in names)
  {
   double best=1;int at=-1;
   for(int y=0;y<=CollectionScreen.ListHeight-CollectionScreen.ArtistHeight;y++)
   {
    if(CollectionScreen.ArtistBand(letters,y).Count()<40)continue;
    double cost=reader.Artists(letters,y,new[]{name})[0].Cost;if(cost<best){best=cost;at=y;}
   }
   var others=at<0?new():reader.Artists(letters,at,names).Where(a=>a.Artist!=name).ToList();
   report.AppendLine($"{name}: 어긋난 몫 {best:0.000}, 줄의 y {at}. 그 줄에서 다른 후보: {string.Join(", ",others.Select(o=>$"{o.Artist} {o.Cost:0.00}"))}");
  }
  File.WriteAllText(reportPath,report.ToString());
 }

 // ── 화면 받아 두기 ──
 // 사용자가 게임에서 직접 곡을 넘기는 동안 앱의 캡처(MainForm.CaptureFrame. 제품이 쓰는 그 화면 복사)로 화면을 받아 폴더에 둔다. 키를 보내지 않는다.
 //   DJMAXLocalRecords.exe --collection-capture <폴더> [초]
 // 게임 창이 앞에 오면 받기 시작하고, 다른 창으로 바꾸거나 정한 시간(기본 300초)이 지나면 끝난다. 준 폴더 아래에 실행마다 새 폴더(날짜-시각)를 만들어 둔다: 앞의 화면과 섞이지 않는다.
 // 제목이나 점수표가 바뀔 때마다 제목 줄·표·목록의 조각을 PNG 로 남기고(NNNN-title·table·list), 화면이 0.5초 넘게 그대로이면 화면 전체도 한 장 남긴다(NNNN-still).
 // capture.txt 에 받은 때(ms)를 적는다. 압축되지 않은 실제 화면으로 숫자 표본을 다시 만들고, 목록이 멈춘 화면에서 줄의 자리와 아티스트 읽기를 확인하는 데 쓴다.
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
 public static void Capture(string folder,int seconds)
 {
  var game=Process.GetProcessesByName("DJMAX RESPECT V").FirstOrDefault(p=>p.MainWindowHandle!=IntPtr.Zero);
  if(game==null){MessageBox.Show("게임 창을 찾지 못했습니다. 게임을 켠 뒤 다시 실행해 주세요.","화면 받아 두기");return;}
  var handle=game.MainWindowHandle;string? problem=null;
  var wait=Stopwatch.StartNew();while(GetForegroundWindow()!=handle){if(wait.Elapsed.TotalSeconds>120){MessageBox.Show("2분 동안 게임 창이 앞에 오지 않아 끝냈습니다.","화면 받아 두기");return;}Thread.Sleep(100);}
  int away=0;
  var (saved,captures,stills,where)=CaptureLoop(folder,seconds,()=>
  {
   if(GetForegroundWindow()!=handle){if(++away>=10)return(null,true);Thread.Sleep(100);return(null,false);}   // 1초쯤 다른 창이 앞에 있으면 끝낸다
   away=0;
   using var bitmap=MainForm.CaptureFrame(handle,out var size);
   if(size.Width!=CollectionFrame.Width||size.Height!=CollectionFrame.Height){problem=$"게임 화면의 크기가 1920×1080 이 아닙니다({size.Width}×{size.Height}).";return(null,true);}
   return(new CollectionFrame(bitmap),false);
  });
  MessageBox.Show(problem??$"끝났습니다. 화면을 {captures}번 잡아 {saved}번 남겼고, 멈춘 화면을 {stills}장 남겼습니다.\n\n{where}","화면 받아 두기");
 }
 /// <summary>녹화를 화면 대신 넣어 받아 두기의 흐름을 본다(게임 없이). 프레임을 건너뛰어 실제 캡처의 간격을 흉내 낸다.</summary>
 public static void CaptureFromVideo(string video,int first,int last,int step,string folder)
 {
  using var frames=Frames(video,first,last).GetEnumerator();
  CaptureLoop(folder,3600,()=>{for(int i=0;i<step;i++)if(!frames.MoveNext())return(null,true);return(new CollectionFrame((byte[])frames.Current.Bytes.Clone()),false);},step*1000.0/60);
 }
 /// <summary>받아 둔 화면(--collection-capture 의 폴더)을 제품의 코드로 읽어 견줄 값과 견준다. 남긴 화면마다 한 번 읽는다(받아 두기는 바뀐 첫 화면만 남긴다).
 ///   DJMAXLocalRecords.exe --collection-read-capture 폴더 견줄 read.json 결과 파일</summary>
 public static void ReadCapture(Catalog catalog,string folder,string expectedPath,string reportPath)
 {
  var reader=new CollectionReader(catalog,CollectionData.Load());
  var expected=JsonSerializer.Deserialize<List<JsonElement>>(File.ReadAllText(expectedPath))!.ToDictionary(e=>e.GetProperty("id").GetString()!,e=>e.GetProperty("score"));
  var lines=new List<string>();var clean=new List<(int Cost,int Margin,string Text)>();var seen=new HashSet<string>();var good=new List<object>();
  int shots=0,decided=0,undecided=0,equal=0,wrong=0,unsure=0,needArtist=0;double titleMax=0;
  foreach(string titlePath in Directory.GetFiles(folder,"*-title.png").OrderBy(f=>f))
  {
   string number=Path.GetFileName(titlePath)[..4];shots++;
   var bytes=new byte[CollectionFrame.Width*CollectionFrame.Height*4];
   Paste(bytes,titlePath,605,262);Paste(bytes,Path.Combine(folder,number+"-table.png"),605,455);Paste(bytes,Path.Combine(folder,number+"-list.png"),CollectionScreen.ListX,CollectionScreen.ListY);
   var shot=reader.Read(new CollectionFrame(bytes));var judge=reader.Judge(shot);
   // 이름이 같은 곡은 제품의 판독이 정하지 않는다. 받아 둔 화면은 바뀐 첫 장뿐이라 아티스트의 줄이 없다: 여기서는 빈칸의 자리·카테고리로 하나가 남을 때 그 곡으로 값을 견주고 따로 센다(아티스트는 --collection-artist-check 로 본다)
   if(judge.NeedsArtist&&judge.Likely.Count==1){needArtist++;reader.Resolve(shot,judge,judge.Likely[0].Artist);}
   if(judge.Song==null){undecided++;lines.Add($"{number} 곡을 정하지 못함: 제목 '{shot.Title?.Text}' {shot.Title?.Cost:0.000}, 후보 {string.Join(" / ",judge.Candidates.Select(s=>s.ToString()))}{(judge.TitleUnclear?" (제목이 또렷하지 않음)":"")}{(judge.NeedsArtist?" (아티스트가 필요함)":"")}{(judge.PatternsDiffer?" (빈칸의 자리가 다름)":"")}");continue;}
   decided++;seen.Add(judge.Song.Id);titleMax=Math.Max(titleMax,shot.Title!.Cost);
   if(!expected.TryGetValue(judge.Song.Id,out var want)){lines.Add($"{number} 견줄 값에 없는 곡 {judge.Song}");continue;}
   bool same=true;
   for(int i=0;i<16;i++)
   {
    string key=$"{CollectionScreen.Buttons[i%4]}B {CollectionScreen.Patterns[i/4]}",value=want.GetProperty(key).GetString()!;var cell=shot.Cells[i];
    if(!judge.Sure[i]){unsure++;same=false;lines.Add($"{number} 확실하지 않은 칸 {judge.Song} {key}: '{cell.Text}' 다른 화소 {cell.Cost} 여유 {cell.Margin} (견줄 값 '{value}')");continue;}
    if(cell.Text!=value){wrong++;same=false;lines.Add($"{number} 다른 값 {judge.Song} {key}: '{cell.Text}' ≠ '{value}' 다른 화소 {cell.Cost} 여유 {cell.Margin}");continue;}
    if(cell.Number)clean.Add((cell.Cost,cell.Margin,cell.Text));
   }
   if(same){equal++;good.Add(new{n=number,id=judge.Song.Id,best=shot.Best,score=Enumerable.Range(0,16).ToDictionary(i=>$"{CollectionScreen.Buttons[i%4]}B {CollectionScreen.Patterns[i/4]}",i=>shot.Cells[i].Text)});}
  }
  // 16칸이 견줄 값과 모두 같고 확실한 화면의 목록. tools\build_collection_data.py 가 이것으로 받아 둔 화면에서 숫자 표본을 만든다
  File.WriteAllText(Path.ChangeExtension(reportPath,".json"),JsonSerializer.Serialize(good));
  var report=new StringBuilder();
  report.AppendLine($"받아 둔 화면 {shots}: 곡을 정한 화면 {decided}(서로 다른 곡 {seen.Count}), 정하지 못한 화면 {undecided}, 16칸이 모두 같고 확실한 화면 {equal}");
  report.AppendLine($"견줄 값과 다른 칸 {wrong}, 확실하지 않은 칸 {unsure}. 제목의 어긋난 몫 최대 {titleMax:0.000}. 이름이 같은 곡이라 아티스트가 필요한 화면 {needArtist}(빈칸의 자리·카테고리로 남은 한 곡으로 견줬다)");
  if(clean.Count>0)report.AppendLine($"맞게 읽힌 점수 칸 {clean.Count}: 다른 화소 0 인 칸 {clean.Count(c=>c.Cost==0)}, 5 넘는 칸 {clean.Count(c=>c.Cost>5)}, 10 넘는 칸 {clean.Count(c=>c.Cost>10)}, 20 넘는 칸 {clean.Count(c=>c.Cost>20)}, 40 넘는 칸 {clean.Count(c=>c.Cost>40)}. 가장 큰 것: {string.Join(", ",clean.OrderByDescending(c=>c.Cost).Take(10).Select(c=>$"{c.Text}({c.Cost})"))}. 여유가 가장 작은 것: {string.Join(", ",clean.OrderBy(c=>c.Margin).Take(10).Select(c=>$"{c.Text}({c.Margin})"))}");
  foreach(string l in lines.Take(400))report.AppendLine(l);
  File.WriteAllText(reportPath,report.ToString());
 }
 static void Paste(byte[] bytes,string path,int x0,int y0)
 {
  using var bitmap=new Bitmap(path);var data=bitmap.LockBits(new Rectangle(0,0,bitmap.Width,bitmap.Height),System.Drawing.Imaging.ImageLockMode.ReadOnly,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
  try{for(int y=0;y<bitmap.Height;y++)System.Runtime.InteropServices.Marshal.Copy(data.Scan0+y*data.Stride,bytes,((y0+y)*CollectionFrame.Width+x0)*4,bitmap.Width*4);}finally{bitmap.UnlockBits(data);}
 }
 static (int Saved,int Captures,int Stills,string Folder) CaptureLoop(string parent,int seconds,Func<(CollectionFrame? Frame,bool Stop)> grab,double fakeStepMs=0)
 {
  string folder=Path.Combine(parent,DateTime.Now.ToString("yyyyMMdd-HHmmss"));for(int n=2;Directory.Exists(folder);n++)folder=Path.Combine(parent,DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+n);
  Directory.CreateDirectory(folder);
  var queue=new System.Collections.Concurrent.BlockingCollection<(string Name,byte[] Bytes,int Width,int Height)>(120);
  var writer=Task.Run(()=>{foreach(var item in queue.GetConsumingEnumerable())SavePng(Path.Combine(folder,item.Name),item.Bytes,item.Width,item.Height);});
  var log=new StringBuilder("번호\t때(ms)\t종류\n");var clock=Stopwatch.StartNew();FrameRead? last=null;Mask? lastList=null;
  int saved=0,captures=0,stills=0;double changedAt=0,now=0;bool still=true;
  while(fakeStepMs>0||clock.Elapsed.TotalSeconds<seconds)
  {
   var (frame,stop)=grab();if(stop)break;if(frame==null)continue;
   now=fakeStepMs>0?captures*fakeStepMs:clock.Elapsed.TotalMilliseconds;captures++;
   var read=new FrameRead(saved,CollectionScreen.Title(frame),Enumerable.Range(0,16).Select(i=>CollectionScreen.Cell(frame,i/4,i%4)).ToArray(),CollectionScreen.Best(frame),CollectionScreen.Category(frame));
   var list=CollectionScreen.ListBright(frame);
   if(lastList==null||list.Differ(lastList)>50)changedAt=now;   // 목록이 아직 움직인다
   lastList=list;
   if(last==null||Changed(last,read))
   {
    queue.Add(($"{saved:0000}-title.png",Crop(frame,605,262,830,72),830,72));queue.Add(($"{saved:0000}-table.png",Crop(frame,605,455,820,237),820,237));
    queue.Add(($"{saved:0000}-list.png",Crop(frame,CollectionScreen.ListX,CollectionScreen.ListY,CollectionScreen.ListWidth,CollectionScreen.ListHeight),CollectionScreen.ListWidth,CollectionScreen.ListHeight));
    log.Append($"{saved:0000}\t{now:0}\t바뀜\n");last=read;saved++;changedAt=now;still=false;
   }
   else if(!still&&now-changedAt>500)
   {
    queue.Add(($"{saved-1:0000}-still.png",(byte[])frame.Bytes.Clone(),CollectionFrame.Width,CollectionFrame.Height));log.Append($"{saved-1:0000}\t{now:0}\t멈춤\n");still=true;stills++;
   }
  }
  queue.CompleteAdding();writer.Wait();
  log.Append($"잡은 횟수 {captures}, 남긴 횟수 {saved}, 멈춘 화면 {stills}, 걸린 시간 {now/1000:0.0}초, 잡는 간격 평균 {(captures>1?now/(captures-1):0):0}ms\n");
  File.WriteAllText(Path.Combine(folder,"capture.txt"),log.ToString());
  return(saved,captures,stills,folder);
 }
 static byte[] Crop(CollectionFrame frame,int x0,int y0,int width,int height){var bytes=new byte[width*height*4];for(int y=0;y<height;y++)Buffer.BlockCopy(frame.Bytes,((y0+y)*CollectionFrame.Width+x0)*4,bytes,y*width*4,width*4);return bytes;}
 static void SavePng(string path,byte[] bgra,int width,int height)
 {
  using var bitmap=new Bitmap(width,height,System.Drawing.Imaging.PixelFormat.Format32bppRgb);
  var data=bitmap.LockBits(new Rectangle(0,0,width,height),System.Drawing.Imaging.ImageLockMode.WriteOnly,System.Drawing.Imaging.PixelFormat.Format32bppRgb);
  try{for(int y=0;y<height;y++)System.Runtime.InteropServices.Marshal.Copy(bgra,y*width*4,data.Scan0+y*data.Stride,width*4);}finally{bitmap.UnlockBits(data);}
  bitmap.Save(path,System.Drawing.Imaging.ImageFormat.Png);
 }

 /// <summary>이름들 가운데 count 개를 차례로 고르는 모든 방법(같은 이름을 두 번 쓰지 않는다).</summary>
 static IEnumerable<string[]> Orders(List<string> names,int count)
 {
  if(count==0){yield return Array.Empty<string>();yield break;}
  foreach(string first in names)foreach(var rest in Orders(names.Where(n=>n!=first).ToList(),count-1))yield return new[]{first}.Concat(rest).ToArray();
 }
 /// <summary>한 곡에 머문 프레임들에서 읽은 값을 정한다. 서로 다른 두 프레임이 같은 값(제목과 16칸)을 내야 한다(제품의 "두 번 잡아 같을 때"). 프레임이 하나뿐이면 두 번 확인한 것이 아니다.
 /// 같은 값을 낸 프레임 가운데 표본과 다른 화소가 가장 적은 것을 쓴다: 누른 채 지나가는 녹화는 곡이 바뀐 직후의 프레임이 거칠게 압축된다.</summary>
 static Visit Decide(CollectionReader reader,List<FrameRead> frames)
 {
  var reads=frames.Select(f=>(Frame:f,Shot:new ShotRead(f.Title,reader.Title(f.Title),f.Cells.Select(reader.Cell).ToArray(),reader.Best(f.Best),f.Category)))
   .Select(r=>(r.Frame,r.Shot,Key:r.Shot.Title?.Text+"|"+string.Join(",",r.Shot.Cells.Select(c=>c.Text)),Cost:r.Shot.Cells.Sum(c=>c.Cost))).ToList();
  var agreed=reads.GroupBy(r=>r.Key).Where(g=>g.Count()>=2).OrderByDescending(g=>g.Count()).FirstOrDefault();
  var pick=(agreed??reads.AsEnumerable()).OrderBy(r=>r.Cost).First();
  return new(frames[0].Index,frames[^1].Index,reader.Judge(pick.Shot),pick.Shot,agreed!=null,pick.Frame.List);
 }
 static bool Changed(FrameRead a,FrameRead b){if(a.Title.Differ(b.Title)>=TitleChanged)return true;for(int i=0;i<16;i++)if(a.Cells[i].Differ(b.Cells[i])>=CellChanged)return true;return false;}

 /// <summary>녹화의 프레임을 차례로 낸다(BGRA 1920×1080). 같은 배열을 다시 쓰므로 다음 프레임을 받기 전에 다 써야 한다.</summary>
 static IEnumerable<(int Index,byte[] Bytes)> Frames(string video,int first,int last)
 {
  var info=new ProcessStartInfo("ffmpeg"){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true};
  foreach(string a in new[]{"-v","error","-i",video,"-vf",$"select=between(n\\,{first}\\,{last}),scale=1920:1080","-fps_mode","passthrough","-f","rawvideo","-pix_fmt","bgra","-"})info.ArgumentList.Add(a);
  using var process=Process.Start(info)??throw new Exception("ffmpeg 을 시작하지 못했습니다.");
  process.ErrorDataReceived+=(_,_)=>{};process.BeginErrorReadLine();
  var stream=process.StandardOutput.BaseStream;var buffer=new byte[CollectionFrame.Width*CollectionFrame.Height*4];
  for(int index=first;index<=last;index++)
  {
   int got=0;while(got<buffer.Length){int n=stream.Read(buffer,got,buffer.Length-got);if(n<=0)break;got+=n;}
   if(got<buffer.Length)yield break;
   yield return(index,buffer);
  }
 }
 static void Fill(byte[] bytes,int x0,int y0,int width,int height,byte b,byte g,byte r){for(int y=y0;y<y0+height;y++)for(int x=x0;x<x0+width;x++){int i=(y*CollectionFrame.Width+x)*4;bytes[i]=b;bytes[i+1]=g;bytes[i+2]=r;}}
 /// <summary>마우스 커서 모양(흰 화살표에 검은 테두리)을 그린다. 끝이 (x,y).</summary>
 internal static void Cursor(byte[] bytes,int x,int y)
 {
  string[] shape={"X","XX","X.X","X..X","X...X","X....X","X.....X","X......X","X.......X","X........X","X.....XXXXX","X..X..X","X.X X..X","XX  X..X","X    X..X","     X..X","      XX"};
  for(int row=0;row<shape.Length;row++)for(int col=0;col<shape[row].Length;col++){char c=shape[row][col];if(c==' ')continue;byte v=c=='X'?(byte)0:(byte)255;int i=((y+row)*CollectionFrame.Width+x+col)*4;bytes[i]=v;bytes[i+1]=v;bytes[i+2]=v;}
 }

 /// <summary>받아 둔 멈춘 화면(*-still.png)으로 화면 한 장을 읽는 일의 부분마다 걸리는 시간을 잰다(속도 줄이기. 다섯 번 되풀이한 평균, ms).
 ///   DJMAXLocalRecords.exe --collection-read-timing <폴더> <결과 파일></summary>
 public static void ReadTiming(Catalog catalog,string folder,string reportPath)
 {
  var data=CollectionData.Load();var reader=new CollectionReader(catalog,data);
  var frames=Directory.GetFiles(folder,"*-still.png").OrderBy(f=>f).Select(f=>{using var b=new Bitmap(f);return b.Width==CollectionFrame.Width&&b.Height==CollectionFrame.Height?new CollectionFrame(b):null;}).Where(f=>f!=null).Cast<CollectionFrame>().ToList();
  var parts=new Dictionary<string,double>();var watch=new Stopwatch();
  void Time(string name,Action act){watch.Restart();act();parts[name]=parts.GetValueOrDefault(name)+watch.Elapsed.TotalMilliseconds;}
  for(int round=0;round<5;round++)foreach(var f in frames)
  {
   Mask? title=null;Time("제목 그림",()=>title=CollectionScreen.Title(f));
   Time("제목 맞히기",()=>reader.Title(title!));
   var cells=new Mask[16];Time("칸 그림 16",()=>{for(int i=0;i<16;i++)cells[i]=CollectionScreen.Cell(f,i/4,i%4);});
   Time("칸 읽기 16",()=>{foreach(var c in cells)reader.Cell(c);});
   Time("BEST",()=>reader.Best(CollectionScreen.Best(f)));
   Time("카테고리",()=>CollectionScreen.Category(f));
   Time("움직임 그림(제목·표)",()=>{CollectionScreen.TitleSign(f);CollectionScreen.TableSign(f);});
   Time("목록 그림",()=>CollectionScreen.ListSign(f));
   Time("고른 줄의 띠",()=>CollectionScreen.Bar(f));
   Time("Read 전체",()=>reader.Read(f));
  }
  int n=frames.Count*5;var report=new StringBuilder($"멈춘 화면 {frames.Count}장 × 5번, 한 장 평균(ms)\n");
  foreach(var (name,total) in parts)report.AppendLine($"{name}\t{total/n:0.0}");
  File.WriteAllText(reportPath,report.ToString());
 }
 /// <summary>1920×1080 PNG 여러 장을 제품의 코드로 읽어 BEST·칸의 여유를 적는다(줄이는 방법을 견주려고. 같은 이름 앞부분의 파일끼리 묶어 본다).
 ///   DJMAXLocalRecords.exe --collection-read-pngs <폴더> <결과 파일></summary>
 public static void ReadPngs(Catalog catalog,string folder,string reportPath)
 {
  var data=CollectionData.Load();var reader=new CollectionReader(catalog,data);var report=new StringBuilder();
  foreach(string path in Directory.GetFiles(folder,"*.png").OrderBy(f=>f))
  {
   using var bitmap=new Bitmap(path);if(bitmap.Width!=CollectionFrame.Width||bitmap.Height!=CollectionFrame.Height){report.AppendLine($"{Path.GetFileName(path)}: 크기 {bitmap.Width}×{bitmap.Height}");continue;}
   var frame=new CollectionFrame(bitmap);var shot=reader.Read(frame);var judge=reader.Judge(shot);var sure=CollectionReader.SureCells(shot);
   // BEST: 숫자마다 가장 가까운 표본과의 다른 화소(같은 너비의 표본이 없으면 '너비 w')
   var mask=CollectionScreen.Best(frame);var cols=mask.ColumnSums();var parts=new List<string>();
   for(int x=0;x<mask.Width;)
   {
    if(cols[x]==0){x++;continue;}int end=x;while(end<mask.Width&&cols[end]>0)end++;
    int best=int.MaxValue;string label="?";
    foreach(var s in data.Best){if(s.Picture.Width!=end-x)continue;int miss=0;for(int y=0;y<s.Picture.Height;y++)for(int i=0;i<s.Picture.Width;i++)if(mask.Bits[y*mask.Width+x+i]!=s.Picture.Bits[y*s.Picture.Width+i])miss++;if(miss<best){best=miss;label=s.Label;}}
    parts.Add(best==int.MaxValue?$"너비{end-x}":$"{label}:{best}");x=end;
   }
   var numbers=Enumerable.Range(0,16).Where(i=>shot.Cells[i].Number).ToList();
   string cells=numbers.Count==0?"숫자 칸 없음":$"숫자 칸 {numbers.Count}, 가장 큰 다른 화소 {numbers.Max(i=>shot.Cells[i].Cost)}, 가장 작은 여유 {numbers.Min(i=>shot.Cells[i].Margin)}";
   report.AppendLine($"{Path.GetFileName(path)}: 곡 {judge.Song?.Name??"(정하지 못함)"} 제목 {shot.Title?.Cost:0.000}, BEST {shot.Best??"(못 읽음)"} [{string.Join(" ",parts)}], {cells}, 확실하지 않은 칸 {sure.Count(s=>!s)}");
  }
  File.WriteAllText(reportPath,report.ToString());
 }
}
