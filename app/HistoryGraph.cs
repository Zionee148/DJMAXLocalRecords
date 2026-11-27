using System.Drawing.Drawing2D;
namespace LadderRecords;

// 기록 히스토리의 점수 그래프.
// 범위·그룹·눈금의 셈(GraphMath)은 그리는 부품(HistoryGraph)과 떼어 두어 자체 검사가 예시 기록으로 돌린다(app\HistoryViewTests.cs).
// 가로는 플레이 회차다(오래된 판부터 1회, 2회 …. 시각이 있는 판만). 세로는 SCORE 다. 일시 미상 기록은 점을 찍지 않는다(그래프 아래의 줄에서 본다).

/// <summary>세로축. Pb 는 최고 SCORE(일시 미상 포함), Ref 는 범위를 잡는 기준(보통은 Pb. 일시 미상 PB 가 날짜 있는 최고보다 50,000 이상 높으면 날짜 있는 최고), Cut 은 낮은 그룹을 가르는 점수(없으면 -1)</summary>
record GraphAxis(int Pb,int Ref,bool Outside,bool Near,bool Compact,int Cut,int Bottom,int Top)
{
 public int Width=>Top-Bottom;
 /// <summary>100점 눈금을 쓰는가: 모든 판이 PB 에서 5,000 안이거나, PB 근처 확대이거나, 날짜 있는 판이 없을 때</summary>
 public bool Precise(int total)=>Compact||Near||total==0;
}
/// <summary>가로 눈금 하나. Labelled 면 숫자를 적고, Guide 면 그래프를 가로지르는 선이다(아니면 축에 붙은 짧은 눈금)</summary>
record GraphTick(int Score,bool Labelled,bool Guide);
/// <summary>보이는 구간의 한 판. Low 면 축 아래에 마름모와 실제 SCORE 로 그린다. Visible 이 아니면(확대에서 PB 와 5,000 넘게 차이) 회차만 적고 점은 없다</summary>
record GraphPoint(Play Play,int Number,bool Low,bool Visible,bool Pb);

static class GraphMath
{
 /// <summary>한 화면에 보이는 회차의 수</summary>
 public const int Span=12;
 public static GraphAxis Axis(HistoryModel model,bool zoom)
 {
  int pb=model.Best?.Score??0;var scores=model.Dated.Select(p=>p.Score).OrderBy(s=>s).ToArray();int high=scores.Length>0?scores[^1]:pb;
  bool outside=scores.Length>0&&pb-high>=50000;int reference=outside?high:pb;
  // 낮은 그룹: 날짜 있는 판이 다섯 이상이고, 점수순으로 이웃한 두 판이 10,000 이상 벌어졌고, 그 위의 판이 절반을 넘고, 위쪽이 모두 기준에서 10,000 안일 때 그 아래를 따로 그린다
  int cut=-1;
  if(scores.Length>=5)for(int i=1;i<scores.Length;i++)if(scores[i]-scores[i-1]>=10000&&scores.Length-i>scores.Length/2.0&&reference-scores[i]<=10000){cut=scores[i];break;}
  int lowest=reference;foreach(int score in scores)if(score>=cut&&reference-score<50000)lowest=Math.Min(lowest,score);
  bool compact=scores.Length>0&&pb-scores[0]<=5000;
  int upper=Math.Min(1000000,(int)Math.Ceiling(reference/1000.0)*1000);
  int width=Math.Min(50000,Math.Max(5000,(int)Math.Ceiling((upper-lowest)/5000.0)*5000));
  // PB 근처 확대는 정확히 PB−5,000 부터 PB 까지다
  if(zoom&&!compact&&scores.Length>0)return new(pb,reference,outside,true,compact,cut,Math.Max(0,pb-5000),Math.Max(5000,pb));
  int floor=Math.Max(0,Math.Min(upper-width,lowest/1000*1000));
  int bottom=Math.Max(0,Math.Min(upper-width,Math.Max(upper-50000,floor)));
  // 폭이 50,000 에 걸렸을 때: 위 끝을 천 단위로 올린 만큼 아래 끝도 올라가, 기준보다 50,000 미만 낮은 판이 축 아래로 밀려날 수 있다(기준 998,413 이면 948,414~948,999).
  // 그때만 축을 '기준−50,000 ~ 기준'으로 잡는다(눈금의 숫자는 그대로 천 단위다). 그러면 50,000 미만 낮은 판은 제 높이에 보이고, 정확히 50,000 낮은 판부터 축 아래의 마름모가 된다
  if(lowest<bottom){bottom=Math.Max(0,reference-50000);return new(pb,reference,outside,false,compact,cut,bottom,bottom+50000);}
  return new(pb,reference,outside,false,compact,cut,bottom,Math.Max(width,bottom+width));
 }
 public static List<GraphTick> Ticks(GraphAxis axis,int total)
 {
  bool precise=axis.Precise(total);int step=precise?100:axis.Width<=10000?1000:5000,major=precise?1000:axis.Width==10000?2000:axis.Width>=45000?10000:step;
  var ticks=new List<GraphTick>();
  for(int score=(axis.Bottom+step-1)/step*step;score<=axis.Top;score+=step)ticks.Add(new(score,score%major==0,!precise||score%500==0));
  return ticks;
 }
 /// <summary>start 번째(0부터)부터 한 화면의 판들. 회차의 번호는 구간을 옮겨도 다시 매기지 않는다</summary>
 public static List<GraphPoint> Points(HistoryModel model,GraphAxis axis,int start,int span=Span)
 {
  var points=new List<GraphPoint>();int end=Math.Min(model.Dated.Count,start+span);
  for(int i=Math.Max(0,start);i<end;i++)
  {
   var play=model.Dated[i];bool low=axis.Ref-play.Score>=50000||play.Score<axis.Cut||play.Score<axis.Bottom;
   points.Add(new(play,i+1,low&&!axis.Near,!axis.Near||axis.Pb-play.Score<=5000,model.Pb!=null&&play.Seq==model.Pb.Seq));
  }
  return points;
 }
 public static int Clamp(int start,int total,int span=Span)=>Math.Max(0,Math.Min(start,total-span));
 /// <summary>전체 / PB 근처 확대를 고르게 할 것인가(모든 판이 이미 5,000 안이면 같은 그림이라 고르게 하지 않는다)</summary>
 public static bool ZoomOffered(GraphAxis axis,int total)=>!axis.Compact&&total>0;
 /// <summary>PB 가 그래프의 범위 밖에 따로 적히는가(일시 미상 PB 가 날짜 있는 최고보다 50,000 이상 높고, 확대가 아닐 때)</summary>
 public static bool PbOutside(GraphAxis axis)=>axis.Outside&&!axis.Near;
 public static string PointTip(GraphPoint point)=>$"{point.Number}회 · {ScoreText.Time(point.Play)} · {ScoreText.Points(point.Play.Score)}{(point.Pb?" · PB":"")}";
 /// <summary>PB 기준선(또는 '범위 밖 PB')에 마우스를 올렸을 때의 글. 일시 미상 PB 는 출처를 모두 적는다(환산이면 환산이라고)</summary>
 public static string PbTip(HistoryModel model)
 {
  if(model.Best==null)return "";
  if(model.Pb!=null)return $"PB · {ScoreText.Points(model.Best.Score)} · {ScoreText.Time(model.Pb)}";
  var sources=model.Undated.FirstOrDefault(g=>g.Score==model.Best.Score)?.Plays.Select(ScoreText.Source)??Enumerable.Empty<string>();
  return $"PB · {ScoreText.Points(model.Best.Score)} · 일시 미상 · {string.Join(", ",sources)}";
 }
 public static string OutsideLabel(GraphAxis axis)=>$"범위 밖 PB · {ScoreText.Points(axis.Pb)}";
}

/// <summary>'점수 그래프' 제목과 접기 화살표. 누르면 그래프를 접고 편다</summary>
sealed class GraphFold:Control
{
 bool expanded=true,hot;
 public event Action? Toggled;
 [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
 public bool Expanded{get=>expanded;set{if(expanded==value)return;expanded=value;AccessibleDescription=value?"펼침":"접힘";Invalidate();}}
 public GraphFold()
 {
  SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.Selectable,true);
  Size=new(120,25);Cursor=Cursors.Hand;AccessibleName="점수 그래프";AccessibleRole=AccessibleRole.PushButton;AccessibleDescription="펼침";TabStop=true;
 }
 protected override void OnPaint(PaintEventArgs e)
 {
  var g=e.Graphics;g.Clear(Parent?.BackColor??Theme.Window);float middle=Height/2f;
  float x=Ink.Draw(g,new[]{new InkPart("점수 그래프",Ink.Head(15f),hot?Theme.Accent:Theme.Text)},0,Ink.Baseline(middle,Ink.Head(15f)));
  Ink.Arrow(g,x+Theme.S(8),middle,!expanded,Theme.Stripe);   // 펼쳤을 때 ▴, 접었을 때 ▾
  if(Focused&&ShowFocusCues){using var pen=new Pen(Theme.Accent);g.DrawRectangle(pen,0,0,Width-1,Height-1);}
 }
 /// <summary>접거나 편다(누른 것과 같다)</summary>
 internal void Flip(){Expanded=!expanded;Toggled?.Invoke();}
 protected override void OnMouseEnter(EventArgs e){base.OnMouseEnter(e);hot=true;Invalidate();}
 protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hot=false;Invalidate();}
 protected override void OnClick(EventArgs e){base.OnClick(e);Focus();Flip();}
 protected override bool IsInputKey(Keys key)=>key is Keys.Enter or Keys.Space||base.IsInputKey(key);
 protected override void OnKeyDown(KeyEventArgs e){base.OnKeyDown(e);if(e.KeyCode is Keys.Enter or Keys.Space){Flip();e.Handled=true;}}
 protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);Invalidate();}
 protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);Invalidate();}
}

/// <summary>
/// 그래프를 그리고 조작을 받는다: 점을 누르면 그 판을 고르고(표가 그 줄로 간다), 점이나 PB 기준선에 마우스를 올리면 안내가 뜨고, 아래의 막대로 앞선 회차를 본다.
/// 표의 정렬과 그래프의 차례는 따로다. 표에서 고른 판은 그래프에서 점선 원으로 보인다
/// </summary>
sealed class HistoryGraph:Control
{
 HistoryModel model=new("",Array.Empty<Play>());GraphAxis axis;List<GraphPoint> points=new();
 int start;int? picked;bool zoom,line=true;
 // 마우스 아래의 것: 점(points 의 번호), PB 기준선, '범위 밖 PB'. 안내의 글과 자리
 int hotPoint=-1;bool hotLine,hotOutside;string? tip;Point tipAt;bool dragging;int dragOffset;
 public event Action<Play>? Chosen;
 public HistoryGraph()
 {
  SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.Selectable,true);
  axis=GraphMath.Axis(model,false);TabStop=true;AccessibleName="회차별 SCORE";AccessibleRole=AccessibleRole.Chart;
 }
 public GraphAxis Axis=>axis;
 public IReadOnlyList<GraphPoint> Points=>points;
 public int Total=>model.Dated.Count;
 /// <summary>보이는 구간의 첫 회차(0부터)</summary>
 [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
 public int Start{get=>start;set{int next=GraphMath.Clamp(value,Total);if(next==start)return;start=next;Recount();}}
 [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
 public bool Zoom{get=>axis.Near;set{if(zoom==value)return;zoom=value;Recount();}}
 public bool ZoomOffered=>GraphMath.ZoomOffered(axis,Total);
 [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
 public bool PbLine{get=>line;set{if(line==value)return;line=value;Invalidate();}}
 /// <summary>지금 떠 있는 안내의 글(없으면 null)</summary>
 internal string? TipText=>tip;
 /// <summary>
 /// 보일 것을 바꾼다. 다른 판정이면(fresh) 가장 최근 구간과 전체 그래프로 시작한다.
 /// 같은 판정을 다시 읽은 것이면 보던 구간과 확대를 지킨다. 맨 끝을 보고 있었으면 새로 저장된 판이 보이게 끝을 따라간다
 /// </summary>
 public void Display(HistoryModel shown,int? pickedSeq,bool fresh)
 {
  bool atEnd=start+GraphMath.Span>=Total;model=shown;picked=pickedSeq;
  if(fresh){zoom=false;start=Math.Max(0,Total-GraphMath.Span);}
  else start=atEnd?Math.Max(0,Total-GraphMath.Span):GraphMath.Clamp(start,Total);
  Recount();
 }
 public void Pick(int? seq){if(picked==seq)return;picked=seq;Invalidate();}
 void Recount(){axis=GraphMath.Axis(model,zoom);points=GraphMath.Points(model,axis,start);Unhover();Invalidate();}
 void Unhover(){hotPoint=-1;hotLine=hotOutside=false;tip=null;}

 // ── 자리(창의 크기 단계만큼 큰 값). 그래프 영역은 높이 163(100% 때의 값)이고 그 위에 한 줄(SCORE / 플레이 회차), 아래에 회차 막대가 있다
 static float U(float px)=>Theme.S(px);
 RectangleF Plot=>new(U(10),U(27),Width-U(20),U(163));
 float AxisLeft=>Plot.Left+U(57);
 float AxisRight=>Plot.Right-U(32);
 float AxisTop=>Plot.Top+U(13);
 float AxisBase=>Plot.Top+U(119);
 float Under=>Plot.Top+U(132);
 float Y(int score)=>AxisBase-(score-axis.Bottom)/(float)Math.Max(1,axis.Width)*(AxisBase-AxisTop);
 float X(int index){int count=points.Count;return count<=1?(AxisLeft+AxisRight)/2:AxisLeft+U(9)+(AxisRight-AxisLeft-U(18))*index/(count-1);}
 PointF Spot(int index)=>new(X(index),points[index].Low?Under:Y(points[index].Play.Score));
 bool Scrollable=>Total>GraphMath.Span;
 string Range=>$"{start+1}–{Math.Min(Total,start+GraphMath.Span)} / {Total}회";
 // 회차 막대: 왼쪽에 가는 선과 손잡이, 오른쪽에 '3–14 / 14회'
 (RectangleF Track,RectangleF Thumb) Bar()
 {
  float wide=Ink.Width(new[]{new InkPart(Range,Ink.Number(11f,false),Theme.Dim)}),y=Plot.Bottom+U(9);
  var track=new RectangleF(Plot.Left+U(2),y-U(2),Math.Max(U(40),Plot.Width-wide-U(13)),U(4));
  float thumb=U(24),room=track.Width-thumb,at=Total-GraphMath.Span<=0?0:room*start/(Total-GraphMath.Span);
  return(track,new RectangleF(track.Left+at,y-U(4),thumb,U(8)));
 }
 RectangleF OutsideArea(){var words=new[]{new InkPart(GraphMath.OutsideLabel(axis),Ink.Semi(12f),Theme.Pb)};float wide=Ink.Width(words);return new(Plot.Right-wide-U(3),U(7),wide+U(6),U(20));}

 protected override void OnPaint(PaintEventArgs e)
 {
  var g=e.Graphics;g.Clear(Parent?.BackColor??Theme.Window);g.SmoothingMode=SmoothingMode.AntiAlias;
  using(var shape=BadgeDesign.Rounded(new RectangleF(0,0,Width-1,Height-1),U(4))){using var back=new SolidBrush(Theme.Table);g.FillPath(back,shape);using var edge=new Pen(Focused&&ShowFocusCues?Theme.Accent:Theme.Line);g.DrawPath(edge,shape);}
  var small=Ink.Number(11f,false);var caption=Ink.Semi(11f);float captionLine=Ink.Baseline(U(17),caption);
  Ink.Draw(g,new[]{new InkPart("SCORE",caption,Theme.Dim)},Plot.Left,captionLine);
  bool outside=GraphMath.PbOutside(axis)&&model.Best!=null;
  if(outside)Ink.DrawRight(g,new[]{new InkPart(GraphMath.OutsideLabel(axis),Ink.Semi(12f),Theme.Pb)},Plot.Right,Ink.Baseline(U(17),Ink.Semi(12f)));
  else Ink.DrawRight(g,new[]{new InkPart("플레이 회차",caption,Theme.Dim)},Plot.Right,captionLine);
  void Centered(string text,float x,float baseline,Color color){var words=new[]{new InkPart(text,small,color)};Ink.Draw(g,words,x-Ink.Width(words)/2,baseline);}
  if(model.Best==null){Centered("등록한 기록이 없습니다.",Plot.Left+Plot.Width/2,Plot.Top+U(70),Theme.Dim);return;}
  // 눈금
  using(var grid=new Pen(Theme.GraphGrid))using(var minor=new Pen(Theme.RowLine))
   foreach(var tick in GraphMath.Ticks(axis,Total))
   {
    float y=(float)Math.Round(Y(tick.Score));
    g.SmoothingMode=SmoothingMode.None;g.DrawLine(tick.Labelled?grid:minor,tick.Guide?AxisLeft:AxisLeft-U(4),y,tick.Guide?AxisRight:AxisLeft,y);g.SmoothingMode=SmoothingMode.AntiAlias;
    if(tick.Labelled)Ink.DrawRight(g,new[]{new InkPart($"{tick.Score:N0}",small,Theme.Dim)},AxisLeft-U(6),y+U(4));
   }
  // PB 기준선: 실제 PB 의 높이에 파란 점선
  if(line&&!outside){float y=Y(axis.Pb);using var pen=new Pen(Theme.Pb,U(1.4f)){DashPattern=new[]{5f/1.4f,4f/1.4f}};g.DrawLine(pen,AxisLeft,y,AxisRight,y);}
  if(Total==0){Centered("플레이 시각을 확인할 수 있는 기록이 없습니다.",(AxisLeft+AxisRight)/2,Plot.Top+U(94),Theme.Dim);DrawTip(g);return;}
  // 플레이의 선: 보이는 점끼리 차례로 잇는다. 축 아래로 내린 판과 이어지는 선은 점선이다
  var shown=Enumerable.Range(0,points.Count).Where(i=>points[i].Visible).ToArray();
  using(var solid=new Pen(Theme.GraphTrace,U(1.8f)))using(var dashed=new Pen(Theme.GraphTrace,U(1.8f)){DashPattern=new[]{3f/1.8f,4f/1.8f}})
   for(int i=1;i<shown.Length;i++){var from=Spot(shown[i-1]);var to=Spot(shown[i]);g.DrawLine(points[shown[i-1]].Low||points[shown[i]].Low?dashed:solid,from,to);}
  for(int i=0;i<points.Count;i++)
  {
   var point=points[i];var at=Spot(i);
   Centered($"{point.Number}회",at.X,Plot.Top+U(160),Theme.Dim);
   if(!point.Visible)continue;
   if(point.Low)
   {
    float r=U(4.5f);using(var fill=new SolidBrush(Theme.GraphTrace))g.FillPolygon(fill,new[]{new PointF(at.X,at.Y-r),new PointF(at.X+r,at.Y),new PointF(at.X,at.Y+r),new PointF(at.X-r,at.Y)});
    Centered($"{point.Play.Score:N0}",at.X,Plot.Top+U(146),Theme.Dim);
   }
   else if(point.Pb)
   {
    // PB 한 판: 파란 겹원
    float r=U(6);using(var fill=new SolidBrush(Theme.Table))g.FillEllipse(fill,at.X-r,at.Y-r,r*2,r*2);using(var pen=new Pen(Theme.Pb,U(2)))g.DrawEllipse(pen,at.X-r,at.Y-r,r*2,r*2);
    float inner=U(2.5f);using var dot=new SolidBrush(Theme.Pb);g.FillEllipse(dot,at.X-inner,at.Y-inner,inner*2,inner*2);
   }
   else{float r=U(3.8f);using(var fill=new SolidBrush(Theme.Table))g.FillEllipse(fill,at.X-r,at.Y-r,r*2,r*2);using var pen=new Pen(Theme.GraphTrace,U(2));g.DrawEllipse(pen,at.X-r,at.Y-r,r*2,r*2);}
   // 고른 판: 테마 강조색의 점선 원
   if(point.Play.Seq==picked){float r=U(9);using var pen=new Pen(Theme.Stripe,U(1.7f)){DashPattern=new[]{2f/1.7f,2f/1.7f}};g.DrawEllipse(pen,at.X-r,at.Y-r,r*2,r*2);}
  }
  if(shown.Length==0)Centered("이 구간에는 PB 근처의 플레이가 없습니다.",(AxisLeft+AxisRight)/2,Plot.Top+U(82),Theme.Dim);
  if(Scrollable)
  {
   var(track,thumb)=Bar();
   using(var rail=BadgeDesign.Rounded(track,U(2)))using(var fill=new SolidBrush(Theme.Line))g.FillPath(fill,rail);
   using(var knob=BadgeDesign.Rounded(thumb,U(4)))using(var fill=new SolidBrush(dragging?Theme.Accent:Theme.Dim))g.FillPath(fill,knob);
   Ink.DrawRight(g,new[]{new InkPart(Range,small,Theme.Dim)},Plot.Right-U(2),thumb.Top+thumb.Height/2+U(4));
  }
  DrawTip(g);
 }
 // 마우스 아래의 안내: 마우스 바로 위에(자리가 모자라면 아래에), 그래프 밖으로 나가지 않게
 void DrawTip(Graphics g)
 {
  if(tip==null)return;var words=new[]{new InkPart(tip,Ink.Semi(12f),Theme.Text)};
  float wide=Ink.Width(words)+U(16),tall=U(26),x=Math.Max(U(2),Math.Min(tipAt.X-wide/2,Width-wide-U(2))),above=tipAt.Y-tall-U(8);
  float y=above>0?above:Math.Min(Height-tall-U(1),tipAt.Y+U(12));var box=new RectangleF(x,y,wide,tall);
  using(var shape=BadgeDesign.Rounded(box,U(4))){using var back=new SolidBrush(Theme.Field);g.FillPath(back,shape);using var edge=new Pen(Theme.Line);g.DrawPath(edge,shape);}
  Ink.Draw(g,words,box.Left+U(8),Ink.Baseline(box.Top+box.Height/2,Ink.Semi(12f)));
 }

 // ── 마우스와 키보드
 /// <summary>그 자리의 점(points 의 번호. 없으면 -1). 점 둘레 9px 안이면 그 점이다</summary>
 internal int PointAt(PointF where)
 {
  for(int i=0;i<points.Count;i++){if(!points[i].Visible)continue;var at=Spot(i);float dx=where.X-at.X,dy=where.Y-at.Y;if(dx*dx+dy*dy<=U(9)*U(9))return i;}
  return -1;
 }
 /// <summary>그 점이 그려진 자리(개발용 검사가 점을 누를 때 쓴다)</summary>
 internal Point SpotOf(int index)=>Point.Round(Spot(index));
 /// <summary>PB 기준선 위의, 점이 없는 자리(개발용 검사가 마우스를 올릴 때 쓴다)</summary>
 internal Point LineSpot()=>new((int)(points.Count>=2?(X(0)+X(1))/2:(AxisLeft+AxisRight)/2+Theme.S(40)),(int)Math.Round(Y(axis.Pb)));
 bool OnPbLine(PointF where)=>line&&model.Best!=null&&!GraphMath.PbOutside(axis)&&where.X>=AxisLeft&&where.X<=AxisRight&&Math.Abs(where.Y-Y(axis.Pb))<=U(6);
 /// <summary>마우스가 그 자리에 왔다: 안내를 정한다</summary>
 internal void Hover(Point where)
 {
  int point=PointAt(where);bool outside=point<0&&GraphMath.PbOutside(axis)&&model.Best!=null&&OutsideArea().Contains(where),onLine=point<0&&!outside&&OnPbLine(where);
  string? text=point>=0?GraphMath.PointTip(points[point]):outside||onLine?GraphMath.PbTip(model):null;
  if(point==hotPoint&&onLine==hotLine&&outside==hotOutside&&text==tip&&(text==null||where==tipAt))return;
  hotPoint=point;hotLine=onLine;hotOutside=outside;tip=text;tipAt=where;Cursor=point>=0?Cursors.Hand:Cursors.Default;Invalidate();
 }
 /// <summary>그 자리를 눌렀다: 점이면 그 판을 고른다</summary>
 internal bool Press(Point where)
 {
  int point=PointAt(where);if(point<0)return false;
  picked=points[point].Play.Seq;Invalidate();Chosen?.Invoke(points[point].Play);return true;
 }
 protected override void OnMouseMove(MouseEventArgs e)
 {
  base.OnMouseMove(e);
  if(dragging){var(track,thumb)=Bar();float room=track.Width-thumb.Width;if(room>0)Start=(int)Math.Round((e.X-dragOffset-track.Left)/room*(Total-GraphMath.Span));return;}
  Hover(e.Location);
 }
 protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);if(tip!=null||hotPoint>=0){Unhover();Invalidate();}}
 protected override void OnMouseDown(MouseEventArgs e)
 {
  base.OnMouseDown(e);Focus();if(e.Button!=MouseButtons.Left)return;
  if(Scrollable)
  {
   var(track,thumb)=Bar();var reach=RectangleF.Inflate(track,0,U(7));
   if(RectangleF.Inflate(thumb,U(2),U(5)).Contains(e.Location)){dragging=true;dragOffset=(int)(e.X-thumb.Left);Capture=true;Invalidate();return;}
   if(reach.Contains(e.Location)){Start+=e.X<thumb.Left?-GraphMath.Span:GraphMath.Span;return;}   // 손잡이 옆을 누르면 한 화면씩
  }
  Press(e.Location);
 }
 protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);if(dragging){dragging=false;Capture=false;Invalidate();}}
 protected override void OnMouseWheel(MouseEventArgs e){base.OnMouseWheel(e);if(Scrollable)Start+=e.Delta>0?-1:1;}
 protected override bool IsInputKey(Keys key)=>key is Keys.Left or Keys.Right or Keys.Home or Keys.End||base.IsInputKey(key);
 protected override void OnKeyDown(KeyEventArgs e){base.OnKeyDown(e);if(Key(e.KeyCode))e.Handled=true;}
 /// <summary>키 하나를 받는다: 왼쪽·오른쪽으로 한 회차씩, Home·End 로 처음과 끝(받았으면 true)</summary>
 internal bool Key(Keys key)
 {
  if(!Scrollable)return false;
  switch(key){case Keys.Left:Start--;return true;case Keys.Right:Start++;return true;case Keys.Home:Start=0;return true;case Keys.End:Start=Total;return true;default:return false;}
 }
 protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);Invalidate();}
 protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);Invalidate();}
}
