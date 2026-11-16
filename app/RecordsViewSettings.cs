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
 public static RecordsViewSettings Load(string? path){try{return path!=null&&File.Exists(path)?JsonSerializer.Deserialize<RecordsViewSettings>(File.ReadAllText(path))??new():new();}catch{return new();}}
 public void Save(string path){var temporary=path+".tmp";File.WriteAllText(temporary,JsonSerializer.Serialize(this));File.Move(temporary,path,true);}
}
