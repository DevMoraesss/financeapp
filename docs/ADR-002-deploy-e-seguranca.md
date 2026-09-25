# ADR-002 - Deploy na Vercel com proxy de mesma origem, e as trancas do deploy publico

- **Status:** Aceita - 24/09/2026
- **Decisor:** Juan
- **Complementa:** ADR-001, Decisoes 4, 5 e 6. Substitui o Cloudflare Pages pela Vercel e troca o
  cookie de sessao cross-site por um cookie de primeira parte.

## Contexto

O MVP estava pronto mas nunca tinha ido para a internet. Tres fatos mudaram o desenho de deploy:

1. **Hospedagem da SPA:** o autor ja tem conta na Vercel (e nao no Cloudflare Pages, previsto no
   ADR-001). Railway continua com API e Postgres.
2. **Cookie de terceiros:** com SPA em `*.vercel.app` e API em `*.up.railway.app`, o cookie de
   refresh seria de outro site (`SameSite=None`). Safari (ITP) e os bloqueios de cookie de
   terceiros derrubam esse cookie: o usuario seria deslogado a cada F5.
3. **Exposicao real:** o app passa a ter URL publica, com dados financeiros de ate 10 pessoas.
   O cadastro aberto e a ausencia de rate limit, aceitaveis em localhost, deixam de ser.

## Decisao 1 - A Vercel serve a SPA e repassa `/api/*` para o Railway

`web/vercel.json` tem um rewrite `/api/:path*` -> `https://<api>.up.railway.app/api/:path*`. Para o
navegador existe uma origem so.

**Consequencias:** o cookie de refresh vira de primeira parte e pode ser `SameSite=Strict` (fecha
CSRF no refresh e no logout); CORS deixa de ser necessario (`Cors:AllowedOrigin` vazio em producao);
o front continua chamando `/api/v1` relativo, igual ao proxy do Vite em desenvolvimento.

**Alternativas rejeitadas:**

- *SPA e API em dominios diferentes com `SameSite=None`* - quebra em Safari e com bloqueio de cookie
  de terceiros; exige CORS com credenciais.
- *Dominio proprio com subdominios (app. e api.)* - resolveria o cookie (mesmo site), mas custa um
  dominio e DNS para configurar. Fica como opcao futura; o proxy nao impede.
- *Middleware da Vercel lendo a URL da API de variavel de ambiente* - evita o dominio fixo no
  `vercel.json`, mas poe codigo executando em toda requisicao. O dominio da API quase nunca muda.

## Decisao 2 - Cadastro por convite, com teto de usuarios

`Registration:InviteCode` (comparado em tempo constante) e `Registration:MaxUsers` (10). Sem codigo
configurado, o cadastro fica **fechado**, a menos que `Registration:Open=true` (so em
desenvolvimento). E o "cadastro por convite/link nao divulgado" da SPEC secao 9.4, agora imposto pelo
servidor e nao pela discricao.

## Decisao 3 - Trancas de autenticacao

| Tranca | Valor | Por que |
|---|---|---|
| Senha minima | 12 caracteres, maxima 128 | NIST SP 800-63B; o teto evita hash PBKDF2 de megabytes |
| Rate limit por IP | 10/min em login, cadastro, troca de senha e exclusao; 30/min no refresh; 300/min geral | Soma-se ao lockout por conta (5 erros, 15 min) |
| Corpo da requisicao | 64 KB | O maior JSON legitimo tem centenas de bytes |
| Rotacao do refresh | Atomica, com janela de 30 s para reuso | Ver abaixo |
| Cabecalhos | CSP, HSTS, nosniff, DENY, `Cache-Control: no-store` na API | Dado financeiro nao fica em cache nem em iframe |

**A janela de 30 segundos.** A deteccao de reuso do refresh token (token usado duas vezes = roubo,
derruba a cadeia) confundia uma corrida benigna com roubo: duas abas, ou tres requisicoes da mesma
tela recebendo 401 juntas, renovavam com o mesmo cookie. Resultado medido antes da correcao: a
segunda renovacao respondia 401 e matava ate o token recem-emitido, deslogando o usuario a cada
15 minutos. Agora o front faz **uma** renovacao por vez (`refreshSession` compartilha a promessa) e o
servidor aceita o token recem-rotacionado por 30 s. Logout e troca de senha nao ganham janela
(vencem o `expires_at` de toda a cadeia).

## Decisao 4 - Migrations no startup, uma replica

`Database:MigrateOnStartup=true` no Railway: a API aplica as migrations dos quatro modulos, na ordem
Identity -> Accounts -> Transactions -> Budget, antes de atender. Migration quebrada = API nao sobe =
healthcheck falha = Railway mantem a versao anterior.

**Condicao:** uma replica (configurada no painel do Railway). O cache em memoria do catch-up de
recorrencia tambem assume isso. Com mais replicas, a migration vai para um passo de pre-deploy e o
cache para o banco.

## Consequencias

**Positivas:** sessao estavel em qualquer navegador; nada de CORS em producao; banco sem acesso
publico; cadastro impossivel sem convite; deploy que nao quebra o que esta no ar.

**Negativas (aceitas):** o dominio da API fica escrito no `vercel.json` (trocar o dominio exige
commit); o rate limit por IP pode ser contornado por quem chamar o Railway direto falsificando
`X-Forwarded-For` (o lockout por conta continua valendo); 2FA ainda nao existe.

**Nota de 25/09/2026:** o Railway descontinuou o "config as code" (`railway.json`) para servicos
criados a partir de 28/08/2026. Build pelo Dockerfile, healthcheck, watch paths e replica passaram a
ser configurados no painel; o passo a passo esta no `docs/deploy.md`, secao 3.

**Documentos afetados:** `docs/deploy.md` (roteiro), `docs/api.md` (convite, 403, 429, cookie),
`CLAUDE.md`, `README.md`.
