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
  return {invitation, preferCodec};
})();
if (typeof module !== 'undefined') module.exports = FrameHelpers;
