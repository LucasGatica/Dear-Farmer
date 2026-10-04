-- Para bancos criados antes do design de cartas. Em bancos novos, schema.sql já inclui a coluna.
--   npx wrangler d1 execute dear-farmer --remote --file=./migrations/0002_style.sql
ALTER TABLE letters ADD COLUMN style TEXT;
