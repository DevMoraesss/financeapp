using FinanceMove.Modules.Accounts.Contracts;
using FinanceMove.Modules.Transactions.Contracts;
using FinanceMove.Shared;
using Microsoft.EntityFrameworkCore;

namespace FinanceMove.Modules.Transactions;

internal sealed class TransactionService(
    TransactionsDbContext context,
    IAccountsQuery accountsQuery,
    ITransactionsQuery transactionsQuery,
    IClock clock) : ITransactionService
{
    private const int MaxInstallments = 48;

    public async Task<TransactionListDto> ListAsync(
        Guid userId,
        TransactionFilter filter,
        CancellationToken cancellationToken = default)
    {
        var query = context.Transactions
            .AsNoTracking()
            .Include(transaction => transaction.Category)
            .Where(transaction => transaction.UserId == userId);

        if (!string.IsNullOrWhiteSpace(filter.Month))
        {
            // Mesma regra do resumo do mes: compra no cartao aparece no mes da fatura.
            query = query.Where(MonthRange.ReferenceFilter(filter.Month));
        }

        if (filter.AccountId is { } accountId)
        {
            query = query.Where(transaction =>
                transaction.AccountId == accountId || transaction.DestinationAccountId == accountId);
        }

        if (filter.CategoryId is { } categoryId)
        {
            query = query.Where(transaction => transaction.CategoryId == categoryId);
        }

        if (filter.Type is { } type)
        {
            query = query.Where(transaction => transaction.Type == type);
        }

        if (filter.Status is { } status)
        {
            query = query.Where(transaction => transaction.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(transaction => EF.Functions.ILike(transaction.Description, $"%{term}%"));
        }

        var total = await query.CountAsync(cancellationToken);

        var size = Math.Clamp(filter.Size, 1, 200);
        var page = Math.Max(filter.Page, 1);

        // O desempate final pelo id deixa a ordem ESTAVEL entre paginas: as parcelas de uma compra
        // nascem com o mesmo CreatedAt, e sem isso o Postgres poderia repetir uma na pagina 2 e
        // pular outra (a exportacao, que pagina ate o fim, perderia lancamentos).
        var items = await query
            .OrderByDescending(transaction => transaction.Date)
            .ThenByDescending(transaction => transaction.CreatedAt)
            .ThenByDescending(transaction => transaction.Id)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);

        var accounts = await LoadAccountsAsync(userId, cancellationToken);

        var summary = string.IsNullOrWhiteSpace(filter.Month)
            ? new MonthSummaryDto(0m, 0m, 0m)
            : await transactionsQuery.GetMonthSummaryAsync(userId, filter.Month, cancellationToken);

        return new TransactionListDto(
            items.Select(transaction => Mapping.ToDto(transaction, accounts)).ToList(),
            new PaginationDto(page, size, total, (total + size - 1) / size),
            summary);
    }

    public async Task<TransactionDto?> FindAsync(
        Guid userId,
        Guid transactionId,
        CancellationToken cancellationToken = default)
    {
        var transaction = await context.Transactions
            .AsNoTracking()
            .Include(candidate => candidate.Category)
            .FirstOrDefaultAsync(candidate => candidate.UserId == userId && candidate.Id == transactionId, cancellationToken);

        if (transaction is null)
        {
            return null;
        }

        var accounts = await LoadAccountsAsync(userId, cancellationToken);
        return Mapping.ToDto(transaction, accounts);
    }

    public async Task<TransactionWriteResultDto> CreateAsync(
        Guid userId,
        CreateTransactionRequest request,
        CancellationToken cancellationToken = default)
    {
        var account = await RequireUsableAccountAsync(userId, request.AccountId, cancellationToken);
        AccountSummary? destination = null;
        Category? category = null;

        if (request.Type == TransactionType.Transfer)
        {
            if (request.DestinationAccountId is not { } destinationId)
            {
                throw DomainException.Unprocessable("Transferencia precisa de uma conta de destino.", "missing-destination");
            }

            if (destinationId == request.AccountId)
            {
                throw DomainException.Unprocessable("A conta de destino precisa ser diferente da origem.", "same-account");
            }

            destination = await RequireUsableAccountAsync(userId, destinationId, cancellationToken);

            if (request.CategoryId is not null)
            {
                throw DomainException.Unprocessable("Transferencia nao leva categoria.", "transfer-with-category");
            }
        }
        else
        {
            if (request.CategoryId is not { } categoryId)
            {
                throw DomainException.Unprocessable("Receita e despesa precisam de categoria.", "missing-category");
            }

            category = await RequireCategoryAsync(userId, categoryId, ExpectedKind(request.Type), cancellationToken);
        }

        var transaction = new Transaction
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Type = request.Type,
            Amount = RequirePositive(request.Amount),
            Date = request.Date,
            Description = RequireDescription(request.Description),
            Status = TransactionStatus.Confirmed,
            AccountId = request.AccountId,
            DestinationAccountId = request.DestinationAccountId,
            CategoryId = request.CategoryId,
            StatementMonth = StatementMonthFor(account, request.Type, request.Date),
            CreatedAt = clock.UtcNow,
            UpdatedAt = clock.UtcNow,
        };

        context.Transactions.Add(transaction);
        await context.SaveChangesAsync(cancellationToken);

        transaction.Category = category;
        return await BuildWriteResultAsync(userId, transaction, cancellationToken);
    }

    public async Task<InstallmentsResultDto> CreateInstallmentsAsync(
        Guid userId,
        CreateInstallmentsRequest request,
        CancellationToken cancellationToken = default)
    {
        var card = await RequireUsableAccountAsync(userId, request.CardId, cancellationToken);

        if (card.Type != AccountType.CreditCard)
        {
            throw DomainException.Unprocessable(
                "Parcelamento so existe em cartao de credito na v1.",
                "installments-require-card");
        }

        if (request.Installments is < 2 or > MaxInstallments)
        {
            throw DomainException.Unprocessable(
                $"O numero de parcelas precisa estar entre 2 e {MaxInstallments}.",
                "invalid-installments");
        }

        var category = await RequireCategoryAsync(userId, request.CategoryId, CategoryKind.Expense, cancellationToken);
        var total = RequirePositive(request.TotalAmount);
        var description = RequireDescription(request.Description);

        var group = new InstallmentGroup
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Description = description,
            TotalAmount = total,
            Installments = (short)request.Installments,
            CardId = request.CardId,
            PurchaseDate = request.Date,
            CreatedAt = clock.UtcNow,
        };

        context.InstallmentGroups.Add(group);

        // O rateio garante que a soma das parcelas bata com o total ao centavo. Sem isso,
        // R$ 100,00 em 3x viraria R$ 99,99 (modelo-de-dados secao 4.4).
        var amounts = Money.Split(total, request.Installments);
        var created = new List<Transaction>(request.Installments);

        for (var index = 0; index < amounts.Length; index++)
        {
            var date = AddMonthsClamped(request.Date, index);

            var installment = new Transaction
            {
                Id = Guid.CreateVersion7(),
                UserId = userId,
                Type = TransactionType.Expense,
                Amount = amounts[index],
                Date = date,
                Description = $"{description} {index + 1}/{request.Installments}",
                Status = TransactionStatus.Confirmed,
                AccountId = request.CardId,
                CategoryId = request.CategoryId,
                InstallmentGroupId = group.Id,
                InstallmentNumber = (short)(index + 1),
                InstallmentTotal = (short)request.Installments,
                StatementMonth = StatementMonthFor(card, TransactionType.Expense, date),
                CreatedAt = clock.UtcNow,
                UpdatedAt = clock.UtcNow,
            };

            installment.Category = category;
            created.Add(installment);
        }

        context.Transactions.AddRange(created);
        await context.SaveChangesAsync(cancellationToken);

        var accounts = await LoadAccountsAsync(userId, cancellationToken);

        return new InstallmentsResultDto(
            group.Id,
            created.Select(transaction => Mapping.ToDto(transaction, accounts)).ToList());
    }

    public async Task<TransactionWriteResultDto?> UpdateAsync(
        Guid userId,
        Guid transactionId,
        UpdateTransactionRequest request,
        CancellationToken cancellationToken = default)
    {
        var transaction = await context.Transactions
            .Include(candidate => candidate.Category)
            .FirstOrDefaultAsync(candidate => candidate.UserId == userId && candidate.Id == transactionId, cancellationToken);

        if (transaction is null)
        {
            return null;
        }

        var account = await RequireUsableAccountAsync(userId, request.AccountId, cancellationToken);

        if (transaction.Type == TransactionType.Transfer)
        {
            if (request.DestinationAccountId is not { } destinationId || destinationId == request.AccountId)
            {
                throw DomainException.Unprocessable(
                    "Transferencia precisa de uma conta de destino diferente da origem.",
                    "invalid-transfer");
            }

            await RequireUsableAccountAsync(userId, destinationId, cancellationToken);
            transaction.DestinationAccountId = destinationId;
            transaction.CategoryId = null;
            transaction.Category = null;
        }
        else
        {
            if (request.CategoryId is not { } categoryId)
            {
                throw DomainException.Unprocessable("Receita e despesa precisam de categoria.", "missing-category");
            }

            transaction.Category = await RequireCategoryAsync(
                userId,
                categoryId,
                ExpectedKind(transaction.Type),
                cancellationToken);

            transaction.CategoryId = categoryId;
        }

        transaction.Amount = RequirePositive(request.Amount);
        transaction.Date = request.Date;
        transaction.Description = RequireDescription(request.Description);
        transaction.AccountId = request.AccountId;
        transaction.StatementMonth = StatementMonthFor(account, transaction.Type, request.Date);
        transaction.UpdatedAt = clock.UtcNow;

        await context.SaveChangesAsync(cancellationToken);
        return await BuildWriteResultAsync(userId, transaction, cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid userId, Guid transactionId, CancellationToken cancellationToken = default)
    {
        var affected = await context.Transactions
            .Where(transaction => transaction.UserId == userId && transaction.Id == transactionId)
            .ExecuteDeleteAsync(cancellationToken);

        return affected > 0;
    }

    public async Task<TransactionWriteResultDto?> ConfirmAsync(
        Guid userId,
        Guid transactionId,
        decimal? amount,
        CancellationToken cancellationToken = default)
    {
        var transaction = await context.Transactions
            .Include(candidate => candidate.Category)
            .FirstOrDefaultAsync(candidate => candidate.UserId == userId && candidate.Id == transactionId, cancellationToken);

        if (transaction is null)
        {
            return null;
        }

        if (transaction.Status != TransactionStatus.Pending)
        {
            throw DomainException.Conflict("Esta transacao ja esta confirmada.", "already-confirmed");
        }

        if (amount is { } newAmount)
        {
            // Permitir ajustar o valor na confirmacao existe porque conta de luz varia todo mes.
            transaction.Amount = RequirePositive(newAmount);
        }

        transaction.Status = TransactionStatus.Confirmed;
        transaction.UpdatedAt = clock.UtcNow;

        await context.SaveChangesAsync(cancellationToken);
        return await BuildWriteResultAsync(userId, transaction, cancellationToken);
    }

    public async Task<bool> DiscardAsync(Guid userId, Guid transactionId, CancellationToken cancellationToken = default)
    {
        var affected = await context.Transactions
            .Where(transaction => transaction.UserId == userId
                && transaction.Id == transactionId
                && transaction.Status == TransactionStatus.Pending)
            .ExecuteDeleteAsync(cancellationToken);

        return affected > 0;
    }

    public async Task<int> DeleteInstallmentGroupAsync(
        Guid userId,
        Guid groupId,
        CancellationToken cancellationToken = default)
    {
        var today = clock.Today;

        // So as parcelas futuras somem. As passadas aconteceram de verdade e ficam no historico
        // (modelo-de-dados secao 5.3).
        var removed = await context.Transactions
            .Where(transaction => transaction.UserId == userId
                && transaction.InstallmentGroupId == groupId
                && transaction.Date > today)
            .ExecuteDeleteAsync(cancellationToken);

        var remaining = await context.Transactions
            .AnyAsync(
                transaction => transaction.UserId == userId && transaction.InstallmentGroupId == groupId,
                cancellationToken);

        if (!remaining)
        {
            await context.InstallmentGroups
                .Where(group => group.UserId == userId && group.Id == groupId)
                .ExecuteDeleteAsync(cancellationToken);
        }

        return removed;
    }

    public async Task<TransactionWriteResultDto?> AdjustBalanceAsync(
        Guid userId,
        Guid accountId,
        decimal realBalance,
        CancellationToken cancellationToken = default)
    {
        var account = await accountsQuery.FindAsync(userId, accountId, cancellationToken);

        if (account is null)
        {
            return null;
        }

        var today = clock.Today;
        var current = await transactionsQuery.GetBalanceAsync(userId, accountId, today, cancellationToken);
        var difference = Money.Round(realBalance - current);

        if (difference == 0m)
        {
            throw DomainException.Conflict("O saldo informado ja e o saldo atual da conta.", "no-adjustment-needed");
        }

        // Diferenca positiva significa que faltava dinheiro no app: entra como receita.
        var kind = difference > 0 ? CategoryKind.Income : CategoryKind.Expense;

        var category = await context.Categories.FirstOrDefaultAsync(
            candidate => candidate.UserId == userId
                && candidate.System
                && candidate.Name == CategoryService.AdjustmentName
                && candidate.Type == kind,
            cancellationToken);

        if (category is null)
        {
            throw DomainException.Unprocessable(
                "A categoria de sistema \"Ajuste\" nao foi encontrada nesta conta de usuario.",
                "missing-adjustment-category");
        }

        var transaction = new Transaction
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Type = difference > 0 ? TransactionType.Income : TransactionType.Expense,
            Amount = Math.Abs(difference),
            Date = today,
            Description = "Ajuste de saldo",
            Status = TransactionStatus.Confirmed,
            AccountId = accountId,
            CategoryId = category.Id,
            Category = category,
            CreatedAt = clock.UtcNow,
            UpdatedAt = clock.UtcNow,
        };

        context.Transactions.Add(transaction);
        await context.SaveChangesAsync(cancellationToken);

        return await BuildWriteResultAsync(userId, transaction, cancellationToken);
    }

    // -----------------------------------------------------------------------------------------
    // Apoio
    // -----------------------------------------------------------------------------------------

    private static CategoryKind ExpectedKind(TransactionType type) =>
        type == TransactionType.Income ? CategoryKind.Income : CategoryKind.Expense;

    private static decimal RequirePositive(decimal amount)
    {
        var rounded = Money.Round(amount);

        if (rounded <= 0m)
        {
            throw DomainException.Unprocessable("O valor deve ser maior que zero.", "invalid-amount");
        }

        return rounded;
    }

    private static string RequireDescription(string? description)
    {
        var trimmed = (description ?? string.Empty).Trim();

        if (trimmed.Length == 0)
        {
            throw DomainException.Unprocessable("A descricao e obrigatoria.", "missing-description");
        }

        return trimmed.Length > 120 ? trimmed[..120] : trimmed;
    }

    /// <summary>
    /// Data da parcela k. Se o dia nao existir no mes de destino (compra em 31/01 gerando parcela
    /// em fevereiro), cai no ultimo dia do mes.
    /// </summary>
    private static DateOnly AddMonthsClamped(DateOnly date, int months)
    {
        var target = date.AddMonths(months);
        var daysInMonth = DateTime.DaysInMonth(target.Year, target.Month);
        return new DateOnly(target.Year, target.Month, Math.Min(date.Day, daysInMonth));
    }

    /// <summary>Despesa em cartao carrega a fatura em que caiu; os demais lancamentos, nao.</summary>
    private static string? StatementMonthFor(AccountSummary account, TransactionType type, DateOnly date)
    {
        if (account.Type != AccountType.CreditCard || type != TransactionType.Expense)
        {
            return null;
        }

        if (account.ClosingDay is not { } closing || account.DueDay is not { } due)
        {
            return null;
        }

        return CardCycle.StatementMonthFor(closing, due, date);
    }

    private async Task<AccountSummary> RequireUsableAccountAsync(
        Guid userId,
        Guid accountId,
        CancellationToken cancellationToken)
    {
        // Consulta pelo contrato do modulo Accounts. Este modulo nunca le a tabela accounts.account.
        var account = await accountsQuery.FindAsync(userId, accountId, cancellationToken);

        if (account is null)
        {
            throw DomainException.Unprocessable("Conta nao encontrada.", "account-not-found");
        }

        if (account.Archived)
        {
            throw DomainException.Unprocessable(
                $"A conta \"{account.Name}\" esta arquivada e nao aceita novos lancamentos.",
                "archived-account");
        }

        return account;
    }

    private async Task<Category> RequireCategoryAsync(
        Guid userId,
        Guid categoryId,
        CategoryKind expected,
        CancellationToken cancellationToken)
    {
        var category = await context.Categories.FirstOrDefaultAsync(
            candidate => candidate.UserId == userId && candidate.Id == categoryId,
            cancellationToken);

        if (category is null)
        {
            throw DomainException.Unprocessable("Categoria nao encontrada.", "category-not-found");
        }

        if (category.Type != expected)
        {
            var expectedLabel = expected == CategoryKind.Income ? "receita" : "despesa";
            throw DomainException.Unprocessable(
                $"A categoria \"{category.Name}\" nao pode classificar uma {expectedLabel}.",
                "category-kind-mismatch");
        }

        return category;
    }

    private async Task<IReadOnlyDictionary<Guid, AccountSummary>> LoadAccountsAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var accounts = await accountsQuery.ListAsync(userId, includeArchived: true, cancellationToken);
        return accounts.ToDictionary(account => account.Id);
    }

    private async Task<TransactionWriteResultDto> BuildWriteResultAsync(
        Guid userId,
        Transaction transaction,
        CancellationToken cancellationToken)
    {
        var accounts = await LoadAccountsAsync(userId, cancellationToken);
        var today = clock.Today;

        // A resposta ja traz o saldo recalculado: assim a UI nao precisa somar nada, o que
        // manteria a aritmetica de dinheiro fora do JavaScript (ADR-001, Decisao 3).
        var accountBalance = await transactionsQuery.GetBalanceAsync(userId, transaction.AccountId, today, cancellationToken);

        decimal? destinationBalance = transaction.DestinationAccountId is { } destinationId
            ? await transactionsQuery.GetBalanceAsync(userId, destinationId, today, cancellationToken)
            : null;

        return new TransactionWriteResultDto(
            Mapping.ToDto(transaction, accounts),
            accountBalance,
            destinationBalance);
    }
}
