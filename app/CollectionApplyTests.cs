using System.Text.Json;
namespace LadderRecords;
// 컬렉션 가져오기의 적용(셈과 저장)을 창 없이 검사한다.
// 합성 기록만 쓰고, 실행 파일 옆의 test-data 아래에만 쓴다. 결과는 collection-apply-validation.txt
static class CollectionApplyTests
{
 public static void Run()
 {
  var notes=new List<string>();void Check(bool value,string name){if(!value)throw new Exception(name);notes.Add("PASS: "+name);}
  string root=Path.Combine(AppContext.BaseDirectory,"test-data","collection-apply-"+Guid.NewGuid().ToString("N"));
  string At(string name){string dir=Path.Combine(root,name);Directory.CreateDirectory(dir);return Path.Combine(dir,"records.json");}
  Selection Chart(string song,string judge,string pattern="SC")=>new(song,4,pattern,judge,0);
  var a=Chart("a","노말");var aHard=Chart("a","하드");var aHd=Chart("a","노말","HARD");var b=Chart("b","노말");var c=Chart("c","노말");
  var t=new DateTime(2026,11,26,10,0,0,DateTimeKind.Utc);
  // 곡 목록: 곡마다의 노말 키. a·b·c 세 곡이 있고 a 는 SC·HARD 두 패턴
  var charts=new Dictionary<string,IReadOnlyList<string>>(StringComparer.Ordinal){["a"]=new[]{a.Key,aHd.Key},["b"]=new[]{b.Key},["c"]=new[]{c.Key}};
  string State(RecordStore store)=>JsonSerializer.Serialize(new{store.Active.NextSeq,store.Active.Plays});
  CollectionOutcome Read(RecordStore store,params (Selection Chart,int? Score)[] values)
  {
   var o=new CollectionOutcome{ProfileId=store.Active.Id};o.Seen.UnionWith(new[]{"a","b","c"});
   foreach(var (chart,score) in values)o.Values[chart.Key]=score;return o;
  }
  CollectionSaveResult Apply(RecordStore store,CollectionOutcome o,CollectionChoices? choices=null){choices??=new();return store.ImportCollection(o,charts,CollectionPlan.Make(store,charts,o),choices);}
  Play Insert(RecordStore store,Selection chart,int score,DateTime? at,string source)
  {
   var profile=store.Active;var row=new Play(profile.NextSeq++,chart.Key,score,at,source);profile.Plays.Add(row);store.Save();return row;
  }
  Play? Kept(RecordStore store,Selection chart)=>store.Active.Plays.FirstOrDefault(p=>p.Source==PlaySource.Collection&&p.Key==chart.Key);

  // 1. 더하기, 같은 값 그대로, 높아지든 낮아지든 갱신, 되돌리기
  {
   var store=new RecordStore(At("basic"));string empty=State(store);
   var o=Read(store,(a,995000),(b,null));var plan=CollectionPlan.Make(store,charts,o);
   Check(plan.AddCount==1&&plan.Adds[0] is {Score:995000,At:null,Source:PlaySource.Collection}&&plan.UpdateCount==0&&plan.Drops.Count==0&&plan.DeleteCount(new())==0,"an empty profile: one collection row is added, '-' adds nothing (no 0-point row)");
   var saved=Apply(store,o);
   Check(saved is {State:CollectionSave.Saved,Added:1}&&Kept(store,a) is {Score:995000,At:null}&&store.Active.Undo.Count==1&&store.Active.Undo[^1].Kind==PlayOps.ImportCollection&&store.Recent(a.Key)==null,"it is saved as one step, untimed, and the import time is not a recent play");
   Check(Apply(store,o).State==CollectionSave.Nothing&&store.Active.Undo.Count==1,"importing the same result again changes nothing and leaves the undo step as it was");
   Check(Apply(store,Read(store,(a,990000))) is {State:CollectionSave.Saved,Updated:1}&&Kept(store,a)!.Score==990000,"a lower game value replaces the collection row (it is the latest value, not the best)");
   Check(Apply(store,Read(store,(a,999000))) is {Updated:1}&&Kept(store,a)!.Score==999000&&store.Active.Plays.Count(p=>p.Source==PlaySource.Collection)==1,"a higher one too, and there is still one collection row per pattern");
   store.Undo();store.Undo();store.Undo();
   Check(State(store)==empty,"three undos bring back the empty profile");
  }
  // 2. 날짜가 있는 같은 점수: 가져올 때(직접·수동·노말에 반영된 하드·옛 기록), 나중에 직접 저장할 때(등록·수동 추가·교체). 수정으로는 지우지 않는다
  {
   var store=new RecordStore(At("dated"));
   store.Add(a,995000,t);
   Check(CollectionPlan.Make(store,charts,Read(store,(a,995000))) is {AddCount:0,UpdateCount:0},"a dated play with the same score: no collection row is added");
   var hardStore=new RecordStore(At("dated-hard"));hardStore.Add(aHard,995000,t);
   Check(hardStore.History(a.Key).Count==1&&CollectionPlan.Make(hardStore,charts,Read(hardStore,(a,995000))).AddCount==0,"a hard play shown on normal with the same score counts as a dated one");
   var oldStore=new RecordStore(At("dated-old"));Insert(oldStore,a,995000,t,PlaySource.Old);
   Check(CollectionPlan.Make(oldStore,charts,Read(oldStore,(a,995000))).AddCount==0,"an old best record (its date is the v1.1 save time) counts as a dated one too");
   var rateStore=new RecordStore(At("dated-rate"));Insert(rateStore,a,995000,null,PlaySource.OldRate);
   Check(CollectionPlan.Make(rateStore,charts,Read(rateStore,(a,995000))).AddCount==1,"an untimed old RATE row is not a dated one");
   // 컬렉션이 먼저 있고 같은 점수의 날짜 있는 판이 이미 있으면 다시 가져올 때 컬렉션 줄이 없어진다
   var later=new RecordStore(At("dated-later"));Apply(later,Read(later,(a,995000)));Insert(later,a,995000,t,PlaySource.Manual);
   var drop=CollectionPlan.Make(later,charts,Read(later,(a,995000)));
   Check(drop.Drops.Count==1&&drop.Drops[0].Why==CollectionDrop.SameDated&&drop.DeleteCount(new())==1,"a collection row next to a dated play with the same score is dropped on the next import (reason: the same dated score)");
   // 나중에 직접 저장: 등록
   var reg=new RecordStore(At("save-after"));Apply(reg,Read(reg,(a,995000)));string withCollection=State(reg);
   var registered=reg.Register(a,995000,t);
   Check(Kept(reg,a)==null&&reg.History(a.Key).Count==1&&reg.Active.Undo[^1].Kind==PlayOps.Add&&reg.Active.Undo[^1].Plays.Count==2,"registering the same score removes the collection row in the same step");
   var view=UndoView.Of(reg.Active.Undo[^1],reg.Active.Name);
   Check(view.Question=="저장한 플레이를 삭제할까요?"&&view.Target==registered.Play,"the undo confirmation still reads as undoing one play");
   reg.Undo();
   Check(State(reg)==withCollection,"undoing the registration brings the collection row back");
   Check(reg.Register(a,990000,t) is not null&&Kept(reg,a) is {Score:995000},"a different score leaves the collection row");
   // 수동 추가와 최근 플레이 교체
   var man=new RecordStore(At("manual-after"));Apply(man,Read(man,(a,995000)));
   man.Add(a,995000,t,PlaySource.Manual);
   Check(Kept(man,a)==null,"a manual add of the same score removes it too");
   var rep=new RecordStore(At("replace-after"));rep.Add(a,990000,t,PlaySource.Manual);Apply(rep,Read(rep,(a,995000)));
   Check(Kept(rep,a) is {Score:995000},"(set-up) a lower manual play and a collection row");
   rep.Replace(a,995000);
   Check(Kept(rep,a)==null&&UndoView.Of(rep.Active.Undo[^1],rep.Active.Name).Question=="교체 전 점수로 되돌릴까요?","replacing the recent play with the same score removes it; the undo still reads as a replace");
   var edit=new RecordStore(At("edit-after"));var row=edit.Add(a,990000,t,PlaySource.Manual);Apply(edit,Read(edit,(a,995000)));
   edit.Edit(row.Seq,995000);
   Check(Kept(edit,a) is {Score:995000},"editing a play to the same score does not remove it");
  }
  // 3. 기록 없음(`-`)과 이번 목록에 없는 곡: 컬렉션 줄은 저절로 없어지고, 점수가 있는 노말 줄은 0점과 견준 후보가 된다
  {
   var store=new RecordStore(At("missing"));
   Apply(store,Read(store,(a,995000),(b,990000),(c,980000)));
   var played=store.Add(b,970000,t);
   var o=Read(store,(a,null));o.Seen.Remove("b");o.Seen.Remove("c");
   var plan=CollectionPlan.Make(store,charts,o);
   Check(plan.Drops.Select(d=>(d.Row.Key,d.Why)).OrderBy(d=>d.Key).SequenceEqual(new[]{(a.Key,CollectionDrop.NoRecord),(b.Key,CollectionDrop.NotInList),(c.Key,CollectionDrop.NotInList)}),"'-' and songs not in this list drop their collection rows, with the reason");
   Check(plan.Higher.Count==1&&plan.Higher[0] is {Collection:null} h&&h.Row==played&&plan.AddCount==0,"a positive normal row of a song not in the list is a candidate against 0 points; no 0-point row is made");
   Check(!plan.Higher.Any(x=>x.NormalKey==aHd.Key),"a pattern of a seen song that was not read (blank cell) is not compared");
  }
  // 4. 보호: 제외한 패턴, 곡 전체. 어느 셈에도 들지 않는다(일괄 삭제 포함)
  {
   var store=new RecordStore(At("protect"));
   Apply(store,Read(store,(a,995000),(b,990000)));store.Add(a,999000,t);store.Import(new Dictionary<Selection,decimal>{[a]=99.9m,[b]=99.0m,[c]=98.0m});
   var o=Read(store,(a,null),(b,null));o.Protected.Add(a.Key);o.ProtectedSongs.Add("c");o.Seen.Remove("c");
   var plan=CollectionPlan.Make(store,charts,o);
   Check(!plan.Drops.Any(d=>d.Row.Key==a.Key)&&!plan.Higher.Any(h=>h.NormalKey==a.Key)&&!plan.VArchive.Any(r=>r.Key==a.Key),"a protected pattern: its collection row stays, it gives no candidate and its V-ARCHIVE row is not bulk-deleted");
   Check(!plan.VArchive.Any(r=>r.Key==c.Key)&&!plan.Drops.Any(d=>d.Row.Key==c.Key)&&!plan.Higher.Any(h=>h.NormalKey==c.Key),"a protected song (all candidates excluded) is kept whole, even though it is not in the list");
   Check(plan.Drops.Any(d=>d.Row.Key==b.Key)&&plan.VArchive.Any(r=>r.Key==b.Key),"an unprotected pattern next to them is handled as usual");
  }
  // 5. 일괄 삭제 둘과 높은 노말 후보: 켜고 끔, 중복 없는 건수, 하드·맥스 반영은 노말 반영만 끊는다
  {
   var store=new RecordStore(At("delete"));
   store.Import(new Dictionary<Selection,decimal>{[a]=99.9m,[b]=90.0m});   // V아카이브 a 999000(컬렉션보다 높음), b 900000(낮음)
   Insert(store,c,990000,null,PlaySource.OldRate);Insert(store,Chart("c","하드"),995000,null,PlaySource.OldRate);   // 노말의 옛 RATE, 하드의 옛 RATE(대상이 아니다)
   var manual=store.Add(a,998000,t,PlaySource.Manual);var hard=store.Add(Chart("b","하드"),999500,t);   // 하드 999,500 은 노말에 반영된다
   var o=Read(store,(a,995000),(b,995000),(c,980000));var plan=CollectionPlan.Make(store,charts,o);var choices=new CollectionChoices();
   Check(plan.VArchive.Count==2&&plan.OldRate.Count==1&&plan.OldRate[0].Key==c.Key,"bulk targets: every V-ARCHIVE row, and the normal old RATE rows only");
   var visible=plan.Visible(choices).ToList();
   Check(visible.Select(v=>v.Row.Seq).OrderBy(s=>s).SequenceEqual(new[]{manual.Seq,hard.Seq})&&visible.Single(v=>v.Row==hard).Unlink,"with both bulk deletes on, the candidates are the higher manual play and the hard play shown on normal (to be unlinked)");
   Check(plan.DeleteCount(choices)==3&&plan.AddCount==3,"deletions by default: 2 V-ARCHIVE + 1 old RATE; the higher plays are not chosen");
   choices.DeleteVArchive=false;
   Check(plan.Visible(choices).Any(v=>v.Row.Source==PlaySource.VArchive&&v.Row.Key==a.Key)&&!plan.Visible(choices).Any(v=>v.Row.Source==PlaySource.VArchive&&v.Row.Key==b.Key)&&plan.DeleteCount(choices)==1,"V-ARCHIVE bulk off: only its rows higher than the collection appear as candidates");
   choices.DeleteVArchive=true;choices.Higher.Add(hard);choices.Higher.Add(manual);choices.Higher.Add(plan.VArchive[0]);
   Check(plan.DeleteCount(choices)==5,"a row both bulk-deleted and picked is counted once");
   var saved=store.ImportCollection(o,charts,plan,choices);
   var hardRow=store.Active.Plays.Single(p=>p.Seq==hard.Seq);
   Check(saved is {State:CollectionSave.Saved,Added:3,Deleted:5}&&hardRow.Normal==NormalLink.Cancelled&&hardRow.Score==999500&&!store.History(b.Key).Contains(hardRow)&&store.History(Chart("b","하드").Key).Contains(hardRow),"the hard play stays with its score; only its showing on normal is cancelled");
   Check(store.Active.Plays.Any(p=>p.Source==PlaySource.OldRate&&p.Key==Chart("c","하드").Key)&&store.Best(a.Key) is {Source:PlaySource.Collection,Score:995000},"the hard old RATE row is kept; the best is now the collection row");
   store.Undo();
   Check(store.Active.Plays.Single(p=>p.Seq==hard.Seq).Normal==NormalLink.Linked&&store.Active.Plays.Count(p=>p.Source==PlaySource.VArchive)==2&&!store.Active.Plays.Any(p=>p.Source==PlaySource.Collection)&&store.Best(a.Key) is {Score:999000},"one undo restores V-ARCHIVE, old RATE, the normal link and the best");
   store.Import(new Dictionary<Selection,decimal>{[a]=99.0m});
   Check(store.Active.Plays.Count(p=>p.Source==PlaySource.VArchive)==2,"(V-ARCHIVE import after undo works as before)");
  }
  // 6. 적용 직전의 재셈: 기록이 바뀌었으면 저장하지 않고 그 키를 돌려준다. 프로필이 다르면 저장하지 않는다
  {
   var store=new RecordStore(At("recheck"));var played=store.Add(a,999000,t);
   var o=Read(store,(a,995000),(b,990000));var seen=CollectionPlan.Make(store,charts,o);var choices=new CollectionChoices();choices.Higher.Add(played);
   string before=State(store);int steps=store.Active.Undo.Count;
   store.Edit(played.Seq,999500);steps++;before=State(store);
   var changed=store.ImportCollection(o,charts,seen,choices);
   Check(changed.State==CollectionSave.Changed&&changed.Changed.SequenceEqual(new[]{a.Key})&&State(store)==before&&store.Active.Undo.Count==steps,"a picked candidate's score changed: nothing is saved and that pattern is returned for another look");
   var fresh=new CollectionChoices();
   store.Add(b,999900,t);steps++;before=State(store);
   var added=store.ImportCollection(o,charts,changed.Now,fresh);
   Check(added.State==CollectionSave.Changed&&added.Changed.SequenceEqual(new[]{b.Key})&&State(store)==before,"a new higher play saved meanwhile: that pattern is returned");
   Check(store.ImportCollection(o,charts,added.Now,fresh).State==CollectionSave.Saved,"with the latest plan it is saved");
   var other=new CollectionOutcome{ProfileId="someone-else"};
   bool refused=false;try{store.ImportCollection(other,charts,added.Now,fresh);}catch(Exception){refused=true;}
   Check(refused,"a result read for another profile is not saved");
  }
  // 6-2. 고른 판을 되돌린 뒤 같은 순번으로 다른 판이 들어선 경우: 견주기가 알아채고, 옛 선택은 새 판에 걸리지 않는다
  {
   var store=new RecordStore(At("reuse"));store.Add(b,990000,t);
   var picked=store.Add(a,999000,t.AddHours(1));
   var o=Read(store,(a,995000));var seen=CollectionPlan.Make(store,charts,o);var choices=new CollectionChoices();choices.Higher.Add(picked);
   store.Undo();var again=store.Add(a,999000,t.AddHours(2),PlaySource.Manual);   // 등록을 되돌리면 다음 순번도 돌아간다: 새 판이 같은 순번을 받는다
   Check(again.Seq==picked.Seq,"(after an undo the next play gets the undone play's number)");
   string before=State(store);var r=store.ImportCollection(o,charts,seen,choices);
   Check(r.State==CollectionSave.Changed&&r.Changed.SequenceEqual(new[]{a.Key})&&State(store)==before,"another play with the same number and score is seen as a change: nothing is saved");
   Check(!r.Now.Changes(choices).Any(c=>c.Before==again),"the old pick does not apply to the new play");
   var saved=store.ImportCollection(o,charts,r.Now,choices);
   Check(saved.State==CollectionSave.Saved&&store.Active.Plays.Contains(again),"saved with the latest plan and the old picks, the new play stays");
   // 하드에서 반영된 판을 골랐다가 되돌리고, 맥스에서 같은 점수가 반영된 경우
   var store2=new RecordStore(At("reuse-linked"));var hardPick=store2.Add(Chart("a","하드"),999000,t);
   var o2=Read(store2,(a,995000));var seen2=CollectionPlan.Make(store2,charts,o2);var choices2=new CollectionChoices();choices2.Higher.Add(hardPick);
   store2.Undo();var max=store2.Add(Chart("a","맥스"),999000,t.AddHours(1));
   Check(hardPick.Normal==NormalLink.Linked&&max.Seq==hardPick.Seq&&max.Normal==NormalLink.Linked,"(the max play takes the undone hard play's number and is shown on normal)");
   var r2=store2.ImportCollection(o2,charts,seen2,choices2);
   Check(r2.State==CollectionSave.Changed&&r2.Changed.SequenceEqual(new[]{a.Key}),"a different linked play with the same number is seen as a change");
   var saved2=store2.ImportCollection(o2,charts,r2.Now,choices2);
   Check(saved2.State==CollectionSave.Saved&&store2.Active.Plays.Single(p=>p.Seq==max.Seq).Normal==NormalLink.Linked,"the old pick does not cancel the new max play's showing on normal");
  }
  // 7. 여러 줄을 한 번에 바꾸는 길(RecordStore.SetAll)은 줄마다 Set 을 부른 것과 결과가 같다: 순번 차례인 목록·뒤섞인 목록, 고침·지움·넣음, 겹치는 순번, 없는 줄
  {
   var random=new Random(83);int quick=0,same=0,thrown=0;
   for(int trial=0;trial<400;trial++)
   {
    int n=random.Next(0,40);var rows=new List<Play>();int seq=1;
    for(int i=0;i<n;i++){seq+=random.Next(1,4);rows.Add(new Play(seq,a.Key,random.Next(1,1000000),t,PlaySource.Play));}
    if(trial%5==4)rows=rows.OrderBy(_=>random.Next()).ToList();   // 순번 차례가 아닌 목록
    var changes=new List<(Play? Before,Play? After)>();int top=seq;
    // 고치거나 지울 줄은 대개 서로 다르게 고른다. 일곱 번에 한 번은 같은 줄을 두 번 고를 수 있게, 아홉 번에 한 번은 없는 줄을 하나 넣는다
    var pool=rows.OrderBy(_=>random.Next()).ToList();int taken=0;
    Play? Pick()=>trial%7==6&&rows.Count>0&&random.Next(3)==0?rows[random.Next(rows.Count)]:taken<pool.Count?pool[taken++]:null;
    for(int left=random.Next(0,25);left>0;left--)
    {
     int kind=random.Next(0,10);
     if(kind<3){if(Pick() is {} row)changes.Add((row,row with{Score=random.Next(1,1000000)}));}
     else if(kind<5){if(Pick() is {} row)changes.Add((row,null));}
     else if(kind<8)changes.Add((null,new Play(top+=random.Next(1,3),b.Key,random.Next(1,1000000),t,PlaySource.Manual)));
     else if(kind<9){int gap=random.Next(1,seq+2);if(!rows.Any(r=>r.Seq==gap)&&!changes.Any(c=>c.After?.Seq==gap))changes.Add((null,new Play(gap,b.Key,5000,t,PlaySource.Manual)));}   // 사이의 빈 순번에 되살리기
     else if(trial%4==3){if(Pick() is {} row)changes.Add((row,row with{Seq=row.Seq+1000}));}   // 순번이 바뀌는 고침(하나씩 가는 길)
    }
    if(trial%9==8)changes.Insert(random.Next(changes.Count+1),(new Play(seq+500,a.Key,1,t,PlaySource.Play),null));   // 없는 줄
    var one=new PlayProfile{Plays=new(rows)};var all=new PlayProfile{Plays=new(rows)};string? oneError=null,allError=null;
    try{foreach(var (before,after) in changes)RecordStore.Set(one,before,after);}catch(InvalidOperationException e){oneError=e.Message;}
    try{RecordStore.SetAll(all,changes);}catch(InvalidOperationException e){allError=e.Message;}
    if(oneError!=allError)throw new Exception($"SetAll threw differently in trial {trial}: {oneError} / {allError}");
    if(oneError!=null){thrown++;continue;}
    if(JsonSerializer.Serialize(one.Plays)!=JsonSerializer.Serialize(all.Plays))throw new Exception($"SetAll gave another list in trial {trial}");
    same++;if(changes.Count>1&&rows.Zip(rows.Skip(1)).All(p=>p.First.Seq<p.Second.Seq))quick++;
   }
   Check(same+thrown==400&&quick>=150&&thrown>=20,$"changing many rows at once gives the same list as one Set per row ({same} same, {thrown} refused alike, {quick} on sorted lists)");
  }
  // 8. 완료 화면의 글(CollectionDoneView): 항목과 건수, 줄의 글, 전체 선택, 재확인의 '검수 당시', 바뀐 줄의 체크만 풀기, 변경 0건
  {
   var catalog=new Catalog("test",DateTime.UnixEpoch,new(){new Song("a",1,"Alpha","Ann","p",new(){new Chart(4,"SC"),new Chart(4,"HARD")}),new Song("b",2,"Beta","Bob","p",new(){new Chart(4,"SC")}),new Song("c",3,"Gamma","Cid","p",new(){new Chart(4,"SC")})});
   var store=new RecordStore(At("done"));
   store.Import(new Dictionary<Selection,decimal>{[a]=99.50m});                                        // V아카이브 995,000(컬렉션보다 높다)
   var manual=store.Add(b,999000,t,PlaySource.Manual);                                                 // 컬렉션보다 높은 수동 판
   var hard=store.Add(Chart("c","하드"),999500,t);                                                      // 노말에 반영된 하드 판
   var oldRate=Insert(store,aHd,980000,null,PlaySource.OldRate);                                        // 옛 RATE
   var oldCollection=Insert(store,c,990000,null,PlaySource.Collection);                                 // c 는 '-' 로 읽혔다: 저절로 없어진다
   var o=Read(store,(a,990000),(aHd,970000),(b,995000),(c,null));var plan=CollectionPlan.Make(store,charts,o);var choices=new CollectionChoices();
   var groups=CollectionDoneView.Groups(plan,choices,catalog);
   Check(groups.Select(g=>g.Kind).SequenceEqual(new[]{DoneKind.VArchive,DoneKind.OldRate,DoneKind.Higher,DoneKind.Auto})&&groups.Select(g=>g.Name).SequenceEqual(new[]{"V아카이브","이전 버전 노말 RATE 기록","컬렉션보다 높은 노말 기록","예전 컬렉션 기록"}),"the four delete groups in their screen order, with their names");
   var v=groups[0].Rows.Single();var r=groups[1].Rows.Single();var auto=groups[3].Rows.Single();
   Check(v is {Title:"Alpha",Pattern:"4B · SC",Main:"99.50%",Sub:"컬렉션 990,000점"}&&r is {Pattern:"4B · HARD",Main:"98.00%",Sub:"컬렉션 970,000점"},"a V-ARCHIVE or old RATE row shows its RATE and the collection score");
   Check(groups[2].Rows.Count==2&&groups[2].Rows.Any(x=>x.Pick==manual&&x.Note=="수동 등록"&&x.Main=="999,000점"&&x.Sub=="컬렉션 995,000점")&&groups[2].Rows.Any(x=>x.Pick==hard&&x.Note=="하드판정에서 반영 · 하드판정 원본 유지"&&x.Sub=="컬렉션에 기록 없음"),"a higher normal row shows its source (or the reflected judge) and the collection score");
   Check(auto is {Title:"Gamma",Note:"이 패턴의 HIGH SCORE가 ‘-’로 표시되었습니다.",Main:"990,000점",Sub:"컬렉션에 기록 없음"},"an old collection row going away shows why");
   Check(CollectionDoneView.Picked(plan,choices)==CheckState.Unchecked,"'select all' starts unchecked");
   choices.Higher.Add(manual);Check(CollectionDoneView.Picked(plan,choices)==CheckState.Indeterminate,"one of two picked: 'select all' shows partly");
   choices.Higher.Add(hard);Check(CollectionDoneView.Picked(plan,choices)==CheckState.Checked,"both picked: 'select all' is checked");
   choices.DeleteVArchive=false;
   Check(CollectionDoneView.Groups(plan,choices,catalog).Single(g=>g.Kind==DoneKind.Higher).Rows.Any(x=>x.Pick?.Source==PlaySource.VArchive)&&CollectionDoneView.Picked(plan,choices)==CheckState.Indeterminate,"V-ARCHIVE bulk off: its higher row appears among the candidates (not picked)");
   choices.DeleteVArchive=true;
   var adds=CollectionDoneView.Adds(plan,catalog);
   Check(adds.Count==3&&adds.All(x=>x.Note=="추가"),"the add/update list has the three new collection rows");
   // 재확인: 고른 수동 판의 점수가 바뀌면 그 줄만 '검수 당시'를 달고 체크가 풀린다. 고른 하드 판은 그대로
   store.Edit(manual.Seq,999700);var now=CollectionPlan.Make(store,charts,o);CollectionDoneView.Keep(choices,now);
   var again=CollectionDoneView.Groups(now,choices,catalog,plan).Single(g=>g.Kind==DoneKind.Higher).Rows;
   var changed=again.Single(x=>x.Pick?.Seq==manual.Seq);
   Check(changed.Was=="검수 당시 999,000점"&&changed.Main=="999,700점"&&!choices.Higher.Contains(changed.Pick!)&&choices.Higher.Contains(hard)&&again.Single(x=>x.Pick==hard).Was==null,"recheck: only the changed row shows the old value and is unchecked; the other pick stays");
   // 고른 판의 점수가 컬렉션 아래로 내려가 후보에서 빠지면 그 항목에 '삭제 대상에서 빠짐' 줄로 한 번 보인다(건수에 들지 않는다)
   store.Edit(manual.Seq,990000);var lowered=CollectionPlan.Make(store,charts,o);
   var higherNow=CollectionDoneView.Groups(lowered,choices,catalog,now,store.Active.Plays).Single(g=>g.Kind==DoneKind.Higher).Rows;
   var gone=higherNow.Single(x=>x.Gone);
   Check(gone.Note.EndsWith("· 삭제 대상에서 빠짐")&&gone.Was=="검수 당시 999,700점"&&gone.Main=="990,000점"&&gone.Pick==null&&higherNow.Count(x=>!x.Gone)==1,"a picked row that fell below the collection shows once as 'out of the deletions', with its old and current score, and is not counted");
   // 되돌린 뒤 같은 순번을 받은 다른 판에는 옛 값을 붙이지 않는다
   var reuse=new RecordStore(At("done-reuse"));var first=reuse.Add(b,999000,t);var ro=Read(reuse,(a,990000),(b,995000));var seen2=CollectionPlan.Make(reuse,charts,ro);
   reuse.Undo();var other=reuse.Add(a,999500,t.AddHours(1));var now2=CollectionPlan.Make(reuse,charts,ro);
   var rows2=CollectionDoneView.Groups(now2,new CollectionChoices(),catalog,seen2,reuse.Active.Plays).Single(g=>g.Kind==DoneKind.Higher).Rows;
   Check(other.Seq==first.Seq&&rows2.Single(x=>x.Pick==other).Was=="검수 당시 없음"&&rows2.Single(x=>x.Gone).Main=="기록 없음","another play with the undone play's number gets '검수 당시 없음', not the old play's score; the undone play shows as gone");
   // 바뀐 판의 수: 일괄 삭제를 푼 V아카이브 판은 두 항목에 보여도 한 번, 지워진 판도 센다
   var vs=new RecordStore(At("done-count"));vs.Import(new Dictionary<Selection,decimal>{[a]=99.12m,[c]=98.00m});var vm=vs.Add(b,999000,t,PlaySource.Manual);
   var vo=Read(vs,(a,990000),(b,995000),(c,970000));var off=new CollectionChoices{DeleteVArchive=false};var vbefore=CollectionPlan.Make(vs,charts,vo);
   vs.Import(new Dictionary<Selection,decimal>{[a]=99.82m});var vnow=CollectionPlan.Make(vs,charts,vo);
   Check(CollectionDoneView.ChangedPlays(vbefore,vnow,off)==1,"a V-ARCHIVE play updated while its bulk delete is off counts once (it shows in two groups)");
   var on=new CollectionChoices();var cRow=vs.Active.Plays.Single(p=>p.Source==PlaySource.VArchive&&p.Key==c.Key);vbefore=CollectionPlan.Make(vs,charts,vo);
   vs.Delete(cRow.Seq);vs.Edit(vm.Seq,999500);vnow=CollectionPlan.Make(vs,charts,vo);
   Check(CollectionDoneView.ChangedPlays(vbefore,vnow,on)==2,"a deleted V-ARCHIVE play and an edited manual play count as two");
   // 후보에서 빠진 반영 판에는 출처만('원본 유지'를 붙이지 않는다)
   var hs=new RecordStore(At("done-gone-hard"));var linked=hs.Add(Chart("c","하드"),999000,t);var ho=Read(hs,(c,990000));var hbefore=CollectionPlan.Make(hs,charts,ho);
   hs.Delete(linked.Seq);var hnow=CollectionPlan.Make(hs,charts,ho);
   var ghost=CollectionDoneView.Groups(hnow,new CollectionChoices(),catalog,hbefore,hs.Active.Plays).Single(g=>g.Kind==DoneKind.Higher).Rows.Single();
   Check(ghost is {Gone:true,Note:"하드판정에서 반영 · 삭제 대상에서 빠짐",Main:"기록 없음"},$"a reflected hard play whose original was deleted shows only its source [{ghost.Note}]");
   // 바뀔 것이 없는 결과
   var empty=new RecordStore(At("done-empty"));var none=Read(empty,(c,null));
   Check(CollectionDoneView.Nothing(CollectionPlan.Make(empty,charts,none),new CollectionChoices()),"nothing to add, update or delete and no candidate: '변경할 기록이 없습니다.'");
  }
  // 9. 되돌리기 확인의 글: 컬렉션 가져오기, 컬렉션 일괄 삭제, 같은 점수의 컬렉션 줄이 돌아오는 한 줄
  {
   var store=new RecordStore(At("undo-view"));
   Apply(store,Read(store,(a,990000),(b,980000)));                                   // 컬렉션 a·b
   var hard=store.Add(Chart("c","하드"),999000,t);                                     // 노말 c 에 반영된 하드 판
   var o=Read(store,(a,995000),(b,980000),(c,990000));var plan=CollectionPlan.Make(store,charts,o);var choices=new CollectionChoices();choices.Higher.Add(hard);
   Check(store.ImportCollection(o,charts,plan,choices).State==CollectionSave.Saved,"(a collection import that adds c, updates a and unlinks the hard play)");
   var view=UndoView.Of(store.Active.Undo[^1],"기본");
   Check(view is {Question:"이번 가져오기를 되돌릴까요?",Work:"컬렉션 가져오기",Target:null}&&view.Counts.SequenceEqual(new[]{("삭제할 새 기록",1),("이전 값으로 복원할 기록",1),("복원할 기록",1)}),
    $"undoing a collection import: the unlinked hard play is counted with the rows that come back, not with the updated ones [{string.Join(", ",view.Counts)}]");
   store.DeleteSource(PlaySource.Collection);view=UndoView.Of(store.Active.Undo[^1],"기본");
   Check(view is {Question:"일괄 삭제한 기록을 복원할까요?",Work:"컬렉션 일괄 삭제"}&&view.Counts.SequenceEqual(new[]{("복원할 컬렉션 기록",3)}),"undoing a collection bulk delete shows how many collection rows come back");
   store.Undo();
   store.Add(b,980000,t.AddHours(1));view=UndoView.Of(store.Active.Undo[^1],"기본");
   Check(view is {Question:"저장한 플레이를 삭제할까요?"}&&view.Target is {Score:980000}&&view.Notes.SequenceEqual(new[]{UndoView.SameCollection})&&store.Active.Undo[^1].Plays.Count==2,"a saved play that took the place of the same collection score says the collection row comes back");
   store.Add(a,990000,t.AddHours(2));store.Replace(a,995000);view=UndoView.Of(store.Active.Undo[^1],"기본");
   Check(view is {Question:"교체 전 점수로 되돌릴까요?"}&&view.Notes.SequenceEqual(new[]{UndoView.SameCollection}),"a replacement that reached the collection score says the same");
   store.Add(a,991000,t.AddHours(3));view=UndoView.Of(store.Active.Undo[^1],"기본");
   Check(view.Notes.Count==0,"a play with a different score says nothing about the collection");
  }
  File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"collection-apply-validation.txt"),string.Join("\n",notes));
 }

 /// <summary>
 /// --collection-apply-measure &lt;result.json&gt; &lt;결과 파일&gt;: 실제 게임에서 읽은 값(--collection-scan-window 가 결과 폴더에 남기는 result.json. 곡을 다 정한 것)으로 셈·저장·다시 가져오기·되돌리기를 재고 건수를 맞춰 본다.
 /// 프로필은 합성이다(빈 것, 출처가 섞인 것). 읽은 값은 곡 목록의 패턴만 쓴다(그 밖의 칸은 패턴이 없는 빈칸이다). 실행 파일 옆의 test-data 아래에만 쓴다. 시간은 이 PC 에서 한 번 잰 값
 /// </summary>
 public static void Measure(Catalog catalog,string resultPath,string outPath)
 {
  var lines=new List<string>();var watch=new System.Diagnostics.Stopwatch();
  double Time(Action act){watch.Restart();act();return watch.Elapsed.TotalMilliseconds;}
  void Must(bool value,string name){if(!value)throw new Exception(name);lines.Add("PASS: "+name);}
  var charts=CollectionPlan.Charts(catalog);var songs=catalog.Songs.ToDictionary(s=>s.Id,StringComparer.Ordinal);
  var read=new List<(string Key,string Song,int Button,string Pattern,int? Score)>();var seen=new HashSet<string>(StringComparer.Ordinal);
  using(var doc=JsonDocument.Parse(File.ReadAllText(resultPath)))
   foreach(var row in doc.RootElement.EnumerateArray())
   {
    string id=row.GetProperty("id").GetString()??throw new Exception("곡을 정하지 못한 줄이 있다");seen.Add(id);
    foreach(var chart in songs[id].Charts)
    {
     string text=row.GetProperty("score").GetProperty($"{chart.Button}B {chart.Pattern}").GetString()!;
     read.Add((CollectionOutcome.Key(id,chart.Button,chart.Pattern),id,chart.Button,chart.Pattern,text=="-"?null:int.Parse(text)));
    }
   }
  int numbers=read.Count(r=>r.Score!=null),missingKeys=charts.Where(c=>!seen.Contains(c.Key)).Sum(c=>c.Value.Count);
  lines.Add($"읽은 곡 {seen.Count}(곡 목록 {catalog.Songs.Count}), 곡 목록의 패턴 {read.Count}(점수 {numbers} · 기록 없음 {read.Count-numbers}), 이번 목록에 없는 곡의 패턴 {missingKeys}");
  string root=Path.Combine(AppContext.BaseDirectory,"test-data","collection-apply-measure-"+Guid.NewGuid().ToString("N"));
  string At(string name){string dir=Path.Combine(root,name);Directory.CreateDirectory(dir);return Path.Combine(dir,"records.json");}
  CollectionOutcome Outcome(RecordStore store){var o=new CollectionOutcome{ProfileId=store.Active.Id};o.Seen.UnionWith(seen);foreach(var r in read)o.Values[r.Key]=r.Score;return o;}
  string State(RecordStore store)=>JsonSerializer.Serialize(new{store.Active.NextSeq,store.Active.Plays});
  var t=new DateTime(2026,11,26,10,0,0,DateTimeKind.Utc);

  // 1. 빈 프로필: 점수 칸마다 컬렉션 줄 하나가 생기고, 다시 가져오면 바뀌는 것이 없고, 되돌리면 비어 있다
  {
   string path=At("empty");var store=new RecordStore(path);string before=State(store);var o=Outcome(store);
   CollectionPlan plan=null!;double make=Time(()=>plan=CollectionPlan.Make(store,charts,o));
   CollectionSaveResult saved=null!;double save=Time(()=>saved=store.ImportCollection(o,charts,plan,new()));
   long size=new FileInfo(path).Length;RecordStore again=null!;double load=Time(()=>again=new RecordStore(path));
   lines.Add($"빈 프로필: 셈 {make:0}ms, 저장(다시 셈·견줌·적용·파일 쓰기) {save:0}ms, 파일 {size/1024.0:0}KiB, 다시 열기 {load:0}ms");
   Must(saved is {State:CollectionSave.Saved}&&saved.Added==numbers&&saved.Updated==0&&saved.Deleted==0,$"an empty profile: {numbers} collection rows are added, one per score cell, and nothing else");
   Must(again.Active.Plays.Count(p=>p.Source==PlaySource.Collection)==numbers&&read.Where(r=>r.Score!=null).All(r=>again.History(r.Key).Count==1&&again.History(r.Key)[0] is {At:null,Source:PlaySource.Collection} row&&row.Score==r.Score),"the file read back has each score once, untimed");
   var o2=Outcome(again);CollectionSaveResult twice=null!;double re=Time(()=>twice=again.ImportCollection(o2,charts,CollectionPlan.Make(again,charts,o2),new()));
   Must(twice.State==CollectionSave.Nothing&&again.Active.Undo.Count==1,$"importing the same result again changes nothing ({re:0}ms)");
   UndoView view=null!;double shown=Time(()=>view=UndoView.Of(again.Active.Undo[^1],again.Active.Name));
   lines.Add($"  되돌리기 확인에 보일 것({shown:0}ms): {view.Work} · {string.Join(", ",view.Counts.Select(c=>$"{c.Name} {c.Count}"))}");
   double undo=Time(()=>again.Undo());
   Must(State(again)==before,$"one undo returns the profile to empty ({undo:0}ms)");
  }

  // 2. 출처가 섞인 프로필: 패턴마다 V아카이브 줄, 다섯에 하나 같은 점수의 직접 판(날짜 있음), 열셋에 하나 같은 점수의 하드 판(노말 반영),
  //    일곱에 하나 더 높은 수동 판, 열하나에 하나 옛 RATE 줄, 셋에 하나 지난번 컬렉션 줄(점수가 다르다).
  //    V아카이브 줄은 게임에 점수가 있는 패턴에만 둔 것(있을 법한 크기)과 모든 패턴에 둔 것(가장 큰 크기) 둘을 잰다
  foreach(bool all in new[]{false,true})
  {
   string path=At(all?"mixed-all":"mixed");var seed=new RecordStore(path);var profile=seed.Active;int i=0;
   foreach(var r in read)
   {
    i++;int score=r.Score??0;
    if(r.Score!=null||all)profile.Plays.Add(new Play(profile.NextSeq++,r.Key,(r.Score??400000)/100*100,null,PlaySource.VArchive));   // RATE 를 환산한 출처는 100점 단위
    if(r.Score!=null&&i%5==0)profile.Plays.Add(new Play(profile.NextSeq++,r.Key,score,t.AddMinutes(i),PlaySource.Play));
    if(r.Score!=null&&i%13==0)profile.Plays.Add(new Play(profile.NextSeq++,new Selection(r.Song,r.Button,r.Pattern,"하드",0).Key,score,t.AddMinutes(i),PlaySource.Play,NormalLink.Linked));
    if(i%7==0&&score<1000000)profile.Plays.Add(new Play(profile.NextSeq++,r.Key,Math.Min(1000000,score+1000),t.AddMinutes(i),PlaySource.Manual));
    if(i%11==0)profile.Plays.Add(new Play(profile.NextSeq++,r.Key,Math.Max(100,(score-500)/100*100),null,PlaySource.OldRate));
    if(i%3==0)profile.Plays.Add(new Play(profile.NextSeq++,r.Key,Math.Max(1,score-100),null,PlaySource.Collection));
   }
   seed.Save();var store=new RecordStore(path);string before=State(store);int rows=store.Active.Plays.Count;var o=Outcome(store);
   CollectionPlan plan=null!;double make=Time(()=>plan=CollectionPlan.Make(store,charts,o));var choices=new CollectionChoices();
   int visible=plan.Visible(choices).Count();double changes=Time(()=>plan.Changes(choices));
   lines.Add($"섞인 프로필(V아카이브 {(all?"모든 패턴":"점수 있는 패턴")}) {rows}줄: 셈 {make:0}ms(변경 목록 {changes:0}ms) — 더함 {plan.AddCount}, 갱신 {plan.UpdateCount}, 없어짐 {string.Join("·",plan.Drops.GroupBy(d=>d.Why).Select(g=>$"{g.Key} {g.Count()}"))}, V아카이브 {plan.VArchive.Count}, 옛 RATE {plan.OldRate.Count}, 높은 노말 {plan.Higher.Count}(보일 것 {visible}), 삭제 예정 {plan.DeleteCount(choices)}");
   CollectionSaveResult saved=null!;double save=Time(()=>saved=store.ImportCollection(o,charts,plan,choices));
   long size=new FileInfo(path).Length;var again=new RecordStore(path);
   lines.Add($"  저장 {save:0}ms, 파일 {size/1024.0:0}KiB, 더함 {saved.Added} · 갱신 {saved.Updated} · 삭제 {saved.Deleted}");
   // 뒤에 남을 것: 점수 칸은 날짜 있는 같은 점수가 없을 때만 컬렉션 줄 하나, 기록 없음과 날짜 있는 같은 점수는 컬렉션 줄 없음
   bool Right((string Key,string Song,int Button,string Pattern,int? Score) r)
   {
    var kept=again.Active.Plays.Where(p=>p.Source==PlaySource.Collection&&p.Key==r.Key).ToList();
    bool dated=r.Score!=null&&again.History(r.Key).Any(p=>p.Source!=PlaySource.Collection&&p.At!=null&&p.Score==r.Score);
    return r.Score==null||dated?kept.Count==0:kept.Count==1&&kept[0].Score==r.Score;
   }
   Must(saved.State==CollectionSave.Saved&&read.All(Right),"a mixed profile: each score cell has one collection row unless a dated play has the same score, and '-' cells have none");
   Must(again.Active.Plays.All(p=>p.Source!=PlaySource.VArchive)&&again.Active.Plays.All(p=>!(p.Source==PlaySource.OldRate&&RecordStore.NormalKey(p.Key)==null)),"the two bulk deletes (checked by default) remove every V-ARCHIVE row and normal old RATE row");
   Must(again.Active.Plays.Count(p=>p.Source==PlaySource.Manual)==read.Select((r,n)=>(r,n:n+1)).Count(x=>x.n%7==0&&(x.r.Score??0)<1000000),"higher normal rows are kept when none is picked");
   UndoView view=null!;double shown=Time(()=>view=UndoView.Of(again.Active.Undo[^1],again.Active.Name));
   lines.Add($"  되돌리기 확인에 보일 것({shown:0}ms): {view.Work} · {string.Join(", ",view.Counts.Select(c=>$"{c.Name} {c.Count}"))}");
   double undo=Time(()=>again.Undo());
   Must(State(again)==before,$"one undo returns the mixed profile as it was ({undo:0}ms)");
  }
  File.WriteAllText(outPath,string.Join("\n",lines)+"\n");
 }
}
