# CLAUDE.md - guia de trabalho no FinanceMove

App web de finanças pessoais (pt-BR, R$). Monólito modular em .NET 10 + PostgreSQL, SPA React
separada. Futuro módulo financeiro do hub **AppLife** - por isso as fronteiras entre módulos são
levadas a sério.

## Documentos (leia antes de mudar comportamento)

| Arquivo | O que responde |
|---|---|
| `SPEC.md` | O que o produto faz: escopo, histórias de usuário, critérios de aceite, verificação fim-a-fim |
| `docs/ADR-001-arquitetura.md` | Por que a stack é esta, e o que foi rejeitado |
| `docs/arquitetura.md` | Componentes, fronteiras dos módulos, interface para o AppLife |
| `docs/modelo-de-dados.md` | Tabelas, tipos, índices; por que decimal, como funciona recorrência e saldo |
| `docs/api.md` | Contrato dos endpoints e padrão de erro |
| `docs/fluxos-usuario.md` | Jornadas críticas camada a camada |

## Comandos

```bash
# Banco (Postgres 17 em container; publica na porta 5435 do host)
docker compose up -d
docker compose ps
docker compose down # para
docker compose down -v # para E APAGA os dados

# Build e testes
dotnet build FinanceMove.sln
dotnet test FinanceMove.sln # unitários + integração
dotnet test tests/UnitTests # só unitários (rápido, sem Docker)

# Lint / formatador
dotnet format FinanceMove.sln # corrige
dotnet format FinanceMove.sln --verify-no-changes # só verifica (é o que o CI roda)

# API
dotnet run --project src/Api --urls http://localhost:5080
curl http://localhost:5080/health
curl http://localhost:5080/health/ready

# Scripts de apoio (com a API rodando)
pwsh tools/smoke.ps1          # percorre o app do cadastro ao dashboard e confere os numeros
node tools/clean-chars.mjs    # troca travessao, setas e emoji por ASCII (--check no CI)
```

### O banco comeca vazio

O app nao tem dado de exemplo embutido: quem se cadastra recebe apenas as 16 categorias padrao
(SPEC Apendice A) e cria as proprias contas e lancamentos.

Dois scripts mexem em dados e **nenhum roda sozinho**:

- `tools/smoke.ps1` cria um usuario com e-mail aleatorio, exercita a API inteira e **exclui esse
  usuario no fim**. Nao encosta nos seus dados.
- `tools/seed-demo.ps1` popula uma conta de demonstracao (`juan@financemove.local`). Existe apenas
  para inspecionar as telas cheias; nao rode se quiser testar com os seus proprios dados.

Para zerar tudo e comecar limpo:

```bash
docker exec financemove-db psql -U financemove -d financemove -c "DELETE FROM identity.app_user;"
```

Um DELETE so, porque as chaves estrangeiras em cascata limpam os quatro schemas.

### Migrations (o comando tem pegadinha)

Cada módulo tem o **próprio DbContext e as próprias migrations**. Não existe "a migration do
projeto" - sempre diga qual módulo:

```bash
# Criar
dotnet ef migrations add <Nome> \
  --project src/Modules/Accounts/Accounts \
  --startup-project src/Api \
  --context AccountsDbContext \
  --output-dir Migrations

# Aplicar - ORDEM IMPORTA: Identity primeiro (as outras têm FK para identity.app_user)
dotnet ef database update --project src/Modules/Identity/Identity         --startup-project src/Api --context IdentityModuleDbContext
dotnet ef database update --project src/Modules/Accounts/Accounts         --startup-project src/Api --context AccountsDbContext
dotnet ef database update --project src/Modules/Transactions/Transactions --startup-project src/Api --context TransactionsDbContext
dotnet ef database update --project src/Modules/Budget/Budget             --startup-project src/Api --context BudgetDbContext

# Desfazer a última migration ainda não aplicada
dotnet ef migrations remove --project <modulo> --startup-project src/Api --context <Contexto>
```

### Frontend (`web/`)

```bash
cd web
npm install
npm run dev # http://localhost:5173 - proxy de /api para localhost:5080
npm run build # typecheck (tsc) + build de produção
npm run lint
```

## Estrutura

```
src/
  Api/                    host: Program.cs, endpoints, auth, CORS, DI
  Shared/                 IClock, ICurrentUser, Money, eventos, convencao snake_case
  Modules/
    Identity/             usuarios, senha, tokens
    Accounts/             contas bancarias e cartoes
    Transactions/         categorias, lancamentos, faturas, parcelas, recorrencia
    Budget/               limite mensal por categoria
tests/
  UnitTests/              rapidos, sem I/O
  IntegrationTests/       Postgres real via Testcontainers
web/
  src/lib/                api.ts (contrato tipado), format.ts (pt-BR), contextos
  src/components/         AppLayout, componentes de UI, formularios
  src/pages/              Login, Dashboard, Transactions, Statements, Budget, Settings
```

Cada modulo e um par de projetos: `X.Contracts` (o que os outros enxergam) e `X` (implementacao
interna). Modulos que ainda vao nascer: `Goals` e `Investments`, ambos v2.

## Convenções

### Idioma

- **Código, banco e API em inglês**: projetos, classes, tabelas, colunas, rotas, campos JSON.
- **Documentação e UI em pt-BR**. Mensagens de erro da API também (vão para a tela).
- **Dados do usuário em pt-BR**: nomes de categoria do seed ("Mercado", "Ajuste"), descrições.

### Fronteiras entre módulos (a regra que protege o futuro AppLife)

1. Um módulo **nunca** referencia a implementação de outro - só o `.Contracts`.
2. Um módulo **nunca** lê tabela de outro. Cada um tem seu schema no Postgres.
3. Comunicação: chamada de interface (via DI) ou evento in-process.
4. `Shared` é mínimo. Se algo "de negócio" quiser morar lá, a fronteira está errada.

### Código

- Namespace com escopo de arquivo, `var` quando o tipo é óbvio, chaves sempre.
- Implementações de contratos são `internal`; só interfaces e DTOs são públicos.
- Nomes de teste: `Metodo_Cenario_ResultadoEsperado` (com underscore mesmo).
- `.editorconfig` manda no estilo; `dotnet format` resolve.

## Armadilhas (já custaram tempo neste projeto)

### Dinheiro é SEMPRE decimal

A regra número um. `decimal` no C#, `numeric(14,2)` no Postgres. **Nunca `float`/`double`**, em
nenhuma camada - nem em variável temporária, nem em DTO.

```csharp
// CERTO
decimal valor = 10.50m;
Money.Round(x);                                  // AwayFromZero, 2 casas
Math.Round(x, 2, MidpointRounding.AwayFromZero); // se nao usar o helper

// ERRADO
double valor = 10.50;   // ponto flutuante: 0.1 + 0.2 != 0.3
Math.Round(2.5m);       // arredondamento bancario: devolve 2, e nao 3
```

- Divisão de parcelas: use `Money.Split` - a soma das parcelas tem de bater com o total ao centavo.
- **O front não faz conta de dinheiro.** Toda soma, percentual e projeção sai calculada da API;
  a SPA só formata com `Intl.NumberFormat('pt-BR')`. JSON number vira `double` no JavaScript.

### "Hoje" nunca vem de `DateTime.Now`

Use `IClock.Today` (injetado). Sem isso é impossível testar fatura fechando e recorrência
disparando - e a verificação da SPEC secao 10 avança o calendário. Em dev/teste, `TEST_TODAY=2026-09-04`
congela a data (ignorado em Production).

### As FKs entre schemas são escritas à mão

`<schema>.<tabela>.user_id -> identity.app_user.id ON DELETE CASCADE` está em SQL cru dentro das
migrations `InitialAccounts`, `InitialTransactions` e `InitialBudget`. O EF não gera isso sozinho
(contextos diferentes). **Se regerar qualquer uma dessas migrations, reponha o bloco.** São elas
que fazem o "excluir minha conta" da LGPD ser confiável: um único DELETE limpa os quatro schemas.

### Só ASCII em pontuação e símbolos

Nada de travessão, setas, aspas curvas, checkmarks ou emoji em código, comentário ou documento.
Acentos do português seguem normais. `node tools/clean-chars.mjs --check` verifica; sem `--check`,
corrige.

### Recurso de outro usuário responde 404, nunca 403

403 confirmaria que o id existe. O `userId` vem **sempre** do token (`ICurrentUser`), nunca de
rota, query ou corpo.

### A porta do Postgres é 5435, não 5432

Esta máquina já tem outros containers Postgres nas portas 5432, 5433 e 5434. Se der conflito,
`FINANCEMOVE_DB_PORT=5436 docker compose up -d` e ajuste `src/Api/appsettings.Development.json`.

### OpenAPI/Swagger foi removido do template

O `Microsoft.OpenApi` 2.0.0 tem vulnerabilidade alta (GHSA-v5pm-xwqc-g5wc) e a versão corrigida
(3.x) quebra o gerador do `Microsoft.AspNetCore.OpenApi` 10.0.5. Volta quando houver endpoints
para documentar - conferindo as versões compatíveis na ocasião.

### Teste de integração usa Postgres de verdade

Nada de banco em memória: dependemos de `numeric(14,2)`, CHECK constraints, índice único parcial e
`ON DELETE CASCADE` entre schemas. Um fake passaria em teste e quebraria em produção.
**Docker precisa estar rodando** para `dotnet test` completo.

## Segredos

- **Nunca** comite: connection string de produção, `Jwt:SigningKey` real, chave de API.
- `.env` está no `.gitignore`. `.env.example` é só documentação - mantenha valores de exemplo,
  não valores reais.
- `appsettings.Development.json` tem credencial **local** do docker-compose, que não vale nada
  fora da sua máquina. Produção só por variável de ambiente (Railway).

## Definição de pronto

Antes de considerar qualquer tarefa concluída:

```bash
dotnet format FinanceMove.sln --verify-no-changes # exit 0
dotnet build FinanceMove.sln # 0 erros, 0 avisos
dotnet test FinanceMove.sln # tudo verde
```
