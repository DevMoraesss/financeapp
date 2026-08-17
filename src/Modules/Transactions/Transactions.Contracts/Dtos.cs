namespace FinanceMove.Modules.Transactions.Contracts;

public sealed record CategoryDto(
    Guid Id,
    string Name,
    CategoryKind Type,
    string Color,
    string Icon,
    bool System,
    bool Archived);

public sealed record CategoryRefDto(Guid Id, string Name, string Color, string Icon);

public sealed record AccountRefDto(Guid Id, string Name);

public sealed record InstallmentRefDto(int Number, int Total);

public sealed record TransactionDto(
    Guid Id,
    TransactionType Type,
    decimal Amount,
    DateOnly Date,
    string Description,
    TransactionStatus Status,
    AccountRefDto Account,
    AccountRefDto? DestinationAccount,
    CategoryRefDto? Category,
    InstallmentRefDto? Installment,
    bool Recurring,
    string? StatementMonth);

/// <summary>Saldo de uma conta, ja somando o saldo inicial dela (SPEC secao 5.1).</summary>
public sealed record AccountBalanceDto(Guid AccountId, decimal Balance);

public sealed record MonthSummaryDto(decimal Income, decimal Expenses, decimal Balance);

public sealed record CategoryTotalDto(Guid CategoryId, string Category, string Color, decimal Amount, decimal Percent);

public sealed record PaginationDto(int Page, int Size, int Total, int TotalPages);

public sealed record TransactionListDto(
    IReadOnlyList<TransactionDto> Items,
    PaginationDto Pagination,
    MonthSummaryDto MonthSummary);

/// <summary>Filtros da listagem de transacoes (docs/api.md secao 6).</summary>
public sealed record TransactionFilter(
    string? Month = null,
    Guid? AccountId = null,
    Guid? CategoryId = null,
    TransactionType? Type = null,
    TransactionStatus? Status = null,
    string? Search = null,
    int Page = 1,
    int Size = 50);

public sealed record CreateTransactionRequest(
    TransactionType Type,
    decimal Amount,
    DateOnly Date,
    string Description,
    Guid AccountId,
    Guid? DestinationAccountId,
    Guid? CategoryId);

public sealed record CreateInstallmentsRequest(
    string Description,
    decimal TotalAmount,
    int Installments,
    DateOnly Date,
    Guid CardId,
    Guid CategoryId);

/// <summary>
/// Resultado de uma compra parcelada. O id do grupo importa porque e por ele que se cancela as
/// parcelas futuras de uma vez.
/// </summary>
public sealed record InstallmentsResultDto(
    Guid InstallmentGroupId,
    IReadOnlyList<TransactionDto> Transactions);

public sealed record UpdateTransactionRequest(
    decimal Amount,
    DateOnly Date,
    string Description,
    Guid AccountId,
    Guid? DestinationAccountId,
    Guid? CategoryId);

/// <summary>Resposta de escrita: devolve o saldo recalculado para a UI nao ter de somar nada.</summary>
public sealed record TransactionWriteResultDto(
    TransactionDto Transaction,
    decimal AccountBalance,
    decimal? DestinationAccountBalance);

public sealed record CreateCategoryRequest(string Name, CategoryKind Type, string Color, string Icon);

public sealed record UpdateCategoryRequest(string Name, string Color, string Icon);

public sealed record StatementSummaryDto(
    string Month,
    DateOnly ClosingDate,
    DateOnly DueDate,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    StatementStatus Status,
    decimal Total);

public sealed record StatementDetailDto(
    StatementSummaryDto Summary,
    IReadOnlyList<TransactionDto> Purchases);

public sealed record PayStatementResultDto(
    TransactionDto Transaction,
    StatementStatus Status,
    decimal SourceAccountBalance,
    decimal CardBalance);

public sealed record RecurrenceDto(
    Guid Id,
    string Description,
    decimal Amount,
    CategoryKind Type,
    RecurrenceFrequency Frequency,
    int ReferenceDay,
    int? ReferenceMonth,
    DateOnly StartsOn,
    DateOnly? EndsOn,
    DateOnly NextRunOn,
    bool Active,
    AccountRefDto Account,
    CategoryRefDto Category);

public sealed record CreateRecurrenceRequest(
    string Description,
    decimal Amount,
    CategoryKind Type,
    RecurrenceFrequency Frequency,
    int ReferenceDay,
    int? ReferenceMonth,
    DateOnly StartsOn,
    DateOnly? EndsOn,
    Guid AccountId,
    Guid CategoryId);
