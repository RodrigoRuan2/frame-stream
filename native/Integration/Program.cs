using Frame;
using System.Diagnostics;
using System.Text.Json;

bool withAudio=args.Contains("--audio");
using var room=new Room(withAudio);
room.Diagnostic+=Console.WriteLine;room.Status+=Console.WriteLine;
using var video=new VideoEncoder();video.Packet+=room.SendVideo;video.Failed+=Console.WriteLine;
var source=args.Length>1&&args[0]=="--window"?new CaptureSource("Window test","hwnd="+long.Parse(args[1])):new CaptureSource("Test pattern","",true);
using var sound=withAudio?new AudioCapture():null;
if(sound!=null){sound.Packet+=room.SendAudio;sound.Failed+=Console.WriteLine;sound.Start();}
await video.Start(source,1080,60,16);
await room.Start();
await File.WriteAllTextAsync(Path.Combine(AppContext.BaseDirectory,"invitation.txt"),room.Invite);
Console.WriteLine("READY");
var sw=Stopwatch.StartNew();long last=video.Frames;var lastTime=sw.Elapsed;bool canceled=false;
Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;canceled=true;};
while(!canceled&&sw.Elapsed.TotalSeconds<180){await Task.Delay(5000);var f=video.Frames;var t=sw.Elapsed;Console.WriteLine(JsonSerializer.Serialize(new{seconds=t.TotalSeconds,fps=(f-last)/(t-lastTime).TotalSeconds,viewers=room.Count}));last=f;lastTime=t;}
