using FinanceMove.Modules.Accounts.Contracts;
using FinanceMove.Modules.Transactions.Contracts;
using FinanceMove.Shared;
using Microsoft.EntityFrameworkCore;

namespace FinanceMove.Modules.Transactions;

/// <summary>
/// Regras de lancamento recorrente e o catch-up que as materializa (modelo-de-dados secao 4.2).
/// </summary>
internal sealed class RecurrenceService(
    TransactionsDbContext context,
    IAccountsQuery accountsQuery,
    IClock clock) : IRecurrenceService
{
    /// <summary>
    /// Teto de ocorrencias geradas numa unica passada. Protege contra uma regra antiga com data
    /// de inicio muito no passado gerar centenas de lancamentos de uma vez.
    /// </summary>
    private const int MaxOccurrencesPerRun = 24;

    public async Task<IReadOnlyList<RecurrenceDto>> ListAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var rules = await context.RecurrenceRules
            .AsNoTracking()
            .Include(rule => rule.Category)
            .Where(rule => rule.UserId == userId)
            .OrderByDescending(rule => rule.Active)
            .ThenBy(rule => rule.NextRunOn)
            .ToListAsync(cancellationToken);

        var accounts = (await accountsQuery.ListAsync(userId, includeArchived: true, cancellationToken))
            .ToDictionary(account => account.Id);

        return rules.Select(rule => new RecurrenceDto(
            rule.Id,
            rule.Description,
            rule.Amount,
            rule.Type,
            rule.Frequency,
            rule.ReferenceDay,
            rule.ReferenceMonth,
            rule.StartsOn,
            rule.EndsOn,
            rule.NextRunOn,
            rule.Active,
            accounts.TryGetValue(rule.AccountId, out var account)
                ? new AccountRefDto(account.Id, account.Name)
                : new AccountRefDto(rule.AccountId, "Conta removida"),
            rule.Category is null
                ? new CategoryRefDto(rule.CategoryId, "Categoria removida", "#8b979f", "circle-dashed")
                : Mapping.ToRef(rule.Category))).ToList();
    }

    public async Task<RecurrenceDto> CreateAsync(
        Guid userId,
        CreateRecurrenceRequest request,
        CancellationToken cancellationToken = default)
    {
        var account = await accountsQuery.FindAsync(userId, request.AccountId, cancellationToken)
            ?? throw DomainException.Unprocessable("Conta nao encontrada.", "account-not-found");

        if (account.Archived)
        {
            throw DomainException.Unprocessable(
                $"A conta \"{account.Name}\" esta arquivada.",
                "archived-account");
        }

        var category = await context.Categories.FirstOrDefaultAsync(
            candidate => candidate.UserId == userId && candidate.Id == request.CategoryId,
            cancellationToken) ?? throw DomainException.Unprocessable("Categoria nao encontrada.", "category-not-found");

        if (category.Type != request.Type)
        {
            throw DomainException.Unprocessable(
                $"A categoria \"{category.Name}\" nao combina com o tipo da recorrencia.",
                "category-kind-mismatch");
        }

        ValidateSchedule(request);

        var rule = new RecurrenceRule
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Description = request.Description.Trim(),
            Amount = Money.Round(request.Amount),
            Type = request.Type,
            CategoryId = request.CategoryId,
            AccountId = request.AccountId,
            Frequency = request.Frequency,
            ReferenceDay = (short)request.ReferenceDay,
            ReferenceMonth = request.ReferenceMonth is { } month ? (short)month : null,
            StartsOn = request.StartsOn,
            EndsOn = request.EndsOn,
            Active = true,
            CreatedAt = clock.UtcNow,
            UpdatedAt = clock.UtcNow,
        };

        if (rule.Amount <= 0m)
        {
            throw DomainException.Unprocessable("O valor deve ser maior que zero.", "invalid-amount");
        }

        // Criar a regra NAO cria transacao nenhuma. So aponta quando a primeira deve nascer.
        rule.NextRunOn = rule.FirstOccurrence();

        context.RecurrenceRules.Add(rule);
        await context.SaveChangesAsync(cancellationToken);

        rule.Category = category;

        return new RecurrenceDto(
            rule.Id,
            rule.Description,
            rule.Amount,
            rule.Type,
            rule.Frequency,
            rule.ReferenceDay,
            rule.ReferenceMonth,
            rule.StartsOn,
            rule.EndsOn,
            rule.NextRunOn,
            rule.Active,
            new AccountRefDto(account.Id, account.Name),
            Mapping.ToRef(category));
    }

    public async Task<bool> DeactivateAsync(Guid userId, Guid recurrenceId, CancellationToken cancellationToken = default)
    {
        // Desativa em vez de excluir: as transacoes ja geradas continuam validas e rastreaveis.
        var affected = await context.RecurrenceRules
            .Where(rule => rule.UserId == userId && rule.Id == recurrenceId)
            .ExecuteUpdateAsync(
                update => update
                    .SetProperty(rule => rule.Active, false)
                    .SetProperty(rule => rule.UpdatedAt, clock.UtcNow),
                cancellationToken);

        return affected > 0;
    }

    public async Task<int> RunCatchUpAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var today = clock.Today;

        await using var databaseTransaction = await context.Database.BeginTransactionAsync(cancellationToken);

        // FOR UPDATE SKIP LOCKED: se o usuario abrir duas abas ao mesmo tempo, ou o job diario
        // disparar junto, apenas uma das execucoes pega cada regra. Sem isso, o mesmo aluguel
        // seria lancado duas vezes.
        var due = await context.RecurrenceRules
            .FromSql($"""
                SELECT * FROM transactions.recurrence_rule
                 WHERE user_id = {userId}
                   AND active
                   AND next_run_on <= {today}
                 FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);

        if (due.Count == 0)
        {
            await databaseTransaction.CommitAsync(cancellationToken);
            return 0;
        }

        var created = 0;

        foreach (var rule in due)
        {
            var occurrences = 0;

            while (rule.NextRunOn <= today && occurrences < MaxOccurrencesPerRun)
            {
                if (rule.EndsOn is { } endsOn && rule.NextRunOn > endsOn)
                {
                    rule.Active = false;
                    break;
                }

                context.Transactions.Add(new Transaction
                {
                    Id = Guid.CreateVersion7(),
                    UserId = userId,
                    Type = rule.Type == CategoryKind.Income ? TransactionType.Income : TransactionType.Expense,
                    Amount = rule.Amount,
                    Date = rule.NextRunOn,
                    Description = rule.Description,

                    // Nasce PENDENTE: nao entra no saldo nem no orcamento ate o usuario confirmar,
                    // porque o valor real pode ser outro (conta de luz varia).
                    Status = TransactionStatus.Pending,
                    AccountId = rule.AccountId,
                    CategoryId = rule.CategoryId,
                    RecurrenceRuleId = rule.Id,
                    CreatedAt = clock.UtcNow,
                    UpdatedAt = clock.UtcNow,
                });

                // O ponteiro avanca na MESMA transacao de banco do insert. E exatamente isso que
                // torna o catch-up idempotente (SPEC secao 10, passo 8).
                rule.NextRunOn = rule.OccurrenceAfter(rule.NextRunOn);
                rule.UpdatedAt = clock.UtcNow;

                created++;
                occurrences++;
            }
        }

        await context.SaveChangesAsync(cancellationToken);
        await databaseTransaction.CommitAsync(cancellationToken);

        return created;
    }

    private static void ValidateSchedule(CreateRecurrenceRequest request)
    {
        var valid = request.Frequency switch
        {
            RecurrenceFrequency.Weekly => request.ReferenceDay is >= 1 and <= 7 && request.ReferenceMonth is null,
            RecurrenceFrequency.Monthly => request.ReferenceDay is >= 1 and <= 28 && request.ReferenceMonth is null,
            RecurrenceFrequency.Yearly => request.ReferenceDay is >= 1 and <= 28
                && request.ReferenceMonth is >= 1 and <= 12,
            _ => false,
        };

        if (!valid)
        {
            throw DomainException.Unprocessable(
                "Periodicidade invalida. Semanal usa dia 1 a 7; mensal, dia 1 a 28; anual, dia 1 a 28 mais o mes.",
                "invalid-schedule");
        }

        if (request.EndsOn is { } endsOn && endsOn < request.StartsOn)
        {
            throw DomainException.Unprocessable("A data de fim nao pode ser antes do inicio.", "invalid-period");
        }
    }
}
