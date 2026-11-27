using System.Text.Json;
namespace LadderRecords;
record RecordsViewSettings
{
 public string Search{get;init;}="";
 public string Button{get;init;}="전체";
 public string Pattern{get;init;}="전체";
 public string Judge{get;init;}="전체";
 public string SortColumn{get;init;}="LastPlayedAt";
 public bool Descending{get;init;}=true;
 public int Width{get;init;}
 public int Height{get;init;}
 public Dictionary<string,int> ColumnWidths{get;init;}=new();
 /// <summary>기록 히스토리의 그래프를 접어 두었는가. 예전 판이 적은 파일에는 없다: 펼친 것으로 읽는다</summary>
 public bool GraphFolded{get;init;}
 /// <summary>기록 히스토리의 PB 기준선. 예전 판이 적은 파일에는 없다: 켠 것으로 읽는다</summary>
 public bool PbLine{get;init;}=true;
 public static RecordsViewSettings Load(string? path){try{return path!=null&&File.Exists(path)?JsonSerializer.Deserialize<RecordsViewSettings>(File.ReadAllText(path))??new():new();}catch{return new();}}
 public void Save(string path){var temporary=path+".tmp";File.WriteAllText(temporary,JsonSerializer.Serialize(this));File.Move(temporary,path,true);}
}
