using FinanceMove.Modules.Transactions.Contracts;

namespace FinanceMove.Modules.Transactions;

/// <summary>
/// Categoria de receita ou despesa. Vive no modulo Transactions porque categoria so existe para
/// classificar lancamento (docs/arquitetura.md secao 3).
/// </summary>
public sealed class Category
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>Nome em portugues: e dado do usuario, exibido na UI pt-BR.</summary>
    public required string Name { get; set; }

    public CategoryKind Type { get; set; }

    /// <summary>Cor hexadecimal usada no donut e nas listas.</summary>
    public required string Color { get; set; }

    /// <summary>Nome do icone lucide correspondente no front.</summary>
    public required string Icon { get; set; }

    /// <summary>
    /// Categoria de sistema ("Ajuste"). Nao pode ser renomeada nem arquivada, porque o ajuste de
    /// saldo depende dela existir (SPEC US-11).
    /// </summary>
    public bool System { get; set; }

    public bool Archived { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Um lancamento. Receita, despesa ou transferencia (SPEC secao 5.2).
/// </summary>
public sealed class Transaction
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public TransactionType Type { get; set; }

    /// <summary>
    /// Sempre positivo. Quem decide o sinal e o <see cref="Type"/>, nunca o valor.
    /// Guardar despesa como numero negativo espalharia Math.Abs pelo codigo e permitiria o
    /// estado sem sentido "receita de menos R$ 50" (modelo-de-dados secao 3.1).
    /// </summary>
    public decimal Amount { get; set; }

    public DateOnly Date { get; set; }

    public required string Description { get; set; }

    public TransactionStatus Status { get; set; }

    /// <summary>Conta de origem. Referencia logica: o modulo Accounts e outro schema.</summary>
    public Guid AccountId { get; set; }

    /// <summary>Preenchida se, e somente se, o tipo for transferencia.</summary>
    public Guid? DestinationAccountId { get; set; }

    /// <summary>Obrigatoria em receita e despesa, proibida em transferencia.</summary>
    public Guid? CategoryId { get; set; }

    public Category? Category { get; set; }

    public Guid? RecurrenceRuleId { get; set; }

    public Guid? InstallmentGroupId { get; set; }

    public short? InstallmentNumber { get; set; }

    public short? InstallmentTotal { get; set; }

    /// <summary>
    /// AAAA-MM. Em compras de cartao, indica em qual fatura a compra caiu. Na transferencia de
    /// pagamento, indica qual fatura foi quitada.
    /// </summary>
    public string? StatementMonth { get; set; }

    /// <summary>Reservado para deduplicacao da importacao de extrato (v2).</summary>
    public string? ExternalId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Quanto este lancamento move o saldo da conta informada. Fonte unica da verdade sobre
    /// sinal de dinheiro no sistema inteiro.
    /// </summary>
    public decimal BalanceEffectOn(Guid accountId) => Type switch
    {
        TransactionType.Income when AccountId == accountId => Amount,
        TransactionType.Expense when AccountId == accountId => -Amount,
        TransactionType.Transfer when AccountId == accountId => -Amount,
        TransactionType.Transfer when DestinationAccountId == accountId => Amount,
        _ => 0m,
    };
}

/// <summary>Agrupa as parcelas de uma compra parcelada no cartao (SPEC US-06).</summary>
public sealed class InstallmentGroup
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public required string Description { get; set; }

    public decimal TotalAmount { get; set; }

    public short Installments { get; set; }

    public Guid CardId { get; set; }

    public DateOnly PurchaseDate { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Regra de lancamento recorrente. NAO e uma transacao: e a receita que gera transacoes quando a
/// data chega (modelo-de-dados secao 4.2).
/// </summary>
public sealed class RecurrenceRule
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public required string Description { get; set; }

    public decimal Amount { get; set; }

    public CategoryKind Type { get; set; }

    public Guid CategoryId { get; set; }

    public Category? Category { get; set; }

    public Guid AccountId { get; set; }

    public RecurrenceFrequency Frequency { get; set; }

    /// <summary>Dia da semana (1 a 7) na frequencia semanal, dia do mes (1 a 28) nas demais.</summary>
    public short ReferenceDay { get; set; }

    /// <summary>Mes de referencia (1 a 12). So existe na frequencia anual.</summary>
    public short? ReferenceMonth { get; set; }

    public DateOnly StartsOn { get; set; }

    public DateOnly? EndsOn { get; set; }

    /// <summary>
    /// Ponteiro da proxima geracao. Avanca na MESMA transacao de banco em que a transacao e
    /// criada, e e isso que torna o catch-up idempotente.
    /// </summary>
    public DateOnly NextRunOn { get; set; }

    public bool Active { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Calcula a ocorrencia seguinte a data informada, respeitando a frequencia.</summary>
    public DateOnly OccurrenceAfter(DateOnly date) => Frequency switch
    {
        RecurrenceFrequency.Weekly => date.AddDays(7),
        RecurrenceFrequency.Monthly => date.AddMonths(1),
        RecurrenceFrequency.Yearly => date.AddYears(1),
        _ => date.AddMonths(1),
    };

    /// <summary>Primeira ocorrencia igual ou posterior a data de inicio.</summary>
    public DateOnly FirstOccurrence()
    {
        switch (Frequency)
        {
            case RecurrenceFrequency.Weekly:
                {
                    // ReferenceDay 1 a 7 com segunda-feira em 1, para casar com o costume brasileiro.
                    var target = (DayOfWeek)(ReferenceDay % 7);
                    var candidate = StartsOn;

                    while (candidate.DayOfWeek != target)
                    {
                        candidate = candidate.AddDays(1);
                    }

                    return candidate;
                }

            case RecurrenceFrequency.Yearly:
                {
                    var month = ReferenceMonth ?? StartsOn.Month;
                    var candidate = new DateOnly(StartsOn.Year, month, ReferenceDay);
                    return candidate >= StartsOn ? candidate : candidate.AddYears(1);
                }

            default:
                {
                    var candidate = new DateOnly(StartsOn.Year, StartsOn.Month, ReferenceDay);
                    return candidate >= StartsOn ? candidate : candidate.AddMonths(1);
                }
        }
    }
}
