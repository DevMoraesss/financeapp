using FinanceMove.Modules.Accounts.Contracts;

namespace FinanceMove.Modules.Accounts;

/// <summary>
/// Uma conta do usuario: corrente, poupanca, dinheiro ou cartao de credito (SPEC secao 5.1).
/// </summary>
public sealed class Account
{
    public Guid Id { get; set; }

    /// <summary>
    /// Dono da conta. E a ancora do multi-tenant: toda query filtra por aqui, e esta e a UNICA
    /// coluna com chave estrangeira para outro modulo (identity.app_user, com ON DELETE CASCADE,
    /// conforme modelo-de-dados secao 1.1).
    /// </summary>
    public Guid UserId { get; set; }

    public required string Name { get; set; }

    public AccountType Type { get; set; }

    /// <summary>
    /// Saldo no momento em que a conta foi cadastrada. Pode ser negativo.
    /// O saldo ATUAL nunca e armazenado: e este valor somado as transacoes (SPEC D4).
    /// </summary>
    public decimal InitialBalance { get; set; }

    /// <summary>Dia do fechamento da fatura (1 a 28). So para cartao de credito.</summary>
    public short? ClosingDay { get; set; }

    /// <summary>Dia do vencimento da fatura (1 a 28). So para cartao de credito.</summary>
    public short? DueDay { get; set; }

    /// <summary>
    /// Conta arquivada some dos formularios mas continua nos relatorios. Conta que ja teve
    /// transacao nunca e excluida de verdade (modelo-de-dados secao 5.3).
    /// </summary>
    public bool Archived { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Datas da fatura identificada por <paramref name="month"/>, que e o mes em que ela vence.
    /// Delega para <see cref="CardCycle"/>, que fica em Contracts porque o modulo Transactions
    /// tambem precisa da mesma regra.
    /// </summary>
    /// <exception cref="InvalidOperationException">Se a conta nao for cartao de credito.</exception>
    public StatementCycle GetStatementCycle(DateOnly month)
    {
        if (Type != AccountType.CreditCard || ClosingDay is not { } closing || DueDay is not { } due)
        {
            throw new InvalidOperationException("Somente contas de cartao de credito possuem ciclo de fatura.");
        }

        return CardCycle.For(closing, due, month);
    }
}
