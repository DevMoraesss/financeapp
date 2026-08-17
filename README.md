# FinanceMove

App web de finanças pessoais em pt-BR: contas, transações (com cartão de crédito, fatura,
parcelamento e recorrência), orçamento por categoria e dashboard. Multi-usuário desde o dia 1.

Construído como **monólito modular** porque um dia vira o módulo financeiro do hub *AppLife*.

| | |
|---|---|
| **Backend** | .NET 10 · ASP.NET Core · EF Core · PostgreSQL 17 |
| **Frontend** | React + Vite (SPA separada) |
| **Auth** | ASP.NET Identity com bearer token + refresh rotacionado |
| **Hospedagem** | Railway (API + banco) · Cloudflare Pages (SPA) |

> **Status:** MVP funcional de ponta a ponta. Cadastro, login, contas, transações (com
> transferência, parcelamento e recorrência), faturas de cartão, orçamento e dashboard.
> Metas e Investimentos são v2 e ainda não foram implementados.

## Rodando do zero numa máquina limpa

### 1. Pré-requisitos

| Ferramenta | Versão | Para quê |
|---|---|---|
| [.NET SDK](https://dotnet.microsoft.com/download) | 10.0+ | compilar e rodar a API |
| [Docker Desktop](https://www.docker.com/products/docker-desktop/) | 24+ | Postgres local e testes de integração |
| [Git](https://git-scm.com/) | 2.40+ | - |

Confira:

```bash
dotnet --version # 10.0.x
docker --version # 24+ (e o Docker Desktop precisa estar ABERTO)
```

### 2. Clonar e restaurar

```bash
git clone https://github.com/DevMoraesss/financeapp.git
cd financeapp
dotnet restore FinanceMove.sln
dotnet tool restore # instala o dotnet-ef na versão fixada no repo
```

### 3. Subir o banco

```bash
docker compose up -d
docker compose ps # deve mostrar "Up (healthy)"
```

O Postgres sobe na porta **5435** do host (5432, 5433 e 5434 costumam estar ocupadas por outros
projetos). Se der conflito de porta:

```bash
FINANCEMOVE_DB_PORT=5436 docker compose up -d
# e ajuste a porta em src/Api/appsettings.Development.json
```

### 4. Criar as tabelas

A ordem importa. `Identity` vem primeiro, porque as outras tabelas têm chave estrangeira para
`identity.app_user`:

```bash
dotnet ef database update --project src/Modules/Identity/Identity         --startup-project src/Api --context IdentityModuleDbContext
dotnet ef database update --project src/Modules/Accounts/Accounts         --startup-project src/Api --context AccountsDbContext
dotnet ef database update --project src/Modules/Transactions/Transactions --startup-project src/Api --context TransactionsDbContext
dotnet ef database update --project src/Modules/Budget/Budget             --startup-project src/Api --context BudgetDbContext
```

### 5. Rodar a API

```bash
dotnet run --project src/Api --urls http://localhost:5080
```

Em outro terminal:

```bash
curl http://localhost:5080/health
# {"status":"Healthy","version":"1.0.0","durationMs":1}

curl http://localhost:5080/health/ready
# {"status":"Healthy",...,"checks":[{"name":"postgres","status":"Healthy",...}]}
```

### 6. Rodar o frontend

Em outro terminal, com a API de pé:

```bash
cd web
npm install
npm run dev # http://localhost:5173
```

O Vite encaminha `/api/*` para `http://localhost:5080`, então em desenvolvimento o navegador vê
uma origem só e o CORS não atrapalha. Em produção são domínios diferentes de verdade - aí vale o
CORS de origem única do `Program.cs`.

Abra `http://localhost:5173`, crie sua conta e comece a lançar. O banco começa **vazio**: o único
dado que o cadastro cria são as 16 categorias padrão, que você pode renomear e arquivar.

Para conferir a API inteira sem abrir o navegador, `pwsh tools/smoke.ps1` percorre os 18 passos do
caminho principal, do cadastro à exclusão de conta, conferindo os valores de cada etapa. Ele usa um
e-mail aleatório e apaga o próprio usuário no fim, então não encosta nos seus dados.

### 7. Rodar os testes

```bash
dotnet test FinanceMove.sln
```

Os testes de integração sobem um Postgres próprio via Testcontainers - **o Docker precisa estar
rodando**. Para os testes rápidos, sem Docker:

```bash
dotnet test tests/UnitTests
```

E no frontend:

```bash
cd web
npm run build # typecheck (tsc) + build de produção
npm run lint
```

## Estrutura do repositório

```
financeapp/
+-- SPEC.md o que o produto faz (escopo, histórias, critérios de aceite)
+-- CLAUDE.md guia de trabalho: comandos, convenções, armadilhas
+-- docs/
| +-- ADR-001-arquitetura.md decisões técnicas e o que foi rejeitado
| +-- arquitetura.md componentes e fronteiras dos módulos
| +-- modelo-de-dados.md tabelas, índices e as justificativas técnicas
| +-- api.md contrato dos endpoints
| \-- fluxos-usuario.md jornadas camada a camada
+-- docker-compose.yml       Postgres de desenvolvimento
+-- FinanceMove.sln
+-- tools/                  scripts de apoio (smoke, limpeza de caracteres)
+-- src/
|   +-- Api/                host HTTP: endpoints, auth, CORS, healthchecks
|   +-- Shared/             IClock, ICurrentUser, Money, eventos de domínio
|   \-- Modules/
|       +-- Identity/       usuários, senha, tokens
|       +-- Accounts/       contas bancárias e cartões
|       +-- Transactions/   categorias, lançamentos, faturas, parcelas, recorrência
|       \-- Budget/         limite mensal por categoria
+-- tests/
|   +-- UnitTests/
|   \-- IntegrationTests/
\-- web/                    SPA React (Vite + Tailwind)
    \-- src/
        +-- lib/            cliente da API, formatação pt-BR, contextos
        +-- components/     layout, componentes de UI e formulários
        \-- pages/          Login, Dashboard, Transações, Faturas, Orçamento, Configurações
```

Cada módulo é um par de projetos: `X.Contracts` (o que os outros módulos enxergam) e `X`
(implementação, `internal`). Detalhes em [docs/arquitetura.md](docs/arquitetura.md).

## Roadmap

| Fase | Entrega | Status |
|---|---|---|
| Fundação | estrutura modular, healthcheck, banco, migrations, testes, SPA | pronto |
| Auth e identidade | registro, login, refresh rotacionado, exportar e excluir conta | pronto |
| Contas e categorias | CRUD, arquivamento, ajuste de saldo, seed de 16 categorias | pronto |
| Transações | CRUD, filtros, busca, transferência, parcelamento, recorrência | pronto |
| Faturas de cartão | ciclo de fechamento, compras por fatura, pagamento | pronto |
| Orçamento | limite por categoria com faixas de 80% e 100% | pronto |
| Dashboard | saldos, resumo do mês, donut, pendências | pronto |
| Metas e Investimentos | v2, desenhados em `docs/` e ainda não implementados | pendente |
| Importação CSV/OFX | v2 | pendente |
| CI/CD e deploy no Railway | GitHub Actions, imagem Docker, migrations no deploy | pendente |

## Licença

Projeto pessoal de estudo. Sem licença definida.
