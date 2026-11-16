using System.Text.Json;
namespace LadderRecords;

static class OverlayLayout
{
 public const int CanvasWidth=540,CanvasHeight=276;
 public const int ObsWidth=CanvasWidth*2,ObsHeight=CanvasHeight*2;
 public static int Number(JsonElement a,string key)=>a.GetProperty(key).GetInt32();
 public static Font FontOf(JsonElement a,string key,bool bold=true)=>new(a.GetProperty("Font").GetString()!,Number(a,key),bold?FontStyle.Bold:FontStyle.Regular,GraphicsUnit.Pixel);
 public static int Width(JsonElement a,string[] titles)=>CanvasWidth;
 public static int NormalHeight(JsonElement a,bool showCurrent)=>CanvasHeight;
 public static int Height(JsonElement a,bool showCurrent)=>CanvasHeight;
}

class StylePreview:Control
{
 readonly Func<JsonElement> appearance;
 readonly string title;
 readonly string confirmKey,cancelKey;
 readonly System.Windows.Forms.Timer motion=new(){Interval=30};
 long started;
 [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public bool ShowCurrent{get;set;}
 [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public string ButtonText{get;set;}="8B";
 [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public string PatternText{get;set;}="SC";
 [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public string JudgeText{get;set;}="하드";
 [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public int Mode{get;set;}

 public StylePreview(Func<JsonElement> appearance,string[] titles,bool showCurrent,List<Hotkey>? shortcuts=null)
 {
  var keys=shortcuts??Hotkeys.Defaults();
  confirmKey=Hotkeys.Format(keys[2]);cancelKey=Hotkeys.Format(keys[3]);
  this.appearance=appearance;ShowCurrent=showCurrent;
  title=titles.OrderByDescending(t=>t.Length).FirstOrDefault()??"Fundamental";
  DoubleBuffered=true;
  motion.Tick+=(_,_)=>{Invalidate();if(Environment.TickCount64-started>=7500)motion.Stop();};
 }
 public void Restart(){started=Environment.TickCount64;if(Mode>=7)motion.Start();else motion.Stop();Invalidate();}
 protected override void Dispose(bool disposing){if(disposing)motion.Dispose();base.Dispose(disposing);}
 protected override void OnPaint(PaintEventArgs e)
 {
  base.OnPaint(e);
  var g=e.Graphics;
  using var light=new SolidBrush(Color.FromArgb(240,240,240));
  using var dark=new SolidBrush(Color.FromArgb(215,215,215));
  for(int y=0;y<Height;y+=16)for(int x=0;x<Width;x+=16)g.FillRectangle((x/16+y/16)%2==0?dark:light,x,y,16,16);
  const int width=OverlayLayout.CanvasWidth,height=OverlayLayout.CanvasHeight;
  float scale=Math.Min(1,Math.Min(Width/(float)width,Height/(float)height));
  g.ScaleTransform(scale,scale);
  g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
  g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
  var a=BadgeDesign.Complete(appearance());
  int N(string key)=>OverlayLayout.Number(a,key);
  int alpha=(int)(255*(1-N("TextTransparency")/100f));
  string family=a.GetProperty("Font").GetString()!;
  Color C(string key)=>Color.FromArgb(alpha,ColorTranslator.FromHtml(a.GetProperty(key).GetString()!));
  using(var bg=new SolidBrush(Color.FromArgb((int)(255*(1-N("BackgroundTransparency")/100f)),ColorTranslator.FromHtml(a.GetProperty("Background").GetString()!))))
  using(var path=BadgeDesign.Rounded(new(0,0,width,height),12))g.FillPath(bg,path);
  using var ink=new SolidBrush(C("Text"));
  using var above=new SolidBrush(C("Above"));
  using var below=new SolidBrush(C("Below"));
  Font F(float size,bool bold=true)=>new(family,Math.Max(size,1),bold?FontStyle.Bold:FontStyle.Regular,GraphicsUnit.Pixel);
  float M(string s,Font font)=>g.MeasureString(s,font,PointF.Empty,StringFormat.GenericTypographic).Width;
  void Text(string s,Font font,Brush brush,float x,float y)=>g.DrawString(s,font,brush,new PointF(x,y),StringFormat.GenericTypographic);
  void Center(string s,float size,Brush brush,float y,bool bold=true)
  {
   using var requested=F(size,bold);
   using var fitted=F(size*Math.Min(1,488/Math.Max(1,M(s,requested))),bold);
   Text(s,fitted,brush,(width-M(s,fitted))/2,y);
  }
  string suffix=$" · {ButtonText} · {PatternText} · {JudgeText}판정";
  if(Mode>=7){
   bool first=Mode==7,best=Mode==8,kept=Mode==9;
   var firstScoreColor=Color.FromArgb(alpha,134,220,255);
   using var firstScore=new SolidBrush(firstScoreColor);
   long elapsed=started==0?1500:Environment.TickCount64-started;
   if(best)DrawChevrons(g,width,height,alpha);
   if(elapsed<1500){
    using var tag=new SolidBrush(first?firstScoreColor:best?C("Above"):Color.FromArgb(alpha,183,216,255));
    float eventSize=N("EventSize");
    Center(first?"FIRST SCORE SAVED":best?"NEW PERSONAL BEST":"PERSONAL BEST UNCHANGED",eventSize*.52f,tag,138-eventSize*1.02f);
    Center(first?"첫 기록 등록":best?"최고 기록 경신":"최고 기록 유지",eventSize,ink,140-eventSize*.30f);
    return;
   }
   Brush main=first?firstScore:best?above:ink;
   using var delta=new SolidBrush(kept?Color.FromArgb(alpha,99,183,255):Color.FromArgb(alpha,255,116,124));
   float r=N("ResultRateSize"),b=N("ResultScoreSize");
   int footerDrop=kept?0:8;
   float factor=Math.Min(1,(height-16-(kept?18:12)-(kept?52:26)-2*footerDrop)/(1.12f*(r+b+42)));
   float used=1.12f*(r+b+42)*factor+4+(kept?18:12)+(kept?52:26);
   float y=(height-used)/2;
   Center(best?"99.62%":"99.28%",r*factor,main,y);y+=r*factor*1.12f+2;
   Center(first?"▲99.28%":best?"▲0.14%":"▼0.26%",22*factor,delta,y);y+=22*factor*1.12f+6;
   Center(best?"996,200":"992,869",b*factor,main,y);y+=b*factor*1.12f+2;
   Center(first?"▲992,869":best?"▲1,400":"▼2,563",20*factor,delta,y);y+=20*factor*1.12f+6;
   if(kept){Center("BEST SCORE  995,432 | 99.54%",20,ink,y,false);y+=32;}
   using var footer=F(20,false);
   Center(OverlayText.DisplayTitle(title,footer,Math.Max(20,488-(int)M(suffix,footer)))+suffix,20,ink,y+footerDrop,false);
   return;
  }
  bool checking=Mode is 4 or 5,confirming=Mode==6;
  float header=N("HeaderSize"),badge=N("BadgeSize");
  float upperScale=Math.Min(1,(checking||confirming?91f:105f)/(header*1.3f+13+Math.Max(39,badge*1.35f+12)));
  using var initialBadge=F(badge);
  badge*=Math.Min(upperScale,394/Math.Max(1,new[]{"8B","MAXIMUM","맥스판정"}.Sum(t=>M(t,initialBadge))));
  header*=upperScale;
  using var headerFont=F(header);using var badgeFont=F(badge);
  Text(OverlayText.DisplayTitle(Mode==3?"악곡을 감지하고 있습니다":title,headerFont,488),headerFont,ink,26,22);
  float row=22+header*1.3f+13;
  if(Mode!=3){
   float x=26;
   foreach(var badgeText in new[]{ButtonText,PatternText,JudgeText+"판정"}){
    float w=M(badgeText,badgeFont)+26;
    BadgeDesign.Draw(g,badgeText,badgeFont,new(x,row,w,badge*1.35f+12),alpha);
    x+=w+8;
   }
  }
  row+=Math.Max(39,badge*1.35f+12)+(confirming?13:19);
  using(var pen=new Pen(Color.FromArgb(alpha/7,255,255,255)))g.DrawLine(pen,26,row,width-26,row);
  if(checking){Center(Mode==4?"점수를 확인하고 있습니다...":"점수를 확인하지 못했습니다",33.44f,ink,row+22,false);Center(Mode==4?$"취소 ({cancelKey})":"잠시 후 자동으로 취소됩니다",22.8f,ink,row+82,false);return;}
  if(confirming){
   Center("등록하시겠습니까?",27,ink,row+8);
   Center("992,869 │ 99.28%",44,ink,row+49);
   Center($"등록 ({confirmKey}) / 취소 ({cancelKey})",20,ink,row+112,false);
   return;
  }
  row+=14;
  float FitRow(string label,float size){
   using var normal=F(size,false);using var bold=F(size);
   return size*Math.Min(1,453/Math.Max(1,M(label,normal)+M("100.00%",bold)));
  }
  float goal=FitRow("개인 최고 점수 :",N("GoalSize")),current=FitRow("현재 점수 :",N("CurrentSize"));
  float scoresScale=Math.Min(1,Math.Max(55,height-15-row)/(1.6f*(goal+(ShowCurrent?current:0))));
  void Score(string label,string value,float fontSize,Brush brush,float y){
   using var normal=F(fontSize,false);using var bold=F(fontSize);
   Text(label,normal,brush,26,y);Text(value,bold,brush,width-26-M(value,bold),y);
  }
  Brush tone=Mode==0?above:Mode==1?below:ink;
  Score("개인 최고 점수 :",Mode==3?"-":Mode==2?"미등록":"99.25%",goal*scoresScale,ShowCurrent?ink:tone,row);
  if(ShowCurrent)Score("현재 점수 :",Mode==3?"-":Mode==1?"98.72%":"99.48%",current*scoresScale,tone,row+goal*scoresScale*1.6f);
 }
 void DrawChevrons(Graphics g,int width,int height,int alpha){if(started==0)return;double elapsed=Environment.TickCount64-started;var defs=new[]{(3,56,0,1950),(13,92,400,2300),(25,42,180,1650),(36,68,750,2100),(49,38,360,1850),(60,100,120,2450),(73,54,620,1950),(86,80,280,2200),(95,34,850,1600),(19,48,1500,1900),(55,60,1600,2000),(80,40,1750,1750)};float scale=height/290f;foreach(var (x,size,delay,time) in defs){double p=(elapsed-delay*.44)/(time*.52);if(p<0||p>1)continue;float opacity=(float)(Math.Min(1,Math.Min(p/.2,(1-p)/.2))*.38*(size>80?.7:1));float w=size*scale*1.6f,h=size*scale*1.8f,left=width*x/100f-w/2,top=height+100+(180-(float)p*670)*scale-h;var points=new[]{new PointF(left,top+h*.26f),new PointF(left+w/2,top),new PointF(left+w,top+h*.26f),new PointF(left+w,top+h),new PointF(left+w/2,top+h*.74f),new PointF(left,top+h)};using var path=new System.Drawing.Drawing2D.GraphicsPath();path.AddPolygon(points);using var brush=new System.Drawing.Drawing2D.LinearGradientBrush(new RectangleF(left,top,w,h),Color.FromArgb((int)(alpha*opacity),177,237,118),Color.Transparent,90f);g.FillPath(brush,path);}}
}
