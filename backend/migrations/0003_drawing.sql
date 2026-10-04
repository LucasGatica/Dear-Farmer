-- Para bancos criados antes dos desenhos. Em bancos novos, schema.sql já inclui as colunas.
--   npx wrangler d1 execute dear-farmer --remote --file=./migrations/0003_drawing.sql
ALTER TABLE letters ADD COLUMN drawing TEXT;
ALTER TABLE letters ADD COLUMN had_drawing INTEGER NOT NULL DEFAULT 0;
