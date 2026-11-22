'use strict';
const $=id=>document.getElementById(id);
const params=new URLSearchParams(location.hash.slice(1)),host=params.get('room'),key=params.get('key');
let ws,pc,timer,heartbeat,deadline,prev,generation=0,candidates=[],remote=new MediaStream(),lastFrameAt=0;
function notice(text){$('notice').textContent=text;$('notice').hidden=!text;}
function trace(text){console.info('Frame: '+text);}
function stop(){generation++;clearInterval(timer);clearInterval(heartbeat);clearTimeout(deadline);try{ws?.close();pc?.close();}catch{}ws=pc=null;prev=null;candidates=[];remote.getTracks().forEach(t=>t.stop());remote=new MediaStream();$('video').srcObject=null;$('sound').disabled=true;}
function fail(text){stop();$('status').textContent='Transmissão indisponível';$('placeholder').textContent=text;$('overlay').hidden=false;$('retry').hidden=false;notice(text);}
async function join(){
  stop();const g=generation;
  if(!/^frame-native-[a-f0-9]{32}$/.test(host||'')||! /^[a-f0-9]{48}$/.test(key||'')){fail('Convite inválido. Peça um novo link a quem transmite.');return;}
  $('retry').hidden=true;$('overlay').hidden=false;$('placeholder').textContent='Conectando à transmissão…';$('status').textContent='Entrando na sala';notice('');
  for(const id of ['resolution','fps','bitrate','rtt','codec','decode','buffer','loss','dropped'])$(id).textContent='—';
  const id='frame-watch-'+crypto.randomUUID(),cid=crypto.randomUUID();let offerSent=false;const pendingLocal=[];
  const send=(type,payload)=>{if(g===generation&&ws?.readyState===WebSocket.OPEN)ws.send(JSON.stringify({type,dst:host,payload:{...payload,type:'media',key,metadata:{key},connectionId:cid}}));};
  pc=new RTCPeerConnection({iceServers:[{urls:'stun:stun.l.google.com:19302'}],bundlePolicy:'max-bundle'});
  const v=pc.addTransceiver('video',{direction:'recvonly'});pc.addTransceiver('audio',{direction:'recvonly'});
  const codecs=RTCRtpReceiver.getCapabilities('video')?.codecs.filter(c=>c.mimeType.toLowerCase()==='video/h264');
  if(codecs?.length&&v.setCodecPreferences)v.setCodecPreferences(codecs);
  pc.onicecandidate=e=>{if(e.candidate){if(offerSent)send('CANDIDATE',{candidate:e.candidate.toJSON()});else pendingLocal.push(e.candidate.toJSON());}};
  pc.ontrack=e=>{if(g!==generation)return;remote.addTrack(e.track);$('video').srcObject=remote;try{if('jitterBufferTarget'in e.receiver)e.receiver.jitterBufferTarget=0;}catch{}
    if(e.track.kind==='audio'){$('sound').disabled=false;$('audio').textContent='Disponível · clique em Ativar áudio';}
    $('video').play().catch(()=>{if(g===generation)notice('Clique no vídeo para iniciar a reprodução.');});
  };
  pc.onconnectionstatechange=()=>{if(g!==generation)return;const state=pc.connectionState;
    if(state==='connected'){$('status').textContent='Ao vivo';clearTimeout(deadline);clearInterval(timer);lastFrameAt=Date.now();timer=setInterval(()=>stats(g),1000);notice('');}
    if(state==='disconnected'){$('status').textContent='Reconectando…';notice('A conexão foi interrompida. Aguardando recuperação.');clearTimeout(deadline);deadline=setTimeout(()=>{if(g===generation)fail('A transmissão foi interrompida. Tente entrar novamente.');},15000);}
    if(state==='failed')fail('Não foi possível conectar o vídeo. A sala pode ter encerrado ou esta rede pode precisar de TURN.');
  };
  ws=new WebSocket(`wss://0.peerjs.com/peerjs?key=peerjs&id=${encodeURIComponent(id)}&token=${crypto.randomUUID()}&version=1.5.5`);
  ws.onopen=()=>trace('Sinalização aberta');
  let chain=Promise.resolve();
  ws.onmessage=event=>{chain=chain.then(async()=>{
    if(g!==generation)return;const m=JSON.parse(event.data);trace('Sinalização '+m.type);
    if(m.type==='OPEN') {heartbeat=setInterval(()=>{if(ws?.readyState===WebSocket.OPEN)ws.send('{"type":"HEARTBEAT"}');},5000);
      await pc.setLocalDescription(await pc.createOffer());send('OFFER',{sdp:pc.localDescription.toJSON()});offerSent=true;for(const candidate of pendingLocal)send('CANDIDATE',{candidate});pendingLocal.length=0;return;}
    if(m.type==='EXPIRE'){fail('Esta sala não está disponível. Peça um novo link a quem transmite.');return;}
    if(m.type==='ERROR'||m.type==='ID-TAKEN'){fail('O serviço de salas não conseguiu conectar. Tente novamente.');return;}
    if(m.src!==host||m.payload?.key!==key||m.payload?.connectionId!==cid)return;
    const p=m.payload;
    if(m.type==='ANSWER'){if(p.error){fail(p.error);return;}await pc.setRemoteDescription(p.sdp);for(const ice of candidates)await pc.addIceCandidate(ice);candidates=[];if(!p.audio)$('audio').textContent='Transmissor iniciou sem áudio';}
    if(m.type==='CANDIDATE'){if(p.candidate.candidate&&!p.candidate.candidate.startsWith('candidate:'))p.candidate.candidate='candidate:'+p.candidate.candidate;if(pc.remoteDescription)await pc.addIceCandidate(p.candidate);else candidates.push(p.candidate);}
  }).catch(error=>{trace('Negociação: '+error.message);if(g===generation)fail('Falha ao negociar a transmissão. Tente novamente.');});};
  ws.onerror=()=>{if(g===generation&&pc?.connectionState!=='connected')fail('Não foi possível acessar o serviço de salas.');};
  ws.onclose=event=>{trace('Sinalização fechada: '+event.code+' '+event.reason);if(g===generation){clearInterval(heartbeat);if(pc?.connectionState!=='connected')fail('O serviço de conexão foi interrompido.');else notice('Serviço de salas desconectado; o vídeo já conectado pode continuar.');}};
  deadline=setTimeout(()=>{if(g===generation)fail('A sala não respondeu. Confira se o transmissor está aberto. Algumas redes precisam de TURN.');},35000);
}
async function stats(g){
  try{const reports=await pc.getStats();if(g!==generation)return;let r,pair;reports.forEach(x=>{if(x.type==='inbound-rtp'&&x.kind==='video')r=x;if(x.type==='transport')pair=reports.get(x.selectedCandidatePairId);});if(!r)return;
    if(r.frameWidth)$('resolution').textContent=`${r.frameWidth} × ${r.frameHeight}`;
    $('fps').textContent=`${Math.round(r.framesPerSecond||0)} FPS`;$('loss').textContent=Math.max(0,r.packetsLost||0);$('dropped').textContent=r.framesDropped||0;
    $('codec').textContent=reports.get(r.codecId)?.mimeType||'H.264';$('decoder').textContent=(r.decoderImplementation||'Não informado')+(r.powerEfficientDecoder===true?' · eficiente':'');$('rtt').textContent=pair?.currentRoundTripTime!=null?Math.round(pair.currentRoundTripTime*1000)+' ms':'—';
    if(prev){let dt=(r.timestamp-prev.timestamp)/1000,frames=r.framesDecoded-prev.framesDecoded,emitted=r.jitterBufferEmittedCount-prev.jitterBufferEmittedCount;
      if(dt>0)$('bitrate').textContent=((r.bytesReceived-prev.bytesReceived)*8/dt/1e6).toFixed(1)+' Mbps';
      if(frames>0){$('decode').textContent=((r.totalDecodeTime-prev.totalDecodeTime)*1000/frames).toFixed(1)+' ms / quadro';lastFrameAt=Date.now();$('overlay').hidden=true;}
      if(emitted>0)$('buffer').textContent=((r.jitterBufferDelay-prev.jitterBufferDelay)*1000/emitted).toFixed(1)+' ms';
    }prev=r;
    if(Date.now()-lastFrameAt>7000){$('overlay').hidden=false;$('placeholder').textContent='Aguardando novos quadros…';}
  }catch{}
}
$('sound').onclick=async()=>{const v=$('video');v.muted=!v.muted;await v.play();$('sound').textContent=v.muted?'Ativar áudio':'Silenciar';$('audio').textContent=v.muted?'Disponível · silenciado':'Reproduzindo';};
$('video').onclick=()=>$('video').play().catch(()=>{});
$('fullscreen').onclick=()=>$('stage').requestFullscreen().catch(()=>notice('Tela cheia indisponível neste navegador.'));
$('retry').onclick=()=>join();$('leave').onclick=()=>{stop();$('status').textContent='Você saiu';$('overlay').hidden=false;$('placeholder').textContent='Até a próxima partida.';$('retry').hidden=false;};
window.addEventListener('beforeunload',stop);join();
