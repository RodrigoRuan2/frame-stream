using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Collections.Concurrent;

namespace Frame;
public sealed class VideoEncoder : IDisposable
{
    public event Action<byte[], uint, int>? Packet;
    public event Action<string>? Failed;
    public long Frames, Bytes;
    public double Fps;
    public TimeSpan CpuTime { get { try{return process?.TotalProcessorTime??TimeSpan.Zero;}catch{return TimeSpan.Zero;} } }
    public string LastError => string.Join(Environment.NewLine, errors);
    readonly ConcurrentQueue<string> errors = new();
    readonly CancellationTokenSource stop = new();
    UdpClient? receiver;
    Process? process;
    ChildProcessJob? job;
    Task? reader;
    readonly TaskCompletionSource firstFrame = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public async Task Start(CaptureSource source, int height, int fps, int mbps)
    {
        string ffmpeg = Path.Combine(AppContext.BaseDirectory,"media","ffmpeg.exe");
        if (!File.Exists(ffmpeg)) throw new FileNotFoundException("Motor de vídeo ausente. Reinstale o Frame.");
        receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback,0));
        receiver.Client.ReceiveBufferSize = 4*1024*1024;
        int port = ((IPEndPoint)receiver.Client.LocalEndPoint!).Port;
        reader = ReadPackets();
        var start = new ProcessStartInfo(ffmpeg) { UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true };
        void Add(params string[] args) { foreach(var a in args) start.ArgumentList.Add(a); }
        Add("-hide_banner","-nostdin","-loglevel","warning","-stats_period","1","-progress","pipe:1");
        if (source.Synthetic) Add("-re","-f","lavfi","-i",$"testsrc2=size={height*16/9}x{height}:rate={fps}","-vf","format=nv12");
        else Add("-filter_complex",$"gfxcapture={source.Filter}:max_framerate={fps*2}:width={height*16/9}:height={height}:resize_mode=scale_aspect:display_border=1:dup_frames=1,fps=fps={fps}:round=near");
        Add("-an","-c:v","h264_nvenc","-preset","p3","-tune","ull","-profile:v","baseline","-level:v","4.2",
            "-rc","vbr","-b:v",$"{mbps}M","-maxrate",$"{mbps}M","-bufsize",$"{mbps*200}k",
            "-rc-lookahead","0","-bf","0","-g",fps.ToString(),"-zerolatency","1","-forced-idr","1",
            "-fps_mode","passthrough","-flush_packets","1","-f","rtp","-payload_type","96",$"rtp://127.0.0.1:{port}?pkt_size=1150");
        process = new Process { StartInfo=start, EnableRaisingEvents=true };
        process.ErrorDataReceived += (sender,e) => { if (!string.IsNullOrWhiteSpace(e.Data)) { errors.Enqueue(e.Data); while(errors.Count>16)errors.TryDequeue(out _); } };
        process.OutputDataReceived += (_,e) => { if(e.Data?.StartsWith("fps=")==true && double.TryParse(e.Data[4..],System.Globalization.CultureInfo.InvariantCulture,out var f)) Fps=f; };
        process.Exited += (_,_) => { if(!stop.IsCancellationRequested) { var message="A captura parou. " + LastError; firstFrame.TrySetException(new InvalidOperationException(message)); Failed?.Invoke(message); } };
        process.Start();job=new ChildProcessJob(process.Handle);process.BeginErrorReadLine(); process.BeginOutputReadLine();
        await firstFrame.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }
    async Task ReadPackets()
    {
        try {
            while(!stop.IsCancellationRequested) {
                var d=await receiver!.ReceiveAsync(stop.Token); var b=d.Buffer;
                if(b.Length<12 || b[0]>>6!=2)continue;
                int offset=12+(b[0]&15)*4;
                if((b[0]&16)!=0) { if(b.Length<offset+4)continue; offset+=4+((b[offset+2]<<8)|b[offset+3])*4; }
                int end=b.Length-((b[0]&32)!=0?b[^1]:0); if(end<=offset)continue;
                uint timestamp=System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(4,4));
                int marker=b[1]>>7; if(marker==1) { Interlocked.Increment(ref Frames); firstFrame.TrySetResult(); }
                Interlocked.Add(ref Bytes,end-offset);
                Packet?.Invoke(b[offset..end],timestamp,marker);
            }
        } catch(OperationCanceledException){} catch(ObjectDisposedException){} catch(Exception e) { if(!stop.IsCancellationRequested)Failed?.Invoke(e.Message); }
    }
    public void Dispose()
    {
        if(stop.IsCancellationRequested)return; stop.Cancel(); receiver?.Dispose();
        try { if(process is {HasExited:false}) { process.Kill(true); process.WaitForExit(3000); } } catch {}
        process?.Dispose();job?.Dispose();
    }
}
