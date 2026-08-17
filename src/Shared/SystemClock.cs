namespace FinanceMove.Shared;

/// <summary>
/// Implementação real do <see cref="IClock"/>.
/// <para>
/// Aceita um "hoje" fixo para desenvolvimento e teste (variável <c>TEST_TODAY</c>), o que permite
/// rodar o roteiro da SPEC secao 10 avançando o calendário sem mexer no relógio da máquina.
/// O override é ignorado em Production - a checagem fica em <c>Program.cs</c>.
/// </para>
/// </summary>
public sealed class SystemClock(DateOnly? fixedToday = null) : IClock
{
    private static readonly TimeZoneInfo SaoPaulo = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    public DateOnly Today =>
        fixedToday ?? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, SaoPaulo).DateTime);

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
