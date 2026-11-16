using System.Text.Json;
namespace LadderRecords;
record UserOptions(bool Fullscreen=false,bool DiagnosticImages=false,bool Dark=false)
{
 public static UserOptions Load(string path)=>File.Exists(path)?JsonSerializer.Deserialize<UserOptions>(File.ReadAllText(path))??new():new();
 public void Save(string path){string temporary=path+".tmp";File.WriteAllText(temporary,JsonSerializer.Serialize(this));File.Move(temporary,path,true);}
}
