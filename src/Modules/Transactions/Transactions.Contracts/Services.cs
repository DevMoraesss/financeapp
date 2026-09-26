namespace FinanceMove.Modules.Transactions.Contracts;

/// <summary>
/// Leituras que OUTROS modulos fazem sobre transacoes. E a porta pela qual Budget e o Dashboard
/// obtem numeros sem nunca tocar nas tabelas deste modulo (ADR-001, Decisao 2).
/// </summary>
public interface ITransactionsQuery
{
    /// <summary>
    /// Saldo de todas as contas do usuario na data informada, ja com o saldo inicial somado. Em
    /// cartao, e tudo que ainda nao foi pago, inclusive parcelas futuras (fica negativo).
    /// </summary>
    Task<IReadOnlyList<AccountBalanceDto>> GetBalancesAsync(
        Guid userId,
        DateOnly asOf,
        CancellationToken cancellationToken = default);

    Task<decimal> GetBalanceAsync(
        Guid userId,
        Guid accountId,
        DateOnly asOf,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Receitas, despesas e saldo do mes. Despesa de cartao conta no mes da fatura; transferencias
    /// ficam de fora (SPEC D6 e secao 5.3).
    /// </summary>
    Task<MonthSummaryDto> GetMonthSummaryAsync(
        Guid userId,
        string month,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Soma das despesas confirmadas por categoria no mes. E a base do donut do dashboard e do
    /// progresso do orcamento.
    /// </summary>
    Task<IReadOnlyList<CategoryTotalDto>> SumExpensesByCategoryAsync(
        Guid userId,
        string month,
        CancellationToken cancellationToken = default);

    /// <summary>Usado para decidir se uma conta pode ser excluida ou apenas arquivada.</summary>
    Task<bool> AccountHasTransactionsAsync(
        Guid userId,
        Guid accountId,
        CancellationToken cancellationToken = default);

    /// <summary>Ultimos lancamentos confirmados com data ate <paramref name="asOf"/> (sem parcela futura).</summary>
    Task<IReadOnlyList<TransactionDto>> GetRecentAsync(
        Guid userId,
        int count,
        DateOnly asOf,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TransactionDto>> GetPendingAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}

public interface ICategoryService
{
    Task<IReadOnlyList<CategoryDto>> ListAsync(
        Guid userId,
        CategoryKind? type = null,
        bool includeArchived = false,
        CancellationToken cancellationToken = default);

    Task<CategoryDto> CreateAsync(Guid userId, CreateCategoryRequest request, CancellationToken cancellationToken = default);

    Task<CategoryDto?> UpdateAsync(
        Guid userId,
        Guid categoryId,
        UpdateCategoryRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> ArchiveAsync(Guid userId, Guid categoryId, CancellationToken cancellationToken = default);
}

public interface ITransactionService
{
    Task<TransactionListDto> ListAsync(Guid userId, TransactionFilter filter, CancellationToken cancellationToken = default);

    Task<TransactionDto?> FindAsync(Guid userId, Guid transactionId, CancellationToken cancellationToken = default);

    Task<TransactionWriteResultDto> CreateAsync(
        Guid userId,
        CreateTransactionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cria as N parcelas de uma compra no cartao de uma vez, com o rateio que garante que a soma
    /// bata com o total ao centavo (modelo-de-dados secao 4.4).
    /// </summary>
    Task<InstallmentsResultDto> CreateInstallmentsAsync(
        Guid userId,
        CreateInstallmentsRequest request,
        CancellationToken cancellationToken = default);

    Task<TransactionWriteResultDto?> UpdateAsync(
        Guid userId,
        Guid transactionId,
        UpdateTransactionRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid userId, Guid transactionId, CancellationToken cancellationToken = default);

    /// <summary>Confirma uma pendencia de recorrencia, permitindo ajustar o valor (conta de luz varia).</summary>
    Task<TransactionWriteResultDto?> ConfirmAsync(
        Guid userId,
        Guid transactionId,
        decimal? amount,
        CancellationToken cancellationToken = default);

    Task<bool> DiscardAsync(Guid userId, Guid transactionId, CancellationToken cancellationToken = default);

    /// <summary>Remove as parcelas FUTURAS de um grupo. As passadas aconteceram e ficam.</summary>
    Task<int> DeleteInstallmentGroupAsync(Guid userId, Guid groupId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ajuste de saldo (SPEC US-11): cria uma transacao na categoria de sistema "Ajuste" com a
    /// diferenca entre o saldo informado e o calculado. Nenhuma transacao antiga e alterada.
    /// </summary>
    Task<TransactionWriteResultDto?> AdjustBalanceAsync(
        Guid userId,
        Guid accountId,
        decimal realBalance,
        CancellationToken cancellationToken = default);
}

public interface IStatementService
{
    Task<IReadOnlyList<StatementSummaryDto>> ListAsync(
        Guid userId,
        Guid cardId,
        int months = 6,
        CancellationToken cancellationToken = default);

    Task<StatementDetailDto?> GetAsync(Guid userId, Guid cardId, string month, CancellationToken cancellationToken = default);

    Task<PayStatementResultDto?> PayAsync(
        Guid userId,
        Guid cardId,
        string month,
        Guid sourceAccountId,
        CancellationToken cancellationToken = default);
}

public interface IRecurrenceService
{
    Task<IReadOnlyList<RecurrenceDto>> ListAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<RecurrenceDto> CreateAsync(Guid userId, CreateRecurrenceRequest request, CancellationToken cancellationToken = default);

    Task<bool> DeactivateAsync(Guid userId, Guid recurrenceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Materializa as ocorrencias vencidas como transacoes pendentes (modelo-de-dados secao 4.2).
    /// Idempotente: rodar duas vezes no mesmo dia nao duplica nada.
    /// </summary>
    Task<int> RunCatchUpAsync(Guid userId, CancellationToken cancellationToken = default);
}
