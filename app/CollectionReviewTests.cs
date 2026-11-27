namespace LadderRecords;
// 컬렉션 가져오기의 판독 검수 모델(CollectionReview)을 창 없이 검사한다(--self-test). 결과는 collection-review-validation.txt
static class CollectionReviewTests
{
 public static void Run()
 {
  var notes=new List<string>();void Check(bool value,string name){if(!value)throw new Exception(name);notes.Add("PASS: "+name);}
  Song S(string id,params int[] buttons)=>new(id,0,id.ToUpperInvariant(),"artist "+id,"p",buttons.Select(b=>new Chart(b,"SC")).ToList());
  var a=S("a",4,5);var b=S("b",4,5);var c=S("c",4);var d=S("d",4,5);
  CellRead Blank()=>new("",0,0,0);CellRead Sure(string text)=>new(text,0,100,100);CellRead Unsure(string text)=>new(text,999,0,100);
  ScanItem Item(Song? song,IEnumerable<Song> candidates,params (int Cell,CellRead Read,bool Sure)[] cells)
  {
   var reads=Enumerable.Range(0,16).Select(_=>Blank()).ToArray();var sure=Enumerable.Repeat(true,16).ToArray();
   foreach(var (cell,read,isSure) in cells){reads[cell]=read;sure[cell]=isSure;}
   var judge=new SongJudge{Song=song};judge.Candidates.AddRange(candidates);
   return new ScanItem{Shot=new ShotRead(new Mask(1,1),null,reads,null,new Mask(1,1)),Judge=judge,Sure=sure,Questions=(song==null?1:0)+sure.Count(s=>!s)};
  }
  // SC 의 칸: 4B 12, 5B 13, 6B 14, 8B 15
  ScanResult Result(out ScanItem fixedItem,out ScanItem twoCandidates,out ScanItem settled,out ScanItem noCandidate)
  {
   fixedItem=Item(d,new[]{d},(12,Unsure("990000"),false),(13,Unsure("-"),false));                               // 곡은 정했고 칸 둘이 흐리다
   twoCandidates=Item(null,new[]{a,b,c},(12,Sure("995000"),true),(13,Sure("-"),true),(14,Unsure("970000"),false));  // 곡을 정하지 못했다. c 는 5B 가 없어 후보에서 빠진다
   settled=Item(b,new[]{b},(12,Sure("999000"),true),(13,Sure("998000"),true));                                    // 확인할 것이 없는 곡(b)
   noCandidate=Item(null,Array.Empty<Song>(),(12,Sure("960000"),true));                                           // 제목을 끝내 읽지 못했다
   return new ScanResult{End=ScanEnd.Done,Items={fixedItem,twoCandidates,settled,noCandidate}};
  }

  // 1. 줄 만들기
  {
   var review=CollectionReview.From(Result(out var f,out var two,out _,out var none));
   var g1=review.Groups.Single(g=>g.Item==f);var g2=review.Groups.Single(g=>g.Item==two);var g4=review.Groups.Single(g=>g.Item==none);
   Check(review.Groups.Count==3&&g1.Rows.Select(r=>(r.Cell,r.Kind)).SequenceEqual(new[]{(12,ReviewKind.Score),(13,ReviewKind.Score)})&&!g1.NeedsSong,"a song already decided: one 'score' row per unclear cell");
   Check(g2.Rows.Select(r=>(r.Cell,r.Kind,r.Read)).SequenceEqual(new[]{(12,ReviewKind.Song,"995000"),(14,ReviewKind.SongScore,"")})&&g2.Candidates.SequenceEqual(new[]{a,b}),"an undecided song: a 'song' row for each clear score, 'song & score' for unclear cells, no row for a clear '-'; candidates whose patterns fit the clear cells");
   Check(g4.Rows.Single() is {Kind:ReviewKind.Song,Read:"960000"}&&g4.Candidates.Count==0,"a title never read: no candidate");
   Check(review.Left==5&&review.Status=="확인할 항목 5건이 남았습니다."&&!review.CanConfirm&&review.NotFoundNote==CollectionReview.NotFound,"left: 2 scores + (1 song + 1 score) + 1 song; the not-found note is shown");
  }
  // 2. 점수 입력
  {
   var review=CollectionReview.From(Result(out var f,out _,out _,out _));var row=review.Groups.Single(g=>g.Item==f).Rows[0];
   foreach(var bad in new[]{"0","1000001","1,000"," 1","abc"}){row.Typed=bad;Check(row.Score==null&&row.ScoreBad&&!row.ScoreDone,$"'{bad}' is not a score and shows the note");}
   foreach(var (good,value) in new[]{("1",1),("1000000",1000000),("0001",1)}){row.Typed=good;Check(row.Score==value&&!row.ScoreBad&&row.ScoreDone,$"'{good}' is accepted");}
   row.Typed="";Check(!row.ScoreBad&&!row.ScoreDone,"an empty input is unchecked, without the note");
   row.None=true;Check(row.ScoreDone&&!row.ScoreBad,"'no record' settles the row");
   row.None=false;row.Typed="0";row.Excluded=true;Check(!row.ScoreBad,"an excluded row shows no score note");
  }
  // 3. 곡 고르기와 안내
  {
   var review=CollectionReview.From(Result(out var f,out var two,out _,out var none));
   var g2=review.Groups.Single(g=>g.Item==two);var g4=review.Groups.Single(g=>g.Item==none);
   g2.Chosen=b;Check(review.SongNotes(g2).SequenceEqual(new[]{CollectionReview.AlreadyRead})&&review.Duplicated,"a song read without questions is 'already read' and blocks confirming");
   g2.Chosen=a;g4.Chosen=a;
   Check(review.SongNotes(g2).SequenceEqual(new[]{CollectionReview.PickedElsewhere})&&review.SongNotes(g4).Contains(CollectionReview.PickedElsewhere)&&review.Duplicated,"the same song picked in two groups: both show 'picked elsewhere'");
   g4.Chosen=c;Check(!review.Duplicated&&review.SongNotes(g4).Count==0,"different songs: no note");
   g2.Chosen=c;Check(review.SongNotes(g2).Contains(CollectionReview.OtherPatterns)&&review.SongNotes(g2).Contains(CollectionReview.PickedElsewhere),"a song whose patterns differ from the screen warns (two notes together)");
   g2.Chosen=d;Check(review.SongNotes(g2).SequenceEqual(new[]{CollectionReview.AlreadyRead}),"a song decided in another reviewed group is also 'already read'");
  }
  // 4. 제외
  {
   var review=CollectionReview.From(Result(out var f,out var two,out _,out var none));
   var g1=review.Groups.Single(g=>g.Item==f);var g2=review.Groups.Single(g=>g.Item==two);var g4=review.Groups.Single(g=>g.Item==none);
   foreach(var r in g2.Rows)r.Excluded=true;
   Check(review.ProtectNote=="곡을 고르지 않아 후보 1곡의 기존 기록을 모두 유지합니다."&&review.Protected.Single()==a&&review.Left==3,"every row of a two-candidate group excluded without a song: the group is settled and protects its candidate, but not b (read elsewhere without questions)");
   foreach(var r in g4.Rows)r.Excluded=true;
   Check(review.Left==3&&review.NotFoundNote!=null,"a group with no candidate cannot be settled by excluding everything");
   g4.Chosen=c;foreach(var r in g4.Rows)r.Excluded=false;g1.Rows[0].Excluded=true;g1.Rows[1].None=true;
   Check(review.CanConfirm&&review.Status=="모든 항목을 확인했습니다.","all settled: confirm is allowed");
   var o=review.Outcome("p");
   Check(o.ProtectedSongs.SetEquals(new[]{"a"})&&o.Seen.IsSupersetOf(new[]{"a","b","c","d"})&&o.Protected.SetEquals(new[]{CollectionOutcome.Key("d",4,"SC")})&&o.Values[CollectionOutcome.Key("d",5,"SC")]==null&&o.Values[CollectionOutcome.Key("b",4,"SC")]==999000,
    "outcome: the remaining candidate protected and seen (b keeps its read values), the excluded pattern protected, 'no record' is null");
  }
  // 5. 결과
  {
   var review=CollectionReview.From(Result(out var f,out var two,out _,out var none));
   var g1=review.Groups.Single(g=>g.Item==f);var g2=review.Groups.Single(g=>g.Item==two);var g4=review.Groups.Single(g=>g.Item==none);
   g1.Rows[0].Typed="990000";g1.Rows[1].None=true;g2.Chosen=a;g2.Rows[1].Typed="970000";g4.Chosen=c;
   var o=review.Outcome("p");string K(string song,int button)=>CollectionOutcome.Key(song,button,"SC");
   Check(o.Values[K("d",4)]==990000&&o.Values[K("d",5)]==null&&o.Values[K("a",4)]==995000&&o.Values[K("a",5)]==null&&!o.Values.ContainsKey(K("a",6))&&o.Values[K("b",4)]==999000&&o.Values[K("b",5)]==998000&&o.Values[K("c",4)]==960000,
    "outcome values: typed scores, read scores on 'song' rows and rowless cells, '-' as null, and no value for a pattern the chosen song lacks");
   Check(o.Seen.SetEquals(new[]{"a","b","c","d"})&&o.Protected.Count==0&&o.ProtectedSongs.Count==0,"every song read is seen; nothing protected");
  }
  // 6. 모두 제외한 묶음도 고른 곡이 겹치면 확인을 막는다: 그 묶음도 줄이 없는 칸의 값('-')을 써서 다른 묶음의 점수를 덮는다
  {
   var one=Item(null,new[]{a,b},(12,Unsure("999777"),false),(13,Sure("-"),true));var two=Item(null,new[]{a,b},(12,Unsure("990000"),false),(13,Sure("-"),true));
   var review=CollectionReview.From(new ScanResult{End=ScanEnd.Done,Items={one,two}});var g1=review.Groups.Single(g=>g.Item==one);var g2=review.Groups.Single(g=>g.Item==two);
   g1.Chosen=a;g1.Rows.Single().Typed="999777";g2.Chosen=a;foreach(var r in g2.Rows)r.Excluded=true;
   Check(review.Duplicated&&!review.CanConfirm&&review.SongNotes(g2).Contains(CollectionReview.PickedElsewhere)&&review.SongNotes(g1).Contains(CollectionReview.PickedElsewhere),"a group with every row excluded still blocks when its song is also picked in another group");
   g2.Chosen=null;var cleared=review.Outcome("p");
   Check(review.CanConfirm&&cleared.Values[CollectionOutcome.Key("a",4,"SC")]==999777&&cleared.ProtectedSongs.SetEquals(new[]{"b"})&&!cleared.Guards(CollectionOutcome.Key("a",4,"SC"))&&review.ProtectNote=="곡을 고르지 않아 후보 1곡의 기존 기록을 모두 유지합니다.",
    "clearing the excluded group's song lets the other group's score through: the song picked elsewhere is not among the protected candidates");
   var only=Item(null,new[]{a},(12,Unsure("990000"),false),(13,Sure("-"),true));var mine=Item(null,new[]{a},(12,Unsure("995000"),false),(13,Sure("-"),true));
   var review3=CollectionReview.From(new ScanResult{End=ScanEnd.Done,Items={only,mine}});var h1=review3.Groups.Single(x=>x.Item==only);var h2=review3.Groups.Single(x=>x.Item==mine);
   h1.Chosen=a;h1.Rows.Single().Typed="990000";foreach(var r in h2.Rows)r.Excluded=true;
   Check(!review3.CanConfirm&&review3.Left==1&&review3.Protected.Count==0&&review3.SongNotes(h2).SequenceEqual(new[]{CollectionReview.TakenElsewhere})&&!review3.Duplicated,"a group whose every candidate is decided elsewhere cannot be settled by excluding everything, and says why on its song cell (not a duplicate)");
   h1.Chosen=null;Check(review3.SongNotes(h2).Count==0,"once the candidate is free again the note goes away");h1.Chosen=a;
   g2.Chosen=b;var o=review.Outcome("p");
   Check(review.CanConfirm&&o.Values[CollectionOutcome.Key("a",4,"SC")]==999777&&o.Values[CollectionOutcome.Key("b",5,"SC")]==null&&o.Protected.Contains(CollectionOutcome.Key("b",4,"SC")),"picking another song for the excluded group writes only that song");
  }
  // 7. 제목이 흔들려 곡을 정하지 못한 묶음은 곡을 고르지 않고 모두 제외할 수 없다(후보는 마지막에 본 제목의 곡이라 실제 곡이 아닐 수 있다)
  {
   var shaky=Item(null,new[]{b},(12,Unsure("990000"),false),(13,Unsure("-"),false));shaky.TitleUnsteady=true;shaky.Unsteady=true;
   var review=CollectionReview.From(new ScanResult{End=ScanEnd.Done,Items={shaky}});var g=review.Groups.Single();
   Check(g.TitleShaky&&review.ShakyNote==CollectionReview.Shaky&&review.Left==3,"a title that kept changing: one song to pick and two scores, with the note");
   foreach(var r in g.Rows)r.Excluded=true;
   Check(review.Left==1&&!review.CanConfirm&&review.ProtectNote==null&&review.Protected.Count==0,"excluding every row without a song does not settle it, and protects no candidate (the last title may be the wrong song)");
   g.Chosen=a;
   Check(review.CanConfirm&&review.ShakyNote==null&&review.Outcome("p").Seen.Contains("a")&&review.Outcome("p").Protected.SetEquals(new[]{CollectionOutcome.Key("a",4,"SC"),CollectionOutcome.Key("a",5,"SC")}),"picking the song settles it: the excluded patterns of the picked song are protected");
   // 4B 만 있는 두 곡(빈칸 13 과 맞는다)
   var steady=Item(null,new[]{c,S("e",4)},(12,Unsure("990000"),false));var review2=CollectionReview.From(new ScanResult{End=ScanEnd.Done,Items={steady}});foreach(var r in review2.Groups.Single().Rows)r.Excluded=true;
   Check(review2.CanConfirm&&review2.ShakyNote==null&&review2.Protected.Count==2&&review2.ProtectNote=="곡을 고르지 않아 두 후보 곡의 기존 기록을 모두 유지합니다.","a steady title with unclear cells follows the usual rule (excluding everything protects the candidates)");
  }
  // 8. 캡처 보기의 장면: 점수를 읽은 화면, 다시 잡은 화면들, 아티스트를 읽은 화면의 차례
  {
   var item=Item(null,new[]{a,b},(12,Unsure("990000"),false));item.Screen=new byte[]{1};item.TriedScreens=new(){new byte[]{2},new byte[]{3}};item.ArtistScreen=new byte[]{4};
   var shots=CaptureViewer.For(new ReviewGroup{Item=item});
   Check(shots.Select(s=>(s.Png[0],s.Scene)).SequenceEqual(new[]{((byte)1,"점수표"),((byte)2,"점수표(다시 잡은 1번째)"),((byte)3,"점수표(다시 잡은 2번째)"),((byte)4,"다음 곡으로 이동 후 · 오른쪽 곡 목록")})&&shots[3].Note=="이 장면의 점수표는 다른 곡입니다."&&shots.Take(3).All(s=>s.Note==null),
    "capture scenes: the read screen, then the screens taken again, then the artist scene with its note");
   item.TriedScreens=new();item.ArtistScreen=null;Check(CaptureViewer.For(new ReviewGroup{Item=item}).Count==1,"a steady song with one unclear cell has one capture");
  }
  File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"collection-review-validation.txt"),string.Join("\n",notes));
 }
}
