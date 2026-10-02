-- Dear Farmer — esquema do banco (Cloudflare D1 / SQLite)
-- Aplicar com:  npx wrangler d1 execute dear-farmer --remote --file=./schema.sql

CREATE TABLE IF NOT EXISTS letters (
  id            INTEGER PRIMARY KEY AUTOINCREMENT,
  author_id     TEXT    NOT NULL,            -- sha256(token do jogador); nunca o token em si
  ip_hash       TEXT    NOT NULL,            -- sha256(ip + salt), usado só para rate limit
  name          TEXT,                        -- assinatura opcional ("Fazendeiro X")
  body          TEXT    NOT NULL,            -- texto da carta (vazio se removida)
  body_hash     TEXT    NOT NULL,            -- sha256(texto normalizado) para detectar spam repetido
  lang          TEXT    NOT NULL DEFAULT 'en',
  score         INTEGER NOT NULL DEFAULT 0,  -- soma dos votos (+1 / -1)
  upvotes       INTEGER NOT NULL DEFAULT 0,
  downvotes     INTEGER NOT NULL DEFAULT 0,
  reports       INTEGER NOT NULL DEFAULT 0,
  delivered     INTEGER NOT NULL DEFAULT 0,  -- quantas vezes foi entregue a alguem
  hidden        INTEGER NOT NULL DEFAULT 0,  -- 1 = removida (downvotes, denuncias, admin, autor)
  hidden_reason TEXT,
  hidden_at     INTEGER,
  rand          REAL    NOT NULL,            -- numero aleatorio fixo p/ amostragem barata via indice
  created_at    INTEGER NOT NULL             -- unix seconds
);

-- Amostragem do feed: busca por (lang, rand) só entre cartas visíveis.
CREATE INDEX IF NOT EXISTS idx_letters_serve   ON letters(lang, rand) WHERE hidden = 0;
CREATE INDEX IF NOT EXISTS idx_letters_author  ON letters(author_id, created_at);
CREATE INDEX IF NOT EXISTS idx_letters_ip      ON letters(ip_hash, created_at);
CREATE INDEX IF NOT EXISTS idx_letters_body    ON letters(body_hash, created_at);
CREATE INDEX IF NOT EXISTS idx_letters_hidden  ON letters(hidden_at) WHERE hidden = 1;

CREATE TABLE IF NOT EXISTS votes (
  letter_id   INTEGER NOT NULL REFERENCES letters(id) ON DELETE CASCADE,
  voter_id    TEXT    NOT NULL,
  value       INTEGER NOT NULL,              -- -1, 0 (removeu o voto) ou 1
  report      INTEGER NOT NULL DEFAULT 0,    -- 1 = denunciou
  reason      TEXT,
  created_at  INTEGER NOT NULL,
  updated_at  INTEGER NOT NULL,
  PRIMARY KEY (letter_id, voter_id)
);
CREATE INDEX IF NOT EXISTS idx_votes_voter ON votes(voter_id, updated_at);

CREATE TABLE IF NOT EXISTS bans (
  author_id  TEXT PRIMARY KEY,
  reason     TEXT,
  created_at INTEGER NOT NULL
);
