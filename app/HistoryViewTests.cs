namespace LadderRecords;
// 기록 히스토리가 보여 줄 것의 셈을 본다(app\HistoryView.cs). 자체 검사(--self-test)에 들어 있다. 창을 띄우지 않는다.
// 예시 기록으로 본다: PB 를 가르는 다섯 판(2), 일시 미상 기록과 PB(3)
static class HistoryViewTests
{
 // 그래프의 셈(app\HistoryGraph.cs 의 GraphMath). PB 와 점수 그룹, 일시 미상 PB 와 범위 밖 PB 를 예시 기록으로 본다.
 static void Graph(Action<bool,string> Check,Func<int,int,int,string,string,string?,Play> At,Func<int,int,string,Play> No)
 {
  const string normal="song|8|SC|노말";
  HistoryModel Of(params int[] scores)=>new(normal,scores.Select((score,i)=>At(i+1,score,i,normal,PlaySource.Play,null)));
  int[] Lows(HistoryModel model,GraphAxis axis)=>GraphMath.Points(model,axis,0).Where(p=>p.Low).Select(p=>p.Number).ToArray();
  int[] Labels(GraphAxis axis,int total)=>GraphMath.Ticks(axis,total).Where(t=>t.Labelled).Select(t=>t.Score).ToArray();

  // 같은 PB(998,413)와 최저점(955,260)에서 높은 8판 · 낮은 4판: 높은 그룹에 맞춘 989,000~999,000, 낮은 네 판(1·2·5·8회)은 마름모. PB 는 동점 둘 가운데 뒤의 12회
  var eight=Of(955260,963120,991840,994200,960100,995300,996100,958400,997050,998413,996800,998413);var axis=GraphMath.Axis(eight,false);
  Check(axis is {Bottom:989000,Top:999000,Cut:991840,Pb:998413,Outside:false,Compact:false}&&Lows(eight,axis).SequenceEqual(new[]{1,2,5,8})&&GraphMath.Points(eight,axis,0).Single(p=>p.Pb).Number==12,"eight high and four low plays: the axis fits the high group and the four low plays drop below it");
  Check(Labels(axis,12).SequenceEqual(new[]{990000,992000,994000,996000,998000})&&GraphMath.Ticks(axis,12).Count==11&&GraphMath.Ticks(axis,12).All(t=>t.Guide),"a 10,000 wide axis has a line every 1,000 and a number every 2,000");
  // 높은 6판 · 낮은 6판(4회와 9회를 낮은 점수로): 반반이라 가르지 않는다. 폭 45,000(954,000~999,000)에 열두 판이 모두 제 높이에 있다
  var six=Of(955260,963120,991840,961000,960100,995300,996100,958400,957300,998413,996800,998413);axis=GraphMath.Axis(six,false);
  Check(axis.Width==45000&&axis.Cut==-1&&Lows(six,axis).Length==0&&six.Dated.All(p=>p.Score>=axis.Bottom&&p.Score<=axis.Top)&&axis is {Bottom:954000,Top:999000},$"six and six: no group is split off and every play is at its own height ({axis.Bottom}~{axis.Top})");
  Check(Labels(axis,12).SequenceEqual(new[]{960000,970000,980000,990000})&&GraphMath.Ticks(axis,12).Count==9,"a 45,000 wide axis has a line every 5,000 and a number every 10,000");
  // 점수 차이 2,000 → 폭 5,000 에 100점 눈금(확대는 고르게 하지 않는다). 8,400 → 폭 10,000
  var close=Of(996413,998413,997000);axis=GraphMath.Axis(close,false);var ticks=GraphMath.Ticks(axis,3);
  Check(axis is {Bottom:994000,Top:999000,Compact:true}&&!GraphMath.ZoomOffered(axis,3)&&ticks.Count==51&&ticks.Count(t=>t.Labelled)==6&&ticks.Count(t=>t.Guide)==11&&GraphMath.Axis(close,true)==axis,"plays within 2,000: a 5,000 wide axis with 100-point ticks, and no separate zoom");
  axis=GraphMath.Axis(Of(990013,998413,995000),false);Check(axis is {Bottom:989000,Top:999000,Compact:false}&&GraphMath.ZoomOffered(axis,3),"plays within 8,400: a 10,000 wide axis");
  // 그룹의 경계: 이웃 점수의 차이 10,000 은 경계가 되고 9,999 는 안 된다. 다섯 판 미만은 가르지 않는다
  Check(GraphMath.Axis(Of(980000,990000,991000,992000,998413),false).Cut==990000&&GraphMath.Axis(Of(980001,990000,991000,992000,998413),false).Cut==-1&&GraphMath.Axis(Of(980000,990000,991000,998413),false).Cut==-1,"a gap of 10,000 can split the groups, 9,999 cannot, and fewer than five plays are never split");
  // 폭이 50,000 에 걸릴 때: PB 보다 정확히 50,000 낮은 판은 마름모다
  var wide=Of(948413,960000,975000,990000,998413);axis=GraphMath.Axis(wide,false);
  Check(axis.Cut==-1&&Lows(wide,axis).SequenceEqual(new[]{1})&&axis.Width<=50000&&Lows(Of(949413,960000,975000,990000,998413),GraphMath.Axis(Of(949413,960000,975000,990000,998413),false)).Length==0,"a play exactly 50,000 below the PB is shown below the axis; 49,000 below is not");
  // 같은 PB(998,413)에서 49,999 낮은 판은 제 높이의 원이고 정확히 50,000 낮은 판은 마름모다. 위 끝을 천 단위로 올리느라 아래 끝이 949,000 이 되면 948,414~948,999 가 밀려나므로, 그때는 축을 PB−50,000 부터 PB 까지로 잡는다
  var edge=Of(948414,998413);axis=GraphMath.Axis(edge,false);
  Check(axis is {Bottom:948413,Top:998413,Cut:-1}&&Lows(edge,axis).Length==0&&Lows(Of(948999,998413),GraphMath.Axis(Of(948999,998413),false)).Length==0&&Lows(Of(948413,998413),GraphMath.Axis(Of(948413,998413),false)).SequenceEqual(new[]{1})&&Labels(axis,2).SequenceEqual(new[]{950000,960000,970000,980000,990000}),$"49,999 below the PB is a point at its own height, exactly 50,000 below is a diamond ({axis.Bottom}~{axis.Top})");
  // PB 근처 확대: 정확히 PB−5,000 부터 PB 까지. 5,000 낮은 판은 보이고 5,001 낮은 판은 빠진다(회차는 그대로 적는다)
  var spread=Of(990000,993413,993412,998413,997000);axis=GraphMath.Axis(spread,true);var zoomed=GraphMath.Points(spread,axis,0);
  Check(axis is {Near:true,Bottom:993413,Top:998413}&&zoomed.Where(p=>p.Visible).Select(p=>p.Number).SequenceEqual(new[]{2,4,5})&&zoomed.Count==5&&zoomed.All(p=>!p.Low)&&GraphMath.Ticks(axis,5).Count(t=>t.Labelled)==5,"the zoom shows the plays within 5,000 of the PB at their real positions");
  // 일시 미상 PB 가 날짜 있는 최고보다 50,000 이상 높으면 날짜 있는 판으로 범위를 잡고 PB 는 범위 밖에 따로 적는다
  var away=new HistoryModel(normal,Of(934100,937642,936218,941800,940523).All.Append(No(9,997200,PlaySource.Collection)));axis=GraphMath.Axis(away,false);
  Check(axis is {Outside:true,Ref:941800,Pb:997200,Bottom:932000,Top:942000}&&GraphMath.PbOutside(axis)&&GraphMath.OutsideLabel(axis)=="범위 밖 PB · 997,200점"&&GraphMath.PbTip(away)=="PB · 997,200점 · 일시 미상 · 컬렉션"&&Lows(away,axis).Length==0,"an undated PB far above the plays is named outside the graph, and the plays keep their own range");
  bool Out(int high)=>GraphMath.Axis(new HistoryModel(normal,Of(high,high-1000).All.Append(No(9,997200,PlaySource.VArchive))),false).Outside;
  Check(Out(947200)&&!Out(947201)&&GraphMath.PbTip(new HistoryModel(normal,Of(947201).All.Append(No(9,997200,PlaySource.VArchive))))=="PB · 997,200점 · 일시 미상 · V아카이브 (RATE 환산)","exactly 50,000 above counts as outside; the tip names the source and says when it is converted");
  // 날짜 있는 판이 없으면 플레이 0회: 점 없이 PB 기준선만
  var none=new HistoryModel(normal,new[]{No(1,997200,PlaySource.Collection),No(2,995400,PlaySource.OldRate)});axis=GraphMath.Axis(none,false);
  Check(GraphMath.Points(none,axis,0).Count==0&&axis is {Bottom:993000,Top:998000,Outside:false}&&axis.Precise(0)&&!GraphMath.ZoomOffered(axis,0)&&GraphMath.Axis(new HistoryModel(normal,Array.Empty<Play>()),false).Pb==0,"with no dated play there is no point, only the PB line");
  // 회차: 한 화면에 열두 판, 구간을 옮겨도 번호를 다시 매기지 않는다. 열네 판의 예: 990,000~1,000,000, 8회가 마름모, PB 는 13회
  var fourteen=Of(951221,963120,993860,995421,994287,996184,995703,961490,997227,998156,999511,997894,999511,998870);axis=GraphMath.Axis(fourteen,false);var window=GraphMath.Points(fourteen,axis,GraphMath.Clamp(99,14));
  Check(axis is {Bottom:990000,Top:1000000}&&window.Select(p=>p.Number).SequenceEqual(Enumerable.Range(3,12))&&window.Where(p=>p.Low).Select(p=>p.Number).SequenceEqual(new[]{8})&&window.Single(p=>p.Pb).Number==13&&GraphMath.Clamp(-5,14)==0&&GraphMath.Clamp(3,5)==0&&GraphMath.Points(fourteen,axis,0).Select(p=>p.Number).SequenceEqual(Enumerable.Range(1,12)),"twelve plays show at a time and keep their numbers when the window moves");
  Check(GraphMath.PointTip(window.Single(p=>p.Pb))==$"13회 · {ScoreText.Time(fourteen.Pb!)} · 999,511점 · PB"&&GraphMath.PbTip(fourteen)==$"PB · 999,511점 · {ScoreText.Time(fourteen.Pb!)}","tips name the play number, time and score");
 }
 public static void Run()
 {
  var notes=new List<string>();void Check(bool value,string name){if(!value)throw new Exception(name);notes.Add("PASS: "+name);}
  const string normal="song|8|SC|노말",hard="song|8|SC|하드";var t=new DateTime(2026,11,20,10,0,0,DateTimeKind.Utc);
  Play At(int seq,int score,int minute,string key=normal,string source=PlaySource.Play,string? link=null)=>new(seq,key,score,t.AddMinutes(minute),source,link);
  Play No(int seq,int score,string source)=>new(seq,normal,score,null,source);

  // 1. 글로 적는 규칙
  Check(ScoreText.Full(At(1,995421,0))=="995,421점 (99.54%)"&&ScoreText.Full(No(1,995400,PlaySource.VArchive))=="995,400점 (99.54% · 환산)"&&ScoreText.Full(No(1,995400,PlaySource.OldRate))=="995,400점 (99.54% · 환산)"&&ScoreText.Full(No(1,997201,PlaySource.Collection))=="997,201점 (99.72%)","a RATE-only record is written as a converted score where scores are compared");
  Check(ScoreText.Kept(At(1,995421,0))=="995,421점 (99.54%)"&&ScoreText.Kept(No(1,995400,PlaySource.VArchive))=="99.54%"&&ScoreText.Kept(No(1,995400,PlaySource.OldRate))=="99.54%","where the real value is confirmed, a RATE-only record shows only its RATE");
  Check(ScoreText.Time(At(1,1,0))==t.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")&&ScoreText.Time(At(1,1,0,source:PlaySource.Old)).EndsWith(" (저장 시각)")&&ScoreText.Time(No(1,1,PlaySource.VArchive))=="일시 미상"&&ScoreText.TimeName(At(1,1,0,source:PlaySource.Old))=="저장 시각"&&ScoreText.TimeName(At(1,1,0))=="플레이 시각","an old best record carries the time it was saved, named as such");
  Check(PlaySource.Label(PlaySource.VArchive)=="V아카이브"&&PlaySource.Label(PlaySource.Collection)=="컬렉션"&&PlaySource.Label(PlaySource.OldRate)==PlaySource.FirstVersion+" 이전 기록 (RATE 환산)"&&PlaySource.FirstVersion.StartsWith("v"),"sources of records with no play time");
  Check(ScoreText.Source(No(1,995400,PlaySource.VArchive))=="V아카이브 (RATE 환산)"&&ScoreText.Source(No(1,995400,PlaySource.OldRate))==PlaySource.FirstVersion+" 이전 기록 (RATE 환산)"&&ScoreText.Source(No(1,995400,PlaySource.Collection))=="컬렉션","under the graph a converted record says so beside its source; an exact one does not");

  // 2. PB 한 판: 990,000 → 998,000 → 995,000 → 998,000 → 997,000 이면 네 번째 판. 그 판이 없어지면 두 번째 판
  var five=new[]{At(1,990000,0),At(2,998000,1),At(3,995000,2),At(4,998000,3),At(5,997000,4)};var model=new HistoryModel(normal,five);
  Check(model.Pb!.Seq==4&&model.Best!.Seq==4&&model.Dated.Select(p=>p.Seq).SequenceEqual(new[]{1,2,3,4,5}),"with two plays at the best score the later one is the PB; plays run oldest first for the graph");
  var rows=model.Rows();
  Check(rows.Select(r=>r.Play.Seq).SequenceEqual(new[]{5,4,3,2,1})&&rows.Where(r=>r.Tie).Select(r=>r.Play.Seq).SequenceEqual(new[]{4,2})&&rows.Single(r=>r.Pb).Play.Seq==4,"the table opens latest first; both plays at the best score are marked, one carries PB");
  Check(new HistoryModel(normal,five.Where(p=>p.Seq!=4)).Pb!.Seq==2,"without that play the PB moves to the earlier one");
  Check(model.Rows(true).Select(r=>r.Play.Seq).SequenceEqual(new[]{4,2,5,3,1})&&model.Rows(true,false).Select(r=>r.Play.Seq).SequenceEqual(new[]{1,3,5,4,2})&&model.Rows(false,false).Select(r=>r.Play.Seq).SequenceEqual(new[]{1,2,3,4,5})&&model.Rows(true).Single(r=>r.Pb).Play.Seq==4,"by score the later play comes first among equals; sorting does not move the PB");
  // 시각까지 같으면 나중에 등록한 판
  Check(new HistoryModel(normal,new[]{At(7,998000,1),At(9,998000,1),At(8,998000,1)}).Pb!.Seq==9,"at the same time the play registered later is the PB");

  // 3. 일시 미상: 최고 SCORE 가 일시 미상에만 있으면 표 맨 아래에 'PB · 일시 미상' 한 줄. 같은 SCORE 는 한 줄에 출처마다 하나
  var mixed=new[]{At(1,995000,0),At(2,994000,1),No(3,997200,PlaySource.VArchive),No(4,997200,PlaySource.Collection),No(5,995400,PlaySource.OldRate)};model=new(normal,mixed);
  Check(model.Best!.Score==997200&&model.Pb==null&&model.Dated.Count==2,"when the best score has no play time no play carries the PB");
  Check(model.Undated.Select(g=>g.Score).SequenceEqual(new[]{997200,995400})&&model.Undated[0].Plays.Select(p=>p.Source).SequenceEqual(new[]{PlaySource.Collection,PlaySource.VArchive})&&model.Undated[1].Plays.Single().Seq==5,"records with no play time are grouped by score, highest first");
  rows=model.Rows();
  Check(rows.Count==3&&rows[0].Play.Seq==2&&rows[^1].Group!.Count==2&&rows[^1].Pb&&rows[^1].Tie&&!rows[^1].Editable&&rows.Take(2).All(r=>!r.Tie&&!r.Pb),"the table shows the dated plays and one PB line for the undated best");
  Check(model.Rows(true)[^1].Group!=null&&model.Rows(true,false)[^1].Group!=null&&model.Rows(false,false)[^1].Group!=null,"that line stays at the foot however the table is sorted");
  // 날짜 있는 판이 같은 최고 SCORE 에 닿으면 PB 는 그 판이고, 표에 따로 줄을 두지 않는다
  model=new(normal,mixed.Append(At(6,997200,5)));
  Check(model.Pb!.Seq==6&&model.Rows().All(r=>r.Group==null)&&model.Rows().Single(r=>r.Pb).Play.Seq==6&&model.Undated.Count==2,"a dated play at the same best score takes the PB; the undated records stay under the graph");
  // 모든 기록이 일시 미상
  model=new(normal,new[]{No(1,995400,PlaySource.VArchive)});
  Check(model.Dated.Count==0&&model.Rows().Single().Group!.Single().Seq==1&&model.Undated.Single().Score==995400,"with only undated records: zero plays, one PB line, and the record below");
  Check(new HistoryModel(normal,Array.Empty<Play>()) is {Best:null,Pb:null} empty&&empty.Rows().Count==0&&empty.Undated.Count==0,"no record at all");

  // 4. 노말에 함께 보이는 하드의 판, 고칠 수 있는 판
  var shown=new[]{At(1,990000,0),At(2,998000,1,hard,PlaySource.Manual,NormalLink.Linked),At(3,980000,2,source:PlaySource.Old),No(4,970000,PlaySource.VArchive)};model=new(normal,shown);rows=model.Rows();
  Check(rows.Single(r=>r.Play.Seq==2) is {Reflected:true,Editable:false,Pb:true}&&ScoreText.Reflected(shown[1])=="하드판정에서 반영"&&rows.Single(r=>r.Play.Seq==1) is {Reflected:false,Editable:true}&&rows.Single(r=>r.Play.Seq==3).Editable&&!model.Editable(shown[3]),"a hard play shown on normal is not edited there; direct plays and old exact records are; imported records are not");
  Check(new HistoryModel(hard,new[]{shown[1]}).Rows().Single() is {Reflected:false,Editable:true},"the same play is edited in its own judge's history");

  // 5. 지우면 비게 되는 판정(삭제 확인의 '미등록이 됩니다')
  string root=Path.Combine(AppContext.BaseDirectory,"test-data","history-view-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  try
  {
   var store=new RecordStore(Path.Combine(root,"records.json"));var hardChart=new Selection("song",8,"SC","하드",0);var normalChart=hardChart with{Judge="노말"};
   var linked=store.Add(hardChart,998000,t);
   Check(HistoryModel.Emptied(store,linked,false).SequenceEqual(new[]{"하드","노말"})&&HistoryModel.Emptied(store,linked,true).SequenceEqual(new[]{"노말"}),"deleting the only hard play empties both judges; removing only its reflection empties normal");
   store.Add(normalChart,990000,t.AddMinutes(1));var second=store.Add(hardChart,997000,t.AddMinutes(2));
   Check(HistoryModel.Emptied(store,linked,false).Count==0&&HistoryModel.Emptied(store,linked,true).Count==0&&HistoryModel.Emptied(store,second,false).Count==0,"with other plays left nothing becomes empty");
   var read=HistoryModel.Read(store,normalChart.Key);
   Check(read.All.Count==2&&read.Pb!.Seq==linked.Seq&&read.Reflected(read.Pb)&&read.Dated.Count==2,"read from the store, normal shows its own play and the hard play that carried over");
  }
  finally{try{Directory.Delete(root,true);}catch(IOException){}catch(UnauthorizedAccessException){}}
  Graph(Check,At,No);
  File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"history-view-validation.txt"),string.Join("\n",notes)+"\n");
 }
}
