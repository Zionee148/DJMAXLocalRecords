using System.Net;
using System.Text.Json;
namespace LadderRecords;

// 자동 업데이트: 새 버전이 있는지 알아보는 부분 (기획서 docs\plans\20261116-자동업데이트-기획서.md 의 2·3절).
// 공식 저장소의 정식 릴리즈 가운데 지금 버전보다 높은 것만 고른다. 내려받거나 설치하지는 않는다.

/// <summary>새 버전을 찾는 곳. 배포하는 빌드에서는 공식 저장소로 고정되어 있다.</summary>
record UpdateSource(string Releases,string DownloadPrefix,string PagePrefix,bool Test)
{
 public static readonly UpdateSource Official=new(
  "https://api.github.com/repos/Zionee148/DJMAXLocalRecords/releases?per_page=30",
  "https://github.com/Zionee148/DJMAXLocalRecords/releases/download/",
  "https://github.com/Zionee148/DJMAXLocalRecords/releases/",false);
 public static UpdateSource For(UpdateTestConfig? test)
 {
  if(test?.Source is not {Length:>0} source)return Official;
  string root=source.TrimEnd('/');
  return new(root+"/releases",root+"/download/",root+"/",true);
 }
}

/// <summary>시험용 빌드만 읽는 설정(실행 파일 옆의 update-test.json). 배포하는 빌드에서는 언제나 null 이다.</summary>
record UpdateTestConfig(string? Source=null,int? OverlayPort=null,string? Instance=null)
{
 public const string FileName="update-test.json";
 public static UpdateTestConfig? Load(string? folder=null)
 {
#if UPDATE_TEST
  try
  {
   string path=Path.Combine(folder??AppContext.BaseDirectory,FileName);
   return File.Exists(path)?JsonSerializer.Deserialize<UpdateTestConfig>(File.ReadAllText(path)):null;
  }
  catch(JsonException){return null;}
  catch(IOException){return null;}
#else
  return null;
#endif
 }
}

/// <summary>내려받을 새 버전 하나.</summary>
record ReleaseInfo(Version Version,string Notes,string Page,string AssetName,string Url,long Size,string Sha256);

abstract record CheckResult;
/// <summary>지금 버전보다 높은 정식 릴리즈가 없다.</summary>
sealed record UpToDate:CheckResult;
sealed record UpdateAvailable(ReleaseInfo Release):CheckResult;
/// <summary>더 높은 정식 릴리즈는 있는데 자동 업데이트에 쓸 파일이 갖춰지지 않았다.</summary>
sealed record NoPackage(Version Version,string Page,string Reason):CheckResult;
/// <summary>알아보지 못했다. 최신 버전이라는 뜻이 아니다.</summary>
sealed record CheckFailed(string Reason):CheckResult;

static class UpdateChecker
{
 public const long MaxPackageBytes=1L<<30;
 const int MaxListBytes=4<<20,MaxNotes=20000;
 public static string AssetName(Version version)=>$"DJMAXLocalRecords-{AppVersion.Text(version)}-win-x64.zip";

 internal static readonly HttpClient Http=Create();
 static HttpClient Create()
 {
  var client=new HttpClient(new SocketsHttpHandler{AllowAutoRedirect=true,MaxAutomaticRedirections=5,ConnectTimeout=TimeSpan.FromSeconds(10)}){Timeout=Timeout.InfiniteTimeSpan};
  // GitHub 은 User-Agent 가 없는 요청을 받지 않는다. 프로그램 이름과 버전만 보낸다(사용자를 알 수 있는 것은 넣지 않는다)
  client.DefaultRequestHeaders.UserAgent.ParseAdd("DJMAXLocalRecords/"+AppVersion.Plain(AppVersion.Current));
  client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
  client.DefaultRequestHeaders.Add("X-GitHub-Api-Version","2022-11-28");
  return client;
 }

 public static async Task<CheckResult> Check(Version current,UpdateSource source,CancellationToken cancel=default)
 {
  try
  {
   using var limit=CancellationTokenSource.CreateLinkedTokenSource(cancel);limit.CancelAfter(TimeSpan.FromSeconds(15));
   using var response=await Http.GetAsync(source.Releases,HttpCompletionOption.ResponseHeadersRead,limit.Token);
   if(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
    return new CheckFailed("GitHub 이 지금은 조회를 받지 않습니다(짧은 시간에 너무 많이 물었을 때 그렇습니다). 잠시 뒤에 다시 확인해 주세요.");
   if(!response.IsSuccessStatusCode)return new CheckFailed($"릴리즈 목록을 받지 못했습니다(응답 {(int)response.StatusCode}).");
   await using var stream=await response.Content.ReadAsStreamAsync(limit.Token);
   using var memory=new MemoryStream();var buffer=new byte[81920];int read;
   while((read=await stream.ReadAsync(buffer,limit.Token))>0)
   {
    if(memory.Length+read>MaxListBytes)return new CheckFailed("릴리즈 목록이 예상보다 큽니다.");
    memory.Write(buffer,0,read);
   }
   return Pick(System.Text.Encoding.UTF8.GetString(memory.GetBuffer(),0,(int)memory.Length),current,source);
  }
  catch(OperationCanceledException)when(!cancel.IsCancellationRequested){return new CheckFailed("응답이 없어 확인하지 못했습니다. 인터넷 연결을 확인해 주세요.");}
  catch(HttpRequestException){return new CheckFailed("연결하지 못해 확인하지 못했습니다. 인터넷 연결을 확인해 주세요.");}
  catch(IOException){return new CheckFailed("받는 도중 연결이 끊겨 확인하지 못했습니다.");}
  // 새 버전을 알아보다 난 오류로 프로그램이 멈추면 안 된다. 무엇이든 "확인하지 못함"으로 돌려준다
  catch(Exception e)when(e is not OperationCanceledException){return new CheckFailed("확인하는 도중 오류가 났습니다. ("+e.Message+")");}
 }

 /// <summary>릴리즈 목록(GitHub 의 응답)에서 지금 버전보다 높은 정식 릴리즈 가운데 가장 높은 것을 고른다.</summary>
 public static CheckResult Pick(string json,Version current,UpdateSource source)
 {
  JsonDocument document;
  try{document=JsonDocument.Parse(json);}catch(JsonException){return new CheckFailed("릴리즈 목록을 읽지 못했습니다.");}
  using(document)
  {
   if(document.RootElement.ValueKind!=JsonValueKind.Array)return new CheckFailed("GitHub에서 받은 릴리즈 목록의 형식을 읽을 수 없습니다.");
   Version? best=null;JsonElement chosen=default;
   foreach(var item in document.RootElement.EnumerateArray())
   {
    if(item.ValueKind!=JsonValueKind.Object)continue;
    // 초안과 시험판은 대상이 아니다. 둘 다 "아니다"라고 분명히 적힌 것만 정식 릴리즈로 본다
    if(!IsFalse(item,"draft")||!IsFalse(item,"prerelease"))continue;
    if(!AppVersion.TryParseTag(Text(item,"tag_name"),out var version)||version<=current)continue;
    if(best==null||version>best){best=version;chosen=item;}
   }
   if(best==null)return new UpToDate();
   string page=Text(chosen,"html_url") is {} url&&url.StartsWith(source.PagePrefix,StringComparison.Ordinal)?url:source.PagePrefix;
   string wanted=AssetName(best);
   var assets=chosen.TryGetProperty("assets",out var list)&&list.ValueKind==JsonValueKind.Array
    ?list.EnumerateArray().Where(a=>a.ValueKind==JsonValueKind.Object&&Text(a,"name")==wanted).ToList():new();
   if(assets.Count!=1)return new NoPackage(best,page,assets.Count==0?$"릴리즈에 자동 업데이트용 파일({wanted})이 없습니다.":"릴리즈에 같은 이름의 파일이 여러 개 있습니다.");
   var asset=assets[0];
   string? digest=Text(asset,"digest"),download=Text(asset,"browser_download_url");
   if(digest==null||!digest.StartsWith("sha256:",StringComparison.Ordinal)||digest.Length!=71||!digest.AsSpan(7).ToString().All(Uri.IsHexDigit))
    return new NoPackage(best,page,"릴리즈에서 유효한 SHA-256 해시를 확인할 수 없습니다.");
   if(!asset.TryGetProperty("size",out var sizeValue)||!sizeValue.TryGetInt64(out long size)||size<=0||size>MaxPackageBytes)
    return new NoPackage(best,page,"파일 크기가 릴리즈에 없거나 예상 범위를 벗어납니다.");
   if(download==null||!download.StartsWith(source.DownloadPrefix,StringComparison.Ordinal)||!Uri.TryCreate(download,UriKind.Absolute,out var address)
      ||(address.Scheme!=Uri.UriSchemeHttps&&!(source.Test&&address.Scheme==Uri.UriSchemeHttp)))
    return new NoPackage(best,page,"파일의 주소가 공식 저장소의 것이 아닙니다.");
   string notes=Text(chosen,"body")??"";
   if(notes.Length>MaxNotes)notes=notes[..MaxNotes]+"\n…";
   return new UpdateAvailable(new(best,notes,page,wanted,download,size,digest[7..].ToLowerInvariant()));
  }
 }
 static string? Text(JsonElement item,string name)=>item.TryGetProperty(name,out var value)&&value.ValueKind==JsonValueKind.String?value.GetString():null;
 static bool IsFalse(JsonElement item,string name)=>item.TryGetProperty(name,out var value)&&value.ValueKind==JsonValueKind.False;

 /// <summary>릴리즈 본문(마크다운)을 그대로 읽을 만한 글로 고친다. 제목 표시와 목록 표시만 다듬는다.</summary>
 public static string PlainNotes(string notes)
 {
  var lines=notes.Replace("\r\n","\n").Replace('\r','\n').Split('\n').Select(line=>
  {
   string trimmed=line.TrimStart();string indent=line[..(line.Length-trimmed.Length)];
   if(trimmed.StartsWith('#'))return trimmed.TrimStart('#').Trim();
   if(trimmed.StartsWith("- ")||trimmed.StartsWith("* "))return indent+"· "+trimmed[2..];
   return line.TrimEnd();
  });
  string text=string.Join("\r\n",lines).Replace("**","").Trim();
  return text.Length==0?"(패치노트가 비어 있습니다)":text;
 }
}
