namespace LadderRecords;

// 공통 안내창과 켤 때 만나는 안내의 글을 본다.
// 보는 것: 켤 때의 안내 셋의 글, 단추가 하나일 때와 여럿일 때의 기본 선택(연 직후의 Enter, Esc·닫기), 펼치는 줄을 펴고 접을 때의 창 높이,
// 긴 경로가 줄을 바꿔 다 보이는가, 창의 모든 글이 "글자를 가진 컨트롤"에 있는가(시험 도구 tools\update_test.py 가 자식 창의 글자로 창을 읽는다). 결과는 guide-validation.txt
[System.Runtime.Versioning.SupportedOSPlatform("windows10.0.19041")]
static class GuideDialogChecks
{
 static IEnumerable<Control> All(Control top){foreach(Control child in top.Controls){yield return child;foreach(var inner in All(child))yield return inner;}}
 public static void Run()
 {
  var notes=new List<string>();void Check(bool value,string text){if(!value)throw new Exception(text);notes.Add("PASS: "+text);}
  // 1. 켤 때의 안내의 글
  var running=StartGuides.AlreadyRunning;
  Check(running is {Title:"디맥 개인 기록 저장 오버레이",Headline:"프로그램이 이미 실행 중입니다."}&&running.Parts.SequenceEqual(new GuidePart[]{new GuideText("잠시 후 작업 표시줄에서 프로그램 창을 확인해 주세요.")})&&running.ButtonTexts.SequenceEqual(new[]{"확인"}),"a second start that cannot find the first window says the program is already running and where to look");
  var port=StartGuides.Error(new System.Net.Sockets.SocketException(10048));
  Check(port is {Title:"실행 오류",Headline:"프로그램을 시작할 수 없습니다."}&&port.Parts.SequenceEqual(new GuidePart[]{new GuideText("다른 프로그램이 OBS 연결용 포트(18765)를 사용하고 있습니다."),new GuideText("이전 버전의 기록 프로그램이 켜져 있다면 종료한 뒤 다시 실행해 주세요.","dim")}),"a taken overlay port gets its own words and next step");
  var other=StartGuides.Error(new FileNotFoundException("곡 목록 파일(data/catalog.json)을 찾을 수 없습니다."));
  Check(other.Headline=="프로그램을 시작할 수 없습니다."&&other.Parts.SequenceEqual(new GuidePart[]{new GuideText("곡 목록 파일(data/catalog.json)을 찾을 수 없습니다."),new GuideText("다시 실행해도 같은 오류가 뜨면 위 내용을 함께 제보해 주세요.","dim")}),"any other start error shows the message it carries and asks to report it");
  string missing=RecordStore.Missing(Path.Combine("C:\\Games\\DJMAXLocalRecords",UserFiles.FolderName,"records.json"),new(){".bak",".tmp"});
  // 기록 파일 때문에 시작하지 못할 때: 글은 받은 그대로이고, '이름: 경로'인 줄만 경로 상자로 놓인다. 제보 안내는 붙지 않는다
  var records=StartGuides.Error(new UserFilesException(missing));string plain=records.Plain();
  Check(records.Headline=="프로그램을 시작할 수 없습니다."&&records.Parts[0] is GuideText{Tone:""} first&&first.Text.StartsWith("‘UserData’ 폴더에 기록 파일(records.json)이 없고 records.json.bak, records.json.tmp만 남아 있습니다.")
   &&records.Parts.OfType<GuidePaths>().SelectMany(block=>block.Items).Select(item=>item.Name).SequenceEqual(new[]{"프로그램 폴더","UserData 폴더"})
   &&records.Parts.OfType<GuidePaths>().Last().Items[1].Path==Path.Combine("C:\\Games\\DJMAXLocalRecords",UserFiles.FolderName)
   &&records.Parts.OfType<GuideText>().Count(text=>text.Text.StartsWith("·"))==3&&!plain.Contains("제보"),
   "a record-file error puts its folders in path boxes and keeps each bullet as its own paragraph; it adds no report line");
  Check(missing.Split('\n').Where(line=>line.Length>0).All(line=>plain.Contains(line)||line.Contains(": ")&&plain.Contains(line[..line.IndexOf(": ",StringComparison.Ordinal)].TrimStart('·',' '))&&plain.Contains(line[(line.IndexOf(": ",StringComparison.Ordinal)+2)..])),
   "nothing of the words is lost: every line is there, as a paragraph or as a name with its path");
  var newer=StartGuides.Error(new UserFilesException(RecordStore.Newer(Path.Combine("C:\\Games\\DJMAXLocalRecords",UserFiles.FolderName,"records.json"),3)));
  Check(newer.Parts.OfType<GuideLink>().Single() is {Name:"자세한 안내"} link&&link.Url==UpdateBackup.GuidePage&&newer.Parts.OfType<GuidePaths>().Any()&&!newer.Plain().Contains("제보"),"a line that names an address becomes a name with the address under it");
  Check(StartGuides.Lines("첫 줄\n둘째 줄\n\n셋째 문단\n· 가\n· 나: 값\n폴더: D:\\a\\b\n다른 폴더: \\\\server\\share").SequenceEqual(new GuidePart[]{new GuideText("첫 줄\n둘째 줄"),new GuideText("셋째 문단"),new GuideText("· 가"),new GuideText("· 나: 값")})==false
   &&StartGuides.Lines("첫 줄\n둘째 줄\n\n셋째 문단\n· 가\n· 나: 값").SequenceEqual(new GuidePart[]{new GuideText("첫 줄\n둘째 줄"),new GuideText("셋째 문단"),new GuideText("· 가"),new GuideText("· 나: 값")})
   &&StartGuides.Lines("폴더: D:\\a\\b\n다른 폴더: \\\\server\\share").Single() is GuidePaths{Items.Length:2},"lines are grouped into paragraphs by blank lines and bullets; consecutive path lines share one box (drive and network paths)");
  var notice=StartGuides.Records(UserFiles.ChangedText);
  Check(notice is {Title:"기록 안내",Headline:"이전 버전에서 변경한 기록이 있습니다."}&&notice.Parts.SequenceEqual(new GuidePart[]{new GuideText("현재 버전은 ‘UserData’ 폴더의 기록을 사용합니다. 이전 버전의 기록 파일도 보관되어 있습니다.")}),"the record notice puts its first sentence on the first line and the rest below");
  // 2. 단추 하나: '확인'이 기본이고 Esc 도 닫기도 같은 답이다
  using(var dialog=new GuideDialog(running))
  {
   dialog.Show();Application.DoEvents();var buttons=All(dialog).OfType<Button>().ToList();
   Check(buttons.Count==1&&buttons[0].Text=="확인"&&dialog.AcceptButton==buttons[0]&&dialog.ActiveControl==buttons[0]&&dialog.Chosen==0,"with one button it is '확인', the default and the first focus");
   dialog.Close();Check(dialog.Chosen==0,"closing answers the same as the button");
  }
  // 3. 단추 여럿: 기본 선택이 맨 오른쪽의 채운 단추이고 연 직후의 Enter 와 Esc·닫기가 그쪽이다. 다른 단추는 눌러야만 고른다
  var choice=new Guide("이전 버전 복구","이전 버전으로 복구할까요?",new GuidePart[]{new GuideText("v1.1.0 으로 돌아갑니다.","strong"),new GuideText("플레이 기록과 설정은 그대로입니다.","dim")},new[]{"복구","취소"},1);
  using(var dialog=new GuideDialog(choice))
  {
   dialog.Show();Application.DoEvents();var buttons=All(dialog).OfType<Button>().OrderBy(button=>button.Left).ToList();
   Check(buttons.Select(button=>button.Text).SequenceEqual(new[]{"복구","취소"})&&buttons[1].Tag as string=="primary"&&buttons[0].Tag as string!="primary"&&dialog.AcceptButton==buttons[1]&&dialog.ActiveControl==buttons[1]&&dialog.Chosen==1,"with a choice, the default is the filled button on the right and has the first focus");
   buttons[0].PerformClick();Check(dialog.Chosen==0&&!dialog.Visible,"the other button answers only when it is pressed");
  }
  using(var dialog=new GuideDialog(choice with{Default=0})){dialog.Show();Application.DoEvents();var buttons=All(dialog).OfType<Button>().OrderBy(button=>button.Left).ToList();Check(buttons.Select(button=>button.Text).SequenceEqual(new[]{"취소","복구"})&&dialog.AcceptButton==buttons[1]&&dialog.Chosen==0,"whichever answer is the default goes to the right");dialog.Close();Check(dialog.Chosen==0,"and closing gives that default");}
  // 4. 놓을 수 있는 것 전부: 경로 상자, 절차, 주소, 펼치는 줄. 펴면 창이 늘어나고 접으면 처음 높이로 돌아온다. 너비는 그대로다
  const string backup="D:\\DJMAXLocalRecords\\update\\backup\\v1.1.0-20261121-183409\\program";
  var full=new Guide("업데이트 실패","업데이트를 마치지 못했습니다",new GuidePart[]
  {
   new GuideText("원래 파일을 되돌리는 데도 실패해 두 버전의 프로그램 파일이 섞여 있을 수 있습니다.","strong"),new GuideText("플레이 기록과 설정은 그대로입니다.","dim"),new GuideText("아래 백업에서 프로그램 파일을 직접 복구해 주세요.","strong"),
   new GuidePaths(("복사할 백업 폴더",backup),("붙여넣을 프로그램 폴더","D:\\DJMAXLocalRecords")),
   new GuideSteps("디맥 개인 기록 저장 오버레이를 모두 종료합니다.","백업 폴더 안의 모든 파일과 폴더를 프로그램 폴더에 복사하고, 이름이 같은 파일은 덮어씁니다.","프로그램 폴더에서 DJMAXLocalRecords.exe를 실행합니다."),
   new GuideLink("자세한 복구 안내","https://github.com/Zionee148/DJMAXLocalRecords"),
   new GuideFold("오류 내용",new GuideText("파일 교체 중 오류\n다른 프로그램에서 파일을 사용하고 있습니다."),new GuideText("복구 중 오류\n파일에 접근할 수 없습니다.")),
  });
  using(var dialog=new GuideDialog(full))
  {
   dialog.Show();Application.DoEvents();int closed=dialog.Height,wide=dialog.Width;var fold=dialog.Folds.Single();
   var shown=All(dialog).Where(control=>control.Visible&&control is Label or TextBox or Button or SectionHeader).Select(control=>control.Text.Replace("\r\n","\n")).ToList();
   Check(!fold.Open&&shown.Contains(backup)&&shown.Contains("https://github.com/Zionee148/DJMAXLocalRecords")&&shown.Contains("1.")&&shown.Contains("오류 내용")==false&&!shown.Any(text=>text.Contains("파일 교체 중 오류")),"the folded part starts hidden; paths, steps and the address are there as text");
   var pathBox=All(dialog).OfType<WrapBox>().First(box=>box.Text==backup);
   Check(pathBox.Height>pathBox.Font.Height+4&&pathBox.ReadOnly&&pathBox.OnField,"a long path wraps onto more lines and can be selected (read-only text)");
   fold.Flip();Application.DoEvents();int opened=dialog.Height;
   Check(opened>closed&&dialog.Width==wide&&All(dialog).Any(control=>control is Label or WordLabel&&control.Visible&&control.Text.Contains("파일 교체 중 오류")),"opening the fold shows what is inside and the window grows with it, keeping its width");
   fold.Flip();Application.DoEvents();Check(dialog.Height==closed&&!dialog.Scrolled,"folding it again brings the window back to its first height (a guide that fits is never scrolled)");
   // 창의 글은 모두 글자를 가진 컨트롤에 있다(시험 도구가 자식 창의 글자로 창을 읽는다)
   fold.Flip();Application.DoEvents();var texts=All(dialog).Select(control=>control.Text.Replace("\r\n","\n")).Where(text=>text.Length>0).ToList();
   var wanted=full.Plain().Split('\n').Where(line=>line.Length>0&&!System.Text.RegularExpressions.Regex.IsMatch(line,"^[0-9]\\. ")).ToList();
   Check(wanted.All(line=>texts.Any(text=>text.Contains(line)))&&full.Parts.OfType<GuideSteps>().Single().Steps.All(step=>texts.Contains(step)),"every line the window shows is the text of a control, so a tool that reads window text finds it");
   dialog.Close();
  }
  // 4-1. 작업 영역보다 긴 안내: 첫 줄과 아래 단추는 그대로 보이고 그 사이만 굴린다.
  //  펼치는 줄을 펴고 접을 때 다시 정한다. Tab 으로 가려진 것에 가면 보이게 굴린다
  var tall=new Guide("실행 오류","프로그램을 시작할 수 없습니다.",Enumerable.Range(1,90).Select(i=>(GuidePart)new GuideText($"{i}번째 줄: 안내가 작업 영역보다 길어지는 경우를 만든다."))
   .Append(new GuidePaths(("남은 파일이 있는 폴더",backup))).Append(new GuideFold("오류 내용",new GuideText("펼친 내용 첫 줄"),new GuideText("펼친 내용 둘째 줄"))).ToArray());
  using(var dialog=new GuideDialog(tall))
  {
   dialog.Show();Application.DoEvents();var area=Screen.FromControl(dialog).WorkingArea;var ok=All(dialog).OfType<Button>().Single();var head=All(dialog).OfType<SectionHeader>().First();
   bool OnScreen(Control control)=>area.Contains(control.RectangleToScreen(control.ClientRectangle));
   Rectangle View()=>dialog.BodyArea.RectangleToScreen(dialog.BodyArea.ClientRectangle);
   Check(dialog.Scrolled&&dialog.Top>=area.Top&&dialog.Bottom<=area.Bottom&&OnScreen(ok)&&OnScreen(head)&&head.Text=="프로그램을 시작할 수 없습니다.",$"a guide taller than the work area keeps its first line and its button on screen and scrolls only the body (굴림 {dialog.Scrolled}, 창 {dialog.Bounds}, 작업 영역 {area}, 단추 {ok.RectangleToScreen(ok.ClientRectangle)}, 첫 줄 {head.RectangleToScreen(head.ClientRectangle)}, 본문 {dialog.BodyArea.Size})");
   Check(!All(dialog).OfType<HScrollBar>().Any()&&!dialog.BodyArea.HorizontalScroll.Visible,"it scrolls only up and down");
   var fold=dialog.Folds.Single();bool below=!View().Contains(fold.RectangleToScreen(fold.ClientRectangle));
   dialog.Activate();fold.Focus();Application.DoEvents();
   Check(below&&View().Contains(fold.RectangleToScreen(fold.ClientRectangle)),$"moving the keyboard focus to a part below the view scrolls it into view (처음에 가려짐 {below}, 초점 {fold.Focused}, 보이는 곳 {View()}, 펼치는 줄 {fold.RectangleToScreen(fold.ClientRectangle)}, 굴린 자리 {dialog.BodyArea.AutoScrollPosition})");
   int height=dialog.Height;fold.Flip();Application.DoEvents();
   Check(dialog.Scrolled&&dialog.Height==height&&dialog.Bottom<=area.Bottom&&OnScreen(ok),"opening the fold keeps the window inside the work area with the button where it was");
   dialog.Close();
  }
  // 4-2. 글은 낱말 사이에서만 줄을 바꾼다: 놓인 조각이 모두 원래 낱말이고, 어느 조각도 너비를 넘지 않고, 높이는 줄 수만큼이다
  {
   const TextFormatFlags flat=TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine;
   string sentence="이전 버전에서도 ‘UserData’ 폴더의 기록과 설정을 그대로 사용합니다. 복구하면 프로그램이 잠시 종료된 뒤 이전 버전으로 다시 실행됩니다.";
   var whole=sentence.Split(' ').ToHashSet();
   foreach(int width in new[]{120,200,300,404})
   {
    using var label=new WordLabel(sentence,width,9.75f,"strong");
    Check(label.Placed.All(word=>whole.Contains(word.Text))&&label.Placed.All(word=>word.X+TextRenderer.MeasureText(word.Text,label.Font,Size.Empty,flat).Width<=width)&&label.Height==label.Lines*label.Font.Height&&label.Text==sentence,
     $"words wrap only between words at width {width} ({label.Lines} lines) and the window text stays the whole sentence");
   }
   using(var narrow=new WordLabel("가나다라마바사아자차카타파하가나다라마바사",60,9.75f))
    Check(narrow.Lines>1&&string.Concat(narrow.Placed.Select(word=>word.Text))=="가나다라마바사아자차카타파하가나다라마바사","a word longer than the line is broken between letters, nothing lost");
   using(var two=new WordLabel("첫 줄\n둘째 줄",400,9.75f))Check(two.Lines==2,"a line break starts a new line");
   // 줄 앞의 들여쓰기를 살리고, 가운뎃점 목록의 다음 줄은 가운뎃점 뒤에 맞춘다
   using(var nested=new WordLabel("· 위 항목\n  · 아래 항목이 길어서 다음 줄로 넘어가는 경우를 보여 주는 목록의 한 줄",200,9.75f))
   {
    int space=TextRenderer.MeasureText(" ",nested.Font,Size.Empty,flat).Width;var rows=nested.Placed.GroupBy(word=>word.Y).OrderBy(row=>row.Key).Select(row=>row.Min(word=>word.X)).ToList();
    Check(rows[0]==0&&rows[1]==2*space&&rows.Count>2&&rows.Skip(2).All(x=>x>2*space),$"a nested list keeps its indent and wraps under its text, not under the dot ({string.Join(",",rows)})");
   }
  }
  // 주소가 든 줄만 글자 단위로 꺾고, 같은 문단의 한국어 줄은 낱말 사이에서 꺾는다
  using(var mixed=new GuideDialog(new Guide("업데이트 실패","설치된 프로그램의 파일 목록을 읽지 못했습니다",new GuidePart[]{new GuideFold("오류 내용",new GuideText("지금 설치된 프로그램의 파일 목록(update-manifest.json)이 없거나 읽히지 않아, 어느 파일을 바꿔도 되는지 알 수 없습니다.\n자세한 안내: https://github.com/Zionee148/DJMAXLocalRecords/blob/main/guide/update.md"))})))
  {
   mixed.Show();Application.DoEvents();mixed.Folds[0].Flip();Application.DoEvents();
   var words=All(mixed).OfType<WordLabel>().SingleOrDefault(label=>label.Text.StartsWith("지금 설치된"));var address=All(mixed).OfType<WrapBox>().SingleOrDefault(box=>box.Text.Contains("https://"));
   Check(words!=null&&address!=null&&!words.Text.Contains("https://")&&!address.Text.Contains("지금 설치된"),"a paragraph with an address puts only that line in the letter-wrapping box; the Korean line wraps between words");
   mixed.Close();
  }
  // 5. 창 대신 답하는 자리와, 주 화면 없이 띄우는 길
  try{Guide? seen=null;GuideDialog.Hook=guide=>{seen=guide;return 1;};Check(GuideDialog.Show(null,choice)==1&&GuideDialog.ShowAlone(choice)==1&&ReferenceEquals(seen,choice),"checks can answer in place of the window, for both ways of showing it");}
  finally{GuideDialog.Hook=null;}
  File.WriteAllLines(Path.Combine(AppContext.BaseDirectory,"guide-validation.txt"),notes);
 }
}
