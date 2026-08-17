# Contrato da API - FinanceMove v1 (MVP)

> Base: `SPEC.md`, `docs/modelo-de-dados.md`, `docs/fluxos-usuario.md`.
> **Idioma:** rotas e campos JSON em **inglês**; textos de erro e dados do usuário (nomes de categoria,
> descrições) em **pt-BR**, porque são exibidos na UI.
> Endpoints marcados **v2** estão documentados para reservar o formato - **não entram no MVP**.

## 1. Convenções

| Item | Regra |
|---|---|
| Base URL | `https://api.financemove.app/api/v1` (dev: `http://localhost:5080/api/v1`) |
| Versão | no caminho (`/v1`) desde o dia 1 - front e API deployam separados (ADR Decisão 4) |
| Formato | JSON UTF-8; `Content-Type: application/json` |
| Autenticação | `Authorization: Bearer <accessToken>` em tudo, exceto `/auth/*` e `/health` |
| Datas | ISO-8601 `YYYY-MM-DD` no JSON (a UI converte para DD/MM/YYYY) |
| Mês | `YYYY-MM` (ex.: `2026-08`) |
| Dinheiro | JSON **number** com 2 casas (`1234.56`). Ver secao 1.1 |
| IDs | UUID string |
| Nomes de campo | `camelCase` em inglês |
| Fuso | "hoje" é sempre America/Sao_Paulo, resolvido no servidor |

### 1.1 A regra do dinheiro no JSON

Valores viajam como número decimal (`"amount": 150.00`). **A SPA nunca soma, subtrai ou calcula
percentual** desses números - JavaScript os carrega como `double` e reintroduziria o erro de ponto
flutuante que o `decimal` do servidor eliminou (modelo-de-dados secao 4.1). Todo total, saldo, percentual e
projeção **vem pronto da API**; a UI apenas formata com `Intl.NumberFormat('pt-BR', {style:'currency',
currency:'BRL'})`.

Por isso respostas de escrita devolvem o estado recalculado (ex.: `accountBalance` após criar
transação): é mais barato que a UI tentar deduzir.

### 1.2 Isolamento entre usuários

Toda rota resolve o `userId` **do token**, nunca de parâmetro. Pedir um recurso de outro usuário
responde **404** (não 403 - 403 confirmaria que o id existe). É o requisito US-14.

## 2. Autenticação

Fluxo: `register` -> `login` -> usa o **access token** (15 min, guardado em memória na SPA) -> quando
expira, `refresh` (refresh token de 7 dias em cookie `httpOnly`, **rotacionado** a cada uso).

| Método | Rota | Auth | Descrição |
|---|---|---|---|
| POST | `/auth/register` | anônima | cria usuário + dispara seed de categorias |
| POST | `/auth/login` | anônima | devolve access token (+ cookie de refresh) |
| POST | `/auth/refresh` | cookie | rotaciona e devolve novo access token |
| POST | `/auth/logout` | bearer | revoga o refresh token atual |
| POST | `/auth/confirm-email` | anônima | consome o token do e-mail |
| POST | `/auth/forgot-password` | anônima | envia link (resposta sempre 202, ver secao 2.1) |
| POST | `/auth/reset-password` | anônima | troca a senha com o token do e-mail |

### POST /auth/register

```jsonc
// Request
{ "name": "Juan", "email": "juan@exemplo.com", "password": "umaSenhaBoa123" }

// 201 Created
{ "id": "0192f3a1-...", "name": "Juan", "email": "juan@exemplo.com" }
```

### POST /auth/login

```jsonc
// Request
{ "email": "juan@exemplo.com", "password": "umaSenhaBoa123" }

// 200 OK (+ Set-Cookie: refreshToken=...; HttpOnly; Secure; SameSite=None)
{
  "accessToken": "eyJhbGciOi...",
  "expiresAt": "2026-08-15T18:45:00Z",
  "user": { "id": "0192f3a1-...", "name": "Juan", "email": "juan@exemplo.com" }
}

// 401 - credencial inválida OU conta bloqueada por tentativas (mensagem idêntica de propósito)
```

### 2.1 Respostas deliberadamente "cegas"

`register` com e-mail existente devolve **201**, e `forgot-password` devolve **202** mesmo para e-mail
inexistente. Parece errado, mas é proposital: respostas diferentes permitiriam a qualquer um descobrir
**quem tem conta no app** (*user enumeration*). O usuário legítimo recebe a informação por e-mail.

## 3. Padrão de erro - ProblemDetails (RFC 9457)

Todo erro, sem exceção, responde neste formato (é o nativo do ASP.NET Core - `Results.Problem()` e a
validação automática já o produzem). **Mensagens em pt-BR**, porque vão para a tela:

```jsonc
// 400 - erro de validação (o mais comum)
{
  "type": "https://financemove.app/errors/validation",
  "title": "Requisição inválida",
  "status": 400,
  "detail": "Um ou mais campos falharam na validação.",
  "instance": "/api/v1/transactions",
  "traceId": "00-4bf92f35...-01",
  "errors": {
    "amount": ["O valor deve ser maior que zero."],
    "categoryId": ["Categoria não pode ser informada em transferência."]
  }
}

// 409 - conflito de regra de negócio
{
  "type": "https://financemove.app/errors/statement-already-paid",
  "title": "Fatura já paga",
  "status": 409,
  "detail": "A fatura 2026-09 do cartão Roxo já foi paga em 04/09/2026.",
  "instance": "/api/v1/cards/0192.../statements/2026-09/pay",
  "traceId": "00-9de11a02...-01"
}
```

| Código | Quando | Exemplo no FinanceMove |
|---|---|---|
| **400** | formato ou validação de campo | `amount` <= 0; data inválida; `description` > 120 |
| **401** | sem token, token expirado ou credencial errada | access token vencido -> a SPA chama `/auth/refresh` |
| **403** | autenticado mas proibido | raro na v1 (não há papéis) |
| **404** | não existe **ou não é seu** | id de transação de outro usuário (US-14) |
| **409** | conflito com o estado atual | pagar fatura já paga; orçamento duplicado; nome de conta repetido |
| **422** | sintaxe ok, regra de domínio impede | parcelar fora de cartão; categoria de receita numa despesa |
| **429** | rate limit | 5 tentativas de login por minuto por IP |
| **500** | falha inesperada | resposta genérica + `traceId`; **detalhe técnico nunca vai no corpo** |

`traceId` aparece em todas as respostas de erro e nos logs do servidor: é como você liga a reclamação
do usuário à linha de log correspondente.

## 4. Accounts

| Método | Rota | Descrição |
|---|---|---|
| GET | `/accounts` | lista (query `includeArchived=false`) com saldo atual de cada uma |
| POST | `/accounts` | cria |
| GET | `/accounts/{id}` | detalhe |
| PUT | `/accounts/{id}` | edita nome/ciclo |
| POST | `/accounts/{id}/archive` | arquiva (não exclui - modelo-de-dados secao 5.3) |
| DELETE | `/accounts/{id}` | exclui **apenas** se nunca teve transação; senão 409 |
| POST | `/accounts/{id}/adjust-balance` | cria transação de ajuste (US-11) |

```jsonc
// POST /accounts - cartão de crédito (fecha 25, vence 02 do mês seguinte)
{ "name": "Nubank", "type": "credit_card", "initialBalance": 0.00,
  "closingDay": 25, "dueDay": 2 }

// 201 Created
{ "id": "0192f3b2-...", "name": "Nubank", "type": "credit_card",
  "initialBalance": 0.00, "currentBalance": 0.00,
  "closingDay": 25, "dueDay": 2, "archived": false }
```

```jsonc
// GET /accounts -> saldo já calculado pelo servidor (SPEC secao 5.1)
{ "accounts": [
    { "id": "...", "name": "Corrente", "type": "checking", "currentBalance": 3730.00, "archived": false },
    { "id": "...", "name": "Nubank", "type": "credit_card", "currentBalance": -100.00,
      "closingDay": 25, "dueDay": 2, "archived": false }
  ],
  "totalBalance": 3630.00 }
```

```jsonc
// POST /accounts/{id}/adjust-balance - app diz 1230,00 e o banco real diz 1198,00
{ "realBalance": 1198.00 }

// 201 Created - a diferença vira transação na categoria de sistema "Ajuste"
{ "createdTransaction": { "id": "...", "type": "expense", "amount": 32.00,
                          "description": "Ajuste de saldo", "category": "Ajuste", "date": "2026-08-15" },
  "currentBalance": 1198.00 }
```

## 5. Categories

| Método | Rota | Descrição |
|---|---|---|
| GET | `/categories` | lista (`type=income\|expense`, `includeArchived`) |
| POST | `/categories` | cria |
| PUT | `/categories/{id}` | renomeia / muda cor e ícone (bloqueado se `system`) |
| POST | `/categories/{id}/archive` | arquiva |

```jsonc
// GET /categories?type=expense - nomes em pt-BR: são dados do usuário
{ "categories": [
  { "id": "...", "name": "Mercado", "type": "expense", "color": "#F59E0B", "icon": "shopping-cart",
    "system": false, "archived": false },
  { "id": "...", "name": "Ajuste", "type": "expense", "color": "#6B7280", "icon": "wrench",
    "system": true, "archived": false }
]}
```

Tentar editar ou arquivar uma categoria `system` -> **422**.

## 6. Transactions

| Método | Rota | Descrição |
|---|---|---|
| GET | `/transactions` | lista com filtros e paginação |
| POST | `/transactions` | cria avulsa (income, expense ou transfer) |
| POST | `/transactions/installments` | cria compra em Nx no cartão |
| GET | `/transactions/{id}` | detalhe |
| PUT | `/transactions/{id}` | edita |
| DELETE | `/transactions/{id}` | exclui |
| POST | `/transactions/{id}/confirm` | confirma pendente de recorrência (permite ajustar valor) |
| DELETE | `/transactions/{id}/discard` | descarta pendente sem afetar saldo |
| DELETE | `/installment-groups/{id}` | remove as parcelas **futuras** do grupo |

**Filtros do GET** (opcionais e combináveis): `month=2026-08`, `accountId`, `categoryId`,
`type=income|expense|transfer`, `status=pending|confirmed`, `search=` (descrição, case-insensitive),
`page=1`, `size=50` (máx. 200).

```jsonc
// GET /transactions?month=2026-08&type=expense&search=mercado
{
  "items": [
    { "id": "...", "type": "expense", "amount": 150.00, "date": "2026-08-14",
      "description": "Mercado Assaí", "status": "confirmed",
      "account": { "id": "...", "name": "Corrente" },
      "category": { "id": "...", "name": "Mercado", "color": "#F59E0B", "icon": "shopping-cart" },
      "installment": null, "recurring": false }
  ],
  "pagination": { "page": 1, "size": 50, "total": 1, "totalPages": 1 },
  "monthSummary": { "income": 3000.00, "expenses": 370.00, "balance": 2630.00 }
}
```

```jsonc
// POST /transactions - transferência (sem categoria, com destino)
{ "type": "transfer", "amount": 500.00, "date": "2026-08-15",
  "description": "Guardando", "accountId": "<corrente>", "destinationAccountId": "<poupanca>" }

// 201 Created
{ "id": "...", "type": "transfer", "amount": 500.00,
  "accountBalance": 3230.00, "destinationAccountBalance": 500.00 }
```

```jsonc
// POST /transactions/installments - R$ 300,00 em 3x no cartão
{ "description": "Fone", "totalAmount": 300.00, "installments": 3, "date": "2026-08-14",
  "cardId": "<nubank>", "categoryId": "<lazer>" }

// 201 Created - o rateio garante soma = total (modelo-de-dados secao 4.4)
{ "installmentGroupId": "...",
  "transactions": [
    { "id": "...", "description": "Fone 1/3", "amount": 100.00, "date": "2026-08-14", "statementMonth": "2026-09" },
    { "id": "...", "description": "Fone 2/3", "amount": 100.00, "date": "2026-09-14", "statementMonth": "2026-10" },
    { "id": "...", "description": "Fone 3/3", "amount": 100.00, "date": "2026-10-14", "statementMonth": "2026-11" }
  ]}
```

```jsonc
// POST /transactions/{id}/confirm - pendente de recorrência (conta de luz varia)
{ "amount": 132.40 } // opcional; sem o campo, confirma o valor da regra
// 200 OK
{ "id": "...", "status": "confirmed", "amount": 132.40, "accountBalance": 3597.60 }
```

## 7. Recurrences

| Método | Rota | Descrição |
|---|---|---|
| GET | `/recurrences` | lista as regras |
| POST | `/recurrences` | cria a regra (**não** cria transação) |
| PUT | `/recurrences/{id}` | edita - afeta só gerações futuras |
| POST | `/recurrences/{id}/deactivate` | para de gerar; nada é apagado |

```jsonc
// POST /recurrences
{ "description": "Internet Vivo Fibra", "amount": 120.00, "type": "expense",
  "frequency": "monthly", "referenceDay": 20, "startsOn": "2026-08-15",
  "accountId": "<corrente>", "categoryId": "<assinaturas>" }

// 201 Created - repare: nenhuma transação foi criada
{ "id": "...", "nextRunOn": "2026-08-20", "active": true }
```

`frequency: "yearly"` exige também `referenceMonth` (1-12); `"weekly"` usa `referenceDay` 1-7.

## 8. Card statements (faturas)

| Método | Rota | Descrição |
|---|---|---|
| GET | `/cards/{accountId}/statements` | lista os meses com total e situação |
| GET | `/cards/{accountId}/statements/{month}` | detalhe + compras da fatura |
| POST | `/cards/{accountId}/statements/{month}/pay` | paga o total via transferência |

```jsonc
// GET /cards/{id}/statements/2026-09 (cartão fecha 25, vence 02 -> fechamento no mês anterior)
{
  "month": "2026-09",
  "closingDate": "2026-08-25",
  "dueDate": "2026-09-02",
  "period": { "from": "2026-07-26", "to": "2026-08-25" },
  "status": "closed", // open | closed | paid
  "total": 1840.00,
  "purchases": [
    { "id": "...", "date": "2026-08-14", "description": "Fone 1/3", "amount": 100.00,
      "category": "Lazer" }
  ]
}
```

```jsonc
// POST /cards/{id}/statements/2026-09/pay
{ "sourceAccountId": "<corrente>" }

// 201 Created - vira TRANSFER, não despesa (SPEC D6)
{ "transaction": { "id": "...", "type": "transfer", "amount": 1840.00, "statementMonth": "2026-09" },
  "status": "paid", "sourceAccountBalance": 1790.00, "cardBalance": 0.00 }

// 409 se já paga · 422 se a conta informada não for cartão · 422 se a fatura ainda estiver aberta
```

## 9. Budgets

| Método | Rota | Descrição |
|---|---|---|
| GET | `/budgets?month=2026-08` | limites + progresso do mês |
| PUT | `/budgets/{categoryId}` | define ou atualiza o limite (idempotente) |
| DELETE | `/budgets/{categoryId}` | remove o limite |

```jsonc
// GET /budgets?month=2026-08 - a API decide a faixa; a UI só pinta
{
  "month": "2026-08",
  "summary": { "totalSpent": 10224.63, "totalLimit": 9200.00,
               "categoriesNearLimit": 4, "categoriesOverLimit": 5 },
  "items": [
    { "categoryId": "...", "category": "Mercado", "color": "#F59E0B",
      "limit": 1100.00, "spent": 1259.15, "percent": 114.5,
      "status": "over", "difference": -159.15 },
    { "categoryId": "...", "category": "Moradia", "color": "#3B82F6",
      "limit": 3400.00, "spent": 3322.20, "percent": 97.7,
      "status": "warning", "difference": 77.80 }
  ]
}
```

`status`: `normal` (< 80%), `warning` (80-100%), `over` (> 100%). Só despesas **confirmadas**;
transferências nunca entram (fluxos-usuario secao 3).

## 10. Dashboard

```jsonc
// GET /dashboard?month=2026-08 - uma chamada, tudo pronto para a tela
{
  "totalBalance": 3630.00,
  "accounts": [ { "id": "...", "name": "Corrente", "currentBalance": 3630.00 } ],
  "month": { "income": 3000.00, "expenses": 370.00, "balance": 2630.00 },
  "expensesByCategory": [
    { "category": "Mercado", "color": "#F59E0B", "amount": 150.00, "percent": 40.5 }
  ],
  "recentTransactions": [ /* 10 itens, mesmo formato do secao 6 */ ],
  "pending": [ { "id": "...", "description": "Internet Vivo Fibra", "amount": 120.00,
                 "date": "2026-08-20" } ]
}
```

Uma chamada só, deliberadamente: o dashboard do protótipo faria 5 requisições e sofreria com N+1.

## 11. Conta do usuário e LGPD

| Método | Rota | Descrição |
|---|---|---|
| GET | `/me` | perfil |
| PUT | `/me` | altera nome |
| POST | `/me/change-password` | senha atual + nova |
| GET | `/me/export` | **US-12** - CSV (UTF-8 com BOM, separador `;`, datas DD/MM/YYYY, valores com vírgula) |
| DELETE | `/me` | **US-13** - hard delete com confirmação |

```jsonc
// DELETE /me
{ "confirmation": "EXCLUIR", "password": "umaSenhaBoa123" }
// 204 No Content - DELETE em identity.app_user cascateia todos os schemas
```

## 12. Saúde e observabilidade

| Método | Rota | Auth | Descrição |
|---|---|---|---|
| GET | `/health` | anônima | liveness - o processo responde? |
| GET | `/health/ready` | anônima | readiness - inclui conexão com o Postgres |

```jsonc
// GET /health -> 200
{ "status": "Healthy", "version": "1.0.0", "durationMs": 2 }

// GET /health/ready -> 200 (ou 503 se o banco não responder)
{ "status": "Healthy", "checks": [ { "name": "postgres", "status": "Healthy", "durationMs": 7 } ] }
```

> Nota: `/health` fica **fora** do prefixo `/api/v1` - é infraestrutura, não contrato de produto, e o
> Railway aponta o healthcheck dele para lá.

## 13. Endpoints reservados para a v2

Formato registrado para não mudar contrato depois; **não implementar agora**.

| Rota | Módulo |
|---|---|
| `GET/POST /goals`, `POST /goals/{id}/contributions` | Goals - resposta traz `percentComplete` e `monthlySuggestion` (fluxos-usuario secao 4) |
| `GET/POST /investments/assets`, `POST /investments/contributions`, `PUT /investments/assets/{id}/price` | Investments (fluxos-usuario secao 5) |
| `POST /imports` (CSV/OFX) | Transactions - dedup por `externalId` |
| `GET /dashboard/cash-flow?months=6` | Dashboard |
