using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Frame;
// Windows closes the encoder even if the app crashes or is ended externally.
sealed class ChildProcessJob : IDisposable
{
    readonly SafeFileHandle handle;
    public ChildProcessJob(nint process)
    {
        handle=CreateJobObject(0,null);
        var info=new ExtendedLimits {Basic=new BasicLimits {LimitFlags=0x2000}};
        if(handle.IsInvalid||!SetInformationJobObject(handle,9,ref info,(uint)Marshal.SizeOf<ExtendedLimits>())||!AssignProcessToJobObject(handle,process)) {
            handle.Dispose();throw new InvalidOperationException("Não foi possível proteger o encerramento do motor de captura.");
        }
    }
    public void Dispose()=>handle.Dispose();
    [StructLayout(LayoutKind.Sequential)]struct BasicLimits {public long ProcessTime,JobTime;public uint LimitFlags;public nuint MinimumWorkingSet,MaximumWorkingSet;public uint ActiveProcessLimit;public nuint Affinity;public uint PriorityClass,SchedulingClass;}
    [StructLayout(LayoutKind.Sequential)]struct IoCounters {public ulong ReadOperations,WriteOperations,OtherOperations,ReadBytes,WriteBytes,OtherBytes;}
    [StructLayout(LayoutKind.Sequential)]struct ExtendedLimits {public BasicLimits Basic;public IoCounters Io;public nuint ProcessMemoryLimit,JobMemoryLimit,PeakProcessMemory,PeakJobMemory;}
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern SafeFileHandle CreateJobObject(nint attributes,string? name);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool SetInformationJobObject(SafeFileHandle job,int infoClass,ref ExtendedLimits info,uint size);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool AssignProcessToJobObject(SafeFileHandle job,nint process);
}
