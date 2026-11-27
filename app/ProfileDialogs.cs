namespace LadderRecords;

// 프로필의 이름을 받는 창 셋(새로, 복사, 이름 변경)과 삭제 확인 창.
// 창 너비 390, 본문 좌우 여백 18, 입력칸 높이 34, 단추 너비 70·사이 6(100% 때의 값).
// 이름의 길이와 중복은 창이 아니라 저장 쪽(RecordStore.NameCheck)이 본다.
class NameDialog:CompactDialog
{
 public const int ContentWidth=352;
 readonly InputBox input;
 public string Value=>input.Box.Text;
 public TextBox Input=>input.Box;
 /// <param name="source">복사할 때의 원본 프로필 이름(본문 첫 줄에 보인다)</param>
 public NameDialog(string title,string label,string initial,string accept,string? source=null):base(title)
 {
  Body.Padding=new(18,16,18,16);
  if(source!=null)
  {
   var row=new FlowLayoutPanel(){AutoSize=true,WrapContents=false,Margin=new(0,0,0,14),MaximumSize=new(ContentWidth,0)};
   var name=Boxes.Text("원본 프로필","dim",9.75f);name.Margin=new(0,0,12,0);
   var value=Boxes.Text(source,null,9.75f,true,ContentWidth-name.PreferredWidth-12);   // 긴 이름은 줄을 바꿔 끝까지 보인다
   row.Controls.Add(name);row.Controls.Add(value);Body.Controls.Add(row);
  }
  Body.Controls.Add(Parts.TitleLine(label,ContentWidth,"최대 40자"));
  input=new InputBox(ContentWidth,initial,40){Margin=new(0,7,0,16)};Body.Controls.Add(input);
  var ok=new Button(){Text=accept,DialogResult=DialogResult.OK,Tag="primary"};var cancel=new Button(){Text="취소",DialogResult=DialogResult.Cancel};
  Body.Controls.Add(Parts.Footer(ContentWidth,ok,cancel));
  AcceptButton=ok;CancelButton=cancel;
  Shown+=(_,_)=>{input.Box.Focus();input.Box.SelectAll();};   // 바로 치면 이름 전체가 바뀐다
 }
 public static string? Ask(IWin32Window owner,string title,string label,string initial,string accept,string? source=null)
 {
  using var dialog=new NameDialog(title,label,initial,accept,source);return dialog.ShowDialog(owner)==DialogResult.OK?dialog.Value:null;
 }
}

// 프로필 삭제 확인. 처음 선택은 취소다: 연 직후의 Enter·Esc·닫기는 모두 취소이고, 삭제 단추를 직접 골라야 지운다.
class ProfileDeleteDialog:ChoiceDialog
{
 public ProfileDeleteDialog(string profile,int records):base("프로필 삭제")
 {
  const int width=NameDialog.ContentWidth;
  Body.Padding=new(18,16,18,16);
  var title=Parts.TitleLine("이 프로필을 삭제할까요?",width);title.Margin=new(0,0,0,12);Body.Controls.Add(title);
  var box=new FieldPanel(){Width=width,Margin=new(0,0,0,14)};
  var table=new TableLayoutPanel(){AutoSize=true,ColumnCount=2,RowCount=2,Margin=Padding.Empty,Location=new(14,11),BackColor=Color.Transparent};
  table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,width-28-96));table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,96));
  var nameHead=Boxes.Text("프로필 이름","dim",9f);var countHead=Boxes.Text("저장된 기록","dim",9f);countHead.Anchor=AnchorStyles.Right;
  var name=Parts.Value(profile);name.MaximumSize=new(width-28-96-8,0);name.Margin=new(0,5,0,0);
  var count=Parts.Value($"{records:N0}건");count.Anchor=AnchorStyles.Right|AnchorStyles.Top;count.Margin=new(0,5,0,0);
  table.Controls.Add(nameHead,0,0);table.Controls.Add(countHead,1,0);table.Controls.Add(name,0,1);table.Controls.Add(count,1,1);
  box.Controls.Add(table);box.Height=table.PreferredSize.Height+11+12;Body.Controls.Add(box);
  Body.Controls.Add(Boxes.Text("프로필과 기록이 함께 삭제됩니다.",null,9.75f,false,width));
  var second=Boxes.Text("‘직전 저장 되돌리기’로 복구할 수 없습니다.",null,9.75f,false,width);second.Margin=new(0,3,0,18);Body.Controls.Add(second);
  var delete=new Button(){Text="삭제",DialogResult=DialogResult.Yes,Tag="danger"};var cancel=new Button(){Text="취소",DialogResult=DialogResult.No,Tag="primary"};
  Body.Controls.Add(Parts.Footer(width,delete,cancel));
  AcceptButton=cancel;CancelButton=cancel;Shown+=(_,_)=>ActiveControl=cancel;
 }
 public static bool Ask(IWin32Window owner,string profile,int records){using var dialog=new ProfileDeleteDialog(profile,records);return dialog.ShowDialog(owner)==DialogResult.Yes;}
}
