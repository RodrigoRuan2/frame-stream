const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
const source=fs.readFileSync(require('node:path').join(__dirname,'../watch.js'),'utf8');
const start=source.indexOf('async function stats(g)'),end=source.indexOf("$('sound').onclick",start);
const elements=new Map();let reports;
const ctx={generation:1,prev:null,prevAudio:null,samples:[],lastFrameAt:Date.now(),Date,Math,pc:{getStats:async()=>reports},$:id=>{if(!elements.has(id))elements.set(id,{textContent:'',hidden:true});return elements.get(id);}};
vm.createContext(ctx);vm.runInContext(source.slice(start,end),ctx);
const report=(n,delay,loss)=>new Map([
 ['v',{type:'inbound-rtp',kind:'video',timestamp:n*1000,frameWidth:1280,frameHeight:720,framesPerSecond:60,framesReceived:n*60,framesDecoded:n*60,totalDecodeTime:n*.06,jitterBufferEmittedCount:n*60,jitterBufferDelay:delay,bytesReceived:n*1500000,packetsReceived:n*990,packetsLost:loss,framesDropped:0,codecId:'c',jitter:.01}],
 ['a',{type:'inbound-rtp',kind:'audio',jitterBufferEmittedCount:n*48000,jitterBufferDelay:n*4800,codecId:'ac'}],
 ['c',{mimeType:'video/H264'}],['ac',{mimeType:'audio/opus',clockRate:48000}],
 ['t',{type:'transport',selectedCandidatePairId:'p'}],['p',{currentRoundTripTime:.064}]
]);
(async()=>{
 reports=report(0,0,0);await ctx.stats(1);
 reports=report(1,21,10);await ctx.stats(1);
 assert.equal(elements.get('received').textContent,'60 FPS recebidos');
 assert.equal(elements.get('bitrate').textContent,'12.0 Mbps');
 assert.equal(elements.get('decode').textContent,'1.0 ms / quadro');
 assert.equal(elements.get('buffer').textContent,'350.0 ms');
 assert.equal(elements.get('audioBuffer').textContent,'100.0 ms');
 assert.equal(elements.get('lossrate').textContent,'1.00%');
 assert.equal(elements.get('resync').hidden,false);
 reports=report(2,24,9);await ctx.stats(1);
 assert.equal(elements.get('buffer').textContent,'50.0 ms');
 assert.equal(elements.get('lossrate').textContent,'0.00%');
 assert.equal(elements.get('resync').hidden,true);
 assert.equal(ctx.samples.length,2);
 reports=report(3,200,200);await ctx.stats(0);
 assert.equal(elements.get('buffer').textContent,'50.0 ms');
 console.log('PASS interval bitrate, decode, video/audio buffers, loss, reconnect control and stale-session isolation');
})().catch(e=>{console.error(e);process.exitCode=1;});
