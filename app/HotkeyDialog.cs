namespace LadderRecords;

// 단축키 설정 창.
// 다섯 동작의 키(F1~F12, A~Z)와 Ctrl·Alt·Shift 를 고른다. 창 너비 460(100% 때의 값).
// 같은 조합을 두 동작에 주면 그 줄들을 강조하고 무엇끼리 겹쳤는지 알린다(적용할 수 없다). 적용했는데 Windows 가 그 조합을 받아 주지 않으면(다른 프로그램이 쓰는 중)
// 창을 닫지 않고 그 동작과 조합을 알린다. 조합을 고치면 다시 적용할 수 있다. 취소·Esc·닫기는 고치던 것을 버린다.
// 아래쪽 '컬렉션 가져오기' 구역은 시작·중지 키의 읽기 전용 안내다(키는 ScanHotkeys 의 것을 적는다).
class HotkeyDialog:CompactDialog
{
 const int ContentWidth=422,NameWidth=148,KeyWidth=84,BoxWidth=58,RowHeight=37;
 static int BoxLeft(int column)=>ContentWidth-BoxWidth*(3-column);
 readonly List<(Label Name,ComboBox Key,CheckBox Ctrl,CheckBox Alt,CheckBox Shift)> rows=new();
 readonly Label problem=new(){AutoSize=true,UseMnemonic=false,Tag="bad",Visible=false,MaximumSize=new(ContentWidth,0),Margin=new(0,9,0,0)};
 readonly Button apply=new(){Text="적용",Tag="primary"};
 readonly Func<List<Hotkey>,Exception?>? commit;
 readonly Control collectionKeys;
 /// <summary>개발용 검사: '컬렉션 가져오기' 구역에 적힌 글(이름과 키)</summary>
 internal IReadOnlyList<string> CollectionKeyTexts=>collectionKeys.Controls.Cast<Control>().Select(c=>c.Text).ToList();
 int refused=-1;string refusedCombo="";   // Windows 가 받아 주지 않은 동작과 그때의 조합. 그 조합을 고칠 때까지 알림을 둔다
 public List<Hotkey> Value=>rows.Select((r,i)=>new Hotkey(Hotkeys.Defaults()[i].Name,(Keys)r.Key.SelectedItem!,(uint)((r.Ctrl.Checked?2:0)|(r.Alt.Checked?1:0)|(r.Shift.Checked?4:0)))).ToList();

 /// <param name="commit">적용을 누르면 불린다. 등록하고 저장한 뒤 null 을, 못 했으면 그 까닭을 돌려준다. 없으면 창만 닫는다</param>
 public HotkeyDialog(List<Hotkey> values,Func<List<Hotkey>,Exception?>? commit=null):base("단축키 설정")
 {
  this.commit=commit;Body.Padding=new(18,14,18,16);
  Body.Controls.Add(new SectionHeader("기록과 감지"){Width=ContentWidth,Margin=new(0,0,0,6)});
  var head=new Panel(){Size=new(ContentWidth,26),Margin=Padding.Empty,Tag="field"};
  void Head(string text,int left,int width,ContentAlignment align)=>head.Controls.Add(new Label(){Text=text,Tag="dim",AutoSize=false,Bounds=new(left,0,width,26),TextAlign=align,Font=Theme.Px(AppFonts.Ui,9f,FontStyle.Bold),BackColor=Color.Transparent});
  Head("동작",8,NameWidth-8,ContentAlignment.MiddleLeft);Head("키",NameWidth+2,KeyWidth,ContentAlignment.MiddleLeft);
  Head("Ctrl",BoxLeft(0),BoxWidth,ContentAlignment.MiddleCenter);Head("Alt",BoxLeft(1),BoxWidth,ContentAlignment.MiddleCenter);Head("Shift",BoxLeft(2),BoxWidth,ContentAlignment.MiddleCenter);
  Body.Controls.Add(head);
  foreach(var item in values)
  {
   var row=new Panel(){Size=new(ContentWidth,RowHeight),Margin=Padding.Empty};
   var name=new Label(){Text=item.Name,AutoSize=false,Bounds=new(8,0,NameWidth-8,RowHeight-1),TextAlign=ContentAlignment.MiddleLeft,Font=Theme.Px(AppFonts.Ui,9.75f,FontStyle.Bold),UseMnemonic=false};
   var key=new ThemedCombo(){DropDownStyle=ComboBoxStyle.DropDownList,Width=KeyWidth,AccessibleName=item.Name+" 키"};
   key.Items.AddRange(Enumerable.Range((int)Keys.F1,12).Select(k=>(object)(Keys)k).Concat(Enumerable.Range((int)Keys.A,26).Select(k=>(object)(Keys)k)).ToArray());key.SelectedItem=item.Key;
   key.Location=new(NameWidth,(RowHeight-1-key.PreferredHeight)/2);
   CheckBox Box(string modifier,uint bit,int column)=>new(){Text="",AutoSize=false,Size=new(18,18),Location=new(BoxLeft(column)+(BoxWidth-18)/2+2,(RowHeight-1-18)/2),Checked=(item.Modifiers&bit)!=0,AccessibleName=item.Name+" "+modifier};
   var ctrl=Box("Ctrl",2,0);var alt=Box("Alt",1,1);var shift=Box("Shift",4,2);
   var line=new Panel(){Bounds=new(0,RowHeight-1,ContentWidth,1),Tag="line"};
   row.Controls.AddRange(new Control[]{name,key,ctrl,alt,shift,line});Body.Controls.Add(row);
   rows.Add((name,key,ctrl,alt,shift));
   key.SelectedIndexChanged+=(_,_)=>Review();ctrl.CheckedChanged+=(_,_)=>Review();alt.CheckedChanged+=(_,_)=>Review();shift.CheckedChanged+=(_,_)=>Review();
  }
  Body.Controls.Add(problem);
  var note=Boxes.Text("저장 확인·취소는 저장 확인을 기다리는 동안 작동합니다.\n저장 취소는 점수를 확인하는 중에도 확인을 멈춥니다.","dim",9.75f,false,ContentWidth);note.Margin=new(0,9,0,14);Body.Controls.Add(note);
  // 컬렉션 가져오기의 두 키(읽기 전용 안내. 바꿀 수 없다)
  Body.Controls.Add(new SectionHeader("컬렉션 가져오기"){Width=ContentWidth,Margin=new(0,0,0,6)});
  collectionKeys=KeyBoxes.Make(0,ContentWidth,ScanHotkeys.Start,ScanHotkeys.Stop);collectionKeys.Margin=new(0,0,0,14);Body.Controls.Add(collectionKeys);
  Body.Controls.Add(new Panel(){Size=new(ContentWidth,1),Margin=new(0,0,0,12),Tag="line"});
  var cancel=new Button(){Text="취소",DialogResult=DialogResult.Cancel};
  Body.Controls.Add(Parts.Footer(ContentWidth,apply,cancel));
  apply.Click+=(_,_)=>Apply();AcceptButton=apply;CancelButton=cancel;
  Review();
 }
 void Mark(int index,bool wrong){rows[index].Name.Tag=wrong?"bad":null;rows[index].Name.ForeColor=Theme.For(rows[index].Name);}
 // 지금 고른 조합을 본다: 같은 조합이 둘 이상이면 그 줄들을 강조하고 적용을 막는다
 void Review()
 {
  var value=Value;
  if(refused>=0&&Hotkeys.Format(value[refused])!=refusedCombo)refused=-1;
  var same=value.Select((k,i)=>(Key:(k.Key,k.Modifiers),Index:i)).GroupBy(x=>x.Key).Where(g=>g.Count()>1).ToList();
  var marked=same.SelectMany(g=>g.Select(x=>x.Index)).ToHashSet();if(refused>=0)marked.Add(refused);
  for(int i=0;i<rows.Count;i++)Mark(i,marked.Contains(i));
  string? text=same.Count>0
   ?string.Join("\n",same.Select(g=>$"{string.Join("·",g.Select(x=>$"‘{value[x.Index].Name}’"))}에 같은 단축키({Hotkeys.Format(value[g.First().Index])})가 지정되어 있습니다. 다른 조합을 선택해 주세요."))
   :refused>=0?$"‘{value[refused].Name}’에 지정한 {refusedCombo}{Josa(refusedCombo)} 다른 프로그램이 사용 중입니다. 다른 조합을 선택해 주세요.":null;
  problem.Text=text??"";problem.Visible=text!=null;apply.Enabled=text==null;
 }
 // 조합의 끝 글자(키 이름)에 맞는 목적격 조사. F1~F12 는 숫자를 읽은 소리, A~Z 는 글자 이름의 소리로 고른다
 static string Josa(string combo)
 {
  string key=combo[(combo.LastIndexOf('+')+1)..];
  bool closed=key.Length>1&&key[0]=='F'?key[^1] is '0' or '1' or '3' or '6' or '7' or '8':key is "L" or "M" or "N" or "R";
  return closed?"을":"를";
 }
 void Apply()
 {
  if(!apply.Enabled)return;
  var value=Value;var error=commit?.Invoke(value);
  if(error==null){DialogResult=DialogResult.OK;return;}
  if(error is HotkeyTaken taken){refused=taken.Index;refusedCombo=Hotkeys.Format(value[taken.Index]);Review();return;}
  problem.Text=error.Message;problem.Visible=true;   // 그 밖의 까닭(저장 실패 등)은 그대로 보인다. 조합을 바꾸지 않고 다시 적용할 수 있다
 }
}
