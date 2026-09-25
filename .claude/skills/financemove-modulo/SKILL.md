---
name: financemove-modulo
description: Cria um modulo novo no monolito modular do FinanceMove (par X.Contracts + X, DbContext com schema proprio, migration com a FK manual de user_id, registro no host, migrator do deploy, Dockerfile, testes de cascata e isolamento, docs). Use sempre que a tarefa pedir um subsistema com dados proprios - metas, investimentos, patrimonio (imovel, carro), cotacoes, importacao de extrato - ou quando alguem disser "novo modulo", "modulo de X", "comecar a v2", mesmo que nao use a palavra modulo.
---

# Modulo novo no FinanceMove

Cada modulo e destacavel: um dia o hub AppLife vai referenciar so o `.Contracts` dele. Isso so
continua verdade se todo modulo nascer igual. Os passos abaixo sao o molde; pular um costuma
aparecer semanas depois (o deploy que nao restaura, o "excluir minha conta" que deixa lixo).

## 0. Precisa mesmo de um modulo?

Modulo novo quando o dado tem dono e ciclo de vida proprios (Goals, Investments, Assets). Se e so
mais uma visao sobre lancamentos, e uma feature do Transactions: use `financemove-feature`.

Antes de desenhar tabelas, leia o que ja foi decidido: `docs/modelo-de-dados.md` secao 2.1 e 7
(tabelas e formulas da v2), `docs/fluxos-usuario.md` secoes 4 e 5 (metas e aportes) e
`docs/arquitetura.md` secao 3 (fronteiras). Se o pedido divergir do desenho, aponte a divergencia ao
usuario e decida com ele antes de codar.

## 1. Projetos

```bash
dotnet new classlib -n FinanceMove.Modules.X.Contracts -o src/Modules/X/X.Contracts
dotnet new classlib -n FinanceMove.Modules.X -o src/Modules/X/X
dotnet sln FinanceMove.sln add src/Modules/X/X.Contracts src/Modules/X/X
rm src/Modules/X/X*/Class1.cs
```

- Tire dos `.csproj` o `TargetFramework`, `Nullable` e `ImplicitUsings`: vem do
  `Directory.Build.props`, e repetir deixa o projeto para tras quando a raiz mudar.
- `X` referencia `X.Contracts`, `Shared` e **so** `.Contracts` de outros modulos. Pacote:
  `Npgsql.EntityFrameworkCore.PostgreSQL` na mesma versao dos outros modulos (confira no
  `FinanceMove.Modules.Budget.csproj`).
- `X.Contracts` tem so interfaces, DTOs (`record`) e eventos. Nada de EF.
- `src/Api/FinanceMove.Api.csproj` e os dois projetos de teste referenciam os dois.
- **`Dockerfile`:** acrescente as duas linhas `COPY src/Modules/X/.../*.csproj`. Esquecer quebra o
  `dotnet restore` do deploy, e o erro so aparece no Railway.

## 2. Modelo e DbContext

- `public const string Schema = "x";` + `HasDefaultSchema(Schema)`, e `UseSnakeCaseNames()` como
  ULTIMA linha do `OnModelCreating`.
- Toda entidade tem `UserId`; indices e unicos comecam por `user_id`.
- Dinheiro em reais: `numeric(14,2)`. Quantidade e preco unitario de ativo: `numeric(18,8)` (0,085 BTC
  nao cabe em 2 casas). Enum como texto + `HasCheckConstraint`.
- Referencia para entidade de outro modulo (conta, categoria, transacao): `Guid` **sem** FK; valide
  a existencia pelo contrato do outro modulo.
- Numero derivado (valor acumulado da meta, patrimonio total) nao e coluna. Excecao documentada:
  preco medio e quantidade do ativo, recalculados a cada movimento (modelo-de-dados secao 7).

## 3. Registro

- `XModuleExtensions.AddXModule(this IServiceCollection services, string connectionString)`:
  `AddDbContext` com `MigrationsHistoryTable("__ef_migrations_history", XDbContext.Schema)` e os
  servicos. Implementacoes `internal sealed`; so interfaces e DTOs publicos.
- `Program.cs`: `builder.Services.AddXModule(connectionString);` e os `MapXEndpoints()`.
- `src/Api/DatabaseMigrator.cs`: inclua o `XDbContext` na lista, depois do Identity. Sem isso o
  deploy sobe sem as tabelas novas.
- Eventos: ouvinte registrado no proprio modulo (`AddScoped<IEventHandler<Evento>, Handler>()`),
  como o `CategorySeedHandler`.

## 4. Migration inicial

```bash
dotnet ef migrations add InitialX \
  --project src/Modules/X/X --startup-project src/Api \
  --context XDbContext --output-dir Migrations
```

Acrescente A MAO, no fim do `Up`, uma FK por tabela (o EF nao gera, os contextos sao diferentes):

```csharp
migrationBuilder.Sql("""
    ALTER TABLE x.tabela
      ADD CONSTRAINT fk_tabela_user
      FOREIGN KEY (user_id) REFERENCES identity.app_user (id) ON DELETE CASCADE;
    """);
```

E ela que faz o "excluir minha conta" (LGPD) apagar tudo com um DELETE so. Modelo pronto em
`src/Modules/Budget/Budget/Migrations/*_InitialBudget.cs`.

## 5. Escrita que cruza modulos

Cada modulo tem o proprio DbContext e, portanto, a propria conexao. Duas escritas em modulos
diferentes (ex.: aporte = transferencia no Transactions + movimento no Investments) **nao** sao
atomicas por acidente. Decida com o usuario antes: ordenar as escritas para que a falha no meio
deixe um estado aceitavel, compensar a primeira se a segunda falhar, ou compartilhar a transacao
de banco explicitamente. E teste o caminho de falha. Nao escreva em comentario ou doc que algo e
"uma transacao so" se o codigo nao garante (o seed de categorias no cadastro, hoje, nao garante).

## 6. Testes obrigatorios do modulo

- **Cascata:** excluir o usuario apaga as linhas do modulo (modelo: `UserCascadeDeleteTests`).
- **Isolamento:** usuario B recebe 404 nos recursos do usuario A.
- As regras de calculo (preco medio, sugestao mensal da meta) em testes unitarios, com os exemplos
  numericos dos docs (ex.: PETR4 400 x 34,20 -> 41,85 = +22,37%).

## 7. Front e docs

- Rota em `web/src/App.tsx`, item de menu em `components/AppLayout.tsx`, pagina em `pages/`, tipos e
  chamadas em `lib/api.ts` (regras da skill `financemove-feature`).
- Docs no mesmo trabalho: `docs/arquitetura.md` (tabela de fronteiras), `docs/modelo-de-dados.md`
  (tira o "v2, nao migrado"), `docs/api.md`, estrutura no `CLAUDE.md`, escopo na `SPEC.md`.

Feche com a definicao de pronto do `CLAUDE.md` e um resumo para o usuario do que o modulo faz, o que
ficou para depois e como foi verificado.
