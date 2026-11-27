using System.Text.Json;
namespace LadderRecords;
// 개인 파일의 자리('UserData')와 첫 전환을 검사한다.
// 꾸민 프로그램 폴더를 실행 파일 옆의 test-data 아래에 만들어 쓴다. 실제 프로그램 폴더의 개인 파일은 건드리지 않는다. 결과는 user-files-validation.txt
static class UserFilesTests
{
 sealed class Cut():Exception("끊김(시험)");   // 그 자리에서 프로그램이 죽은 것으로 친다(전환이 잡지 않는 예외)
 public static void Run()
 {
  var notes=new List<string>();void Check(bool value,string name){if(!value)throw new Exception(name);notes.Add("PASS: "+name);}
  string root=Path.Combine(AppContext.BaseDirectory,"test-data","user-files-"+Guid.NewGuid().ToString("N"));
  var chart=new Selection("synthetic-a",4,"SC","노말",0);var time=new DateTime(2026,11,23,0,0,0,DateTimeKind.Utc);
  string App(string name){string dir=Path.Combine(root,name);Directory.CreateDirectory(dir);return dir;}
  string New(string app,string name)=>Path.Combine(UserFiles.FolderOf(app),name);
  string Old(string app,string name)=>Path.Combine(app,name);
  // v1.1.x 가 남긴 모양: 실행 파일 옆의 기록(두 번 저장해 백업도 있다)과 설정 넷
  void Legacy(string app,int score=900000)
  {
   var store=new RecordStore(Old(app,"records.json"));store.Put(chart,ScoreValue.Rate(score-100),measuredAt:time,rawScore:score-100);store.Put(chart,ScoreValue.Rate(score),measuredAt:time.AddMinutes(1),rawScore:score);
   foreach(string name in new[]{"hotkeys.json","preferences.json","records-view.json","appearance.json"})File.WriteAllText(Old(app,name),"{\"old\":\""+name+"\"}");
  }
  string Hash(string path)=>File.Exists(path)?UpdateFiles.Sha256(path):"";
  Dictionary<string,string> Snapshot(string dir)=>Directory.EnumerateFiles(dir,"*",SearchOption.TopDirectoryOnly).ToDictionary(f=>Path.GetFileName(f),Hash);
  bool Same(Dictionary<string,string> a,Dictionary<string,string> b)=>a.Count==b.Count&&a.All(p=>b.TryGetValue(p.Key,out string? v)&&v==p.Value);
  UserFiles.Info? Info(string app){string path=New(app,UserFiles.InfoName);return File.Exists(path)?JsonSerializer.Deserialize<UserFiles.Info>(File.ReadAllText(path)):null;}
  string Refused(Action act){try{act();}catch(UserFilesException e){return e.Message;}catch(InvalidDataException e){return "기록을 읽는 쪽: "+e.Message;}return "";}
  // 알릴 것이 있으면 사용자가 본 것으로 친다(주 화면이 알림 창을 닫은 뒤에 하는 일)
  string? See(string app){var result=UserFiles.Prepare(app);if(result.Notice!=null)UserFiles.Shown(app,result.NoticeKey);return result.Notice;}
  FileStream Lock(string path)=>new(path,FileMode.Open,FileAccess.Read,FileShare.None);
  string Keep(string app)=>app+" 보관";   // 안내대로 "다른 폴더로 옮긴" 파일을 두는 곳(프로그램 폴더 밖)
  try
  {
   // 1. 새 설치: 폴더만 생기고, 적을 것이 없으니 표시 파일도 없다. 첫 저장 때 기록 파일이 새 자리에 생긴다
   {
    string app=App("fresh");var result=UserFiles.Prepare(app);
    Check(Directory.Exists(UserFiles.FolderOf(app))&&result.Copied.Count==0&&result.Notice==null&&result.Store==null&&Info(app)==null&&!Directory.EnumerateFileSystemEntries(UserFiles.FolderOf(app)).Any(),"a new install: the folder is made and nothing else");
    var store=new RecordStore(New(app,"records.json"));store.Put(chart,90m,measuredAt:time,rawScore:900000);
    Check(File.Exists(New(app,"records.json"))&&!File.Exists(Old(app,"records.json")),"the first save writes the records in the new place only");
    result=UserFiles.Prepare(app);
    Check(result.Copied.Count==0&&result.Notice==null&&result.Store?.Get(chart) is {Value:900000}&&Info(app) is {} info&&info.Settled.Contains("records.json")&&info.OldRecords=="","the next start settles the records that are there and hands over the opened records");
   }
   // 2. v1.1.x 에서 올라옴: 있는 것을 모두 옮기고 옛 파일은 그대로다. 다시 켜면 아무것도 하지 않는다
   {
    string app=App("legacy");Legacy(app);var before=Snapshot(app);
    var result=UserFiles.Prepare(app);
    Check(result.Copied.SequenceEqual(UserFiles.Names)&&UserFiles.Names.All(name=>Hash(New(app,name))==before[name])&&result.Notice==null,"from v1.1.x: every file is copied, records first");
    Check(Same(Snapshot(app),before),"the old files are left as they were");
    Check(Info(app) is {} info&&info.Settled.SequenceEqual(UserFiles.Names)&&info.OldRecords==before["records.json"]&&string.IsNullOrEmpty(info.Moving)&&!Directory.EnumerateFiles(UserFiles.FolderOf(app),"*.moving").Any(),"what was settled is written down at the end");
    Check(result.Store?.Get(chart) is {Value:900000}&&new RecordStore(New(app,"records.json")).Get(chart) is {Value:900000},"the copied records open");
    string stamp=File.ReadAllText(New(app,UserFiles.InfoName));
    result=UserFiles.Prepare(app);
    Check(result.Copied.Count==0&&result.Notice==null&&File.ReadAllText(New(app,UserFiles.InfoName))==stamp,"starting again copies nothing and writes nothing");
    // 9. 새 자리에서 지운 것은 옛 자리에서 다시 채우지 않는다
    File.Delete(New(app,"hotkeys.json"));File.Delete(New(app,"records.json"));File.Delete(New(app,"records.json.bak"));
    result=UserFiles.Prepare(app);
    Check(result.Copied.Count==0&&!File.Exists(New(app,"hotkeys.json"))&&!File.Exists(New(app,"records.json")),"what the user removed from the new place is not brought back");
   }
   // 3. 새 폴더에 설정만 있음: 그 설정은 그대로, 기록과 빠진 것만 옮긴다
   {
    string app=App("settings-only");Legacy(app);Directory.CreateDirectory(UserFiles.FolderOf(app));File.WriteAllText(New(app,"preferences.json"),"{\"new\":true}");
    var result=UserFiles.Prepare(app);
    Check(File.ReadAllText(New(app,"preferences.json"))=="{\"new\":true}"&&!result.Copied.Contains("preferences.json")&&result.Copied.Contains("records.json")&&result.Copied.Contains("hotkeys.json"),"settings already in the new place are kept, the rest is copied");
   }
   // 4. 새 폴더에 정상 기록(0건이어도): 그 기록을 쓴다. 옛 기록과 그 백업은 옮기지 않고, 처음에는 알리지 않는다
   {
    string app=App("both");Legacy(app);Directory.CreateDirectory(UserFiles.FolderOf(app));
    new RecordStore(New(app,"records.json")).Rename("새 폴더의 프로필");string mine=Hash(New(app,"records.json"));
    var result=UserFiles.Prepare(app);
    Check(Hash(New(app,"records.json"))==mine&&!File.Exists(New(app,"records.json.bak"))&&!result.Copied.Contains("records.json")&&!result.Copied.Contains("records.json.bak")&&result.Notice==null,"records already in the new place win; the old backup is not put beside them");
    Check(result.Copied.Contains("hotkeys.json")&&new RecordStore(New(app,"records.json")).Active.Name=="새 폴더의 프로필","the missing settings still come over");
    // 10. 되돌린 이전 버전이 옛 자리에 저장함: 사용자가 볼 때까지 알리고, 본 뒤에는 알리지 않는다. 또 달라지면 또 알린다. 11. 시각만 달라진 것은 알리지 않는다
    File.SetLastWriteTimeUtc(Old(app,"records.json"),DateTime.UtcNow.AddDays(1));
    Check(UserFiles.Prepare(app).Notice==null,"only the time of the old file changed: no notice");
    new RecordStore(Old(app,"records.json")).Put(chart,95m,measuredAt:time.AddDays(1),rawScore:950000);
    Check(UserFiles.Prepare(app).Notice==UserFiles.ChangedText&&UserFiles.Prepare(app).Notice==UserFiles.ChangedText,"the old records changed since the move: said, and said again while it has not been seen");
    Check(See(app)==UserFiles.ChangedText&&UserFiles.Prepare(app).Notice==null,"once it has been seen it is not said again");
    new RecordStore(Old(app,"records.json")).Put(chart,96m,measuredAt:time.AddDays(2),rawScore:960000);
    Check(See(app)==UserFiles.ChangedText&&UserFiles.Prepare(app).Notice==null&&Hash(New(app,"records.json"))==mine,"changed again: said once more; the new place is still what is used");
    // 이미 자리 잡은 뒤에는 옛 기록을 잠깐 읽지 못해도 시작을 막지 않는다(이번에는 견주지 않을 뿐이다)
    new RecordStore(Old(app,"records.json")).Put(chart,97m,measuredAt:time.AddDays(3),rawScore:970000);
    using(Lock(Old(app,"records.json")))Check(UserFiles.Prepare(app) is {Notice:null,Copied.Count:0},"settled, and the old records are locked for a moment: it still starts, without comparing");
    Check(UserFiles.Prepare(app).Notice==UserFiles.ChangedText,"when they can be read again the change is said");
   }
   // 사용자가 새 자리에 본 파일만 미리 넣어 둠(옛 것과 같은 내용이어도): 옛 백업을 그 옆에 두지 않는다(이 프로그램이 옮기던 것이 아니다)
   {
    string app=App("same-records");Legacy(app);Directory.CreateDirectory(UserFiles.FolderOf(app));File.Copy(Old(app,"records.json"),New(app,"records.json"));
    var result=UserFiles.Prepare(app);
    Check(!result.Copied.Contains("records.json")&&!result.Copied.Contains("records.json.bak")&&!File.Exists(New(app,"records.json.bak"))&&Info(app)!.Settled.Contains("records.json.bak"),"records put there by the user, even the same ones: the old backup does not follow");
   }
   // 5. 새 자리의 기록을 읽을 수 없거나 백업만 있음: 아무것도 옮기지 않고 표시도 쓰지 않은 채 시작하지 않으며 두 자리를 알린다
   {
    string app=App("unreadable");Legacy(app);Directory.CreateDirectory(UserFiles.FolderOf(app));File.WriteAllText(New(app,"records.json"),"{ 깨진 파일");
    string said=Refused(()=>UserFiles.Prepare(app));
    Check(said.StartsWith("기록 파일을 읽을 수 없습니다.")&&said.Contains(New(app,"records.json"))&&said.Contains("프로그램 폴더: "+app)&&said.Contains("UserData 폴더: "+UserFiles.FolderOf(app)),"unreadable records in the new place: not started, both places named");
    Check(File.ReadAllText(New(app,"records.json"))=="{ 깨진 파일"&&Snapshot(UserFiles.FolderOf(app)).Count==1,"nothing else is copied and no mark is written before refusing");
    string only=App("backup-only-new");Legacy(only);Directory.CreateDirectory(UserFiles.FolderOf(only));File.WriteAllText(New(only,"records.json.bak"),"{}");
    said=Refused(()=>UserFiles.Prepare(only));
    Check(said.StartsWith("‘UserData’ 폴더에 기록 파일(records.json)이 없고 records.json.bak만 남아 있습니다.")&&!said.Contains("남은 파일이 있는 폴더")&&said.Contains("UserData 폴더: "+UserFiles.FolderOf(only))&&said.Contains("프로그램 폴더: "+only)&&Snapshot(UserFiles.FolderOf(only)).Count==1,"only a backup in the new place: the old records are not copied over it, not started, nothing written");
    Check(said.Contains("다시 실행하면: 프로그램 폴더의 기록(records.json)을 가져와 시작합니다.")&&said.Contains("UserData 폴더의 파일(records.json.bak)과 프로그램 폴더의 파일(records.json, records.json.bak)을 위치별로 따로 다른 폴더에 옮긴 뒤")&&!said.Contains("빈 기록으로 새로 시작하려면: 남은 파일을"),"first move with records in the old place: moving the leftovers away brings those records in, and an empty start names the files of both places");
    // 안내대로 해 본다: 새 자리에 남은 파일을 치우고 다시 켜면 옛 기록으로 열린다
    Directory.CreateDirectory(Keep(only));File.Move(New(only,"records.json.bak"),Path.Combine(Keep(only),"records.json.bak"));
    var followed=UserFiles.Prepare(only);
    Check(followed.Copied.Contains("records.json")&&followed.Store?.Get(chart) is {Value:900000},"doing what it says: the old records come in");
    // 빈 기록으로 시작하는 쪽: 옛 자리에 저장하다 만 파일까지 남아 있으면 그것도 이름에 든다. 적힌 것을 모두 치우면 빈 기록으로 열린다
    string clean=App("empty-start");Legacy(clean);File.WriteAllText(Old(clean,"records.json.tmp"),"{");Directory.CreateDirectory(UserFiles.FolderOf(clean));File.WriteAllText(New(clean,"records.json.tmp"),"{");
    said=Refused(()=>UserFiles.Prepare(clean));
    Check(said.Contains("UserData 폴더의 파일(records.json.tmp)과 프로그램 폴더의 파일(records.json, records.json.bak, records.json.tmp)을 위치별로 따로"),"the empty start names every records file that is really left in both places, the half-written one too");
    Directory.CreateDirectory(Path.Combine(Keep(clean),"UserData"));Directory.CreateDirectory(Path.Combine(Keep(clean),"프로그램 폴더"));
    File.Move(New(clean,"records.json.tmp"),Path.Combine(Keep(clean),"UserData","records.json.tmp"));foreach(string name in new[]{"records.json","records.json.bak","records.json.tmp"})File.Move(Old(clean,name),Path.Combine(Keep(clean),"프로그램 폴더",name));
    followed=UserFiles.Prepare(clean);
    Check(followed.Store==null&&!followed.Copied.Contains("records.json")&&!File.Exists(New(clean,"records.json"))&&followed.Copied.Contains("hotkeys.json"),"doing what it says: an empty start (the settings still come over)");
    // 전환을 마친 설치에서 새 자리의 본 파일만 사라짐: 남은 파일을 치우면 옛 기록을 가져오지 않고 빈 기록으로 시작한다. 그렇게 적고, 옛 기록을 쓰는 방법을 따로 적는다
    string done=App("settled-lost");Legacy(done);UserFiles.Prepare(done);File.Delete(New(done,"records.json"));
    said=Refused(()=>UserFiles.Prepare(done));
    Check(said.Contains("· 빈 기록으로 새로 시작하려면: UserData 폴더에 남은 파일(records.json.bak)을 다른 폴더로 옮긴 뒤 다시 실행해 주세요. 프로그램 폴더의 파일은 그대로 두어도 됩니다.")&&said.Contains("· 프로그램 폴더의 기록을 쓰려면: 프로그램 폴더의 records.json을 UserData 폴더에 복사한 뒤 다시 실행해 주세요.")&&!said.Contains("가져와 시작합니다"),"after the move is done: it does not promise the old records, says the old place can stay as it is, and says how to use the old records");
    Check(said.Contains("UserData 폴더에 남은 기록 파일: records.json.bak\n")&&said.Contains("프로그램 폴더에 있는 기록 파일: records.json, records.json.bak\n"),"the records files that are really in each place are listed, here too");
    Directory.CreateDirectory(Keep(done));File.Move(New(done,"records.json.bak"),Path.Combine(Keep(done),"records.json.bak"));
    followed=UserFiles.Prepare(done);
    Check(followed.Copied.Count==0&&followed.Store==null&&!File.Exists(New(done,"records.json")),"doing what it says: an empty start, nothing brought back");
    string picked=App("settled-lost-copy");Legacy(picked);UserFiles.Prepare(picked);File.Delete(New(picked,"records.json"));File.Copy(Old(picked,"records.json"),New(picked,"records.json"));
    Check(UserFiles.Prepare(picked).Store?.Get(chart) is {Value:900000},"doing the other thing it says (copying the old records in): they are used");
    // 옛 자리에도 백업만 남음: 새 자리의 것만 치우면 다시 멈춘다고 적는다. 둘 다 치우면 빈 기록으로 열린다
    string twice=App("both-leftovers");Legacy(twice);File.Delete(Old(twice,"records.json"));Directory.CreateDirectory(UserFiles.FolderOf(twice));File.WriteAllText(New(twice,"records.json.tmp"),"{");
    said=Refused(()=>UserFiles.Prepare(twice));
    Check(said.Contains("UserData 폴더의 파일(records.json.tmp)과 프로그램 폴더의 파일(records.json.bak)을 위치별로 따로")&&said.Contains("· UserData 폴더의 파일만 옮기면 프로그램 폴더에 남은 파일 때문에 다시 멈춥니다."),"leftovers in both places: it says that clearing only the new place stops again");
    Directory.CreateDirectory(Keep(twice));File.Move(New(twice,"records.json.tmp"),Path.Combine(Keep(twice),"new.tmp"));
    said=Refused(()=>UserFiles.Prepare(twice));
    Check(said.Contains("records.json.bak만 남아 있습니다")&&said.Contains("프로그램 폴더에 기록 파일")&&!said.Contains("남은 파일이 있는 폴더")&&said.Contains("프로그램 폴더: "+twice),"and it does stop again, on the old place");
    File.Move(Old(twice,"records.json.bak"),Path.Combine(Keep(twice),"old.bak"));
    Check(UserFiles.Prepare(twice).Store==null,"both cleared: an empty start");
    // 표시 파일까지 깨져 있음: 남은 파일만 치우면 표시 파일의 안내에서 다시 멈추므로 표시 파일도 함께 치우라고 적는다. 그대로 하면 프로그램 폴더의 기록을 다시 가져온다
    string worse=App("broken-and-leftover");Legacy(worse);UserFiles.Prepare(worse);File.Delete(New(worse,"records.json"));File.WriteAllText(New(worse,UserFiles.InfoName),"{ 깨짐");
    said=Refused(()=>UserFiles.Prepare(worse));
    Check(said.Contains("· 프로그램 폴더에 남은 파일로 다시 옮기려면: UserData 폴더에 남은 파일(records.json.bak)과 기록 위치 정보 파일(folder-info.json)을 다른 폴더로 옮긴 뒤 다시 실행해 주세요.")&&said.Contains("UserData 폴더에 남은 기록 파일: records.json.bak\n")&&said.Contains("프로그램 폴더에 있는 기록 파일: records.json, records.json.bak\n"),"with a broken mark as well: it lists both places and says to move the mark away together with the leftover");
    Directory.CreateDirectory(Keep(worse));File.Move(New(worse,"records.json.bak"),Path.Combine(Keep(worse),"records.json.bak"));
    Check(Refused(()=>UserFiles.Prepare(worse)).StartsWith("기록 위치 정보 파일(folder-info.json)을 읽을 수 없고"),"moving the leftover alone stops on the broken mark (why it says to move the mark too)");
    File.Move(New(worse,UserFiles.InfoName),Path.Combine(Keep(worse),UserFiles.InfoName));followed=UserFiles.Prepare(worse);
    Check(followed.Copied.Contains("records.json")&&followed.Store?.Get(chart) is {Value:900000},"doing what it says (moving the leftover and the mark away): the records in the program folder come in again");
    // 표시 파일이 깨졌고 옛 자리에는 저장하다 만 파일만 있음: 새 자리의 것만 치우면 그 파일 때문에 다시 멈춘다고 적는다. 둘 다 치우면 빈 기록으로 열린다
    string stuck=App("broken-old-tmp");File.WriteAllText(Old(stuck,"records.json.tmp"),"{");Directory.CreateDirectory(UserFiles.FolderOf(stuck));File.WriteAllText(New(stuck,"records.json.bak"),"{}");File.WriteAllText(New(stuck,UserFiles.InfoName),"{ 깨짐");
    said=Refused(()=>UserFiles.Prepare(stuck));
    Check(said.Contains("프로그램 폴더에 있는 기록 파일: records.json.tmp\n")&&said.Contains("UserData 폴더의 파일(records.json.bak)과 프로그램 폴더의 파일(records.json.tmp)을 위치별로 따로")&&said.Contains("· UserData 폴더의 파일만 옮기면 프로그램 폴더에 남은 파일 때문에 다시 멈춥니다.")&&!said.Contains("남은 파일을 다른 폴더로 옮긴 뒤 다시 실행해 주세요."),"a broken mark and only a half-written file in the old place: it does not promise an empty start from clearing the new place alone");
    Directory.CreateDirectory(Keep(stuck));File.Move(New(stuck,"records.json.bak"),Path.Combine(Keep(stuck),"new.bak"));
    said=Refused(()=>UserFiles.Prepare(stuck));
    Check(said.Contains("records.json.tmp만 남아 있습니다")&&said.Contains("프로그램 폴더에 기록 파일")&&!said.Contains("남은 파일이 있는 폴더")&&said.Contains("프로그램 폴더: "+stuck),"and clearing the new place alone does stop again, on the old place");
    File.Move(Old(stuck,"records.json.tmp"),Path.Combine(Keep(stuck),"old.tmp"));
    Check(UserFiles.Prepare(stuck).Store==null,"both cleared: an empty start");
    string alone=App("backup-only-new-alone");Directory.CreateDirectory(UserFiles.FolderOf(alone));File.WriteAllText(New(alone,"records.json.tmp"),"{");
    said=Refused(()=>UserFiles.Prepare(alone));
    Check(said.Contains("records.json.tmp만 남아 있습니다")&&said.Contains("· 빈 기록으로 새로 시작하려면: 남은 파일을 다른 폴더로 옮긴 뒤 다시 실행해 주세요.")&&said.Contains("UserData 폴더에 남은 기록 파일: records.json.tmp\n")&&said.Contains("프로그램 폴더에 있는 기록 파일: 없음\n"),"with nothing in the old place, moving the leftovers away starts empty, and it says so");
   }
   // 6. 옛 자리에 백업·저장하다 만 파일만 있고 새 자리에 기록이 없음: 빈 기록으로 시작하지 않는다
   {
    string app=App("backup-only-old");Legacy(app);File.Delete(Old(app,"records.json"));File.WriteAllText(Old(app,"records.json.tmp"),"{");var before=Snapshot(app);
    string said=Refused(()=>UserFiles.Prepare(app));
    Check(said.Contains("records.json.bak, records.json.tmp만 남아 있습니다")&&said.Contains("프로그램 폴더에 기록 파일")&&!said.Contains("남은 파일이 있는 폴더")&&said.Contains("프로그램 폴더: "+app)&&said.Contains("UserData 폴더: "+UserFiles.FolderOf(app))&&Same(Snapshot(app),before)&&!File.Exists(New(app,"hotkeys.json")),"only a backup and a half-written file in the old place: not started, nothing copied");
    string broken=App("unreadable-old");Legacy(broken);File.WriteAllText(Old(broken,"records.json"),"{ 깨진 옛 파일");
    said=Refused(()=>UserFiles.Prepare(broken));
    Check(said.StartsWith("기록 파일을 읽을 수 없습니다.")&&said.Contains(Old(broken,"records.json"))&&!Directory.EnumerateFileSystemEntries(UserFiles.FolderOf(broken)).Any(),"unreadable records in the old place: not copied, not started");
    Check(said.Contains("프로그램 폴더: "+broken)&&said.Contains("UserData 폴더: "+UserFiles.FolderOf(broken))&&!said.Contains("\n폴더: "+broken)&&!said.EndsWith("\n폴더: "+broken),"and that notice names both places too (not the plain folder line)");
   }
   // 7. 복사하다 끊김: 이 프로그램이 본 파일을 옮기던 것이므로 다시 켜면 백업을 마저 옮긴다. 이 전환이 쓰는 임시 이름만 버린다
   {
    string app=App("cut");Legacy(app);var before=Snapshot(app);
    UserFiles.Step=at=>{if(at=="copy:records.json.bak")throw new Cut();};
    bool cut=false;try{UserFiles.Prepare(app);}catch(Cut){cut=true;}
    UserFiles.Step=null;
    Check(cut&&File.Exists(New(app,"records.json"))&&!File.Exists(New(app,"records.json.bak"))&&Info(app) is {Settled.Count:0} begun&&begun.Moving==before["records.json"],"cut after the records and before their backup: only 'moving these records' is written down");
    File.WriteAllText(New(app,"hotkeys.json.moving"),"반쯤 복사된 것");File.WriteAllText(New(app,"other.moving"),"사용자의 파일");
    var result=UserFiles.Prepare(app);
    Check(result.Copied.SequenceEqual(UserFiles.Names.Skip(1))&&UserFiles.Names.All(name=>Hash(New(app,name))==before[name])&&Info(app) is {Settled.Count:6} done&&string.IsNullOrEmpty(done.Moving)&&Same(Snapshot(app),before),"the next start finishes the move, backup included");
    Check(!File.Exists(New(app,"hotkeys.json.moving"))&&File.ReadAllText(New(app,"other.moving"))=="사용자의 파일","its own half-copied file is thrown away; another file with the same ending is left alone");
    // 끊긴 사이에 옛 자리의 기록이 달라졌으면(되돌린 이전 버전이 저장함) 그 백업은 옮겨 온 기록의 짝이 아니다: 가져오지 않고, 달라졌다고 알린다
    string moved=App("cut-then-changed");Legacy(moved);
    UserFiles.Step=at=>{if(at=="copy:records.json.bak")throw new Cut();};
    try{UserFiles.Prepare(moved);}catch(Cut){}
    UserFiles.Step=null;new RecordStore(Old(moved,"records.json")).Put(chart,95m,measuredAt:time.AddDays(1),rawScore:950000);
    result=UserFiles.Prepare(moved);
    Check(!result.Copied.Contains("records.json.bak")&&!File.Exists(New(moved,"records.json.bak"))&&result.Notice==UserFiles.ChangedText&&new RecordStore(New(moved,"records.json")).Get(chart) is {Value:900000},"cut, and the old records changed meanwhile: their backup is not taken, and the change is said");
    // 맨 처음(옮기는 중이라고 적기 직전)에 끊기면 아무것도 옮겨지지 않았다. 마지막 표시를 쓰기 직전에 끊기면 다음에는 표시만 쓴다
    string early=App("cut-first");Legacy(early);
    UserFiles.Step=at=>{if(at=="info")throw new Cut();};
    cut=false;try{UserFiles.Prepare(early);}catch(Cut){cut=true;}
    UserFiles.Step=null;
    Check(cut&&!Directory.EnumerateFileSystemEntries(UserFiles.FolderOf(early)).Any()&&UserFiles.Prepare(early).Copied.Count==6,"cut before anything was written: nothing was moved, and the next start does it all");
    string late=App("cut-info");Legacy(late);int marks=0;
    UserFiles.Step=at=>{if(at=="info"&&++marks==2)throw new Cut();};
    cut=false;try{UserFiles.Prepare(late);}catch(Cut){cut=true;}
    UserFiles.Step=null;result=UserFiles.Prepare(late);
    Check(cut&&result.Copied.Count==0&&Info(late) is {Settled.Count:6} marked&&string.IsNullOrEmpty(marked.Moving)&&result.Notice==null,"cut just before the last mark: the next start only writes the mark");
    // 옮길 것이 없는 평소의 시작에서 위치 정보 파일을 저장하지 못하면 옮기기의 글을 쓰지 않는다
    string settledApp=App("settled-write");UserFiles.Prepare(settledApp);File.WriteAllText(New(settledApp,"hotkeys.json"),"[]");
    UserFiles.Step=at=>{if(at=="info")throw new IOException("막힘(시험)");};string plain=Refused(()=>UserFiles.Prepare(settledApp));UserFiles.Step=null;
    Check(plain.StartsWith("기록 위치 정보 파일(folder-info.json)을 저장하지 못했습니다.")&&!plain.Contains("옮기기를 이어갑니다")&&!plain.Contains("원본은 보존되어"),"a mark that cannot be written on a plain start says nothing about moving");
   }
   // 8. 복사에 실패: 시작하지 않는다. 옛 파일은 그대로이고 이미 옮긴 것은 남는다. 까닭에 따라 다음 행동이 다르다. 다시 켜면 이어서 한다
   {
    string app=App("failed");Legacy(app);var before=Snapshot(app);
    string Fail(Exception error){UserFiles.Step=at=>{if(at=="copy:hotkeys.json")throw error;};try{return Refused(()=>UserFiles.Prepare(app));}finally{UserFiles.Step=null;}}
    string full=Fail(new IOException("디스크가 가득 참(시험)",unchecked((int)0x80070070)));
    Check(full.StartsWith("hotkeys.json을 UserData 폴더에 복사하지 못했습니다. 프로그램 폴더의 원본은 보존되어 있습니다. 저장 공간을 확보한 뒤 다시 실행해 주세요.")&&full.Contains("그러면 이미 복사한 파일을 확인하고 옮기기를 이어갑니다.")&&full.Contains("프로그램 폴더: "+app)&&full.Contains("UserData 폴더: "+UserFiles.FolderOf(app)),"a full disk: not started, told to free space");
    Check(Same(Snapshot(app),before)&&File.Exists(New(app,"records.json"))&&!File.Exists(New(app,"hotkeys.json"))&&Info(app) is {Settled.Count:0}&&!Directory.EnumerateFiles(UserFiles.FolderOf(app),"*.moving").Any(),"the old files are untouched, what was already copied stays, and nothing is marked as done");
    Check(Fail(new IOException("다른 프로그램이 쓰는 중(시험)",unchecked((int)0x80070020))).Contains("이 파일을 사용 중인 다른 프로그램을 닫은 뒤 다시 실행해 주세요.")&&Fail(new UnauthorizedAccessException("쓸 수 없음(시험)")).Contains("표시된 폴더에 파일을 쓸 수 있는지 확인한 뒤 다시 실행해 주세요."),"a file in use and a folder that cannot be written get their own advice");
    var result=UserFiles.Prepare(app);
    Check(result.Copied.SequenceEqual(UserFiles.Names.Skip(2))&&Info(app)!.Settled.Count==6,"once it can copy, the move goes on from where it stopped");
   }
   // 견줄 파일이 실제로 잠겨 있음(다른 프로그램이 잡고 있다): 읽지 못한 것을 "없음"으로 치지 않는다. 시작하지 않고 알리며, 풀리면 이어서 한다
   {
    string app=App("locked");Legacy(app);var before=Snapshot(app);
    UserFiles.Step=at=>{if(at=="copy:records.json.bak")throw new Cut();};
    try{UserFiles.Prepare(app);}catch(Cut){}
    UserFiles.Step=null;string said;
    using(Lock(Old(app,"records.json")))said=Refused(()=>UserFiles.Prepare(app));
    Check(said.StartsWith("records.json의 내용을 확인하지 못했습니다.")&&said.Contains("이 파일을 사용 중인 다른 프로그램을 닫은 뒤 다시 실행해 주세요.")&&said.Contains("프로그램 폴더: "+app)&&!File.Exists(New(app,"records.json.bak"))&&Info(app) is {Settled.Count:0},"resuming while the old records are locked: not started, nothing marked, the backup is not given up");
    var result=UserFiles.Prepare(app);
    Check(result.Copied.Contains("records.json.bak")&&Hash(New(app,"records.json.bak"))==before["records.json.bak"]&&result.Notice==null&&UserFiles.Prepare(app).Notice==null,"unlocked: the backup comes over and no change is announced");
    using(Lock(New(app,"records.json")))said=Refused(()=>UserFiles.Prepare(app));
    Check(said.StartsWith("기록 파일을 읽을 수 없습니다.")&&said.Contains(New(app,"records.json"))&&said.Contains("프로그램 폴더: "+app)&&said.Contains("UserData 폴더: "+UserFiles.FolderOf(app)),"the new records locked: the same notice with both places, not a system error");
    Check(File.Exists(New(app,"records.json.bak"))&&said.Contains("다른 프로그램이 사용하고 있습니다")&&!said.Contains("백업으로 되살리려면"),"a locked file is not a broken one: it says what holds it and does not offer the backup (that would drop the last save)");
    using(Lock(New(app,UserFiles.InfoName)))said=Refused(()=>UserFiles.Prepare(app));
    Check(said.StartsWith("기록 위치 정보 파일(folder-info.json)을 읽지 못했습니다.")&&Info(app)!.Settled.Count==6,"the mark locked for a moment is not taken for a broken one: not started, and it is not rewritten");
    Check(!said.Contains("옮기기를 이어갑니다")&&!said.Contains("원본은 보존되어"),"a failure that is not part of moving says nothing about moving");
   }
   // 12. 새 구조끼리의 수동 업데이트: 새 폴더만 있는 프로그램 폴더는 그대로 읽는다
   {
    string from=App("manual-from");Legacy(from);UserFiles.Prepare(from);string app=App("manual-to");
    Directory.CreateDirectory(UserFiles.FolderOf(app));foreach(string file in Directory.EnumerateFiles(UserFiles.FolderOf(from)))File.Copy(file,Path.Combine(UserFiles.FolderOf(app),Path.GetFileName(file)));
    var result=UserFiles.Prepare(app);
    Check(result.Copied.Count==0&&result.Notice==null&&new RecordStore(New(app,"records.json")).Get(chart) is {Value:900000},"the folder copied into a new program folder is used as it is");
   }
   // 13. 새로 설치해 한 번 켠 뒤에 옛 파일을 실행 파일 옆에 가져다 놓음: 새 자리에 아직 기록이 없으면 옮겨 온다. 이미 저장한 기록이 있으면 그 기록을 쓰고 한 번 알린다
   {
    string app=App("late");UserFiles.Prepare(app);Legacy(app);
    var result=UserFiles.Prepare(app);
    Check(result.Copied.Contains("records.json")&&result.Copied.Contains("records.json.bak")&&result.Notice==null,"old files dropped in later, nothing saved yet: they come over");
    string saved=App("late-saved");UserFiles.Prepare(saved);new RecordStore(New(saved,"records.json")).Rename("이미 저장함");UserFiles.Prepare(saved);Legacy(saved);
    result=UserFiles.Prepare(saved);
    Check(!result.Copied.Contains("records.json")&&result.Notice==UserFiles.ChangedText&&See(saved)==UserFiles.ChangedText&&UserFiles.Prepare(saved).Notice==null&&new RecordStore(New(saved,"records.json")).Active.Name=="이미 저장함","old files dropped in after saving: the new records stay and it is said until seen");
   }
   // 표시 파일의 내용이 깨짐
   {
    // 새 자리에 기록이 있으면: 전환은 끝난 것으로 보고 옛 자리에서 아무것도 가져오지 않는다. 표시는 다시 쓴다
    string app=App("bad-info");Legacy(app);UserFiles.Prepare(app);File.Delete(New(app,"hotkeys.json"));File.WriteAllText(New(app,UserFiles.InfoName),"{ 깨짐");
    var result=UserFiles.Prepare(app);
    Check(result.Copied.Count==0&&!File.Exists(New(app,"hotkeys.json"))&&result.Notice==null&&Info(app) is {} info&&info.Settled.Count==6,"a broken mark with good records in place: nothing is refilled, the mark is rewritten");
    // 새 자리에 기록도 없으면: 처음 하는 것으로 보지 않는다(사용자가 치운 것일 수 있다). 되살리지 않고 묻는다. 안내대로 표시를 치우면 옛 것을 가져온다
    string gone=App("bad-info-no-records");Legacy(gone);UserFiles.Prepare(gone);File.Delete(New(gone,"records.json"));File.Delete(New(gone,"records.json.bak"));File.Delete(New(gone,"hotkeys.json"));File.WriteAllText(New(gone,UserFiles.InfoName),"{ 깨짐");
    string said=Refused(()=>UserFiles.Prepare(gone));
    Check(said.StartsWith("기록 위치 정보 파일(folder-info.json)을 읽을 수 없고 UserData 폴더에 기록 파일이 없습니다.")&&said.Contains("· 프로그램 폴더에 남은 파일로 다시 옮기려면: UserData 폴더의 folder-info.json을 다른 폴더로 옮긴 뒤")&&said.Contains("프로그램 폴더: "+gone)&&said.Contains("UserData 폴더: "+UserFiles.FolderOf(gone)),"a broken mark and no records in the new place: not started, and it asks");
    Check(!File.Exists(New(gone,"records.json"))&&!File.Exists(New(gone,"hotkeys.json"))&&File.ReadAllText(New(gone,UserFiles.InfoName))=="{ 깨짐","what the user cleared is not brought back, and the mark is left as it is");
    File.Delete(New(gone,UserFiles.InfoName));result=UserFiles.Prepare(gone);
    Check(result.Copied.Contains("records.json")&&result.Copied.Contains("hotkeys.json"),"following the notice (moving the mark away) brings the old files in");
    // 옛 자리에 백업만 남은 채로 같은 안내를 따르면: 이전을 다시 해 보고, 옛 자리의 백업 안내로 이어진다("옛 기록으로 시작한다"고 적지 않은 까닭)
    string half=App("bad-info-old-backup");Legacy(half);UserFiles.Prepare(half);File.Delete(New(half,"records.json"));File.Delete(New(half,"records.json.bak"));File.Delete(Old(half,"records.json"));File.WriteAllText(New(half,UserFiles.InfoName),"{ 깨짐");
    Check(Refused(()=>UserFiles.Prepare(half)).StartsWith("기록 위치 정보 파일(folder-info.json)을 읽을 수 없고"),"a broken mark, no new records, only a backup in the old place: it asks");
    File.Delete(New(half,UserFiles.InfoName));said=Refused(()=>UserFiles.Prepare(half));
    Check(said.Contains("records.json.bak만 남아 있습니다")&&said.Contains("프로그램 폴더에 기록 파일")&&!said.Contains("남은 파일이 있는 폴더")&&said.Contains("프로그램 폴더: "+half),"trying the move again then leads to the notice about the old backup");
    // 옛 자리에도 아무것도 없으면 되살릴 것이 없다: 새로 설치한 것처럼 시작하고 표시를 다시 쓴다
    string empty=App("bad-info-empty");Directory.CreateDirectory(UserFiles.FolderOf(empty));File.WriteAllText(New(empty,UserFiles.InfoName),"{ 깨짐");
    result=UserFiles.Prepare(empty);
    Check(result.Copied.Count==0&&Info(empty) is {Settled.Count:0},"a broken mark with nothing anywhere: starts as a new install");
   }
   // 읽기만 하는 쪽이 쓰는 자리와 자동 업데이트의 보호·사본
   {
    string app=App("places");Legacy(app);
    Check(UserFiles.Existing(app,"preferences.json")==Old(app,"preferences.json")&&UserFiles.Used(app)==app,"before the move, a read-only start reads the old place");
    UserFiles.Prepare(app);File.WriteAllText(New(app,"preferences.json"),"{\"new\":true}");
    Check(UserFiles.Existing(app,"preferences.json")==New(app,"preferences.json")&&UserFiles.Used(app)==UserFiles.FolderOf(app),"after it, the new place");
    Check(UpdateFiles.IsProtected(UserFiles.FolderName+"/records.json")&&UpdateFiles.IsProtected(UserFiles.FolderName)&&UpdateFiles.IsProtected("records.json")&&UpdateFiles.IsProtected("RECORDS.JSON.BAK"),"the update leaves the new folder and the old originals alone");
    new RecordStore(New(app,"records.json")).Rename("새 자리에서 고침");
    string backup=Path.Combine(app,"update","backup",UpdateBackup.Create(new UpdatePaths(app),new Version(1,2,0),new Version(1,2,1)));
    Check(Hash(Path.Combine(UpdateBackup.Data(backup),"records.json"))==Hash(New(app,"records.json"))&&Hash(Path.Combine(UpdateBackup.Data(backup),"preferences.json"))==Hash(New(app,"preferences.json"))&&Hash(Path.Combine(UpdateBackup.Data(backup),"records.json"))!=Hash(Old(app,"records.json")),"the copy made before an update is taken from the new place");
   }
  }
  finally{UserFiles.Step=null;}
  File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"user-files-validation.txt"),string.Join("\n",notes));
 }
}
