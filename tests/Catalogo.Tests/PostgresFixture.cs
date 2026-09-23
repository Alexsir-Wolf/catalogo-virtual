using Catalogo.Data;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Catalogo.Tests;

/// <summary>
/// Banco limpo em container para cada execução da suíte. A imagem carrega as extensões
/// que a migration habilita (<c>unaccent</c> e <c>pg_trgm</c>), e o usuário do container
/// é superusuário — é o que permite verificar o <c>CREATE EXTENSION</c> de verdade.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public string ConnectionString => container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await container.StartAsync();

        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await container.DisposeAsync();

    public CatalogDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        return new CatalogDbContext(options);
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
