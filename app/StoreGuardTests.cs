using System.Text.Json;
namespace LadderRecords;
// 기록 파일의 보호 두 가지를 검사한다(2026-11-16, 리뷰 docs\reviews\20261115-저장되돌리기-보호검증-리뷰.md 의 1·2번).
//  1. 프로그램 밖에서 바뀌었거나 사라진 기록 파일은 덮어쓰지 않는다
//  2. 본 파일(records.json)이 없는데 백업(.bak)이나 저장 도중에 남은 임시 파일(.tmp)이 있으면 빈 기록으로 시작하지 않는다
// 합성 기록만 쓰고, 실행 파일 옆의 test-data 아래에만 쓴다. 결과는 store-guard-validation.txt
static class StoreGuardTests
{
 public static void Run()
 {
  var notes=new List<string>();void Check(bool value,string name){if(!value)throw new Exception(name);notes.Add("PASS: "+name);}
  string root=Path.Combine(AppContext.BaseDirectory,"test-data",Guid.NewGuid().ToString("N"));
  string At(string name){string dir=Path.Combine(root,name);Directory.CreateDirectory(dir);return Path.Combine(dir,"records.json");}
  var hard=new Selection("synthetic-a",4,"SC","하드",0);var other=new Selection("synthetic-b",4,"SC","노말",0);
  var time=new DateTime(2026,11,16,0,0,0,DateTimeKind.Utc);
  RecordStore Seed(string path){var s=new RecordStore(path);s.Put(hard,90m,measuredAt:time,rawScore:900000);s.Put(hard,91m,measuredAt:time.AddMinutes(1),rawScore:910000);return s;}
  string State(RecordStore s)=>JsonSerializer.Serialize(s.Data);
  bool Same(string path,byte[] bytes)=>File.Exists(path)&&File.ReadAllBytes(path).SequenceEqual(bytes);
  // 밖에서 바뀐 것을 알아채 막았는가: 저장을 막는 예외(RecordFileChangedException)가 나고, 메모리·저장 횟수·본 파일·백업이 그대로이고, 임시 파일이 남지 않는다
  void Blocked(string name,RecordStore s,string path,Action act,byte[]? main,byte[]? backup)
  {
   string before=State(s);long revision=s.Revision;Exception? caught=null;
   try{act();}catch(Exception e){caught=e;}
   Check(caught is RecordFileChangedException,name+": 저장을 막음");
   Check(State(s)==before&&s.Revision==revision,name+": 메모리와 저장 횟수 그대로");
   Check(main==null?!File.Exists(path):Same(path,main),name+": 본 파일 그대로");
   Check(backup==null?!File.Exists(path+".bak"):Same(path+".bak",backup),name+": 백업 그대로");
   Check(!File.Exists(path+".tmp"),name+": 임시 파일 남지 않음");
  }

  // ── 평소의 저장은 그대로 된다
  {
   string path=At("first-run");var s=new RecordStore(path);Check(s.Active.Records.Count==0&&!File.Exists(path),"처음 실행: 빈 기록으로 시작하고 파일을 만들지 않음");
   s.Put(hard,90m,rawScore:900000);Check(File.Exists(path)&&!File.Exists(path+".bak")&&!File.Exists(path+".tmp"),"처음 저장: 본 파일만 생김");
   byte[] first=File.ReadAllBytes(path);s.Put(hard,91m,rawScore:910000);Check(Same(path+".bak",first)&&new RecordStore(path).Get(hard)!.Value==910000,"둘째 저장: 백업은 바로 앞의 본 파일");
  }
  {
   string path=At("touched");var s=Seed(path);byte[] bytes=File.ReadAllBytes(path);
   File.SetLastWriteTimeUtc(path,DateTime.UtcNow.AddHours(3));s.Put(hard,92m,rawScore:920000);
   File.WriteAllBytes(path,File.ReadAllBytes(path));s.Put(hard,93m,rawScore:930000);
   Check(new RecordStore(path).Get(hard)!.Value==930000&&!bytes.SequenceEqual(File.ReadAllBytes(path)),"내용이 같으면 저장함(시각만 바뀜, 같은 내용으로 다시 씀)");
  }
  {
   string path=At("bom");var s=Seed(path);File.WriteAllBytes(path,new byte[]{0xEF,0xBB,0xBF}.Concat(File.ReadAllBytes(path)).ToArray());
   var reopened=new RecordStore(path);Check(reopened.Get(hard)!.Value==910000,"BOM 이 붙은 파일도 읽음");reopened.Put(hard,92m,rawScore:920000);Check(new RecordStore(path).Get(hard)!.Value==920000,"BOM 이 붙은 파일을 읽은 뒤 저장함");
  }
  {
   string path=At("stale-tmp");Seed(path);File.WriteAllText(path+".tmp","{");
   var s=new RecordStore(path);Check(s.Get(hard)!.Value==910000,"본 파일이 있으면 남은 임시 파일을 무시하고 시작함");s.Put(hard,92m,rawScore:920000);Check(!File.Exists(path+".tmp"),"다음 저장이 남은 임시 파일을 치움");
  }

  // ── 1번: 밖에서 바뀐 파일은 덮어쓰지 않는다
  {
   string path=At("external-save");var s=Seed(path);var foreign=new RecordStore(path);foreign.Put(hard,100m,rawScore:1000000);foreign.Put(other,99m,rawScore:990000);
   byte[] main=File.ReadAllBytes(path),backup=File.ReadAllBytes(path+".bak");
   Blocked("밖에서 바뀜·등록",s,path,()=>s.Put(hard,98m,rawScore:980000),main,backup);
   Blocked("밖에서 바뀜·되돌리기",s,path,s.Undo,main,backup);
   Blocked("밖에서 바뀜·수동 기록",s,path,()=>s.SetManual(hard,98m,5,rawScore:980000),main,backup);
   Blocked("밖에서 바뀜·기록 삭제",s,path,()=>s.DeleteRecord(hard),main,backup);
   Blocked("밖에서 바뀜·가져오기",s,path,()=>s.Import(new Dictionary<Selection,decimal>{{other,99m}}),main,backup);
   Blocked("밖에서 바뀜·프로필 만들기",s,path,()=>s.Create("새 프로필",true),main,backup);
   Blocked("밖에서 바뀜·프로필 이름",s,path,()=>s.Rename("다른 이름"),main,backup);
   var session=new Session();session.Detect(hard);session.Begin(s.Active.Id,measuredAt:time,measuredAccuracy:99m,rawScore:990000);
   Blocked("밖에서 바뀜·등록 확인",s,path,()=>session.Confirm(s),main,backup);Check(session.Pending!=null,"밖에서 바뀜·등록 확인: 확인 대기는 남음");
   var restarted=new RecordStore(path);Check(restarted.Get(hard)!.Value==1000000&&restarted.Get(other)!=null,"다시 켜면 밖에서 바뀐 기록을 읽음");
   restarted.Put(other,99.5m,rawScore:995000);Check(new RecordStore(path).Get(other)!.Value==995000&&Same(path+".bak",main),"다시 켠 뒤에는 저장되고, 밖에서 바뀐 파일이 백업으로 남음");
  }
  {
   string path=At("external-undo");var s=Seed(path);var foreign=new RecordStore(path);foreign.Put(hard,100m,rawScore:1000000);
   Blocked("밖에서 바뀜·바로 되돌리기",s,path,s.Undo,File.ReadAllBytes(path),File.ReadAllBytes(path+".bak"));
  }
  {
   string path=At("restored");var s=Seed(path);byte[] seen=File.ReadAllBytes(path);var foreign=new RecordStore(path);foreign.Put(hard,100m,rawScore:1000000);
   Blocked("바뀌었다가 돌아옴·바뀐 동안",s,path,()=>s.Rename("이름"),File.ReadAllBytes(path),File.ReadAllBytes(path+".bak"));
   File.WriteAllBytes(path,seen);s.Rename("이름");Check(new RecordStore(path).Active.Name=="이름","읽었던 내용으로 돌아오면 다시 저장함");
  }
  {
   string path=At("deleted");var s=Seed(path);byte[] backup=File.ReadAllBytes(path+".bak");File.Delete(path);
   Blocked("실행 중에 사라짐",s,path,()=>s.Put(hard,98m,rawScore:980000),null,backup);
  }
  {
   string path=At("appeared");var s=new RecordStore(path);byte[] foreign=File.ReadAllBytes(Path.Combine(root,"external-undo","records.json"));File.WriteAllBytes(path,foreign);
   Blocked("파일 없이 시작한 뒤 파일이 생김",s,path,()=>s.Put(hard,98m,rawScore:980000),foreign,null);
  }
  {
   // 다른 프로그램이 본 파일을 고치려고 열어 둔 동안에는 저장이 실패하고(RecordFileChangedException 이 아니라 보통의 파일 오류 IOException), 닫히면 한 번만 저장된다
   string path=At("being-written");var s=Seed(path);string before=State(s);long revision=s.Revision;byte[] main=File.ReadAllBytes(path);Exception? caught=null;
   using(new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.ReadWrite|FileShare.Delete)){try{s.Put(hard,98m,rawScore:980000);}catch(Exception e){caught=e;}}
   Check(caught is IOException and not RecordFileChangedException&&State(s)==before&&s.Revision==revision&&Same(path,main),"다른 쪽이 쓰려고 연 동안: 저장 실패, 메모리·본 파일 그대로");
   s.Put(hard,98m,rawScore:980000);Check(s.Revision==revision+1&&new RecordStore(path).Get(hard)!.PlayCount==3,"다른 쪽이 닫은 뒤: 한 번만 저장됨");
  }

  // ── 2번: 본 파일이 없는데 복구 자료가 남아 있으면 시작하지 않는다
  void Refused(string name,string path,string[] kept,string[] named)
  {
   var bytes=kept.ToDictionary(end=>end,end=>File.ReadAllBytes(path+end));Exception? caught=null;
   try{_=new RecordStore(path);}catch(Exception e){caught=e;}
   Check(caught is InvalidDataException,name+": 시작하지 않음");
   Check(named.All(caught!.Message.Contains)&&caught.Message.Contains(Path.GetDirectoryName(path)!),name+": 남은 파일과 폴더를 알려 줌");
   Check(!File.Exists(path)&&kept.All(end=>Same(path+end,bytes[end])),name+": 본 파일을 만들지 않고 남은 파일 그대로");
  }
  {
   string path=At("missing-main");Seed(path);File.Delete(path);
   Refused("본 파일 없음·백업 있음",path,new[]{".bak"},new[]{"records.json.bak","사본의 이름을 records.json 으로"});
   File.Copy(path+".bak",path);var s=new RecordStore(path);Check(s.Get(hard)!.Value==900000,"안내대로 백업의 사본을 본 파일로 두면 시작함(한 단계 앞의 기록)");
   s.Put(hard,95m,rawScore:950000);Check(new RecordStore(path).Get(hard)!.Value==950000,"되살린 뒤 저장함");
  }
  {
   string path=At("missing-main-tmp");File.WriteAllText(path+".tmp","{\"Profiles\":[");
   Refused("본 파일 없음·임시 파일만",path,new[]{".tmp"},new[]{"records.json.tmp","온전하지 않을 수"});
  }
  {
   string path=At("missing-main-both");Seed(path);File.Delete(path);File.WriteAllText(path+".tmp","{");
   Refused("본 파일 없음·백업과 임시 파일",path,new[]{".bak",".tmp"},new[]{"records.json.bak","records.json.tmp"});
  }
  File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"store-guard-validation.txt"),string.Join("\n",notes));
 }
}
