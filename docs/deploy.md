# Deploy - FinanceMove (Railway + Vercel)

> Roteiro para colocar o app no ar do zero e mante-lo. As decisoes por tras de cada passo estao no
> `docs/ADR-002-deploy-e-seguranca.md`. Tempo estimado do primeiro deploy: 30 a 45 minutos.

## Como fica no ar

```text
Navegador
   |  HTTPS, uma origem so: https://<projeto>.vercel.app
   v
Vercel  --- serve a SPA (web/dist) e repassa /api/* --->  Railway: API .NET (container)
                                                               |  rede privada do Railway
                                                               v
                                                          Railway: PostgreSQL (sem acesso publico)
```

O navegador so conhece o dominio da Vercel. Isso faz o cookie de sessao ser "de primeira parte"
(`SameSite=Strict`) e dispensa CORS. O Postgres nao fica exposto na internet.

## 0. Antes de tudo: proteja as contas

O ponto mais provavel de vazamento nao e o app, e a sua conta nos provedores. Quem entra no seu
Railway le o banco inteiro.

- [ ] Autenticacao em dois fatores (2FA) no **GitHub**, no **Railway** e na **Vercel**.
- [ ] O repositorio `DevMoraesss/financeapp` e **publico**. Tudo bem, desde que nenhum segredo
      entre no git. Atencao: a chave que esteve no `.env.example` no commit `64b5f57` (removida em
      `6f4abe0`) continua no historico publico para sempre. **Nunca use aquele valor** em producao.

## 1. Gere os segredos (na sua maquina)

```bash
openssl rand -base64 48   # -> Jwt__SigningKey (assina os tokens de acesso)
openssl rand -base64 18   # -> Registration__InviteCode (codigo de convite)
```

Guarde os dois num gerenciador de senhas. Eles vao **somente** para as variaveis do Railway.

## 2. Railway: banco de dados

1. Railway -> **New Project** -> **Deploy PostgreSQL**.
2. No servico Postgres -> **Settings -> Networking**: se existir um **TCP Proxy** (endereco
   `*.proxy.rlwy.net`), remova. A API acessa o banco pela rede privada; ninguem mais precisa.
3. Se o seu plano oferecer **Backups** do volume, ative (servico Postgres -> Backups).

## 3. Railway: API

1. No mesmo projeto: **New -> GitHub Repo -> DevMoraesss/financeapp**. O `railway.json` da raiz
   ja configura tudo: build pelo `Dockerfile`, healthcheck em `/health/ready`, 1 replica, e so
   refaz o deploy quando algo em `src/` muda.
2. Aba **Variables** do servico da API:

   | Variavel | Valor |
   |---|---|
   | `ConnectionStrings__Postgres` | `${{Postgres.DATABASE_URL}}` (referencia; a senha do banco nunca e copiada) |
   | `Jwt__SigningKey` | o primeiro valor do passo 1 |
   | `Registration__InviteCode` | o segundo valor do passo 1 |
   | `Registration__MaxUsers` | `10` |
   | `Database__MigrateOnStartup` | `true` |

   **Nao** defina `TEST_TODAY`, `ASPNETCORE_ENVIRONMENT` nem `Cors__AllowedOrigin`. O Dockerfile
   ja fixa `Production`, e em Production o `TEST_TODAY` e ignorado de qualquer forma.
3. **Settings -> Networking -> Generate Domain**, porta **8080**. Edite o prefixo para algo seu
   (ex.: `financemove-api-juan`). Anote o dominio completo: `https://<prefixo>.up.railway.app`.
4. Faca o deploy e acompanhe **Deployments -> Logs**. Esperado no primeiro deploy:
   - `Aplicando 1 migration(s) de IdentityModuleDbContext` e o mesmo para Accounts, Transactions
     e Budget, nessa ordem;
   - algumas linhas `fail: ... __ef_migrations_history`. **Sao normais**: e o EF procurando a
     tabela de historico que ainda nao existe num banco vazio. Nao aparecem nos deploys seguintes.
5. Confira: `curl https://<prefixo>.up.railway.app/health/ready` deve responder
   `{"status":"Healthy",...}`.

## 4. Vercel: SPA

1. No repositorio, edite `web/vercel.json` e troque `TROQUE-PELO-DOMINIO-DA-API.up.railway.app`
   pelo dominio do passo 3. Commit e push.
2. Vercel -> **Add New -> Project** -> importe o repositorio -> **Root Directory: `web`**. O
   framework (Vite) e detectado. Nenhuma variavel de ambiente e necessaria.
3. Deploy. O endereco final fica `https://<projeto>.vercel.app`.

## 5. Primeiro acesso e verificacao

1. Abra `https://<projeto>.vercel.app`, clique em **Criar agora** e cadastre-se com o codigo de
   convite. Senha: minimo de 12 caracteres (uma frase serve).
2. Com o app aberto, DevTools -> **Application -> Cookies**: `refreshToken` precisa estar com
   **HttpOnly**, **Secure**, **SameSite=Strict** e Path `/api/v1/auth`.
3. Rode as checagens de seguranca (troque os dominios):

   ```bash
   APP=https://<projeto>.vercel.app
   # cadastro sem convite: 403
   curl -s -o /dev/null -w "%{http_code}\n" -H 'Content-Type: application/json' \
     -d '{"name":"x","email":"x@x.com","password":"umaFraseBemLonga"}' $APP/api/v1/auth/register
   # rota protegida sem token: 401
   curl -s -o /dev/null -w "%{http_code}\n" $APP/api/v1/accounts
   # cabecalhos de seguranca da SPA
   curl -sI $APP | grep -iE "content-security-policy|strict-transport|x-frame-options"
   ```

4. Lance um gasto no cartao e confira a fatura. Se chegou ate aqui, esta no ar.

## Convidar alguem

- Mande o codigo de convite por canal privado (nao em grupo).
- O teto `Registration__MaxUsers=10` vale para o sistema inteiro, contando voce.
- Quando todos tiverem entrado, **feche o cadastro**: apague `Registration__InviteCode` no Railway
  (sem codigo, o cadastro fica fechado) ou troque por um novo.

## Operacao do dia a dia

| Tarefa | Como |
|---|---|
| Ver erro que alguem relatou | A tela mostra um `traceId`; procure por ele em Railway -> Logs |
| Voltar uma versao | Railway -> Deployments -> no deploy anterior, **Redeploy**. Cuidado: migration ja aplicada nao volta sozinha |
| Destravar conta (5 senhas erradas = 15 min travada) | Esperar 15 min, ou no banco: `UPDATE identity.app_user SET lockout_end = NULL, access_failed_count = 0 WHERE email = '...';` |
| Deslogar todo mundo | `UPDATE identity.refresh_token SET expires_at = now();` |
| Trocar a chave JWT | Nova `Jwt__SigningKey` no Railway. Os tokens de acesso antigos morrem; as sessoes se renovam sozinhas pelo refresh |
| Backup do banco | Backups do Railway, se o plano tiver. Manual: reative o TCP Proxy por alguns minutos, rode `pg_dump "<DATABASE_PUBLIC_URL>" -Fc -f financemove-AAAA-MM-DD.dump` e remova o proxy de novo. Guarde o arquivo fora do repositorio |
| Backup pessoal | Configuracoes -> Exportar meus dados (CSV com todas as transacoes) |

Para rodar SQL no banco de producao use o painel **Data/Query** do servico Postgres no Railway.

## Problemas comuns

| Sintoma | Causa provavel |
|---|---|
| `/api/...` responde 404 ou erro de DNS na Vercel | `web/vercel.json` com o dominio errado ou sem o rewrite |
| Login funciona, mas o F5 manda de volta para o login | Voce esta abrindo a API ou um preview por outro dominio. Use sempre `https://<projeto>.vercel.app` |
| Cadastro responde 403 | `Registration__InviteCode` ausente (cadastro fechado) ou codigo digitado errado |
| Deploy fica vermelho no healthcheck | Veja os logs: quase sempre `ConnectionStrings__Postgres` ou uma migration falhando |
| 429 "Muitas tentativas" | Rate limit: 10 logins ou cadastros por minuto por IP. Espere um minuto |

## O que o FinanceMove nunca guarda

- **Numero de cartao, CVV, senha de banco ou token de banco.** Um cartao, para o app, e so um nome,
  o dia de fechamento e o dia de vencimento. Nao existe campo para o resto, e nao precisa existir.
- Evite dado sensivel nas descricoes (numero de documento, senha). Descricao e texto livre.

## Limites conhecidos (em aberto)

- **Sem 2FA dentro do app ainda.** A protecao hoje e senha de 12+ caracteres, lockout, rate limit e
  cadastro por convite. TOTP (Google Authenticator) e o proximo passo recomendado antes de
  convidar mais gente (`docs/prompts.md`).
- **Lockout como incomodo:** quem souber o seu e-mail consegue travar sua conta por 15 minutos
  errando a senha 5 vezes. E o preco da protecao contra forca bruta.
- **Rate limit por IP** confia no `X-Forwarded-For` que a Vercel preenche. Quem chamar o dominio do
  Railway direto consegue falsificar o IP e fugir do limite por IP; o lockout por conta continua
  valendo nesse caso.
- **Row Level Security** do Postgres ainda nao esta ativo (divida consciente do ADR-001). O
  isolamento entre usuarios e feito na aplicacao e coberto por teste de integracao.
