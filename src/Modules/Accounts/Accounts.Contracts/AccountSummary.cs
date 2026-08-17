namespace FinanceMove.Modules.Accounts.Contracts;

/// <summary>
/// O que os outros módulos precisam saber de uma conta.
/// </summary>
/// <remarks>
/// Repare que <b>não há saldo atual aqui</b>: quem calcula saldo é o módulo Transactions, que soma
/// os lançamentos sobre o <see cref="InitialBalance"/> (SPEC secao 5.1 e modelo-de-dados secao 4.3).
/// Accounts é dono do cadastro; Transactions é dono dos fatos.
/// </remarks>
public sealed record AccountSummary(
    Guid Id,
    string Name,
    AccountType Type,
    decimal InitialBalance,
    short? ClosingDay,
    short? DueDay,
    bool Archived);
