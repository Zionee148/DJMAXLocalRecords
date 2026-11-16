using System.Reflection;

namespace LadderRecords;

static class CompatibilityLibrary
{
 static readonly Lazy<Assembly> Library=new(()=>Assembly.LoadFrom(
  Path.Combine(AppContext.BaseDirectory,"DJMAXLocalRecords.Compatibility.dll")));

 public static Type GetType(string name)=>Library.Value.GetType(name,throwOnError:true)!;
}
