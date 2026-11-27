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
