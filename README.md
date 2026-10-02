# Dear Farmer

Mod de Stardew Valley (SMAPI) no estilo [Kind Words](https://store.steampowered.com/app/1070710/): você escreve uma carta na sua caixa de correio e ela aparece na caixa de correio de outro jogador, em algum lugar do mundo. Quem recebe pode dar **gostei**, **não gostei** ou **denunciar**. Cartas bem votadas circulam mais; cartas muito mal votadas são apagadas.

```
Dear-Farmer/
├── DearFarmer/   -> o mod (C#, SMAPI 4 / Stardew Valley 1.6)
├── backend/      -> o servidor (Cloudflare Worker + D1, custo zero)
└── tools/        -> script que gera o sprite sheet dos ícones
```

## Por que Cloudflare Workers + D1 (e não Firebase, Supabase, um VPS...)

O requisito era **zero gasto com backend**. As opções gratuitas que fazem sentido para um mod:

| Opção | Free tier | Problema |
|---|---|---|
| **Cloudflare Workers + D1** | 100k requisições/dia, D1 com 5 GB, 5M leituras e 100k escritas por dia, cron grátis | Nenhum: não hiberna, não pede cartão, latência baixa no mundo todo |
| Supabase | 500 MB Postgres | O projeto **pausa após 7 dias sem uso**; alguém precisa "acordar" na dashboard |
| Firebase Firestore | 50k leituras/dia | Regras de segurança complexas para rate limit; SDK pesado para usar de dentro do jogo |
| Render / Fly / Railway | pouco | Servidores gratuitos hibernam ou expiram; Railway cobra depois do trial |
| Deno Deploy | 1M req/mês | Precisa de banco externo (Deno KV) e o KV é mais limitado que o D1 |

Com o Worker, um jogador gera por volta de 1 requisição a cada 5 dias de jogo (as cartas são buscadas em lote) mais os votos. O free tier aguenta **milhares de jogadores ativos** sem custo. A conta de leituras no D1 é a que mais importa, e por isso o feed usa um índice `(lang, rand)` com pivô aleatório em vez de `ORDER BY RANDOM()` (que leria a tabela inteira a cada chamada).

## Como funciona

1. Na primeira execução o mod gera um **token anônimo** (UUID) e guarda em `.smapi/mod-data`. Não há cadastro, e-mail ou senha. O servidor guarda só o `sha256(token)`.
2. **Escrever**: clique na caixa de correio (sem correio pendente) ou aperte `F7` → "Escrever carta". O mod manda `POST /v1/letters`.
3. **Receber**: a cada dia do jogo o mod entrega até `LettersPerDay` (padrão 2) cartas na caixa de correio. Ele mantém uma fila local e só pede mais ao servidor (`POST /v1/feed`) quando a fila esvazia.
4. **Ler**: a carta entra no correio normal do jogo (bandeira levanta). Ao abrir, o mod troca o visualizador padrão pelo seu, com papel azulado, carimbo "DF" e botões de voto. Cartas do jogo continuam exatamente iguais.
5. **Votar**: cada carta entregue vem com um `vote_token` assinado (HMAC) pelo servidor. Só quem recebeu a carta consegue votar nela, e cada jogador tem um voto por carta (pode trocar ou remover).
6. **Moderação automática**: `score <= -5` ou `3 denúncias` → a carta é removida e o texto apagado do banco. O autor vê "removida" na lista "Cartas que escrevi" e pode apagar as próprias cartas quando quiser.

### Anti-abuso (em camadas)

| Camada | Regra | Onde |
|---|---|---|
| Dia do jogo | 3 cartas por dia do jogo (`MaxSentPerGameDay`) | mod |
| Hora real | 12 cartas/hora por jogador (`LETTERS_PER_HOUR`) | servidor |
| Dia real | 40 cartas/24h por jogador (`LETTERS_PER_DAY`) | servidor |
| IP | 30 cartas/hora por IP + 120 req/min por IP na borda | servidor |
| Votos | 120 votos/hora por jogador | servidor |
| Conteúdo | 10–600 caracteres, sem links, sem spam de caracteres repetidos, sem duplicata (mesmo texto em 7 dias), lista de palavras bloqueadas opcional | servidor |
| Nome | só letras/números/espaço, 24 caracteres | servidor |
| Ranking | peso = frescor × score × (1 / exposição): cartas novas e pouco entregues ganham chance, cartas negativas quase não aparecem | servidor |
| Remoção | score ≤ -5 ou ≥ 3 denúncias; cron apaga de vez após 30 dias | servidor |
| Ban | `/admin/bans` esconde tudo do autor e bloqueia novas cartas | servidor |

O limite por dia do jogo **não zera ao recarregar um save antigo**: o contador só reseta quando o jogo chega a um dia *maior* que o último dia em que você enviou. "Sair sem salvar" não dá cartas extras, e o limite por hora real do servidor segura o resto.

## Edge cases tratados

- **Servidor fora do ar / sem internet**: nada trava. O feed falha em silêncio e tenta de novo em 10 minutos; ao enviar, o erro aparece no menu e o **rascunho fica salvo** para tentar depois. Votos offline são desfeitos na UI com aviso.
- **Correio do jogo no mesmo dia**: o correio do jogo é aberto primeiro (está na frente da fila); as cartas do mod vêm depois. Opção `DeliverOnlyWhenMailboxEmpty` evita misturar.
- **Multiplayer**: cada jogador (host ou farmhand) tem identidade, fila de entrega e caixa de correio próprias. O mod nunca grava no save, então farmhands funcionam.
- **Mesma carta duas vezes**: o mod guarda os últimos 1000 ids vistos e manda os 300 mais recentes como `exclude`; o servidor também nunca devolve cartas do próprio autor.
- **Carta removida depois de entregue**: votar devolve `410`; o mod marca como removida e mostra o aviso em vez do texto.
- **Emoji / caracteres que a fonte não desenha**: filtrados na digitação e na renderização (viram `?`), nunca derrubam o jogo.
- **Teclas do jogo enquanto digita** (`E`, `I`, `Esc`...): a caixa de texto assume o teclado; `Esc` sai da caixa, e só então fecha o menu.
- **Idioma**: a carta vai com o idioma do jogo; o feed prioriza o mesmo idioma, cai para inglês e por último para qualquer idioma (jogador de idioma raro não fica sem cartas).
- **Texto do jogo em `Data/mail`**: o corpo é injetado com `%`, `@`, `^`, `[`, `{` neutralizados, então mesmo se o mod for removido o jogo mostra a carta como texto simples, sem quebrar.
- **Estado do mod apagado**: ids órfãos no correio recebem um texto de fallback em vez de carta em branco; ids antigos são limpos de `mailReceived` no fim do dia.
- **Mod desatualizado**: o servidor responde `426` e o mod pede para atualizar (`MIN_CLIENT_VERSION`).
- **Resolução pequena / UI scale**: o papel encolhe proporcionalmente, e a lista usa a caixa de diálogo padrão.
- **Controle**: menus suportam snapping (`SnappyMenus`), `B` fecha, `Y` envia, gatilhos trocam de página.
- **Carta gigante**: o visualizador pagina; o limite de 600 caracteres vale dos dois lados.
- **Vários jogadores na mesma casa**: limite por IP é maior que o por jogador para não punir irmãos.
- **Relógio do jogador errado**: todo rate limit usa o relógio do servidor.

## Subindo o backend (uma vez, ~5 minutos)

Pré-requisitos: conta gratuita na Cloudflare e Node 18+.

```bash
cd backend
npm install
npx wrangler login
npx wrangler d1 create dear-farmer          # copie o database_id para o wrangler.toml
npx wrangler d1 execute dear-farmer --remote --file=./schema.sql
npx wrangler secret put SIGNING_SECRET      # cole algo longo e aleatório (ex.: openssl rand -hex 32)
npx wrangler secret put ADMIN_KEY           # opcional, para os endpoints /admin
npx wrangler deploy                         # imprime a URL: https://dear-farmer.<sua-conta>.workers.dev
```

Teste: `curl https://dear-farmer.<sua-conta>.workers.dev/v1/health`.

Para rodar local: copie `.dev.vars.example` para `.dev.vars`, rode `npm run db:migrate:local`, `npm run dev` e, em outro terminal, `npm test` (teste de fumaça que cobre validação, feed, votos, remoção automática, rate limit e admin).

Os limites ficam em `[vars]` no `wrangler.toml` e podem ser mudados sem tocar no código.

### Moderação manual

```bash
curl -H "X-Admin-Key: $ADMIN_KEY" https://.../admin/stats
curl -H "X-Admin-Key: $ADMIN_KEY" "https://.../admin/letters?limit=50"
curl -H "X-Admin-Key: $ADMIN_KEY" -X DELETE https://.../admin/letters/123
curl -H "X-Admin-Key: $ADMIN_KEY" -X POST -H 'content-type: application/json' -d '{"author_id":"<hash>","reason":"spam"}' https://.../admin/bans
```

## Compilando o mod

Pré-requisitos: [.NET 6 SDK](https://dotnet.microsoft.com/download), Stardew Valley 1.6 e SMAPI 4 instalados.

```bash
cd DearFarmer
dotnet build -c Release
```

O pacote `Pathoschild.Stardew.ModBuildConfig` acha o jogo sozinho, referencia os DLLs e copia o mod para `Mods/DearFarmer`. Se não achar, descomente `<GamePath>` no `.csproj`.

Depois, em `Mods/DearFarmer/config.json` (criado na primeira execução), coloque a URL do seu Worker em `ApiBaseUrl`. Com o [Generic Mod Config Menu](https://www.nexusmods.com/stardewvalley/mods/5098) dá para mudar tudo pelo jogo.

> Este repositório foi escrito sem acesso ao jogo para compilar. O backend tem teste automatizado; o mod precisa de um `dotnet build` com o jogo instalado para a primeira validação (veja a seção de verificação abaixo).

### config.json

| Chave | Padrão | O que faz |
|---|---|---|
| `ApiBaseUrl` | — | URL do Worker |
| `LettersPerDay` | 2 | cartas entregues por dia do jogo (0–5) |
| `MaxSentPerGameDay` | 3 | cartas que você pode escrever por dia do jogo |
| `SignWithFarmerName` | true | assinar com o nome do fazendeiro (pode trocar na hora de escrever) |
| `OpenMenuKey` | F7 | abre o menu de qualquer lugar |
| `ShowHudMessage` | true | aviso no HUD ao receber carta |
| `DeliverOnlyWhenMailboxEmpty` | false | não entregar em dias com correio do jogo |
| `FetchBatchSize` | 8 | cartas buscadas por requisição |
| `PreferredLanguage` | (idioma do jogo) | ex. `pt-BR`, `en` |

## API (resumo)

Todas as rotas em `/v1` exigem `Authorization: Bearer <token>` e aceitam/retornam JSON.

| Rota | Corpo | Resposta |
|---|---|---|
| `GET /v1/health` | — | limites e versão mínima do cliente |
| `POST /v1/letters` | `{ body, name?, lang? }` | `{ id, remaining_hour, remaining_day }` |
| `POST /v1/feed` | `{ count, exclude: [ids], lang }` | `{ letters: [{ id, name, body, lang, created_at, vote_token }] }` |
| `POST /v1/letters/:id/vote` | `{ value: 1 \| -1 \| 0, vote_token }` | `{ score, upvotes, downvotes, removed }` |
| `POST /v1/letters/:id/report` | `{ vote_token, reason? }` | idem |
| `GET /v1/mine` | — | suas cartas com votos e status |
| `DELETE /v1/letters/:id` | — | apaga a própria carta |

Erros: `{ error, message, retry_after? }` com status 400/401/403/409/410/426/429.

## Verificação antes de publicar

1. `dotnet build` do mod com o jogo instalado (primeira compilação real).
2. Abrir um save, clicar na caixa de correio vazia → menu "Dear Farmer".
3. Escrever e enviar uma carta; conferir no `GET /v1/mine` ou no menu "Cartas que escrevi".
4. Com um segundo token (outra instalação ou apagar `.smapi/mod-data/LucasGatica.DearFarmer/state.json`), dormir um dia e ver a carta chegar.
5. Votar, denunciar, trocar o voto; conferir `score` em `/admin/letters`.
6. Desligar a internet e tentar enviar: a mensagem de erro aparece e o rascunho sobrevive ao fechar o menu.
