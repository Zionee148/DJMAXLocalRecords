namespace LadderRecords;
static class DeleteTests
{
 public static void Run(Catalog catalog){
 var notes=new List<string>();void Check(bool value,string name){if(!value)throw new Exception(name);notes.Add("PASS: "+name);}
 var dir=Path.Combine(AppContext.BaseDirectory,"test-data",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);var path=Path.Combine(dir,"delete.json");var store=new RecordStore(path);var song=catalog.Songs[0].Id;var c=new Selection(song,4,"SC","하드",1);var others=new[]{c with{Button=5},c with{Pattern="HARD"},c with{Judge="맥스"}};store.Put(c,99m);store.Put(c,98m);foreach(var other in others)store.Put(other,90m);var original=store.Get(c)!;store.Wipe(c);Check(store.Get(c)==null&&others.All(x=>store.Get(x)!=null),"Delete exact combination only");Check(new RecordStore(path).Get(c)==null,"Deletion persists after restart");var reopened=new RecordStore(path);reopened.Undo();reopened.Undo();Check(reopened.Get(c)==original,"Undo after restart restores score count and recent time");   // 두 판을 한 판씩 지웠으니 두 번 되돌린다
 var before=System.Text.Json.JsonSerializer.Serialize(reopened.Data);try{reopened.Delete(int.MaxValue);throw new Exception("Missing record deleted");}catch(Exception e)when(e.Message.Contains("없습니다")){}Check(System.Text.Json.JsonSerializer.Serialize(reopened.Data)==before,"Missing deletion does not modify data or undo");
 // 위에서 다른 객체(reopened)가 같은 파일에 저장했다. 옛 객체로 이어서 저장하면 밖에서 바뀐 파일로 보고 막으므로, 다시 읽어 같은 상태(c 가 지워진 상태)로 만든 뒤 잇는다
 store=new RecordStore(path);store.Wipe(c);
 var first=store.Active.Id;store.Create("복사",true);store.Put(c,95m);store.Wipe(c);store.Select(first);Check(store.Get(c)==null&&others.All(x=>store.Get(x)!=null),"Delete stays in selected profile");
 // 쓰기가 실패한 삭제(기록 파일이 잠겨 있다). 노말에 보이는 판이 둘이다: 노말의 제 판 하나, 노말에 반영된 하드 한 판(반영되지 않은 하드 한 판이 더 있다).
 // 제 판을 지우는 것과 반영을 끊는 것은 판마다 따로 한 작업이다. 각각을 잠긴 파일에 해 본다.
 // 실패한 뒤에는 기록 전체(점수, 시각, 노말 반영)와 되돌리기 내역, 변경 번호, 파일의 원문이 작업 전과 같아야 한다. 잠금이 풀리면 같은 삭제가 된다
 string lockedPath=Path.Combine(dir,"locked.json");var failure=new RecordStore(lockedPath);var own=c with{Judge="노말"};DateTime at=new(2026,11,23,1,0,0,DateTimeKind.Utc);
 failure.Put(own,97m,measuredAt:at);failure.Put(c,99m,measuredAt:at.AddMinutes(1));failure.Put(c,98m,measuredAt:at.AddMinutes(2));
 Check(failure.Active.Plays.Count==3&&failure.Active.Plays.Count(p=>p.Normal==NormalLink.Linked)==1&&failure.Get(own) is {PlayCount:2},"Setup for the failed deletion: an own normal play and a hard play shown on normal");
 string stateBefore=System.Text.Json.JsonSerializer.Serialize(failure.Data);long revisionBefore=failure.Revision;byte[] diskBefore=File.ReadAllBytes(lockedPath);var shownBefore=failure.Get(own);
 int ownSeq=failure.History(own.Key).Single(p=>p.Key==own.Key).Seq,linkedSeq=failure.History(own.Key).Single(p=>p.Key!=own.Key).Seq;
 using(new FileStream(lockedPath,FileMode.Open,FileAccess.Read,FileShare.None))
 {
  try{failure.Delete(ownSeq);throw new Exception("Write failure ignored");}catch(IOException){}catch(UnauthorizedAccessException){}
  Check(System.Text.Json.JsonSerializer.Serialize(failure.Data)==stateBefore&&failure.Revision==revisionBefore&&failure.Get(own)==shownBefore,"Failed deletion of an own play leaves memory as it was");
  try{failure.CancelNormal(linkedSeq);throw new Exception("Write failure ignored");}catch(IOException){}catch(UnauthorizedAccessException){}
 }
 Check(System.Text.Json.JsonSerializer.Serialize(failure.Data)==stateBefore&&failure.Revision==revisionBefore&&failure.Get(own)==shownBefore&&File.ReadAllBytes(lockedPath).SequenceEqual(diskBefore),"Failed deletion leaves every play, link and time, the undo history and the file as they were");
 failure.Delete(ownSeq);failure.CancelNormal(linkedSeq);Check(failure.Get(own)==null&&failure.Get(c) is {PlayCount:2}&&failure.Active.Plays.Count(p=>p.Normal==NormalLink.Cancelled)==1,"The same deletion goes through once the file is free");
 using var small=new Font("맑은 고딕",35,FontStyle.Bold,GraphicsUnit.Pixel);string longTitle=OverlayText.KartTitle;string shortTitle=OverlayText.DisplayTitle(longTitle,small,420);Check(shortTitle.EndsWith("...")&&longTitle.StartsWith(shortTitle[..^3])&&OverlayText.Measure(shortTitle,small)<=420,"Long titles keep prefix and literal trailing dots");Check(OverlayText.DisplayTitle(longTitle,small)==longTitle&&OverlayText.DisplayTitle(OverlayText.ReferenceTitle,small)==OverlayText.ReferenceTitle,"No special song aliases remain");
 File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"delete-validation.txt"),string.Join("\n",notes));
 }
}
