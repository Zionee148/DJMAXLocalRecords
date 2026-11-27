namespace LadderRecords;

// 직전 저장 되돌리기의 확인 창.
// 창 너비 452(100% 때의 값. 본문 좌우 여백 18).
// 되돌리는 것은 작업 단위다. 한 판을 바꾼 작업은 그 판(곡·버튼·난이도·판정, SCORE·RATE, 시각)을, 여러 판을 바꾼 작업은 작업 이름과 되돌릴 동작마다의 건수를 보인다.
// 실제로 되돌리는 것은 RecordStore.Undo 다. 이 창은 묻기만 한다.

/// <summary>되돌리기 확인 창이 보일 것. 그리는 코드와 떼어 두어 자체 검사가 그대로 본다</summary>
sealed record UndoView(string Question,string Profile)
{
 /// <summary>등록·교체가 같은 점수의 컬렉션 줄을 함께 지운 작업을 되돌릴 때 안내 끝에 더하는 한 줄</summary>
 public const string SameCollection="같은 점수의 컬렉션 기록도 다시 나타납니다.";
 /// <summary>한 판을 바꾼 작업: 지워지거나 돌아오거나 노말에 다시 반영될 판. 수정·교체에서는 복원할 점수의 판이다. 여러 판을 바꾼 작업이면 없다</summary>
 public Play? Target{get;init;}
 /// <summary>수정·교체: 지금 들어 있는 점수의 판(되돌리면 Target 의 점수로 돌아간다)</summary>
 public Play? Entered{get;init;}
 /// <summary>Target 의 이름('삭제할 기록', '복원할 기록', '노말판정에 다시 반영할 기록', '복원할 점수')</summary>
 public string TargetName{get;init;}="";
 /// <summary>대상 아래에 적는 안내(노말 반영이 함께 바뀔 때)</summary>
 public IReadOnlyList<string> Notes{get;init;}=Array.Empty<string>();
 /// <summary>여러 판을 바꾼 작업의 이름('V아카이브 가져오기' 등). 한 판을 바꾼 작업이면 없다</summary>
 public string? Work{get;init;}
 /// <summary>여러 판을 바꾼 작업: 되돌리면 일어나는 일과 그 건수(0건인 것은 넣지 않는다)</summary>
 public IReadOnlyList<(string Name,int Count)> Counts{get;init;}=Array.Empty<(string,int)>();

 /// <summary>
 /// 그 작업을 되돌릴 때 보일 것. 무엇이 일어나는지는 작업의 이름이 아니라 **적힌 변경의 모양**으로 가른다(더한 줄은 사라지고, 지운 줄은 돌아오고, 고친 줄은 고치기 전으로 돌아간다).
 /// 질문의 글만 작업의 이름을 본다(수정과 교체는 변경의 모양이 같다)
 /// </summary>
 public static UndoView Of(Operation step,string profile)
 {
  // 등록·교체(수동 기록 설정의 추가도 등록이다)는 같은 점수의 컬렉션 줄을 같은 작업에서 지울 수 있다(RecordStore.DropSameCollection): 그 줄은 빼고 한 판의 작업으로 보고,
  // 그 줄이 돌아온다는 한 줄을 안내 끝에 더한다
  var main=step.Kind is PlayOps.Add or PlayOps.Replace?step.Plays.Where(c=>!(c.Before?.Source==PlaySource.Collection&&c.After==null)).ToList():step.Plays;
  bool dropped=main.Count<step.Plays.Count;
  IReadOnlyList<string> With(params string[] notes)=>dropped?notes.Append(SameCollection).ToArray():notes;
  if(main.Count==1&&step.Kind is PlayOps.Add or PlayOps.Replace or PlayOps.Edit or PlayOps.Delete or PlayOps.Unlink)
  {
   var (before,after)=main[0];string judge=ScoreText.Judge((after??before)!.Key);
   if(before==null)return new("저장한 플레이를 삭제할까요?",profile){Target=after,TargetName="삭제할 기록",
    Notes=With(after!.Normal==NormalLink.Linked?new[]{$"같은 플레이의 노말판정 기록({judge}판정에서 반영)도 함께 삭제됩니다."}:Array.Empty<string>())};
   if(after==null)return new("삭제한 플레이를 복원할까요?",profile){Target=before,TargetName="복원할 기록",
    Notes=before.Normal==NormalLink.Linked?new[]{$"같은 플레이의 노말판정 기록({judge}판정에서 반영)도 함께 복원됩니다."}:Array.Empty<string>()};
   // 노말 반영만 끊은 작업: 점수와 시각은 그대로이고 연결만 달라졌다. 되돌리면 노말에 다시 보인다(원래 판은 그동안에도 남아 있었다)
   if(before.Normal==NormalLink.Linked&&after.Normal==NormalLink.Cancelled&&before.Score==after.Score)
    return new("이 플레이의 노말판정 반영을 복원할까요?",profile){Target=before,TargetName="노말판정에 다시 반영할 기록",Notes=new[]{$"원래 {judge}판정 기록은 유지됩니다."}};
   return new(step.Kind==PlayOps.Replace?"교체 전 점수로 되돌릴까요?":"수정 전 점수로 되돌릴까요?",profile){Target=before,Entered=after,TargetName="복원할 점수",
    Notes=With(before.Normal==NormalLink.Linked?new[]{"같은 플레이의 노말판정 기록도 함께 되돌아갑니다."}:Array.Empty<string>())};
  }
  int added=step.Plays.Count(c=>c.Before==null),removed=step.Plays.Count(c=>c.After==null),changed=step.Plays.Count-added-removed;
  // 컬렉션 가져오기가 노말 반영만 끊은 하드·맥스 판(점수 그대로): '이전 값으로 복원할 기록'이 아니라 '복원할 기록'으로 센다
  int unlinked=step.Kind==PlayOps.ImportCollection?step.Plays.Count(c=>c.Before is {Normal:NormalLink.Linked} b&&c.After is {Normal:NormalLink.Cancelled} a&&a.Score==b.Score):0;
  var (question,counts)=step.Kind switch
  {
   PlayOps.ImportVArchive=>("이번 가져오기를 되돌릴까요?",new[]{("삭제할 새 기록",added),("이전 값으로 복원할 기록",changed),("복원할 기록",removed)}),
   PlayOps.DeleteVArchive=>("일괄 삭제한 기록을 복원할까요?",new[]{("복원할 V아카이브 기록",removed),("삭제할 기록",added),("이전 값으로 복원할 기록",changed)}),
   PlayOps.ImportCollection=>("이번 가져오기를 되돌릴까요?",new[]{("삭제할 새 기록",added),("이전 값으로 복원할 기록",changed-unlinked),("복원할 기록",removed+unlinked)}),
   PlayOps.DeleteCollection=>("일괄 삭제한 기록을 복원할까요?",new[]{("복원할 컬렉션 기록",removed),("삭제할 기록",added),("이전 값으로 복원할 기록",changed)}),
   // 지금은 만들지 않는 작업(예전 개발 빌드가 남긴 판정 단위 삭제 등): 일어나는 일만 그대로 센다
   _=>("직전 작업을 되돌릴까요?",new[]{("삭제할 기록",added),("복원할 기록",removed),("이전 값으로 복원할 기록",changed)}),
  };
  return new(question,profile){Work=PlayOps.Name(step.Kind),Counts=counts.Where(c=>c.Item2>0).ToList()};
 }
}

class UndoDialog:ChoiceDialog
{
 public const int ContentWidth=414;
 const int Side=14;
 // 주 화면의 상태 줄에 적히는 글
 public const string Nothing="이 프로필에는 되돌릴 저장이 없습니다.",Changed="확인하는 동안 직전 저장이 바뀌었습니다. ‘직전 저장 되돌리기’를 다시 눌러 확인해 주세요.";
 /// <summary>개발용 검사가 창 대신 답하는 자리(보일 것을 받고, 되돌릴지를 돌려준다)</summary>
 internal static Func<UndoView,bool>? Hook{get;set;}

 /// <summary>
 /// 직전 작업을 보이고 되돌릴지 묻는다. 되돌렸으면 true. 되돌릴 작업이 없거나, 확인하는 동안 직전 작업이 달라졌으면 알린다(예외. 주 화면이 상태 줄에 적는다).
 /// 승인하는 순간의 직전 작업이 창이 보여 준 바로 그 작업일 때만 되돌린다: 창이 떠 있는 동안에도 단축키로 새 판이 저장될 수 있다
 /// </summary>
 public static bool Ask(IWin32Window owner,Catalog catalog,RecordStore store)
 {
  var step=store.Active.Undo.LastOrDefault()??throw new Exception(Nothing);
  string profileId=store.Active.Id;var view=UndoView.Of(step,store.Active.Name);bool yes;
  if(Hook!=null)yes=Hook(view);
  else{using var dialog=new UndoDialog(view,catalog);yes=dialog.ShowDialog(owner)==DialogResult.Yes;}
  if(!yes)return false;
  if(store.Active.Id!=profileId||!ReferenceEquals(store.Active.Undo.LastOrDefault(),step))throw new Exception(Changed);
  store.Undo();return true;
 }

 /// <summary>
 /// 창에 적는 곡 이름. 곡 목록에 이름이 같은 곡이 또 있으면 아티스트를 붙인다('Alone (Marshmello)'. 오버레이·가져오기 목록과 같은 표기):
 /// 이 창은 곡 이름만으로 어느 기록을 되돌리는지 알려야 한다
 /// </summary>
 internal static string SongTitle(Catalog catalog,string key)
 {
  string id=key.Split('|')[0];var song=catalog.Songs.FirstOrDefault(s=>s.Id==id);
  return song==null?ScoreText.MissingSong(id):OverlayText.Title(catalog,song);
 }
 public UndoDialog(UndoView view,Catalog catalog):base("직전 저장 되돌리기")
 {
  Body.Padding=new(18,16,18,16);
  var question=Parts.TitleLine(view.Question,ContentWidth,"프로필 · "+view.Profile);question.Margin=new(0,0,0,10);Body.Controls.Add(question);
  if(view.Target is {} target)
  {
   var parts=target.Key.Split('|');
   Body.Controls.Add(new SongLine(SongTitle(catalog,target.Key),ContentWidth,20f){Margin=Padding.Empty});
   if(parts.Length==4){var chips=Boxes.Chips(parts[1]+"B",parts[2],parts[3]+"판정");chips.Margin=new(0,6,0,10);Body.Controls.Add(chips);}
   Body.Controls.Add(One(view,target));
  }
  else
  {
   var name=new List<InkPart>{new(view.Work!,Ink.Head(20f),Theme.Text)};Body.Controls.Add(new InkLabel(()=>name,Ink.Head(20f),28){Margin=new(0,0,0,10),AccessibleName=view.Work});
   Body.Controls.Add(Many(view));
  }
  foreach(string line in view.Notes){var note=Boxes.Text(line,null,9.75f,false,ContentWidth);note.Margin=new(0,10,0,0);Body.Controls.Add(note);}
  var undo=new Button(){Text="되돌리기",DialogResult=DialogResult.Yes};var cancel=new Button(){Text="취소",DialogResult=DialogResult.No,Tag="primary"};
  var footer=Parts.Footer(ContentWidth,undo,cancel);footer.Margin=new(0,16,0,0);Body.Controls.Add(footer);
  AcceptButton=cancel;CancelButton=cancel;Shown+=(_,_)=>ActiveControl=cancel;
 }

 // 점수 한 덩이: 이름, 큰 SCORE 와 '점', 그 아래의 RATE. RATE 만 남은 기록은 실제로 남은 RATE 만 크게 적는다(환산한 SCORE 를 적지 않는다)
 static Panel Block(string name,Play play)
 {
  var block=new Panel(){Margin=Padding.Empty};bool exact=PlaySource.Exact(play.Source);
  var label=Boxes.Text(name,"dim",9f);label.BackColor=Color.Transparent;block.Controls.Add(label);label.Location=Point.Empty;
  var parts=exact?new List<InkPart>{new($"{play.Score:N0}",Ink.Number(24f),Theme.Score),new("점",Ink.Body(12f,true),Theme.Score,3)}:new List<InkPart>{new(ScoreText.Rate(play.Score),Ink.Number(24f),Theme.Score)};
  var big=new InkLabel(()=>parts,Ink.Number(24f),30){AccessibleName=name+" "+ScoreText.Kept(play)};block.Controls.Add(big);big.Location=new(0,20);
  // InkLabel 의 너비는 이미 창의 크기 단계가 곱해진 값이다. 자리를 잡는 데에는 100% 때의 값으로 되돌려 쓴다
  int wide=Math.Max(label.PreferredWidth,(int)Math.Ceiling(big.Width/Theme.Scale));
  if(exact){var rate=PlayParts.Plain(ScoreText.Rate(play.Score),true);rate.Tag="dim";rate.Font=Theme.Px(AppFonts.Latin,9.75f,FontStyle.Bold);block.Controls.Add(rate);rate.Location=new(0,54);wide=Math.Max(wide,rate.PreferredWidth);}
  block.Size=new(wide+2,exact?74:52);return block;
 }
 // 이름은 왼쪽, 값은 오른쪽 끝에 맞춘 한 줄
 static void Line(Control box,int top,int height,string name,Label value)
 {
  var label=Boxes.Text(name,"dim",9f);label.BackColor=Color.Transparent;box.Controls.Add(label);label.Location=new(Side,top+(height-label.PreferredHeight)/2);
  box.Controls.Add(value);value.Location=new(ContentWidth-Side-value.PreferredWidth,top+(height-value.PreferredHeight)/2);
 }
 static Control One(UndoView view,Play target)
 {
  var box=new FieldPanel(){Width=ContentWidth};
  // 수정·교체는 두 덩이를 양 끝에 둔다: 왼쪽에 지금 들어 있는 점수, 오른쪽에 복원할 점수
  var restored=Block(view.TargetName,target);box.Controls.Add(restored);int tall=restored.Height;
  if(view.Entered is {} entered){var now=Block("현재 입력된 점수",entered);box.Controls.Add(now);now.Location=new(Side,12);restored.Location=new(ContentWidth-Side-restored.Width,12);tall=Math.Max(tall,now.Height);}
  else restored.Location=new(Side,12);
  int y=12+tall+6;
  box.Controls.Add(new Panel(){Location=new(Side,y),Size=new(ContentWidth-Side*2,1),Tag="line"});y+=1;
  var time=PlayParts.Plain(target.At is {} at?at.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"):"일시 미상",target.At!=null);
  if(target.At!=null)time.Font=Theme.Px(AppFonts.Latin,9.75f,FontStyle.Bold);
  Line(box,y,38,ScoreText.TimeName(target),time);y+=38;
  // 일시 미상인 기록은 어디서 온 것인지를 함께 적는다
  if(target.At==null){Line(box,y-6,30,"출처",PlayParts.Plain(PlaySource.Label(target.Source)));y+=26;}
  box.Height=y;return box;
 }
 // 여러 판을 바꾼 작업: 되돌리면 일어나는 일마다 한 줄(이름과 건수)
 static Control Many(UndoView view)=>PlayParts.Pairs(ContentWidth,44,view.Counts.Select(count=>
 {
  var parts=new List<InkPart>{new($"{count.Count:N0}",Ink.Number(18f),Theme.Text),new("건",Ink.Body(12f),Theme.Dim,5)};
  return (count.Name,(Control)new InkLabel(()=>parts,Ink.Number(18f),26){AccessibleName=$"{count.Name} {count.Count:N0}건"});
 }).ToArray(),lines:true,strong:true);
}
