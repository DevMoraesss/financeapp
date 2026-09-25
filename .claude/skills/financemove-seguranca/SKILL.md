---
name: financemove-seguranca
description: Revisao de seguranca do FinanceMove - isolamento entre usuarios, auth e sessao, cookie e proxy de mesma origem, segredos, dinheiro, dependencias e LGPD - com um script de varredura e um checklist especifico deste app financeiro. Use antes de todo commit ou deploy que mexa em backend, auth, endpoints, configuracao ou vercel.json, e sempre que o usuario perguntar "isso e seguro?", "pode subir?", "revisa isso", ou quando uma mudanca tocar AuthService, cookies, CORS, rate limit, queries com userId ou dados pessoais.
---

# Revisao de seguranca do FinanceMove

O app guarda a vida financeira de ate 10 pessoas e esta na internet. Os riscos reais, em ordem de
dano: (1) um usuario ver dado de outro, (2) alguem entrar numa conta, (3) segredo vazar no
repositorio, que e **publico**, (4) numero de dinheiro errado. O checklist abaixo segue essa ordem.
As defesas atuais e o porque de cada uma estao em `docs/ADR-002-deploy-e-seguranca.md`.

## 1. Delimite o que revisar

```bash
git diff --staged; git diff             # mudanca local
git diff main...HEAD                    # branch inteira
```

Revise o diff inteiro, mas leia tambem o arquivo em volta de cada trecho: falha de isolamento
costuma estar no metodo vizinho que o diff chama.

## 2. Rode a varredura

```bash
bash .claude/skills/financemove-seguranca/scripts/scan.sh          # heuristicas (segundos)
bash .claude/skills/financemove-seguranca/scripts/scan.sh --deps   # + NuGet e npm vulneraveis
```

A linha de base atual e **zero suspeitos**; qualquer linha listada e nova. Cada achado e um
suspeito: confira no codigo antes de reportar. O script nao substitui o checklist, so acha o
obvio rapido.

## 3. Checklist (o que o script nao ve)

**Isolamento (US-14)**
- Toda query sobre entidade de usuario filtra `UserId == userId`, e `userId` vem de `ICurrentUser`
  (token), nunca de rota, query ou corpo. Nao ha filtro global do EF: o `Where` e a tranca.
- Recurso inexistente ou alheio responde 404, inclusive em PUT/DELETE e em sub-recursos
  (`/cards/{id}/statements`).
- Id de outro modulo recebido no corpo (conta, categoria) e validado pelo contrato *com o userId*
  (`IAccountsQuery.FindAsync(userId, id)`), senao da para lancar na conta de outra pessoa.
- Recurso novo tem teste de integracao "outro usuario recebe 404".

**Autenticacao e sessao**
- Endpoint novo fora de grupo tem `.RequireAuthorization()`; anonimo tem rate limit
  (`RequireRateLimiting(RateLimiting.AuthPolicy)`).
- `AuthService.RefreshAsync`: a revogacao continua condicional e a janela (`ReuseGrace`) continua
  valendo so para token com sucessor. Logout e troca de senha matam a cadeia (`RevokeAllSessionsAsync`).
- Cookie de refresh: `HttpOnly`, `SameSite=Strict`, `Secure` fora de dev, `Path=/api/v1/auth`.
  Qualquer `SameSiteMode.None`, CORS com credenciais ou URL absoluta da API no front desfaz o
  desenho de mesma origem.
- Cadastro: convite em tempo constante, antes de consultar o e-mail; fechado por padrao.
- Senha: 12 a 128 caracteres; nada de logar senha, token ou hash.

**Segredos (repositorio publico)**
- Nenhuma chave, senha, connection string de producao ou codigo de convite em arquivo rastreado.
  Producao so em variavel do Railway. A chave que esteve no `.env.example` (commit `64b5f57`) e
  publica: nao pode ser usada.
- `dados-pessoais/` (CSV e extratos reais) nunca entra no git.

**Entrada e saida**
- Strings do corpo: null tratado e tamanho conferido contra a coluna; cor so `#RRGGBB`.
- SQL cru so com `FromSql($"...")` interpolado (vira parametro). `FromSqlRaw` com concatenacao e
  SQL injection.
- Erro 500 nao devolve detalhe tecnico (o `DomainExceptionHandler` ja faz; nao contorne).
- CSV exportado passa por `MeEndpoints.Escape` (anti CSV injection).
- Front: nada de `dangerouslySetInnerHTML`; a CSP do `web/vercel.json` nao ganha `unsafe-eval`.

**Dinheiro**
- `decimal`/`numeric(14,2)` em toda camada; front so formata. Um numero errado aqui e o dano mais
  silencioso: ninguem desconfia de um saldo que "parece certo".

**LGPD**
- Tabela nova com `user_id` tem a FK manual com `ON DELETE CASCADE`, e o teste de cascata.
- Dado novo aparece no export (`/me/export`) se for dado do usuario.

## 4. Como reportar

Liste os achados do mais grave para o mais leve:

```
[Critico|Alto|Medio|Baixo] arquivo:linha - o problema em uma frase
  Cenario: quem faz o que e o que consegue (ex.: "usuario B chama PUT /accounts/{id da A} e renomeia a conta da A")
  Correcao: a mudanca concreta
```

Se nada sobreviver a verificacao, diga isso e liste o que foi conferido (varredura + itens do
checklist), para o usuario saber o tamanho da garantia. Nao invente achado para parecer util, e nao
chame de seguro o que voce nao conferiu.
