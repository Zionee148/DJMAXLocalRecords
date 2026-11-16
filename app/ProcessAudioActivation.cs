using System.Runtime.InteropServices;
namespace LadderRecords;
public static class ProcessAudioActivation
{
 [StructLayout(LayoutKind.Sequential)]struct ActivationParameters{public int Type;public uint ProcessId;public int Mode;}
 [StructLayout(LayoutKind.Sequential)]struct Blob{public uint Size;public IntPtr Data;}
 [StructLayout(LayoutKind.Explicit,Size=24)]struct PropVariant{[FieldOffset(0)]public ushort Type;[FieldOffset(8)]public Blob Blob;}
 [DllImport("Mmdevapi.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]static extern int ActivateAudioInterfaceAsync(string device,ref Guid iid,ref PropVariant parameters,ICompletion completion,out IOperation operation);
 [ComVisible(true),Guid("41D949AB-9862-444A-80F6-C261334DA5EB"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]public interface ICompletion{[PreserveSig]int ActivateCompleted(IOperation operation);}
 [ComVisible(true),Guid("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]public interface IAgile{}
 [ComImport,Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]public interface IOperation{[PreserveSig]int GetActivateResult(out int result,[MarshalAs(UnmanagedType.IUnknown)]out object? activated);}
 [ComVisible(true),ClassInterface(ClassInterfaceType.None)]public sealed class Completion:ICompletion,IAgile
 {
  internal readonly TaskCompletionSource<LoopbackAudio.IAudioClient> Result=new(TaskCreationOptions.RunContinuationsAsynchronously);
  public int ActivateCompleted(IOperation operation){object? activated=null;try{Marshal.ThrowExceptionForHR(operation.GetActivateResult(out int hr,out activated));Marshal.ThrowExceptionForHR(hr);var client=(LoopbackAudio.IAudioClient)(activated??throw new Exception("디맥 소리 캡처를 시작하지 못했습니다."));activated=null;Result.TrySetResult(client);}catch(Exception e){Result.TrySetException(e);}finally{if(activated!=null&&Marshal.IsComObject(activated))Marshal.FinalReleaseComObject(activated);}return 0;}
 }
 internal static LoopbackAudio.IAudioClient Open(uint processId){var data=Marshal.AllocCoTaskMem(Marshal.SizeOf<ActivationParameters>());IOperation? operation=null;var completion=new Completion();try{Marshal.StructureToPtr(new ActivationParameters{Type=1,ProcessId=processId,Mode=0},data,false);var parameters=new PropVariant{Type=65,Blob=new Blob{Size=(uint)Marshal.SizeOf<ActivationParameters>(),Data=data}};var iid=typeof(LoopbackAudio.IAudioClient).GUID;Marshal.ThrowExceptionForHR(ActivateAudioInterfaceAsync(@"VAD\Process_Loopback",ref iid,ref parameters,completion,out operation));return completion.Result.Task.GetAwaiter().GetResult();}finally{GC.KeepAlive(completion);if(operation!=null)Marshal.FinalReleaseComObject(operation);Marshal.FreeCoTaskMem(data);}}
}
