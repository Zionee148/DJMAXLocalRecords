using System.Runtime.InteropServices;
namespace LadderRecords;

// 머리글과 줄을 직접 그리는 목록(기록 목록, 히스토리의 표)의 바탕.
// 줄은 WinForms 의 OwnerDraw(DrawSubItem)로 그린다. 머리글은 그 길(DrawColumnHeader)을 쓰지 않고 Windows 의 알림을 직접 받아 그린다:
// 그 길로는 머리글이 직접 그려지지 않고 Windows 의 기본 모양(흰 바탕, 검은 글자)으로 나온다(실제 화면을 찍어 보고 알았다. 창을 그림으로 옮기는 검사로는 가려지지 않는다).
// 까닭은 끝까지 가려 보지 않았다. 짐작: WinForms 는 머리글을 그리기 전에 목록의 글꼴을 GDI+ 글꼴로 바꿔 만드는데(Font.FromHfont), 앱이 실은 글꼴(SUIT)은 Windows 에 설치된 글꼴이 아니다.
// 목록의 글꼴은 머리글의 높이를 정하는 데에만 쓴다(글자는 모두 직접 그린다).
abstract class OwnerTable:ListView
{
 [StructLayout(LayoutKind.Sequential)]struct Notice{public IntPtr From,Id;public int Code;}
 [StructLayout(LayoutKind.Sequential)]struct Edges{public int Left,Top,Right,Bottom;}
 [StructLayout(LayoutKind.Sequential)]struct CustomDraw{public Notice Header;public int Stage;public IntPtr Dc;public Edges Area;public IntPtr Item;public int State;public IntPtr Param;}
 [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr window,int message,IntPtr w,IntPtr l);
 [DllImport("user32.dll")]static extern bool GetClientRect(IntPtr window,out Edges area);
 /// <summary>머리글의 한 칸을 그린다(바탕과 아래의 선은 이미 그려져 있다)</summary>
 protected Action<Graphics,Rectangle,int>? HeaderPainter;
 /// <summary>머리글을 직접 그린 횟수(개발용 검사가 "직접 그렸는가"를 본다)</summary>
 internal int HeaderDraws{get;private set;}
 protected OwnerTable()
 {
  View=View.Details;VirtualMode=true;OwnerDraw=true;FullRowSelect=true;MultiSelect=false;GridLines=false;HideSelection=false;DoubleBuffered=true;BorderStyle=BorderStyle.None;Tag="table";
  DrawItem+=(_,_)=>{};
 }
 protected override void WndProc(ref Message m)
 {
  // WM_NOTIFY · NM_CUSTOMDRAW 가 머리글에서 왔을 때만 가로챈다
  if(m.Msg==0x004E&&m.LParam!=IntPtr.Zero&&IsHandleCreated)
  {
   var notice=Marshal.PtrToStructure<Notice>(m.LParam);
   if(notice.Code==-12&&notice.From!=IntPtr.Zero&&notice.From==SendMessage(Handle,0x101F,IntPtr.Zero,IntPtr.Zero))
   {
    var draw=Marshal.PtrToStructure<CustomDraw>(m.LParam);
    if(draw.Stage==0x1)   // 그리기 전: 머리글 전체의 바탕과 아래의 선을 그리고, 칸마다 알려 달라고 답한다
    {
     GetClientRect(notice.From,out var whole);
     using(var g=Graphics.FromHdc(draw.Dc))
     {
      using(var back=new SolidBrush(Theme.Field))g.FillRectangle(back,0,0,whole.Right,whole.Bottom);
      using var line=new Pen(Theme.Line);g.DrawLine(line,0,whole.Bottom-1,whole.Right,whole.Bottom-1);
     }
     m.Result=(IntPtr)0x20;return;
    }
    if(draw.Stage==0x10001)   // 한 칸을 그리기 전: 직접 그리고, 기본 그리기를 건너뛰라고 답한다
    {
     int column=(int)draw.Item;var area=Rectangle.FromLTRB(draw.Area.Left,draw.Area.Top,draw.Area.Right,draw.Area.Bottom);
     if(column>=0&&column<Columns.Count&&area.Width>0)
      using(var g=Graphics.FromHdc(draw.Dc))
      {
       using(var back=new SolidBrush(Theme.Field))g.FillRectangle(back,area);
       using(var line=new Pen(Theme.Line))g.DrawLine(line,area.Left,area.Bottom-1,area.Right,area.Bottom-1);
       HeaderPainter?.Invoke(g,area,column);HeaderDraws++;
      }
     m.Result=(IntPtr)0x4;return;
    }
   }
  }
  base.WndProc(ref m);
  // 줄이 끝난 아래쪽: 다크에서는 Windows 의 테마(DarkMode_Explorer. 스크롤 막대를 어둡게 하려고 쓴다)가 칸의 경계마다 세로선을 그린다. 목록이 그린 뒤에 표의 바탕으로 덮는다
  if(m.Msg==0x000F&&IsHandleCreated)
  {
   int from=RowsEnd();
   if(from<ClientSize.Height){using var g=Graphics.FromHwnd(Handle);using var back=new SolidBrush(Theme.Table);g.FillRectangle(back,0,from,ClientSize.Width,ClientSize.Height-from);}
  }
 }
 /// <summary>마지막 줄의 아래쪽 끝(줄이 없으면 머리글의 아래)</summary>
 protected int RowsEnd()=>VirtualListSize>0?GetItemRect(VirtualListSize-1).Bottom:HeadTall();
 /// <summary>머리글의 높이. 목록을 굴려도 같다(줄의 자리는 굴린 만큼 달라지므로 줄의 좌표로 머리글의 높이를 셈하지 않는다)</summary>
 protected int HeadTall()
 {
  var header=SendMessage(Handle,0x101F,IntPtr.Zero,IntPtr.Zero);
  return header!=IntPtr.Zero&&GetClientRect(header,out var area)?area.Bottom:0;
 }
}
