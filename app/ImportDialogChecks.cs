namespace LadderRecords;

// V아카이브 가져오기 창을 본다. V아카이브에는 묻지 않는다: 창이 응답을 받는 자리(ImportDialog.Fetcher)에 합성한 응답을 준다. 알림은 창을 띄우지 않고 받는다(PlayNotice.Hook).
// 보는 것: 닉네임 → 불러오기 → 건수와 접힌 목록 → 가져오기. 바뀔 것이 없을 때, 불러오지 못했을 때와 다시 입력, 가져오기를 누른 순간의 재확인(대상·추가와 갱신의 구분·전후 값),
// 다른 곡의 저장만 있었을 때는 다시 묻지 않는 것, 조회 직후와 반영 직전의 프로필 변경, 저장 실패. 합성 기록만 쓰고 실행 파일 옆의 test-data 아래에만 쓴다. 결과는 import-dialog-validation.txt
[System.Runtime.Versioning.SupportedOSPlatform("windows10.0.19041")]
static class ImportDialogChecks
{
 public static void Run(Catalog catalog)
 {
  var notes=new List<string>();void Check(bool value,string text){if(!value)throw new Exception(text);notes.Add("PASS: "+text);}
  string folder=Path.Combine(AppContext.BaseDirectory,"test-data","import-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);string path=Path.Combine(folder,"records.json");var store=new RecordStore(path);
  var songs=catalog.Songs.Where(s=>s.Charts.Count>0).Take(5).ToList();Selection Normal(int index)=>new(songs[index].Id,songs[index].Charts[0].Button,songs[index].Charts[0].Pattern,"노말",0);
  var a=Normal(0);var b=Normal(1);var c=Normal(2);var d=Normal(3);var e=Normal(4);
  string Pattern(Selection chart)=>$"{chart.Button}B · {chart.Pattern}";string Title(int index)=>OverlayText.Title(catalog,songs[index]);
  var told=new List<(string Title,string Head,string Text)>();
  Dictionary<Selection,decimal> answer=new();Exception? failure=null;string? askedName=null;Action? during=null;
  ImportDialog Open(){var dialog=new ImportDialog(catalog,store);dialog.Show();Application.DoEvents();return dialog;}
  void Ask(ImportDialog dialog,string name){dialog.NicknameBox.Text=name;dialog.FetchButton.PerformClick();dialog.Pending.GetAwaiter().GetResult();Application.DoEvents();}
  try
  {
   PlayNotice.Hook=(title,head,text)=>told.Add((title,head,text));
   ImportDialog.Fetcher=(_,name)=>{askedName=name;during?.Invoke();return failure!=null?Task.FromException<Dictionary<Selection,decimal>>(failure):Task.FromResult(new Dictionary<Selection,decimal>(answer));};
   // 1. 닉네임을 받는다: 비었거나 공백뿐이면 불러올 수 없다
   using(var dialog=Open())
   {
    Check(dialog.Text=="V아카이브 가져오기 · 기본"&&dialog.Stage=="ask"&&!dialog.FetchButton.Enabled&&dialog.AcceptButton==dialog.FetchButton,"the window is named with the current profile and starts by asking for the nickname; an empty box cannot fetch");
    dialog.NicknameBox.Text="   ";Check(!dialog.FetchButton.Enabled,"blanks alone cannot fetch either");
    // 2. 불러온다 → 건수와 접힌 목록. 아직 아무것도 저장하지 않는다
    answer=new(){[a]=99.54m,[b]=98.76m,[c]=97.5m};Ask(dialog,"  RESPECT ");
    Check(askedName=="RESPECT"&&dialog.Stage=="result"&&dialog.Counts=="추가 3건 | 갱신 0건"&&dialog.Warning==""&&dialog.AcceptButton==dialog.ApplyButton&&store.Active.Plays.Count==0&&store.Active.Undo.Count==0,"fetching (with the nickname trimmed) shows how many records would be added and updated, and saves nothing yet");
    Check(!dialog.Fold.Open&&!dialog.Table.Visible&&dialog.Table.Rows.Count==3&&dialog.Table.Rows[0]==new ImportRow(Title(0),Pattern(a),true,995400)&&dialog.Table.Rows.All(row=>row.Added),"the list of scores starts folded and holds one line for each record: song, pattern, added or updated, the converted score");
    dialog.Fold.Flip();Application.DoEvents();Check(dialog.Fold.Open&&dialog.Table.Visible,"the list opens");dialog.Fold.Flip();Check(!dialog.Table.Visible,"and folds again");
    // 3. 가져온다
    Check(dialog.Apply()&&dialog.Imported==3&&store.Count(PlaySource.VArchive)==3&&store.Active.Undo.Count==1&&store.Active.Undo[^1].Kind==PlayOps.ImportVArchive&&store.Best(a.Key) is {Score:995400,Source:PlaySource.VArchive,At:null}&&told.Count==0,"importing saves the shown records as one step");
   }
   // 4. 다시 가져온다: 그 출처의 줄보다 높을 때만 갱신, 같은 값은 건너뛴다. 그사이 다른 곡의 저장이 있었어도 바뀔 것이 같으면 다시 묻지 않는다
   using(var dialog=Open())
   {
    answer=new(){[a]=99.9m,[b]=98.76m,[d]=90m};Ask(dialog,"RESPECT");
    Check(dialog.Counts=="추가 1건 | 갱신 1건"&&dialog.Table.Rows.SequenceEqual(new[]{new ImportRow(Title(0),Pattern(a),false,999000),new ImportRow(Title(3),Pattern(d),true,900000)}),"a second import lists only what changes: a higher value as an update, a new chart as an addition; an equal value is left out");
    store.Add(c,991000,new DateTime(2026,11,23,1,0,0,DateTimeKind.Utc));store.Add(a,999900,new DateTime(2026,11,23,1,1,0,DateTimeKind.Utc));
    Check(dialog.Apply()&&dialog.Imported==2&&dialog.Warning==""&&store.History(a.Key).Single(p=>p.Source==PlaySource.VArchive).Score==999000&&store.Best(a.Key)!.Score==999900&&store.Best(d.Key)!.Score==900000,
     "plays saved meanwhile that do not change what the import does (another chart, or an own play on the same chart) do not ask again");
   }
   // 5. 가져오기를 누른 순간에 바뀔 것이 달라졌다: 이번에는 저장하지 않고 건수와 목록을 고쳐 적는다. 건수가 같아도 전후 값이 다르면 다시 확인한다
   using(var dialog=Open())
   {
    answer=new(){[a]=99.95m,[c]=99m};Ask(dialog,"RESPECT");
    Check(dialog.Counts=="추가 0건 | 갱신 2건","two updates are shown");
    store.Import(new Dictionary<Selection,decimal>{[c]=99m});int steps=store.Active.Undo.Count;string before=System.Text.Json.JsonSerializer.Serialize(store.Active.Plays);
    Check(!dialog.Apply()&&dialog.Imported==null&&dialog.Stage=="result"&&dialog.Warning=="가져올 기록이 달라졌습니다. 변경 내용을 확인한 뒤 ‘가져오기’를 다시 눌러 주세요."&&dialog.Counts=="추가 0건 | 갱신 1건"&&dialog.Table.Rows.Single()==new ImportRow(Title(0),Pattern(a),false,999500)
     &&store.Active.Undo.Count==steps&&System.Text.Json.JsonSerializer.Serialize(store.Active.Plays)==before,"when what would change is different at the moment of importing, nothing is saved: the counts and the list are rewritten and it asks again");
    store.Import(new Dictionary<Selection,decimal>{[a]=99.93m});steps=store.Active.Undo.Count;
    Check(!dialog.Apply()&&dialog.Counts=="추가 0건 | 갱신 1건"&&dialog.Warning.Length>0&&store.Active.Undo.Count==steps&&store.History(a.Key).Single(p=>p.Source==PlaySource.VArchive).Score==999300,"the same count with a different value before the change still asks again");
    Check(dialog.Apply()&&dialog.Imported==1&&store.History(a.Key).Single(p=>p.Source==PlaySource.VArchive).Score==999500,"pressing import again saves what is shown now");
   }
   // 6. 그사이 바뀔 것이 없어졌다: 저장하지 않고 '변경할 기록이 없습니다.'를 보인다. 되돌리기 대상은 그대로다
   using(var dialog=Open())
   {
    answer=new(){[e]=95m};Ask(dialog,"RESPECT");store.Import(new Dictionary<Selection,decimal>{[e]=95m});int steps=store.Active.Undo.Count;var last=store.Active.Undo[^1];
    Check(!dialog.Apply()&&dialog.Stage=="empty"&&dialog.Message=="변경할 기록이 없습니다."&&dialog.Imported==null&&store.Active.Undo.Count==steps&&ReferenceEquals(store.Active.Undo[^1],last),"when nothing is left to change, nothing is saved and the step to undo stays the same");
   }
   // 7. 불러왔는데 바뀔 것이 없다. 불러오지 못했다 → 닉네임 다시 입력
   using(var dialog=Open())
   {
    Ask(dialog,"RESPECT");
    Check(dialog.Stage=="empty"&&dialog.Message=="변경할 기록이 없습니다."&&!dialog.RetryButton.Visible,"a fetch that changes nothing says so");
   }
   using(var dialog=Open())
   {
    failure=new HttpRequestException("Response status code does not indicate success: 404 (Not Found).",null,System.Net.HttpStatusCode.NotFound);Ask(dialog,"없는닉네임");
    Check(dialog.Stage=="failed"&&dialog.Message=="기록을 불러오지 못했습니다.\n닉네임과 네트워크 연결을 확인해 주세요."&&dialog.RetryButton.Visible&&told.Count==0,"a failed fetch tells to check the nickname and the network and offers to type the nickname again");
    // 까닭은 받은 영문 문장이 아니라 짧은 한 줄이다(제보할 때 쓸 번호나 종류)
    Check(dialog.Detail=="서버 응답 코드 404"&&ImportDialog.FetchDetail(new HttpRequestException(HttpRequestError.NameResolutionError,"No such host is known."))=="서버에 연결하지 못함 (NameResolutionError)"
     &&ImportDialog.FetchDetail(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 15 seconds elapsing."))=="응답 시간 초과"&&ImportDialog.FetchDetail(new System.Text.Json.JsonException("'<' is an invalid start of a value."))=="응답을 읽지 못함"
     &&ImportDialog.FetchDetail(new InvalidDataException("8B 기록을 받지 못함"))=="8B 기록을 받지 못함"&&ImportDialog.FetchDetail(new InvalidOperationException("Something in English."))=="오류 종류 InvalidOperationException",
     "under the advice, the reason is one short line (the server's status code, no connection, timeout, an unreadable answer), never the raw English sentence");
    // 서버가 응답을 마치기 전에 연결을 닫은 경우: 머리글을 받기 전이든 자료를 받는 도중이든 같은 한 줄이다(연결은 됐다)
    Check(ImportDialog.FetchDetail(new HttpRequestException(HttpRequestError.ResponseEnded,"The response ended prematurely."))=="서버 응답을 끝까지 받지 못함 (ResponseEnded)"
     &&ImportDialog.FetchDetail(new HttpIOException(HttpRequestError.ResponseEnded,"The response ended prematurely, with at least 120 additional bytes expected."))=="서버 응답을 끝까지 받지 못함 (ResponseEnded)"
     &&ImportDialog.FetchDetail(new HttpRequestException(HttpRequestError.ConnectionError,"No connection could be made."))=="서버에 연결하지 못함 (ConnectionError)",
     "a response that ends before it is complete says so in both forms it can arrive in; a failed connection still says it could not connect");
    dialog.RetryButton.PerformClick();Application.DoEvents();
    Check(dialog.Stage=="ask"&&dialog.NicknameBox.Text=="없는닉네임"&&dialog.FetchButton.Enabled,"typing again goes back to the nickname with what was typed");
    failure=null;answer=new(){[e]=96m};Ask(dialog,"RESPECT");
    Check(dialog.Stage=="result"&&dialog.Counts=="추가 0건 | 갱신 1건","and the next fetch works");
   }
   // 8. 조회 직후와 반영 직전에 프로필이 달라져 있다: 조회한 것을 저장하지 않고 두 프로필을 보인 뒤 닫는다
   string home=store.Active.Id;int homeRows=store.Active.Plays.Count;
   const string profileText="가져오기를 시작한 프로필: 기본\n현재 프로필: 둘째\n불러온 기록은 저장되지 않았습니다.\n메인 창에서 기록을 받을 프로필을 선택한 뒤 ‘V아카이브 가져오기’를 다시 눌러 주세요.";
   using(var dialog=Open())
   {
    during=()=>store.Create("둘째",false);answer=new(){[e]=97m};Ask(dialog,"RESPECT");during=null;
    Check(told.Count==1&&told[0]==("V아카이브 가져오기","기록을 가져오는 동안 프로필이 바뀌었습니다.",profileText)&&!dialog.Visible&&dialog.Imported==null&&store.Active.Plays.Count==0&&store.Data.Profiles.Single(p=>p.Id==home).Plays.Count==homeRows,
     "when the profile changed while fetching, nothing is saved to either profile; both names are shown and the window closes");
   }
   told.Clear();store.Select(home);
   using(var dialog=Open())
   {
    answer=new(){[e]=97m};Ask(dialog,"RESPECT");store.Select(store.Data.Profiles.Single(p=>p.Name=="둘째").Id);
    Check(dialog.Stage=="result"&&!dialog.Apply()&&told.Count==1&&told[0].Text==profileText&&!dialog.Visible&&store.Active.Plays.Count==0&&store.Data.Profiles.Single(p=>p.Id==home).Plays.Count==homeRows,"the same when the profile changed between the result and the import");
   }
   told.Clear();store.Select(home);
   // 9. 기록 파일에 쓰지 못했다: 알리고 닫는다. 기록과 되돌리기 대상은 그대로다
   using(var dialog=Open())
   {
    answer=new(){[e]=97m};Ask(dialog,"RESPECT");int steps=store.Active.Undo.Count;
    using(new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.None))Check(!dialog.Apply()&&told.Count==1&&told[0].Title=="V아카이브 가져오기"&&told[0].Head=="불러온 기록을 저장하지 못했습니다."&&told[0].Text.EndsWith("‘V아카이브 가져오기’를 다시 눌러 주세요.")&&dialog.Imported==null,"a save that cannot be written says so with what to do next");
    Check(store.Active.Undo.Count==steps&&store.Active.Plays.Count==homeRows&&store.History(e.Key).Single().Score==950000,"and leaves the records and the step to undo as they were");
   }
   // 10. 서른다섯 줄의 목록: 끝까지 내린 뒤 접었다 펴도 표가 같은 높이로 보인다(첫 줄의 좌표로 높이를 셈하면 굴린 만큼 줄어 0 이 된다)
   using(var dialog=Open())
   {
    answer=catalog.Songs.Where(s=>s.Charts.Count>0).Skip(5).Take(35).ToDictionary(s=>new Selection(s.Id,s.Charts[0].Button,s.Charts[0].Pattern,"노말",0),_=>91.23m);
    Ask(dialog,"RESPECT");dialog.Fold.Flip();Application.DoEvents();int tall=dialog.Table.Height,row=dialog.Table.GetItemRect(0).Height;
    dialog.Table.EnsureVisible(34);Application.DoEvents();int top=dialog.Table.GetItemRect(0).Top;
    dialog.Fold.Flip();dialog.Fold.Flip();Application.DoEvents();
    Check(dialog.Table.Rows.Count==35&&tall>row*ImportDialog.MaxRows&&tall<row*(ImportDialog.MaxRows+1)&&top<0&&dialog.Table.Visible&&dialog.Table.Height==tall&&dialog.Table.GetItemRect(34).Bottom<=dialog.Table.ClientSize.Height,
     $"a long list shows five rows and scrolls inside the table; folding and opening it again after scrolling to the end keeps the same height (height {tall}, row {row}, first row at {top}, now {dialog.Table.Height})");
   }
   // 저장하지 못한 까닭마다의 글: 공간 부족, 다른 프로그램이 사용 중, 프로그램이 스스로 알리는 까닭, 쓸 권한 없음
   Check(ImportNotice.Cause(new IOException("x",unchecked((int)0x80070070)))==("기록을 저장할 공간이 부족합니다.","공간을 확보한 뒤 ‘V아카이브 가져오기’를 다시 눌러 주세요.")
    &&ImportNotice.Cause(new IOException("x",unchecked((int)0x80070020))).Reason=="기록 파일을 다른 프로그램이 사용하고 있습니다."&&ImportNotice.Cause(new RecordFileChangedException("밖에서 바뀜")).Reason=="밖에서 바뀜"&&ImportNotice.Cause(new RecordFileChangedException("밖에서 바뀜")).Next==null&&SaveTrouble.Retry(new RecordFileChangedException("밖에서 바뀜"))=="밖에서 바뀜"
    &&ImportNotice.Cause(new UnauthorizedAccessException()).Reason=="기록 파일에 쓸 수 없습니다.","the reason and the next step fit the cause: no space left, the file in use, a refusal the program explains itself");
  }
  finally{PlayNotice.Hook=null;ImportDialog.Fetcher=null;}
  File.WriteAllLines(Path.Combine(AppContext.BaseDirectory,"import-dialog-validation.txt"),notes);
 }
}
