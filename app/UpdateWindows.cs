using System.Diagnostics;
namespace LadderRecords;

// 자동 업데이트의 창: 새 버전 안내(업데이트 / 나중에), 진행 창(다운로드 → 파일 확인 → 백업 → 교체 → 재실행), 교체를 맡은 쪽이 띄우는 창.

/// <summary>진행 단계를 한 줄씩 보여 준다. 끝난 것은 ✓, 하는 중인 것은 ▶ 로 표시한다.</summary>
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

/// <summary>얼마나 받았는지 보여 주는 가는 막대. 값이 없으면 비워 둔다.</summary>
sealed class ProgressStrip:Control
{
 double? value;
 public ProgressStrip(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);Height=6;TabStop=false;}
 [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
 public double? Value{get=>value;set{this.value=value;Invalidate();}}
 protected override void OnPaint(PaintEventArgs e)
 {
  using(var back=new SolidBrush(Theme.Line))e.Graphics.FillRectangle(back,ClientRectangle);
  if(value is double v){using var fill=new SolidBrush(Theme.Accent);e.Graphics.FillRectangle(fill,0,0,(int)(Width*Math.Clamp(v,0,1)),Height);}
 }
}

/// <summary>새 버전을 알리고 업데이트할지 묻는다. 예(Yes)는 "업데이트", 그 밖에는 "나중에"다. 기본 단추는 "나중에"다.</summary>
class UpdateDialog:CompactDialog
{
 public UpdateDialog(ReleaseInfo release,UpdateSource source):base("업데이트")
 {
  const int Width=520;
  Body.Controls.Add(new InfoBox(){Controls={Boxes.Stats(12.5f,("현재 버전",AppVersion.Text(AppVersion.Current),null),("새 버전",AppVersion.Text(release.Version),"accent"),("내려받을 크기",$"{release.Size/1048576.0:N0}MB",null))}});
  Body.Controls.Add(new SectionHeader("패치노트"){Width=Width,Margin=new(0,0,0,4)});
  Body.Controls.Add(new TextBox(){Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Width=Width,Height=220,Text=UpdateChecker.PlainNotes(release.Notes),TabStop=false,Margin=new(0,0,0,10)});
  var note=Boxes.Text("업데이트하면 프로그램이 잠시 꺼졌다가 새 버전으로 다시 켜집니다. 기록과 설정은 그대로 유지되고, 지금 버전은 백업으로 남습니다.","dim",10f,false,Width);
  Body.Controls.Add(note);
  var row=new FlowLayoutPanel(){AutoSize=true,WrapContents=false,Margin=new(0,12,0,0)};
  var update=new Button(){Text="업데이트",AutoSize=true,DialogResult=DialogResult.Yes,Tag="primary"};
  var later=new Button(){Text="나중에",AutoSize=true,DialogResult=DialogResult.No,Tag="default"};
  var page=new Button(){Text="릴리즈 페이지 열기",AutoSize=true,Tag="plain"};
  page.Click+=(_,_)=>OpenPage(release.Page,source);
  row.Controls.AddRange(new Control[]{update,later,page});Body.Controls.Add(row);
  AcceptButton=later;CancelButton=later;Shown+=(_,_)=>ActiveControl=later;
 }
 /// <summary>릴리즈 페이지를 기본 브라우저로 연다. 공식 저장소의 주소일 때만 연다.</summary>
 public static void OpenPage(string page,UpdateSource source)
 {
  if(!page.StartsWith(source.PagePrefix,StringComparison.Ordinal)||!Uri.TryCreate(page,UriKind.Absolute,out var address)||(address.Scheme!=Uri.UriSchemeHttps&&!source.Test))return;
  try{Process.Start(new ProcessStartInfo(address.AbsoluteUri){UseShellExecute=true})?.Dispose();}
  catch(System.ComponentModel.Win32Exception){}
 }
}

/// <summary>업데이트를 준비하는 동안 띄우는 창. Work 가 끝나면 스스로 닫힌다. 교체로 넘어가기 전까지 취소할 수 있다.</summary>
class UpdateProgressDialog:CompactDialog
{
 readonly StageList stages=new(UpdatePrepare.Stages);
 readonly Label detail=Boxes.Text(" ","dim",9.5f);
 readonly ProgressStrip bar=new(){Width=360,Margin=new(0,4,0,0)};
 readonly Button cancel=new(){Text="취소",AutoSize=true,Tag="plain"};
 readonly CancellationTokenSource source=new();
 bool locked,finished;
 public Func<UpdateProgressDialog,CancellationToken,Task>? Work;
 /// <summary>실패 사유(실패했을 때만 값이 있다). 취소했으면 Cancelled, 끝까지 갔으면 Completed.</summary>
 public string? Failure{get;private set;}
 public bool Cancelled{get;private set;}
 public bool Completed{get;private set;}

 public UpdateProgressDialog(Version from,Version to):base("업데이트")
 {
  var head=Boxes.Text($"{AppVersion.Text(from)}  →  {AppVersion.Text(to)}",null,11.5f,true);head.Margin=new(0,0,0,10);
  detail.MinimumSize=new(360,0);detail.MaximumSize=new(360,0);
  cancel.Margin=new(0,12,0,0);cancel.Click+=(_,_)=>RequestCancel();
  Body.Controls.AddRange(new Control[]{head,stages,detail,bar,cancel});
  stages.Set(0);
  FormClosing+=(_,e)=>{if(finished)return;e.Cancel=true;RequestCancel();};
  Shown+=async(_,_)=>
  {
   try{if(Work!=null)await Work(this,source.Token);Completed=true;}
   catch(OperationCanceledException){Cancelled=true;}
   catch(UpdateException e){Failure=e.Message;}
   catch(Exception e){Failure=e.Message;}
   finally{finished=true;Close();}
  };
 }
 void RequestCancel()
 {
  if(locked||finished||source.IsCancellationRequested)return;
  source.Cancel();cancel.Enabled=false;detail.Text="취소하는 중…";
 }
 /// <summary>어느 단계의 무엇을 하는 중인지 알린다. 어느 스레드에서 불러도 된다.</summary>
 public void Report(int stage,string? text,double? fraction)
 {
  if(IsDisposed||!IsHandleCreated)return;
  if(InvokeRequired){try{BeginInvoke(()=>Report(stage,text,fraction));}catch(InvalidOperationException){}return;}
  if(source.IsCancellationRequested&&!locked)return;
  stages.Set(stage);detail.Text=text??" ";bar.Value=fraction;
 }
 /// <summary>교체로 넘어가기 직전에 부른다(창을 만든 스레드에서). 이미 취소를 눌렀으면 false 이고, 아니면 이때부터 취소할 수 없다.</summary>
 public bool LockCancel()
 {
  if(source.IsCancellationRequested)return false;
  locked=true;cancel.Enabled=false;return true;
 }
}

/// <summary>교체나 복구를 맡은 쪽(새 버전 또는 백업의 실행 파일)이 띄우는 창. 일이 끝날 때까지 닫을 수 없다. Work 는 다른 스레드에서 돈다.</summary>
class InstallWindow:CompactDialog
{
 readonly StageList stages;readonly Label detail=Boxes.Text(" ","dim",9.5f);
 bool finished;
 public Action<InstallWindow>? Work;

 public InstallWindow(string title,string heading,string[] names,int start):base(title)
 {
  StartPosition=FormStartPosition.CenterScreen;ShowInTaskbar=true;ControlBox=false;
  stages=new(names);
  var head=Boxes.Text(heading,null,11.5f,true);head.Margin=new(0,0,0,10);
  detail.MinimumSize=new(380,0);detail.MaximumSize=new(380,0);
  var wait=Boxes.Text("끝날 때까지 이 창을 그대로 두세요.","dim",9.5f);wait.Margin=new(0,10,0,0);
  Body.Controls.AddRange(new Control[]{head,stages,detail,wait});
  stages.Set(start);
  FormClosing+=(_,e)=>{if(!finished)e.Cancel=true;};
  Shown+=(_,_)=>Task.Run(()=>
  {
   try{Work?.Invoke(this);}
   finally{try{BeginInvoke(()=>{finished=true;Close();});}catch(InvalidOperationException){}}
  });
 }
 public void Stage(int index,string? text,bool failed=false)=>Invoke(()=>{stages.Set(index,failed);detail.Text=text??" ";});
 /// <summary>확인만 받는 알림을 이 창 위에 띄우고 닫힐 때까지 기다린다.</summary>
 public void Notice(string title,string text)=>Invoke(()=>NoticeDialog.Show(this,title,text));
 /// <summary>예 / 아니요 를 묻고 답을 기다린다. 기본은 "아니요"다.</summary>
 public bool Ask(string title,string lead,string? note,string question)=>(bool)Invoke(()=>ChoiceDialog.Ask(this,title,lead,null,note,question)==DialogResult.Yes);
}
