// Gera DearFarmer/assets/ui.png (sprite sheet dos ícones do mod) sem dependências.
// Uso: node tools/make-ui-png.mjs
import { writeFileSync } from 'node:fs';
import { deflateSync, crc32 } from 'node:zlib';

const W = 104, H = 28;
const px = new Uint8Array(W * H * 4); // RGBA, transparente

const DARK = [62, 40, 30, 255];
const WHITE = [255, 255, 255, 255];
const PAL = { '#': DARK, 'o': WHITE };

function put(x, y, c) { if (x < 0 || y < 0 || x >= W || y >= H) return; const i = (y * W + x) * 4; px[i] = c[0]; px[i + 1] = c[1]; px[i + 2] = c[2]; px[i + 3] = c[3]; }
function blit(rows, ox, oy, pal = PAL) { rows.forEach((row, y) => [...row].forEach((ch, x) => { if (pal[ch]) put(ox + x, oy + y, pal[ch]); })); }

const thumbsUp = [
  '................',
  '......##........',
  '.....#oo#.......',
  '.....#oo#.......',
  '.....#oo#.......',
  '.....#oo#.......',
  '.##.#oooo###....',
  '#oo##ooooooo#...',
  '#ooo#ooooooo#...',
  '#ooo#oooooo#....',
  '#ooo#ooooooo#...',
  '#ooo#oooooo#....',
  '#ooo#ooooooo#...',
  '#ooo#oooooo#....',
  '.####oooooo#....',
  '.....######.....',
];
const thumbsDown = [...thumbsUp].reverse();
const flag = [
  '................',
  '...##...........',
  '...#o#..........',
  '...#ooo#........',
  '...#ooooo#......',
  '...#ooooooo#....',
  '...#ooooooo#....',
  '...#ooooo#......',
  '...#ooo#........',
  '...#o#..........',
  '...##...........',
  '...##...........',
  '...##...........',
  '...##...........',
  '...##...........',
  '................',
];
const pen = [
  '................',
  '...........##...',
  '..........#oo#..',
  '.........#oooo#.',
  '........#oooo#..',
  '.......#oooo#...',
  '......#oooo#....',
  '.....#oooo#.....',
  '....#oooo#......',
  '...#oooo#.......',
  '..#oooo#........',
  '.#oo##..........',
  '.#o#............',
  '.##.............',
  '................',
  '................',
];
const envelope = [
  '................',
  '................',
  '.##############.',
  '.#oooooooooooo#.',
  '.#o#oooooooo#o#.',
  '.#oo#oooooo#oo#.',
  '.#ooo#oooo#ooo#.',
  '.#oooo#oo#oooo#.',
  '.#ooooo##ooooo#.',
  '.#oooooooooooo#.',
  '.#oooooooooooo#.',
  '.#oooooooooooo#.',
  '.##############.',
  '................',
  '................',
  '................',
];

// Carimbo postal 24x28: borda serrilhada, papel creme, coração e "DF".
const STAMP_W = 24, STAMP_H = 28;
const CREAM = [246, 232, 196, 255], RED = [204, 72, 72, 255], TEAL = [86, 150, 150, 255];
for (let y = 0; y < STAMP_H; y++) for (let x = 0; x < STAMP_W; x++) {
  const edge = x === 0 || y === 0 || x === STAMP_W - 1 || y === STAMP_H - 1;
  if (edge) { if ((x + y) % 2 === 0) put(x, y, DARK); continue; }
  const inner = x >= 2 && y >= 2 && x <= STAMP_W - 3 && y <= STAMP_H - 3;
  put(x, y, inner ? CREAM : DARK);
}
blit([
  '.##..##.',
  '#oo##oo#',
  '#oooooo#',
  '#oooooo#',
  '.#oooo#.',
  '..#oo#..',
  '...##...',
], 8, 6, { '#': DARK, 'o': RED });
blit([
  '###..###',
  '#..#.#..',
  '#..#.##.',
  '#..#.#..',
  '###..#..',
], 8, 17, { '#': TEAL });

blit(thumbsUp, 24, 0);
blit(thumbsDown, 40, 0);
blit(flag, 56, 0);
blit(pen, 72, 0);
blit(envelope, 88, 0);

// --- PNG ---
function chunk(type, data) {
  const t = Buffer.from(type, 'ascii');
  const len = Buffer.alloc(4); len.writeUInt32BE(data.length);
  const crc = Buffer.alloc(4); crc.writeUInt32BE(crc32(Buffer.concat([t, data])) >>> 0);
  return Buffer.concat([len, t, data, crc]);
}
const ihdr = Buffer.alloc(13); ihdr.writeUInt32BE(W, 0); ihdr.writeUInt32BE(H, 4); ihdr[8] = 8; ihdr[9] = 6; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
const raw = Buffer.alloc((W * 4 + 1) * H);
for (let y = 0; y < H; y++) { raw[y * (W * 4 + 1)] = 0; Buffer.from(px.buffer, y * W * 4, W * 4).copy(raw, y * (W * 4 + 1) + 1); }
const png = Buffer.concat([Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]), chunk('IHDR', ihdr), chunk('IDAT', deflateSync(raw)), chunk('IEND', Buffer.alloc(0))]);
writeFileSync(new URL('../DearFarmer/assets/ui.png', import.meta.url), png);
console.log('wrote DearFarmer/assets/ui.png', W + 'x' + H, png.length, 'bytes');
