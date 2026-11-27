using System.Text.Json;
namespace LadderRecords;
// 컬렉션 가져오기가 게임의 MUSIC DATA 화면을 읽을 때 쓰는 자료.
// 파일은 data\collection\collection.json 이고 tools\build_collection_data.py 가 만든다(그 도구가 미리 그린 글자 그림을 넣는다. 판독할 때는 원본 글꼴 파일이 필요하지 않다).
// 화면의 자리와 읽는 법은 CollectionScreen.cs·CollectionReader.cs.

/// <summary>0/1 의 그림. 화면에서 글자로 본 화소나 자료의 글자 그림이다.</summary>
sealed class Mask
{
 public readonly int Width,Height;public readonly bool[] Bits;
 public Mask(int width,int height){Width=width;Height=height;Bits=new bool[width*height];}
 public bool this[int x,int y]{get=>Bits[y*Width+x];set=>Bits[y*Width+x]=value;}
 public int Count(){int n=0;foreach(bool b in Bits)if(b)n++;return n;}
 /// <summary>글자가 있는 가장 왼쪽·오른쪽 열. 없으면 (-1,-1).</summary>
 public (int First,int Last) Columns(){int first=-1,last=-1;for(int x=0;x<Width;x++){bool any=false;for(int y=0;y<Height&&!any;y++)any=Bits[y*Width+x];if(any){if(first<0)first=x;last=x;}}return(first,last);}
 /// <summary>열마다의 화소 수.</summary>
 public int[] ColumnSums(){var sums=new int[Width];for(int y=0;y<Height;y++)for(int x=0;x<Width;x++)if(Bits[y*Width+x])sums[x]++;return sums;}
 /// <summary>화소마다 둘레 한 칸까지 넓힌 그림(3×3).</summary>
 public Mask Grown(){var o=new Mask(Width,Height);for(int y=0;y<Height;y++)for(int x=0;x<Width;x++){if(!Bits[y*Width+x])continue;for(int dy=-1;dy<=1;dy++){int yy=y+dy;if(yy<0||yy>=Height)continue;for(int dx=-1;dx<=1;dx++){int xx=x+dx;if(xx>=0&&xx<Width)o.Bits[yy*Width+xx]=true;}}}return o;}
 public int Differ(Mask other){int n=0;for(int i=0;i<Bits.Length;i++)if(Bits[i]!=other.Bits[i])n++;return n;}
 /// <summary>자료 파일의 한 줄로 묶인 비트(행 차례, 높은 자리부터)를 푼다.</summary>
 public static Mask Unpack(string base64,int width,int height){var m=new Mask(width,height);if(width==0)return m;var bytes=Convert.FromBase64String(base64);for(int i=0;i<m.Bits.Length;i++)m.Bits[i]=(bytes[i>>3]&(0x80>>(i&7)))!=0;return m;}
}

/// <summary>글자 하나의 그림. Left 는 놓는 자리에서 왼쪽으로 비켜난 정도, Advance 는 다음 글자까지의 거리(소수).</summary>
sealed record Glyph(Mask Picture,int Left,double Advance);
/// <summary>이름이 붙은 글자 표본(점수 칸의 숫자와 '-', BEST 의 숫자).</summary>
sealed record Sample(string Label,Mask Picture);

/// <summary>한 글꼴·크기의 글자 그림 묶음(제목용, 아티스트용).</summary>
sealed class GlyphSet
{
 public int Height{get;}public int Base{get;}readonly Dictionary<string,Glyph> glyphs=new();readonly Glyph blank;
 public GlyphSet(JsonElement e){Height=e.GetProperty("height").GetInt32();Base=e.GetProperty("base").GetInt32();blank=new(new Mask(0,Height),0,0);
  foreach(var g in e.GetProperty("glyphs").EnumerateObject()){var v=g.Value;int w=v[0].GetInt32();glyphs[g.Name]=new(Mask.Unpack(v[3].GetString()!,w,Height),v[1].GetInt32(),v[2].GetDouble());}}
 /// <summary>자료에 없는 글자는 자리를 차지하지 않는 빈 글자다(그 이름은 화면과 겹치지 않아 고르지 못한다).</summary>
 public Glyph Get(string ch)=>glyphs.TryGetValue(ch,out var g)?g:blank;
 public bool Has(string ch)=>glyphs.ContainsKey(ch);
 /// <summary>글을 그렸을 때의 너비(글자들의 거리의 합).</summary>
 public double WidthOf(string text){double w=0;foreach(string ch in Letters(text))w+=Get(ch).Advance;return w;}
 /// <summary>글을 글자(유니코드의 한 글자)로 나눈다.</summary>
 public static IEnumerable<string> Letters(string text){for(int i=0;i<text.Length;){int n=char.IsSurrogatePair(text,i)?2:1;yield return text.Substring(i,n);i+=n;}}
}

sealed class CollectionData
{
 public GlyphSet Title{get;}public GlyphSet Artist{get;}
 public List<Sample> Digits{get;}=new();public int DigitHeight{get;}
 public List<Sample> Best{get;}=new();public int BestWidth{get;}public int BestHeight{get;}
 public Dictionary<string,Mask> Categories{get;}=new();public int CategoryWidth{get;}public int CategoryHeight{get;}
 /// <summary>게임이 곡 목록의 이름과 다르게 그리는 곡: 이름 → 게임의 글.</summary>
 public Dictionary<string,string> Shown{get;}=new();

 public CollectionData(string json)
 {
  using var doc=JsonDocument.Parse(json);var root=doc.RootElement;
  if(root.GetProperty("version").GetInt32()!=1)throw new InvalidDataException("컬렉션 판독 자료의 형식을 모릅니다.");
  Title=new(root.GetProperty("title"));Artist=new(root.GetProperty("artist"));
  var digits=root.GetProperty("digits");DigitHeight=digits.GetProperty("height").GetInt32();
  foreach(var g in digits.GetProperty("glyphs").EnumerateArray())Digits.Add(new(g[0].GetString()!,Mask.Unpack(g[2].GetString()!,g[1].GetInt32(),DigitHeight)));
  var best=root.GetProperty("best");BestHeight=best.GetProperty("height").GetInt32();BestWidth=best.GetProperty("width").GetInt32();
  foreach(var g in best.GetProperty("glyphs").EnumerateArray())Best.Add(new(g[0].GetString()!,Mask.Unpack(g[2].GetString()!,g[1].GetInt32(),BestHeight)));
  var cats=root.GetProperty("categories");CategoryHeight=cats.GetProperty("height").GetInt32();CategoryWidth=cats.GetProperty("width").GetInt32();
  foreach(var p in cats.GetProperty("packs").EnumerateObject())Categories[p.Name]=Mask.Unpack(p.Value.GetString()!,CategoryWidth,CategoryHeight);
  foreach(var s in root.GetProperty("shown").EnumerateObject())Shown[s.Name]=s.Value.GetString()!;
 }
 public static CollectionData Load()=>new(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"data","collection","collection.json")));
 /// <summary>곡의 제목을 게임이 그리는 글.</summary>
 public string TitleOf(Song song){string name=song.Name.Trim();return Shown.TryGetValue(name,out var shown)?shown:name;}
}
