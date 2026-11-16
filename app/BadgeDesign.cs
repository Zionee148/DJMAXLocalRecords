using System.Drawing.Drawing2D;
using System.Text.Json;
namespace LadderRecords;
static class BadgeDesign
{
 public static JsonElement Defaults=>JsonSerializer.SerializeToElement(new {
  Font="맑은 고딕", HeaderSize=25, BadgeSize=20, GoalSize=40, CurrentSize=30, EventSize=50,
  ResultRateSize=60, ResultScoreSize=36, Above="#B3FF68", Below="#FF707B", Text="#DCE6F0",
  Background="#10141E", TextTransparency=0, BackgroundTransparency=5
 });
 public static Color Background(string text)=>ColorTranslator.FromHtml(text switch{"4B"=>"#008B50","5B"=>"#267E91","6B"=>"#BC7408","8B"=>"#5144AC","NORMAL"=>"#EDC52A","HARD"=>"#EE781B","MAXIMUM"=>"#E50068","SC"=>"#A20BC3","하드" or "하드판정"=>"#B70C42","맥스" or "맥스판정"=>"#C211C9",_=>"#59616E"});
 public static Color Foreground(string text)=>text is "NORMAL" or "HARD"?Color.FromArgb(36,28,11):Color.White;
 public static GraphicsPath Rounded(RectangleF r,float radius){var p=new GraphicsPath();float d=Math.Min(radius*2,Math.Min(r.Width,r.Height));p.AddArc(r.Left,r.Top,d,d,180,90);p.AddArc(r.Right-d,r.Top,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.Left,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;}
 public static void Draw(Graphics g,string text,Font font,RectangleF r,int opacity=255){g.SmoothingMode=SmoothingMode.AntiAlias;using var path=Rounded(r,6);using var fill=new SolidBrush(Color.FromArgb(opacity,Background(text)));g.FillPath(fill,path);using var ink=new SolidBrush(Color.FromArgb(opacity,Foreground(text)));using var fmt=new StringFormat(){Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center};g.DrawString(text,font,ink,r,fmt);}
 public static JsonElement Complete(JsonElement original){var data=original.EnumerateObject().ToDictionary(p=>p.Name,p=>(object)(p.Value.ValueKind==JsonValueKind.String?p.Value.GetString()!:p.Value.GetInt32()));data.TryAdd("BadgeSize",Math.Clamp((int)Math.Round(original.GetProperty("HeaderSize").GetInt32()*.72),12,64));data.TryAdd("EventSize",original.GetProperty("HeaderSize").GetInt32());data.TryAdd("ResultRateSize",54);data.TryAdd("ResultScoreSize",32);return JsonSerializer.SerializeToElement(data);}
}

