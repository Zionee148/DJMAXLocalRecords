namespace LadderRecords;

// 주 화면의 자동 업데이트: "업데이트" 줄, 켤 때 한 번의 확인, 업데이트를 시작할 수 있는 상태인지 보기, 진행 창, 이전 버전 복구.
// 내려받기·교체·복구의 실제 일은 Update*.cs 에 있다.
partial class MainForm
{
 /// <summary>개발용 검사가 주 화면을 띄울 때는 끈다(검사 중에는 새 버전을 조회하거나 이전 업데이트의 상태 파일·임시 폴더·지난 백업을 정리하지 않는다).</summary>
 internal static bool UpdateFeatures=true;

 readonly Button updateCheck=new(){Text="업데이트 확인",AutoSize=true},updateRestore=new(){Text="이전 버전 복구",AutoSize=true,Visible=false};
 readonly Label updateNote=new(){AutoSize=true,Tag="dim",Margin=new(6,8,0,0),UseMnemonic=false};
 readonly UpdatePaths updatePaths=new(AppContext.BaseDirectory);
 readonly UpdateSource updateSource=UpdateSource.For(UpdateTestConfig.Load());
 // updating: 업데이트를 시작한 뒤부터 true. 단축키로 들어오는 조작을 받지 않는다(창의 조작은 진행 창이 막는다). 기록 저장도 잠근다
 bool updating,updateChecking;
 // 지난 업데이트의 결과(업데이트 완료, 이전 버전으로 복구함 등). 켤 때의 확인이 "최신 버전"이나 "확인 실패"로 이 글을 지우지 않게 따로 둔다
 string? updateStartNote;

 bool UpdateOff=>!UpdateFeatures||(AppVersion.IsDevelopment&&!updateSource.Test);
 string VersionText=>AppVersion.IsDevelopment?"개발용 빌드":AppVersion.Text(AppVersion.Current)+(updateSource.Test?" 시험용":"");

 Control[] UpdateRow()
 {
  updateCheck.Click+=(_,_)=>CheckUpdate(true);
  updateRestore.Click+=(_,_)=>Safe(RestorePrevious);
  updateCheck.Enabled=!UpdateOff;
  Note(UpdateOff&&AppVersion.IsDevelopment?"개발용 빌드 · 새 버전 확인 안 함":VersionText,"dim");
  return new Control[]{updateCheck,updateRestore,updateNote};
 }
 void Note(string text,string tag){updateNote.Text=text;updateNote.Tag=tag;updateNote.ForeColor=Theme.For(updateNote);}

 /// <summary>주 화면이 뜬 뒤 한 번: 지난 업데이트의 상태 파일을 보고 임시 폴더와 지난 백업을 정리한 다음(UpdateStartup.Resolve), 새 버전이 있는지 한 번 알아본다. 켜 둔 동안 다시 알아보지 않는다.</summary>
 void StartUpdateFeatures()
 {
  if(!UpdateFeatures)return;
  try
  {
   var(note,notice)=UpdateStartup.Resolve(updatePaths);
   // 업데이트 완료와 이전 버전으로 복구함만 초록이다. 지난 업데이트 실패·중단은 경고색이다
   if(note!=null){updateStartNote=note;Note(VersionText+" · "+note,UpdateStartup.Good(note)?"good":"warn");}
   ShowRestore();
   if(notice!=null)GuideDialog.Show(this,notice);
  }
  // 지난 업데이트의 상태 확인과 정리가 실패해도 프로그램은 떠야 한다. 업데이트 줄의 글은 명사형으로 적는다
  catch(Exception e){updatePaths.Log("프로그램 시작 시 이전 업데이트 정리 실패: "+e.Message);Note(VersionText+" · 지난 업데이트 상태 확인 실패","warn");}
  if(!UpdateOff)CheckUpdate(false);
 }
 void ShowRestore()
 {
  var backup=UpdateBackup.Latest(updatePaths);
  updateRestore.Visible=UpdateFeatures&&backup is {} found&&found.Info.Version!=AppVersion.Plain(AppVersion.Current);
 }

 /// <summary>새 버전이 있는지 알아본다. manual 이 아니면(켤 때의 확인) 결과를 업데이트 줄에만 적고 창을 띄우지 않는다.</summary>
 async void CheckUpdate(bool manual)
 {
  if(updateChecking||updating||UpdateOff)return;
  updateChecking=true;updateCheck.Enabled=false;
  if(manual)Note(VersionText+" · 확인 중","dim");
  try
  {
   var result=await UpdateChecker.Check(AppVersion.Current,updateSource);
   if(IsDisposed||Disposing)return;
   switch(result)
   {
    case UpdateAvailable found:
     // 지난 업데이트의 결과가 적혀 있으면 지우지 않고 뒤에 붙인다(줄이 길어지지 않게 번호는 뺀다. 번호는 [업데이트 확인]의 창에 보인다)
     Note(updateStartNote!=null&&!manual?$"{VersionText} · {updateStartNote} · 새 버전 있음":$"{VersionText} · 새 버전 {AppVersion.Text(found.Release.Version)} 있음","accent");
     if(manual)OfferUpdate(found.Release);
     break;
    case UpToDate:
     if(manual||updateStartNote==null)Note(VersionText+" · 최신 버전","dim");
     if(manual)GuideDialog.Show(this,UpdateGuides.Latest(AppVersion.Current));
     break;
    case NoPackage missing:
     Note($"{VersionText} · 새 버전 {AppVersion.Text(missing.Version)} (직접 내려받기 필요)","warn");
     if(manual&&GuideDialog.Show(this,UpdateGuides.NoPackage(missing.Version,missing.Reason))==0)
      UpdateDialog.OpenPage(missing.Page,updateSource);
     break;
    case CheckFailed failed:
     // 알아보지 못한 것을 최신 버전이라고 적지 않는다
     if(manual){Note(VersionText+" · 새 버전 확인 실패","warn");GuideDialog.Show(this,UpdateGuides.CheckFailed(failed.Reason,failed.Detail));}
     else if(updateStartNote==null)Note(VersionText+" · 새 버전 확인 실패","warn");
     break;
   }
  }
  catch(Exception e)when(e is not OperationCanceledException)
  {
   // 여기서 난 오류가 프로그램을 멈추게 두지 않는다
   if(IsDisposed||Disposing)return;
   Note(VersionText+" · 새 버전 확인 실패","warn");
   if(manual)GuideDialog.Show(this,UpdateGuides.CheckFailed(UpdateChecker.CheckError,e.Message));
  }
  finally{updateChecking=false;if(!IsDisposed)updateCheck.Enabled=!updating;}
 }

 /// <summary>
 /// 지금 업데이트(또는 복구)를 시작할 수 없는 까닭. 시작해도 되면 null. 등록 후보를 대신 저장하거나 취소하지 않는다: 사용자가 먼저 끝내야 한다.
 /// 여럿이 걸리면 점수 확인·저장 확인, 감지, 가져오기, 열린 창의 차례로 먼저 걸리는 것 하나다
 /// </summary>
 UpdateBusy? UpdateBlocker()
 {
  if(activeRegistration!=null)return UpdateBusy.Scoring;
  if(session.Pending!=null)return UpdateBusy.Pending;
  if(session.Running)return UpdateBusy.Detecting;
  if(importing)return UpdateBusy.Importing;
  if(recordsVisible||Application.OpenForms.Cast<Form>().Any(form=>form!=this&&form.Visible))return UpdateBusy.OtherWindow;
  return null;
 }

 void OfferUpdate(ReleaseInfo release)
 {
  using(var offer=new UpdateDialog(release,updateSource))if(offer.ShowDialog(this)!=DialogResult.Yes)return;   // 나중에: 이번 실행에서는 다시 묻지 않는다(줄의 안내는 남는다)
  if(UpdateBlocker() is {} blocker){GuideDialog.Show(this,UpdateGuides.Busy(blocker,false));return;}
  RunUpdate(release);
 }

 void RunUpdate(ReleaseInfo release)
 {
  if(updating)return;
  updating=true;store.Frozen=true;updateCheck.Enabled=false;
  string? backup=null;bool handed=false;
  using var progress=new UpdateProgressDialog(AppVersion.Current,release.Version);
  progress.Work=async(dialog,cancel)=>
  {
   backup=await UpdatePrepare.Run(updatePaths,release,dialog.Report,cancel);
   // 여기서부터는 취소할 수 없다. 그 사이에 취소를 눌렀으면 넘어가지 않는다
   if(!dialog.LockCancel())throw new OperationCanceledException();
   dialog.Report(UpdatePrepare.Replace,"새 버전 설치를 시작하는 중",null);
   await Task.Run(()=>UpdatePrepare.HandOver(updatePaths,release,backup));
   handed=true;
  };
  progress.ShowDialog(this);
  if(handed)
  {
   // 새 버전의 설치 과정이 이 프로그램이 끝나기를 기다린다. 조작 제한은 풀지 않고 끝낸다
   status.Text="업데이트하려고 프로그램을 종료합니다.";
   Close();return;
  }
  // 교체 전에 멈췄다: 프로그램도 기록도 바뀌지 않았다. 상태 파일, 내려받아 풀어 놓은 파일, 이번에 만든 백업 폴더를 지우고 조작 제한을 푼다
  updatePaths.ClearState();UpdatePrepare.Clear(updatePaths);if(backup!=null)UpdateProgram.RemoveNewBackup(updatePaths,backup);
  updating=false;store.Frozen=false;updateCheck.Enabled=!UpdateOff;
  if(progress.Cancelled)
  {
   updatePaths.Log("사용자가 취소함");
   status.Text="업데이트를 취소했습니다.";
   GuideDialog.Show(this,UpdateGuides.Cancelled(AppVersion.Current));
  }
  else
  {
   var why=progress.Failure??new UpdateException("알 수 없는 원인으로 멈췄습니다.");
   updatePaths.Log($"교체 전에 실패({(why as UpdateException)?.Kind??UpdateTrouble.Unknown}): "+why.Message);
   status.Text="업데이트 실패";
   GuideDialog.Show(this,UpdateGuides.Failed(why,AppVersion.Current));
  }
 }

 /// <summary>자동 업데이트가 남긴 직전 버전으로 프로그램 파일을 되돌린다. 기록과 설정은 지금 것을 그대로 둔다.</summary>
 void RestorePrevious()
 {
  if(updating)return;
  if(UpdateBackup.Latest(updatePaths) is not {} backup){ShowRestore();GuideDialog.Show(this,UpdateGuides.NoBackup);return;}
  if(UpdateBlocker() is {} blocker){GuideDialog.Show(this,UpdateGuides.Busy(blocker,true));return;}
  // 복구 확인: 단추 0 '복구', 1 '취소'(기본. 처음 초점이 '취소'라 연 직후의 Enter 와 Esc·닫기는 취소)
  // 복구 뒤의 기록 줄: 기록이 'UserData'에 자리 잡았을 때만 "따로 보관된다"고 적는다
  if(GuideDialog.Show(this,UpdateGuides.RestoreAsk(AppVersion.Current,backup.Info.Version,backup.Info.Made,UserFiles.RecordsMoved(AppContext.BaseDirectory)))!=0)return;
  if(UpdateBlocker() is {} late){GuideDialog.Show(this,UpdateGuides.Busy(late,true));return;}
  updating=true;store.Frozen=true;
  try
  {
   UpdatePrepare.Launch(updatePaths,Path.Combine(UpdateBackup.Program(backup.Folder),UpdateManifest.Executable),UpdateProgram.RestoreFlag,"복구 과정");
  }
  catch(UpdateException e)
  {
   updating=false;store.Frozen=false;
   GuideDialog.Show(this,UpdateGuides.RestoreNotStarted(e,backup.Folder,updatePaths.App));
   return;
  }
  status.Text="이전 버전으로 복구하려고 프로그램을 종료합니다.";
  Close();
 }
}
