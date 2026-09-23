using Catalogo.Features.Categories;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Catalogo.Tests;

[Collection(PostgresCollection.Name)]
public sealed class SchemaTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Migration_cria_as_tabelas_de_categoria_e_produto()
    {
        var tables = await QueryTextAsync(
            "select table_name from information_schema.tables where table_schema = 'public'");

        Assert.Contains("Categories", tables);
        Assert.Contains("Products", tables);
    }

    [Fact]
    public async Task Produto_tem_todos_os_campos_do_prd()
    {
        var columns = await QueryTextAsync(
            "select column_name from information_schema.columns where table_name = 'Products'");

        string[] expected =
        [
            "Name", "Summary", "Description", "Price", "PriceLabel",
            "CategoryId", "Position", "Status",
            "Photo_OriginalFileName", "Photo_ThumbnailFileName",
            "Photo_CardFileName", "Photo_LargeFileName", "Photo_PrintFileName"
        ];

        Assert.All(expected, column => Assert.Contains(column, columns));
    }

    [Fact]
    public async Task Extensoes_de_busca_textual_estao_habilitadas()
    {
        var extensions = await QueryTextAsync("select extname from pg_extension");

        Assert.Contains("unaccent", extensions);
        Assert.Contains("pg_trgm", extensions);
    }

    [Fact]
    public async Task Indice_trigrama_cobre_o_nome_do_produto()
    {
        var definitions = await QueryTextAsync(
            "select indexdef from pg_indexes where tablename = 'Products'");

        Assert.Contains(definitions, definition =>
            definition.Contains("gin", StringComparison.OrdinalIgnoreCase)
            && definition.Contains("gin_trgm_ops", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Nenhuma_tabela_tem_coluna_de_tenant()
    {
        var columns = await QueryTextAsync(
            "select column_name from information_schema.columns where table_schema = 'public'");

        Assert.DoesNotContain(columns, column =>
            column.Contains("tenant", StringComparison.OrdinalIgnoreCase)
            || column.Contains("organization", StringComparison.OrdinalIgnoreCase)
            || column.Contains("store", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Migration_e_idempotente_na_subida()
    {
        await using var context = postgres.CreateContext();

        var pending = await context.Database.GetPendingMigrationsAsync();

        Assert.Empty(pending);
    }

    [Fact]
    public async Task Categoria_de_nome_repetido_e_recusada()
    {
        await using var context = postgres.CreateContext();
        var name = $"Impressoras {Guid.NewGuid():N}";

        context.Categories.Add(new Category { Name = name, Position = 1 });
        await context.SaveChangesAsync();

        await using var second = postgres.CreateContext();
        second.Categories.Add(new Category { Name = name, Position = 2 });

        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());

        Assert.Equal(PostgresErrorCodes.UniqueViolation, ((PostgresException)failure.InnerException!).SqlState);
    }

    private async Task<List<string>> QueryTextAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        var values = new List<string>();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }
}
