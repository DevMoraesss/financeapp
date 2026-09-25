---
name: financemove-feature
description: Implementa uma mudanca de comportamento no FinanceMove de ponta a ponta - contrato do modulo, servico, endpoint, api.ts, tela, testes e docs - sem quebrar as invariantes do projeto (dinheiro em decimal, isolamento por usuario, 404 para recurso alheio, front que nao calcula dinheiro). Use sempre que o pedido for criar ou alterar endpoint, campo, filtro, regra de negocio, relatorio ou tela do FinanceMove, mesmo que a pessoa nao diga "feature" (ex. "mostra a fatura atual no dashboard", "quero ver quanto ja comprometi de parcela", "adiciona observacao na transacao", "o orcamento devia considerar X").
---

# Feature ponta a ponta no FinanceMove

O FinanceMove e um monolito modular (.NET 10 + Postgres) com uma SPA React separada. Quase todo
bug caro deste projeto nasceu de uma camada "esquecida": a regra certa no servico mas o front
somando numero, ou o endpoint novo sem o filtro de usuario. Este roteiro existe para que a mudanca
atravesse todas as camadas de uma vez, na ordem certa.

O `CLAUDE.md` ja esta no contexto: ele manda nas convencoes. Aqui esta o *como* aplicar.

## 1. Situe a mudanca antes de escrever codigo

Responda para si mesmo, e se algo nao fechar, pergunte ao usuario antes de codar:

- **Qual modulo e dono do dado?**

  | Dado | Modulo dono |
  |---|---|
  | usuario, senha, sessao | Identity |
  | contas, cartao e ciclo de fatura (fechamento/vencimento) | Accounts |
  | lancamentos, categorias, faturas, parcelas, recorrencia, **saldo** | Transactions |
  | limite mensal por categoria | Budget |
  | metas, investimentos, patrimonio | ainda nao existem: use a skill `financemove-modulo` |

- **E um numero derivado?** Saldo, total de fatura, percentual, projecao: calcule no modulo dono dos
  fatos, na hora da leitura. Nunca vira coluna (SPEC D4 e D7): numero armazenado dessincroniza em
  silencio, e e a classe de bug que o projeto inteiro foi desenhado para evitar.
- **Precisa de dado de outro modulo?** Pela interface do `.Contracts` dele (`IAccountsQuery`,
  `ITransactionsQuery`...). Se falta um metodo, acrescente ao contrato. Nunca referencie a
  implementacao de outro modulo nem leia a tabela dele: e a fronteira que permite o AppLife absorver
  os modulos depois.
- **A resposta junta varios modulos?** Componha no host (`src/Api/Endpoints`), como o
  `DashboardEndpoints` faz.
- **E regra de negocio nova?** Atualize a `SPEC.md` (regra + criterio de aceite Dado/Quando/Entao)
  no mesmo trabalho. Spec desatualizada engana a proxima conversa.

## 2. Backend, de dentro para fora

Ordem: DTO/interface em `X.Contracts` -> servico `internal` em `X` -> endpoint em `src/Api/Endpoints`
-> `docs/api.md`.

- **Tenant:** o endpoint pega `ICurrentUser.Id` e passa como `userId` ao servico; toda query tem
  `UserId == userId`. Nao existe filtro global do EF neste projeto: o `Where` explicito E o
  isolamento. Esquecer um e vazar dado de outra pessoa.
- **Recurso inexistente ou de outro usuario:** o servico devolve `null` e o endpoint responde 404.
  Nunca 403, que confirmaria que o id existe.
- **Dinheiro:** `decimal`, `Money.Round`, `Money.Split` para dividir; coluna `numeric(14,2)`.
  Percentual com `Math.Round(x, 1, MidpointRounding.AwayFromZero)`. Nada de `double` nem em
  variavel temporaria.
- **Hoje:** `IClock.Today`, nunca `DateTime.Now` (a verificacao da SPEC avanca o calendario).
- **Entrada:** string do corpo pode chegar `null`; use `(valor ?? string.Empty).Trim()` e confira o
  tamanho contra a coluna `varchar`. Sem isso o banco estoura e o usuario ve um 500 generico.
- **Erro de regra:** `DomainException.Unprocessable` (422) ou `.Conflict` (409), com mensagem em
  pt-BR e um slug (`"invalid-amount"`). Siga o estilo dos arquivos vizinhos (as mensagens no C#
  estao sem acento).
- **Enum na query string:** receba `string?` e converta com `EnumParsing.ParseOptional`; o
  serializador JSON nao atua na query.
- **Escrita que muda saldo:** devolva o estado recalculado (`accountBalance`...) para a tela nao
  precisar somar.
- **Coluna ou tabela nova:** migration do modulo (comando no `CLAUDE.md`, sempre com `--context`).
  Se regenerar uma migration `Initial*`, reponha o bloco SQL da FK de `user_id`.

## 3. Front (`web/`)

- Tipo novo em `web/src/lib/api.ts`, espelhando o DTO (campos camelCase, enums em snake_case), e a
  chamada dentro de `endpoints`.
- Chame a API sempre por `api.ts`, nunca com `fetch` direto: e ele que renova a sessao uma vez so
  quando o token vence. Download de arquivo autenticado usa `download()`, nao `<a href>` (o link nao
  leva o token).
- Carregamento com `useApi` (ja trata carregando/erro/pronto) e componentes de `components/ui.tsx`.
- **O front nao faz conta de dinheiro.** Nada de somar, subtrair ou calcular percentual em JS; so
  `formatMoney`, `formatPercent`, `formatDate`. Se a tela precisa de um total que a API nao manda, a
  mudanca certa e a API mandar.
- Valor digitado pelo usuario ("1.234,56") passa pelo mesmo parse do `TransactionModal`
  (`parseAmount`); a data padrao vem de `todayIso()`.

## 4. Testes

- Regra pura (calculo, ciclo, divisao) -> `tests/UnitTests`.
- Qualquer coisa com banco ou endpoint -> `tests/IntegrationTests`, classe com
  `[Collection(PostgresCollection.Name)]`. `fixture.CreateUserAsync()` devolve um cliente ja logado;
  `fixture.CreateFactory(overrides)` sobe a API com configuracao diferente.
- Todo recurso novo ganha um teste de isolamento: outro usuario pedindo o id recebe 404.
- Nome: `Metodo_Cenario_ResultadoEsperado`.
- Sem Docker na maquina: `FINANCEMOVE_TEST_POSTGRES="Host=...;Username=...;Database=postgres"`.

## 5. Pronto

Rode a definicao de pronto do `CLAUDE.md` (format, build sem aviso, testes, `clean-chars --check`,
`npm run build` e `npm run lint` se mexeu no front). Atualize `docs/api.md` e, se for regra, a SPEC.

Ao terminar, conte ao usuario em poucas linhas: o que mudou para ele na tela, o que foi verificado
(com os comandos que passaram) e o que ficou de fora. Se algo nao foi possivel verificar, diga.
