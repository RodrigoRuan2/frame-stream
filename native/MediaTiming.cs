using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using SIPSorcery.Net;

namespace Frame;
public static class AudioTimeline
{
    public static long Slot(double milliseconds)=>(long)(milliseconds/20);
    public static uint Timestamp(long slot)=>unchecked((uint)(slot*960));
}

// Sleeps without spinning or changing the system-wide timer resolution.
sealed class AudioDeadline : IDisposable
{
    sealed class TimerHandle : WaitHandle { public TimerHandle(SafeWaitHandle handle){SafeWaitHandle=handle;} }
    readonly TimerHandle timer;
    [DllImport("kernel32.dll",SetLastError=true,CharSet=CharSet.Unicode)]
    static extern SafeWaitHandle CreateWaitableTimerEx(IntPtr attributes,string? name,uint flags,uint access);
    [DllImport("kernel32.dll",SetLastError=true)]
    static extern bool SetWaitableTimer(SafeWaitHandle handle,ref long dueTime,int period,IntPtr completion,IntPtr arg,bool resume);
    public AudioDeadline() {
        var handle=CreateWaitableTimerEx(IntPtr.Zero,null,2,0x1F0003);
        if(handle.IsInvalid){handle.Dispose();handle=CreateWaitableTimerEx(IntPtr.Zero,null,0,0x1F0003);}
        if(handle.IsInvalid)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        timer=new TimerHandle(handle);
    }
    public bool Wait(double milliseconds,WaitHandle stop) {
        long due=-(long)Math.Max(1,milliseconds*10000);
        if(!SetWaitableTimer(timer.SafeWaitHandle,ref due,0,IntPtr.Zero,IntPtr.Zero,false))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        return WaitHandle.WaitAny([stop,timer])==0;
    }
    public void Dispose()=>timer.Dispose();
}

// Pair RTP with its actual media time, not the later RTCP timer callback.
public sealed class MediaClock(int rate)
{
    readonly object gate=new();
    bool initialized;
    uint latest;
    long ticks;
    ulong origin;
    public void Observe(uint timestamp,ulong ntp) {
        lock(gate) {
            if(!initialized){initialized=true;latest=timestamp;origin=ntp;return;}
            ticks+=unchecked((int)(timestamp-latest));latest=timestamp;
            // Encoder startup can delay the first packet. Calibrate against the
            // earliest delivery during warmup, then keep the clock stable.
            if(ticks>=0&&ticks<rate*10L) {
                ulong elapsed=(ulong)((Int128)ticks*((long)1<<32)/rate);
                if(ntp>=elapsed)origin=Math.Min(origin,ntp-elapsed);
            }
        }
    }
    public ulong NtpFor(uint timestamp) {
        lock(gate) {
            long offset=ticks+unchecked((int)(timestamp-latest));
            return unchecked(origin+(ulong)((Int128)offset*((long)1<<32)/rate));
        }
    }
    public void Correct(RTCPCompoundPacket report) {
        lock(gate)if(initialized && report.SenderReport is {} sr)sr.NtpTimestamp=NtpFor(sr.RtpTimestamp);
    }
    public static ulong Now() {
        long ticks=DateTime.UtcNow.Ticks-new DateTime(1900,1,1).Ticks;
        return (ulong)((Int128)ticks*((long)1<<32)/TimeSpan.TicksPerSecond);
    }
}

public static class NativeAudio
{
    // Load only the shipped binary, independently of the shortcut working directory.
    static readonly Lazy<IntPtr> library=new(()=> {
        string path=Path.Combine(AppContext.BaseDirectory,"opus.dll");
        if(!File.Exists(path))path=Path.Combine(AppContext.BaseDirectory,"runtimes","win-x64","native","opus.dll");
        return NativeLibrary.Load(path);
    });
    public static void EnsureLoaded() {
        try{_ = library.Value;}
        catch(DllNotFoundException){Concentus.OpusCodecFactory.AttemptToUseNativeLibrary=false;}
        catch(BadImageFormatException){Concentus.OpusCodecFactory.AttemptToUseNativeLibrary=false;}
    }
}
