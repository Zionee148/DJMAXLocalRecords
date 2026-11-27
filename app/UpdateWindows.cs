using System.Diagnostics;
namespace LadderRecords;

// 자동 업데이트의 창: 새 버전 안내(업데이트 / 나중에), 진행 창(다운로드 → 파일 확인 → 백업 → 교체 → 재실행), 교체를 맡은 쪽이 띄우는 창.

/// <summary>진행 단계를 한 줄씩 보여 준다. 끝난 것은 ✓, 하는 중인 것은 ▶ 로 표시한다. 단순한 모양이다: 교체·복구의 진행 창이 새 모양을 만들지 못했을 때 물러서는 자리(InstallWindow 의 plain)</summary>
sealed class StageList:FlowLayoutPanel
{
 readonly Label[] items;readonly string[] names;
 public StageList(string[] names)
 {
  this.names=names;AutoSize=true;AutoSizeMode=AutoSizeMode.GrowAndShrink;FlowDirection=FlowDirection.TopDown;WrapContents=false;Margin=new(0,0,0,8);
  items=names.Select(name=>Boxes.Text("·  "+name,"dim",10.5f)).ToArray();
  foreach(var item in items){item.Margin=new(0,2,0,2);Controls.Add(item);}
 }
 public void Set(int current,bool failed=false)
 {
  for(int i=0;i<items.Length;i++)
  {
   bool done=i<current,now=i==current;
   items[i].Text=(done?"✓  ":now?(failed?"✕  ":"▶  "):"·  ")+names[i];
   items[i].Tag=done?"good":now?(failed?"bad":"accent"):"dim";
   items[i].ForeColor=Theme.For(items[i]);
  }
 }
}

/// <summary>진행 창의 단계 하나의 상태</summary>
enum StageState{Later,Now,Done,Failed}

/// <summary>
/// 진행 창의 단계 줄: 지금 하는 단계는 강조색 세모·옅은 바탕·오른쪽 끝의 '진행 중',
/// 끝난 단계는 ✓, 남은 단계는 작은 점. 줄마다 단계의 이름을 글자로 가진다(시험 도구가 창의 글로 읽는다)
/// </summary>
sealed class StageRows:FlowLayoutPanel
{
 readonly StageRow[] rows;
 public StageRows(string[] names,int width)
 {
  AutoSize=true;AutoSizeMode=AutoSizeMode.GrowAndShrink;FlowDirection=FlowDirection.TopDown;WrapContents=false;Margin=Padding.Empty;
  rows=names.Select(name=>new StageRow(name){Size=new(width,28),Margin=Padding.Empty}).ToArray();Controls.AddRange(rows);
 }
 public void Set(int current,bool failed=false){for(int i=0;i<rows.Length;i++)rows[i].State=i<current?StageState.Done:i==current?(failed?StageState.Failed:StageState.Now):StageState.Later;}
 internal StageState StateOf(int index)=>rows[index].State;
 /// <summary>지금 하는 단계의 오른쪽 끝에 적는 글('진행 중', 업데이트 도중의 복구에서는 '복구 중')</summary>
 [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
 public string NowText{get=>rows[0].NowText;set{foreach(var row in rows)row.NowText=value;}}
}

sealed class StageRow:Control
{
 StageState state=StageState.Later;string now="진행 중";
 [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
 public string NowText{get=>now;set{if(now==value)return;now=value;Invalidate();}}
 [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
 public StageState State{get=>state;set{if(state==value)return;state=value;AccessibleDescription=value switch{StageState.Now=>"진행 중",StageState.Done=>"끝남",StageState.Failed=>"실패",_=>"남음"};Invalidate();}}
 public StageRow(string name)
 {
  SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
  Text=name;AccessibleName=name;AccessibleRole=AccessibleRole.StaticText;AccessibleDescription="남음";TabStop=false;
 }
 // 치수는 100% 때 줄 높이 28 의 값이다. 창의 크기 단계만큼 커진 줄의 높이로 나눠 쓴다
 protected override void OnPaint(PaintEventArgs e)
 {
  var g=e.Graphics;float k=Height/28f;g.Clear(Parent?.BackColor??Theme.Window);
  if(state is StageState.Now or StageState.Failed){using var fill=new SolidBrush(Theme.Field);g.FillRectangle(fill,ClientRectangle);}
  float x=10*k,middle=Height/2f;
  g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
  switch(state)
  {
   case StageState.Now:using(var mark=new SolidBrush(Theme.Accent))g.FillPolygon(mark,new[]{new PointF(x,middle-5*k),new PointF(x,middle+5*k),new PointF(x+9*k,middle)});break;
   case StageState.Done:using(var tick=new Pen(Theme.Text,1.6f*k))g.DrawLines(tick,new[]{new PointF(x,middle),new PointF(x+3.5f*k,middle+3.5f*k),new PointF(x+9*k,middle-4*k)});break;
   case StageState.Failed:using(var cross=new Pen(Theme.Bad,1.6f*k)){g.DrawLine(cross,x,middle-4*k,x+8*k,middle+4*k);g.DrawLine(cross,x,middle+4*k,x+8*k,middle-4*k);}break;
   default:using(var dot=new SolidBrush(Theme.Dim))g.FillEllipse(dot,x+3*k,middle-1.5f*k,3*k,3*k);break;
  }
  g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.Default;
  bool strong=state!=StageState.Later;
  using(var font=Theme.Px(AppFonts.Ui,10.5f*k,strong?FontStyle.Bold:FontStyle.Regular))
   TextRenderer.DrawText(g,Text,font,new Rectangle((int)(30*k),0,Width-(int)(30*k),Height),state==StageState.Failed?Theme.Bad:strong?Theme.Text:Theme.Dim,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix);
  if(state==StageState.Now)
   using(var small=Theme.Px(AppFonts.Ui,9f*k,FontStyle.Bold))
    TextRenderer.DrawText(g,now,small,new Rectangle(0,0,Width-(int)(10*k),Height),Theme.Accent,TextFormatFlags.Right|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix);
 }
}

/// <summary>얼마나 받았는지 보여 주는 가는 막대. 값이 없으면 비워 둔다. Hidden 이면 자리만 차지하고 아무것도 그리지 않는다(진행 창의 높이가 단계마다 바뀌지 않게).</summary>
sealed class ProgressStrip:Control
{
 double? value;bool hidden;
 [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
 public bool Hidden{get=>hidden;set{if(hidden==value)return;hidden=value;Invalidate();}}
 public ProgressStrip(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);Height=6;TabStop=false;}
 [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
 public double? Value{get=>value;set{this.value=value;Invalidate();}}
 protected override void OnPaint(PaintEventArgs e)
 {
  if(hidden){e.Graphics.Clear(Parent?.BackColor??Theme.Window);return;}
  using(var back=new SolidBrush(Theme.Line))e.Graphics.FillRectangle(back,ClientRectangle);
  if(value is double v){using var fill=new SolidBrush(Theme.Fill);e.Graphics.FillRectangle(fill,0,0,(int)(Width*Math.Clamp(v,0,1)),Height);}
 }
}

/// <summary>
/// 새 버전 안내(너비 500). 예(Yes)는 '업데이트', 그 밖에는 '나중에'다.
/// '업데이트'는 강조색으로 그리고, 처음 키보드 초점과 Enter·Esc·닫기는 '나중에'다. '업데이트'를 누르거나 Tab 으로 옮겨 Enter 를 눌러야 시작한다.
/// 패치노트가 짧으면 내용에 맞는 높이로 보이고, 길면 그 칸 안에서만 굴린다(처음 줄을 볼 때도 끝 줄을 볼 때도 단추는 같은 자리에 있다)
/// </summary>
class UpdateDialog:CompactDialog
{
 public const int ContentWidth=464,Side=18,NotesMax=220;
 readonly VerticalScroller notesView=new(){Tag="field"};readonly FlowLayoutPanel notesList=new();readonly FieldPanel notesBox=new();
 /// <summary>패치노트의 칸(개발용 검사)</summary>
 internal VerticalScroller NotesArea=>notesView;

 public UpdateDialog(ReleaseInfo release,UpdateSource source):base("업데이트")
 {
  Body.Padding=new(0,16,0,0);
  var main=new FlowLayoutPanel(){AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,FlowDirection=FlowDirection.TopDown,WrapContents=false,Margin=new(Side,0,Side,14)};
  Body.Controls.Add(main);
  main.Controls.Add(new SectionHeader("새 버전이 있습니다"){Width=ContentWidth,Margin=Padding.Empty});
  main.Controls.Add(Versions(release));
  main.Controls.Add(new SectionHeader("패치노트"){Width=ContentWidth,Margin=new(0,16,0,0)});
  main.Controls.Add(Notes(release.Notes));
  var note=new WordLabel("업데이트하면 프로그램이 잠시 꺼졌다가 다시 켜집니다. 기록과 설정은 유지되며, 현재 버전은 백업으로 남습니다.",ContentWidth,9.75f){Margin=new(0,12,0,0)};main.Controls.Add(note);
  // 선과 아래 줄: 왼쪽에 밑줄 글자 '릴리즈 페이지 열기', 오른쪽에 '업데이트'(강조) · '나중에'
  Body.Controls.Add(new Panel(){Size=new(ContentWidth+Side*2,1),Margin=Padding.Empty,Tag="line"});
  var update=new Button(){Text="업데이트",DialogResult=DialogResult.Yes,Tag="primary"};
  var later=new Button(){Text="나중에",DialogResult=DialogResult.No,Tag="plain"};
  var footer=Parts.Footer(ContentWidth,update,later);
  var page=new LinkLabel(){Text="릴리즈 페이지 열기",AutoSize=true,LinkBehavior=LinkBehavior.AlwaysUnderline,UseMnemonic=false,Font=Theme.Px(AppFonts.Ui,9.75f),Margin=Padding.Empty,
   LinkColor=Theme.Text,ActiveLinkColor=Theme.Accent,VisitedLinkColor=Theme.Text,TabIndex=0};
  page.LinkClicked+=(_,_)=>OpenPage(release.Page,source);
  var band=new Panel(){Size=new(ContentWidth+Side*2,27+24),Margin=Padding.Empty};band.Controls.Add(page);band.Controls.Add(footer);
  footer.Location=new(Side,12);footer.TabIndex=1;page.Location=new(Side,12+(27-page.PreferredHeight)/2);
  Body.Controls.Add(band);
  AcceptButton=later;CancelButton=later;Shown+=(_,_)=>ActiveControl=later;
  // 창이 단계만큼 커진 뒤의 글자로 패치노트의 높이를 정한다. 창이 화면에 놓인 뒤(보이기 전) 그 화면의 작업 영역에 맞춰 한 번 더 정한다
  Load+=(_,_)=>FitNotes(null);
 }
 protected override void OnShown(EventArgs e)
 {
  try
  {
   var area=Screen.FromRectangle(Bounds).WorkingArea;FitNotes(area);PerformLayout();
   if(Bottom>area.Bottom||Top<area.Top)Top=Math.Max(area.Top,area.Top+(area.Height-Height)/2);
  }
  catch{}   // 높이를 정하지 못해도 창은 보여야 한다
  base.OnShown(e);
 }

 // 현재 버전 · 새 버전(강조색) · 내려받을 크기의 세 칸
 static Control Versions(ReleaseInfo release)
 {
  var box=new FieldPanel(){Size=new(ContentWidth,68),Margin=new(0,10,0,0)};int third=ContentWidth/3;
  void Column(int index,string name,string value,string? tag)
  {
   int x=index*third;
   if(index>0)box.Controls.Add(new Panel(){Location=new(x,14),Size=new(1,40),Tag="line"});
   box.Controls.Add(new Label(){Text=name,AutoSize=true,UseMnemonic=false,Tag="dim",Font=Theme.Px(AppFonts.Ui,9f),BackColor=Color.Transparent,Margin=Padding.Empty,Location=new(x+14,13)});
   box.Controls.Add(new Label(){Text=value,AutoSize=true,UseMnemonic=false,Tag=tag,Font=Theme.Px(AppFonts.Latin,15f,FontStyle.Bold),BackColor=Color.Transparent,Margin=Padding.Empty,Location=new(x+13,31)});
  }
  Column(0,"현재 버전",AppVersion.Text(AppVersion.Current),null);
  Column(1,"새 버전",AppVersion.Text(release.Version),"accent");
  Column(2,"내려받을 크기",$"{release.Size/1048576.0:N0} MB",null);
  return box;
 }

 /// <summary>릴리즈 본문(마크다운)을 보일 줄로: 제목(#)은 굵게, 목록(- , * )은 가운뎃점, 굵게 표시(**)는 지운다. Gap 은 앞에 빈 줄이 있었는가(문단 사이)</summary>
 internal static List<(string Text,bool Head,bool Gap)> NoteLines(string notes)
 {
  var lines=new List<(string Text,bool Head,bool Gap)>();bool gap=false;
  foreach(string raw in notes.Replace("\r\n","\n").Replace('\r','\n').Split('\n'))
  {
   string line=raw.TrimEnd(),trimmed=line.TrimStart();
   if(trimmed.Length==0){gap=lines.Count>0;continue;}
   string indent=line[..(line.Length-trimmed.Length)];
   if(trimmed.StartsWith('#'))lines.Add((trimmed.TrimStart('#').Trim().Replace("**",""),true,gap));
   else if(trimmed.StartsWith("- ")||trimmed.StartsWith("* "))lines.Add((indent+"· "+trimmed[2..].Replace("**",""),false,gap));
   else lines.Add((line.Replace("**",""),false,gap));
   gap=false;
  }
  if(lines.Count==0)lines.Add(("(패치노트가 비어 있습니다)",false,false));
  return lines;
 }
 Control Notes(string notes)
 {
  notesBox.Width=ContentWidth;notesBox.Margin=new(0,10,0,0);
  notesList.AutoSize=true;notesList.AutoSizeMode=AutoSizeMode.GrowAndShrink;notesList.FlowDirection=FlowDirection.TopDown;notesList.WrapContents=false;notesList.Margin=Padding.Empty;notesList.Padding=new(14,11,14,11);
  // 굴릴 때 세로 막대가 들어설 자리를 늘 남겨 둔다(글이 막대 밑으로 들어가지 않게)
  int width=ContentWidth-2-28-18;
  foreach(var (text,head,gap) in NoteLines(notes))
  {
   var line=new WordLabel(text,width,head?10f:9.75f,head?"strong":""){Margin=new(0,notesList.Controls.Count==0?0:gap?10:3,0,0)};
   notesList.Controls.Add(line);
  }
  notesView.Controls.Add(notesList);notesList.Location=Point.Empty;
  notesView.Location=new(1,1);notesBox.Controls.Add(notesView);
  int full=notesList.GetPreferredSize(Size.Empty).Height;
  notesView.Size=new(ContentWidth-2,Math.Min(full,NotesMax));notesBox.Height=notesView.Height+2;
  return notesBox;
 }
 /// <summary>
 /// 패치노트의 칸: 짧으면 내용의 높이, 길면 정한 높이(100% 때 220)에서 굴린다. area 를 주면 창 전체가 그 작업 영역에 들어가게 칸을 더 줄인다
 /// (큰 앱 표시 크기로 작은 보조 화면에 뜨면 단추가 화면 밖으로 나갈 수 있다. 제목·릴리즈 링크·두 단추는 늘 보이고 패치노트만 굴린다)
 /// </summary>
 internal void FitNotes(Rectangle? area)
 {
  int full=notesList.Height,max=Theme.S(NotesMax);
  if(area is {} room)
  {
   // 패치노트 칸을 뺀 창의 높이. 창의 실제 높이는 화면 크기쯤에서 막히므로 내용의 높이로 셈한다
   int others=Height-ClientSize.Height+Padding.Vertical+Body.Margin.Vertical+Body.GetPreferredSize(Size.Empty).Height-notesView.Height;
   max=Math.Max(Theme.S(60),Math.Min(max,room.Height-others));
  }
  bool scroll=full>max;
  notesView.Scrolling=scroll;notesView.Height=scroll?max:full;notesBox.Height=notesView.Height+2*Math.Max(1,notesView.Top);
 }

 /// <summary>릴리즈 페이지를 기본 브라우저로 연다. 공식 저장소의 주소일 때만 연다.</summary>
 public static void OpenPage(string page,UpdateSource source)
 {
  if(!page.StartsWith(source.PagePrefix,StringComparison.Ordinal)||!Uri.TryCreate(page,UriKind.Absolute,out var address)||(address.Scheme!=Uri.UriSchemeHttps&&!source.Test))return;
  try{Process.Start(new ProcessStartInfo(address.AbsoluteUri){UseShellExecute=true})?.Dispose();}
  catch(System.ComponentModel.Win32Exception){}
 }
}

/// <summary>
/// 업데이트를 준비하는 동안 띄우는 창(너비 442). Work 가 끝나면 스스로 닫힌다.
/// 다섯 단계에서 지금 하는 것만 강조한다. 받은 용량과 막대는 다운로드 동안에만 보이고, 파일 확인부터는 하는 일을 글로 적는다.
/// 높이는 처음부터 가장 긴 상태(다운로드: 글, 용량, 막대)에 맞춰 두고 단계가 바뀌어도 그대로다.
/// 교체로 넘어가기 직전에 취소를 잠근다: 취소 단추를 거두고 그 자리에 기다려 달라는 글을 보인다.
/// 키: 닫기(X)와 Esc 는 취소 요청이다(잠긴 뒤에는 아무 일도 하지 않는다). 처음에는 어느 단추에도 초점을 두지 않는다(Enter 로 취소되지 않게. Tab 으로 '취소'에 갈 수 있다)
/// </summary>
class UpdateProgressDialog:CompactDialog
{
 public const int ContentWidth=404,Side=18;
 public const string Downloading="업데이트 파일을 받는 중",Cancelling="취소하는 중",Waiting="업데이트가 끝날 때까지 기다려 주세요.";
 readonly StageRows stages=new(UpdatePrepare.Stages,ContentWidth);
 readonly Label detail=Boxes.Text(" ",null,9.75f,true,ContentWidth-130),amount=new(){AutoSize=false,Size=new(126,20),TextAlign=ContentAlignment.TopRight,UseMnemonic=false,Font=Theme.Px(AppFonts.Latin,9.75f,FontStyle.Bold),Margin=Padding.Empty};
 readonly ProgressStrip bar=new(){Hidden=true};
 readonly Button cancel=new(){Text="취소",Tag="plain"};
 readonly Label wait=Boxes.Text(Waiting,null,9.75f);
 readonly CancellationTokenSource source=new();
 bool locked,finished;
 public Func<UpdateProgressDialog,CancellationToken,Task>? Work;
 /// <summary>멈춘 까닭(실패했을 때만 값이 있다. UpdateException 이면 종류와 값이 함께 온다). 취소했으면 Cancelled, 끝까지 갔으면 Completed.</summary>
 public Exception? Failure{get;private set;}
 public bool Cancelled{get;private set;}
 public bool Completed{get;private set;}
 /// <summary>취소를 잠갔는가(개발용 검사)</summary>
 internal bool CancelLocked=>locked;
 internal StageRows Stages=>stages;
 internal Label Detail=>detail;
 internal Label Amount=>amount;
 internal ProgressStrip Bar=>bar;
 internal Button CancelButtonForChecks=>cancel;
 internal Label WaitNote=>wait;

 public UpdateProgressDialog(Version from,Version to):base("업데이트")
 {
  Body.Padding=new(0,16,0,0);
  var main=new FlowLayoutPanel(){AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,FlowDirection=FlowDirection.TopDown,WrapContents=false,Margin=new(Side,0,Side,14)};
  Body.Controls.Add(main);
  main.Controls.Add(new SectionHeader("업데이트 진행"){Width=ContentWidth,Margin=Padding.Empty});
  // 앞의 버전(옅게) > 새 버전
  var versions=new FlowLayoutPanel(){AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,WrapContents=false,Margin=new(0,12,0,0)};
  Label Version(string text,string? tag,float size)=>new(){Text=text,AutoSize=true,UseMnemonic=false,Tag=tag,Font=Theme.Px(AppFonts.Latin,size,FontStyle.Bold),Margin=new(0,0,10,0),Anchor=AnchorStyles.Left};
  var arrow=Version(">","dim",11f);arrow.Anchor=AnchorStyles.None;
  versions.Controls.AddRange(new Control[]{Version(AppVersion.Text(from),"dim",13f),arrow,Version(AppVersion.Text(to),null,13f)});
  main.Controls.Add(versions);
  stages.Margin=new(0,12,0,0);main.Controls.Add(stages);
  main.Controls.Add(new Panel(){Size=new(ContentWidth,1),Margin=new(0,12,0,0),Tag="line"});
  var detailRow=new Panel(){Size=new(ContentWidth,32),Margin=new(0,10,0,0)};
  detail.Location=new(0,0);amount.Location=new(ContentWidth-126,1);bar.Location=new(0,26);bar.Size=new(ContentWidth,6);
  detailRow.Controls.AddRange(new Control[]{detail,amount,bar});main.Controls.Add(detailRow);
  Body.Controls.Add(new Panel(){Size=new(ContentWidth+Side*2,1),Margin=Padding.Empty,Tag="line"});
  cancel.Click+=(_,_)=>RequestCancel();
  var footer=Parts.Footer(ContentWidth,cancel);
  var band=new Panel(){Size=new(ContentWidth+Side*2,27+24),Margin=Padding.Empty};band.Controls.Add(footer);band.Controls.Add(wait);
  footer.Location=new(Side,12);wait.Location=new(Side,12+(27-wait.PreferredHeight)/2);wait.Visible=false;wait.BringToFront();   // 단추 줄(투명하지 않은 패널)이 같은 자리에 있다. 글을 그 위에 둔다
  Body.Controls.Add(band);
  stages.Set(0);
  KeyPreview=true;KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Escape){e.Handled=true;RequestCancel();}};
  FormClosing+=(_,e)=>{if(finished)return;e.Cancel=true;RequestCancel();};
  Shown+=async(_,_)=>
  {
   ActiveControl=null;
   try{if(Work!=null)await Work(this,source.Token);Completed=true;}
   catch(OperationCanceledException){Cancelled=true;}
   catch(Exception e){Failure=e;}
   finally{finished=true;Close();}
  };
 }
 void RequestCancel()
 {
  if(locked||finished||source.IsCancellationRequested)return;
  source.Cancel();cancel.Enabled=false;detail.Text=Cancelling;amount.Text="";bar.Hidden=true;
 }
 /// <summary>어느 단계의 무엇을 하는 중인지 알린다. 다운로드 단계의 text 는 받은 용량이다("12.3 / 45.6 MB"). 어느 스레드에서 불러도 된다.</summary>
 public void Report(int stage,string? text,double? fraction)
 {
  if(IsDisposed||!IsHandleCreated)return;
  if(InvokeRequired){try{BeginInvoke(()=>Report(stage,text,fraction));}catch(InvalidOperationException){}return;}
  if(source.IsCancellationRequested&&!locked)return;
  stages.Set(stage);
  bool download=stage==UpdatePrepare.Download;
  detail.Text=download?Downloading:text??" ";amount.Text=download?text??"":"";
  bar.Hidden=!download;bar.Value=download?fraction:null;
 }
 /// <summary>교체로 넘어가기 직전에 부른다(창을 만든 스레드에서). 이미 취소를 눌렀으면 false 이고, 아니면 이때부터 취소할 수 없다(취소 단추 대신 기다려 달라는 글).</summary>
 public bool LockCancel()
 {
  if(source.IsCancellationRequested)return false;
  locked=true;cancel.Visible=false;wait.Visible=true;return true;
 }
}

/// <summary>
/// 교체나 복구를 맡은 쪽(새 버전 또는 백업의 실행 파일)이 띄우는 진행 창(업데이트의 교체, 백업으로 하는 복구, 업데이트 도중의 복구. 너비 442).
/// 일이 끝날 때까지 닫을 수 없고 단추도 닫기 단추도 없다. 아래 줄에는 기다려 달라는 글. Work 는 다른 스레드에서 돈다.
/// 높이는 처음부터 가장 긴 상태(상세 글 두 줄)에 맞춰 두고 그대로다.
/// plain 이면 단순한 모양(StageList)으로 만든다: 새 모양을 만들다 실패했을 때 물러서는 자리다
/// (이 창이 뜨지 못하면 모든 사용자의 업데이트가 막힌다)
/// </summary>
class InstallWindow:CompactDialog
{
 public const int ContentWidth=404,Side=18;
 readonly StageRows? rows;readonly StageList? list;readonly SectionHeader? head;readonly Label? fromLabel,toLabel,plainHead;readonly Control detail;readonly Label wait;
 bool finished;
 public Action<InstallWindow>? Work;
 /// <summary>단순한 모양으로 만들었는가(개발용 검사)</summary>
 internal bool Plain=>list!=null;
 internal StageRows? Rows=>rows;
 internal Control Detail=>detail;
 internal Label WaitNote=>wait;
 internal string Headline=>head?.Text??plainHead?.Text??"";

 public InstallWindow(string title,string headline,string? from,string? to,string[] names,int start,string waitText,bool plain=false):base(title)
 {
  StartPosition=FormStartPosition.CenterScreen;ShowInTaskbar=true;ControlBox=false;
  if(plain)
  {
   list=new(names);plainHead=Boxes.Text(from!=null&&to!=null?$"{from}  →  {to}":headline,null,11.5f,true);plainHead.Margin=new(0,0,0,10);
   var words=Boxes.Text(" ","dim",9.5f);words.MinimumSize=new(380,0);words.MaximumSize=new(380,0);detail=words;
   wait=Boxes.Text(waitText,"dim",9.5f);wait.Margin=new(0,10,0,0);
   Body.Controls.AddRange(new Control[]{plainHead,list,detail,wait});list.Set(start);
  }
  else
  {
   Body.Padding=new(0,16,0,0);
   var main=new FlowLayoutPanel(){AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,FlowDirection=FlowDirection.TopDown,WrapContents=false,Margin=new(Side,0,Side,14)};
   Body.Controls.Add(main);
   head=new SectionHeader(headline){Width=ContentWidth,Margin=Padding.Empty};main.Controls.Add(head);
   // 앞의 버전(옅게) > 뒤의 버전. 상태 파일을 읽지 못해 버전을 모르면 비워 둔다(줄의 자리는 그대로)
   var versions=new FlowLayoutPanel(){AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,WrapContents=false,Margin=new(0,12,0,0),MinimumSize=new(0,24)};
   Label Version(string text,string? tag,float size)=>new(){Text=text,AutoSize=true,UseMnemonic=false,Tag=tag,Font=Theme.Px(AppFonts.Latin,size,FontStyle.Bold),Margin=new(0,0,10,0),Anchor=AnchorStyles.Left};
   fromLabel=Version(from??"","dim",13f);toLabel=Version(to??"",null,13f);var arrow=Version(from!=null&&to!=null?">":"","dim",11f);arrow.Anchor=AnchorStyles.None;
   versions.Controls.AddRange(new Control[]{fromLabel,arrow,toLabel});main.Controls.Add(versions);
   rows=new(names,ContentWidth){Margin=new(0,12,0,0)};main.Controls.Add(rows);
   main.Controls.Add(new Panel(){Size=new(ContentWidth,1),Margin=new(0,12,0,0),Tag="line"});
   // 상세 글은 두 줄까지(복구의 글이 길다). 두 줄의 자리를 처음부터 잡아 둔다
   var words=new WordLabel(" ",ContentWidth,9.75f,"strong");detail=words;
   var detailBox=new Panel(){Size=new(ContentWidth,words.Font.Height*2),Margin=new(0,10,0,0)};detailBox.Controls.Add(words);main.Controls.Add(detailBox);
   Body.Controls.Add(new Panel(){Size=new(ContentWidth+Side*2,1),Margin=Padding.Empty,Tag="line"});
   wait=Boxes.Text(waitText,null,9.75f);
   var band=new Panel(){Size=new(ContentWidth+Side*2,27+24),Margin=Padding.Empty};band.Controls.Add(wait);wait.Location=new(Side,12+(27-wait.PreferredHeight)/2);
   Body.Controls.Add(band);
   rows.Set(start);
  }
  FormClosing+=(_,e)=>{if(!finished)e.Cancel=true;};
  Shown+=(_,_)=>Task.Run(()=>
  {
   try{Work?.Invoke(this);}
   finally{try{BeginInvoke(()=>{finished=true;Close();});}catch(InvalidOperationException){}}
  });
 }
 /// <summary>지금의 단계와 상세 글. 어느 스레드에서 불러도 된다(창의 스레드로 넘긴다)</summary>
 public void Stage(int index,string? text,bool failed=false)=>Invoke(()=>{rows?.Set(index,failed);list?.Set(index,failed);detail.Text=text??" ";});
 /// <summary>업데이트 도중의 복구로 바꾼다: 첫 줄은 '이전 버전 복구', 버전은 새 → 이전, 지금 단계의 오른쪽 끝은 '복구 중', 아래 줄은 복구가 끝날 때까지 기다려 달라는 글</summary>
 public void Restoring(string? from,string? to)=>Invoke(()=>
 {
  if(head!=null)head.Text=UpdateGuides.RollbackHead;
  if(fromLabel!=null&&toLabel!=null){fromLabel.Text=from??"";toLabel.Text=to??"";}
  if(plainHead!=null)plainHead.Text=from!=null&&to!=null?$"{from}  →  {to}":UpdateGuides.RollbackHead;
  if(rows!=null)rows.NowText="복구 중";
  wait.Text=UpdateGuides.RollbackWait;
 });
 /// <summary>이 창 위에 안내를 띄우고 닫힐 때까지 기다린다. 누른 단추의 번호(닫기·Esc 는 기본 선택). 안내창을 띄우지 못하면 Windows 의 알림 창으로 알린다</summary>
 public int Show(Guide guide)=>(int)Invoke(()=>GuideDialog.ShowSafe(this,guide));
}
