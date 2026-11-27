namespace LadderRecords;
// 컬렉션 가져오기: 끝까지 읽은 결과(와 판독 검수의 결과)로 지금 프로필에 무엇을 더하고 고치고 지울지 셈한다. 창이 없다(창은 CollectionWindow).
// 저장은 RecordStore.ImportCollection(한 작업으로 저장하고 직전 저장 되돌리기 한 번으로 돌아간다).

/// <summary>
/// 읽기가 끝까지 정상으로 끝난 뒤의 결과(판독 검수를 마친 것). 모든 키는 노말 판정의 키다(곡|버튼|패턴|노말).
/// Values: 점수(1~1,000,000) 또는 기록 없음(null. 게임에서 `-`). 패턴이 없는 빈칸은 들어오지 않는다(비교하지 않는다).
/// Protected: 제외한 키. ProtectedSongs: 곡 전체를 보호하는 곡(곡을 고르지 않고 묶음을 다 제외한 때의 남은 후보 곡).
/// Seen: 이번 게임 목록에 나온 곡. 보호한 후보 곡도 넣는다. 곡 목록에 있고 여기에 없는 곡은 모든 패턴을 기록 없음으로 본다
/// </summary>
sealed class CollectionOutcome
{
 public required string ProfileId{get;init;}
 public Dictionary<string,int?> Values{get;}=new(StringComparer.Ordinal);
 public HashSet<string> Protected{get;}=new(StringComparer.Ordinal);
 public HashSet<string> ProtectedSongs{get;}=new(StringComparer.Ordinal);
 public HashSet<string> Seen{get;}=new(StringComparer.Ordinal);
 /// <summary>보호하는 키인가(그 키는 어느 셈에도 들어가지 않는다: 추가·갱신·자동 삭제, 높은 노말 후보, 일괄 삭제)</summary>
 public bool Guards(string key)=>Protected.Contains(key)||ProtectedSongs.Contains(SongOf(key));
 public static string SongOf(string key){int at=key.IndexOf('|');return at<0?key:key[..at];}
 public static string Key(string songId,int button,string pattern)=>new Selection(songId,button,pattern,"노말",0).Key;
 /// <summary>읽기(CollectionScan)의 결과에서 만든다. 끝까지 정상으로 읽었고(1곡 이상) 확인할 것이 하나도 없을 때만 쓸 수 있다(있으면 판독 검수를 거친다)</summary>
 public static CollectionOutcome From(ScanResult result,string profileId)
 {
  if(result.End!=ScanEnd.Done||result.Items.Count==0)throw new InvalidOperationException("끝까지 읽은 결과가 아니다");
  if(result.Questions>0||result.Items.Any(i=>i.Judge.Song==null||i.Sure.Any(s=>!s)))throw new InvalidOperationException("확인할 것이 남은 결과다(판독 검수를 거친다)");
  var outcome=new CollectionOutcome{ProfileId=profileId};
  foreach(var item in result.Items)
  {
   var song=item.Judge.Song!;outcome.Seen.Add(song.Id);
   for(int i=0;i<16;i++)
   {
    var cell=item.Shot.Cells[i];if(cell.Blank)continue;   // 패턴이 없는 칸
    string key=Key(song.Id,CollectionScreen.Buttons[i%4],CollectionScreen.Patterns[i/4]);
    outcome.Values[key]=cell.None?null:cell.Score;
   }
  }
  return outcome;
 }
}

/// <summary>완료 화면에서 고르는 것: 일괄 삭제 둘(기본 체크)과 '컬렉션보다 높은 노말 기록' 가운데 고른 줄(기본은 아무것도 고르지 않는다).
/// 고른 줄은 순번이 아니라 판 전체(순번·판정 키·점수·시각·출처·노말 반영·연습)로 기억한다: 되돌리기는 다음 순번도 되돌리므로, 고른 판을 되돌린 뒤 저장한 다른 판이 같은 순번을 받는다.
/// 그 판에 옛 선택이 걸리지 않게 한다. 고른 판의 점수나 반영이 바뀌어도 더는 고른 것이 아니다(재확인에서 바뀐 줄의 체크만 풀리는 규칙과 같다)</summary>
sealed class CollectionChoices
{
 public bool DeleteVArchive{get;set;}=true;
 public bool DeleteOldRate{get;set;}=true;
 public HashSet<Play> Higher{get;}=new();
}

/// <summary>컬렉션 줄이 없어지는 까닭: 게임에서 `-` · 끝까지 읽었는데 이번 목록에 그 곡이 없음 · 같은 점수의 날짜 있는 노말 기록이 있음</summary>
enum CollectionDrop{NoRecord,NotInList,SameDated}

/// <summary>'컬렉션보다 높은 노말 기록'의 한 줄. Collection 은 견준 컬렉션 점수(기록 없음이면 null = 0점으로 견줌).
/// 노말에 반영된 하드·맥스 판이면 고를 때 노말 반영만 끊는다(원래 판은 남는다)</summary>
sealed record CollectionCandidate(Play Row,string NormalKey,int? Collection){public bool Unlink=>Row.Normal==NormalLink.Linked&&Row.Key!=NormalKey;}

/// <summary>한 번의 셈. 고르는 것(CollectionChoices)과 따로 셈해 두고, 고르는 것이 바뀌면 Changes·건수만 다시 본다</summary>
sealed class CollectionPlan
{
 public required string ProfileId{get;init;}
 /// <summary>새 컬렉션 줄(순번은 적용할 때 준다. 여기서는 0)</summary>
 public List<Play> Adds{get;}=new();
 /// <summary>점수가 바뀌는 컬렉션 줄(높아지든 낮아지든 게임의 지금 값으로)</summary>
 public List<PlayChange> Updates{get;}=new();
 /// <summary>저절로 없어지는 컬렉션 줄과 그 까닭</summary>
 public List<(Play Row,CollectionDrop Why)> Drops{get;}=new();
 /// <summary>일괄 삭제의 대상: 지금 프로필의 V아카이브 줄 전부, 노말의 옛 RATE 줄 전부(보호를 뺀)</summary>
 public List<Play> VArchive{get;}=new();
 public List<Play> OldRate{get;}=new();
 /// <summary>노말 키마다 이번에 읽은 값(기록 없음과 이번 목록에 없는 곡의 패턴은 null). 완료 화면이 '컬렉션 n점'·'컬렉션에 기록 없음'을 적을 때 쓴다</summary>
 public Dictionary<string,int?> Read{get;}=new(StringComparer.Ordinal);
 /// <summary>컬렉션보다 높은 노말 줄(모든 출처). 화면에 보일 것은 Visible: 일괄 삭제에 든 출처는 빠진다</summary>
 public List<CollectionCandidate> Higher{get;}=new();

 public IEnumerable<CollectionCandidate> Visible(CollectionChoices choices)=>Higher.Where(h=>!(choices.DeleteVArchive&&h.Row.Source==PlaySource.VArchive)&&!(choices.DeleteOldRate&&h.Row.Source==PlaySource.OldRate));

 /// <summary>적용할 변경(한 줄은 한 번만. 저절로 없어지는 줄과 일괄 삭제가 고른 후보보다 앞선다)</summary>
 public List<PlayChange> Changes(CollectionChoices choices)
 {
  var changes=new List<PlayChange>();var done=new HashSet<int>();
  void Remove(Play row){if(done.Add(row.Seq))changes.Add(new(row,null));}
  foreach(var add in Adds)changes.Add(new(null,add));
  foreach(var update in Updates){done.Add(update.Before!.Seq);changes.Add(update);}
  foreach(var (row,_) in Drops)Remove(row);
  if(choices.DeleteVArchive)foreach(var row in VArchive)Remove(row);
  if(choices.DeleteOldRate)foreach(var row in OldRate)Remove(row);
  foreach(var pick in Visible(choices))
  {
   if(!choices.Higher.Contains(pick.Row)||!done.Add(pick.Row.Seq))continue;
   changes.Add(pick.Unlink?new(pick.Row,pick.Row with{Normal=NormalLink.Cancelled}):new(pick.Row,null));
  }
  return changes;
 }
 public int AddCount=>Adds.Count;
 public int UpdateCount=>Updates.Count;
 /// <summary>삭제 예정 건수: 없어지는 줄과 노말 반영을 끊는 줄(한 줄은 한 번만)</summary>
 public int DeleteCount(CollectionChoices choices)=>Changes(choices).Count(c=>c.Before!=null&&(c.After==null||c.After.Normal==NormalLink.Cancelled&&c.Before.Normal==NormalLink.Linked));

 /// <summary>곡 목록에서 곡마다의 노말 키들(이번 목록에 없는 곡의 패턴을 기록 없음으로 셈할 때 쓴다)</summary>
 public static Dictionary<string,IReadOnlyList<string>> Charts(Catalog catalog)=>catalog.Songs.ToDictionary(s=>s.Id,s=>(IReadOnlyList<string>)s.Charts.Select(c=>CollectionOutcome.Key(s.Id,c.Button,c.Pattern)).ToList(),StringComparer.Ordinal);

 /// <summary>지금 프로필의 기록에서 셈한다(읽기만 한다). 결과의 프로필이 지금 프로필이 아니면 셈하지 않는다</summary>
 public static CollectionPlan Make(RecordStore store,IReadOnlyDictionary<string,IReadOnlyList<string>> charts,CollectionOutcome outcome)
 {
  var profile=store.Active;if(profile.Id!=outcome.ProfileId)throw new InvalidOperationException("결과를 읽은 프로필이 지금 프로필이 아니다");
  var plan=new CollectionPlan{ProfileId=profile.Id};
  // 셈할 키와 이번 값: 읽은 값, 그리고 이번 목록에 없는 곡의 모든 패턴(기록 없음)
  var values=new SortedDictionary<string,(int? Score,bool Missing)>(StringComparer.Ordinal);
  foreach(var (key,score) in outcome.Values)values[key]=(score,false);
  foreach(var (song,keys) in charts)if(!outcome.Seen.Contains(song))foreach(string key in keys)values.TryAdd(key,(null,true));
  var collection=profile.Plays.Where(p=>p.Source==PlaySource.Collection).GroupBy(p=>p.Key,StringComparer.Ordinal).ToDictionary(g=>g.Key,g=>g.First(),StringComparer.Ordinal);
  foreach(var (key,(score,missing)) in values)
  {
   plan.Read[key]=score;
   if(outcome.Guards(key))continue;
   var history=store.History(key,profile);collection.TryGetValue(key,out var kept);
   if(score is int s)
   {
    // 날짜가 있는 같은 점수(직접·수동·노말에 반영된 하드·맥스·옛 기록)가 있으면 그 판이 남고 컬렉션 줄은 두지 않는다
    bool dated=history.Any(p=>p.Source!=PlaySource.Collection&&p.At!=null&&p.Score==s);
    if(dated){if(kept!=null)plan.Drops.Add((kept,CollectionDrop.SameDated));}
    else if(kept==null)plan.Adds.Add(new Play(0,key,s,null,PlaySource.Collection));
    else if(kept.Score!=s)plan.Updates.Add(new(kept,kept with{Score=s}));
   }
   else if(kept!=null)plan.Drops.Add((kept,missing?CollectionDrop.NotInList:CollectionDrop.NoRecord));
   int floor=score??0;   // 기록 없음은 0점으로 견준다
   foreach(var row in history)if(row.Source!=PlaySource.Collection&&row.Score>floor)plan.Higher.Add(new(row,key,score));
  }
  foreach(var row in profile.Plays)
  {
   if(outcome.Guards(row.Key))continue;
   if(row.Source==PlaySource.VArchive)plan.VArchive.Add(row);
   else if(row.Source==PlaySource.OldRate&&RecordStore.NormalKey(row.Key)==null)plan.OldRate.Add(row);
  }
  return plan;
 }

 /// <summary>
 /// 사용자가 본 셈(seen)과 지금 기록으로 다시 셈한 것(now)이 다른 노말 키들. 같은 고른 것으로 견준다: 적용할 변경과 보이는 후보의 줄·점수가 하나라도 다르면 그 키다.
 /// 줄은 순번만이 아니라 판정 키·시각·출처·연습까지 견준다(같은 순번에 다른 판이 들어설 수 있다).
 /// 다르면 저장하지 않고 그 줄을 완료 화면의 재확인으로 다시 확인받는다(이미 읽은 값은 다시 읽지 않는다)
 /// </summary>
 public static List<string> Differences(CollectionPlan seen,CollectionPlan now,CollectionChoices choices)
 {
  static string NormalOf(Play row)=>RecordStore.NormalKey(row.Key)??row.Key;
  static string Who(Play? row)=>row==null?"-":$"{row.Seq}:{row.Key}:{row.At?.ToString("o")??"-"}:{row.Source}:{row.Practice}";
  static Dictionary<string,string> Sign(CollectionPlan plan,CollectionChoices choices)
  {
   var lines=new SortedDictionary<string,List<string>>(StringComparer.Ordinal);
   void Note(string key,string line){if(!lines.TryGetValue(key,out var list))lines[key]=list=new();list.Add(line);}
   foreach(var c in plan.Changes(choices)){var row=(c.Before??c.After)!;Note(NormalOf(row),$"change {Who(c.Before)}:{c.Before?.Score}:{c.Before?.Normal} > {c.After?.Score}:{c.After?.Normal}");}
   foreach(var h in plan.Visible(choices))Note(h.NormalKey,$"higher {Who(h.Row)}:{h.Row.Score}:{h.Row.Normal}:{h.Collection}");
   return lines.ToDictionary(p=>p.Key,p=>string.Join("\n",p.Value.OrderBy(l=>l,StringComparer.Ordinal)),StringComparer.Ordinal);
  }
  var a=Sign(seen,choices);var b=Sign(now,choices);
  return a.Keys.Union(b.Keys).Where(k=>!a.TryGetValue(k,out var x)||!b.TryGetValue(k,out var y)||x!=y).OrderBy(k=>k,StringComparer.Ordinal).ToList();
 }
}

/// <summary>컬렉션 가져오기를 적용한 결과. Saved 가 아니면 기록은 그대로다(Changed 면 다시 확인할 키와 지금의 셈)</summary>
enum CollectionSave{Saved,Nothing,Changed}
sealed record CollectionSaveResult(CollectionSave State,int Added,int Updated,int Deleted,IReadOnlyList<string> Changed,CollectionPlan Now);
