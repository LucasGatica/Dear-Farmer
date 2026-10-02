// Teste de fumaça: roda contra `npm run dev` (http://127.0.0.1:8787).
// Uso: BASE=http://127.0.0.1:8787 node test/smoke.mjs
const BASE = process.env.BASE || 'http://127.0.0.1:8787';
const ADMIN = process.env.ADMIN_KEY || 'dev-admin';
let failures = 0;

function tok(n) { return 'test-token-' + String(n).padStart(8, '0') + '-' + 'x'.repeat(12); }
async function call(method, path, { token, body, headers = {} } = {}) {
  const res = await fetch(BASE + path, {
    method,
    headers: { 'content-type': 'application/json', 'X-DearFarmer-Version': '1.0.0', ...(token ? { Authorization: 'Bearer ' + token } : {}), ...headers },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  let data = null; try { data = await res.json(); } catch {}
  return { status: res.status, data };
}
function check(name, cond, extra) { if (cond) console.log('  ok   ' + name); else { failures++; console.log('  FAIL ' + name, extra ?? ''); } }

const letter = (i) => `Olá, fazendeiro distante número ${i}! Espero que sua colheita esteja indo bem e que o Junimo te traga sorte.`;

console.log('health');
{ const r = await call('GET', '/v1/health'); check('200', r.status === 200 && r.data.ok); }

console.log('auth');
{ const r = await call('POST', '/v1/letters', { body: { body: letter(0) } }); check('sem token -> 401', r.status === 401); }

console.log('validação');
{
  let r = await call('POST', '/v1/letters', { token: tok(1), body: { body: 'oi' } }); check('curta -> 400 too_short', r.status === 400 && r.data.error === 'too_short');
  r = await call('POST', '/v1/letters', { token: tok(1), body: { body: 'x'.repeat(700) } }); check('longa -> 400 too_long', r.status === 400 && r.data.error === 'too_long');
  r = await call('POST', '/v1/letters', { token: tok(1), body: { body: 'entra no meu site www.exemplo.com agora mesmo por favor' } }); check('link -> 400 no_links', r.status === 400 && r.data.error === 'no_links');
  r = await call('POST', '/v1/letters', { token: tok(1), body: { body: 'aaaaaaaaaaaaaaaaaaaaaaaa' } }); check('spam -> 400 low_effort', r.status === 400 && r.data.error === 'low_effort');
  r = await call('POST', '/v1/letters', { token: tok(1), body: { body: letter(1) }, headers: { 'X-DearFarmer-Version': '0.9.0' } }); check('cliente velho -> 426', r.status === 426);
}

console.log('enviar cartas (autor A = tok(1))');
let ids = [];
for (let i = 1; i <= 3; i++) {
  const r = await call('POST', '/v1/letters', { token: tok(1), body: { body: letter(i), name: 'Fazendeiro A <script>', lang: 'pt-BR' } });
  check(`carta ${i} -> 201`, r.status === 201, r.data); if (r.data?.id) ids.push(r.data.id);
}
{ const r = await call('POST', '/v1/letters', { token: tok(1), body: { body: letter(1), lang: 'pt-BR' } }); check('duplicada -> 409', r.status === 409); }
{ const r = await call('GET', '/v1/mine', { token: tok(1) }); check('mine lista 3', r.status === 200 && r.data.letters.length === 3, r.data); check('nome sanitizado', r.data.letters[0].name === 'Fazendeiro A script'); }

console.log('feed (leitor B = tok(2))');
let fed;
{
  const r = await call('POST', '/v1/feed', { token: tok(2), body: { count: 2, lang: 'pt-BR', exclude: [] } });
  check('feed 200 com 2 cartas', r.status === 200 && r.data.letters.length === 2, r.data); fed = r.data.letters;
  check('vote_token presente', fed.every((l) => typeof l.vote_token === 'string' && l.vote_token.length === 32));
  const r2 = await call('POST', '/v1/feed', { token: tok(2), body: { count: 5, lang: 'pt-BR', exclude: fed.map((l) => l.id) } });
  check('exclude respeitado', r2.status === 200 && r2.data.letters.every((l) => !fed.some((f) => f.id === l.id)) && r2.data.letters.length === 1, r2.data);
  const r3 = await call('POST', '/v1/feed', { token: tok(1), body: { count: 5, lang: 'pt-BR' } });
  check('autor não recebe as próprias cartas', r3.status === 200 && r3.data.letters.length === 0, r3.data);
  const r4 = await call('POST', '/v1/feed', { token: tok(3), body: { count: 5, lang: 'ja' } });
  check('idioma raro cai no fallback', r4.status === 200 && r4.data.letters.length === 3, r4.data);
}

console.log('votos');
{
  const L = fed[0];
  let r = await call('POST', `/v1/letters/${L.id}/vote`, { token: tok(2), body: { value: 1, vote_token: 'errado' } }); check('vote_token errado -> 403', r.status === 403);
  r = await call('POST', `/v1/letters/${L.id}/vote`, { token: tok(2), body: { value: 1, vote_token: L.vote_token } }); check('upvote -> score 1', r.status === 200 && r.data.score === 1, r.data);
  r = await call('POST', `/v1/letters/${L.id}/vote`, { token: tok(2), body: { value: 1, vote_token: L.vote_token } }); check('upvote de novo não duplica', r.data.score === 1, r.data);
  r = await call('POST', `/v1/letters/${L.id}/vote`, { token: tok(2), body: { value: -1, vote_token: L.vote_token } }); check('trocar p/ downvote -> score -1', r.data.score === -1 && r.data.downvotes === 1 && r.data.upvotes === 0, r.data);
  r = await call('POST', `/v1/letters/${L.id}/vote`, { token: tok(2), body: { value: 0, vote_token: L.vote_token } }); check('remover voto -> score 0', r.data.score === 0, r.data);
  r = await call('POST', `/v1/letters/${L.id}/vote`, { token: tok(2), body: { value: 2, vote_token: L.vote_token } }); check('value inválido -> 400', r.status === 400);
  // autor tenta votar na própria (precisa de um vote_token válido, que ele não tem -> 403)
  r = await call('POST', `/v1/letters/${L.id}/vote`, { token: tok(1), body: { value: 1, vote_token: L.vote_token } }); check('autor sem token válido -> 403', r.status === 403);
}

console.log('remoção automática por downvotes (HIDE_SCORE=-5)');
{
  const target = ids[2];
  let removed = false;
  for (let v = 10; v < 16; v++) {
    const f = await call('POST', '/v1/feed', { token: tok(v), body: { count: 5, lang: 'pt-BR' } });
    const L = f.data.letters.find((l) => l.id === target);
    if (!L) { check(`leitor ${v} recebeu a carta alvo`, false, f.data); break; }
    const r = await call('POST', `/v1/letters/${target}/vote`, { token: tok(v), body: { value: -1, vote_token: L.vote_token } });
    if (r.status === 200 && r.data.removed) { removed = true; check(`removida após ${v - 9} downvotes`, v - 9 === 5); break; }
  }
  check('carta removida', removed);
  const f = await call('POST', '/v1/feed', { token: tok(20), body: { count: 10, lang: 'pt-BR' } });
  check('removida some do feed', !f.data.letters.some((l) => l.id === target));
  const m = await call('GET', '/v1/mine', { token: tok(1) });
  const mine = m.data.letters.find((l) => l.id === target);
  check('autor vê hidden + corpo apagado', mine && mine.hidden === 1 && mine.body === '' && mine.hidden_reason === 'downvotes', mine);
}

console.log('remoção por denúncias (HIDE_REPORTS=3)');
{
  const target = ids[1];
  let removed = false;
  for (let v = 30; v < 34; v++) {
    const f = await call('POST', '/v1/feed', { token: tok(v), body: { count: 5, lang: 'pt-BR' } });
    const L = f.data.letters.find((l) => l.id === target);
    if (!L) { check(`leitor ${v} recebeu a carta alvo`, false, f.data); break; }
    const r = await call('POST', `/v1/letters/${target}/report`, { token: tok(v), body: { vote_token: L.vote_token, reason: 'spam' } });
    if (r.data.removed) { removed = true; check(`removida após ${v - 29} denúncias`, v - 29 === 3); break; }
  }
  check('carta removida por denúncia', removed);
}

console.log('autor apaga a própria carta');
{
  const r = await call('DELETE', `/v1/letters/${ids[0]}`, { token: tok(1) }); check('delete -> 200', r.status === 200);
  const r2 = await call('DELETE', `/v1/letters/${ids[0]}`, { token: tok(2) }); check('outro autor -> 404', r2.status === 404);
  const r3 = await call('POST', `/v1/letters/${ids[0]}/vote`, { token: tok(2), body: { value: 1, vote_token: fed.find((l) => l.id === ids[0])?.vote_token || 'x' } });
  check('votar em carta apagada -> 410/403', r3.status === 410 || r3.status === 403, r3);
}

console.log('rate limit por hora (LETTERS_PER_HOUR=12)');
{
  let last;
  for (let i = 0; i < 13; i++) last = await call('POST', '/v1/letters', { token: tok(50), body: { body: letter(100 + i) + ' ' + i } });
  check('13ª carta -> 429 letters_per_hour', last.status === 429 && last.data.error === 'letters_per_hour' && last.data.retry_after > 0, last.data);
}

console.log('admin');
{
  let r = await call('GET', '/admin/stats'); check('sem chave -> 403', r.status === 403);
  r = await call('GET', '/admin/stats', { headers: { 'X-Admin-Key': ADMIN } }); check('stats', r.status === 200 && r.data.visible >= 1, r.data);
  const m = await call('GET', '/v1/mine', { token: tok(50) });
  const authorId = (await call('GET', '/admin/letters?limit=1', { headers: { 'X-Admin-Key': ADMIN } })).data.letters[0].author_id;
  r = await call('POST', '/admin/bans', { headers: { 'X-Admin-Key': ADMIN }, body: { author_id: authorId, reason: 'teste' } }); check('ban', r.status === 200);
  r = await call('GET', '/v1/mine', { token: tok(50) }); check('banido -> 403', r.status === 403, r.data);
  r = await call('DELETE', '/admin/bans/' + authorId, { headers: { 'X-Admin-Key': ADMIN } }); check('unban', r.status === 200);
  r = await call('GET', '/v1/mine', { token: tok(50) }); check('desbanido -> 200', r.status === 200);
}

console.log(failures === 0 ? '\nTUDO OK' : `\n${failures} FALHA(S)`);
process.exit(failures === 0 ? 0 : 1);
