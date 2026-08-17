using FinanceMove.Modules.Accounts;
using FinanceMove.Modules.Accounts.Contracts;
using FinanceMove.Modules.Identity;
using Microsoft.EntityFrameworkCore;

namespace FinanceMove.IntegrationTests;

/// <summary>
/// Prova, no banco de verdade, a decisão de modelagem mais importante da fundação:
/// <c>user_id</c> tem FK com ON DELETE CASCADE para <c>identity.app_user</c>, mesmo cruzando
/// schemas de módulos diferentes (modelo-de-dados secao 1.1).
/// </summary>
/// <remarks>
/// É isso que faz o "excluir minha conta" da LGPD (SPEC US-13) ser confiável: um único DELETE
/// apaga tudo, sem depender de uma cadeia de eventos que pode falhar no meio e deixar dados
/// órfãos de um usuário supostamente excluído.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class UserCascadeDeleteTests(PostgresFixture fixture)
{
    [Fact]
    public async Task ExcluirUsuario_ApagaEmCascataAsContasDeOutroModulo()
    {
        var userId = Guid.NewGuid();

        await using (var identity = fixture.CreateIdentityContext())
        {
            identity.Users.Add(new AppUser
            {
                Id = userId,
                Name = "Juan Teste",
                Email = $"{userId}@teste.local",
                NormalizedEmail = $"{userId}@TESTE.LOCAL",
                UserName = $"{userId}@teste.local",
                NormalizedUserName = $"{userId}@TESTE.LOCAL",
                SecurityStamp = Guid.NewGuid().ToString(),
            });

            await identity.SaveChangesAsync();
        }

        await using (var accounts = fixture.CreateAccountsContext())
        {
            accounts.Accounts.Add(new Account
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Name = "Corrente",
                Type = AccountType.Checking,
                InitialBalance = 1000.00m,
            });

            await accounts.SaveChangesAsync();
            Assert.Equal(1, await accounts.Accounts.CountAsync(a => a.UserId == userId));
        }

        // O "excluir minha conta" da LGPD: um DELETE só.
        await using (var identity = fixture.CreateIdentityContext())
        {
            await identity.Users.Where(user => user.Id == userId).ExecuteDeleteAsync();
        }

        await using (var accounts = fixture.CreateAccountsContext())
        {
            Assert.Equal(0, await accounts.Accounts.CountAsync(a => a.UserId == userId));
        }
    }

    [Fact]
    public async Task DinheiroEGravadoComoNumeric14x2_SemErroDePontoFlutuante()
    {
        var userId = Guid.NewGuid();

        await using (var identity = fixture.CreateIdentityContext())
        {
            identity.Users.Add(new AppUser
            {
                Id = userId,
                Name = "Teste Decimal",
                Email = $"{userId}@teste.local",
                NormalizedEmail = $"{userId}@TESTE.LOCAL",
                UserName = $"{userId}@teste.local",
                NormalizedUserName = $"{userId}@TESTE.LOCAL",
                SecurityStamp = Guid.NewGuid().ToString(),
            });

            await identity.SaveChangesAsync();
        }

        var accountId = Guid.NewGuid();

        await using (var accounts = fixture.CreateAccountsContext())
        {
            accounts.Accounts.Add(new Account
            {
                Id = accountId,
                UserId = userId,
                Name = "Poupança",
                Type = AccountType.Savings,
                InitialBalance = 0.10m,
            });

            await accounts.SaveChangesAsync();
        }

        await using (var accounts = fixture.CreateAccountsContext())
        {
            var stored = await accounts.Accounts.AsNoTracking().SingleAsync(a => a.Id == accountId);

            // Com float, 0.10 + 0.20 daria 0.30000000000000004 e esta igualdade falharia.
            Assert.Equal(0.30m, stored.InitialBalance + 0.20m);
        }
    }
}
