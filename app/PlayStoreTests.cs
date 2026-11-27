using System.Text;
using System.Text.Json;
namespace LadderRecords;
// 기록 파일의 형식 2 와 그 저장(RecordStore)을 검사한다: 형식 1 을 올리는 규칙, 읽기·쓰기, 새 형식으로 처음 저장하기 직전의 원본 사본.
// 합성 기록만 쓰고, 실행 파일 옆의 test-data 아래에만 쓴다. 결과는 play-store-validation.txt
static class PlayStoreTests
{
 public static void Run()
 {
  var notes=new List<string>();void Check(bool value,string name){if(!value)throw new Exception(name);notes.Add("PASS: "+name);}
  string root=Path.Combine(AppContext.BaseDirectory,"test-data","play-store-"+Guid.NewGuid().ToString("N"));
  string At(string name){string dir=Path.Combine(root,name);Directory.CreateDirectory(dir);return Path.Combine(dir,"records.json");}
  string Beside(string path,string name)=>Path.Combine(Path.GetDirectoryName(path)!,name);
  string Json(object value)=>JsonSerializer.Serialize(value);
  string Refused(Action act){try{act();}catch(InvalidDataException e){return e.Message;}return "";}
  string Put(string name,string content){string path=At(name);File.WriteAllText(path,content);return path;}
  var t0=new DateTime(2026,10,1,12,0,0,DateTimeKind.Utc);DateTime t1=t0.AddDays(1),t2=t0.AddDays(2),played=t0.AddDays(30);
  // 형식 1 의 기록: 세부 SCORE 가 있는 것, RATE 만 있는 것, 100점 단위로 끝나는 세부 SCORE, 옛 "하드 → 노말 반영"의 두 기록, 저장 시각이 같은 것, 프로필 둘, 되돌리기 내역
  SavedData Old(bool reversed=false)
  {
   var records=new List<KeyValuePair<string,Score>>{
    new("a|4|SC|노말",new Score(99.54m,t1,3,played,995421)),
    new("b|4|SC|노말",new Score(99.54m,t0,0,null,null)),
    new("c|6|MX|하드",new Score(98.00m,t2,5,played.AddDays(1),980000)),
    new("c|6|MX|노말",new Score(98.00m,t2,1,played.AddDays(1),980000)),
    new("d|8|HD|노말",new Score(100.00m,t0,null,null,null))};
   if(reversed)records.Reverse();
   var first=new Profile{Id="p1",Name="첫째",Records=new(records)};first.Undo.Add(new("a|4|SC|노말",null,records[0].Value));
   return new SavedData{ActiveId="p2",Profiles=new(){first,new Profile{Id="p2",Name="둘째"}}};
  }
  void WriteOld(string path,SavedData? data=null)=>File.WriteAllText(path,JsonSerializer.Serialize(data??Old(),new JsonSerializerOptions{WriteIndented=true}));
  try
  {
   // 형식 1 을 올리는 규칙
   {
    var data=PlayFormat.Upgrade(Old());var plays=data.Profiles[0].Plays;
    Check(data.Format==2&&data.ActiveId=="p2"&&data.Profiles.Select(p=>p.Id+p.Name).SequenceEqual(new[]{"p1첫째","p2둘째"}),"profiles and the chosen one are kept");
    Check(plays.Select(p=>p.Key).SequenceEqual(new[]{"b|4|SC|노말","d|8|HD|노말","a|4|SC|노말","c|6|MX|노말","c|6|MX|하드"})&&plays.Select(p=>p.Seq).SequenceEqual(new[]{1,2,3,4,5})&&data.Profiles[0].NextSeq==6,"one row for each record, numbered by saved time and then by key");
    Check(plays[2]==new Play(3,"a|4|SC|노말",995421,t1,PlaySource.Old),"a record with a detailed score: that score, its saved time, 'old'");
    Check(plays[0]==new Play(1,"b|4|SC|노말",995400,null,PlaySource.OldRate)&&plays[1]==new Play(2,"d|8|HD|노말",1000000,null,PlaySource.OldRate),"a record with only a rate: the converted score, no time, 'oldrate'");
    Check(plays[4]==new Play(5,"c|6|MX|하드",980000,t2,PlaySource.Old),"a detailed score that ends in 00 is still a detailed score");
    Check(plays[3]==new Play(4,"c|6|MX|노말",980000,t2,PlaySource.Old)&&plays.All(p=>p.Normal==null&&!p.Practice),"the old hard-to-normal pair stays two separate rows, not linked");
    Check(data.Profiles[0].Recent.Count==3&&data.Profiles[0].Recent["a|4|SC|노말"]==played&&data.Profiles[0].Recent["c|6|MX|하드"]==played.AddDays(1)&&!data.Profiles[0].Recent.ContainsKey("b|4|SC|노말"),"the last-played times are carried apart from the rows");
    Check(data.Profiles.All(p=>p.Undo.Count==0)&&data.Profiles[1].Plays.Count==0&&data.Profiles[1].NextSeq==1,"the old undo history is not carried; an empty profile stays empty");
    Check(Json(PlayFormat.Upgrade(Old()))==Json(data)&&Json(PlayFormat.Upgrade(Old(reversed:true)))==Json(data),"the same records always give the same result, whatever their order in the file");
    Check(PlayFormat.Problem(data)==null,"the result is a valid format 2");
   }
   // 쓸 수 없는 모양을 가려낸다(고쳐 읽지 않는다)
   {
    PlayData Good()=>PlayFormat.Upgrade(Old());
    PlayData With(Action<PlayData> change){var data=Good();change(data);return data;}
    Check(PlayFormat.Problem(With(d=>d.ActiveId="없는 프로필"))!=null&&PlayFormat.Problem(With(d=>d.Profiles.Clear()))!=null&&PlayFormat.Problem(With(d=>d.Profiles[1].Id="p1"))!=null,"no profiles, an unknown chosen profile, the same id twice");
    Check(PlayFormat.Problem(With(d=>d.Profiles[0].Plays[0]=d.Profiles[0].Plays[0] with{Score=1000001}))!=null&&PlayFormat.Problem(With(d=>d.Profiles[0].Plays[0]=d.Profiles[0].Plays[0] with{Score=-1}))!=null,"a score out of range");
    Check(PlayFormat.Problem(With(d=>d.Profiles[0].Plays[1]=d.Profiles[0].Plays[1] with{Seq=1}))!=null&&PlayFormat.Problem(With(d=>d.Profiles[0].NextSeq=5))!=null&&PlayFormat.Problem(With(d=>d.Profiles[0].Plays[0]=d.Profiles[0].Plays[0] with{Seq=0}))!=null,"a number used twice, a number not below the next one, a number below 1");
    Check(PlayFormat.Problem(With(d=>d.Profiles[0].Plays[0]=d.Profiles[0].Plays[0] with{Source="모름"}))!=null&&PlayFormat.Problem(With(d=>d.Profiles[0].Plays[0]=d.Profiles[0].Plays[0] with{Normal="모름"}))!=null&&PlayFormat.Problem(With(d=>d.Profiles[0].Plays[0]=d.Profiles[0].Plays[0] with{Key=""}))!=null,"an unknown source, an unknown normal link, an empty key");
    var bad=Old();bad.Profiles[0].Records["e|4|NM|노말"]=new Score(150m,t0);
    Check(PlayFormat.Problem(PlayFormat.Upgrade(bad))!=null,"an old rate out of range is not converted into something else");
   }
   // 형식 1 의 파일을 연다: 메모리에서만 올리고 파일은 건드리지 않는다
   {
    string path=At("upgrade");WriteOld(path);byte[] before=File.ReadAllBytes(path);
    var store=new RecordStore(path);
    Check(store.Upgraded&&store.Active.Id=="p2"&&Json(store.Data)==Json(PlayFormat.Upgrade(Old())),"an old file opens as format 2 in memory");
    Check(File.ReadAllBytes(path).SequenceEqual(before)&&Directory.GetFiles(Path.GetDirectoryName(path)!).Length==1,"opening it writes nothing: the file is still the old one and nothing is beside it");
    // 첫 저장: 그 직전에 원본 사본을 남기고, 새 형식으로 쓴다. 직전 저장 백업은 옛 파일 그대로다
    store.Active.Name="둘째(고침)";store.Save();
    Check(File.ReadAllBytes(Beside(path,RecordStore.OriginalName)).SequenceEqual(before)&&File.ReadAllBytes(path+".bak").SequenceEqual(before),"the first save keeps the old file as records.format1.json, byte for byte (and as the backup)");
    Check(!store.Upgraded&&!File.Exists(path+".tmp")&&!Directory.GetFiles(Path.GetDirectoryName(path)!,"*.moving").Any(),"nothing half-written is left");
    var again=new RecordStore(path);
    Check(!again.Upgraded&&again.Active.Name=="둘째(고침)"&&Json(again.Data)==Json(store.Data),"the saved file opens as format 2 with the same rows");
    string text=File.ReadAllText(path);
    Check(text.StartsWith("{\"Format\":2,")&&!text.Contains('\n'),"the file is written without line breaks, the format number first");
    Check(!text.Contains("\"At\":null")&&!text.Contains("\"Normal\"")&&!text.Contains("\"Practice\""),"empty fields are left out of the file");
    // 이 형식을 모르는 프로그램(형식 1 만 아는 이전 버전)이 읽지 않고 알리는 것은 실제 옛 버전의 코드로 본다(tools\update_test.py 의 format2-old)
    again.Active.Name="둘째(또 고침)";again.Save();
    Check(Directory.GetFiles(Path.GetDirectoryName(path)!,"records.format1*").Length==1&&File.ReadAllBytes(Beside(path,RecordStore.OriginalName)).SequenceEqual(before),"later saves do not make another copy");
   }
   // 원본 사본이 이미 있음: 같은 내용이면 그대로 두고, 다른 내용이면 덮지 않고 번호를 붙여 하나 더 남긴다
   {
    string same=At("copy-same");WriteOld(same);File.Copy(same,Beside(same,RecordStore.OriginalName));
    new RecordStore(same).Save();
    Check(Directory.GetFiles(Path.GetDirectoryName(same)!,"records.format1*").Length==1,"the same copy already there: it is enough");
    string other=At("copy-other");WriteOld(other);byte[] mine=File.ReadAllBytes(other);File.WriteAllText(Beside(other,RecordStore.OriginalName),"다른 원본의 사본");
    new RecordStore(other).Save();
    Check(File.ReadAllText(Beside(other,RecordStore.OriginalName))=="다른 원본의 사본"&&File.ReadAllBytes(Beside(other,"records.format1-2.json")).SequenceEqual(mine),"a different copy already there: it is not overwritten, a second one is kept as records.format1-2.json");
    // 옛 기록 파일을 다시 가져다 놓고(형식 1) 또 저장한다: 그 원본도 따로 남는다
    var third=Old();third.Profiles[1].Name="다시 가져온 옛 기록";WriteOld(other,third);byte[] theirs=File.ReadAllBytes(other);
    new RecordStore(other).Save();
    Check(File.ReadAllBytes(Beside(other,"records.format1-3.json")).SequenceEqual(theirs)&&File.ReadAllBytes(Beside(other,"records.format1-2.json")).SequenceEqual(mine)&&new RecordStore(other).Active.Name=="다시 가져온 옛 기록","an old file put back and saved again: its original is kept too, as the next number");
   }
   // 원본 사본을 남기지 못함: 저장하지 않는다. 파일은 옛 형식 그대로이고, 다음 저장에서 다시 해 본다
   {
    string path=At("copy-failed");WriteOld(path);byte[] before=File.ReadAllBytes(path);var store=new RecordStore(path);
    string Fail(Exception error){RecordStore.Step=at=>{if(at.StartsWith("original:"))throw error;};try{store.Save();return "";}catch(OriginalCopyException e){return e.Message;}finally{RecordStore.Step=null;}}
    string full=Fail(new IOException("디스크가 가득 참(시험)",unchecked((int)0x80070070)));
    Check(full.StartsWith("이전 기록의 원본 사본을 보관하지 못해 이번 변경을 저장하지 않았습니다. 기존 기록은 그대로입니다. 저장 공간을 확보한 뒤 다시 시도해 주세요.")&&full.EndsWith("보관하려던 파일: "+Beside(path,RecordStore.OriginalName)),"the copy cannot be kept (disk full): the change is not saved, and it says what to do and which file");
    Check(File.ReadAllBytes(path).SequenceEqual(before)&&Directory.GetFiles(Path.GetDirectoryName(path)!).Length==1&&store.Upgraded&&store.Revision==0,"the records file is untouched and nothing is left beside it");
    Check(Fail(new IOException("쓰는 중(시험)",unchecked((int)0x80070020))).Contains("이 파일을 사용 중인 다른 프로그램을 닫은 뒤 다시 시도해 주세요.")&&Fail(new UnauthorizedAccessException("쓸 수 없음(시험)")).Contains("표시된 폴더에 파일을 쓸 수 있는지 확인한 뒤 다시 시도해 주세요."),"a file in use and a folder that cannot be written get their own advice");
    store.Save();
    Check(File.ReadAllBytes(Beside(path,RecordStore.OriginalName)).SequenceEqual(before)&&!store.Upgraded&&!new RecordStore(path).Upgraded,"once it can be kept, the save goes through");
   }
   // 읽지 않는 파일들: 더 새 형식, 깨진 파일, 쓸 수 없는 모양, 본 파일 없이 남은 백업. 어느 것도 건드리지 않는다
   {
    string newer=At("newer");File.WriteAllText(newer,"{\"Format\":3,\"ActiveId\":\"x\",\"Profiles\":[]}");
    Check(Refused(()=>new RecordStore(newer)).Contains("새 버전에서 저장된 형식입니다(기록 형식 3)")&&File.ReadAllText(newer).StartsWith("{\"Format\":3"),"a newer format is refused and left alone");
    foreach(var (name,content) in new[]{("garbage","{ 깨진 파일"),("array","[]"),("empty",""),("no-profiles","{\"Format\":2,\"ActiveId\":\"x\",\"Profiles\":[]}"),("old-no-profiles","{\"ActiveId\":\"x\",\"Profiles\":[]}"),
     ("bad-score","{\"Format\":2,\"ActiveId\":\"p\",\"Profiles\":[{\"Id\":\"p\",\"Name\":\"n\",\"NextSeq\":2,\"Plays\":[{\"Seq\":1,\"Key\":\"a|4|SC|노말\",\"Score\":2000000,\"Source\":\"play\"}],\"Recent\":{},\"Undo\":[]}]}"),
     ("format-text","{\"Format\":\"2\",\"ActiveId\":\"p\",\"Profiles\":[]}")})
    {
     string path=At(name);File.WriteAllText(path,content);string said=Refused(()=>new RecordStore(path));
     Check(said.StartsWith("기록 파일을 읽을 수 없습니다.")&&said.Contains(path)&&File.ReadAllText(path)==content,"unreadable ("+name+"): refused with the file named, and left alone");
    }
    string lone=At("backup-only");File.WriteAllText(lone+".bak","{}");
    Check(Refused(()=>new RecordStore(lone)).Contains("records.json.bak만 남아 있습니다")&&!File.Exists(lone),"only a backup left: not started as empty");
    string inside=Path.Combine(root,"folder",UserFiles.FolderName,"records.json");Directory.CreateDirectory(Path.GetDirectoryName(inside)!);File.WriteAllText(inside,"{ 깨진 파일");
    string where=Refused(()=>new RecordStore(inside));
    Check(where.Contains("프로그램 폴더: "+Path.Combine(root,"folder"))&&where.Contains("UserData 폴더: "+Path.GetDirectoryName(inside)),"in the program's own folder the notice names both places");
   }
   // 형식 번호와 실제 모양이 어긋난 파일: 다른 형식으로 읽어 기록을 비우지 않는다. 읽지 않고, 파일을 건드리지 않는다
   {
    string made=At("shape-source");var source=new RecordStore(made);source.Active.Plays.Add(new(source.Active.NextSeq++,"a|4|SC|노말",995421,t1,PlaySource.Play));source.Save();
    string two=File.ReadAllText(made),one=JsonSerializer.Serialize(Old());   // 한 판이 든 형식 2 와, 형식 1
    void Refuse(string name,string content,string what)
    {
     string path=At(name);File.WriteAllText(path,content);string said=Refused(()=>new RecordStore(path));
     Check(said.StartsWith("기록 파일을 읽을 수 없습니다.")&&File.ReadAllText(path)==content&&Directory.GetFiles(Path.GetDirectoryName(path)!).Length==1,what);
    }
    Check(two.StartsWith("{\"Format\":2,")&&one.StartsWith("{\"Format\":1,"),"(the two sample files are what they should be)");
    Refuse("two-without-number",two.Replace("{\"Format\":2,","{"),"format 2 rows with the number missing: not opened as format 1 with no records");
    Refuse("one-as-two",one.Replace("{\"Format\":1,","{\"Format\":2,"),"format 1 records under the number 2: not opened as format 2 with no rows");
    Refuse("two-as-one",two.Replace("{\"Format\":2,","{\"Format\":1,"),"format 2 rows under the number 1");
    Refuse("zero",one.Replace("{\"Format\":1,","{\"Format\":0,"),"the number 0 is not a format");
    Refuse("two-numbers",two.Replace("{\"Format\":2,","{\"Format\":2,\"Format\":3,"),"two different numbers in one file");
    string twice=At("same-number-twice");File.WriteAllText(twice,two.Replace("{\"Format\":2,","{\"Format\":2,\"Format\":2,"));
    Check(new RecordStore(twice).Active.Plays.Count==1,"the same number written twice is still that format");
    // 있어야 하는 칸이 빠진 파일: 기본값(0점, 서기 1년, 빈 목록)으로 채워 읽지 않는다. 0점이나 빈 목록이 적혀 있는 것은 읽는다
    string play="{\"Seq\":1,\"Key\":\"a|4|SC|노말\",\"Score\":995421,\"At\":\"2026-10-02T12:00:00Z\",\"Source\":\"play\"}";
    string Two(string plays,string next="2",string undo="[]")=>"{\"Format\":2,\"ActiveId\":\"p\",\"Profiles\":[{\"Id\":\"p\",\"Name\":\"n\",\"NextSeq\":"+next+",\"Plays\":["+plays+"],\"Recent\":{},\"Undo\":"+undo+"}]}";
    string One(string records)=>"{\"Format\":1,\"ActiveId\":\"p\",\"Profiles\":[{\"Id\":\"p\",\"Name\":\"n\",\"Records\":"+records+",\"Undo\":[]}]}";
    Check(new RecordStore(Put("whole",Two(play))).Active.Plays.Single().Score==995421,"(the sample row is read when nothing is missing)");
    foreach(var (name,content) in new[]{("no-score",Two(play.Replace("\"Score\":995421,",""))),("no-source",Two(play.Replace(",\"Source\":\"play\"",""))),("no-seq",Two(play.Replace("\"Seq\":1,",""))),("no-key",Two(play.Replace("\"Key\":\"a|4|SC|노말\",",""))),
     ("null-play",Two("null")),("null-profile","{\"Format\":2,\"ActiveId\":\"p\",\"Profiles\":[null]}"),("no-recent","{\"Format\":2,\"ActiveId\":\"p\",\"Profiles\":[{\"Id\":\"p\",\"Name\":\"n\",\"NextSeq\":1,\"Plays\":[],\"Undo\":[]}]}"),
     ("next-zero",Two("","0")),("null-undo",Two(play,"2","[null]")),("undo-no-rows",Two(play,"2","[{\"Kind\":\"add\",\"At\":\"2026-10-02T12:00:00Z\",\"NextSeq\":1}]")),
     ("old-no-time",One("{\"a|4|SC|노말\":{\"Accuracy\":99.54,\"RawScore\":995421}}")),("old-no-rate",One("{\"a|4|SC|노말\":{\"At\":\"2026-10-02T12:00:00Z\",\"RawScore\":995421}}")),("old-null-records",One("null")),("old-null-record",One("{\"a|4|SC|노말\":null}")),
     ("old-null-profile","{\"Format\":1,\"ActiveId\":\"p\",\"Profiles\":[null]}"),("old-no-records","{\"Format\":1,\"ActiveId\":\"p\",\"Profiles\":[{\"Id\":\"p\",\"Name\":\"n\",\"Undo\":[]}]}")})
     Refuse("missing-"+name,content,"a field that must be there is missing ("+name+"): refused, not filled with a default");
    Check(new RecordStore(Put("zero-score",Two(play.Replace("995421","0")))).Active.Plays.Single().Score==0&&new RecordStore(Put("no-plays",Two("","1"))).Active.Plays.Count==0&&new RecordStore(Put("no-old-records",One("{}"))).Active.Plays.Count==0,"a written 0 and a written empty list are read as they are");
   }
   // 값 하나하나는 맞아도 함께 쓸 수 없는 판
   {
    Play hard=new(1,"a|4|SC|하드",995421,t1,PlaySource.Play);
    Check(new[]{hard,hard with{Normal=NormalLink.Linked},hard with{Normal=NormalLink.Cancelled},hard with{Practice=true},new Play(1,"a|4|SC|노말",995400,null,PlaySource.VArchive),new Play(1,"a|4|SC|노말",995421,null,PlaySource.Collection),
     new Play(1,"a|4|SC|맥스",995421,t1,PlaySource.Old),new Play(1,"막대|가|든|곡|9|NEW|맥스",1,t1,PlaySource.Manual)}.All(p=>PlayFormat.Problem(p)==null),"rows whose fields go together are accepted, also with a new button or pattern or a song id with bars in it");
    Check(new[]{new Play(1,"a|4|SC|노말",995400,t1,PlaySource.OldRate),new Play(1,"a|4|SC|노말",995421,null,PlaySource.OldRate),new Play(1,"a|4|SC|노말",995421,null,PlaySource.VArchive),new Play(1,"a|4|SC|노말",995400,t1,PlaySource.VArchive),
     new Play(1,"a|4|SC|노말",995421,null,PlaySource.Play),new Play(1,"a|4|SC|노말",995421,null,PlaySource.Old)}.All(p=>PlayFormat.Problem(p)!=null),"the source and the time or the precision of the score do not go together");
    Check(new[]{new Play(1,"a|4|SC|노말",995421,t1,PlaySource.Play,NormalLink.Linked),new Play(1,"a|4|SC|노말",995421,t1,PlaySource.Play,null,true),hard with{Practice=true,Normal=NormalLink.Linked},hard with{Practice=true,Normal=NormalLink.Cancelled},
     new Play(1,"a|4|SC|하드",995421,t1,PlaySource.Old,NormalLink.Linked),new Play(1,"a|4|SC|하드",995400,null,PlaySource.VArchive),new Play(1,"a|4|SC|맥스",995421,null,PlaySource.Collection)}.All(p=>PlayFormat.Problem(p)!=null),"a normal link or a practice mark where there cannot be one; a fetched best that is not on normal");
    Check(new[]{"한덩어리","a|4|SC|모름","a|넷|SC|노말","|4|SC|노말","a|4||노말","a|0|SC|노말","4|SC|노말"}.All(key=>PlayFormat.Problem(hard with{Key=key})!=null),"a key that cannot be split into song, button, pattern and judge");
    PlayData One(params Play[] plays)=>new(){ActiveId="p",Profiles=new(){new PlayProfile{Id="p",Name="n",NextSeq=plays.Length+1,Plays=plays.ToList()}}};
    Check(PlayFormat.Problem(One(new Play(1,"a|4|SC|노말",995400,null,PlaySource.VArchive),new Play(2,"b|4|SC|노말",995400,null,PlaySource.VArchive),new Play(3,"a|4|SC|노말",995400,null,PlaySource.OldRate),new Play(4,"a|4|SC|노말",995421,null,PlaySource.Collection)))==null,"one row of each fetched kind for a pattern is fine");
    Check(PlayFormat.Problem(One(new Play(1,"a|4|SC|노말",995400,null,PlaySource.VArchive),new Play(2,"a|4|SC|노말",995500,null,PlaySource.VArchive)))!=null&&PlayFormat.Problem(One(new Play(1,"a|4|SC|노말",995421,t1,PlaySource.Old),new Play(2,"a|4|SC|노말",995400,null,PlaySource.OldRate)))!=null,"two rows where there can be only one (the V-ARCHIVE best, the old best)");
    var undone=One(hard);undone.Profiles[0].Undo.Add(new("add",t1,1,new(){new(null,hard with{Key="한덩어리"})}));
    var empty=One(hard);empty.Profiles[0].Undo.Add(new("add",t1,1,new(){new(null,null)}));
    Check(PlayFormat.Problem(undone)!=null&&PlayFormat.Problem(empty)!=null,"the rows kept for undo are held to the same rules");
   }
   // 저장하기 전에도 같은 확인을 한다: 쓸 수 없는 모양은 쓰지 않는다. 본 파일, 직전 백업, 저장 횟수가 그대로다
   {
    string path=At("save-check");WriteOld(path);byte[] before=File.ReadAllBytes(path);var store=new RecordStore(path);
    string problem="";string Fail(){try{store.Save();return "";}catch(InvalidRecordsException e){problem=e.Problem;return e.Message;}}
    store.Data.Profiles[0].NextSeq=3;   // 이미 준 순번과 겹친다
    // 화면에는 정해 둔 글만 보이고, 무엇이 틀렸는지(진단 글)는 Problem 에만 있다
    string? told=null;RecordStore.Rejected=why=>told=why;string refused;try{refused=Fail();}finally{RecordStore.Rejected=null;}
    Check(told!=null&&told==problem,"the reason a save was refused reaches the diagnostics hook");
    Check(refused==InvalidRecordsException.Text&&problem!=""&&!refused.Contains(problem)&&File.ReadAllBytes(path).SequenceEqual(before)&&Directory.GetFiles(Path.GetDirectoryName(path)!).Length==1&&store.Revision==0&&store.Upgraded,"a state that could not be opened again is not written: no file changes, not even the copy of the original");
    store.Data.Profiles[0].NextSeq=6;store.Save();byte[] saved=File.ReadAllBytes(path),backup=File.ReadAllBytes(path+".bak");
    store.Data.Profiles[0].Plays.Add(store.Data.Profiles[0].Plays[0]);   // 같은 순번이 두 번
    Check(Fail()!=""&&File.ReadAllBytes(path).SequenceEqual(saved)&&File.ReadAllBytes(path+".bak").SequenceEqual(backup)&&store.Revision==1&&!File.Exists(path+".tmp"),"the same on a format 2 file: the file, its backup and the save count stay as they were");
    store.Data.Profiles[0].Plays.RemoveAt(store.Data.Profiles[0].Plays.Count-1);store.Save();
    Check(store.Revision==2&&new RecordStore(path).Active.Id=="p2","once the state is right again it saves");
   }
   // 옛 프로그램이 읽던 UTF-16 파일(편집기로 다시 저장한 것)도 읽는다. 원본 사본은 바꾸기 전의 바이트 그대로다
   {
    string wide=At("utf16");File.WriteAllText(wide,JsonSerializer.Serialize(Old()),Encoding.Unicode);byte[] raw=File.ReadAllBytes(wide);
    var store=new RecordStore(wide);
    Check(raw[0]==0xFF&&raw[1]==0xFE&&store.Upgraded&&Json(store.Data)==Json(PlayFormat.Upgrade(Old())),"an old file saved as UTF-16 opens (the format 1 store of v1.1.x read it too)");
    store.Save();
    Check(File.ReadAllBytes(Beside(wide,RecordStore.OriginalName)).SequenceEqual(raw)&&!new RecordStore(wide).Upgraded,"its original is kept in the bytes it had");
   }
   // 그 밖: 처음 시작, 형식 2 의 모든 칸이 그대로 돌아오는가, BOM 이 붙은 옛 파일, 밖에서 바뀐 파일, 저장이 잠긴 동안
   {
    string fresh=At("fresh");var store=new RecordStore(fresh);
    Check(!store.Upgraded&&store.Data.Profiles.Count==1&&store.Active.Plays.Count==0&&!File.Exists(fresh),"no file: one empty profile, nothing written yet");
    var profile=store.Active;profile.Plays.Add(new(profile.NextSeq++,"a|4|SC|하드",998413,t1,PlaySource.Play,NormalLink.Linked));profile.Plays.Add(new(profile.NextSeq++,"a|4|SC|하드",990000,t2,PlaySource.Manual,null,true));
    profile.Plays.Add(new(profile.NextSeq++,"a|4|SC|노말",995400,null,PlaySource.VArchive));profile.Plays.Add(new(profile.NextSeq++,"b|4|SC|하드",1000000,t2,PlaySource.Play,NormalLink.Cancelled));profile.Recent["a|4|SC|노말"]=played;
    profile.Undo.Add(new("add",t2,3,new(){new(null,profile.Plays[2])},new(){new("a|4|SC|노말",null,played)}));
    store.Save();var back=new RecordStore(fresh);
    Check(Json(back.Data)==Json(store.Data)&&back.Active.Plays.SequenceEqual(profile.Plays)&&back.Active.Undo[0].Plays[0].After==profile.Plays[2]&&!Directory.GetFiles(Path.GetDirectoryName(fresh)!,"records.format1*").Any(),"every field of format 2 comes back as written; a new file has no original to keep");
    string marked=At("bom");File.WriteAllBytes(marked,new byte[]{0xEF,0xBB,0xBF}.Concat(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(Old()))).ToArray());
    Check(new RecordStore(marked) is {Upgraded:true} bom&&bom.Active.Id=="p2","an old file that starts with a byte-order mark opens");
    string noNumber=At("no-number");File.WriteAllText(noNumber,"{\"ActiveId\":\"p\",\"Profiles\":[{\"Id\":\"p\",\"Name\":\"번호 없는 옛 파일\",\"Records\":{\"a|4|SC|노말\":{\"Accuracy\":99.54,\"At\":\"2026-10-01T12:00:00\"}},\"Undo\":[]}]}");
    Check(new RecordStore(noNumber) is {Upgraded:true} first&&first.Active.Plays.Single()==new Play(1,"a|4|SC|노말",995400,null,PlaySource.OldRate),"a file from before the format number existed is read as format 1");
    string outside=At("outside");WriteOld(outside);var watched=new RecordStore(outside);var changed=Old();changed.Profiles[1].Name="밖에서 고침";WriteOld(outside,changed);byte[] theirs=File.ReadAllBytes(outside);
    bool refused=false;try{watched.Save();}catch(RecordFileChangedException){refused=true;}
    Check(refused&&File.ReadAllBytes(outside).SequenceEqual(theirs)&&!File.Exists(outside+".tmp"),"a file changed outside the program is not overwritten");
    string frozen=At("frozen");WriteOld(frozen);byte[] cold=File.ReadAllBytes(frozen);var held=new RecordStore(frozen){Frozen=true};
    refused=false;try{held.Save();}catch(RecordStoreFrozenException){refused=true;}
    Check(refused&&File.ReadAllBytes(frozen).SequenceEqual(cold)&&Directory.GetFiles(Path.GetDirectoryName(frozen)!).Length==1,"while saving is frozen nothing is written, not even the copy of the original");
   }
  }
  finally{RecordStore.Step=null;}
  File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"play-store-validation.txt"),string.Join("\n",notes));
 }
}
