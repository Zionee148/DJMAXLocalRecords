using System.Net.Http.Json;

namespace LadderRecords;

static class VArchiveImport
{
 static readonly HttpClient Client=new(){Timeout=TimeSpan.FromSeconds(15)};
 static readonly int[] Buttons={4,5,6,8};
 internal record Response(bool Success,int Button,List<Entry>? Records);
 internal record Entry(int Title,string Pattern,decimal? Score);

 public static async Task<Dictionary<Selection,decimal>> Fetch(Catalog catalog,string nickname)
 {
  var encoded=Uri.EscapeDataString(nickname);
  var responses=await Task.WhenAll(Buttons.Select(async button=>{
   var url=$"https://v-archive.net/api/v2/archive/{encoded}/button/{button}";
   var data=await Client.GetFromJsonAsync<Response>(url).ConfigureAwait(false);
   if(data is not {Success:true,Records:not null}||data.Button!=button)
    throw new InvalidDataException($"{button}B 기록을 받지 못함");   // 가져오기 창의 옅은 까닭 줄에 그대로 보인다(다른 까닭과 같은 '~함' 꼴)
   return data;
  })).ConfigureAwait(false);
  return Collect(catalog,responses);
 }

 internal static Dictionary<Selection,decimal> Collect(Catalog catalog,IEnumerable<Response> responses)
 {
  var songs=catalog.Songs.ToDictionary(s=>s.SourceId);
  var scores=new Dictionary<Selection,decimal>();
  foreach(var data in responses)
  foreach(var entry in data.Records!){
   var pattern=SteamPresence.NormalizePattern(entry.Pattern??"");
   if(entry.Score is not decimal value||value<0||value>100||decimal.Round(value,2)!=value)continue;
   if(!songs.TryGetValue(entry.Title,out var song)||!song.Charts.Contains(new(data.Button,pattern)))continue;
   var chart=new Selection(song.Id,data.Button,pattern,"노말",0);
   if(!scores.TryGetValue(chart,out var previous)||value>previous)scores[chart]=value;
  }
  return scores;
 }
}
