using System.Globalization;
namespace LadderRecords;

// 수동 기록 설정 창.
// 창 너비 560(100% 때의 값. 본문 좌우 여백 20).
// 이 창은 한 판을 더하거나(새 플레이), 직접 저장한 가장 최근의 한 판의 점수를 바꾼다(교체). 기록을 통째로 바꾸던 옛 창의 뜻과 플레이 카운트 칸은 없다.
// 저장은 이 창이 스스로 한다: 저장하는 순간에 프로필·패턴·보여 준 최고 기록·교체할 판이 그대로인지 보고, 달라졌으면 저장하지 않고 알린 뒤 창에 남는다(입력은 그대로 둔다).

/// <summary>'최근 플레이 1건 교체' 상자의 오른쪽: 교체할 판의 점수와 시각(없으면 없다는 글). 오른쪽 끝에 맞춰 그린다</summary>
sealed class ReplaceTargetView:Control
{
 public const string None="교체할 플레이가 없습니다.";
 Play? target;
 public ReplaceTargetView(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);TabStop=false;Margin=Padding.Empty;AccessibleName=None;}
 /// <summary>지금 보이는 글</summary>
 public string Shown=>target is {} play?$"{ScoreText.Points(play.Score)} · {ScoreText.Time(play)}":None;
 public void Set(Play? play){target=play;AccessibleName=Shown;Invalidate();}
 protected override void OnPaint(PaintEventArgs e)
 {
  var g=e.Graphics;g.Clear(Parent?.BackColor??Theme.Field);
  if(target is not {} play){var chain=Ink.Body(12f);Ink.DrawRight(g,new[]{new InkPart(None,chain,Theme.Dim)},Width,Ink.Baseline(Height/2f,chain));return;}
  var score=Ink.Number(14f);Ink.DrawRight(g,new[]{new InkPart($"{play.Score:N0}",score,Theme.Text),new InkPart("점",Ink.Body(12f,true),Theme.Text,1)},Width,Ink.Baseline(Height*.29f,score));
  var time=Ink.Number(12f,false);Ink.DrawRight(g,new[]{new InkPart(ScoreText.Time(play),time,Theme.Dim)},Width,Ink.Baseline(Height*.74f,time));
 }
}

/// <summary>창 아래의 바닥줄: 위에 선 하나, 바탕은 한 단계 어둡게</summary>
sealed class FootBand:Panel
{
 public FootBand(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);Margin=Padding.Empty;}
 protected override void OnPaint(PaintEventArgs e){if(BackColor!=Theme.Side)BackColor=Theme.Side;e.Graphics.Clear(Theme.Side);using var pen=new Pen(Theme.Line);e.Graphics.DrawLine(pen,0,0,Width,0);}
}

/// <summary>수동 기록 설정이 저장을 멈추고 띄우는 안내들</summary>
static class ManualNotice
{
 public const string Title="수동 기록 설정";
 public static void Changed(IWin32Window owner,Play? best,int entered)=>PlayNotice.Show(owner,Title,"입력하는 동안 기록이 바뀌었습니다.","입력한 점수를 저장하려면 수동 기록 설정에서 ‘저장’을 다시 눌러 주세요.",
  new[]{NoticeRow.Best("현재 최고 기록",best),NoticeRow.Entered("입력한 점수",entered)});
 public static void Profile(IWin32Window owner,string started,string now)=>PlayNotice.Show(owner,Title,"입력하는 동안 프로필이 바뀌었습니다.","점수는 저장되지 않았습니다. 수동 기록 설정을 닫고, 기록을 남길 프로필에서 다시 열어 주세요.",
  new[]{NoticeRow.Word("입력을 시작한 프로필",started),NoticeRow.Word("현재 프로필",now)});
 public static void TargetChanged(IWin32Window owner,Play target,int entered)=>PlayNotice.Show(owner,Title,"입력하는 동안 교체할 플레이가 바뀌었습니다.","교체할 플레이를 확인한 뒤 ‘저장’을 다시 눌러 주세요.",
  new[]{NoticeRow.Target("현재 교체할 플레이",target),NoticeRow.Entered("입력한 점수",entered)});
 // 저장하는 순간에 교체할 플레이가 없어졌을 때: 교체 옵션을 끄고 입력은 그대로 둔다. 새 플레이로 남기려면 사용자가 '저장'을 다시 누른다
 public static void NoTarget(IWin32Window owner)=>PlayNotice.Show(owner,Title,"교체할 플레이가 없습니다.","입력한 점수를 새 플레이로 등록하려면 수동 기록 설정에서 ‘저장’을 다시 눌러 주세요.");
 public static void NoPattern(IWin32Window owner)=>PlayNotice.Show(owner,Title,"선택한 패턴을 확인할 수 없습니다.","곡과 버튼·난이도를 다시 선택한 뒤 저장해 주세요.");
 public static void Failed(IWin32Window owner,Exception error)=>PlayNotice.Show(owner,Title,"점수를 저장하지 못했습니다.",SaveTrouble.Retry(error));
 public static void DeleteStopped(IWin32Window owner,string source=PlaySource.VArchive)=>PlayNotice.Show(owner,BulkDeleteDialog.TitleOf(source),"기록이 바뀌어 삭제를 멈췄습니다.",$"‘{BulkDeleteDialog.ButtonOf(source)}’를 다시 눌러 주세요.");
 public static void DeleteFailed(IWin32Window owner,Exception error,string source=PlaySource.VArchive)=>PlayNotice.Show(owner,BulkDeleteDialog.TitleOf(source),"기록을 삭제하지 못했습니다.",SaveTrouble.Retry(error));
}

class RecordDialog:CompactDialog
{
 public const int ContentWidth=518;
 public const string TimeFormat="yyyy-MM-dd HH:mm:ss";
 public const string LateTime="플레이 시각이 현재보다 늦습니다. 날짜와 시각을 확인해 주세요.";
 // 없는 날짜(11월 31일)나 다른 모양의 글에 보이는 안내. 입력값은 그대로 두고 그 칸 아래에 알린다
 public const string BadTime="없는 날짜이거나 형식이 맞지 않습니다. ‘2026-11-22 21:35:08’처럼 입력해 주세요.";
 public const string BadScore="SCORE는 0~1,000,000점, RATE는 0.00~100.00%로 입력해 주세요.";
 public const string PracticeHelp="래더 연습 모드에서 플레이했거나 매치가 무효 처리된 경우에 체크합니다. 이 플레이는 하드·맥스 기록에만 저장되며, 노말 최고 기록에는 반영되지 않습니다.";
 /// <summary>개발용 검사: 지금의 현지 시각을 정해 준다(직접 적은 시각이 미래인지 볼 때 쓴다)</summary>
 internal static Func<DateTime>? Clock{get;set;}
 /// <summary>개발용 검사: 일괄 삭제 확인 창 대신 답한다(프로필 이름과 건수를 받고, 지울지를 돌려준다)</summary>
 internal static Func<string,int,bool>? BulkHook{get;set;}
 static DateTime Now(){var now=Clock?.Invoke()??DateTime.Now;return new DateTime(now.Year,now.Month,now.Day,now.Hour,now.Minute,now.Second,DateTimeKind.Local);}

 readonly Catalog catalog;readonly RecordStore store;readonly string profileId,profileName;
 readonly InputBox search=new(168,"",0,30),time=new(190,"",19,30);
 readonly ThemedCombo songs=Combo(338,"곡"),buttons=Combo(166,"버튼"),patterns=Combo(166,"난이도"),judges=Combo(166,"판정");
 readonly ScoreRateInput input=new(ContentWidth,null,277,true);
 readonly CheckBox useNow=new(){Text="저장 시각 사용",AutoSize=true,Checked=true},replace=new(){Text="최근 플레이 1건을 입력한 점수로 교체",AutoSize=true},practice=new(){Text="래더 연습 모드 · 무효 처리된 매치",AutoSize=true};
 readonly Label wrong,timeWrong,timeFixed,practiceHelp;readonly FieldPanel replaceBox=new(){Size=new(ContentWidth,52),Margin=new(0,10,0,0)};
 readonly ReplaceTargetView targetView=new(){Size=new(250,42)};readonly Panel practiceRow;readonly InkLabel bestLabel;readonly HelpBadge help;
 readonly Button save=new(){Text="저장",Tag="primary"},cancel=new(){Text="취소",DialogResult=DialogResult.Cancel},deleteArchive=new(){Text="V아카이브 삭제"},deleteCollection=new(){Text="컬렉션 삭제"};
 List<InkPart> bestParts=new();bool updating,practiceWanted;
 // 지금의 모습(컨트롤의 Visible 은 창이 화면에 뜬 뒤에만 참이 되므로 따로 기억한다): 교체를 골랐는가, 하드·맥스판정인가, 도움말을 폈는가, 시각 칸의 안내
 bool swapping,upperJudge,helpOpen;string timeIssue="";
 Play? shownBest,shownTarget;   // 이 창이 '현재 최고 기록'과 '교체할 판'으로 보여 준 것. 창이 떠 있는 동안에도 단축키로 새 판이 저장될 수 있어, 저장하는 순간에 다시 견준다

 static ThemedCombo Combo(int width,string name)=>new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=width,Tallness=30,Roomy=true,AccessibleName=name,Margin=Padding.Empty};
 static Label Caption(string text){var label=Boxes.Text(text,"dim",9f,true);label.BackColor=Color.Transparent;return label;}

 /// <summary>저장했는가(새 플레이를 더했거나 한 판을 교체했다)</summary>
 public bool Saved{get;private set;}
 internal ScoreRateInput Input=>input;
 internal TextBox SearchBox=>search.Box;
 internal TextBox TimeBox=>time.Box;
 internal ComboBox Songs=>songs;
 internal ComboBox ButtonChoice=>buttons;
 internal ComboBox Patterns=>patterns;
 internal ComboBox Judges=>judges;
 internal CheckBox UseNow=>useNow;
 internal CheckBox ReplaceOption=>replace;
 internal CheckBox PracticeOption=>practice;
 internal Button SaveButton=>save;
 internal Button DeleteArchiveButton=>deleteArchive;internal Button DeleteCollectionButton=>deleteCollection;
 /// <summary>'현재 최고 기록' 줄에 보이는 글</summary>
 internal string BestShown{get;private set;}="";
 internal string TargetShown=>targetView.Shown;
 /// <summary>플레이 시각 칸 아래에 보이는 안내(없으면 빈 글)</summary>
 internal string TimeProblem=>timeIssue;
 /// <summary>교체를 골라 원래 플레이 시각을 보이고 있으면 그 글</summary>
 internal string? FixedTime=>swapping?timeFixed.Text:null;
 internal bool PracticeShown=>upperJudge;
 /// <summary>연습·무효의 설명이 펼쳐져 있는가</summary>
 internal bool HelpShown=>helpOpen;
 internal HelpBadge HelpMark=>help;
 /// <summary>연습·무효의 설명을 펴거나 접는다(물음표를 누른 것과 같다)</summary>
 internal void ToggleHelp(){helpOpen=!helpOpen&&upperJudge;practiceHelp.Visible=helpOpen;}

 bool Chosen=>songs.SelectedItem is Song&&buttons.SelectedItem is int&&patterns.SelectedItem!=null&&judges.SelectedItem!=null;
 static string JudgeName(object? item)=>((string)item!).Replace("판정","");
 public Selection Chart=>new(((Song?)songs.SelectedItem??throw new Exception("곡을 선택하세요.")).Id,(int)(buttons.SelectedItem??throw new Exception("버튼을 선택하세요.")),patterns.Text,JudgeName(judges.SelectedItem),1);

 public RecordDialog(Catalog catalog,RecordStore store,Selection? initial=null):base(ManualNotice.Title)
 {
  this.catalog=catalog;this.store=store;profileId=store.Active.Id;profileName=store.Active.Name;
  Body.Padding=new(0,16,0,0);
  var main=new FlowLayoutPanel(){AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,FlowDirection=FlowDirection.TopDown,WrapContents=false,Margin=new(20,0,20,14)};
  Body.Controls.Add(main);

  // 곡과 패턴
  main.Controls.Add(new SectionHeader("곡과 패턴"){Width=ContentWidth,Margin=new(0,0,0,6)});
  search.Box.Hint="곡명 또는 아티스트";search.Box.AccessibleName="곡 검색";songs.DropDownWidth=ContentWidth;
  buttons.FormattingEnabled=true;buttons.Format+=(_,e)=>{if(e.ListItem is int number)e.Value=$"{number}B";};
  var first=new Panel(){Size=new(ContentWidth,52),Margin=Padding.Empty};
  Place(first,Caption("곡 검색"),0,0);Place(first,search,0,20);Place(first,Caption("곡"),180,0);Place(first,songs,180,20);
  var second=new Panel(){Size=new(ContentWidth,52),Margin=new(0,8,0,0)};
  Place(second,Caption("버튼"),0,0);Place(second,buttons,0,20);Place(second,Caption("난이도"),176,0);Place(second,patterns,176,20);Place(second,Caption("판정"),352,0);Place(second,judges,352,20);
  main.Controls.Add(first);main.Controls.Add(second);

  // 플레이 기록
  main.Controls.Add(new SectionHeader("플레이 기록"){Width=ContentWidth,Margin=new(0,14,0,6)});
  var bestStrip=new Panel(){Size=new(ContentWidth,32),Margin=new(0,0,0,10),Tag="field"};
  bestParts=BestParts(null,false);bestLabel=new InkLabel(()=>bestParts,Ink.Number(15f),30){Location=new(10,1),AccessibleName="현재 최고 기록"};bestStrip.Controls.Add(bestLabel);
  main.Controls.Add(bestStrip);
  main.Controls.Add(input);
  wrong=Boxes.Text(BadScore,"bad",9.75f,false,ContentWidth);wrong.Margin=new(0,8,0,0);wrong.Visible=false;main.Controls.Add(wrong);

  // 플레이 시각: 기본은 저장하는 순간. 체크를 풀면 지금의 현지 시각을 한 칸에 채워 주고 필요한 곳만 고치게 한다. 교체를 고르면 그 판의 원래 시각을 보이고 잠근다
  var timeRow=new Panel(){Size=new(ContentWidth,30),Margin=new(0,10,0,0)};
  var timeName=Boxes.Text("플레이 시각","dim",9f,true);Place(timeRow,timeName,0,(30-timeName.PreferredHeight)/2);
  Place(timeRow,useNow,72,(30-useNow.PreferredSize.Height)/2);
  time.Box.Font=Theme.Px(AppFonts.Latin,9.75f,FontStyle.Bold);time.Box.AccessibleName="플레이 시각";time.Visible=false;Place(timeRow,time,72+useNow.PreferredSize.Width+10,0);
  timeFixed=new Label(){AutoSize=true,UseMnemonic=false,Margin=Padding.Empty,Font=Theme.Px(AppFonts.Latin,9.75f,FontStyle.Bold),Visible=false};Place(timeRow,timeFixed,72,(30-timeFixed.PreferredHeight)/2);
  main.Controls.Add(timeRow);
  timeWrong=Boxes.Text(LateTime,"bad",9.75f,false,ContentWidth-72);timeWrong.Margin=new(72,4,0,0);timeWrong.Visible=false;main.Controls.Add(timeWrong);

  // 최근 플레이 1건 교체: 체크하기 전부터 교체할 판(직접 저장한 판 가운데 플레이 시각이 가장 늦은 것)의 점수와 시각을 옆에 보인다
  replace.Font=Theme.Px(AppFonts.Ui,9.75f,FontStyle.Bold);
  Place(replaceBox,replace,12,(52-replace.PreferredSize.Height)/2);Place(replaceBox,targetView,ContentWidth-12-250,5);
  main.Controls.Add(replaceBox);

  // 하드·맥스판정에서만: 연습 모드·무효 매치. 물음표를 누르면 아래에 설명이 펼쳐진다
  practiceRow=new Panel(){Size=new(ContentWidth,26),Margin=new(0,10,0,0)};
  practice.Font=Theme.Px(AppFonts.Ui,9.75f,FontStyle.Bold);Place(practiceRow,practice,0,(26-practice.PreferredSize.Height)/2);
  help=new HelpBadge(){Size=new(18,18),Margin=Padding.Empty,AccessibleName="래더 연습 모드 · 무효 처리된 매치 도움말",AccessibleDescription=PracticeHelp};
  help.MakePressable();   // 눌러서 설명을 펴는 물음표다: Tab 으로 올 수 있고 Space·Enter 로 펴고 접는다
  Place(practiceRow,help,practice.PreferredSize.Width+6,4);
  main.Controls.Add(practiceRow);
  practiceHelp=Boxes.Text(PracticeHelp,"dim",9f,false,ContentWidth-22);practiceHelp.Margin=new(22,2,0,0);practiceHelp.Visible=false;main.Controls.Add(practiceHelp);
  help.Click+=(_,_)=>ToggleHelp();

  var footer=Parts.Footer(ContentWidth,save,cancel);footer.Margin=new(0,16,0,0);main.Controls.Add(footer);
  AcceptButton=save;CancelButton=cancel;

  // 바닥줄: 지금 프로필에서 가져온 기록을 출처별로 한 번에 지운다. 'V아카이브 삭제'·'컬렉션 삭제'
  var band=new FootBand(){Size=new(ContentWidth+40,50)};
  var bandName=Boxes.Text("가져온 기록 일괄 삭제","dim",9f,true);bandName.BackColor=Color.Transparent;Place(band,bandName,20,(50-bandName.PreferredHeight)/2);
  foreach(var bulk in new[]{deleteArchive,deleteCollection}){bulk.Font=Theme.Px(AppFonts.Ui,9.75f,FontStyle.Bold);bulk.Size=new(Math.Max(96,TextRenderer.MeasureText(bulk.Text,bulk.Font).Width+22),28);}
  int bulkRight=ContentWidth+20-deleteCollection.Width;Place(band,deleteArchive,bulkRight-8-deleteArchive.Width,11);Place(band,deleteCollection,bulkRight,11);   // 화면과 Tab 의 차례: V아카이브 → 컬렉션
  Body.Controls.Add(band);

  judges.Items.AddRange(new object[]{"노말판정","하드판정","맥스판정"});judges.SelectedItem=(initial?.Judge??"노말")+"판정";
  search.Box.TextChanged+=(_,_)=>FilterSongs();
  songs.SelectedIndexChanged+=(_,_)=>{if(!updating)FillButtons();};buttons.SelectedIndexChanged+=(_,_)=>{if(!updating)FillPatterns();};
  patterns.SelectedIndexChanged+=(_,_)=>{if(!updating)LoadRecord();};judges.SelectedIndexChanged+=(_,_)=>{if(!updating)LoadRecord();};
  input.Changed+=ShowState;
  useNow.CheckedChanged+=(_,_)=>{if(!useNow.Checked)time.Box.Text=Now().ToString(TimeFormat,CultureInfo.InvariantCulture);TimeWrong("");ShowMode();if(!useNow.Checked&&!swapping&&IsHandleCreated)time.Box.Focus();};
  time.Box.TextChanged+=(_,_)=>TimeWrong("");
  replace.CheckedChanged+=(_,_)=>{if(!updating)ShowMode();};
  practice.CheckedChanged+=(_,_)=>{if(!updating&&!replace.Checked)practiceWanted=practice.Checked;};
  save.Click+=(_,_)=>{if(TrySave()){DialogResult=DialogResult.OK;Close();}};
  deleteArchive.Click+=(_,_)=>DeleteImported(PlaySource.VArchive);deleteCollection.Click+=(_,_)=>DeleteImported(PlaySource.Collection);
  FilterSongs(initial);
  Shown+=(_,_)=>{if(Chosen)input.ScoreBox.Focus();else search.Box.Focus();};
  bulkWatch.Tick+=(_,_)=>{if(store.Revision!=seenRevision)SyncBulk();};Shown+=(_,_)=>bulkWatch.Start();FormClosed+=(_,_)=>bulkWatch.Stop();Disposed+=(_,_)=>bulkWatch.Dispose();
 }
 static void Place(Control parent,Control child,int x,int y){parent.Controls.Add(child);child.Location=new(x,y);}

 // ───────────────────────────── 곡과 패턴을 고른다

 void FilterSongs(Selection? initial=null)
 {
  var old=initial?.SongId??(songs.SelectedItem as Song)?.Id;var words=search.Box.Text.Split(' ',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
  var found=catalog.Songs.Where(s=>words.All(w=>(s.Name+" "+s.Artist).Contains(w,StringComparison.OrdinalIgnoreCase))).OrderBy(s=>s.Name).ToArray();
  updating=true;try{songs.Items.Clear();songs.Items.AddRange(found.Cast<object>().ToArray());songs.SelectedItem=found.FirstOrDefault(s=>s.Id==old)??found.FirstOrDefault();}finally{updating=false;}
  FillButtons(initial);
 }
 void FillButtons(Selection? initial=null)
 {
  int? previous=initial?.Button??buttons.SelectedItem as int?;
  updating=true;
  try
  {
   buttons.Items.Clear();
   if(songs.SelectedItem is Song song){buttons.Items.AddRange(song.Charts.Select(c=>c.Button).Distinct().OrderBy(b=>b).Cast<object>().ToArray());buttons.SelectedItem=previous.HasValue&&buttons.Items.Contains(previous.Value)?previous.Value:buttons.Items.Cast<object>().FirstOrDefault();}
  }
  finally{updating=false;}
  FillPatterns(initial);
 }
 void FillPatterns(Selection? initial=null)
 {
  string previous=initial?.Pattern??patterns.Text;
  updating=true;
  try
  {
   patterns.Items.Clear();
   if(songs.SelectedItem is Song song&&buttons.SelectedItem is int button){patterns.Items.AddRange(new[]{"NORMAL","HARD","MAXIMUM","SC"}.Where(p=>song.Charts.Contains(new(button,p))).Cast<object>().ToArray());patterns.SelectedItem=patterns.Items.Contains(previous)?previous:patterns.Items.Cast<object>().FirstOrDefault();}
  }
  finally{updating=false;}
  LoadRecord();
 }

 // ───────────────────────────── 보이는 것

 static List<InkPart> BestParts(Play? best,bool chosen)
 {
  var parts=new List<InkPart>{new("현재 최고 기록",Ink.Body(12f,true),Theme.Dim)};
  if(best==null)parts.Add(new(chosen?"미등록":"곡을 고르면 표시됩니다",Ink.Body(13f,true),chosen?Theme.Text:Theme.Dim,8));
  else{var score=Ink.Score(best,15f,12f,Theme.Score,true);score[0]=score[0] with{Before=8};parts.AddRange(score);}
  return parts;
 }
 /// <summary>고른 패턴·판정의 지금 기록을 다시 읽어 보인다: 최고 기록, 교체할 판. 교체할 판이 없으면 교체 옵션을 끄고 잠근다(적어 둔 점수는 그대로 둔다)</summary>
 void LoadRecord()
 {
  bool chosen=Chosen;shownBest=chosen?store.Best(Chart.Key):null;shownTarget=chosen?store.ReplaceTarget(Chart.Key):null;
  BestShown=shownBest!=null?ScoreText.Full(shownBest):chosen?"미등록":"곡을 고르면 표시됩니다";
  bestParts=BestParts(shownBest,chosen);bestLabel.AccessibleName="현재 최고 기록 "+BestShown;bestLabel.Refit();
  targetView.Set(shownTarget);
  updating=true;try{replace.Enabled=shownTarget!=null;if(shownTarget==null)replace.Checked=false;}finally{updating=false;}
  SyncBulk();
  ShowMode();
 }
 /// <summary>
 /// 바닥줄 삭제 단추 둘의 켜짐을 지금 기록으로 맞춘다. 창이 열린 동안 다른 길(메인 창의 저장 확인 단축키가 같은 점수의 컬렉션 줄을 지우는 등)로 기록이 바뀌면
 /// 이것만 다시 한다: 입력 중인 점수·시각과 교체 대상은 그대로 둔다
 /// </summary>
 void SyncBulk(){seenRevision=store.Revision;deleteArchive.Enabled=store.Active.Id==profileId&&store.Count(PlaySource.VArchive)>0;deleteCollection.Enabled=store.Active.Id==profileId&&store.Count(PlaySource.Collection)>0;}
 readonly System.Windows.Forms.Timer bulkWatch=new(){Interval=300};long seenRevision=-1;
 // 새 플레이와 교체의 차이: 시각 줄(저장 시각·직접 입력 ↔ 원래 시각), 연습·무효 칸(고를 수 있다 ↔ 그 판의 원래 조건을 보이고 잠근다), 상자의 테두리
 void ShowMode()
 {
  bool swap=replace.Checked&&shownTarget!=null,upper=Chosen&&JudgeName(judges.SelectedItem) is "하드" or "맥스";
  swapping=swap;upperJudge=upper;replaceBox.Key(swap);
  useNow.Visible=!swap;time.Visible=!swap&&!useNow.Checked;timeFixed.Visible=swap;
  if(swap){timeFixed.Text=shownTarget!.At!.Value.ToLocalTime().ToString(TimeFormat,CultureInfo.InvariantCulture);TimeWrong("");}
  practiceRow.Visible=upper;if(!upper)helpOpen=false;practiceHelp.Visible=helpOpen;
  updating=true;try{practice.Enabled=!swap;practice.Checked=swap?shownTarget!.Practice:practiceWanted;}finally{updating=false;}
  ShowState();
 }
 void ShowState(){bool valid=input.Value!=null;save.Enabled=Chosen&&valid;wrong.Visible=!valid&&!input.Blank;}

 // ───────────────────────────── 저장

 /// <summary>직접 적은 플레이 시각(현지). 없는 날짜이거나 모양이 다르면 null</summary>
 internal static DateTime? ReadTime(string text)
 {
  if(!DateTime.TryParseExact(text.Trim(),new[]{TimeFormat,"yyyy-M-d H:m:s"},CultureInfo.InvariantCulture,DateTimeStyles.None,out var local))return null;
  return DateTime.SpecifyKind(local,DateTimeKind.Local);
 }
 bool Listed(){if(!Chosen)return false;var chart=Chart;return catalog.Songs.Any(s=>s.Id==chart.SongId&&s.Charts.Contains(new(chart.Button,chart.Pattern)));}
 // 시각 칸 아래의 안내를 보이거나(빈 글이면) 지운다. 보일 때는 그 칸으로 간다
 void TimeWrong(string text){timeIssue=text;if(text.Length>0)timeWrong.Text=text;timeWrong.Visible=text.Length>0;if(text.Length>0&&IsHandleCreated&&!swapping&&!useNow.Checked)time.Box.Focus();}

 /// <summary>
 /// 저장한다. 저장했으면 true(창을 닫는다). 저장하는 순간에 다시 본다: 프로필, 고른 패턴이 곡 목록에 있는가, 직접 적은 시각, 이 창이 보여 준 최고 기록과 교체할 판이 그대로인가.
 /// 달라졌으면 저장하지 않고 알린 뒤 창에 남는다. 적어 둔 점수와 시각은 그대로 둔다. 다시 누르면 그때의 기록으로 다시 본다
 /// </summary>
 internal bool TrySave()
 {
  try
  {
   if(store.Active.Id!=profileId){ManualNotice.Profile(this,profileName,store.Active.Name);return false;}
   if(!Listed()){ManualNotice.NoPattern(this);return false;}
   if(input.Value is not int score)return false;
   var chart=Chart;var best=store.Best(chart.Key);
   if(replace.Checked)
   {
    // 교체: 보여 준 판(순번과 내용)이 그대로일 때만 그 판의 점수를 바꾼다. 점수·시각이 같은 다른 판도 순번으로 가려진다
    var target=store.ReplaceTarget(chart.Key);
    if(target==null){LoadRecord();ManualNotice.NoTarget(this);return false;}
    if(target!=shownTarget){LoadRecord();ManualNotice.TargetChanged(this,target,score);return false;}
    if(best!=shownBest){LoadRecord();ManualNotice.Changed(this,best,score);return false;}
    store.Replace(chart,score);
   }
   else
   {
    // 직접 적은 시각은 저장을 누른 이 순간의 현지 시각과 초 단위로 견준다(같은 초까지 받는다. 과거 쪽의 한계는 없다)
    DateTime? at=null;
    if(!useNow.Checked)
    {
     if(ReadTime(time.Box.Text) is not {} local){TimeWrong(BadTime);return false;}
     if(local>Now()){TimeWrong(LateTime);return false;}
     try{at=local.ToUniversalTime();}catch(ArgumentOutOfRangeException){TimeWrong(BadTime);return false;}
    }
    if(best!=shownBest){LoadRecord();ManualNotice.Changed(this,best,score);return false;}
    store.Add(chart,score,at??DateTime.UtcNow,PlaySource.Manual,chart.Judge is "하드" or "맥스"&&practice.Checked);
   }
   Saved=true;return true;
  }
  catch(Exception error){LoadRecord();ManualNotice.Failed(this,error);return false;}
 }

 /// <summary>바닥줄의 'V아카이브 삭제'·'컬렉션 삭제': 지금 프로필에서 그 출처로 가져온 기록을 모두 지운다(한 작업. 되돌리기 한 번으로 돌아온다). 확인 창이 보여 준 프로필과 건수가 승인하는 순간에도 그대로일 때만 지운다</summary>
 internal void DeleteImported(string source=PlaySource.VArchive)
 {
  int count=store.Count(source);if(store.Active.Id!=profileId||count==0){LoadRecord();return;}
  bool yes;
  if(BulkHook!=null)yes=BulkHook(store.Active.Name,count);
  else{using var dialog=new BulkDeleteDialog(store.Active.Name,count,source);yes=dialog.ShowDialog(this)==DialogResult.Yes;}
  if(!yes)return;
  try
  {
   if(store.Active.Id!=profileId||store.Count(source)!=count)ManualNotice.DeleteStopped(this,source);
   else store.DeleteSource(source);
  }
  catch(Exception error){ManualNotice.DeleteFailed(this,error,source);}
  LoadRecord();
 }
}
