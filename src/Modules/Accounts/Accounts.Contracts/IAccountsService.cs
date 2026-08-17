namespace FinanceMove.Modules.Accounts.Contracts;

public sealed record CreateAccountRequest(
    string Name,
    AccountType Type,
    decimal InitialBalance,
    short? ClosingDay,
    short? DueDay);

public sealed record UpdateAccountRequest(string Name, short? ClosingDay, short? DueDay);

/// <summary>
/// Operacoes de escrita sobre contas. A leitura de saldo NAO esta aqui: quem calcula saldo e o
/// modulo Transactions, dono dos lancamentos (docs/arquitetura.md secao 3).
/// </summary>
public interface IAccountsService
{
    Task<AccountSummary> CreateAsync(Guid userId, CreateAccountRequest request, CancellationToken cancellationToken = default);

    /// <summary>Devolve nulo se a conta nao existir ou nao for do usuario (vira 404).</summary>
    Task<AccountSummary?> UpdateAsync(
        Guid userId,
        Guid accountId,
        UpdateAccountRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> ArchiveAsync(Guid userId, Guid accountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exclui de verdade. So permitido enquanto a conta nunca teve transacao; caso contrario o
    /// chamador deve arquivar (modelo-de-dados secao 5.3).
    /// </summary>
    Task<bool> DeleteAsync(Guid userId, Guid accountId, CancellationToken cancellationToken = default);
}
