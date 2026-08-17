namespace FinanceMove.Modules.Accounts.Contracts;

/// <summary>Datas de uma fatura de cartao de credito.</summary>
/// <param name="Month">Mes que identifica a fatura, no formato AAAA-MM (o mes em que ela vence).</param>
/// <param name="ClosingDate">Dia em que a fatura fecha.</param>
/// <param name="DueDate">Dia em que a fatura vence.</param>
/// <param name="PeriodStart">Primeiro dia do periodo de compras (exclusivo do fechamento anterior).</param>
/// <param name="PeriodEnd">Ultimo dia do periodo de compras, igual ao fechamento.</param>
public sealed record StatementCycle(
    string Month,
    DateOnly ClosingDate,
    DateOnly DueDate,
    DateOnly PeriodStart,
    DateOnly PeriodEnd);

/// <summary>
/// Calculo do ciclo de fatura (SPEC secao 5.3). Mora em Contracts porque tanto o modulo Accounts
/// (dono do cadastro do cartao) quanto o modulo Transactions (dono das compras) precisam dele,
/// e duplicar essa regra seria pedir para os dois divergirem.
/// </summary>
public static class CardCycle
{
    /// <summary>
    /// Calcula a fatura identificada por <paramref name="month"/>, que e o mes em que ela VENCE.
    /// </summary>
    /// <remarks>
    /// A pegadinha que quase todo app brasileiro erra: quando o dia de vencimento e MENOR que o
    /// de fechamento (Nubank fecha 25 e vence 02), o fechamento acontece no mes ANTERIOR ao
    /// vencimento. Fatura de setembro fecha em 25/08 e vence em 02/09.
    /// </remarks>
    public static StatementCycle For(int closingDay, int dueDay, DateOnly month)
    {
        if (closingDay is < 1 or > 28 || dueDay is < 1 or > 28)
        {
            throw new ArgumentOutOfRangeException(nameof(closingDay), "Dias do ciclo precisam estar entre 1 e 28.");
        }

        if (closingDay == dueDay)
        {
            throw new ArgumentException("Fechamento e vencimento nao podem cair no mesmo dia: o ciclo fica ambiguo.");
        }

        var dueDate = new DateOnly(month.Year, month.Month, dueDay);

        // Vencimento depois do fechamento: os dois no mesmo mes. Vencimento antes: fechou no mes passado.
        var closingMonth = dueDay > closingDay ? dueDate : dueDate.AddMonths(-1);
        var closingDate = new DateOnly(closingMonth.Year, closingMonth.Month, closingDay);

        // O periodo comeca no dia seguinte ao fechamento anterior e termina no fechamento desta fatura.
        var previousClosing = closingDate.AddMonths(-1);

        return new StatementCycle(
            Month: $"{month.Year:D4}-{month.Month:D2}",
            ClosingDate: closingDate,
            DueDate: dueDate,
            PeriodStart: previousClosing.AddDays(1),
            PeriodEnd: closingDate);
    }

    /// <summary>
    /// Descobre em qual fatura uma compra cai: a primeira cujo fechamento seja igual ou posterior
    /// a data da compra.
    /// </summary>
    public static string StatementMonthFor(int closingDay, int dueDay, DateOnly purchaseDate)
    {
        // Comeca no mes da compra e anda para frente ate o periodo conter a data. No maximo duas
        // tentativas sao necessarias, mas o laco deixa a intencao explicita.
        var candidate = new DateOnly(purchaseDate.Year, purchaseDate.Month, 1);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var cycle = For(closingDay, dueDay, candidate);

            if (purchaseDate >= cycle.PeriodStart && purchaseDate <= cycle.PeriodEnd)
            {
                return cycle.Month;
            }

            candidate = candidate.AddMonths(1);
        }

        throw new InvalidOperationException(
            $"Nao foi possivel determinar a fatura da compra em {purchaseDate:yyyy-MM-dd}.");
    }
}
