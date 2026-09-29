using Catalogo.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Catalogo.Tests;

/// <summary>
/// Banco próprio para um caso de teste, no mesmo container da coleção.
///
/// Existe porque algumas telas e consultas enxergam o acervo **inteiro** — a listagem do
/// painel, a vitrine, o registro único de configuração. Compartilhando o banco da coleção,
/// nenhuma asserção sobre a lista completa seria exata, e os estados "vazio" e "sem capa"
/// seriam inalcançáveis.
///
/// A higiene aqui não é detalhe: sem ela o padrão derruba a suíte. Cada cadeia de conexão
/// distinta ganha um pool próprio no Npgsql, e o PostgreSQL recusa com
/// <c>53300: sorry, too many clients already</c> por volta de uma centena deles. Daí o
/// pool pequeno e o descarte que limpa e apaga.
/// </summary>
internal sealed class IsolatedDatabase : IAsyncDisposable
{
    /// <summary>
    /// Um caso de teste usa poucas conexões simultâneas — a consulta da vitrine dispara
    /// duas em paralelo, e é o máximo que acontece. Limitar o pool é o que permite dezenas
    /// de bancos por execução sem esgotar o servidor.
    /// </summary>
    private const int MaxPoolSize = 4;

    private readonly string administrative;

    private IsolatedDatabase(string administrative, string connectionString, string name)
    {
        this.administrative = administrative;
        ConnectionString = connectionString;
        Name = name;
    }

    public string ConnectionString { get; }

    public string Name { get; }

    public static async Task<IsolatedDatabase> CreateAsync(PostgresFixture postgres, string prefix)
    {
        var name = $"{prefix}_{Guid.NewGuid():N}";

        await using var admin = new NpgsqlConnection(postgres.ConnectionString);
        await admin.OpenAsync();

        await using var create = admin.CreateCommand();
        create.CommandText = $"""CREATE DATABASE "{name}" """;
        await create.ExecuteNonQueryAsync();

        var connectionString = new NpgsqlConnectionStringBuilder(postgres.ConnectionString)
        {
            Database = name,
            MaxPoolSize = MaxPoolSize,
            MinPoolSize = 0
        }.ConnectionString;

        return new IsolatedDatabase(postgres.ConnectionString, connectionString, name);
    }

    public CatalogDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<CatalogDbContext>().UseNpgsql(ConnectionString).Options);

    public async Task MigrateAsync()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync()
    {
        // Limpar o pool antes de apagar: conexão aberta segura o banco e o `DROP` falha —
        // e é este passo, não o `DROP`, que devolve as conexões ao servidor.
        await using var pooled = new NpgsqlConnection(ConnectionString);
        NpgsqlConnection.ClearPool(pooled);

        try
        {
            await using var admin = new NpgsqlConnection(administrative);
            await admin.OpenAsync();

            await using var drop = admin.CreateCommand();
            drop.CommandText = $"""DROP DATABASE IF EXISTS "{Name}" WITH (FORCE)""";
            await drop.ExecuteNonQueryAsync();
        }
        catch (NpgsqlException)
        {
            // Falha ao apagar não pode derrubar o caso que já passou: o container morre
            // com a execução e leva o banco junto. O que não pode faltar é o `ClearPool`,
            // que é quem devolve as conexões.
        }
    }
}
