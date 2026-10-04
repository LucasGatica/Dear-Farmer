/**
 * Dear Farmer — backend (Cloudflare Worker + D1).
 *
 * Roda inteiro no free tier da Cloudflare: Workers (100k req/dia) + D1 (5M leituras, 100k escritas/dia).
 * Sem servidor, sem cold start, sem cartão de crédito.
 *
 * Rotas (todas JSON, prefixo /v1):
 *   GET  /v1/health                      -> status + limites (para o mod mostrar na UI)
 *   POST /v1/letters                     -> envia uma carta           { name?, body, lang?, style? }
 *   POST /v1/feed                        -> pede cartas para entregar { count, exclude:[ids], lang }
 *   POST /v1/letters/:id/vote            -> vota                      { value: 1|-1|0, vote_token }
 *   POST /v1/letters/:id/report          -> denuncia                  { vote_token, reason? }
 *   GET  /v1/mine                        -> minhas cartas + scores
 *   DELETE /v1/letters/:id               -> autor apaga a própria carta
 *
 * Admin (header X-Admin-Key = secret ADMIN_KEY):
 *   GET    /admin/stats
 *   GET    /admin/letters?hidden=0|1&limit=50&before=<id>
 *   DELETE /admin/letters/:id
 *   POST   /admin/bans   { author_id, reason }      DELETE /admin/bans/:author_id
 *
 * Identidade: o mod gera um token aleatório (UUID) na primeira execução e manda em
 * `Authorization: Bearer <token>`. O servidor só guarda sha256(token) (author_id).
 * Não há cadastro, senha ou e-mail.
 */

const JSON_HEADERS = { 'content-type': 'application/json; charset=utf-8' };
const HOUR = 3600;
const DAY = 86400;

export default {
  async fetch(request, env, ctx) {
    try {
      return await route(request, env, ctx);
    } catch (err) {
      if (err instanceof HttpError) return json({ error: err.code, message: err.message, ...err.extra }, err.status);
      console.error('unhandled', err && err.stack ? err.stack : err);
      return json({ error: 'internal', message: 'Erro interno no servidor.' }, 500);
    }
  },

  // Cron diário: apaga de vez cartas removidas há muito tempo (e, por cascata, seus votos).
  async scheduled(event, env, ctx) {
    const purgeAfter = int(env.PURGE_HIDDEN_AFTER_DAYS, 30) * DAY;
    const cutoff = now() - purgeAfter;
    const res = await env.DB.prepare('DELETE FROM letters WHERE hidden = 1 AND hidden_at IS NOT NULL AND hidden_at < ?').bind(cutoff).run();
    console.log('purged hidden letters', res.meta && res.meta.changes);
  },
};

// ---------------------------------------------------------------------------
// Roteamento
// ---------------------------------------------------------------------------

async function route(request, env, ctx) {
  const url = new URL(request.url);
  const path = url.pathname.replace(/\/+$/, '') || '/';
  const method = request.method.toUpperCase();

  if (method === 'OPTIONS') return new Response(null, { status: 204 });

  if (path === '/' || path === '/v1/health') {
    return json({
      ok: true,
      service: 'dear-farmer',
      time: now(),
      limits: {
        letters_per_hour: int(env.LETTERS_PER_HOUR, 12),
        letters_per_day: int(env.LETTERS_PER_DAY, 40),
        min_body_length: int(env.MIN_BODY_LENGTH, 10),
        max_body_length: int(env.MAX_BODY_LENGTH, 600),
        max_name_length: int(env.MAX_NAME_LENGTH, 24),
      },
      min_client_version: env.MIN_CLIENT_VERSION || '1.0.0',
    });
  }

  if (path.startsWith('/admin/')) return adminRoute(request, env, path, method);
  if (!path.startsWith('/v1/')) throw new HttpError(404, 'not_found', 'Rota não encontrada.');

  // Rate limit na borda por IP (opcional, só se o binding existir).
  const ip = request.headers.get('CF-Connecting-IP') || '0.0.0.0';
  if (env.RL_IP && typeof env.RL_IP.limit === 'function') {
    const { success } = await env.RL_IP.limit({ key: ip });
    if (!success) throw new HttpError(429, 'rate_limited', 'Muitas requisições. Tente de novo em instantes.', { retry_after: 60 });
  }

  checkClientVersion(request, env);

  const auth = await authenticate(request, env, ip);

  let m;
  if (method === 'POST' && path === '/v1/letters') return createLetter(request, env, auth);
  if (method === 'POST' && path === '/v1/feed') return feed(request, env, auth, ctx);
  if (method === 'GET' && path === '/v1/mine') return mine(env, auth);
  if ((m = path.match(/^\/v1\/letters\/(\d+)\/vote$/)) && method === 'POST') return vote(request, env, auth, Number(m[1]), false);
  if ((m = path.match(/^\/v1\/letters\/(\d+)\/report$/)) && method === 'POST') return vote(request, env, auth, Number(m[1]), true);
  if ((m = path.match(/^\/v1\/letters\/(\d+)$/)) && method === 'DELETE') return deleteOwnLetter(env, auth, Number(m[1]));

  throw new HttpError(404, 'not_found', 'Rota não encontrada.');
}

// ---------------------------------------------------------------------------
// Autenticação / identidade
// ---------------------------------------------------------------------------

async function authenticate(request, env, ip) {
  const header = request.headers.get('Authorization') || '';
  const m = header.match(/^Bearer\s+([A-Za-z0-9\-_]{16,128})$/);
  if (!m) throw new HttpError(401, 'unauthorized', 'Token do jogador ausente ou inválido.');
  if (!env.SIGNING_SECRET) throw new HttpError(500, 'misconfigured', 'SIGNING_SECRET não configurado no servidor.');

  const token = m[1];
  const [authorId, ipHash] = await Promise.all([
    sha256Hex('author:' + token),
    sha256Hex('ip:' + env.SIGNING_SECRET + ':' + ip),
  ]);

  const banned = await env.DB.prepare('SELECT reason FROM bans WHERE author_id = ?').bind(authorId).first();
  if (banned) throw new HttpError(403, 'banned', 'Este jogador foi bloqueado: ' + (banned.reason || 'abuso'));

  return { authorId, ipHash };
}

function checkClientVersion(request, env) {
  const min = env.MIN_CLIENT_VERSION;
  if (!min) return;
  const v = request.headers.get('X-DearFarmer-Version');
  if (!v) return; // clientes antigos/sem header passam; endureça se precisar
  if (compareVersions(v, min) < 0) {
    throw new HttpError(426, 'client_outdated', `Atualize o mod Dear Farmer (mínimo ${min}).`, { min_client_version: min });
  }
}

// ---------------------------------------------------------------------------
// POST /v1/letters — enviar carta
// ---------------------------------------------------------------------------

async function createLetter(request, env, auth) {
  const body = await readJson(request);
  const t = now();

  const text = normalizeBody(String(body.body ?? ''));
  const minLen = int(env.MIN_BODY_LENGTH, 10);
  const maxLen = int(env.MAX_BODY_LENGTH, 600);
  if ([...text].length < minLen) throw new HttpError(400, 'too_short', `A carta precisa ter pelo menos ${minLen} caracteres.`);
  if ([...text].length > maxLen) throw new HttpError(400, 'too_long', `A carta pode ter no máximo ${maxLen} caracteres.`);
  if (looksLikeLink(text)) throw new HttpError(400, 'no_links', 'Links não são permitidos nas cartas.');
  if (isLowEffort(text)) throw new HttpError(400, 'low_effort', 'Escreva algo com mais conteúdo (letras repetidas ou só símbolos não valem).');
  const blocked = findBlockedWord(text, env.BLOCKED_WORDS);
  if (blocked) throw new HttpError(400, 'blocked_word', 'A carta contém uma palavra não permitida.');

  const name = normalizeName(body.name, int(env.MAX_NAME_LENGTH, 24));
  const lang = normalizeLang(body.lang);
  const style = normalizeStyle(body.style);
  const bodyHash = await sha256Hex('body:' + text.toLowerCase().replace(/\s+/g, ' '));

  // --- limites por jogador / IP / duplicatas (todas leituras baratas, via índice) ---
  const perHour = int(env.LETTERS_PER_HOUR, 12);
  const perDay = int(env.LETTERS_PER_DAY, 40);
  const perHourIp = int(env.LETTERS_PER_HOUR_IP, 30);

  const [hourRow, dayRow, ipRow, dupRow] = await env.DB.batch([
    env.DB.prepare('SELECT COUNT(*) AS n, MIN(created_at) AS oldest FROM letters WHERE author_id = ? AND created_at > ?').bind(auth.authorId, t - HOUR),
    env.DB.prepare('SELECT COUNT(*) AS n, MIN(created_at) AS oldest FROM letters WHERE author_id = ? AND created_at > ?').bind(auth.authorId, t - DAY),
    env.DB.prepare('SELECT COUNT(*) AS n, MIN(created_at) AS oldest FROM letters WHERE ip_hash = ? AND created_at > ?').bind(auth.ipHash, t - HOUR),
    env.DB.prepare('SELECT id, author_id FROM letters WHERE body_hash = ? AND created_at > ? LIMIT 1').bind(bodyHash, t - 7 * DAY),
  ]).then((rs) => rs.map((r) => r.results[0]));

  if (hourRow.n >= perHour) {
    throw new HttpError(429, 'letters_per_hour', `Você já enviou ${perHour} cartas na última hora. Respire um pouco e volte depois.`, { retry_after: Math.max(60, hourRow.oldest + HOUR - t) });
  }
  if (dayRow.n >= perDay) {
    throw new HttpError(429, 'letters_per_day', `Limite diário de ${perDay} cartas atingido.`, { retry_after: Math.max(60, dayRow.oldest + DAY - t) });
  }
  if (ipRow.n >= perHourIp) {
    throw new HttpError(429, 'letters_per_hour_ip', 'Muitas cartas vindas da sua conexão na última hora.', { retry_after: Math.max(60, ipRow.oldest + HOUR - t) });
  }
  if (dupRow) {
    throw new HttpError(409, 'duplicate', dupRow.author_id === auth.authorId ? 'Você já enviou uma carta igual a essa.' : 'Uma carta idêntica já foi enviada recentemente.');
  }

  const res = await env.DB.prepare(
    `INSERT INTO letters (author_id, ip_hash, name, body, body_hash, lang, rand, created_at, style)
     VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)`
  ).bind(auth.authorId, auth.ipHash, name, text, bodyHash, lang, Math.random(), t, style).run();

  const id = res.meta.last_row_id;
  return json({
    id,
    created_at: t,
    remaining_hour: Math.max(0, perHour - hourRow.n - 1),
    remaining_day: Math.max(0, perDay - dayRow.n - 1),
  }, 201);
}

// ---------------------------------------------------------------------------
// POST /v1/feed — pedir cartas para entregar na caixa de correio
// ---------------------------------------------------------------------------

async function feed(request, env, auth, ctx) {
  const body = await readJson(request);
  const maxCount = int(env.MAX_FEED_COUNT, 20);
  const count = clamp(int(body.count, 5), 1, maxCount);
  const lang = normalizeLang(body.lang);
  const exclude = new Set((Array.isArray(body.exclude) ? body.exclude : []).slice(0, 500).map(Number).filter(Number.isFinite));
  const t = now();
  const minScore = int(env.MIN_SERVE_SCORE, -5);
  const minCreated = t - int(env.FEED_MAX_AGE_DAYS, 180) * DAY;

  // Amostragem barata: pivô aleatório no índice (lang, rand). Lê ~poucas dezenas de linhas
  // por chamada, independente do tamanho da tabela (ORDER BY RANDOM() leria a tabela toda).
  const sampleSize = Math.max(40, count * 8);
  const pivot = Math.random();
  const sql = (op, order) =>
    `SELECT id, name, body, score, delivered, created_at, lang, style FROM letters
     WHERE hidden = 0 AND lang = ? AND rand ${op} ? AND author_id != ? AND score >= ? AND created_at > ?
     ORDER BY rand ${order} LIMIT ?`;

  let rows = [];
  const seen = new Set();
  const take = (rs) => { for (const r of rs) if (!seen.has(r.id) && !exclude.has(r.id)) { seen.add(r.id); rows.push(r); } };

  const langsToTry = lang === 'en' ? ['en'] : [lang, 'en'];
  for (const l of langsToTry) {
    if (rows.length >= sampleSize) break;
    const a = await env.DB.prepare(sql('>=', 'ASC')).bind(l, pivot, auth.authorId, minScore, minCreated, sampleSize).all();
    take(a.results);
    if (rows.length < sampleSize) { // deu a volta: pega o que ficou antes do pivô
      const b = await env.DB.prepare(sql('<', 'ASC')).bind(l, pivot, auth.authorId, minScore, minCreated, sampleSize - rows.length).all();
      take(b.results);
    }
  }
  // Último recurso: qualquer idioma (jogador de idioma raro não fica sem cartas).
  if (rows.length < count) {
    const c = await env.DB.prepare(
      `SELECT id, name, body, score, delivered, created_at, lang, style FROM letters
       WHERE hidden = 0 AND author_id != ? AND score >= ? AND created_at > ? ORDER BY rand LIMIT ?`
    ).bind(auth.authorId, minScore, minCreated, sampleSize).all();
    take(c.results);
  }

  const chosen = weightedSample(rows, count, t);
  const letters = [];
  for (const r of chosen) {
    letters.push({
      id: r.id,
      name: r.name,
      body: r.body,
      lang: r.lang,
      created_at: r.created_at,
      style: parseStyle(r.style),
      vote_token: await voteToken(env, r.id, auth.authorId),
    });
  }

  // Contabiliza entregas depois de responder (não atrasa o jogador). Uma escrita por carta entregue.
  if (letters.length > 0) {
    const stmt = env.DB.prepare('UPDATE letters SET delivered = delivered + 1 WHERE id = ?');
    ctx.waitUntil(env.DB.batch(letters.map((l) => stmt.bind(l.id))).catch((e) => console.error('delivered++ failed', e)));
  }

  return json({ letters, server_time: t });
}

/**
 * Sorteio ponderado: cartas bem votadas aparecem mais, mal votadas menos; cartas novas e
 * pouco entregues ganham um empurrão para que toda carta seja lida por alguém.
 */
function weightedSample(rows, count, t) {
  const pool = rows.map((r) => {
    const ageDays = Math.max(0, (t - r.created_at) / DAY);
    const freshness = ageDays < 7 ? 1.0 : Math.max(0.3, 1 - (ageDays - 7) / 120);
    const scoreFactor = r.score >= 0 ? 1 + Math.min(r.score, 20) * 0.15 : Math.max(0.15, 1 + r.score * 0.3);
    const exposure = 1 / (1 + Math.min(r.delivered, 50) / 10);
    return { row: r, w: freshness * scoreFactor * (0.5 + exposure) };
  });
  const out = [];
  while (out.length < count && pool.length > 0) {
    let total = 0;
    for (const p of pool) total += p.w;
    let x = Math.random() * total;
    let idx = pool.length - 1;
    for (let i = 0; i < pool.length; i++) { x -= pool[i].w; if (x <= 0) { idx = i; break; } }
    out.push(pool[idx].row);
    pool.splice(idx, 1);
  }
  return out;
}

// ---------------------------------------------------------------------------
// POST /v1/letters/:id/vote  e  /report
// ---------------------------------------------------------------------------

async function vote(request, env, auth, letterId, isReport) {
  const body = await readJson(request);
  const t = now();

  // Só quem recebeu a carta (tem o vote_token assinado pelo servidor) pode votar nela.
  const expected = await voteToken(env, letterId, auth.authorId);
  if (!body.vote_token || !timingSafeEqual(String(body.vote_token), expected)) {
    throw new HttpError(403, 'bad_vote_token', 'Você só pode votar em cartas que recebeu.');
  }

  let value;
  if (isReport) value = -1;
  else {
    value = Number(body.value);
    if (![1, -1, 0].includes(value)) throw new HttpError(400, 'bad_value', 'value deve ser 1, -1 ou 0.');
  }

  const letter = await env.DB.prepare('SELECT id, author_id, hidden FROM letters WHERE id = ?').bind(letterId).first();
  if (!letter) throw new HttpError(404, 'not_found', 'Carta não existe mais.');
  if (letter.hidden) throw new HttpError(410, 'gone', 'Esta carta foi removida.');
  if (letter.author_id === auth.authorId) throw new HttpError(400, 'own_letter', 'Você não pode votar na própria carta.');

  const perHour = int(env.VOTES_PER_HOUR, 120);
  const n = await env.DB.prepare('SELECT COUNT(*) AS n FROM votes WHERE voter_id = ? AND updated_at > ?').bind(auth.authorId, t - HOUR).first('n');
  if (n >= perHour) throw new HttpError(429, 'votes_per_hour', 'Muitos votos na última hora.', { retry_after: 600 });

  const reason = isReport ? String(body.reason || '').slice(0, 100) : null;
  const hideScore = int(env.HIDE_SCORE, -5);
  const hideReports = int(env.HIDE_REPORTS, 3);

  // Tudo numa transação (D1 batch é atômico): upsert do voto, recálculo dos contadores,
  // e remoção automática se passou do limite. Uma pessoa = um voto por carta, sempre.
  const results = await env.DB.batch([
    env.DB.prepare(
      `INSERT INTO votes (letter_id, voter_id, value, report, reason, created_at, updated_at)
       VALUES (?, ?, ?, ?, ?, ?, ?)
       ON CONFLICT(letter_id, voter_id) DO UPDATE SET
         value = excluded.value,
         report = MAX(votes.report, excluded.report),
         reason = COALESCE(excluded.reason, votes.reason),
         updated_at = excluded.updated_at`
    ).bind(letterId, auth.authorId, value, isReport ? 1 : 0, reason, t, t),
    env.DB.prepare(
      `UPDATE letters SET
         score     = (SELECT COALESCE(SUM(value), 0) FROM votes WHERE letter_id = ?),
         upvotes   = (SELECT COUNT(*) FROM votes WHERE letter_id = ? AND value = 1),
         downvotes = (SELECT COUNT(*) FROM votes WHERE letter_id = ? AND value = -1),
         reports   = (SELECT COUNT(*) FROM votes WHERE letter_id = ? AND report = 1)
       WHERE id = ?`
    ).bind(letterId, letterId, letterId, letterId, letterId),
    env.DB.prepare(
      `UPDATE letters SET hidden = 1, hidden_at = ?, body = '',
         hidden_reason = CASE WHEN reports >= ? THEN 'reports' ELSE 'downvotes' END
       WHERE id = ? AND hidden = 0 AND (score <= ? OR reports >= ?)`
    ).bind(t, hideReports, letterId, hideScore, hideReports),
    env.DB.prepare('SELECT score, upvotes, downvotes, reports, hidden FROM letters WHERE id = ?').bind(letterId),
  ]);

  const after = results[3].results[0];
  return json({ ok: true, value, score: after.score, upvotes: after.upvotes, downvotes: after.downvotes, removed: after.hidden === 1 });
}

// ---------------------------------------------------------------------------
// GET /v1/mine  /  DELETE /v1/letters/:id
// ---------------------------------------------------------------------------

async function mine(env, auth) {
  const rs = await env.DB.prepare(
    `SELECT id, name, body, lang, score, upvotes, downvotes, delivered, hidden, hidden_reason, created_at, style
     FROM letters WHERE author_id = ? ORDER BY created_at DESC LIMIT 50`
  ).bind(auth.authorId).all();
  return json({ letters: rs.results.map((r) => ({ ...r, style: parseStyle(r.style) })) });
}

async function deleteOwnLetter(env, auth, letterId) {
  const t = now();
  const res = await env.DB.prepare(
    `UPDATE letters SET hidden = 1, hidden_at = ?, hidden_reason = 'author', body = '' WHERE id = ? AND author_id = ? AND hidden = 0`
  ).bind(t, letterId, auth.authorId).run();
  if (!res.meta.changes) throw new HttpError(404, 'not_found', 'Carta não encontrada (ou já removida).');
  return json({ ok: true });
}

// ---------------------------------------------------------------------------
// Admin
// ---------------------------------------------------------------------------

async function adminRoute(request, env, path, method) {
  if (!env.ADMIN_KEY) throw new HttpError(404, 'not_found', 'Admin desativado.');
  const key = request.headers.get('X-Admin-Key') || '';
  if (!timingSafeEqual(key, env.ADMIN_KEY)) throw new HttpError(403, 'forbidden', 'Chave de admin inválida.');
  const url = new URL(request.url);
  let m;

  if (method === 'GET' && path === '/admin/stats') {
    const [a, b, c] = await env.DB.batch([
      env.DB.prepare('SELECT COUNT(*) AS n FROM letters WHERE hidden = 0'),
      env.DB.prepare('SELECT COUNT(*) AS n FROM letters WHERE hidden = 1'),
      env.DB.prepare('SELECT COUNT(*) AS n FROM votes'),
    ]);
    return json({ visible: a.results[0].n, hidden: b.results[0].n, votes: c.results[0].n });
  }
  if (method === 'GET' && path === '/admin/letters') {
    const hidden = url.searchParams.get('hidden') === '1' ? 1 : 0;
    const limit = clamp(int(url.searchParams.get('limit'), 50), 1, 200);
    const before = int(url.searchParams.get('before'), 2147483647);
    const rs = await env.DB.prepare(
      `SELECT id, author_id, name, body, lang, score, upvotes, downvotes, reports, delivered, hidden, hidden_reason, created_at
       FROM letters WHERE hidden = ? AND id < ? ORDER BY id DESC LIMIT ?`
    ).bind(hidden, before, limit).all();
    return json({ letters: rs.results });
  }
  if ((m = path.match(/^\/admin\/letters\/(\d+)$/)) && method === 'DELETE') {
    await env.DB.prepare(`UPDATE letters SET hidden = 1, hidden_at = ?, hidden_reason = 'admin', body = '' WHERE id = ?`).bind(now(), Number(m[1])).run();
    return json({ ok: true });
  }
  if (method === 'POST' && path === '/admin/bans') {
    const body = await readJson(request);
    if (!body.author_id) throw new HttpError(400, 'bad_request', 'author_id obrigatório.');
    await env.DB.batch([
      env.DB.prepare('INSERT OR REPLACE INTO bans (author_id, reason, created_at) VALUES (?, ?, ?)').bind(body.author_id, String(body.reason || ''), now()),
      env.DB.prepare(`UPDATE letters SET hidden = 1, hidden_at = ?, hidden_reason = 'ban', body = '' WHERE author_id = ? AND hidden = 0`).bind(now(), body.author_id),
    ]);
    return json({ ok: true });
  }
  if ((m = path.match(/^\/admin\/bans\/([A-Za-z0-9]+)$/)) && method === 'DELETE') {
    await env.DB.prepare('DELETE FROM bans WHERE author_id = ?').bind(m[1]).run();
    return json({ ok: true });
  }
  throw new HttpError(404, 'not_found', 'Rota admin não encontrada.');
}

// ---------------------------------------------------------------------------
// Validação de texto
// ---------------------------------------------------------------------------

function normalizeBody(s) {
  return s
    .normalize('NFC')
    .replace(/\r\n?/g, '\n')
    .replace(/[^\P{C}\n]/gu, '')        // remove caracteres de controle (mantém \n)
    .replace(/[ \t]+/g, ' ')
    .replace(/\n{3,}/g, '\n\n')
    .split('\n').map((l) => l.trim()).join('\n')
    .trim();
}

function normalizeName(name, maxLen) {
  if (name == null) return null;
  const s = String(name).normalize('NFC').replace(/[^\p{L}\p{N} .'\-]/gu, '').replace(/\s+/g, ' ').trim();
  if (!s) return null;
  return [...s].slice(0, maxLen).join('');
}

function normalizeLang(lang) {
  const s = String(lang || 'en').toLowerCase().replace(/[^a-z\-]/g, '');
  return s.slice(0, 5) || 'en';
}

function looksLikeLink(text) {
  return /(https?:\/\/|www\.|\b[a-z0-9-]+\.(com|net|org|io|gg|br|ru|xyz|tv|me|co)\b|discord\.gg)/i.test(text);
}

function isLowEffort(text) {
  const letters = text.replace(/[^\p{L}]/gu, '');
  if (letters.length < 6) return true;                    // só símbolos/números
  if (/(.)\1{9,}/u.test(text)) return true;                // "aaaaaaaaaa"
  const distinct = new Set(letters.toLowerCase()).size;
  return distinct < 4;                                     // "ababababab"
}

/**
 * Design da carta: objeto com ids curtos (paper, ink, layout, border, stamp, sticker).
 * O servidor só valida a forma; quais ids existem é assunto do mod (ids desconhecidos caem no padrão).
 */
const STYLE_KEYS = ['paper', 'ink', 'layout', 'border', 'stamp', 'sticker'];
function normalizeStyle(style) {
  if (style == null) return null;
  if (typeof style !== 'object' || Array.isArray(style)) throw new HttpError(400, 'bad_style', 'style deve ser um objeto.');
  const out = {};
  for (const key of STYLE_KEYS) {
    const v = style[key];
    if (v == null) continue;
    if (typeof v !== 'string' || !/^[a-z0-9_-]{1,24}$/.test(v)) throw new HttpError(400, 'bad_style', `style.${key} inválido.`);
    out[key] = v;
  }
  const keys = Object.keys(out);
  if (keys.length === 0) return null;
  return JSON.stringify(out);
}
function parseStyle(s) {
  if (!s) return null;
  try { return JSON.parse(s); } catch { return null; }
}

function findBlockedWord(text, list) {
  if (!list) return null;
  const words = list.split(',').map((w) => w.trim().toLowerCase()).filter(Boolean);
  const lower = text.toLowerCase();
  return words.find((w) => lower.includes(w)) || null;
}

// ---------------------------------------------------------------------------
// Utilidades
// ---------------------------------------------------------------------------

class HttpError extends Error {
  constructor(status, code, message, extra = {}) { super(message); this.status = status; this.code = code; this.extra = extra; }
}

function json(data, status = 200) {
  return new Response(JSON.stringify(data), { status, headers: JSON_HEADERS });
}

async function readJson(request) {
  const ct = request.headers.get('content-type') || '';
  if (!ct.includes('application/json')) throw new HttpError(415, 'bad_content_type', 'Envie JSON (content-type: application/json).');
  const text = await request.text();
  if (text.length > 64 * 1024) throw new HttpError(413, 'too_large', 'Corpo da requisição muito grande.');
  try { return JSON.parse(text || '{}'); } catch { throw new HttpError(400, 'bad_json', 'JSON inválido.'); }
}

function now() { return Math.floor(Date.now() / 1000); }
function int(v, d) { const n = parseInt(v, 10); return Number.isFinite(n) ? n : d; }
function clamp(n, lo, hi) { return Math.min(hi, Math.max(lo, n)); }

function compareVersions(a, b) {
  const pa = String(a).split('.').map((x) => parseInt(x, 10) || 0);
  const pb = String(b).split('.').map((x) => parseInt(x, 10) || 0);
  for (let i = 0; i < Math.max(pa.length, pb.length); i++) {
    const d = (pa[i] || 0) - (pb[i] || 0);
    if (d !== 0) return d;
  }
  return 0;
}

async function sha256Hex(s) {
  const buf = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(s));
  return [...new Uint8Array(buf)].map((b) => b.toString(16).padStart(2, '0')).join('');
}

let hmacKeyCache = null;
async function voteToken(env, letterId, authorId) {
  if (!hmacKeyCache || hmacKeyCache.secret !== env.SIGNING_SECRET) {
    const key = await crypto.subtle.importKey('raw', new TextEncoder().encode(env.SIGNING_SECRET), { name: 'HMAC', hash: 'SHA-256' }, false, ['sign']);
    hmacKeyCache = { secret: env.SIGNING_SECRET, key };
  }
  const sig = await crypto.subtle.sign('HMAC', hmacKeyCache.key, new TextEncoder().encode(`vote:${letterId}:${authorId}`));
  return base64url(new Uint8Array(sig)).slice(0, 32);
}

function base64url(bytes) {
  let s = '';
  for (const b of bytes) s += String.fromCharCode(b);
  return btoa(s).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

function timingSafeEqual(a, b) {
  const ea = new TextEncoder().encode(a);
  const eb = new TextEncoder().encode(b);
  if (ea.length !== eb.length) return false;
  let diff = 0;
  for (let i = 0; i < ea.length; i++) diff |= ea[i] ^ eb[i];
  return diff === 0;
}
