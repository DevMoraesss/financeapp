namespace FinanceMove.Modules.Identity.Contracts;

/// <summary>
/// O que os outros módulos (e um dia o hub AppLife) sabem sobre um usuário.
/// Repare no que NÃO está aqui: hash de senha, lockout, tokens - detalhes internos do módulo.
/// </summary>
public sealed record UserDto(Guid Id, string Name, string Email);
