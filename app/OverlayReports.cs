using System.Text;
using System.Text.Json;
namespace LadderRecords;
static class OverlayReports
{
 public static void Write(Catalog catalog,JsonElement a,string output){
  using var font=new Font(a.GetProperty("Font").GetString()!,a.GetProperty("HeaderSize").GetInt32(),FontStyle.Bold,GraphicsUnit.Pixel);
  var rows=catalog.Songs.OrderBy(s=>s.Name).Select(s=>(Title:OverlayText.Title(catalog,s),Shown:OverlayText.DisplayTitle(OverlayText.Title(catalog,s),font))).Where(s=>s.Title!=s.Shown).ToArray();
  string report=$"중간 생략 대상: {rows.Length}곡\n기준 제목: {OverlayText.ReferenceTitle}\n글꼴: {font.Name} / {font.Size}px / 참고 폭: {OverlayText.Measure(OverlayText.ReferenceTitle,font):F1}px\nTekken과 카트라이더는 지정한 표기를 사용합니다. 이후 기준 제목보다 긴 곡은 가운데를 생략합니다.\n\n"+string.Join("\n\n",rows.Select(r=>r.Title+"\n→ "+r.Shown));
  File.WriteAllText(output,report,new UTF8Encoding(true));
  if(OverlayText.DisplayTitle(OverlayText.ReferenceTitle,font)!="Tekken Tag Tournament Piano Intro...")throw new Exception("Reference title shortened");
 }
}