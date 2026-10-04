// Gera DearFarmer/assets/ui.png (sprite sheet dos ícones do mod) sem dependências.
// Uso: node tools/make-ui-png.mjs
//
// Layout da sheet (ver LetterArt.cs):
//   y 0..27   : carimbos 24x28  (heart, star, sun, leaf, moon, flower)        x = i*24
//   y 28..43  : ícones de UI 16x16 (thumbUp, thumbDown, flag, pen, envelope, palette, dice, reset)  x = i*16
//   y 44..59  : adesivos 16x16 (flower, star, heart, cloud, strawberry, junimo, chicken, mushroom)  x = i*16
//   y 60..67  : tiles de borda 8x8 (dots, dashes, hearts, stars, waves, leaves)  x = i*8
import { writeFileSync } from 'node:fs';
import { deflateSync, crc32 } from 'node:zlib';

const W = 160, H = 68;
const px = new Uint8Array(W * H * 4);

const DARK = [62, 40, 30, 255];
const WHITE = [255, 255, 255, 255];
const PAL = { '#': DARK, 'o': WHITE };

function put(x, y, c) { if (x < 0 || y < 0 || x >= W || y >= H) return; const i = (y * W + x) * 4; px[i] = c[0]; px[i + 1] = c[1]; px[i + 2] = c[2]; px[i + 3] = c[3]; }
function blit(rows, ox, oy, pal = PAL) { rows.forEach((row, y) => [...row].forEach((ch, x) => { if (pal[ch]) put(ox + x, oy + y, pal[ch]); })); }

// ---------------------------------------------------------------------------
// Carimbos 24x28: borda serrilhada, papel creme, ícone central e "DF"
// ---------------------------------------------------------------------------
const CREAM = [246, 232, 196, 255], RED = [204, 72, 72, 255], TEAL = [86, 150, 150, 255];
const GOLD = [230, 180, 60, 255], GREEN = [96, 160, 80, 255], NAVY = [70, 80, 150, 255], PINK = [226, 120, 160, 255], ORANGE = [235, 150, 60, 255];

const stampIcons = {
  heart: { c: RED, rows: ['.##..##.', '#oo##oo#', '#oooooo#', '#oooooo#', '.#oooo#.', '..#oo#..', '...##...'] },
  star: { c: GOLD, rows: ['...##...', '...oo...', '.##oo##.', '#oooooo#', '.#oooo#.', '.#o##o#.', '##....##'] },
  sun: { c: ORANGE, rows: ['#..##..#', '.#oooo#.', '#oooooo#', '#oooooo#', '#oooooo#', '.#oooo#.', '#..##..#'] },
  leaf: { c: GREEN, rows: ['.....###', '...##oo#', '..#oooo#', '.#ooooo#', '#oooo##.', '#ooo#...', '.##o#...'] },
  moon: { c: NAVY, rows: ['..####..', '.#oo##..', '#ooo#...', '#ooo#...', '#ooo#...', '.#oo##..', '..####..'] },
  flower: { c: PINK, rows: ['..#..#..', '.#o##o#.', '#oo#ooo#', '.#oooo#.', '#oo#ooo#', '.#o##o#.', '..#..#..'] },
};
Object.values(stampIcons).forEach((icon, i) => {
  const ox = i * 24, oy = 0;
  for (let y = 0; y < 28; y++) for (let x = 0; x < 24; x++) {
    const edge = x === 0 || y === 0 || x === 23 || y === 27;
    if (edge) { if ((x + y) % 2 === 0) put(ox + x, oy + y, DARK); continue; }
    const inner = x >= 2 && y >= 2 && x <= 21 && y <= 25;
    put(ox + x, oy + y, inner ? CREAM : DARK);
  }
  blit(icon.rows, ox + 8, oy + 6, { '#': DARK, 'o': icon.c });
  blit(['###..###', '#..#.#..', '#..#.##.', '#..#.#..', '###..#..'], ox + 8, oy + 17, { '#': TEAL });
});

// ---------------------------------------------------------------------------
// Ícones de UI 16x16 (linha y=28)
// ---------------------------------------------------------------------------
const thumbsUp = [
  '................', '......##........', '.....#oo#.......', '.....#oo#.......', '.....#oo#.......', '.....#oo#.......',
  '.##.#oooo###....', '#oo##ooooooo#...', '#ooo#ooooooo#...', '#ooo#oooooo#....', '#ooo#ooooooo#...', '#ooo#oooooo#....',
  '#ooo#ooooooo#...', '#ooo#oooooo#....', '.####oooooo#....', '.....######.....',
];
const thumbsDown = [...thumbsUp].reverse();
const flag = [
  '................', '...##...........', '...#o#..........', '...#ooo#........', '...#ooooo#......', '...#ooooooo#....',
  '...#ooooooo#....', '...#ooooo#......', '...#ooo#........', '...#o#..........', '...##...........', '...##...........',
  '...##...........', '...##...........', '...##...........', '................',
];
const pen = [
  '................', '...........##...', '..........#oo#..', '.........#oooo#.', '........#oooo#..', '.......#oooo#...',
  '......#oooo#....', '.....#oooo#.....', '....#oooo#......', '...#oooo#.......', '..#oooo#........', '.#oo##..........',
  '.#o#............', '.##.............', '................', '................',
];
const envelope = [
  '................', '................', '.##############.', '.#oooooooooooo#.', '.#o#oooooooo#o#.', '.#oo#oooooo#oo#.',
  '.#ooo#oooo#ooo#.', '.#oooo#oo#oooo#.', '.#ooooo##ooooo#.', '.#oooooooooooo#.', '.#oooooooooooo#.', '.#oooooooooooo#.',
  '.##############.', '................', '................', '................',
];
const palette = [
  '................', '.....######.....', '...##oooooo##...', '..#oo#oooo#oo#..', '.#ooo#oooo#ooo#.', '.#oooooooooooo#.',
  '#oo#oooooooo#oo#', '#oo#ooooooo#oo#.', '#oooooooooo###..', '#oooooooooo#o#..', '.#ooo#ooooo###..', '.#ooo#oooooo#...',
  '..#oooooooo#....', '...##oooo##.....', '.....####.......', '................',
];
const dice = [
  '................', '.############...', '#oooooooooooo#..', '#o##ooooooooo#..', '#o##oooooo##o#..', '#ooooooooo##o#..',
  '#oooooooooooo#..', '#ooooo##ooooo#..', '#ooooo##ooooo#..', '#oooooooooooo#..', '#o##oooooo##o#..', '#o##oooooo##o#..',
  '#oooooooooooo#..', '.############...', '................', '................',
];
const reset = [
  '................', '.....######.....', '...##oooooo##...', '..#oo######oo#..', '.#oo#......#oo#.', '.#o#........#o#.',
  '.#o#...##...#o#.', '.#o#..#oo#..####', '.#o#..#oo#.#ooo#', '.#o#...##...#o#.', '.#o#.........##.', '.#oo#...........',
  '..#oo#..........', '...##...........', '................', '................',
];
[thumbsUp, thumbsDown, flag, pen, envelope, palette, dice, reset].forEach((icon, i) => blit(icon, i * 16, 28));

// ---------------------------------------------------------------------------
// Adesivos 16x16 (linha y=44), coloridos
// ---------------------------------------------------------------------------
const stickers = [
  { pal: { '#': DARK, 'o': PINK, 'y': GOLD, 'g': GREEN }, rows: [ // flower
    '................', '.....####.......', '....#oooo#......', '.##.#oooo#.##...', '#oo##oooo##oo#..', '#ooo##yy##ooo#..', '#oooo#yy#oooo#..', '.##o#oooo#o##...',
    '..#oooooooo#....', '.#oooo##oooo#...', '.#ooo#..#ooo#...', '..###.##.###....', '......#g#.......', '.....#gg#.......', '....#gg#........', '....##..........' ] },
  { pal: { '#': DARK, 'o': GOLD }, rows: [ // star
    '.......#........', '......#o#.......', '......#o#.......', '.....#ooo#......', '.....#ooo#......', '######ooo######.', '#ooooooooooooo#.', '.#ooooooooooo#..',
    '..#ooooooooo#...', '...#ooooooo#....', '...#ooooooo#....', '..#ooooo#ooo#...', '..#ooo##.##o#...', '.#oo#.......##..', '.##.............', '................' ] },
  { pal: { '#': DARK, 'o': RED, 'w': WHITE }, rows: [ // heart
    '................', '..####...####...', '.#oooo#.#oooo#..', '#owoooo#oooooo#.', '#woooooooooooo#.', '#oooooooooooooo#', '#oooooooooooooo#', '.#oooooooooooo#.',
    '.#oooooooooooo#.', '..#oooooooooo#..', '...#oooooooo#...', '....#oooooo#....', '.....#oooo#.....', '......#oo#......', '.......##.......', '................' ] },
  { pal: { '#': DARK, 'o': WHITE, 'b': [190, 220, 240, 255] }, rows: [ // cloud
    '................', '................', '......####......', '.....#oooo#.....', '....#oooooo#....', '..##ooooooo##...', '.#ooooooooooo#..', '#ooooooooooooo#.',
    '#ooooooooooooo#.', '#oooooooooooooo#', '.#oooooooooooo#.', '..############..', '................', '................', '................', '................' ] },
  { pal: { '#': DARK, 'o': RED, 'g': GREEN, 'y': GOLD }, rows: [ // strawberry
    '......#g#.......', '....#ggggg#.....', '...#g#ggg#g#....', '..###oooo###....', '..#oooooooo#....', '.#oyooooooyo#...', '.#oooooyooooo#..', '.#oyooooooyoo#..',
    '.#ooooyooooyo#..', '..#oyoooooyo#...', '..#ooooyoooo#...', '...#oyoooyo#....', '....#ooooo#.....', '.....#ooo#......', '......#o#.......', '.......#........' ] },
  { pal: { '#': DARK, 'o': GREEN, 'w': WHITE, 'l': [150, 210, 110, 255] }, rows: [ // junimo
    '................', '.......#........', '......#l#.......', '.....#lll#......', '....#ooooo#.....', '...#ooooooo#....', '..#ooooooooo#...', '..#o#oooo#oo#...',
    '..#o#oooo#oo#...', '..#ooooooooo#...', '..#ooo#oo#oo#...', '..#oooo##ooo#...', '...#ooooooo#....', '..#o#ooooo#o#...', '..##.#####.##...', '................' ] },
  { pal: { '#': DARK, 'o': WHITE, 'r': RED, 'y': GOLD }, rows: [ // chicken
    '................', '......#r#.......', '.....#rrr#......', '.....#ooo#......', '....#ooooo#yy...', '....#o#ooo#y....', '....#ooooo##....', '...##ooooo#.....',
    '..#oooooooo#....', '.#oooooooooo#...', '.#oooooooooo#...', '..#oooooooo#....', '...#oooooo#.....', '....######......', '.....y..y.......', '....yy..yy......' ] },
  { pal: { '#': DARK, 'o': RED, 'w': WHITE, 't': CREAM }, rows: [ // mushroom
    '................', '.....######.....', '...##oooooo##...', '..#oowooooooo#..', '.#ooooooowooo#..', '.#owooooooooo#..', '#ooooooooowooo#.', '#ooooooooooooo#.',
    '.#############..', '....#tttttt#....', '....#tttttt#....', '....#tttttt#....', '....#tttttt#....', '.....#tttt#.....', '......####......', '................' ] },
];
stickers.forEach((s, i) => blit(s.rows, i * 16, 44, s.pal));

// ---------------------------------------------------------------------------
// Tiles de borda 8x8 (linha y=60), brancos para tingir com a cor da tinta
// ---------------------------------------------------------------------------
const borders = [
  ['........', '........', '........', '...oo...', '...oo...', '........', '........', '........'], // dots
  ['........', '........', '........', '.oooooo.', '.oooooo.', '........', '........', '........'], // dashes
  ['........', '.oo..oo.', 'oooooooo', 'oooooooo', '.oooooo.', '..oooo..', '...oo...', '........'], // hearts
  ['...o....', '...o....', '.ooooo..', '..ooo...', '..ooo...', '.o...o..', '........', '........'], // stars
  ['........', '.oo.....', 'o..o..o.', '....oo..', '........', '.oo.....', 'o..o..o.', '....oo..'], // waves
  ['.....oo.', '....ooo.', '...oooo.', '..ooooo.', '.oooo...', 'ooo.....', 'o.......', '........'], // leaves
];
borders.forEach((rows, i) => blit(rows, i * 8, 60, { 'o': WHITE }));

// --- PNG ---
function chunk(type, data) {
  const t = Buffer.from(type, 'ascii');
  const len = Buffer.alloc(4); len.writeUInt32BE(data.length);
  const crc = Buffer.alloc(4); crc.writeUInt32BE(crc32(Buffer.concat([t, data])) >>> 0);
  return Buffer.concat([len, t, data, crc]);
}
const ihdr = Buffer.alloc(13); ihdr.writeUInt32BE(W, 0); ihdr.writeUInt32BE(H, 4); ihdr[8] = 8; ihdr[9] = 6;
const raw = Buffer.alloc((W * 4 + 1) * H);
for (let y = 0; y < H; y++) { raw[y * (W * 4 + 1)] = 0; Buffer.from(px.buffer, y * W * 4, W * 4).copy(raw, y * (W * 4 + 1) + 1); }
const png = Buffer.concat([Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]), chunk('IHDR', ihdr), chunk('IDAT', deflateSync(raw)), chunk('IEND', Buffer.alloc(0))]);
writeFileSync(new URL('../DearFarmer/assets/ui.png', import.meta.url), png);
console.log('wrote DearFarmer/assets/ui.png', W + 'x' + H, png.length, 'bytes');
