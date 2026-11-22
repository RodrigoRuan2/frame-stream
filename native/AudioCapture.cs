using Concentus;
using Concentus.Enums;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Diagnostics;

namespace Frame;
public sealed class AudioCapture : IDisposable
{
    // Event-driven loopback avoids the default 100 ms polling/bursts.
    sealed class LowLatencyLoopback : WasapiCapture
    {
        public LowLatencyLoopback() : base(WasapiLoopbackCapture.GetDefaultLoopbackCaptureDevice(), true, 20) { }
        protected override AudioClientStreamFlags GetAudioClientStreamFlags() => base.GetAudioClientStreamFlags() | AudioClientStreamFlags.Loopback;
    }
    readonly WasapiCapture capture=new LowLatencyLoopback();
    readonly BufferedWaveProvider buffer;
    readonly ISampleProvider samples;
    readonly IOpusEncoder encoder;
    readonly float[] floats=new float[1920];
    readonly byte[] encoded=new byte[4000];
    readonly object gate=new();
    readonly ManualResetEvent stop=new(false);
    Thread? worker;
    bool disposed;
    public string EncoderName => encoder.GetType().Name;
    public event Action<byte[],uint>? Packet;
    public event Action<string>? Failed;
    public AudioCapture()
    {
        buffer=new BufferedWaveProvider(capture.WaveFormat) {BufferDuration=TimeSpan.FromMilliseconds(100),DiscardOnBufferOverflow=true,ReadFully=true};
        ISampleProvider input=buffer.ToSampleProvider();
        if(input.WaveFormat.Channels==1)input=new MonoToStereoSampleProvider(input);
        if(input.WaveFormat.Channels!=2)throw new InvalidOperationException("Use uma saída de áudio estéreo para transmitir o som.");
        samples=input.WaveFormat.SampleRate==48000?input:new WdlResamplingSampleProvider(input,48000);
        NativeAudio.EnsureLoaded();
        encoder=OpusCodecFactory.CreateEncoder(48000,2,OpusApplication.OPUS_APPLICATION_RESTRICTED_LOWDELAY);
        encoder.Bitrate=128000;encoder.Complexity=3;
        capture.DataAvailable+=OnData;
        capture.RecordingStopped+=(_,e)=>{if(e.Exception!=null&&!disposed)Failed?.Invoke(e.Exception.Message);};
    }
    public void Start()
    {
        capture.StartRecording();
        worker=new Thread(Pump){IsBackground=true,Name="Frame audio",Priority=ThreadPriority.AboveNormal};
        worker.Start();
    }
    void OnData(object? sender,WaveInEventArgs e)
    {
        lock(gate) {
            if(disposed)return;
            // Keep current sound after a stall; never build a growing queue.
            if(buffer.BufferedDuration.TotalMilliseconds+1000.0*e.BytesRecorded/capture.WaveFormat.AverageBytesPerSecond>80)buffer.ClearBuffer();
            buffer.AddSamples(e.Buffer,0,e.BytesRecorded);
        }
    }
    void Pump()
    {
        try {
            using var timer=new AudioDeadline();
            var clock=Stopwatch.StartNew();long nextSlot=1;
            while(!stop.WaitOne(0)) {
                double remaining=nextSlot*20-clock.Elapsed.TotalMilliseconds;
                if(remaining>0 && timer.Wait(remaining,stop))break;
                if(stop.WaitOne(0))break;
                long slot=Math.Max(nextSlot,AudioTimeline.Slot(clock.Elapsed.TotalMilliseconds));
                lock(gate) {
                    if(disposed)break;
                    if(slot>nextSlot)buffer.ClearBuffer();
                    int count=samples.Read(floats,0,floats.Length);
                    if(count<floats.Length)Array.Clear(floats,count,floats.Length-count);
                }
                int length=encoder.Encode(floats,960,encoded,encoded.Length);
                Packet?.Invoke(encoded[..length],AudioTimeline.Timestamp(slot));
                nextSlot=slot+1;
            }
        }catch(Exception ex){if(!disposed)Failed?.Invoke(ex.Message);}
    }
    public void Dispose()
    {
        lock(gate) {if(disposed)return;disposed=true;}
        stop.Set();capture.StopRecording();worker?.Join();capture.Dispose();encoder.Dispose();stop.Dispose();
    }
}
