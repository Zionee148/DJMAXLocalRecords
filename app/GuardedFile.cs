using System.Security.Cryptography;
namespace LadderRecords;

/// <summary>
/// 기록 파일 하나를 지키며 읽고 쓴다. 기록 형식과 상관없이 같은 규칙이고, 기록 파일을 읽고 쓰는 RecordStore 가 쓴다.
///  · 마지막으로 읽었거나 쓴 내용(SHA-256)을 기억해 두고, 저장하기 직전에 디스크의 파일과 견준다. 프로그램 밖에서 바뀌었거나 사라진 파일은 덮어쓰지 않는다.
///    시각이 아니라 내용으로 견준다(동기화 도구나 백신이 시각만 건드려도 저장이 막히지 않게)
///  · 저장: 전체를 .tmp 에 쓰고 → 본 파일을 잡은 채로 읽어 견주고 → 그 읽은 내용을 .bak 으로 남긴 뒤 → .tmp 를 본 파일로 바꾼다
///  · 바꿔 넣기가 다른 프로그램(검색 색인·백신)이 파일을 잠깐 열어 둔 탓에 막히면 조금 쉬었다가 다시 한다. 다시 할 때마다 그 직전에 다시 견준다
/// </summary>
sealed class GuardedFile(string path)
{
 public string Path{get;}=path;
 byte[]? seen;   // 파일 없이 시작했으면 null
 // 바꿔 넣기를 해 보는 횟수와 그 사이에 쉬는 시간(ms): 다시 하기 아홉 번, 쉬는 시간 합쳐 0.9초
 const int MoveTries=10,MoveWait=100;
 /// <summary>파일을 한 번 읽어 돌려준다(없으면 null). 견줄 내용과 풀어 쓸 내용이 같은 것이게, 읽은 그 바이트를 "본 것"으로 기억한다</summary>
 public byte[]? Read()
 {
  if(!File.Exists(Path)){seen=null;return null;}
  byte[] bytes=File.ReadAllBytes(Path);seen=SHA256.HashData(bytes);return bytes;
 }
 /// <summary>serialize 가 쓴 내용으로 저장한다. 밖에서 바뀌었거나 사라졌으면 RecordFileChangedException 을 던지고 아무것도 바꾸지 않는다</summary>
 public void Write(Action<Stream> serialize)
 {
  string tmp=Path+".tmp";
  using(var stream=File.Create(tmp))serialize(stream);
  byte[] written;using(var stream=File.OpenRead(tmp))written=SHA256.HashData(stream);
  // 다른 프로그램(검색 색인·백신)이 본 파일이나 임시 파일을 지우기를 함께 쓰지 않게 잠깐 열어 두면 바꿔 넣기가 막힌다(접근 거부).
  // 조금 쉬었다가 다시 하되, 쉬는 동안 밖에서 바뀐 것을 덮지 않게 할 때마다 그 직전에 다시 견준다. 끝내 막히면 마지막 예외를 그대로 던진다
  for(int tries=1;;tries++){
   Compare(tmp,backup:tries==1);
   try{File.Move(tmp,Path,true);break;}
   catch(Exception e)when(tries<MoveTries&&Busy(e)){Thread.Sleep(MoveWait);}
  }
  seen=written;
 }
 // 본 파일을 다른 쪽이 고치거나 바꿔 넣지 못하게 잡은 채로 읽어서 견주고, backup 이면 그 읽은 내용을 백업으로 쓴다. 놓자마자 교체한다(놓는 것과 교체 사이의 틈은 남는다)
 void Compare(string tmp,bool backup)
 {
  using var held=Hold();
  byte[]? disk=null;if(held!=null){disk=new byte[held.Length];held.ReadExactly(disk);}
  bool same=seen==null?disk==null:disk!=null&&SHA256.HashData(disk).AsSpan().SequenceEqual(seen);
  if(!same){
   // 밖에서 바뀐 본 파일과 지금의 백업은 건드리지 않는다(다시 하다가 알아챘으면 백업은 처음 견줄 때 쓴 것, 곧 바뀌기 전의 본 파일이다). 받아들이지 않은 내용이 임시 파일로 남지 않게 지운다
   try{File.Delete(tmp);}catch(IOException){}catch(UnauthorizedAccessException){}
   throw new RecordFileChangedException(disk==null?"기록 파일을 찾을 수 없어 저장을 멈췄습니다. 파일 위치를 확인한 뒤 프로그램을 다시 실행해 주세요.":"다른 곳에서 기록 파일이 바뀌어 저장을 멈췄습니다. 프로그램을 다시 실행해 최신 기록을 확인해 주세요.");
  }
  if(backup&&disk!=null)File.WriteAllBytes(Path+".bak",disk);
 }
 // 다른 프로그램이 파일을 열어 둔 탓으로 보는 실패: 접근 거부, 공유 위반(32), 잠금 위반(33)
 static bool Busy(Exception e)=>e is UnauthorizedAccessException||e is IOException&&(e.HResult&0xFFFF) is 32 or 33;
 FileStream? Hold(){try{return new FileStream(Path,FileMode.Open,FileAccess.Read,FileShare.Read);}catch(FileNotFoundException){return null;}catch(DirectoryNotFoundException){return null;}}
}
