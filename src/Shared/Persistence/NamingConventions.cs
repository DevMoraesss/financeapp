using System.Text;
using Microsoft.EntityFrameworkCore;

namespace FinanceMove.Shared.Persistence;

/// <summary>
/// Converte os nomes gerados pelo EF Core (PascalCase) para o padrão do banco (snake_case).
/// <para>
/// Sem isto o Postgres receberia colunas como <c>"InitialBalance"</c>, que exigem aspas em toda
/// query manual. Com isto, o SQL fica legível no psql: <c>select initial_balance from accounts.account</c>.
/// </para>
/// </summary>
public static class NamingConventions
{
    /// <summary>
    /// Aplica snake_case a tabelas, colunas, chaves, FKs e índices.
    /// <b>Chame no FIM do <c>OnModelCreating</c></b> - ela reescreve o que já foi configurado.
    /// </summary>
    public static void UseSnakeCaseNames(this ModelBuilder builder)
    {
        foreach (var entity in builder.Model.GetEntityTypes())
        {
            if (entity.GetTableName() is { } table)
            {
                entity.SetTableName(ToSnakeCase(table));
            }

            // Usamos o nome da PROPRIEDADE (e não o da coluna já resolvida) porque é estável
            // e cobre também as propriedades sombra criadas pelo EF para chaves estrangeiras.
            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
            }

            foreach (var key in entity.GetKeys())
            {
                if (key.GetName() is { } name)
                {
                    key.SetName(ToSnakeCase(name));
                }
            }

            foreach (var foreignKey in entity.GetForeignKeys())
            {
                if (foreignKey.GetConstraintName() is { } name)
                {
                    foreignKey.SetConstraintName(ToSnakeCase(name));
                }
            }

            foreach (var index in entity.GetIndexes())
            {
                if (index.GetDatabaseName() is { } name)
                {
                    index.SetDatabaseName(ToSnakeCase(name));
                }
            }
        }
    }

    /// <summary>
    /// <c>AppUser</c> -> <c>app_user</c> · <c>UserId</c> -> <c>user_id</c> · <c>CreditCard</c> -> <c>credit_card</c>.
    /// Também é usada para gravar valores de enum no banco.
    /// </summary>
    public static string ToSnakeCase(string name)
    {
        var result = new StringBuilder(name.Length + 8);

        for (var i = 0; i < name.Length; i++)
        {
            var current = name[i];

            if (char.IsUpper(current))
            {
                var previousIsLower = i > 0 && !char.IsUpper(name[i - 1]);
                var nextIsLower = i + 1 < name.Length && char.IsLower(name[i + 1]);

                if (i > 0 && (previousIsLower || nextIsLower))
                {
                    result.Append('_');
                }

                result.Append(char.ToLowerInvariant(current));
            }
            else
            {
                result.Append(current);
            }
        }

        return result.ToString();
    }
}
