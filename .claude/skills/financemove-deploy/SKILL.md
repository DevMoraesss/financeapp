---
name: financemove-deploy
description: Prepara, conduz, verifica e depura o deploy do FinanceMove (API .NET + Postgres no Railway, SPA na Vercel com proxy /api de mesma origem), com scripts de pre-voo e de verificacao pos-deploy. Use sempre que o usuario falar em subir, publicar, colocar no ar, deploy, Railway, Vercel, variavel de ambiente, dominio, migration em producao, rollback, backup, ou relatar que algo "funciona local mas nao no ar" (login que cai no F5, 404/502 no /api, healthcheck vermelho, 403 no cadastro).
---

# Deploy do FinanceMove

O roteiro humano, passo a passo, esta em `docs/deploy.md` e as decisoes em
`docs/ADR-002-deploy-e-seguranca.md`. Esta skill e o lado do Claude: o que conferir antes, como
guiar o usuario pelos paineis e como provar que o deploy ficou bom.

Voce nao tem acesso aos paineis do Railway e da Vercel (nem CLI deles, a menos que o usuario tenha
instalado `railway`/`vercel`). Entao: automatize o que roda local, de ao usuario instrucoes
clicaveis para o resto e verifique o resultado de fora com `curl`.

## Desenho em uma frase

A Vercel serve a SPA e repassa `/api/*` para a API no Railway; a API fala com o Postgres pela rede
privada. O navegador so conhece o dominio da Vercel, por isso o cookie de sessao e `SameSite=Strict`
e nao existe CORS. Qualquer "solucao" que faca o front chamar o Railway direto quebra a sessao.

## Antes de subir: pre-voo

```bash
bash .claude/skills/financemove-deploy/scripts/preflight.sh [commit-que-esta-no-ar]
```

Ele confere o que so quebraria no provedor: dominio de exemplo no `web/vercel.json`, `.csproj` fora
do `Dockerfile` (restore falha no Railway), `DbContext` fora do `DatabaseMigrator` (tabela nao
criada), replica unica, segredo rastreado, e roda a definicao de pronto. Com o commit que esta no
ar, lista as migrations novas e avisa se alguma e destrutiva.

Precisa do SDK .NET 10 no PATH (`dotnet --version` 10.x). Sem Docker e sem
`FINANCEMOVE_TEST_POSTGRES`, a integracao fica para o CI do push; diga isso ao usuario.

Regras que nao dependem de script:
- **Migration destrutiva** (drop, rename, alter de tipo): backup antes do deploy
  (`docs/deploy.md`, "Backup do banco"). Migration aplicada nao volta com o rollback do codigo.
- **Nunca rode `dotnet ef database update` contra producao** da maquina local: o banco de producao
  so muda pelo deploy (`Database__MigrateOnStartup=true`), o que deixa rastro e ordem garantida.
- **Segredo so em variavel do Railway.** Se precisar gerar: `openssl rand -base64 48` (JWT) e
  `openssl rand -hex 12` (convite, facil de digitar). Nunca escreva o valor em arquivo do repositorio, que e publico.

## Primeiro deploy

Conduza o usuario pelas secoes 0 a 5 do `docs/deploy.md`, uma por vez, esperando a confirmacao dele
entre elas. Os pontos onde as pessoas erram:
1. Esquecer de remover o TCP Proxy do Postgres (banco exposto na internet).
2. `ConnectionStrings__Postgres` digitada com a senha copiada em vez da referencia
   `${{Postgres.DATABASE_URL}}`.
3. Nao trocar o dominio no `web/vercel.json` antes de importar na Vercel, ou importar com Root
   Directory errado (tem de ser `web`).
4. Definir `TEST_TODAY` ou `ASPNETCORE_ENVIRONMENT` no Railway (nao defina; o Dockerfile fixa
   Production).
5. Esquecer `PORT=8080`: o Railway injeta uma porta propria, a API escuta nela, e o dominio gerado
   para 8080 responde 502.
6. Esperar que o `railway.json` configure o servico: desde 28/08/2026 o Railway nao le mais config
   as code em servico novo. Builder (via `RAILWAY_DOCKERFILE_PATH=Dockerfile`), healthcheck, watch
   paths e Wait for CI sao feitos no painel (`docs/deploy.md`, secao 3).
7. Deixar o dominio de exemplo no `web/vercel.json`: o `/api` responde 404 com
   `x-railway-fallback: true` (e o Railway dizendo "Application not found").

## Depois de subir: verificacao

```bash
bash .claude/skills/financemove-deploy/scripts/verify.sh https://<projeto>.vercel.app https://<api>.up.railway.app
```

Checa, de fora: SPA com CSP/HSTS/DENY, F5 em rota interna, `/api` pelo proxy (401 sem token, 403 no
cadastro sem convite, `no-store`, `nosniff`) e `/health/ready` com o banco. Depois peca ao usuario
para conferir o cookie `refreshToken` no DevTools (HttpOnly, Secure, SameSite Strict) e lancar um
gasto de teste.

## Quando algo da errado

| Sintoma | Onde olhar |
|---|---|
| `/api` 404 com `x-railway-fallback: true` | O dominio do rewrite no `web/vercel.json` nao existe no Railway (placeholder ou digitado errado) |
| `/api` 502 ou DNS na Vercel | O dominio existe mas a API nao responde: porta do dominio diferente de 8080, ou deploy vermelho |
| Login ok, F5 volta para o login | Usuario abrindo outro dominio (preview da Vercel, dominio do Railway). O cookie so vale no dominio de producao da Vercel |
| Healthcheck vermelho no deploy | Logs do Railway: connection string, migration quebrada, `Jwt:SigningKey` curta (< 32) |
| `fail: ... __ef_migrations_history` no primeiro deploy | Normal: o EF procura a tabela de historico num banco vazio |
| 403 no cadastro | Sem `Registration__InviteCode` o cadastro fica fechado; ou codigo digitado errado |
| 429 | Rate limit por IP (10/min em login e cadastro). Esperar um minuto |
| Usuario travado | 5 senhas erradas = 15 min de lockout; SQL de destravar em `docs/deploy.md` |

Para erro relatado pelo usuario, peca o `traceId` que a tela mostra e procure nos logs do Railway.

**Rollback:** Railway -> Deployments -> Redeploy da versao anterior. Se a versao nova trouxe
migration, avise que o banco continua migrado; codigo antigo com banco novo precisa ser compativel
(coluna nova nullable costuma ser; coluna removida nao e).

Ao fim de qualquer deploy, diga ao usuario o que foi verificado (saida dos scripts) e o que ficou
por conta dele conferir no painel.
