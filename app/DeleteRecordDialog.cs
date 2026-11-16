namespace LadderRecords;
class DeleteRecordDialog:CompactDialog
{
 public DeleteRecordDialog(RegisteredRecord entry):base("선택 기록 삭제 확인"){
 Body.Controls.Add(new Label(){Text="아래 조합의 기록을 삭제하시겠습니까?",AutoSize=true,Margin=new(0,0,0,10)});
 Body.Controls.Add(Boxes.Song($"{entry.SongName} ({entry.Artist})",$"{entry.Chart.Button}B",entry.Chart.Pattern,entry.Chart.Judge+"판정",$"최고 점수: {entry.Accuracy:F2}% · 플레이 카운트: {(entry.PlayCount is int count?$"{count}회":"-")}",$"최근 플레이: {entry.LastPlayedAt?.ToString("yyyy-MM-dd HH:mm:ss")??"-"}"));
 Body.Controls.Add(new Label(){Text="이 조합의 점수·플레이 카운트·최근 플레이 시간을 삭제합니다.\n다른 조합의 기록은 유지됩니다.",AutoSize=true,Margin=Padding.Empty,Tag="dim"});
 Buttons("삭제");((Button)AcceptButton!).Tag="danger";AcceptButton=null;Shown+=(_,_)=>{if(CancelButton is Control cancel){ActiveControl=cancel;cancel.Focus();}};
 }
}
