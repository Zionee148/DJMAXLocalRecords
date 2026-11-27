using System.Text.Json;
namespace LadderRecords;
// 판 단위 저장과 되돌리기를 검사한다. 기록 히스토리가 기대는 규칙을 창 없이 저장층에서 본다(창은 RecordsChecks·ManualFeatureChecks 가 본다).
// 그 밖에 등록을 확정하는 부름(7), 직전 저장 되돌리기의 확인이 보일 것과 승인의 규칙(8), 예전 개발 빌드가 남긴 판정 단위 삭제의 되돌리기(9)도 본다
// 합성 기록만 쓰고, 실행 파일 옆의 test-data 아래에만 쓴다. 결과는 play-ops-validation.txt
static class PlayOpsTests
{
 public static void Run()
 {
  var notes=new List<string>();void Check(bool value,string name){if(!value)throw new Exception(name);notes.Add("PASS: "+name);}
  string root=Path.Combine(AppContext.BaseDirectory,"test-data","play-ops-"+Guid.NewGuid().ToString("N"));
  string At(string name){string dir=Path.Combine(root,name);Directory.CreateDirectory(dir);return Path.Combine(dir,"records.json");}
  Selection Chart(string song,string judge)=>new(song,4,"SC",judge,0);
  var normal=Chart("a","노말");var hard=Chart("a","하드");var max=Chart("a","맥스");
  var t=new DateTime(2026,11,23,10,0,0,DateTimeKind.Utc);
  // 한 프로필의 기록 전체(되돌리기 내역은 빼고). 되돌린 뒤에 "작업 전과 똑같은가"를 이것으로 견준다
  string State(RecordStore store)=>JsonSerializer.Serialize(new{store.Active.NextSeq,store.Active.Plays,Recent=store.Active.Recent.OrderBy(r=>r.Key,StringComparer.Ordinal).ToList()});
  string Fails(Action act){try{act();}catch(Exception e)when(e is not InvalidRecordsException){return e.Message;}return "";}

  // 1. 저장한 판은 모두 남는다. 최고 기록, PB 한 판, 최근 플레이
  {
   string path=At("plays");var store=new RecordStore(path);
   var low=store.Add(normal,990000,t);var same=store.Add(normal,995000,t.AddMinutes(1));var tie=store.Add(normal,995000,t.AddMinutes(2));var best=store.Add(normal,998000,t.AddMinutes(3));var after=store.Add(normal,997000,t.AddMinutes(4));
   Check(store.History(normal.Key).Select(p=>p.Seq).SequenceEqual(new[]{1,2,3,4,5})&&store.Active.NextSeq==6&&store.Active.Undo.Count==5,"every saved play is its own row: lower, equal and higher ones");
   Check(store.Best(normal.Key)==best&&store.Pb(normal.Key)==best&&store.Recent(normal.Key)==t.AddMinutes(4),"the best is the highest score; the recent time is the latest play, whatever its score");
   store.Delete(best.Seq);
   Check(store.Best(normal.Key)==after&&store.Pb(normal.Key)==after,"with the best row gone the best is found again from the rows that are left");
   store.Delete(after.Seq);
   Check(store.Pb(normal.Key)==tie&&store.Best(normal.Key)!.Score==995000,"two rows share the best score: the PB mark goes to the later play");
   var sameTime=store.Add(normal,995000,t.AddMinutes(2));
   Check(store.Pb(normal.Key)==sameTime,"the same score at the same time: the one registered later");
   var reopened=new RecordStore(path);
   Check(State(reopened)==State(store)&&reopened.Pb(normal.Key)==sameTime&&reopened.Active.Undo.Count==8,"it is all the same after reopening the file");
   // 일시 미상 기록과 PB: 시각이 있는 판과 동점이면 시각이 있는 판에 붙이고, 일시 미상만 최고점이면 판에는 붙이지 않는다
   store.Import(new Dictionary<Selection,decimal>{[normal]=99.5m});
   Check(store.Best(normal.Key)!.Score==995000&&store.Pb(normal.Key)==sameTime,"an untimed row with the same score: the PB mark stays on the timed play");
   store.Import(new Dictionary<Selection,decimal>{[normal]=99.9m});
   Check(store.Best(normal.Key) is {Score:999000,At:null,Source:PlaySource.VArchive}&&store.Pb(normal.Key)==null&&store.Recent(normal.Key)==t.AddMinutes(2),"only an untimed row has the best score: no play carries the PB mark, and the recent time is still a real play");
  }
  // 2. 수동으로 더하기와 '최근 플레이 1건 교체'
  {
   var store=new RecordStore(At("replace"));
   Check(store.ReplaceTarget(normal.Key)==null&&Fails(()=>store.Replace(normal,1)).Contains("교체할 최근 플레이가 없습니다"),"nothing to replace on an empty chart");
   var today=store.Add(normal,990000,t);var yesterday=store.Add(normal,980000,t.AddDays(-1),PlaySource.Manual);
   Check(store.ReplaceTarget(normal.Key)==today,"a play added afterwards but dated earlier does not become the one to replace: the latest play time wins");
   var twin=store.Add(normal,985000,t,PlaySource.Manual);
   Check(store.ReplaceTarget(normal.Key)==twin,"two plays at the same time: the one registered later");
   var replaced=store.Replace(normal,991234);
   Check(replaced==twin with{Score=991234}&&store.Active.Plays.Count==3&&store.History(normal.Key).Single(p=>p.Seq==twin.Seq)==replaced&&store.Active.Undo[^1].Kind==PlayOps.Replace,"replacing changes the score of that row only: no new row, the time kept");
   store.Undo();
   Check(store.History(normal.Key).Single(p=>p.Seq==twin.Seq)==twin,"undo puts the replaced score back");
   // 옛 기록, 가져온 기록, 다른 판정에서 반영된 판은 교체 대상이 아니다
   var other=new RecordStore(At("replace-none"));
   other.Import(new Dictionary<Selection,decimal>{[normal]=99m});other.Add(hard,999000,t);
   Check(other.History(normal.Key).Count==2&&other.ReplaceTarget(normal.Key)==null&&other.ReplaceTarget(hard.Key)!.Score==999000,"a fetched row and a play carried over from hard are not replaceable on normal; the hard play is, on hard");
   var kept=other.Replace(hard,999500);
   Check(kept.Normal==NormalLink.Linked&&other.Best(normal.Key)!.Score==999500,"replacing a play that is shown on normal keeps the link: normal changes with it");
  }
  // 3. 하드·맥스와 노말: 한 판으로 저장하고 노말에도 보인다. 정하는 것은 새로 저장하는 순간뿐이다
  {
   var store=new RecordStore(At("normal"));
   store.Add(normal,990000,t);
   var below=store.Add(hard,985000,t.AddMinutes(1));var equal=store.Add(hard,990000,t.AddMinutes(2));var above=store.Add(hard,995000,t.AddMinutes(3));
   Check(below.Normal==null&&equal.Normal==null&&above.Normal==NormalLink.Linked&&store.Active.Plays.Count==4,"a hard play is shown on normal only when it beats the normal best at that moment (equal is not beating); it stays one row");
   Check(store.History(normal.Key).Select(p=>p.Seq).SequenceEqual(new[]{1,above.Seq})&&store.Best(normal.Key)==above&&store.History(hard.Key).Count==3,"normal shows its own play and that hard play; hard shows only its own");
   var practice=store.Add(max,999000,t.AddMinutes(4),practice:true);
   Check(practice is {Practice:true,Normal:null}&&store.Best(normal.Key)==above,"a practice or voided match is saved on its own judge only");
   // 수정: 반영된 판을 고치면 노말도 함께 바뀐다(노말 최고 아래로 내려가도 연결은 그대로). 반영되지 않았던 판을 고쳐 노말 최고를 넘어도 반영되지 않는다
   store.Edit(above.Seq,980000);
   Check(store.History(normal.Key).Single(p=>p.Seq==above.Seq) is {Score:980000,Normal:NormalLink.Linked}&&store.Best(normal.Key)!.Score==990000,"editing the linked play changes it on both; the old normal play is the best again");
   store.Edit(below.Seq,999999);
   Check(store.History(hard.Key).Single(p=>p.Seq==below.Seq) is {Score:999999,Normal:null}&&store.Best(normal.Key)!.Score==990000&&store.Best(hard.Key)!.Score==999999,"editing a play that was never linked does not link it afterwards, even above the normal best");
   // 노말 쪽에서 지우면 연결만 끊긴다. 원본은 남고, 그 뒤에 원본을 고쳐도 다시 이어지지 않는다
   store.CancelNormal(above.Seq);
   Check(store.History(normal.Key).Count==1&&store.History(hard.Key).Single(p=>p.Seq==above.Seq).Normal==NormalLink.Cancelled&&Fails(()=>store.CancelNormal(above.Seq)).Contains("노말에 반영된 기록이 아닙니다"),"removing it on the normal side only cuts the link; the hard play stays");
   // 되돌리기를 묻는 창이 보일 것(UndoView): 노말 반영만 끊은 작업은 '노말판정에 다시 반영할 기록'과 원래 기록이 남는다는 안내를, 반영된 판을 더한 작업은 노말 기록도 함께 사라진다는 안내를 보인다
   var cut=UndoView.Of(store.Active.Undo[^1],"기본");
   var addedLinked=UndoView.Of(new Operation(PlayOps.Add,t,9,new(){new(null,above with{Normal=NormalLink.Linked})}),"기본");
   var edited=UndoView.Of(new Operation(PlayOps.Edit,t,9,new(){new(above with{Normal=null},above with{Normal=null,Score=above.Score-1})}),"기본");
   Check(cut is {Question:"이 플레이의 노말판정 반영을 복원할까요?",TargetName:"노말판정에 다시 반영할 기록"}&&cut.Notes.SequenceEqual(new[]{"원래 하드판정 기록은 유지됩니다."})
    &&addedLinked is {Question:"저장한 플레이를 삭제할까요?",TargetName:"삭제할 기록"}&&addedLinked.Notes.SequenceEqual(new[]{"같은 플레이의 노말판정 기록(하드판정에서 반영)도 함께 삭제됩니다."})
    &&edited is {Question:"수정 전 점수로 되돌릴까요?"}&&edited.Notes.Count==0,"the undo question says what comes back when only the normal link is restored");
   store.Edit(above.Seq,1000000);
   Check(store.History(normal.Key).Count==1&&store.Best(normal.Key)!.Score==990000,"a cut link does not come back when the original is edited");
   store.Undo();store.Undo();
   Check(store.History(normal.Key).Count==2,"undoing the cut brings the link back as it was");
   // 원본을 지우면 노말에서도 사라진다. 노말에 직접 넣은 판을 지워도 반영되지 않았던 하드 판이 올라오지 않는다
   store.Delete(above.Seq);
   Check(store.History(normal.Key).Select(p=>p.Seq).SequenceEqual(new[]{1})&&store.History(hard.Key).Count==2,"deleting the hard play removes it from normal too");
   store.Delete(1);
   Check(store.Best(normal.Key)==null&&!store.Keys().Contains(normal.Key)&&store.Best(hard.Key)!.Score==999999,"with the normal play deleted, normal is unregistered: hard plays that were never linked are not raised to it");
   var first=store.Add(max,900000,t.AddMinutes(9));
   Check(first.Normal==NormalLink.Linked&&store.Best(normal.Key)==first,"with no normal record, the next hard or max play is shown on normal");
  }
  // 4. V아카이브: 노말 판정마다 한 줄. 일괄 삭제는 지금 프로필의 그 출처만
  {
   string path=At("varchive");var store=new RecordStore(path);var b=Chart("b","노말");var c=Chart("c","노말");
   store.Add(normal,999000,t);
   var scores=new Dictionary<Selection,decimal>{[normal]=99.5m,[b]=98m,[hard]=100m};
   var preview=store.PreviewImport(scores);string before=State(store);
   Check(preview.Count==2&&preview.All(change=>change.Before==null&&change.After is {Seq:0,At:null,Source:PlaySource.VArchive})&&State(store)==before,"the preview lists a new row for each normal chart, also where a higher own play exists; it changes nothing and skips other judges");
   Check(store.Import(scores)==2&&store.Active.Undo[^1] is {Kind:PlayOps.ImportVArchive,Plays.Count:2}&&store.History(normal.Key).Count==2&&store.Best(normal.Key)!.Score==999000&&store.Best(b.Key) is {Score:980000,At:null},"importing adds them as one operation; the best is still the higher own play");
   int steps=store.Active.Undo.Count;
   Check(store.Import(scores)==0&&store.Import(new Dictionary<Selection,decimal>{[b]=97m})==0&&store.Active.Undo.Count==steps&&store.Active.Plays.Count==3,"importing the same or lower values again adds no rows and leaves nothing to undo");
   Check(store.Import(new Dictionary<Selection,decimal>{[b]=98.5m,[c]=90m})==2&&store.History(b.Key).Single() is {Score:985000,Source:PlaySource.VArchive}&&store.Active.Plays.Count==4,"a higher value updates that one row; a new chart gets its row");
   store.Undo();
   Check(store.History(b.Key).Single().Score==980000&&store.Best(c.Key)==null&&store.Active.NextSeq==4,"one undo takes the whole import back");
   // 다른 프로필과 옛 RATE 기록은 일괄 삭제에 걸리지 않는다
   string first=store.Active.Id;store.Create("둘째",true);store.Add(b,990000,t);
   Check(store.DeleteSource(PlaySource.VArchive)==2&&store.Active.Plays.All(p=>p.Source!=PlaySource.VArchive)&&store.Best(b.Key)!.Score==990000&&store.Active.Undo[^1].Kind==PlayOps.DeleteVArchive,"deleting the V-ARCHIVE rows removes exactly those of this profile and keeps own plays");
   store.Select(first);
   Check(store.Active.Plays.Count(p=>p.Source==PlaySource.VArchive)==2&&store.DeleteSource(PlaySource.Collection)==0,"the other profile still has its rows; nothing of a source that is not there is deleted");
   store.Select(store.Data.Profiles[1].Id);store.Undo();
   Check(store.Active.Plays.Count(p=>p.Source==PlaySource.VArchive)==2,"one undo brings all the deleted rows back");
   string legacy=At("varchive-legacy");File.WriteAllText(legacy,"{\"Format\":1,\"ActiveId\":\"p\",\"Profiles\":[{\"Id\":\"p\",\"Name\":\"n\",\"Records\":{\""+normal.Key+"\":{\"Accuracy\":99.54,\"At\":\"2026-10-01T12:00:00Z\",\"LastPlayedAt\":\"2026-10-05T12:00:00Z\"}},\"Undo\":[]}]}");
   var old=new RecordStore(legacy);old.Import(new Dictionary<Selection,decimal>{[normal]=99.54m});
   Check(old.History(normal.Key).Select(p=>p.Source).SequenceEqual(new[]{PlaySource.OldRate,PlaySource.VArchive})&&old.DeleteSource(PlaySource.VArchive)==1&&old.History(normal.Key).Single().Source==PlaySource.OldRate&&old.Recent(normal.Key)==new DateTime(2026,10,5,12,0,0,DateTimeKind.Utc),"the old rate-only row is a separate source: it stays, with the recent time carried from the old file");
   Check(Fails(()=>old.Edit(1,995421)).Contains("수정할 수 없습니다"),"a rate-only old row and a fetched row cannot be edited");
  }
  // 5. 삭제와 되돌리기: 작업의 차례로, 한 작업이 바꾼 것 전부를
  {
   string path=At("undo");var store=new RecordStore(path);var states=new List<string>{State(store)};
   void Did(){states.Add(State(store));}
   store.Add(normal,990000,t);Did();
   store.Add(hard,995000,t.AddMinutes(1));Did();                                   // 노말에도 보인다
   store.Add(normal,980000,t.AddDays(-3),PlaySource.Manual);Did();                 // 과거 시각으로 더한 판
   store.Edit(2,996000);Did();
   store.Replace(normal,991000);Did();
   store.CancelNormal(2);Did();
   store.Import(new Dictionary<Selection,decimal>{[normal]=99.9m,[Chart("b","노말")]=95m});Did();
   store.DeleteSource(PlaySource.VArchive);Did();
   store.Delete(2);Did();
   store.Delete(1);Did();store.Delete(3);Did();   // 남은 두 판(노말의 제 판들)을 한 판씩 지운다
   Check(store.Active.Undo.Select(step=>step.Kind).SequenceEqual(new[]{PlayOps.Add,PlayOps.Add,PlayOps.Add,PlayOps.Edit,PlayOps.Replace,PlayOps.Unlink,PlayOps.ImportVArchive,PlayOps.DeleteVArchive,PlayOps.Delete,PlayOps.Delete,PlayOps.Delete})&&store.Active.Plays.Count==0,"each confirmed change is one step, in the order it was done");
   bool exact=true;
   for(int i=states.Count-2;i>=0;i--){store.Undo();exact&=State(store)==states[i];if(i==5)exact&=State(new RecordStore(path))==states[i];}
   Check(exact&&store.Active.Undo.Count==0&&Fails(()=>store.Undo()).Contains("되돌릴 저장이 없습니다"),"undoing step by step gives exactly the state before each one (rows, link, recent time, next number), also after reopening");
   // 마지막 판을 지우면 그 판정은 미등록이 되고, 형식 1 에서 넘겨받은 최근 플레이 시각도 함께 없어진다. 되돌리면 함께 돌아온다
   string legacy=At("undo-legacy");File.WriteAllText(legacy,"{\"Format\":1,\"ActiveId\":\"p\",\"Profiles\":[{\"Id\":\"p\",\"Name\":\"n\",\"Records\":{\""+normal.Key+"\":{\"Accuracy\":99.54,\"At\":\"2026-10-01T12:00:00Z\",\"LastPlayedAt\":\"2026-10-05T12:00:00Z\",\"RawScore\":995421}},\"Undo\":[]}]}");
   var old=new RecordStore(legacy);string start=State(old);
   old.Edit(1,995500);
   Check(old.History(normal.Key).Single() is {Score:995500,Source:PlaySource.Old}&&old.History(normal.Key).Single().At==new DateTime(2026,10,1,12,0,0,DateTimeKind.Utc),"an old record with a detailed score can be edited; its saved time stays");
   old.Delete(1);
   Check(old.Best(normal.Key)==null&&!old.Keys().Any()&&old.Active.Recent.Count==0&&old.Active.Undo[^1].Recent is {Count:1},"deleting the last row of a chart unregisters it and drops the carried recent time with it");
   old.Undo();old.Undo();
   Check(State(old)==start&&old.Recent(normal.Key)==new DateTime(2026,10,5,12,0,0,DateTimeKind.Utc),"undo brings the row and the carried recent time back");
   // 되돌릴 수 있는 것은 프로필마다 마지막 스무 작업이다
   var many=new RecordStore(At("undo-limit"));for(int i=0;i<25;i++)many.Add(normal,900000+i,t.AddMinutes(i));
   Check(many.Active.Undo.Count==20&&many.Active.Undo[0].Plays[0].After!.Seq==6&&many.Active.Plays.Count==25,"only the last twenty operations are kept for undo; the rows themselves all stay");
   // 저장에 실패하면 메모리의 기록도 그 전으로 돌아간다(일부만 바뀐 채로 남지 않는다)
   string blocked=Path.Combine(root,"blocked");var failing=new RecordStore(blocked);string empty=State(failing);Directory.CreateDirectory(blocked);
   bool threw=false;try{failing.Add(hard,999000,t);}catch(IOException){threw=true;}catch(UnauthorizedAccessException){threw=true;}
   Check(threw&&State(failing)==empty&&failing.Active.Undo.Count==0&&failing.Best(normal.Key)==null,"a failed save leaves memory as it was: no row, no link, no undo step");
  }
  // 6. 프로필: 복사는 판과 넘겨받은 최근 플레이 시각을 모두 가져가고 되돌리기 내역은 가져가지 않는다. 그 뒤로는 서로 따로다
  {
   var store=new RecordStore(At("profiles"));store.Add(normal,990000,t);store.Add(hard,995000,t.AddMinutes(1));store.CancelNormal(2);string first=store.Active.Id,state=State(store);
   store.Create("복사",true);
   Check(State(store)==state&&store.Active.Undo.Count==0&&store.Active.Id!=first,"a copied profile has the same rows, numbers, links and cuts, and an empty undo history");
   store.Add(normal,999999,t.AddMinutes(5));store.Select(first);
   Check(State(store)==state&&store.Active.Undo.Count==3,"what is done in the copy does not touch the original");
   store.Create("새 프로필",false);
   Check(store.Active.Plays.Count==0&&store.Active.NextSeq==1&&!store.Keys().Any(),"a new profile starts empty");
  }
  // 검사 전용 옛 이름(OldNamesChecks.cs. 제품 코드는 쓰지 않는다)이 판 단위의 동작에 이어져 있는가: 다른 검사들이 이 이름으로 최고 기록을 읽는다
  {
   var store=new RecordStore(At("old-names"));
   Check(store.Put(hard,99.54m,measuredAt:t,rawScore:995421)=="최초 등록"&&store.Put(hard,99.54m,measuredAt:t.AddMinutes(1),rawScore:995421)=="기존 기록 유지"&&store.Put(hard,99.4m,measuredAt:t.AddMinutes(2))=="기존 기록 유지"&&store.Put(hard,99.6m,measuredAt:t.AddMinutes(3))=="기록 갱신","the result words are as before: first, kept (equal or lower), renewed");
   Check(store.Active.Plays.Count==4&&store.Get(hard) is {Accuracy:99.6m,PlayCount:4,RawScore:996000}&&store.Get(hard)!.LastPlayedAt==t.AddMinutes(3)&&store.Get(normal) is {PlayCount:2,RawScore:996000},"every confirmed play is kept, and the old view shows the best with the number of rows");
   store.Put(max,99.9m,measuredAt:t.AddMinutes(4),propagateNormal:false);
   Check(store.Active.Plays[^1] is {Practice:true,Normal:null}&&store.Get(normal)!.RawScore==996000,"a ladder practice play is saved as practice and not shown on normal");
   store.SetManual(normal,99.7m,0,t.AddMinutes(5),997000);
   Check(store.Active.Plays[^1] is {Source:PlaySource.Manual,Score:997000}&&store.Get(normal) is {PlayCount:3,RawScore:997000},"the old manual save adds one manual play");
   // 판정에 보이는 판을 한 판씩 없앤다(히스토리에서 한 줄씩 지우는 것과 같다): 제 판은 지워지고 반영되어 온 하드의 두 판은 반영만 끊긴다. 작업은 판마다 하나이고, 그만큼 되돌리면 모두 돌아온다
   int steps=store.Active.Undo.Count;store.Wipe(normal);
   Check(store.Get(normal)==null&&store.Get(hard) is {PlayCount:4}&&store.Active.Plays.Count(p=>p.Normal==NormalLink.Cancelled)==2&&store.Active.Undo.Count==steps+3&&store.Active.Undo.TakeLast(3).All(step=>step.Plays.Count==1)
    &&store.Active.Undo.TakeLast(3).Select(step=>step.Kind).OrderBy(kind=>kind).SequenceEqual(new[]{PlayOps.Delete,PlayOps.Unlink,PlayOps.Unlink}),"clearing a chart play by play removes its own row and cuts the hard plays shown there, one step each");
   store.Undo();store.Undo();store.Undo();
   Check(store.Get(normal) is {PlayCount:3,RawScore:997000}&&store.Active.Undo.Count==steps,"and as many undos bring it all back");
   Check(store.Records().Count()==3&&store.Records().All(pair=>pair.Value.RawScore==store.Best(pair.Key)!.Score),"the old view gets one best record for each chart that has rows");
  }
  // 7. 등록을 확정하는 부름(Register)과 화면이 읽는 값: 결과의 종류와 글, 그 직전의 최고 기록, 목표 RATE, 출처별 건수
  {
   var store=new RecordStore(At("register"));
   var first=store.Register(hard,995421,t);var same=store.Register(hard,995421,t.AddMinutes(1));var lower=store.Register(hard,994000,t.AddMinutes(2));var higher=store.Register(hard,996000,t.AddMinutes(3));
   Check(first is {Kind:"first",Text:"최초 등록",Previous:null}&&same is {Kind:"kept",Text:"기존 기록 유지"}&&same.Previous==first.Play&&lower is {Kind:"kept",Text:"기존 기록 유지"}&&lower.Previous==same.Play&&higher is {Kind:"best",Text:"기록 갱신"}&&higher.Previous==same.Play,
    "registering returns the kind and words (first, kept for equal or lower, renewed) with the best just before it");
   Check(store.Active.Plays.Count==4&&store.Active.Plays.All(play=>play.Source==PlaySource.Play)&&higher.Play==store.Active.Plays[^1]&&store.BestRate(hard)==99.6m&&store.BestRate(normal)==99.6m&&store.BestRate(max)==null,
    "every registered play is one row of the play source; the goal rate is the best of that judge (none when unregistered)");
   var drill=store.Register(max,999000,t.AddMinutes(4),practice:true);
   Check(drill.Play is {Practice:true,Normal:null}&&drill.Kind=="first"&&store.BestRate(normal)==99.6m,"a practice registration is saved as practice and is not shown on normal");
   Check(store.Count(PlaySource.VArchive)==0&&store.Import(new Dictionary<Selection,decimal>{[normal]=99m,[Chart("b","노말")]=95m})==2&&store.Count(PlaySource.VArchive)==2&&store.Count(PlaySource.Play)==5,"rows are counted by source for the current profile");
   // 등록 확정(Session.Confirm)은 읽은 값을 확인하고 Register 로 저장한다: SCORE 가 RATE 와 맞지 않으면 저장하지 않는다
   var session=new Session();session.Observe(normal,0);session.Lock();session.Begin(store.Active.Id,t.AddMinutes(9),measuredAccuracy:99.5m,rawScore:996000);
   int rows=store.Active.Plays.Count;
   Check(Fails(()=>session.Confirm(store))=="점수와 RATE가 일치하지 않습니다."&&store.Active.Plays.Count==rows&&session.Pending!=null,"a confirmation whose score does not match its rate is refused and stays pending");
   session.Cancel();session.Begin(store.Active.Id,t.AddMinutes(9),measuredAccuracy:99.7m,rawScore:997050);var done=session.Confirm(store);
   Check(done is {Kind:"best",Text:"기록 갱신"}&&done.Play is {Score:997050,Source:PlaySource.Play}&&done.Play.At==t.AddMinutes(9)&&done.Previous!.Score==996000&&session.Pending==null,"confirming a pending registration saves that play with its measured time and reports the previous best");
  }
  // 8. 직전 저장 되돌리기의 확인(UndoView, UndoDialog.Ask): 작업마다 무엇을 묻고 무엇을 보이는가. 취소, 승인, 확인하는 동안 다른 판이 저장되거나 프로필이 바뀐 경우, 되돌릴 저장이 없는 경우
  {
   var store=new RecordStore(At("undo-ask"));var b=Chart("b","노말");var x=Chart("x","노말");var y=Chart("y","노말");
   UndoView Last()=>UndoView.Of(store.Active.Undo[^1],store.Active.Name);
   var own=store.Add(normal,998771,t);var view=Last();
   Check(view is {Question:"저장한 플레이를 삭제할까요?",TargetName:"삭제할 기록",Profile:"기본",Entered:null,Work:null}&&view.Target==own&&view.Notes.Count==0&&view.Counts.Count==0,"undoing an added play shows that play as the one to delete");
   var linked=store.Add(hard,999000,t.AddMinutes(1));view=Last();
   Check(view.Target==linked&&view.Notes.SequenceEqual(new[]{"같은 플레이의 노말판정 기록(하드판정에서 반영)도 함께 삭제됩니다."}),"a linked hard play says the normal record goes with it");
   store.Edit(own.Seq,998711);view=Last();
   Check(view is {Question:"수정 전 점수로 되돌릴까요?",TargetName:"복원할 점수"}&&view.Entered is {Score:998711}&&view.Target==own&&view.Notes.Count==0,"undoing an edit shows the score that is there now and the one that comes back");
   store.Replace(hard,999500);view=Last();
   Check(view is {Question:"교체 전 점수로 되돌릴까요?"}&&view.Entered is {Score:999500}&&view.Target==linked&&view.Entered.At==linked.At&&view.Notes.SequenceEqual(new[]{"같은 플레이의 노말판정 기록도 함께 되돌아갑니다."}),"undoing a replace asks its own question, keeps the play's time, and says the normal record follows");
   store.CancelNormal(linked.Seq);view=Last();
   Check(view is {Question:"이 플레이의 노말판정 반영을 복원할까요?",TargetName:"노말판정에 다시 반영할 기록",Entered:null}&&view.Target is {Score:999500,Normal:NormalLink.Linked}&&view.Notes.SequenceEqual(new[]{"원래 하드판정 기록은 유지됩니다."}),"undoing a cut link shows the play that returns to normal and says the original stays");
   store.Delete(own.Seq);view=Last();
   Check(view is {Question:"삭제한 플레이를 복원할까요?",TargetName:"복원할 기록"}&&view.Target is {Score:998711}&&view.Target.At==t&&view.Notes.Count==0,"undoing a delete shows the play that comes back with its own time");
   store.Import(new Dictionary<Selection,decimal>{[normal]=99m,[b]=95m});store.Import(new Dictionary<Selection,decimal>{[normal]=99.5m,[Chart("c","노말")]=90m});view=Last();
   Check(view is {Question:"이번 가져오기를 되돌릴까요?",Work:"V아카이브 가져오기",Target:null}&&view.Counts.SequenceEqual(new[]{("삭제할 새 기록",1),("이전 값으로 복원할 기록",1)}),"undoing an import shows its name and the count of each thing that is undone");
   store.Undo();view=Last();
   Check(view.Counts.SequenceEqual(new[]{("삭제할 새 기록",2)}),"an action with nothing to undo is left out of the counts");
   store.DeleteSource(PlaySource.VArchive);view=Last();
   Check(view is {Question:"일괄 삭제한 기록을 복원할까요?",Work:"V아카이브 일괄 삭제"}&&view.Counts.SequenceEqual(new[]{("복원할 V아카이브 기록",2)}),"undoing a bulk delete shows how many rows come back");
   // RATE 만 남은 기록 한 줄을 지운 작업: 대상은 그 줄이고, 화면은 남아 있는 RATE 만 적는다(ScoreText.Kept)
   store.Undo();store.Delete(store.History(b.Key).Single().Seq);view=Last();
   Check(view.Target is {Source:PlaySource.VArchive,At:null}&&ScoreText.Kept(view.Target)=="95.00%","a rate-only row is shown with the rate that is really kept");
   store.Undo();
   var none=new Catalog("test",DateTime.UnixEpoch,new());   // 창 대신 답하는 자리(Hook)를 쓰면 곡 목록은 쓰이지 않는다
   try
   {
    // 취소: 기록과 되돌리기 대상이 그대로다
    string before=State(store);int steps=store.Active.Undo.Count;var lastStep=store.Active.Undo[^1];UndoView? asked=null;
    UndoDialog.Hook=shown=>{asked=shown;return false;};
    Check(!UndoDialog.Ask(null!,none,store)&&asked is {Work:"V아카이브 가져오기"}&&State(store)==before&&store.Active.Undo.Count==steps&&ReferenceEquals(store.Active.Undo[^1],lastStep),"cancel leaves the records and the step to undo as they were");
    // 승인: 보여 준 작업 한 건을 한 번만 되돌린다
    UndoDialog.Hook=_=>true;
    Check(UndoDialog.Ask(null!,none,store)&&store.Active.Undo.Count==steps-1&&store.Count(PlaySource.VArchive)==0&&store.Active.Undo[^1].Kind==PlayOps.Delete,"approving undoes exactly the shown step, once");
    // A곡 삭제를 되돌리는 확인 창이 열린 동안 B곡을 새로 저장한 뒤 승인: 둘 다 그대로이고 알린다
    var songA=store.Add(x,990000,t.AddHours(1));store.Delete(songA.Seq);
    UndoDialog.Hook=_=>{store.Add(y,980000,t.AddHours(2));return true;};
    string said=Fails(()=>UndoDialog.Ask(null!,none,store));
    Check(said=="확인하는 동안 직전 저장이 바뀌었습니다. ‘직전 저장 되돌리기’를 다시 눌러 확인해 주세요."&&store.Best(x.Key)==null&&store.Best(y.Key) is {Score:980000}&&store.Active.Undo[^1].Kind==PlayOps.Add&&store.Active.Undo[^2].Kind==PlayOps.Delete,
     "when another play is saved while the question is open, approving undoes nothing: the delete and the new play both stay, and it says so");
    // 그사이 프로필이 바뀐 경우도 되돌리지 않는다
    string home=store.Active.Id;int kept=store.Active.Undo.Count;
    UndoDialog.Hook=_=>{store.Create("다른 프로필",false);return true;};
    Check(Fails(()=>UndoDialog.Ask(null!,none,store))=="확인하는 동안 직전 저장이 바뀌었습니다. ‘직전 저장 되돌리기’를 다시 눌러 확인해 주세요."&&store.Active.Id!=home&&store.Data.Profiles.Single(profile=>profile.Id==home).Undo.Count==kept,"the same when the profile changes meanwhile");
    bool called=false;UndoDialog.Hook=_=>{called=true;return true;};
    Check(Fails(()=>UndoDialog.Ask(null!,none,store))=="이 프로필에는 되돌릴 저장이 없습니다."&&!called,"with nothing to undo it says so without asking");
   }
   finally{UndoDialog.Hook=null;}
  }
  // 9. 예전 개발 빌드가 남긴 판정 단위 삭제(delete-all)가 되돌리기 내역에 있는 파일: 읽히고, 일어날 일이 건수로 보이고, 한 번에 되돌려진다(지금은 그 작업을 만들지 않는다)
  {
   string path=At("delete-all");var store=new RecordStore(path);
   var own=store.Add(normal,990000,t);var linked=store.Add(hard,995000,t.AddMinutes(1));string full=State(store);
   store.Delete(own.Seq);store.CancelNormal(linked.Seq);string cleared=State(store);
   // 두 작업을 옛 모양의 한 작업으로 합쳐 파일에 적는다
   var data=JsonSerializer.Deserialize<PlayData>(File.ReadAllText(path))!;var profile=data.Profiles[0];var one=profile.Undo[^2];var two=profile.Undo[^1];
   profile.Undo.RemoveRange(profile.Undo.Count-2,2);profile.Undo.Add(new Operation(PlayOps.DeleteAll,two.At,one.NextSeq,one.Plays.Concat(two.Plays).ToList(),one.Recent));
   File.WriteAllText(path,JsonSerializer.Serialize(data));
   var old=new RecordStore(path);var view=UndoView.Of(old.Active.Undo[^1],old.Active.Name);
   Check(State(old)==cleared&&old.Active.Undo[^1].Kind=="delete-all"&&view is {Question:"직전 작업을 되돌릴까요?",Work:"기록 삭제",Target:null}&&view.Counts.SequenceEqual(new[]{("복원할 기록",1),("이전 값으로 복원할 기록",1)}),
    "a file with the old judge-level delete in its undo history opens, and the question counts what will happen");
   old.Undo();
   Check(State(old)==full&&old.Active.Undo.Count==2,"and it is undone as one step");
  }
  File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"play-ops-validation.txt"),string.Join("\n",notes));
 }
}
