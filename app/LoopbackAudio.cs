using System.Diagnostics;
using System.Runtime.InteropServices;
namespace LadderRecords;
// Captures only the chosen process tree into an eight-second RAM buffer.
class LoopbackAudio:IDisposable
{
 readonly object sync=new();readonly float[] samples=new float[AudioIdentity.Rate*8];int count,write;readonly CancellationTokenSource stop=new();readonly Task worker;int disposed;readonly int? requestedProcessId;
 volatile string? error;volatile int targetProcessId;volatile bool initialized;public string? Error=>error;public int TargetProcessId=>targetProcessId;public bool Initialized=>initialized;
 public double Seconds{get{lock(sync)return count/(double)AudioIdentity.Rate;}}
 public LoopbackAudio(int? processId=null){requestedProcessId=processId;worker=Task.Run(Capture);}
 public float[] Snapshot(){lock(sync){var result=new float[count];int first=(write-count+samples.Length)%samples.Length;int tail=Math.Min(count,samples.Length-first);Array.Copy(samples,first,result,0,tail);Array.Copy(samples,0,result,tail,count-tail);return result;}}
 void Append(List<float> batch){lock(sync)foreach(var value in batch){samples[write]=value;write=(write+1)%samples.Length;count=Math.Min(count+1,samples.Length);}}
 [DllImport("ole32.dll")]static extern int CoInitializeEx(IntPtr reserved,uint mode);
 [DllImport("ole32.dll")]static extern void CoUninitialize();
 static void OK(int hr)=>Marshal.ThrowExceptionForHR(hr);
 static int FindGame(){var games=Process.GetProcessesByName("DJMAX RESPECT V");try{if(games.Length!=1)throw new Exception("실행 중인 디맥 게임을 하나만 확인해 주세요.");return games[0].Id;}finally{foreach(var game in games)game.Dispose();}}
 void Capture(){int com=CoInitializeEx(IntPtr.Zero,0);IAudioClient? client=null;ICaptureClient? capture=null;try{
  OK(com);if(!OperatingSystem.IsWindowsVersionAtLeast(10,0,20348))throw new Exception("디맥 전용 소리 캡처는 Windows 11 이상이 필요합니다.");targetProcessId=requestedProcessId??FindGame();if(TargetProcessId<=0)throw new Exception("디맥 프로세스를 확인하세요.");client=ProcessAudioActivation.Open((uint)TargetProcessId);if(stop.IsCancellationRequested)return;
  // Fixed shared-mode PCM format, converted by Windows independently of playback device.
  var format=Marshal.AllocCoTaskMem(18);try{Marshal.Copy(new byte[18],0,format,18);Marshal.WriteInt16(format,0,1);Marshal.WriteInt16(format,2,2);Marshal.WriteInt32(format,4,44100);Marshal.WriteInt32(format,8,176400);Marshal.WriteInt16(format,12,4);Marshal.WriteInt16(format,14,16);OK(client.Initialize(0,0x80060000,0,0,format,IntPtr.Zero));}finally{Marshal.FreeCoTaskMem(format);}
  using var ready=new AutoResetEvent(false);OK(client.SetEventHandle(ready.SafeWaitHandle.DangerousGetHandle()));var captureId=typeof(ICaptureClient).GUID;OK(client.GetService(ref captureId,out capture));OK(client.Start());initialized=true;long sourceIndex=0;double nextSample=0;float previous=0;bool hasPrevious=false;byte[] bytes=Array.Empty<byte>();var batch=new List<float>(4096);WaitHandle[] waits={stop.Token.WaitHandle,ready};
  while(!stop.IsCancellationRequested){WaitHandle.WaitAny(waits,100);OK(capture.GetNextPacketSize(out uint packet));while(packet>0&&!stop.IsCancellationRequested){OK(capture.GetBuffer(out var data,out uint frames,out uint flags,out _,out _));try{
    if((flags&1)!=0){lock(sync){count=write=0;}hasPrevious=false;nextSample=sourceIndex;}
    int length=checked((int)frames*4);if(bytes.Length<length)bytes=new byte[length];bool silent=(flags&2)!=0;if(!silent)Marshal.Copy(data,bytes,0,length);batch.Clear();
    for(int frame=0;frame<frames;frame++,sourceIndex++){int offset=frame*4;float value=silent?0:(BitConverter.ToInt16(bytes,offset)+BitConverter.ToInt16(bytes,offset+2))/65536f;while(nextSample<=sourceIndex){double fraction=sourceIndex-nextSample;batch.Add(hasPrevious?(float)(value*(1-fraction)+previous*fraction):value);nextSample+=44100d/AudioIdentity.Rate;}previous=value;hasPrevious=true;}Append(batch);
   }finally{OK(capture.ReleaseBuffer(frames));}OK(capture.GetNextPacketSize(out packet));}}
 }catch(Exception e){error="디맥 전용 소리 확인: "+e.Message;lock(sync)count=write=0;}finally{if(client!=null)try{client.Stop();}catch{}foreach(object? value in new object?[]{capture,client})if(value!=null&&Marshal.IsComObject(value))Marshal.FinalReleaseComObject(value);if(com>=0)CoUninitialize();}}
 public void Dispose(){if(Interlocked.Exchange(ref disposed,1)!=0)return;stop.Cancel();_=worker.ContinueWith(_=>stop.Dispose(),TaskScheduler.Default);}
 [ComImport,Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]internal interface IAudioClient{
 [PreserveSig]int Initialize(int mode,uint flags,long duration,long periodicity,IntPtr format,IntPtr session);
 [PreserveSig]int GetBufferSize(out uint frames);[PreserveSig]int GetStreamLatency(out long latency);[PreserveSig]int GetCurrentPadding(out uint padding);[PreserveSig]int IsFormatSupported(int mode,IntPtr format,out IntPtr closest);[PreserveSig]int GetMixFormat(out IntPtr format);[PreserveSig]int GetDevicePeriod(out long def,out long min);[PreserveSig]int Start();[PreserveSig]int Stop();[PreserveSig]int Reset();[PreserveSig]int SetEventHandle(IntPtr handle);[PreserveSig]int GetService(ref Guid iid,[MarshalAs(UnmanagedType.Interface)]out ICaptureClient capture);}
 [ComImport,Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]internal interface ICaptureClient{[PreserveSig]int GetBuffer(out IntPtr data,out uint frames,out uint flags,out ulong position,out ulong qpc);[PreserveSig]int ReleaseBuffer(uint frames);[PreserveSig]int GetNextPacketSize(out uint frames);}
}
