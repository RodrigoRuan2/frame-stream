'use strict';
const FrameHelpers = (() => {
  function invitation(value, base) {
    let params;
    try { params = new URLSearchParams(new URL(value, base).hash.slice(1)); }
    catch { throw new Error('Link de sala inválido. Peça um novo convite ao seu amigo.'); }
    const host = params.get('room'), key = params.get('key');
    if (!/^frame-[a-f0-9-]{36}$/.test(host || '') || !/^[a-f0-9]{32}$/.test(key || '')) {
      throw new Error('Link de sala incompleto. Copie o convite inteiro.');
    }
    return {host, key};
  }
  function preferCodec(sdp, codec) {
    if (codec === 'auto') return sdp;
    return sdp.split(/(?=m=)/).map(section => {
      if (!section.startsWith('m=video ')) return section;
      const rows = section.split('\r\n');
      const payloads = [];
      for (const row of rows) {
        const match = row.match(/^a=rtpmap:(\d+) ([^/]+)\//);
        if (match && match[2].toLowerCase() === codec.toLowerCase()) payloads.push(match[1]);
      }
      if (!payloads.length) return section;
      const preferred = new Set(payloads);
      for (const row of rows) {
        const match = row.match(/^a=fmtp:(\d+) .*\bapt=(\d+)(?:;|$)/);
        if (match && preferred.has(match[2])) preferred.add(match[1]);
      }
      const fields = rows[0].split(' '), types = fields.slice(3);
      rows[0] = [...fields.slice(0,3), ...types.filter(t => preferred.has(t)), ...types.filter(t => !preferred.has(t))].join(' ');
      return rows.join('\r\n');
    }).join('');
  }
  function encoderKind(name, efficient) {
    // Positive identification only. Missing stats must never imply GPU usage.
    if (/openh264|libvpx|libaom|svt[-_]?av1/i.test(name || '')) return 'software';
    if (efficient === true) return 'hardware';
    return 'unknown';
  }
  function performanceNotice({encoder,efficient,reason,sourceFps,fps,target}) {
    const software=encoderKind(encoder,efficient)==='software';
    if(reason==='cpu')return 'O Chrome está limitando o vídeo por processamento'+(software?' e usando codificação por software':'')+'. Verifique a aceleração gráfica em Configurações → Sistema do Chrome e reinicie o navegador para aplicar. Se persistir, teste 720p. Trocar a hospedagem não resolve este gargalo.';
    if(software)return 'Codificação por software detectada ('+encoder+'). A placa de vídeo não está fazendo esta codificação. Verifique a aceleração gráfica em Configurações → Sistema do Chrome; o formato selecionado não garante uso da GPU.';
    if(reason==='bandwidth')return 'O Chrome está adaptando o vídeo à conexão. Teste menos espectadores ou uma resolução menor. O limite de Mbps é um teto, não uma taxa garantida.';
    if(Number.isFinite(sourceFps)&&sourceFps<target*.65&&Number.isFinite(fps)&&fps<target*.65)return 'A origem está fornecendo poucos quadros. Se o jogo está em movimento, compare captura de janela com tela inteira. Conteúdo parado pode gerar poucos quadros normalmente.';
    return '';
  }
  return {invitation, preferCodec, encoderKind, performanceNotice};
})();
if (typeof module !== 'undefined') module.exports = FrameHelpers;
