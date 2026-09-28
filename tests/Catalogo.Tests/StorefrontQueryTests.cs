using Catalogo.Data;
using Catalogo.Features.Categories;
using Catalogo.Features.Products;
using Catalogo.Features.Storefront;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Catalogo.Tests;

/// <summary>
/// Consulta pública da vitrine (T-18). É a consulta mais executada do sistema, e a única
/// que um anônimo alcança: tudo que ela devolve é público por definição, então o filtro da
/// RN-48 não é conveniência de tela — é a fronteira.
///
/// Banco próprio por caso, como em <see cref="ProductListingTests"/>: a consulta enxerga
/// o acervo inteiro e as contagens por categoria são sobre ele, então asserção exata só é
/// possível com o acervo sob controle do teste.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class StorefrontQueryTests(PostgresFixture postgres) : IAsyncLifetime
{
    private string connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        var databaseName = $"vitrine_{Guid.NewGuid():N}";

        await using var admin = new NpgsqlConnection(postgres.ConnectionString);
        await admin.OpenAsync();

        await using var create = admin.CreateCommand();
        create.CommandText = $"""CREATE DATABASE "{databaseName}" """;
        await create.ExecuteNonQueryAsync();

        connectionString = new NpgsqlConnectionStringBuilder(postgres.ConnectionString)
        {
            Database = databaseName
        }.ConnectionString;

        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task RN_48_produto_em_rascunho_nao_aparece_no_resultado()
    {
        var category = await CreateCategoryAsync("Impressoras", position: 1);
        await CreateProductAsync(category, "Multifuncional publicada", ProductStatus.Published);
        await CreateProductAsync(category, "Multifuncional em rascunho");

        var page = await QueryAsync(new StorefrontRequest());

        Assert.Equal(["Multifuncional publicada"], page.Products.Select(product => product.Name));
        Assert.Equal(1, page.Total);
    }

    /// <summary>
    /// CA-22: o acento é a metade fácil. O gabarito real é "Placa-Mãe", e quem busca
    /// digita "placa mae" — sem acento, sem hífen e em caixa baixa.
    /// </summary>
    [Fact]
    public async Task CA_22_busca_por_placa_mae_encontra_placa_mae_com_acento_e_hifen()
    {
        var category = await CreateCategoryAsync("Placas", position: 1);
        await CreateProductAsync(category, "Placa-Mãe Gigabyte A520M K V2", ProductStatus.Published);

        var page = await QueryAsync(new StorefrontRequest { Term = "placa mae" });

        Assert.Single(page.Products);
    }

    [Fact]
    public async Task CA_22_busca_com_acento_encontra_o_produto_sem_acento()
    {
        var category = await CreateCategoryAsync("Redes", position: 1);
        await CreateProductAsync(category, "Switch Gigabit 8 portas", ProductStatus.Published);

        var page = await QueryAsync(new StorefrontRequest { Term = "gigabít" });

        Assert.Single(page.Products);
    }

    /// <summary>
    /// CA-33: a RN-49 procura exclusivamente no nome. É restrição declarada, não
    /// limitação a corrigir — resumo e descrição não são pesquisáveis.
    /// </summary>
    [Fact]
    public async Task CA_33_termo_presente_apenas_na_descricao_nao_encontra()
    {
        var category = await CreateCategoryAsync("Notebooks", position: 1);
        await CreateProductAsync(
            category,
            "Notebook VAIO FE16",
            ProductStatus.Published,
            summary: "Core i5, 16GB, SSD 512GB",
            description: "Tela antirreflexo de 16 polegadas com moldura estreita.");

        Assert.Empty((await QueryAsync(new StorefrontRequest { Term = "antirreflexo" })).Products);
        Assert.Single((await QueryAsync(new StorefrontRequest { Term = "vaio" })).Products);
    }

    [Fact]
    public async Task O_termo_tambem_nao_procura_no_resumo()
    {
        var category = await CreateCategoryAsync("Notebooks", position: 1);
        await CreateProductAsync(
            category,
            "Notebook VAIO FE16",
            ProductStatus.Published,
            summary: "Core i5, 16GB, SSD 512GB");

        Assert.Empty((await QueryAsync(new StorefrontRequest { Term = "SSD" })).Products);
    }

    [Fact]
    public async Task Filtro_por_categoria_restringe_o_resultado()
    {
        var printers = await CreateCategoryAsync("Impressoras", position: 1);
        var networks = await CreateCategoryAsync("Redes", position: 2);
        await CreateProductAsync(printers, "Multifuncional", ProductStatus.Published);
        await CreateProductAsync(networks, "Switch", ProductStatus.Published);

        var page = await QueryAsync(new StorefrontRequest { CategoryId = networks });

        Assert.Equal(["Switch"], page.Products.Select(product => product.Name));
        Assert.Equal(1, page.Total);
    }

    /// <summary>
    /// A contagem por categoria é faceta: ela responde "quantos eu veria se escolhesse
    /// esta categoria", então respeita a busca e **ignora** a categoria já escolhida —
    /// senão as outras zerariam e o filtro deixaria de ser navegável (RN-50).
    /// </summary>
    [Fact]
    public async Task RN_50_contagem_por_categoria_respeita_a_busca_e_ignora_a_categoria_escolhida()
    {
        var printers = await CreateCategoryAsync("Impressoras", position: 1);
        var networks = await CreateCategoryAsync("Redes", position: 2);
        await CreateProductAsync(printers, "Multifuncional Epson", ProductStatus.Published);
        await CreateProductAsync(printers, "Multifuncional HP", ProductStatus.Published);
        await CreateProductAsync(networks, "Switch Multifuncional", ProductStatus.Published);
        await CreateProductAsync(networks, "Roteador", ProductStatus.Published);

        var page = await QueryAsync(new StorefrontRequest
        {
            Term = "multifuncional",
            CategoryId = networks
        });

        Assert.Equal(
            [("Impressoras", 2), ("Redes", 1)],
            page.Categories.Select(category => (category.Name, category.Products)));
    }

    [Fact]
    public async Task RN_50_categoria_sem_produto_publicado_nao_entra_na_contagem()
    {
        var printers = await CreateCategoryAsync("Impressoras", position: 1);
        var empty = await CreateCategoryAsync("Energia", position: 2);
        await CreateProductAsync(printers, "Multifuncional", ProductStatus.Published);
        await CreateProductAsync(empty, "Nobreak em rascunho");

        var page = await QueryAsync(new StorefrontRequest());

        Assert.Equal(["Impressoras"], page.Categories.Select(category => category.Name));
    }

    [Fact]
    public async Task RN_51_resultado_sai_na_ordem_global_de_categoria_e_produto()
    {
        var second = await CreateCategoryAsync("Energia", position: 2);
        var first = await CreateCategoryAsync("Impressoras", position: 1);
        await CreateProductAsync(second, "Nobreak", ProductStatus.Published, position: 1);
        await CreateProductAsync(first, "Scanner", ProductStatus.Published, position: 2);
        await CreateProductAsync(first, "Multifuncional", ProductStatus.Published, position: 1);

        var page = await QueryAsync(new StorefrontRequest());

        Assert.Equal(
            ["Multifuncional", "Scanner", "Nobreak"],
            page.Products.Select(product => product.Name));
    }

    [Fact]
    public async Task RN_52_paginacao_devolve_o_subconjunto_e_o_total_do_acervo()
    {
        var category = await CreateCategoryAsync("Impressoras", position: 1);
        for (var position = 1; position <= 5; position++)
        {
            await CreateProductAsync(
                category,
                $"Produto {position}",
                ProductStatus.Published,
                position: position);
        }

        var second = await QueryAsync(new StorefrontRequest { Page = 2, PageSize = 2 });

        Assert.Equal(["Produto 3", "Produto 4"], second.Products.Select(product => product.Name));
        Assert.Equal(5, second.Total);
        Assert.Equal(3, second.PageCount);
    }

    [Fact]
    public async Task Pagina_alem_da_ultima_devolve_vazio_sem_erro()
    {
        var category = await CreateCategoryAsync("Impressoras", position: 1);
        await CreateProductAsync(category, "Multifuncional", ProductStatus.Published);

        var page = await QueryAsync(new StorefrontRequest { Page = 9, PageSize = 12 });

        Assert.Empty(page.Products);
        Assert.Equal(1, page.Total);
    }

    /// <summary>
    /// O termo é do visitante, então os curingas do `like` precisam chegar como texto.
    /// Sem escape, um `%` solto devolveria o acervo inteiro e um `_` casaria qualquer
    /// caractere — busca virando ferramenta de enumeração.
    /// </summary>
    [Fact]
    public async Task Curinga_digitado_pelo_visitante_e_tratado_como_texto()
    {
        var category = await CreateCategoryAsync("Impressoras", position: 1);
        await CreateProductAsync(category, "Multifuncional", ProductStatus.Published);
        await CreateProductAsync(category, "Desconto de 50% na tinta", ProductStatus.Published);

        Assert.Equal(
            ["Desconto de 50% na tinta"],
            (await QueryAsync(new StorefrontRequest { Term = "50%" })).Products
                .Select(product => product.Name));

        // Como curinga, "%" devolveria os dois. Como texto, devolve só quem tem "%" no
        // nome — que é o que o visitante quis dizer ao digitar.
        Assert.Equal(
            ["Desconto de 50% na tinta"],
            (await QueryAsync(new StorefrontRequest { Term = "%" })).Products
                .Select(product => product.Name));

        Assert.Empty((await QueryAsync(new StorefrontRequest { Term = "_ultifuncional" })).Products);
    }

    [Fact]
    public async Task Termo_em_branco_nao_filtra()
    {
        var category = await CreateCategoryAsync("Impressoras", position: 1);
        await CreateProductAsync(category, "Multifuncional", ProductStatus.Published);

        Assert.Single((await QueryAsync(new StorefrontRequest { Term = "   " })).Products);
    }

    /// <summary>
    /// O risco declarado em T-18: é a consulta mais executada do sistema, e precisa
    /// alcançar o índice de expressão em vez de varrer a tabela. O plano do PostgreSQL é
    /// a única evidência que responde isso — o resto é esperança.
    /// </summary>
    [Fact]
    public async Task A_busca_alcanca_o_indice_trigrama_de_expressao()
    {
        var category = await CreateCategoryAsync("Impressoras", position: 1);
        for (var position = 1; position <= 60; position++)
        {
            await CreateProductAsync(
                category,
                $"Produto {position} multifuncional",
                ProductStatus.Published,
                position: position);
        }

        await using var context = CreateContext();
        await context.Database.ExecuteSqlRawAsync("ANALYZE \"Products\";");

        var plan = await ExplainSearchAsync("multifuncional");

        Assert.Contains("IX_Products_Name_Unaccent", plan);
    }

    private async Task<string> ExplainSearchAsync(string term)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();

        // `enable_seqscan = off` não é truque para forçar o resultado: ele prova que o
        // índice **é utilizável** pela consulta, que é exatamente o que o R-01 de
        // REVIEW-T-06-2026-09-23 dizia não ser verdade do índice anterior. Sem ele, o
        // planejador pode preferir varredura por volume pequeno e a asserção viraria
        // refém do tamanho do acervo de teste.
        command.CommandText = """
            SET enable_seqscan = off;
            EXPLAIN SELECT "Id" FROM "Products"
            WHERE catalogo_unaccent("Name") ILIKE '%' || catalogo_unaccent(@termo) || '%';
            """;
        command.Parameters.AddWithValue("termo", term);

        var plan = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            plan.Add(reader.GetString(0));
        }

        return string.Join('\n', plan);
    }

    private async Task<StorefrontPage> QueryAsync(StorefrontRequest request) =>
        await new StorefrontQuery(new ContextFactory(connectionString)).SearchAsync(request);

    private async Task<int> CreateCategoryAsync(string name, int position)
    {
        await using var context = CreateContext();

        var category = new Category { Name = name, Position = position };
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        return category.Id;
    }

    private async Task CreateProductAsync(
        int categoryId,
        string name,
        ProductStatus status = ProductStatus.Draft,
        string? summary = null,
        string? description = null,
        int position = 1)
    {
        await using var context = CreateContext();

        context.Products.Add(new Product
        {
            Name = name,
            Summary = summary,
            Description = description,
            Price = 100m,
            CategoryId = categoryId,
            Position = position,
            Status = status,
            Photo = Photo()
        });

        await context.SaveChangesAsync();
    }

    private static ProductPhoto Photo()
    {
        var prefix = Guid.NewGuid().ToString("N");

        return new ProductPhoto
        {
            OriginalFileName = $"{prefix}-original.webp",
            ThumbnailFileName = $"{prefix}-miniatura.webp",
            CardFileName = $"{prefix}-cartao.webp",
            LargeFileName = $"{prefix}-ampliada.webp",
            PrintFileName = $"{prefix}-impressao.jpg"
        };
    }

    private CatalogDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<CatalogDbContext>().UseNpgsql(connectionString).Options);

    private sealed class ContextFactory(string connectionString) : IDbContextFactory<CatalogDbContext>
    {
        public CatalogDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<CatalogDbContext>().UseNpgsql(connectionString).Options);
    }
}
