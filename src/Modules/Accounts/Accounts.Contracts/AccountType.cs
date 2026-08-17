namespace FinanceMove.Modules.Accounts.Contracts;

/// <summary>Tipos de conta suportados na v1 (SPEC secao 5.1).</summary>
public enum AccountType
{
    /// <summary>Conta corrente.</summary>
    Checking = 1,

    /// <summary>Poupança.</summary>
    Savings = 2,

    /// <summary>Dinheiro em espécie / carteira.</summary>
    Cash = 3,

    /// <summary>
    /// Cartão de crédito. Único tipo que exige ciclo (dia de fechamento e de vencimento) e cujo
    /// saldo é normalmente negativo - representa o que se deve, não o que se tem.
    /// </summary>
    CreditCard = 4,
}
