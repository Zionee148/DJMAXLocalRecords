namespace LadderRecords;
// 컬렉션 가져오기의 판독 검수 모델(창 없이 검사한다). 끝까지 읽은 결과 가운데 확인할 것이 있는 곡을 묶음으로, 그 칸을 줄로 만들고,
// 사용자가 고른 곡·적은 점수·기록 없음·제외로 남은 항목과 안내를 셈하고, 다 정하면 적용의 셈(CollectionPlan)이 받는 CollectionOutcome 을 만든다.
// 후보가 없는 곡은 모두 제외로 넘길 수 없다(곡을 골라야 한다).

/// <summary>검수할 내용: 곡(곡만 고른다) · 곡 & 스코어 · 스코어(점수만 적는다)</summary>
enum ReviewKind{Song,SongScore,Score}

/// <summary>검수 표의 한 줄(버튼·패턴 하나)</summary>
sealed class ReviewRow
{
 public required ReviewGroup Group{get;init;}
 /// <summary>점수표의 칸(0~15: NORMAL·HARD·MAXIMUM·SC 의 행마다 4B·5B·6B·8B)</summary>
 public required int Cell{get;init;}
 public required ReviewKind Kind{get;init;}
 /// <summary>'곡' 줄이 보이는 읽은 값(확실하게 읽은 점수 또는 '-')</summary>
 public string Read="";
 public string Typed="";public bool None,Excluded;
 public string Pattern=>CollectionReview.PatternName(Cell);
 public string Label=>Kind switch{ReviewKind.Song=>"곡",ReviewKind.SongScore=>"곡 & 스코어",_=>"스코어"};
 public bool NeedsScore=>Kind!=ReviewKind.Song;
 /// <summary>적은 점수가 맞는가(숫자만, 1~1,000,000)</summary>
 public int? Score=>Typed.Length is >0 and <=7&&Typed.All(char.IsAsciiDigit)&&int.TryParse(Typed,out int v)&&v is >=1 and <=1_000_000?v:null;
 public bool ScoreDone=>None||Score!=null;
 /// <summary>비어 있지 않은데 맞지 않는 점수(칸 아래에 안내를 보인다)</summary>
 public bool ScoreBad=>NeedsScore&&!Excluded&&!None&&Typed.Length>0&&Score==null;
}

/// <summary>곡 하나(읽기의 ScanItem 하나)의 줄들. 묶음 안의 줄은 고른 곡을 함께 쓴다</summary>
sealed class ReviewGroup
{
 public required ScanItem Item{get;init;}
 /// <summary>읽을 때 정한 곡(그러면 곡을 고르지 않는다)</summary>
 public Song? Fixed{get;init;}
 public Song? Chosen;
 /// <summary>곡 선택의 후보: 제목이 가리키는 곡 가운데 화면에서 확실히 읽은 패턴과 맞는 곡</summary>
 public List<Song> Candidates{get;}=new();
 public List<ReviewRow> Rows{get;}=new();
 public Song? Song=>Fixed??Chosen;
 public bool NeedsSong=>Fixed==null;
 public bool AllExcluded=>Rows.All(r=>r.Excluded);
 /// <summary>제목이 흔들려 곡을 정하지 못했다(후보는 마지막에 본 제목의 것이다): 곡을 고르지 않고 모두 제외할 수 없다</summary>
 public bool TitleShaky=>Item.TitleUnsteady;
}

sealed class CollectionReview
{
 public const string BadScore="1점부터 100만 점까지 숫자만 입력해 주세요.",AlreadyRead="이미 읽은 곡입니다.",PickedElsewhere="다른 줄에서 이미 고른 곡입니다.",
  OtherPatterns="화면에서 읽은 패턴과 다릅니다. 곡을 다시 확인해 주세요.",NotFound="곡을 찾을 수 없으면 ‘취소’한 뒤 메인 창에서 ‘업데이트 확인’을 눌러 주세요.",
  Shaky="제목이 흔들려 곡을 정하지 못한 곡은 모두 제외할 수 없습니다. 곡을 골라 주세요.",
  TakenElsewhere="후보 곡이 다른 곳에서 이미 확인되었습니다. 전체 곡에서 찾아 선택해 주세요.";
 public static string PatternName(int cell)=>$"{CollectionScreen.Buttons[cell%4]}B {CollectionScreen.Patterns[cell/4]}";
 static bool Has(Song song,int cell)=>song.Charts.Any(c=>c.Button==CollectionScreen.Buttons[cell%4]&&c.Pattern==CollectionScreen.Patterns[cell/4]);

 readonly ScanResult result;
 public IReadOnlyList<ReviewGroup> Groups{get;}
 /// <summary>확인할 것 없이 정해진 곡(검수하지 않는 곡과, 검수하는 곡 가운데 읽을 때 정한 곡)</summary>
 readonly HashSet<string> settled;
 CollectionReview(ScanResult result,List<ReviewGroup> groups){this.result=result;Groups=groups;
  settled=new(result.Items.Where(i=>i.Questions==0&&i.Judge.Song!=null).Select(i=>i.Judge.Song!.Id).Concat(groups.Where(g=>g.Fixed!=null).Select(g=>g.Fixed!.Id)),StringComparer.Ordinal);}

 /// <summary>
 /// 끝까지 읽은 결과에서 검수 표를 만든다. 곡을 정한 곡은 확실하지 않은 칸마다 '스코어' 줄. 곡을 정하지 못한 곡은 확실하게 읽은 점수 칸이 '곡' 줄,
 /// 확실하지 않은 칸이 '곡 & 스코어' 줄이고, 확실하게 읽은 '-' 와 빈칸은 줄을 만들지 않는다(그런 칸밖에 없으면 첫 칸으로 '곡' 줄 하나)
 /// </summary>
 public static CollectionReview From(ScanResult result)
 {
  var groups=new List<ReviewGroup>();
  foreach(var item in result.Items.Where(i=>i.Questions>0))
  {
   var group=new ReviewGroup{Item=item,Fixed=item.Judge.Song};
   var cells=item.Shot.Cells;
   if(group.Fixed!=null){for(int i=0;i<16;i++)if(!cells[i].Blank&&!item.Sure[i])group.Rows.Add(new(){Group=group,Cell=i,Kind=ReviewKind.Score});}
   else
   {
    for(int i=0;i<16;i++)
    {
     if(cells[i].Blank)continue;
     if(!item.Sure[i])group.Rows.Add(new(){Group=group,Cell=i,Kind=ReviewKind.SongScore});
     else if(cells[i].Number)group.Rows.Add(new(){Group=group,Cell=i,Kind=ReviewKind.Song,Read=cells[i].Text});
    }
    if(group.Rows.Count==0){int first=Enumerable.Range(0,16).FirstOrDefault(i=>!cells[i].Blank);group.Rows.Add(new(){Group=group,Cell=first,Kind=ReviewKind.Song,Read=cells[first].Text});}
    group.Candidates.AddRange(item.Judge.Candidates.Where(s=>Fits(s,item)).Distinct());
   }
   groups.Add(group);
  }
  return new CollectionReview(result,groups);
 }
 /// <summary>그 곡의 패턴이 화면에서 확실히 읽은 칸(빈칸 = 패턴 없음, '-'·점수 = 패턴 있음)과 맞는가. 흐린 칸은 근거로 쓰지 않는다</summary>
 public static bool Fits(Song song,ScanItem item){for(int i=0;i<16;i++){var cell=item.Shot.Cells[i];if(!CollectionReader.Certain(cell))continue;if(Has(song,i)==cell.Blank)return false;}return true;}

 /// <summary>
 /// 곡 칸 아래의 안내. 이미 읽은 곡이 먼저, 겹쳐 고른 곡, 패턴이 다른 곡. 모두 제외한 묶음도 본다: 곡을 골랐으면 줄이 없는 칸(확실하게 읽은 점수와 '-')의 값을 쓰므로,
 /// 같은 곡을 고른 다른 묶음의 값을 덮을 수 있다
 /// </summary>
 public IReadOnlyList<string> SongNotes(ReviewGroup group)
 {
  var notes=new List<string>();
  if(!group.NeedsSong)return notes;
  // 후보가 모두 다른 데서 정해진 곡이면 그 까닭을 곡 칸에 둔다(모두 제외로 넘길 수 없다)
  if(group.Chosen==null){if(group.Candidates.Count>0&&Guarded(group).Count==0)notes.Add(TakenElsewhere);return notes;}
  if(settled.Contains(group.Chosen.Id))notes.Add(AlreadyRead);
  else if(Groups.Any(g=>g!=group&&g.Chosen?.Id==group.Chosen.Id))notes.Add(PickedElsewhere);
  if(!Fits(group.Chosen,group.Item))notes.Add(OtherPatterns);
  return notes;
 }
 /// <summary>그 묶음이 고른 곡이 이미 읽은 곡이거나 다른 묶음과 겹친다(확인을 막는다)</summary>
 public bool Blocks(ReviewGroup group)=>SongNotes(group).Any(n=>n is AlreadyRead or PickedElsewhere);
 /// <summary>
 /// 남은 항목: 곡을 고르지 않은 묶음 1건(제외하지 않은 줄이 있으면. 모두 제외했어도 남는 후보가 없거나 제목이 흔들렸으면),
 /// 점수를 정하지 않은 줄마다 1건
 /// </summary>
 public int Left=>Groups.Sum(g=>(g.NeedsSong&&g.Chosen==null&&(!g.AllExcluded||Guarded(g).Count==0||g.TitleShaky)?1:0)+g.Rows.Count(r=>!r.Excluded&&r.NeedsScore&&!r.ScoreDone));
 /// <summary>
 /// 곡을 고르지 않고 모두 제외한 묶음이 보호할 후보: 다른 데서 정해진 곡(확인할 것 없이 정해진 곡, 읽을 때 정한 곡, 다른 묶음이 고른 곡)은 뺀다.
 /// 곡은 목록에 한 번씩 나오므로 그 곡일 수 없고, 보호하면 그 곡에 적은 값이 쓰이지 않는다. 남는 후보가 없으면 곡을 골라야 한다
 /// </summary>
 public IReadOnlyList<Song> Guarded(ReviewGroup group)=>group.Candidates.Where(s=>!settled.Contains(s.Id)&&!Groups.Any(g=>g!=group&&g.Chosen?.Id==s.Id)).ToList();
 /// <summary>이미 읽은 곡을 고른 묶음이나 겹쳐 고른 묶음이 있다(확인을 막는다)</summary>
 public bool Duplicated=>Groups.Any(Blocks);
 public bool CanConfirm=>Left==0&&!Duplicated;
 /// <summary>표 아래의 건수 글</summary>
 public string Status=>Left>0?$"확인할 항목 {Left}건이 남았습니다.":Duplicated?"겹친 곡을 다시 골라 주세요.":"모든 항목을 확인했습니다.";
 /// <summary>곡을 고르지 않은 채 모두 제외한 묶음의 후보 곡(그 곡들의 기존 기록을 모두 유지한다)</summary>
 public IReadOnlyList<Song> Protected=>Groups.Where(g=>g.NeedsSong&&g.Chosen==null&&g.AllExcluded&&!g.TitleShaky).SelectMany(Guarded).DistinctBy(s=>s.Id).ToList();
 /// <summary>보호 안내(표 아래, 보일 때만)</summary>
 public string? ProtectNote{get{int n=Protected.Count;return n==0?null:n==2?"곡을 고르지 않아 두 후보 곡의 기존 기록을 모두 유지합니다.":$"곡을 고르지 않아 후보 {n}곡의 기존 기록을 모두 유지합니다.";}}
 /// <summary>후보가 없는 묶음(제목을 끝내 읽지 못한 곡)이 남았을 때의 안내</summary>
 public string? NotFoundNote=>Groups.Any(g=>g.NeedsSong&&g.Chosen==null&&g.Candidates.Count==0)?NotFound:null;
 /// <summary>제목이 흔들려 곡을 정하지 못한 묶음이 남았을 때의 안내</summary>
 public string? ShakyNote=>Groups.Any(g=>g.NeedsSong&&g.Chosen==null&&g.TitleShaky)?Shaky:null;

 /// <summary>
 /// 다 정한 검수의 결과를 적용의 셈(CollectionPlan.Make)의 입력으로 만든다. 확인할 것이 없던 곡은 읽은 값 그대로. 검수한 곡은 고른 곡의 패턴만(고른 곡에 없는 패턴의 칸은 쓰지 않는다):
 /// 제외한 줄은 그 패턴을 보호하고, 줄이 없는 칸은 읽은 값, 줄이 있는 칸은 '곡' 줄이면 읽은 값, 아니면 적은 점수나 기록 없음.
 /// 곡을 고르지 않고 모두 제외한 묶음은 후보 곡 전체를 보호하고 '이번 목록에 나온 곡'에 넣는다(목록에 없다고 지우지 않게)
 /// </summary>
 public CollectionOutcome Outcome(string profileId)
 {
  if(!CanConfirm)throw new InvalidOperationException("검수가 끝나지 않았다");
  var outcome=new CollectionOutcome{ProfileId=profileId};var groups=Groups.ToDictionary(g=>g.Item);
  foreach(var item in result.Items)
  {
   groups.TryGetValue(item,out var group);
   var song=group==null?item.Judge.Song!:group.Song;
   if(song==null){foreach(var candidate in Guarded(group!)){outcome.ProtectedSongs.Add(candidate.Id);outcome.Seen.Add(candidate.Id);}continue;}
   outcome.Seen.Add(song.Id);
   for(int i=0;i<16;i++)
   {
    var cell=item.Shot.Cells[i];if(cell.Blank||!Has(song,i))continue;
    string key=CollectionOutcome.Key(song.Id,CollectionScreen.Buttons[i%4],CollectionScreen.Patterns[i/4]);
    var row=group?.Rows.FirstOrDefault(r=>r.Cell==i);
    if(row==null)outcome.Values[key]=cell.None||!cell.Number?null:cell.Score;
    else if(row.Excluded)outcome.Protected.Add(key);
    else if(row.Kind==ReviewKind.Song)outcome.Values[key]=row.Read=="-"?null:int.Parse(row.Read);
    else outcome.Values[key]=row.None?null:row.Score;
   }
  }
  return outcome;
 }
}
