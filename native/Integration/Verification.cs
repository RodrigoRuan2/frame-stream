using Frame;
using Concentus;
using Concentus.Enums;
using SIPSorcery.Net;
using System.Diagnostics;

static class Verification
{
    static void Check(bool value,string label){if(!value)throw new Exception(label);Console.WriteLine("PASS "+label);}
    public static void Run()
    {
        var clock=new MediaClock(90000);ulong origin=3900000000UL<<32;
        uint first=uint.MaxValue-44999;clock.Observe(first,origin);
        clock.Observe(unchecked(first+90000),origin+(4UL<<32));
        Check(clock.NtpFor(unchecked(first+90000))==origin+(1UL<<32),"RTP wrap and delayed delivery retain media time");
        Check(clock.NtpFor(unchecked(first+45000))==origin+(1UL<<31),"Older packet is paired with its own NTP timestamp");
        for(int i=0;i<600;i++)clock.Observe(unchecked(first+90000U+(uint)i*9000000U),origin+((1UL+(ulong)i*100)<<32));
        Check(clock.NtpFor(unchecked(first+90000U+599U*9000000U))==origin+(59901UL<<32),"Clock survives a complete RTP rollover");
        var warmup=new MediaClock(90000);warmup.Observe(0,origin+(1UL<<30));warmup.Observe(90000,origin+(1UL<<32));
        Check(warmup.NtpFor(90000)==origin+(1UL<<32),"Initial encoder warmup delay is excluded from synchronization");
        Check(AudioTimeline.Timestamp(AudioTimeline.Slot(180))==8640,"Stalled audio skips stale slots instead of bursting");
        Check(AudioTimeline.Timestamp(5000000)==unchecked((uint)4800000000UL),"Audio RTP rollover");
        foreach(bool native in new[]{false,true}) {
            OpusCodecFactory.AttemptToUseNativeLibrary=native;
            if(native)NativeAudio.EnsureLoaded();
            using var encoder=OpusCodecFactory.CreateEncoder(48000,2,OpusApplication.OPUS_APPLICATION_RESTRICTED_LOWDELAY,Console.Out);
            encoder.Bitrate=128000;encoder.Complexity=3;
            float[] pcm=new float[1920];byte[] output=new byte[4000];
            for(int i=0;i<pcm.Length;i++)pcm[i]=(float)(.15*Math.Sin(2*Math.PI*440*(i/2)/48000.0));
            for(int i=0;i<1500;i++)encoder.Encode(pcm,960,output,output.Length);
            var process=Process.GetCurrentProcess();var start=process.TotalProcessorTime;var timer=Stopwatch.StartNew();
            for(int i=0;i<1500;i++)encoder.Encode(pcm,960,output,output.Length);
            Console.WriteLine($"OPUS {encoder.GetType().Name}: cpu={(process.TotalProcessorTime-start).TotalMilliseconds:F1} ms; wall={timer.Elapsed.TotalMilliseconds:F1} ms / 1500 frames");
            if(native)Check(encoder.GetType().Name.Contains("Native"),"Native Opus is loaded on Windows x64");
        }
        using var capture=new AudioCapture();
        int packets=0;uint last=0;string? failure=null;long gaps=0;
        capture.Failed+=e=>failure=e;
        capture.Packet+=(_,timestamp)=>{if(packets>0&&timestamp-last!=960)gaps++;last=timestamp;Interlocked.Increment(ref packets);};
        capture.Start();Thread.Sleep(5000);
        Check(failure==null,"WASAPI event capture starts: "+failure);
        Check(packets>=230&&packets<=260,$"Audio clock remains active, including silence: {packets} packets / 5 seconds; skipped={gaps}");
    }
}
