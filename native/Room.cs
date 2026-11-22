using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;

namespace Frame;
public sealed class Room : IDisposable
{
    public event Action<string>? Status;
    public event Action<string>? Diagnostic;
    public string Id { get; } = "frame-native-"+Guid.NewGuid().ToString("N");
    readonly string key=Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
    readonly string signalToken=Guid.NewGuid().ToString("N");
    public string Invite => $"https://rodrigoruan2.github.io/frame-stream/watch.html#room={Id}&key={key}";
    public int Count => guests.Values.Count(g=>g.Pc.connectionState==RTCPeerConnectionState.connected);
    public bool Audio {get;}
    readonly ConcurrentDictionary<string,Guest> guests=new();
    readonly CancellationTokenSource stop=new();
    readonly SemaphoreSlim sendLock=new(1,1);
    ClientWebSocket? socket;
    Task? loop;
    readonly TaskCompletionSource opened=new(TaskCreationOptions.RunContinuationsAsynchronously);
    record Guest(RTCPeerConnection Pc,string ConnectionId) { public int VideoPt=96,AudioPt=111; public DateTime Created=DateTime.UtcNow; }
    public Room(bool audio) { Audio=audio; }
    public async Task Start()
    {
        loop=Run();
        await opened.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }
    async Task Run()
    {
        while(!stop.IsCancellationRequested) {
            try {
                using var ws=new ClientWebSocket(); socket=ws;
                await ws.ConnectAsync(new Uri($"wss://0.peerjs.com/peerjs?key=peerjs&id={Id}&token={signalToken}&version=1.5.5"),stop.Token);
                var heartbeat=Heartbeat(ws);
                var buffer=new byte[65536];
                try {
                    while(ws.State==WebSocketState.Open&&!stop.IsCancellationRequested) {
                        using var message=new MemoryStream(); WebSocketReceiveResult r;
                        do { r=await ws.ReceiveAsync(buffer,stop.Token); if(r.MessageType==WebSocketMessageType.Close)break;
                            message.Write(buffer,0,r.Count); if(message.Length>65536)throw new InvalidDataException("Mensagem de conexão grande demais.");
                        } while(!r.EndOfMessage);
                        if(r.MessageType==WebSocketMessageType.Close)break;
                        using var doc=JsonDocument.Parse(message.ToArray()); await Handle(doc.RootElement);
                    }
                } finally { ws.Abort(); try{await heartbeat;}catch{} }
            } catch(OperationCanceledException) {break;} catch(Exception e) { Diagnostic?.Invoke("Sinalização: "+e.Message); }
            if(!stop.IsCancellationRequested) { Status?.Invoke("Reconectando serviço de salas…"); try{await Task.Delay(2500,stop.Token);}catch{break;} }
        }
    }
    async Task Heartbeat(ClientWebSocket ws)
    {
        while(!stop.IsCancellationRequested&&ws.State==WebSocketState.Open) {
            await Task.Delay(5000,stop.Token); if(ws.State!=WebSocketState.Open)break;
            await Send(new {type="HEARTBEAT"});
            foreach(var (id,g) in guests)if(g.Pc.connectionState!=RTCPeerConnectionState.connected && DateTime.UtcNow-g.Created>TimeSpan.FromSeconds(45))Remove(id);
        }
    }
    async Task Send(object message)
    {
        await sendLock.WaitAsync(stop.Token);
        try { if(socket?.State==WebSocketState.Open)await socket.SendAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message)),WebSocketMessageType.Text,true,stop.Token); }
        finally {sendLock.Release();}
    }
    void SendSoon(object message) { _=Task.Run(async()=>{try{await Send(message);}catch{}}); }
    async Task Handle(JsonElement message)
    {
        string type=message.GetProperty("type").GetString()??"";
        if(type=="OPEN") {opened.TrySetResult();Status?.Invoke("Sala aberta · aguardando amigos");return;}
        if(type is "ERROR" or "ID-TAKEN" or "INVALID-KEY")throw new InvalidOperationException("Serviço de salas recusou a conexão.");
        if(!message.TryGetProperty("src",out var s)||!message.TryGetProperty("payload",out var p))return;
        var src=s.GetString(); if(string.IsNullOrEmpty(src)||src.Length>150)return;
        if(!p.TryGetProperty("key",out var k)||k.GetString()!=key)return;
        if(!p.TryGetProperty("connectionId",out var c))return; var cid=c.GetString()??"";
        if(type=="OFFER") {
            if(guests.ContainsKey(src))return;
            if(guests.Count>=4) {await Send(new {type="ANSWER",dst=src,payload=new {connectionId=cid,key,error="A sala está cheia (4 espectadores)."}});return;}
            RTCPeerConnection? pc=null;
            try {
                var config=new RTCConfiguration {iceServers=new List<RTCIceServer>{new(){urls="stun:stun.l.google.com:19302"}}};
                pc=new RTCPeerConnection(config);var guest=new Guest(pc,cid);guests[src]=guest;
                pc.addTrack(new MediaStreamTrack(new VideoFormat(VideoCodecsEnum.H264,96,90000,"packetization-mode=1;profile-level-id=42e02a;level-asymmetry-allowed=1"),MediaStreamStatusEnum.SendOnly));
                if(Audio)pc.addTrack(new MediaStreamTrack(new AudioFormat(AudioCodecsEnum.OPUS,111,48000,2,"minptime=10;useinbandfec=1;stereo=1"),MediaStreamStatusEnum.SendOnly));
                pc.OnVideoFormatsNegotiated += formats=>guest.VideoPt=formats[0].FormatID;
                pc.OnAudioFormatsNegotiated += formats=>guest.AudioPt=formats[0].FormatID;
                pc.onicecandidate += candidate=>{if(candidate!=null)SendSoon(new {type="CANDIDATE",dst=src,payload=new {type="media",connectionId=cid,key,candidate=JsonSerializer.Deserialize<JsonElement>(candidate.toJSON())}});};
                pc.onconnectionstatechange += state=>{
                    Diagnostic?.Invoke("Conexão de espectador: "+state);
                    if(state is RTCPeerConnectionState.failed or RTCPeerConnectionState.closed)Remove(src);
                    Status?.Invoke(Count>0?$"Transmitindo · {Count} espectador(es)":"Sala aberta · aguardando amigos");
                };
                var offer=p.GetProperty("sdp").GetProperty("sdp").GetString()!;
                var result=pc.setRemoteDescription(new RTCSessionDescriptionInit {type=RTCSdpType.offer,sdp=offer});
                if(result!=SetDescriptionResultEnum.OK)throw new InvalidOperationException("Não foi possível negociar H.264: "+result);
                var answer=pc.createAnswer(); await pc.setLocalDescription(answer);
                await Send(new {type="ANSWER",dst=src,payload=new {type="media",connectionId=cid,key,sdp=new{type="answer",sdp=answer.sdp},audio=Audio}});
                Diagnostic?.Invoke("Resposta de vídeo enviada.");
            } catch(Exception e) {Remove(src); pc?.Close("Falha de negociação"); Diagnostic?.Invoke(e.Message);await Send(new {type="ANSWER",dst=src,payload=new {connectionId=cid,key,error="Falha ao conectar vídeo. Tente novamente."}});}
        } else if(guests.TryGetValue(src,out var g)&&g.ConnectionId==cid) {
            if(type=="CANDIDATE"&&p.TryGetProperty("candidate",out var ice)) {
                try {g.Pc.addIceCandidate(new RTCIceCandidateInit {candidate=ice.GetProperty("candidate").GetString(),sdpMid=ice.TryGetProperty("sdpMid",out var mid)?mid.GetString():null,sdpMLineIndex=ice.TryGetProperty("sdpMLineIndex",out var line)?line.GetUInt16():(ushort)0});}catch(Exception e){Diagnostic?.Invoke(e.Message);}
            }else if(type=="LEAVE")Remove(src);
        }
    }
    void Remove(string id) { if(guests.TryRemove(id,out var g))g.Pc.Close("Espectador saiu"); }
    public void SendVideo(byte[] payload,uint timestamp,int marker)
    {
        foreach(var g in guests.Values)if(g.Pc.connectionState==RTCPeerConnectionState.connected)
            try {g.Pc.SendRtpRaw(SDPMediaTypesEnum.video,payload,timestamp,marker,g.VideoPt);}catch{}
    }
    public void SendAudio(byte[] payload,uint timestamp)
    {
        foreach(var g in guests.Values)if(g.Pc.connectionState==RTCPeerConnectionState.connected)
            try {g.Pc.SendRtpRaw(SDPMediaTypesEnum.audio,payload,timestamp,0,g.AudioPt);}catch{}
    }
    public void Dispose()
    {
        if(stop.IsCancellationRequested)return; stop.Cancel(); socket?.Abort();
        foreach(var id in guests.Keys)Remove(id);
    }
}
