/**
 * Formato do desenho da carta (espelhado em DearFarmer/Framework/DrawingCanvas.cs).
 *
 * Grade de 160x90 células, cada uma com um índice de paleta de 0 a 15 (0 = transparente).
 * Codificação: "d1:" + base64 de uma sequência de runs (RLE):
 *   byte b -> valor = b >> 4, c = b & 15
 *     c < 15  : run de (c + 1) células          (1..15)
 *     c == 15 : próximo byte n, run de (16 + n)  (16..271)
 * Um canvas vazio ocupa ~108 bytes; um desenho típico, 1 a 4 KB.
 */
export const DRAWING_W = 160;
export const DRAWING_H = 90;
export const DRAWING_CELLS = DRAWING_W * DRAWING_H;
export const DRAWING_PREFIX = 'd1:';

/** @param {Uint8Array} cells */
export function encodeDrawing(cells) {
  if (cells.length !== DRAWING_CELLS) throw new Error('bad cell count');
  const out = [];
  let i = 0;
  while (i < DRAWING_CELLS) {
    const v = cells[i] & 15;
    let run = 1;
    while (i + run < DRAWING_CELLS && (cells[i + run] & 15) === v && run < 271) run++;
    if (run <= 15) out.push((v << 4) | (run - 1));
    else { out.push((v << 4) | 15); out.push(run - 16); }
    i += run;
  }
  return DRAWING_PREFIX + bytesToBase64(Uint8Array.from(out));
}

/** @returns {Uint8Array | null} células, ou null se inválido */
export function decodeDrawing(s) {
  if (typeof s !== 'string' || !s.startsWith(DRAWING_PREFIX)) return null;
  const b64 = s.slice(DRAWING_PREFIX.length);
  if (!/^[A-Za-z0-9+/]*={0,2}$/.test(b64) || b64.length % 4 !== 0) return null;
  let bytes;
  try { bytes = base64ToBytes(b64); } catch { return null; }
  const cells = new Uint8Array(DRAWING_CELLS);
  let pos = 0;
  for (let i = 0; i < bytes.length; i++) {
    const b = bytes[i];
    const v = b >> 4;
    let run = b & 15;
    if (run === 15) { if (i + 1 >= bytes.length) return null; run = 16 + bytes[++i]; } else run += 1;
    if (pos + run > DRAWING_CELLS) return null;
    cells.fill(v, pos, pos + run);
    pos += run;
  }
  return pos === DRAWING_CELLS ? cells : null;
}

export function isDrawingEmpty(cells) {
  for (let i = 0; i < cells.length; i++) if (cells[i] !== 0) return false;
  return true;
}

function bytesToBase64(bytes) {
  let s = '';
  for (const b of bytes) s += String.fromCharCode(b);
  return btoa(s);
}

function base64ToBytes(b64) {
  const s = atob(b64);
  const out = new Uint8Array(s.length);
  for (let i = 0; i < s.length; i++) out[i] = s.charCodeAt(i);
  return out;
}
