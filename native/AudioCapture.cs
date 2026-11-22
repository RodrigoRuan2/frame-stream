using Concentus;
using Concentus.Enums;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Frame;
public sealed class AudioCapture : IDisposable
{
    readonly WasapiLoopbackCapture capture=new();
    readonly BufferedWaveProvider buffer;
    readonly ISampleProvider samples;
    readonly IOpusEncoder encoder;
    readonly float[] floats=new float[1920];
    readonly short[] pcm=new short[1920];
    readonly byte[] encoded=new byte[4000];
    readonly object gate=new();
    bool disposed;
    uint timestamp;
    public event Action<byte[],uint>? Packet;
    public event Action<string>? Failed;
    public AudioCapture()
    {
        buffer=new BufferedWaveProvider(capture.WaveFormat) {BufferDuration=TimeSpan.FromMilliseconds(250),DiscardOnBufferOverflow=true,ReadFully=false};
        ISampleProvider input=buffer.ToSampleProvider();
        if(input.WaveFormat.Channels==1)input=new MonoToStereoSampleProvider(input);
        if(input.WaveFormat.Channels!=2)throw new InvalidOperationException("Use uma saída de áudio estéreo para transmitir o som.");
        samples=input.WaveFormat.SampleRate==48000?input:new WdlResamplingSampleProvider(input,48000);
        encoder=OpusCodecFactory.CreateEncoder(48000,2,OpusApplication.OPUS_APPLICATION_RESTRICTED_LOWDELAY);
        encoder.Bitrate=128000;encoder.Complexity=3;
        capture.DataAvailable+=OnData;
        capture.RecordingStopped+=(_,e)=>{if(e.Exception!=null&&!disposed)Failed?.Invoke(e.Exception.Message);};
    }
    public void Start()=>capture.StartRecording();
    void OnData(object? sender,WaveInEventArgs e)
    {
        lock(gate) {
            if(disposed)return;
            try {
                buffer.AddSamples(e.Buffer,0,e.BytesRecorded);
                int inputFrameBytes=(int)(capture.WaveFormat.AverageBytesPerSecond*.02);
                while(buffer.BufferedBytes>=inputFrameBytes) {
                    int count=samples.Read(floats,0,floats.Length);if(count<1920)Array.Clear(floats,count,1920-count);
                    for(int i=0;i<1920;i++)pcm[i]=(short)Math.Clamp((int)(floats[i]*32767),short.MinValue,short.MaxValue);
                    int length=encoder.Encode(pcm,960,encoded,encoded.Length);
                    Packet?.Invoke(encoded[..length],timestamp);timestamp+=960;
                }
            }catch(Exception ex){Failed?.Invoke(ex.Message);}
        }
    }
    public void Dispose()
    {
        lock(gate) {if(disposed)return;disposed=true;}
        capture.StopRecording();capture.Dispose();encoder.Dispose();
    }
}
