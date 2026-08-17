namespace FinanceMove.Modules.Transactions.Contracts;

/// <summary>
/// Tipo do lancamento (SPEC secao 5.2).
/// </summary>
/// <remarks>
/// Transferencia e um tipo proprio, e nao um par de receita mais despesa, justamente para que os
/// relatorios nao mintam: mover R$ 1.500 da corrente para a poupanca nao e gasto (SPEC D6).
/// </remarks>
public enum TransactionType
{
    Income = 1,
    Expense = 2,
    Transfer = 3,
}

public enum TransactionStatus
{
    /// <summary>Gerada por recorrencia e ainda nao confirmada. Nao entra no saldo nem no orcamento.</summary>
    Pending = 1,

    Confirmed = 2,
}

/// <summary>Categoria classifica receita ou despesa. Transferencia nunca tem categoria.</summary>
public enum CategoryKind
{
    Income = 1,
    Expense = 2,
}

public enum RecurrenceFrequency
{
    Weekly = 1,
    Monthly = 2,
    Yearly = 3,
}

/// <summary>Situacao da fatura, derivada da data de hoje e do pagamento (SPEC secao 5.3).</summary>
public enum StatementStatus
{
    Open = 1,
    Closed = 2,
    Paid = 3,
}
