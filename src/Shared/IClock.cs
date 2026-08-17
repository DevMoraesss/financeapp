namespace FinanceMove.Shared;

/// <summary>
/// Fonte única de "que dia é hoje" no sistema inteiro.
/// <para>
/// Nunca use <c>DateTime.Now</c> em código de domínio: sem esta abstração é impossível testar
/// fatura fechando, recorrência disparando e parcela vencendo - que é exatamente o que a
/// verificação fim-a-fim da SPEC secao 10 exige (ela avança o calendário).
/// </para>
/// </summary>
public interface IClock
{
    /// <summary>Data de hoje no fuso oficial do produto (America/Sao_Paulo).</summary>
    DateOnly Today { get; }

    /// <summary>Instante atual em UTC - para carimbos técnicos (<c>created_at</c>, <c>updated_at</c>).</summary>
    DateTimeOffset UtcNow { get; }
}
