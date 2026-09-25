namespace FinanceMove.Modules.Identity;

/// <summary>
/// Quem pode criar conta. O FinanceMove e por convite ate o portao de abertura publica
/// (SPEC secao 9.4): cadastro aberto na internet e convite para robo testar senha e encher o banco.
/// </summary>
/// <remarks>
/// Seguro por padrao: sem <see cref="InviteCode"/> e sem <see cref="Open"/>, o cadastro fica
/// FECHADO. Em desenvolvimento o appsettings.Development.json liga <see cref="Open"/>; em producao
/// o codigo vem da variavel Registration__InviteCode (docs/deploy.md).
/// </remarks>
public sealed class RegistrationOptions
{
    public const string SectionName = "Registration";

    /// <summary>
    /// Codigo que o convidado digita no cadastro. Quando preenchido, e obrigatorio e precisa
    /// bater exatamente.
    /// </summary>
    public string InviteCode { get; set; } = string.Empty;

    /// <summary>Cadastro livre, sem codigo. So faz sentido em desenvolvimento e teste.</summary>
    public bool Open { get; set; }

    /// <summary>Teto de contas no sistema. Zero significa sem teto.</summary>
    public int MaxUsers { get; set; }
}
