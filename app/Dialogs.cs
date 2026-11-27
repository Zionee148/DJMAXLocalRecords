using System.Text.Json;
namespace LadderRecords;

static class OverlayText
{
 public const string ReferenceTitle="Tekken Tag Tournament Piano Intro -Massive True Mix-";
 public const string KartTitle="카트라이더, 크레이지아케이드, 버블파이터 Main theme ~CHUCK Remix~";
 public static string Title(Catalog catalog,Song song)=>catalog.Songs.Count(s=>s.Name.Trim().Equals(song.Name.Trim(),StringComparison.OrdinalIgnoreCase))>1?$"{song.Name} ({song.Artist})":song.Name;
 public static string[] Titles(Catalog catalog)=>catalog.Songs.Select(s=>Title(catalog,s)).ToArray();
 public static string Describe(Catalog catalog,Selection c)=>$"{Title(catalog,catalog.Songs.Single(s=>s.Id==c.SongId))} · {c.Button}B · {c.Pattern} · {c.Judge}판정";
 public static float Measure(string text,Font font){using var b=new Bitmap(1,1);using var g=Graphics.FromImage(b);return g.MeasureString(text,font,PointF.Empty,StringFormat.GenericTypographic).Width;}
 public static string DisplayTitle(string title,Font font,int? width=null){if(width==null||Measure(title,font)<=width)return title;var elements=System.Globalization.StringInfo.ParseCombiningCharacters(title);string Short(int n)=>(n==elements.Length?title:title[..elements[n]])+"...";int lo=0,hi=elements.Length;while(lo<hi){int mid=(lo+hi+1)/2;if(Measure(Short(mid),font)<=width)lo=mid;else hi=mid-1;}return Short(lo);}
 public static int Width(JsonElement a,string[] titles)=>OverlayLayout.Width(BadgeDesign.Complete(a),titles);
 public static string Middle(string text,Font font,int width){using var b=new Bitmap(1,1);using var g=Graphics.FromImage(b);bool Fits(string t)=>g.MeasureString(t,font,PointF.Empty,StringFormat.GenericTypographic).Width<=width;if(Fits(text))return text;var elements=System.Globalization.StringInfo.ParseCombiningCharacters(text);string Short(int count){if(count==0)return "…";int left=(int)Math.Ceiling(count*.5),right=count-left;return text[..elements[left]]+"…"+(right==0?"":text[elements[elements.Length-right]..]);}int lo=0,hi=Math.Max(0,elements.Length-1);while(lo<hi){int mid=(lo+hi+1)/2;if(Fits(Short(mid)))lo=mid;else hi=mid-1;}return Short(lo);}

}
class CompactDialog:AppWindow
{
 protected readonly FlowLayoutPanel Body=new(){AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new(14),Margin=Padding.Empty};
 public CompactDialog(string title){Text=title;Font=Theme.Px(AppFonts.Ui,9.75f);AutoSize=true;AutoSizeMode=AutoSizeMode.GrowAndShrink;FormBorderStyle=FormBorderStyle.FixedSingle;MaximizeBox=false;MinimizeBox=false;StartPosition=FormStartPosition.CenterParent;Controls.Add(Body);}
 protected void Row(string name,params Control[] items){var row=new FlowLayoutPanel(){AutoSize=true,WrapContents=false,Margin=new(0,3,0,3)};row.Controls.Add(new Label(){Text=name,Width=170,Height=30,TextAlign=ContentAlignment.MiddleLeft,Tag="dim"});row.Controls.AddRange(items);Body.Controls.Add(row);}
 protected void Section(string title,int width){Body.Controls.Add(new SectionHeader(title){Width=width,Margin=new(0,Body.Controls.Count==0?0:10,0,2)});}
 protected void Buttons(string accept="적용"){var row=new FlowLayoutPanel(){AutoSize=true,WrapContents=false,Margin=new(0,10,0,0)};var ok=new Button(){Text=accept,AutoSize=true,DialogResult=DialogResult.OK};var cancel=new Button(){Text="취소",AutoSize=true,DialogResult=DialogResult.Cancel};row.Controls.AddRange(new Control[]{ok,cancel});Body.Controls.Add(row);AcceptButton=ok;CancelButton=cancel;}
}
// 저장 확인(주 화면 오른쪽. ConfirmPanel)에 보일 것. 스코어는 SCORE 로 읽은 등록에만 있다. Tone 은 이번 기록의 색("good"·"bad"·없음)
record ConfirmView(string Title,string Button,string Pattern,string Judge,string NewRate,string? NewScore,string OldRate,string? OldScore,string? Tone,string? Note=null);
