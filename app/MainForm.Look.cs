namespace LadderRecords;

// 주 화면의 오른쪽 칸에 보이는 곡 정보(곡 이름, 꼬리표, 목표·현재)와 라이트·다크 전환.
partial class MainForm
{
 // 곡 이름이 길면 한 줄로 두고 끝을 "..."으로 줄인다(사용자 결정, 2026-11-15). 줄어든 이름은 마우스를 올리면 전체가 보인다(Label 의 기본 동작)
 readonly Label songTitle=new(){AutoSize=false,Width=480,Height=32,AutoEllipsis=true,Text="—",Margin=new(0,4,0,4),UseMnemonic=false,Font=Theme.Px("맑은 고딕",15f,FontStyle.Bold)};
 readonly Chip buttonChip=new(),patternChip=new(),judgeChip=new(),stateChip=new(){Outline=true,Font=Theme.Px("맑은 고딕",9f)};
 readonly Label goalValue=new(){AutoSize=false,Size=new(124,32),Text="—",Margin=Padding.Empty,Font=Theme.Px("맑은 고딕",16.5f,FontStyle.Bold)};
 readonly Label currentValue=new(){AutoSize=false,Size=new(124,32),Text="—",Margin=Padding.Empty,Font=Theme.Px("맑은 고딕",16.5f,FontStyle.Bold)};
 readonly ModeSwitch modeSwitch=new();
 Control? scrollArea;   // 주 화면 전체를 담고 스크롤되는 판. 미리 보기가 화면 밖으로 밀렸는지 볼 때 쓴다

 // 같은 이름의 곡이 있으면 아티스트를 괄호로 붙인다(오버레이와 같은 규칙)
 void ShowSong(Song song,string button,string pattern,string judge)
 {
  songTitle.Text=OverlayText.Title(catalog,song);
  buttonChip.Set(button);patternChip.Set(pattern);judgeChip.Set(judge);
 }
 // "현재"는 목표 이상이면 연두, 미만이면 빨강. 견줄 것이 없으면 보통 글자색
 void ShowStats(decimal? goal,decimal? current,bool locked,bool any)
 {
  goalValue.Text=goal==null?"미등록":goal.Value.ToString("F2")+"%";
  currentValue.Text=current==null?"—":current.Value.ToString("F2")+"%";
  currentValue.Tag=goal==null||current==null?null:current>=goal?"good":"bad";currentValue.ForeColor=Theme.For(currentValue);
  stateChip.Set(any?(locked?"고정":"후보"):"");
 }
 // 기록 등록 확인 창에 보일 것. '기존 기록'은 지금 저장돼 있는 값이다(확인을 기다리는 동안 기록이 바뀌면 다시 그린다. 저장은 그 순간의 값과 견준다).
 // 등록할 기록의 색: 기존 기록 이상이면 연두, 미만이면 빨강(스코어가 있으면 스코어로, 없으면 퍼센트를 스코어로 바꿔 견준다)
 ConfirmView PendingView(Selection chart,decimal accuracy,int? rawScore,string? note=null)
 {
  var old=store.Get(chart);var song=catalog.Songs.Single(s=>s.Id==chart.SongId);
  string? tone=old==null?null:(rawScore??ScoreValue.FromAccuracy(accuracy))>=old.Value?"good":"bad";
  return new(OverlayText.Title(catalog,song),$"{chart.Button}B",chart.Pattern,chart.Judge+"판정",$"{accuracy:F2}%",rawScore?.ToString("N0"),old==null?"미등록":$"{old.Accuracy:F2}%",old!=null&&rawScore!=null?old.Value.ToString("N0"):null,tone,note);
 }
 // 자주 바뀌는 글(상태, SCORE 상태)은 너비를 고정하고, 줄 수가 달라질 때만 높이를 고친다.
 // 크기를 글에 맞추게 두면(AutoSize) 글이 바뀔 때마다 창 전체의 배치를 다시 잡는다. 그 일이 한 번에 수십 ms 걸리고,
 // 화면 캡처가 같은 화면 스레드에서 0.1초마다 돌기 때문에 캡처가 밀린다(SCORE 쪽 시험에서 초당 캡처가 준 것으로 드러났다. docs\기록\UI개선.md).
 static void Steady(Label label,int width)
 {
  label.AutoSize=false;label.MaximumSize=Size.Empty;label.MinimumSize=Size.Empty;label.Width=width;
  void Fit(){int height=label.GetPreferredSize(new Size(width,0)).Height;if(label.Height!=height)label.Height=height;}
  label.TextChanged+=(_,_)=>Fit();Fit();
 }
 void SaveOptions()=>new UserOptions(fullscreen.Checked,scoreImagesOption.Checked,Theme.Dark).Save(optionsPath);
 bool OnScreen(Control control)=>scrollArea==null||control.RectangleToScreen(control.ClientRectangle).IntersectsWith(scrollArea.RectangleToScreen(scrollArea.ClientRectangle));
}
