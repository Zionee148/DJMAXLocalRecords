namespace LadderRecords;
// 컬렉션 가져오기: 화면 한 장에서 제목·점수표·BEST·카테고리를 읽고, 곡 목록의 어느 곡인지와 어디까지 확실한지를 정한다.
// 글자 인식을 쓰지 않는다. 제목은 곡 목록의 이름을 자료의 글자 그림으로 화면 위에 놓아 견주고, 점수는 숫자 표본으로 왼쪽부터 덮는다.
// 게임의 RATE 표는 읽지 않는다. 창도 키 보내기도 여기에 없다: 화면을 받아 값을 돌려줄 뿐이다.

/// <summary>점수표의 칸 하나를 읽은 것. Ink 는 칸 안의 흰 화소 수, Text 는 읽은 글, Cost 는 고른 글자들을 그린 그림과 화면이 다른 화소 수.
/// 칸의 상태는 넷이다: 빈칸(패턴 없음. 글자가 없다) / 또렷한 `-`(기록 없음) / 숫자 / 읽지 못함(글자는 있는데 맞는 것이 없거나 `-` 가 또렷하지 않다).</summary>
sealed record CellRead(string Text,int Cost,int Margin,int Ink)
{
 public bool Blank=>Ink<CollectionReader.BlankInk;
 public bool None=>!Blank&&Text=="-"&&Cost<=CollectionReader.DashCost;
 public bool Number=>!Blank&&Text.Length is >0 and <=7&&Text.All(char.IsAsciiDigit);
 public int Score=>Number?int.Parse(Text):0;
}
/// <summary>제목을 맞힌 것. Text 는 게임이 그리는 글, Cost 는 그린 것과 화면이 어긋난 화소의 몫(0 이 완전히 같음).</summary>
sealed record TitleRead(string Text,double Cost,string? Second,double SecondCost,List<(string Text,double Cost)> Near);
/// <summary>화면 한 장에서 읽은 것. Cells 는 NORMAL·HARD·MAXIMUM·SC 의 행마다 4B·5B·6B·8B.</summary>
sealed record ShotRead(Mask TitleMask,TitleRead? Title,CellRead[] Cells,string? Best,Mask Category);
/// <summary>곡과 칸마다의 판단. 화면 한 장으로 말할 수 있는 것만 담는다.</summary>
sealed class SongJudge
{
 /// <summary>확실하게 정한 곡. 정하지 못했으면 null.</summary>
 public Song? Song;
 /// <summary>제목이 가리키는 곡 모두(이름이 같은 곡이 여럿이면 둘 이상. 글자 하나만 다른 가까운 이름도 함께 든다).</summary>
 public List<Song> Candidates=new();
 /// <summary>후보 가운데 화면의 빈칸 자리·카테고리와 맞는 곡(보조 근거). 곡을 정하는 데는 쓰지 않고, 아티스트로 고른 곡이 여기에 없으면 확실하다고 보지 않는다.</summary>
 public List<Song> Likely=new();
 /// <summary>후보가 둘 이상이다: 오른쪽 목록의 아티스트로 정해야 한다(읽기를 진행하는 쪽이 아티스트를 읽어 Resolve 를 부른다).</summary>
 public bool NeedsArtist;
 /// <summary>제목이 없거나 어느 이름과도 충분히 겹치지 않는다. 가려진 것인지 곡 목록에 없는 곡인지는 화면 한 장으로 알 수 없다(다시 읽고 판단하는 것은 읽기를 진행하는 쪽의 일이다).</summary>
 public bool TitleUnclear;
 /// <summary>확실히 읽힌 칸의 빈칸 자리가 어느 후보의 패턴과도 맞지 않는다.</summary>
 public bool PatternsDiffer;
 /// <summary>아티스트로 고른 곡이 빈칸의 자리·카테고리와 맞지 않는다.</summary>
 public bool ArtistDiffers;
 /// <summary>칸마다 확실한가(Song 이 있을 때만 뜻이 있다).</summary>
 public bool[] Sure=new bool[16];
 public bool AllSure=>Song!=null&&Sure.All(s=>s);
}

sealed class CollectionReader
{
 // 제목: 고른 이름의 어긋난 몫이 이 값 아래여야 그 이름으로 본다. 녹화 둘에서 맞는 이름의 가장 큰 값이 0.049, 틀린 이름의 가장 작은 값이 0.19 였다
 public const double TitleSure=0.10;
 // 고른 이름과 이만큼 안에 든 다른 이름은 함께 후보로 둔다(`Burn it Down` 과 `Burn It Down` 은 0.003 차다)
 public const double TitleTie=0.02;
 // 점수 칸: 표본과 다른 화소가 이 수를 넘거나, 한 숫자를 다른 숫자로 바꿨을 때 늘어나는 화소가 이 수보다 적으면 확실하지 않다.
 // 받아 둔 실제 화면에서 맞게 읽힌 칸은 다른 화소가 가장 큰 것이 10, 여유가 가장 작은 것이 11 이었다. 압축된 녹화 둘도 통과해야 해서 그 몫을 품은 값이다
 public const int CellCost=40,CellMargin=6;
 // 칸의 흰 화소가 이 수보다 적으면 글자가 없는 빈칸이다. `-` 는 표본과 다른 화소가 이 수 이하일 때만 또렷한 `-` 로 본다(숫자가 가려져 `-` 처럼 남은 조각을 기록 없음으로 읽지 않게)
 public const int BlankInk=3,DashCost=6;
 // 카테고리: 가장 가까운 팩의 그림과 다른 화소가 그 그림의 화소 수의 이 몫보다 적고, 둘째 팩과 CategoryGap 넘게 벌어질 때만 그 팩으로 본다(가려진 줄은 어느 팩과도 가깝지 않다)
 const double CategoryNear=0.45;
 const int BestNear=16,CategoryGap=40;
 static readonly int[] TitleShifts={0,-1,1,-2,2,-3,3};static readonly int[] ArtistShifts={0,-1,1,-2,2};
 static readonly int[] Gaps={-1,0,1,2,3,4,5,6,7};

 readonly CollectionData data;readonly List<(string Text,double Width,List<Song> Songs)> titles=new();
 public CollectionReader(Catalog catalog,CollectionData data)
 {
  this.data=data;
  foreach(var group in catalog.Songs.GroupBy(data.TitleOf))titles.Add((group.Key,data.Title.WidthOf(group.Key),group.ToList()));
 }
 public List<Song> SongsOf(string text)=>titles.First(t=>t.Text==text).Songs;

 public ShotRead Read(CollectionFrame frame)
 {
  // 제목 맞히기와 칸 16개는 서로 기다릴 필요가 없다: 나눠 동시에 한다(곡 하나를 읽는 시간의 대부분이다. 결과는 같다)
  var title=CollectionScreen.Title(frame);var cells=new CellRead[16];TitleRead? read=null;
  Parallel.Invoke(()=>read=Title(title),()=>Parallel.For(0,16,i=>cells[i]=Cell(CollectionScreen.Cell(frame,i/4,i%4))));
  return new(title,read,cells,Best(CollectionScreen.Best(frame)),CollectionScreen.Category(frame));
 }

 // ── 제목 ──
 /// <summary>화면의 제목 그림과 가장 잘 겹치는 이름. 그림에 글자가 없으면 null.</summary>
 public TitleRead? Title(Mask mask)
 {
  var (first,last)=mask.Columns();if(first<0)return null;
  int seen=last-first+1;bool clipped=last>=mask.Width-40;int clip=clipped?last+1:mask.Width;   // 긴 제목은 오른쪽 끝 근처에서 잘린다: 보이는 데까지만 견준다
  var near=mask.Grown();var costs=new double[titles.Count];var tried=new bool[titles.Count];
  // 후보마다의 견주기는 서로 기다릴 필요가 없다: 나눠 동시에 하고, 모으는 차례는 전과 같게 후보의 차례다(같은 값끼리의 순서도 그대로)
  Parallel.For(0,titles.Count,i=>
  {
   var (text,width,_)=titles[i];
   if(clipped){if(width<seen-30)return;}else if(Math.Abs(width-seen)>Math.Max(30,0.12*seen))return;
   costs[i]=Fit(text,mask,near,data.Title,clip,TitleShifts,true);tried[i]=true;
  });
  var scores=new List<(double Cost,string Text)>();for(int i=0;i<titles.Count;i++)if(tried[i])scores.Add((costs[i],titles[i].Text));
  if(scores.Count==0)return new("",1,null,1,new());
  scores.Sort((a,b)=>a.Cost!=b.Cost?a.Cost.CompareTo(b.Cost):string.CompareOrdinal(a.Text,b.Text));
  var top=scores[0];var second=scores.Count>1?scores[1]:(1.0,null!);
  return new(top.Text,top.Cost,scores.Count>1?second.Item2:null,second.Item1,scores.Where(s=>s.Cost-top.Cost<TitleTie).Select(s=>(s.Text,s.Cost)).ToList());
 }
 /// <summary>글을 한 글자씩 화면 그림 위에 왼쪽부터 놓아 본다. 글자마다 좌우로 조금 움직여 가장 잘 겹치는 자리를 고른다(게임의 자간이 조금 다르다).
 /// 돌려주는 값은 그린 것과 화면이 어긋난 화소의 몫이다. grow 이면 한 화소의 여유를 둔다(큰 얇은 글자인 제목). 작은 굵은 글자(아티스트)는 여유를 두면 가르지 못한다.</summary>
 public static double Fit(string text,Mask mask,Mask near,GlyphSet glyphs,int clip,int[] shifts,bool grow)
 {
  var (first,_)=mask.Columns();if(first<0)return 1;
  var drawn=new Mask(mask.Width,mask.Height);double cursor=double.NaN;
  foreach(string ch in GlyphSet.Letters(text))
  {
   var g=glyphs.Get(ch);var p=g.Picture;
   if(double.IsNaN(cursor)){if(p.Width==0)continue;cursor=first-g.Left;}   // 첫 글자의 잉크가 화면의 왼쪽 끝에 오게
   if(p.Width==0){cursor+=g.Advance;continue;}
   int x=(int)Math.Round(cursor,MidpointRounding.ToEven)+g.Left,best=0,bestHit=-1;
   foreach(int shift in shifts)
   {
    int x0=x+shift;if(x0<0||x0>=clip)continue;
    int width=Math.Min(p.Width,clip-x0),hit=0;
    for(int y=0;y<p.Height;y++)for(int i=0;i<width;i++)if(p.Bits[y*p.Width+i]&&near.Bits[y*near.Width+x0+i])hit++;
    if(hit>bestHit){best=shift;bestHit=hit;}
   }
   int at=x+best;
   if(at>=0&&at<clip){int width=Math.Min(p.Width,clip-at);for(int y=0;y<p.Height;y++)for(int i=0;i<width;i++)if(p.Bits[y*p.Width+i])drawn.Bits[y*drawn.Width+at+i]=true;}
   cursor+=best+g.Advance;if(cursor>clip+60)break;
  }
  int total=drawn.Count()+mask.Count();if(total==0)return 1;
  var drawnNear=grow?drawn.Grown():drawn;int wrong=0;
  for(int i=0;i<mask.Bits.Length;i++){if(drawn.Bits[i]&&!near.Bits[i])wrong++;if(mask.Bits[i]&&!drawnNear.Bits[i])wrong++;}
  return (double)wrong/total;
 }

 // ── 점수표의 칸 ──
 sealed record Step(int Cost,string Text,int X,Sample? Used,int Miss,Step? Next);
 /// <summary>칸의 흰 글자 그림을 숫자 표본으로 왼쪽부터 덮어, 그림과 다른 화소가 가장 적은 나열을 고른다(숫자가 서로 붙어 있어 빈 줄로 나누지 못한다).</summary>
 public CellRead Cell(Mask mask)
 {
  var cols=mask.ColumnSums();int sum=cols.Sum();if(sum<BlankInk)return new("",sum,int.MaxValue,sum);
  int width=mask.Width,height=mask.Height,start=Array.FindIndex(cols,c=>c>0);
  var tail=new int[width+1];for(int x=width-1;x>=0;x--)tail[x]=tail[x+1]+cols[x];
  var memo=new Step?[width];
  Step Solve(int x)
  {
   if(x>=width)return new(0,"",x,null,0,null);
   if(memo[x] is{} known)return known;
   var answer=new Step(tail[x],"",x,null,0,null);   // 여기서 끝낸다: 남은 흰 화소가 모두 틀린 것
   foreach(var sample in data.Digits)
   {
    int w=sample.Picture.Width;if(x+w>width)continue;
    int miss=Miss(mask,sample.Picture,x);if(miss>w*height*0.25)continue;
    foreach(int gap in Gaps)
    {
     int next=x+w+gap,between=0;for(int i=x+w;i<Math.Min(next,width);i++)between+=cols[i];
     var rest=Solve(Math.Max(next,x+1));int total=miss+between+rest.Cost;
     if(total<answer.Cost)answer=new(total,sample.Label+(gap>=4&&rest.Text!=""?" ":"")+rest.Text,x,sample,miss,rest);
    }
   }
   return memo[x]=answer;
  }
  var found=Solve(start);
  // 고른 숫자들을 한 장에 그려 화면과 견준다. 위의 셈은 붙은 숫자의 겹친 열을 두 번 세므로, 확실한지는 이 그림과 화면이 다른 화소 수로 본다
  var steps=new List<(int X,Sample Used)>();for(var step=found;step?.Used!=null;step=step.Next)steps.Add((step.X,step.Used));
  int left=Drawn(mask,steps,-1,null);string text=found.Text;
  // 통째 표본(100만 점)은 따로 대 본다: 위의 셈은 낱숫자의 겹친 열을 두 번 세어, 낱숫자의 나열이 통째 표본보다 낮게 나올 때가 있다
  foreach(var whole in data.Digits)
  {
   if(whole.Label.Length==1)continue;
   for(int shift=-1;shift<=1;shift++)
   {
    int x=start+shift;if(x<0||x+whole.Picture.Width>width)continue;
    var one=new List<(int X,Sample Used)>{(x,whole)};int differ=Drawn(mask,one,-1,null);
    if(differ<left){left=differ;steps=one;text=whole.Label;}
   }
  }
  // 숫자 하나를 다른 숫자로 바꿔 그렸을 때 다른 화소가 얼마나 늘어나는지. 가장 작은 값이 이 칸의 여유다
  int margin=int.MaxValue;
  for(int i=0;i<steps.Count;i++)
  {
   if(steps[i].Used.Label=="-"||steps[i].Used.Label.Length!=1)continue;   // 통째 표본(100만 점)은 낱숫자로 바꿔 볼 것이 없다
   foreach(var other in data.Digits){if(other.Label==steps[i].Used.Label||other.Label=="-"||other.Label.Length!=1||steps[i].X+other.Picture.Width>width)continue;margin=Math.Min(margin,Drawn(mask,steps,i,other)-left);}
  }
  return new(text,left,margin,sum);
 }
 /// <summary>숫자들을 제자리에 그린 그림과 화면이 다른 화소 수. swap 번째 숫자는 other 로 바꿔 그린다.</summary>
 static int Drawn(Mask mask,List<(int X,Sample Used)> steps,int swap,Sample? other)
 {
  var drawn=new bool[mask.Bits.Length];
  for(int i=0;i<steps.Count;i++)
  {
   var picture=i==swap?other!.Picture:steps[i].Used.Picture;int x0=steps[i].X;
   for(int y=0;y<picture.Height;y++)for(int x=0;x<picture.Width;x++)if(picture.Bits[y*picture.Width+x]&&x0+x<mask.Width)drawn[y*mask.Width+x0+x]=true;
  }
  int differ=0;for(int i=0;i<drawn.Length;i++)if(drawn[i]!=mask.Bits[i])differ++;return differ;
 }
 static int Miss(Mask mask,Mask sample,int x){int miss=0;for(int y=0;y<sample.Height;y++)for(int i=0;i<sample.Width;i++)if(mask.Bits[y*mask.Width+x+i]!=sample.Bits[y*sample.Width+i])miss++;return miss;}

 /// <summary>표 왼쪽의 BEST. 숫자가 떨어져 있어 빈 줄로 나눠 표본과 견준다. 읽지 못하면 null.
 /// 1920×1080 화면에서는 나눈 덩어리의 폭이 표본과 꼭 같다(Exact). 큰 16:9 를 1920×1080 으로 줄인 화면에서는 숫자의 폭이 1 달라지고, 이웃한 숫자가 붙기도 한다(`444` 의 가로줄):
 /// 꼭 같은 폭으로 읽지 못한 덩어리는 폭이 1 다른 표본까지 좌우로 옮겨 견주고(Loose), 숫자 하나보다 넓은 덩어리는 숫자 폭의 조각으로 나눠 읽는다(Split).</summary>
 public string? Best(Mask mask)
 {
  var cols=mask.ColumnSums();var text=new System.Text.StringBuilder();
  for(int x=0;x<mask.Width;)
  {
   if(cols[x]==0){x++;continue;}
   int end=x;while(end<mask.Width&&cols[end]>0)end++;
   string? digits=Exact(mask,x,end)??Loose(mask,x,end)??Split(mask,x,end);
   if(digits==null)return null;
   text.Append(digits);x=end;
  }
  return text.Length==0?null:text.ToString();
 }
 // 폭을 너그럽게 볼 때: 가장 가까운 숫자의 다른 화소가 BestLoose 미만이고, 둘째로 가까운 다른 숫자와 BestApart 이상 벌어져야 그 숫자로 본다.
 // 받아 둔 1920×1080 화면(1,391장)에서 맞는 숫자는 4 이하·다른 숫자와 36 이상, 2560×1440 을 줄인 화면(46장)에서 맞는 숫자는 16 이하·다른 숫자와 25 이상이었다
 const int BestLoose=24,BestApart=16;
 string? Exact(Mask mask,int x,int end)
 {
  foreach(var sample in data.Best)if(sample.Picture.Width==end-x&&Miss(mask,sample.Picture,x)<BestNear)return sample.Label;
  return null;
 }
 string? Loose(Mask mask,int x,int end){var (label,miss,other)=Nearest(mask,x,end,false);return label!=null&&miss<BestLoose&&other-miss>=BestApart?label:null;}
 /// <summary>숫자 둘 이상이 붙은 덩어리를 숫자 폭의 조각으로 나눈다. 다른 화소의 합이 가장 작은 나눔을 고르고, 조각마다 Loose 와 같은 기준을 넘어야 읽는다</summary>
 string? Split(Mask mask,int x,int end)
 {
  int low=data.Best.Min(s=>s.Picture.Width)-1,high=data.Best.Max(s=>s.Picture.Width)+1;if(end-x<=high)return null;
  var memo=new Dictionary<int,(int Cost,string? Text)>();
  (int Cost,string? Text) Solve(int p)
  {
   if(p==end)return(0,"");
   if(memo.TryGetValue(p,out var known))return known;
   (int Cost,string? Text) answer=(int.MaxValue,null);
   for(int w=low;w<=high&&p+w<=end;w++)
   {
    var (label,miss,other)=Nearest(mask,p,p+w,true);if(label==null||miss>=BestLoose||other-miss<BestApart)continue;
    var rest=Solve(p+w);if(rest.Text==null)continue;
    if(miss+rest.Cost<answer.Cost)answer=(miss+rest.Cost,label+rest.Text);
   }
   return memo[p]=answer;
  }
  return Solve(x).Text;
 }
 /// <summary>x..end 의 덩어리와 가장 가까운 숫자, 그 다른 화소, 둘째로 가까운 다른 숫자의 다른 화소(없으면 int.MaxValue/2).
 /// 폭이 1 다른 표본까지 좌우 1 화소 옮겨 놓아 본다. clip 이면 x..end 밖으로 나간 표본의 열은 보지 않는다(붙은 숫자의 조각). 아니면 표본이 덮는 열까지 함께 견준다</summary>
 (string? Label,int Miss,int Other) Nearest(Mask mask,int x,int end,bool clip)
 {
  var byLabel=new Dictionary<string,int>();
  foreach(var sample in data.Best)
  {
   int w=sample.Picture.Width;if(Math.Abs(w-(end-x))>1)continue;
   for(int dx=-1;dx<=1;dx++)
   {
    int x0=x+dx;if(x0<0||x0+w>mask.Width)continue;
    int lo=clip?x:Math.Min(x,x0),hi=clip?end:Math.Max(end,x0+w),miss=0;
    for(int y=0;y<mask.Height;y++)for(int c=lo;c<hi;c++){bool drawn=c>=x0&&c<x0+w&&sample.Picture.Bits[y*w+c-x0];if(drawn!=mask.Bits[y*mask.Width+c])miss++;}
    if(!byLabel.TryGetValue(sample.Label,out int was)||miss<was)byLabel[sample.Label]=miss;
   }
  }
  if(byLabel.Count==0)return(null,int.MaxValue,int.MaxValue);
  var order=byLabel.OrderBy(p=>p.Value).ToList();
  return(order[0].Key,order[0].Value,order.Count>1?order[1].Value:int.MaxValue/2);
 }

 // ── 아티스트(오른쪽 목록) ──
 /// <summary>목록의 한 줄(위쪽 y)에서 후보 아티스트마다의 어긋난 몫. 목록이 멈춘 화면에서 쓴다.</summary>
 public List<(string Artist,double Cost)> Artists(Mask letters,int top,IEnumerable<string> candidates)
 {
  var band=CollectionScreen.ArtistBand(letters,top);
  return candidates.Distinct().Select(a=>(a,Fit(a,band,band,data.Artist,band.Width,ArtistShifts,false))).OrderBy(a=>a.Item2).ToList();
 }

 // ── 곡과 칸의 판단 ──
 /// <summary>읽은 것으로 곡과 칸마다의 확실함을 정한다.
 /// 제목이 한 곡만 가리키면 그 곡이다(빈칸의 자리가 맞아야 한다). 둘 이상을 가리키면 곡을 정하지 않고 NeedsArtist 와 후보를 돌려준다:
 /// 빈칸의 자리와 카테고리는 보조 근거(Likely)로만 남긴다. 읽기를 진행하는 쪽이 목록의 아티스트를 읽어 Resolve 로 정한다.</summary>
 public SongJudge Judge(ShotRead shot)
 {
  var judge=new SongJudge();var title=shot.Title;
  if(title==null||title.Text==""||title.Cost>TitleSure){judge.TitleUnclear=true;return judge;}
  judge.Candidates=title.Near.SelectMany(n=>SongsOf(n.Text)).Distinct().ToList();
  // 빈칸의 자리: 확실히 읽힌 칸에서만 견준다(읽지 못한 칸은 후보를 거르는 근거로 쓰지 않는다)
  var plain=Plain(shot);var fits=judge.Candidates.Where(s=>Fits(s,shot.Cells,plain)).ToList();
  if(fits.Count==0)judge.PatternsDiffer=true;
  if(judge.Candidates.Count>1){judge.NeedsArtist=true;judge.Likely=fits.Count>1?ByCategory(fits,shot.Category):fits;return judge;}
  judge.Likely=fits;if(fits.Count==1)Settle(judge,fits[0],shot);
  return judge;
 }
 /// <summary>아티스트를 읽은 뒤에 곡을 정한다. 그 아티스트의 후보가 하나이고 빈칸의 자리·카테고리와도 맞을 때만 확정한다. 맞지 않으면 ArtistDiffers 로 두고 정하지 않는다.</summary>
 public SongJudge Resolve(ShotRead shot,SongJudge judge,string artist)
 {
  if(!judge.NeedsArtist||judge.Song!=null)return judge;
  var picked=judge.Candidates.Where(s=>s.Artist==artist).ToList();if(picked.Count!=1)return judge;
  if(!judge.Likely.Contains(picked[0])){judge.ArtistDiffers=true;return judge;}
  Settle(judge,picked[0],shot);return judge;
 }
 static bool[] Plain(ShotRead shot){var plain=new bool[16];for(int i=0;i<16;i++)plain[i]=shot.Cells[i].Blank||shot.Cells[i].None||Clear(shot.Cells[i]);return plain;}
 static void Settle(SongJudge judge,Song song,ShotRead shot){judge.Song=song;judge.Sure=SureCells(shot);}
 /// <summary>칸마다 확실한가: 빈칸·또렷한 `-`·또렷한 숫자이고, 화면의 BEST 가 16칸의 최댓값과 맞는다. 곡을 정하지 못한 화면에서도 쓴다(읽기를 진행하는 쪽이 확인할 것의 수를 셀 때).</summary>
 public static bool[] SureCells(ShotRead shot)
 {
  var sure=Plain(shot);
  // BEST 는 16칸의 최댓값이다. 읽혔는데 다르면 어느 칸이 틀렸는지 알 수 없다: 가장 높던 칸이 `-` 로도, 앞자리가 가려진 작은 숫자로도 읽혔을 수 있다.
  // 그래서 패턴이 있는 칸(숫자와 `-`) 전부를 확실하지 않은 것으로 둔다(가려 뽑으면 고쳐야 할 칸이 빠진다)
  if(shot.Best==null)return sure;
  var numbers=shot.Cells.Where(c=>c.Number).ToList();int max=numbers.Count>0?numbers.Max(c=>c.Score):0;
  if(shot.Best==max.ToString())return sure;
  for(int i=0;i<16;i++)if(!shot.Cells[i].Blank)sure[i]=false;
  return sure;
 }
 /// <summary>그 칸에 패턴이 있는지 없는지를 확실히 아는가(빈칸, '-', 또렷한 숫자). 판독 검수가 곡 후보를 거를 때 쓴다(흐린 칸은 근거로 쓰지 않는다)</summary>
 internal static bool Certain(CellRead cell)=>cell.Blank||cell.None||Clear(cell);
 static bool Clear(CellRead cell)=>cell.Number&&cell.Score is >=1 and <=1_000_000&&cell.Cost<=CellCost&&cell.Margin>=CellMargin;
 /// <summary>화면의 빈칸 자리가 곡의 패턴 유무와 맞는가. plain 인 칸만 본다.</summary>
 static bool Fits(Song song,CellRead[] cells,bool[] plain)
 {
  for(int row=0;row<4;row++)for(int column=0;column<4;column++)
  {
   int i=row*4+column;if(!plain[i])continue;
   bool has=song.Charts.Any(c=>c.Button==CollectionScreen.Buttons[column]&&c.Pattern==CollectionScreen.Patterns[row]);
   if(has==cells[i].Blank)return false;
  }
  return true;
 }
 /// <summary>카테고리 줄의 그림이 한 팩에 뚜렷이 가까우면 그 팩의 곡만 남긴다. 글이 비슷한 팩끼리(숫자만 다른 것)는 가르지 못하므로 그대로 둔다.</summary>
 List<Song> ByCategory(List<Song> songs,Mask category)
 {
  var packs=songs.Select(s=>s.Pack).Distinct().Where(data.Categories.ContainsKey).Select(p=>(Pack:p,Differ:data.Categories[p].Differ(category))).OrderBy(p=>p.Differ).ToList();
  if(packs.Count<2||packs.Count!=songs.Select(s=>s.Pack).Distinct().Count())return songs;
  if(packs[1].Differ-packs[0].Differ<CategoryGap||packs[0].Differ>CategoryNear*data.Categories[packs[0].Pack].Count())return songs;
  return songs.Where(s=>s.Pack==packs[0].Pack).ToList();
 }
}
