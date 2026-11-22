'use strict';
const $ = id => document.getElementById(id);
const MAX_GUESTS = 4;
const RTC_CONFIG = {iceServers:[{urls:'stun:stun.l.google.com:19302'}], bundlePolicy:'max-bundle'};
let mode='host', peer=null, stream=null, remoteStream=null, roomKey='', invite=null;
let generation=0, busy=false, demoSource=false, animation=null, statsTimer=null, statsBusy=false;
let previewHidden=false, reconnectAttempts=0, firstConnected=false;
const clients=new Map(), timers=new Set(), samples=new Map();
const quality=()=>({height:+$('quality').value, fps:+$('framerate').value, bitrate:+$('rate').value*1e6});
function later(fn, ms) { const id=setTimeout(()=>{timers.delete(id);fn();},ms);timers.add(id);return id; }
function cancelLater(id) {clearTimeout(id);timers.delete(id);}
function message(text, kind='') { $('message').textContent=text; $('message').className='message '+kind; $('message').hidden=!text; }
function status(text, live=false) { $('status').textContent=text; $('dot').classList.toggle('live',live); }
function controls() {
  const active=!!peer||!!stream;
  for(const id of ['start','demo','hostTab','viewerTab','surface','codec','withAudio']) $(id).disabled=busy||active;
  $('join').disabled=busy||active; $('stop').disabled=!active&&!busy;
  $('apply').disabled=busy||!stream||mode!=='host';
  $('fullscreen').disabled=$('video').hidden; $('retry').disabled=busy;
}
function resetMetrics() {
  for(const id of ['resolution','fps','bitrate','rtt','codecStat','encoderStat','encodeStat','limitStat','lossStat','audioStat']) $(id).textContent='—';
  $('guestCount').textContent='0 / '+MAX_GUESTS; samples.clear();
}
function setMode(value) {
  mode=value; $('hostTab').setAttribute('aria-selected',String(mode==='host')); $('viewerTab').setAttribute('aria-selected',String(mode==='viewer'));
  $('settings').hidden=mode!=='host'; $('testCard').hidden=mode!=='host';
  $('viewerConnect').hidden=mode!=='viewer'||!!invite; $('guestStatus').hidden=mode!=='viewer'||!invite;
  $('rateLabel').textContent=mode==='host'?'Upload de vídeo':'Vídeo recebido';
  $('placeholderTitle').textContent=mode==='host'?'O próximo play começa aqui.':'Seu lugar na primeira fila.';
  $('placeholderText').textContent=mode==='host'?'Compartilhe a tela inteira ou apenas uma janela. Seus amigos entram pelo link.':'Conectando à sala do seu amigo. Você não precisa compartilhar nada.';
  controls();
}
function cleanup(notify=true) {
  generation++; busy=false;
  for(const entry of clients.values()) {if(notify&&mode==='host'&&entry.data?.open) {try{entry.data.send({type:'ended'});}catch{}}}
  const old=peer; peer=null;
  for(const id of timers) clearTimeout(id);timers.clear();
  if(statsTimer)clearInterval(statsTimer);statsTimer=null;
  if(animation)cancelAnimationFrame(animation);animation=null;
  clients.clear(); if(old)old.destroy();
  for(const t of stream?.getTracks()||[])t.stop(); for(const t of remoteStream?.getTracks()||[])t.stop();
  stream=remoteStream=null; roomKey=''; demoSource=false;firstConnected=false;previewHidden=false;
  $('video').pause();$('video').srcObject=null;$('video').hidden=true;$('video').muted=true;
  $('placeholder').hidden=false;$('inviteCard').hidden=true;$('inviteLink').value='';
  $('sound').hidden=true;$('sound').textContent='Ativar áudio';$('previewToggle').hidden=true;$('previewToggle').textContent='Ocultar prévia';
  $('sourceLabel').textContent='Nenhuma tela compartilhada';$('stageType').textContent='Até 4 amigos · conexão direta';
  resetMetrics();status('Pronto para começar');controls();
}
function fail(text) {cleanup(false);message(text,'error');status('Não foi possível entrar');if(mode==='viewer')$('retry').hidden=false;}
function assertCurrent(g) {if(g!==generation)throw new DOMException('Operação encerrada.','AbortError');}
function peerError(err) {
  return ({'peer-unavailable':'Esta sala não está mais disponível. Peça ao seu amigo um novo link.',
    'network':'Não foi possível acessar o serviço de conexão. Verifique a internet e tente novamente.',
    'server-error':'O serviço de conexão está indisponível. Tente novamente em instantes.',
    'socket-error':'A conexão com o serviço foi interrompida. Tente novamente.',
    'browser-incompatible':'Seu navegador não oferece os recursos necessários. Use o Chrome atualizado.',
    'unavailable-id':'Não foi possível abrir esta sala. Tente criar uma nova.'})[err.type]||'Falha na conexão. Tente novamente; algumas redes precisam de um servidor TURN, ainda não incluído.';
}
async function openPeer(g) {
  if(typeof Peer==='undefined')throw new Error('A biblioteca de conexão não carregou. Recarregue a página.');
  reconnectAttempts=0;
  const p=new Peer('frame-'+crypto.randomUUID(),{config:RTC_CONFIG,secure:true,debug:0});peer=p;
  p.on('connection',data=>{
    if(g!==generation||mode!=='host'||!stream){data.close();return;}
    acceptGuest(data,g);
  });
  p.on('call',call=>{
    if(g!==generation||mode!=='viewer'||call.peer!==invite?.host||call.metadata?.key!==invite?.key){call.close();return;}
    const entry=clients.get(invite.host);
    if(!entry||entry.call){call.close();return;}
    entry.call=call; attachCall(entry,g); call.answer();
    call.on('stream',s=>{
      if(g!==generation)return;remoteStream=s;cancelLater(entry.deadline);
      showVideo(s,true);$('sourceLabel').textContent='Ao vivo · sala do seu amigo';
      $('audioStat').textContent=s.getAudioTracks().length?'Disponível; ative no player':'Sem áudio na captura';
      status('Assistindo ao vivo',true);message('Você entrou na sala. Ative o áudio para ouvir, se estiver disponível.');
    });
  });
  p.on('disconnected',()=>{
    if(g!==generation)return;
    message('Reconectando ao serviço de salas. O vídeo já conectado pode continuar.','warn');
    if(reconnectAttempts<3)later(()=>{if(g===generation&&p.disconnected&&!p.destroyed){reconnectAttempts++;p.reconnect();}},1000*2**reconnectAttempts);
    else message('O serviço de salas caiu. Conexões existentes podem continuar, mas novos amigos talvez não consigam entrar. Encerre e crie outra sala para tentar novamente.','warn');
  });
  let opened=false;
  p.on('open',()=>{if(g!==generation)return;if(opened){reconnectAttempts=0;message('Conexão com o serviço de salas recuperada.');}});
  await new Promise((resolve,reject)=>{
    const deadline=later(()=>reject(new Error('O serviço de salas não respondeu. Verifique a conexão e tente novamente.')),20000);
    p.once('open',()=>{opened=true;cancelLater(deadline);resolve();});
    p.once('close',()=>{if(!opened){cancelLater(deadline);reject(new DOMException('Operação encerrada.','AbortError'));}});
    p.on('error',err=>{
      if(g!==generation)return;
      if(!opened){cancelLater(deadline);reject(new Error(peerError(err)));}
      else if(mode==='viewer'&&!firstConnected)fail(peerError(err));
      else message(peerError(err),'warn');
    });
  });
  assertCurrent(g);return p;
}
function showVideo(s,receiving) {
  const v=$('video');v.srcObject=s;v.muted=true;v.hidden=false;$('placeholder').hidden=true;
  $('sound').hidden=!receiving||!s.getAudioTracks().length;$('previewToggle').hidden=receiving;
  v.play().catch(()=>message('Clique no vídeo para iniciar a reprodução.','warn'));controls();
}
async function capture(g,synthetic) {
  if(!window.isSecureContext)throw new Error('Abra o site por HTTPS ou localhost.');
  const q=quality();
  const captured=synthetic?sampleScene():await navigator.mediaDevices.getDisplayMedia({
    video:{displaySurface:$('surface').value,width:{ideal:Math.round(q.height*16/9)},height:{ideal:q.height},frameRate:{ideal:q.fps,max:q.fps}},
    audio:$('withAudio').checked?{echoCancellation:false,noiseSuppression:false,autoGainControl:false}:false,
    systemAudio:$('withAudio').checked?'include':'exclude',selfBrowserSurface:'exclude',surfaceSwitching:'include',monitorTypeSurfaces:'include'
  });
  if(g!==generation){captured.getTracks().forEach(t=>t.stop());return;}
  stream=captured;demoSource=synthetic;
  stream.getVideoTracks()[0].addEventListener('ended',()=>{if(g===generation){cleanup();message('A captura terminou e a sala foi encerrada.');}},{once:true});
  await applyCapture();assertCurrent(g);showVideo(stream,false);
  const surface=stream.getVideoTracks()[0].getSettings().displaySurface;
  $('sourceLabel').textContent=synthetic?'Cena de teste · prévia local':({monitor:'Tela inteira',window:'Aplicativo / janela',browser:'Aba do Chrome'}[surface]||'Captura selecionada')+' · prévia local';
  $('audioStat').textContent=stream.getAudioTracks().length?'Sim':synthetic?'Cena de teste sem áudio':'Não disponibilizado pelo Chrome';
}
async function createRoom(synthetic=false) {
  if(busy||peer)return;busy=true;const g=generation;controls();message('');status('Preparando sua sala…');
  try {
    await capture(g,synthetic);assertCurrent(g);
    roomKey=crypto.randomUUID().replaceAll('-','');
    const p=await openPeer(g);assertCurrent(g);
    const url=new URL(location.href);url.hash=new URLSearchParams({room:p.id,key:roomKey}).toString();url.search='';
    $('inviteLink').value=url.href;$('inviteCard').hidden=false;status('Sala aberta · aguardando amigos');
    message(synthetic?'Sala de teste criada. Envie o link ou abra-o em outra aba para testar a recepção.':'Sala criada. Envie o link aos amigos e mantenha esta página aberta.');
    if(!synthetic&&$('withAudio').checked&&!stream.getAudioTracks().length)message('Sala criada, mas a captura veio sem áudio. Para incluir som, encerre e marque o compartilhamento de áudio no seletor do Chrome.','warn');
    startStats();
  }catch(e){if(g===generation){cleanup();message(e.name==='NotAllowedError'?'Compartilhamento cancelado. Você pode tentar novamente.':e.name==='NotReadableError'?'O Chrome não conseguiu capturar essa janela. Tente o jogo em janela sem bordas ou compartilhe a tela inteira.':e.message,'error');}}
  finally{if(g===generation){busy=false;controls();}}
}
function updateCount() {
  const connected=[...clients.values()].filter(x=>x.call?.peerConnection?.connectionState==='connected').length;
  if(mode==='host'&&peer){
    $('guestCount').textContent=connected+' / '+MAX_GUESTS;
    $('stageType').textContent=connected+' de '+MAX_GUESTS+' amigos conectados';status(connected?'Transmitindo · '+connected+(connected===1?' amigo':' amigos'):'Sala aberta · aguardando amigos',connected>0);
    for(const entry of clients.values())if(entry.data?.open)entry.data.send({type:'audience',count:connected});
  }
}
function removeGuest(id) {
  const entry=clients.get(id);if(!entry)return;clients.delete(id);samples.delete(id);cancelLater(entry.deadline);
  entry.call?.close();entry.data?.close();updateCount();if(!clients.size){resetMetrics();$('audioStat').textContent=stream?.getAudioTracks().length?'Sim':'Sem áudio';}
}
function acceptGuest(data,g) {
  if(data.metadata?.key!==roomKey||clients.has(data.peer)){data.close();return;}
  if(clients.size>=MAX_GUESTS){data.on('open',()=>{data.send({type:'full'});later(()=>data.close(),500);});later(()=>data.close(),15000);return;}
  const entry={data,call:null,deadline:null};clients.set(data.peer,entry);
  entry.deadline=later(()=>{if(g===generation&&entry.call?.peerConnection?.connectionState!=='connected')removeGuest(data.peer);},35000);
  data.on('open',()=>{
    if(g!==generation||!stream||!clients.has(data.peer))return;
    data.send({type:'welcome'});
    const wanted=$('codec').value;
    entry.call=peer.call(data.peer,stream,{metadata:{key:roomKey},sdpTransform:sdp=>FrameHelpers.preferCodec(sdp,wanted)});
    attachCall(entry,g);
  });
  data.on('close',()=>{if(g===generation)removeGuest(data.peer);});
  data.on('error',()=>{if(g===generation)removeGuest(data.peer);});
}
function attachCall(entry,g) {
  const call=entry.call, pc=call.peerConnection;
  call.on('error',()=>{if(g===generation){if(mode==='host')removeGuest(call.peer);else fail('O vídeo não conectou. Tente outra rede; este protótipo ainda não inclui TURN.');}});
  call.on('close',()=>{if(g===generation){if(mode==='host')removeGuest(call.peer);else finishViewer('A transmissão foi encerrada.');}});
  // Inbound PeerJS calls create their RTCPeerConnection only after answer().
  const setup=()=>{
    const rtc=call.peerConnection;if(!rtc)return;
    const changed=()=>{
      if(g!==generation)return;
      if(rtc.connectionState==='connected'){
        firstConnected=true;cancelLater(entry.deadline);updateCount();
        if(mode==='host')tuneConnection(rtc).catch(()=>message('Não foi possível aplicar todos os limites de qualidade. O Chrome usará a adaptação automática.','warn'));
      }else if(rtc.connectionState==='failed'){
        if(mode==='host'){removeGuest(call.peer);message('Um amigo não conseguiu conectar. Algumas redes precisam de TURN, ainda não incluído.','warn');}
        else fail('A conexão de vídeo falhou. Tente novamente ou use outra rede.');
      }else if(rtc.connectionState==='disconnected'){
        if(mode==='viewer'){status('Reconectando ao vídeo…');message('A conexão foi interrompida. Aguardando recuperação.','warn');}
        later(()=>{if(g===generation&&rtc.connectionState==='disconnected'){if(mode==='host')removeGuest(call.peer);else fail('A conexão foi interrompida. Tente entrar novamente.');}},12000);
      }
    };
    rtc.addEventListener('connectionstatechange',changed);changed();
    if(mode==='host')rtc.addEventListener('signalingstatechange',()=>{if(g===generation&&rtc.signalingState==='stable')tuneConnection(rtc).catch(()=>{});});
  };
  if(pc)setup();else queueMicrotask(setup);
}
function finishViewer(text) {cleanup(false);status('Sala encerrada');message(text,'warn');$('retry').hidden=false;}
async function joinRoom(target) {
  if(busy||peer)return;invite=target;setMode('viewer');busy=true;const g=generation;controls();$('retry').hidden=true;message('');status('Entrando na sala…');
  try {
    const p=await openPeer(g);assertCurrent(g);
    const data=p.connect(invite.host,{metadata:{key:invite.key},serialization:'json',reliable:true});
    const entry={data,call:null,deadline:null};clients.set(invite.host,entry);
    entry.deadline=later(()=>{if(g===generation&&!firstConnected)fail('A sala não respondeu. Ela pode ter sido encerrada ou a rede pode bloquear conexões diretas.');},35000);
    data.on('data',payload=>{
      if(g!==generation)return;
      if(payload?.type==='full')fail('A sala está cheia (4 amigos). Tente novamente quando houver uma vaga.');
      else if(payload?.type==='ended')finishViewer('Seu amigo encerrou a sala. Peça um novo link quando ele voltar.');
      else if(payload?.type==='welcome')status('Conectando o vídeo…');
      else if(payload?.type==='audience'&&Number.isInteger(payload.count)&&payload.count>=0&&payload.count<=MAX_GUESTS)$('guestCount').textContent=payload.count+' / '+MAX_GUESTS;
    });
    data.on('close',()=>{if(g===generation)finishViewer('A sala foi encerrada ou a conexão foi perdida.');});
    data.on('error',()=>{if(g===generation)fail('Não foi possível conectar à sala. Tente novamente.');});
    startStats();
  }catch(e){if(g===generation)fail(e.message);}
  finally{if(g===generation){busy=false;controls();}}
}
async function applyCapture() {
  if(!stream)return;const q=quality();
  if(!demoSource)await stream.getVideoTracks()[0].applyConstraints({width:{max:Math.round(q.height*16/9)},height:{max:q.height},frameRate:{ideal:q.fps,max:q.fps}});
}
async function tuneConnection(pc) {
  const q=quality();
  for(const sender of pc.getSenders()){
    if(sender.track?.kind!=='video')continue;const p=sender.getParameters();if(!p.encodings?.length)continue;
    p.encodings[0].maxBitrate=q.bitrate;p.encodings[0].maxFramerate=q.fps;
    p.encodings[0].scaleResolutionDownBy=demoSource?Math.max(1,1080/q.height):1;
    p.degradationPreference=$('priority').value;await sender.setParameters(p);
  }
}
function startStats() {if(statsTimer)clearInterval(statsTimer);statsTimer=setInterval(collectStats,1000);}
async function collectStats() {
  if(statsBusy||!peer)return;statsBusy=true;const g=generation;
  try{
    let totalRate=0,rateKnown=false,shown=false;
    for(const [id,entry] of clients){
      const pc=entry.call?.peerConnection;if(!pc||pc.connectionState!=='connected')continue;
      const reports=await pc.getStats();if(g!==generation)return;
      let video,pair;reports.forEach(r=>{if(r.kind==='video'&&r.type===(mode==='host'?'outbound-rtp':'inbound-rtp')&&!r.isRemote)video=r;if(r.type==='transport'&&r.selectedCandidatePairId)pair=reports.get(r.selectedCandidatePairId);});
      if(!video)continue;const prev=samples.get(id),bytes=video.bytesSent??video.bytesReceived,frames=video.framesEncoded??video.framesDecoded;
      const dt=prev&&prev.id===video.id?(video.timestamp-prev.time)/1000:0;
      if(dt>0){totalRate+=Math.max(0,bytes-prev.bytes)*8/dt/1e6;rateKnown=true;}
      if(!shown){
        shown=true;if(video.frameWidth&&video.frameHeight)$('resolution').textContent=video.frameWidth+' × '+video.frameHeight;
        if(dt>0)$('fps').textContent=Math.round(video.framesPerSecond??Math.max(0,(frames-prev.frames)/dt))+' FPS';
        $('rtt').textContent=pair?.currentRoundTripTime!=null?Math.round(pair.currentRoundTripTime*1000)+' ms':'—';
        $('codecStat').textContent=reports.get(video.codecId)?.mimeType?.replace('video/','')||'Não informado';
        $('encoderStat').textContent=video.encoderImplementation||'Não exposto pelo navegador';
        if(dt>0&&video.totalEncodeTime!=null&&frames>prev.frames)$('encodeStat').textContent=((video.totalEncodeTime-prev.encode)*1000/(frames-prev.frames)).toFixed(1)+' ms / quadro';
        $('limitStat').textContent=({none:'Nenhuma informada',cpu:'Processamento',bandwidth:'Conexão',other:'Outra'}[video.qualityLimitationReason])||(mode==='viewer'?'Disponível no transmissor':'Não informado');
        let loss=video.packetsLost;if(mode==='host')reports.forEach(r=>{if(r.type==='remote-inbound-rtp'&&r.kind==='video')loss=r.packetsLost;});
        $('lossStat').textContent=loss!=null?String(Math.max(0,loss)):'Aguardando relatório';
      }
      samples.set(id,{id:video.id,time:video.timestamp,bytes,frames,encode:video.totalEncodeTime||0});
    }
    if(rateKnown)$('bitrate').textContent=totalRate.toFixed(1)+' Mbps';
  }catch{}finally{statsBusy=false;}
}
$('start').onclick=()=>createRoom();$('demo').onclick=()=>createRoom(true);
$('stop').onclick=()=>{cleanup();message(mode==='host'?'Sala encerrada. A captura foi liberada e o convite expirou.':'Você saiu da sala.');if(mode==='viewer')$('retry').hidden=false;};
$('hostTab').onclick=()=>{invite=null;history.replaceState(null,'',location.pathname+location.search);setMode('host');message('');};
$('viewerTab').onclick=()=>{setMode('viewer');message('');};
$('join').onclick=()=>{try{joinRoom(FrameHelpers.invitation($('roomInput').value,location.href));}catch(e){message(e.message,'error');}};
$('retry').onclick=()=>{if(invite)joinRoom(invite);};
$('copyLink').onclick=async()=>{try{await navigator.clipboard.writeText($('inviteLink').value);message('Link copiado. Envie aos seus amigos; eles entram automaticamente.');}catch{$('inviteLink').focus();$('inviteLink').select();message('Pressione Ctrl+C para copiar o link selecionado.','warn');}};
$('apply').onclick=async()=>{try{await applyCapture();await Promise.all([...clients.values()].map(e=>e.call?.peerConnection?tuneConnection(e.call.peerConnection):Promise.resolve()));message('Qualidade aplicada às conexões atuais.');}catch{message('Não foi possível aplicar todos os ajustes. Tente novamente ou reinicie a captura.','warn');}};
$('previewToggle').onclick=()=>{previewHidden=!previewHidden;$('video').hidden=previewHidden;$('placeholder').hidden=!previewHidden;$('previewToggle').textContent=previewHidden?'Mostrar prévia':'Ocultar prévia';$('placeholderTitle').textContent='Sua transmissão continua.';$('placeholderText').textContent='Prévia oculta neste PC. Seus amigos continuam recebendo o vídeo.';if(previewHidden)$('video').pause();else $('video').play().catch(()=>{});controls();};
$('sound').onclick=async()=>{const v=$('video');v.muted=!v.muted;try{await v.play();$('sound').textContent=v.muted?'Ativar áudio':'Silenciar';}catch{message('Clique no vídeo para iniciar a reprodução.','warn');}};
$('video').onclick=()=>{$('video').play().catch(()=>{});};
$('fullscreen').onclick=()=>{$('screen').requestFullscreen().catch(()=>message('Tela cheia indisponível neste contexto.','warn'));};
window.addEventListener('beforeunload',()=>cleanup());
document.querySelector('a[href="#help"]').onclick=event=>{event.preventDefault();$('help').scrollIntoView({behavior:'smooth'});};
if(location.hash){try{joinRoom(FrameHelpers.invitation(location.href,location.href));}catch(e){setMode('viewer');message(e.message,'error');}}
function sampleScene(){const canvas=document.createElement('canvas');canvas.width=1920;canvas.height=1080;const c=canvas.getContext('2d',{alpha:false});function draw(t){c.fillStyle='#101a23';c.fillRect(0,0,1920,1080);const offset=t*.15%80;c.strokeStyle='#263b4b';c.lineWidth=1;for(let x=-80;x<1920;x+=80){c.beginPath();c.moveTo(x+offset,0);c.lineTo(x+offset,1080);c.stroke();}for(let y=0;y<1080;y+=80){c.beginPath();c.moveTo(0,y);c.lineTo(1920,y);c.stroke();}c.fillStyle='#b5f5c5';c.font='600 80px system-ui';c.fillText('FRAME / TESTE LOCAL',100,175);c.fillStyle='#b1c2ca';c.font='32px system-ui';c.fillText('Movimento, linhas e texto passam pelo WebRTC.',100,245);c.font='24px monospace';c.fillText('1920 × 1080 · Fonte sintética · '+(t/1000).toFixed(2)+' s',100,310);for(let i=0;i<12;i++){c.fillStyle=['#b5f5c5','#b4bcff','#f3ba87'][i%3];const x=120+(Math.sin(t/700+i*.42)+1)*730;const y=420+i*40;c.fillRect(x,y,180,24);}c.fillStyle='#e8f0f4';c.font='22px monospace';c.fillText('Texto pequeno para comparar nitidez: ABCDEFGH 0123456789',100,1010);animation=requestAnimationFrame(draw);}draw(performance.now());return canvas.captureStream(60);}

