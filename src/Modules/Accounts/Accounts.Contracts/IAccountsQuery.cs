namespace FinanceMove.Modules.Accounts.Contracts;

/// <summary>
/// Contrato público do módulo Contas - como Transactions, Budget e Investments perguntam sobre
/// contas sem nunca ler a tabela <c>accounts.account</c> (ADR-001, Decisão 2).
/// </summary>
/// <remarks>
/// Todos os métodos recebem <c>userId</c> explicitamente: é a assinatura que torna difícil
/// esquecer o filtro de tenant. Conta de outro usuário simplesmente "não existe" (retorna null).
/// </remarks>
public interface IAccountsQuery
{
    Task<AccountSummary?> FindAsync(Guid userId, Guid accountId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AccountSummary>> ListAsync(
        Guid userId,
        bool includeArchived = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Valida, em uma ida ao banco, que a conta existe, é do usuário e não está arquivada -
    /// a checagem que todo lançamento de transação precisa fazer.
    /// </summary>
    Task<bool> IsUsableAsync(Guid userId, Guid accountId, CancellationToken cancellationToken = default);
}
