using FinanceMove.Modules.Accounts.Contracts;
using FinanceMove.Modules.Transactions.Contracts;

namespace FinanceMove.Modules.Transactions;

/// <summary>Conversao das entidades internas para os DTOs publicos do modulo.</summary>
internal static class Mapping
{
    public static CategoryDto ToDto(Category category) => new(
        category.Id,
        category.Name,
        category.Type,
        category.Color,
        category.Icon,
        category.System,
        category.Archived);

    public static CategoryRefDto ToRef(Category category) => new(
        category.Id,
        category.Name,
        category.Color,
        category.Icon);

    public static TransactionDto ToDto(
        Transaction transaction,
        IReadOnlyDictionary<Guid, AccountSummary> accounts)
    {
        var account = Reference(transaction.AccountId, accounts);

        var destination = transaction.DestinationAccountId is { } destinationId
            ? Reference(destinationId, accounts)
            : null;

        var installment = transaction is { InstallmentNumber: { } number, InstallmentTotal: { } total }
            ? new InstallmentRefDto(number, total)
            : null;

        return new TransactionDto(
            transaction.Id,
            transaction.Type,
            transaction.Amount,
            transaction.Date,
            transaction.Description,
            transaction.Status,
            account,
            destination,
            transaction.Category is null ? null : ToRef(transaction.Category),
            installment,
            transaction.RecurrenceRuleId is not null,
            transaction.StatementMonth);
    }

    /// <summary>
    /// Conta arquivada ou removida continua aparecendo no historico. Se por algum motivo o
    /// cadastro sumir, mostramos um rotulo neutro em vez de quebrar a listagem.
    /// </summary>
    private static AccountRefDto Reference(Guid accountId, IReadOnlyDictionary<Guid, AccountSummary> accounts) =>
        accounts.TryGetValue(accountId, out var account)
            ? new AccountRefDto(account.Id, account.Name)
            : new AccountRefDto(accountId, "Conta removida");
}
