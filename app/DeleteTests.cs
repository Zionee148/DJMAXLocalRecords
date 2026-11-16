namespace LadderRecords;
static class DeleteTests
{
 public static void Run(Catalog catalog){
 var notes=new List<string>();void Check(bool value,string name){if(!value)throw new Exception(name);notes.Add("PASS: "+name);}
 var dir=Path.Combine(AppContext.BaseDirectory,"test-data",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);var path=Path.Combine(dir,"delete.json");var store=new RecordStore(path);var song=catalog.Songs[0].Id;var c=new Selection(song,4,"SC","하드",1);var others=new[]{c with{Button=5},c with{Pattern="HARD"},c with{Judge="맥스"}};store.Put(c,99m);store.Put(c,98m);foreach(var other in others)store.Put(other,90m);var original=store.Get(c)!;store.DeleteRecord(c);Check(store.Get(c)==null&&others.All(x=>store.Get(x)!=null),"Delete exact combination only");Check(new RecordStore(path).Get(c)==null,"Deletion persists after restart");var reopened=new RecordStore(path);reopened.Undo();Check(reopened.Get(c)==original,"Undo after restart restores score count and recent time");
 var before=System.Text.Json.JsonSerializer.Serialize(reopened.Data);try{reopened.DeleteRecord(c with{Button=8});throw new Exception("Missing record deleted");}catch(Exception e)when(e.Message.Contains("없습니다")){}Check(System.Text.Json.JsonSerializer.Serialize(reopened.Data)==before,"Missing deletion does not modify data or undo");
 // 위에서 다른 객체(reopened)가 같은 파일에 저장했다. 옛 객체로 이어서 저장하면 밖에서 바뀐 파일로 보고 막으므로, 다시 읽어 같은 상태(c 가 지워진 상태)로 만든 뒤 잇는다
 store=new RecordStore(path);store.DeleteRecord(c);
 var first=store.Active.Id;store.Create("복사",true);store.Put(c,95m);store.DeleteRecord(c);store.Select(first);Check(store.Get(c)==null&&others.All(x=>store.Get(x)!=null),"Delete stays in selected profile");
 string blocked=Path.Combine(dir,"blocked");var failure=new RecordStore(blocked);failure.Active.Records[c.Key]=original;Directory.CreateDirectory(blocked);try{failure.DeleteRecord(c);throw new Exception("Write failure ignored");}catch(IOException){}catch(UnauthorizedAccessException){}Check(failure.Get(c)==original&&failure.Active.Undo.Count==0,"Failed deletion rolls back score and undo history");
 using var small=new Font("맑은 고딕",35,FontStyle.Bold,GraphicsUnit.Pixel);string longTitle=OverlayText.KartTitle;string shortTitle=OverlayText.DisplayTitle(longTitle,small,420);Check(shortTitle.EndsWith("...")&&longTitle.StartsWith(shortTitle[..^3])&&OverlayText.Measure(shortTitle,small)<=420,"Long titles keep prefix and literal trailing dots");Check(OverlayText.DisplayTitle(longTitle,small)==longTitle&&OverlayText.DisplayTitle(OverlayText.ReferenceTitle,small)==OverlayText.ReferenceTitle,"No special song aliases remain");
 File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"delete-validation.txt"),string.Join("\n",notes));
 }
}
