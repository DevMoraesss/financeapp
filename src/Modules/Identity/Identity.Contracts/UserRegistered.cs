using FinanceMove.Shared.Events;

namespace FinanceMove.Modules.Identity.Contracts;

/// <summary>
/// Publicado quando um usuario conclui o cadastro.
/// </summary>
/// <remarks>
/// E o que permite ao modulo Transactions criar o seed de categorias (SPEC Apendice A) sem que o
/// modulo Identidade saiba que categorias existem. E a fronteira que mantem os modulos
/// destacaveis para o futuro AppLife.
/// </remarks>
public sealed record UserRegistered(Guid UserId, string Email, DateTimeOffset OccurredAt) : IDomainEvent;
