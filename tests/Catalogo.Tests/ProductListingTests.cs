using Catalogo.Data;
using Catalogo.Features.Categories;
using Catalogo.Features.Products;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Catalogo.Tests;

/// <summary>
/// Listagem do acervo no painel (T-17). É a única tela onde o Rascunho aparece (RN-15), e
/// a ordem é a global — categoria pela posição dela, produto pela posição dentro da
/// categoria (RN-51, RN-22).
///
/// Esta classe cria um **banco próprio** no mesmo container, em vez de compartilhar o da
/// coleção como as demais. A listagem devolve o acervo inteiro, sem recorte por id: com
/// os dados das outras classes presentes, nenhuma asserção sobre a lista completa seria
/// exata, e o estado "acervo vazio" seria impossível de alcançar. O custo é uma migration
/// a mais por execução da classe.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ProductListingTests(PostgresFixture postgres) : IAsyncLifetime
{
    private string connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        var databaseName = $"listagem_{Guid.NewGuid():N}";

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
    public async Task Produtos_saem_agrupados_por_categoria_na_ordem_global()
    {
        var second = await CreateCategoryAsync("Energia", position: 2);
        var first = await CreateCategoryAsync("Impressoras", position: 1);
        await CreateProductAsync(second, "Nobreak", position: 1);
        await CreateProductAsync(first, "Multifuncional", position: 1);

        var groups = await ListAsync(ProductSituationFilter.All);

        Assert.Equal(["Impressoras", "Energia"], groups.Select(group => group.CategoryName));
    }

    [Fact]
    public async Task Dentro_da_categoria_a_ordem_e_a_posicao_curada()
    {
        var category = await CreateCategoryAsync("Tintas", position: 1);
        await CreateProductAsync(category, "Terceira", position: 3);
        await CreateProductAsync(category, "Primeira", position: 1);
        await CreateProductAsync(category, "Segunda", position: 2);

        var groups = await ListAsync(ProductSituationFilter.All);

        Assert.Equal(
            ["Primeira", "Segunda", "Terceira"],
            groups.Single().Products.Select(product => product.Name));
    }

    /// <summary>
    /// A numeração é posicional sobre a ordem global das categorias e **não muda com o
    /// filtro** — o número que o dono vê é propriedade do acervo, não da visão. A
    /// numeração do PDF é recalculada dentro do recorte gerado, que é outra regra
    /// (RN-39, ADR-015), resolvida em T-24.
    /// </summary>
    [Fact]
    public async Task A_numeracao_da_categoria_e_posicional_e_nao_muda_com_o_filtro()
    {
        var first = await CreateCategoryAsync("Impressoras", position: 1);
        var second = await CreateCategoryAsync("Energia", position: 2);
        await CreateProductAsync(first, "Multifuncional", position: 1, ProductStatus.Published);
        await CreateProductAsync(second, "Nobreak", position: 1);

        var all = await ListAsync(ProductSituationFilter.All);
        var drafts = await ListAsync(ProductSituationFilter.Drafts);

        Assert.Equal([1, 2], all.Select(group => group.Number));
        Assert.Equal(2, drafts.Single().Number);
    }

    [Fact]
    public async Task Filtro_No_ar_devolve_apenas_os_publicados()
    {
        var category = await CreateCategoryAsync("Redes", position: 1);
        await CreateProductAsync(category, "Switch", position: 1, ProductStatus.Published);
        await CreateProductAsync(category, "Roteador", position: 2);

        var groups = await ListAsync(ProductSituationFilter.Published);

        Assert.Equal(["Switch"], groups.Single().Products.Select(product => product.Name));
    }

    [Fact]
    public async Task Filtro_de_rascunhos_devolve_apenas_os_nao_publicados()
    {
        var category = await CreateCategoryAsync("Redes", position: 1);
        await CreateProductAsync(category, "Switch", position: 1, ProductStatus.Published);
        await CreateProductAsync(category, "Roteador", position: 2);

        var groups = await ListAsync(ProductSituationFilter.Drafts);

        Assert.Equal(["Roteador"], groups.Single().Products.Select(product => product.Name));
    }

    /// <summary>
    /// Categoria sem nenhum produto no recorte filtrado não vira faixa vazia: sob o
    /// filtro de rascunhos, uma categoria inteiramente publicada é ruído.
    /// </summary>
    [Fact]
    public async Task Categoria_sem_produto_no_filtro_nao_aparece()
    {
        var category = await CreateCategoryAsync("Redes", position: 1);
        await CreateProductAsync(category, "Switch", position: 1, ProductStatus.Published);

        Assert.Empty(await ListAsync(ProductSituationFilter.Drafts));
    }

    /// <summary>
    /// Acervo vazio é estado próprio da tela (`UI-04.vazio`), distinto do filtro sem
    /// resultado: existindo categoria mas nenhum produto, não há o que listar.
    /// </summary>
    [Fact]
    public async Task Acervo_sem_produto_devolve_lista_vazia()
    {
        await CreateCategoryAsync("Impressoras", position: 1);

        Assert.Empty(await ListAsync(ProductSituationFilter.All));
    }

    /// <summary>
    /// A miniatura é a derivada de tela; a ausência dela é o que a UI-04 exibe como
    /// marcador de falta, e não como espaço vazio.
    /// </summary>
    [Fact]
    public async Task Produto_sem_foto_devolve_miniatura_ausente()
    {
        var category = await CreateCategoryAsync("Impressoras", position: 1);
        await CreateProductAsync(category, "Sem foto", position: 1);
        await CreateProductAsync(category, "Com foto", position: 2, withPhoto: true);

        var products = (await ListAsync(ProductSituationFilter.All)).Single().Products;

        Assert.Null(products[0].ThumbnailFileName);
        Assert.NotNull(products[1].ThumbnailFileName);
    }

    private async Task<IReadOnlyList<ProductListGroup>> ListAsync(ProductSituationFilter filter) =>
        await new ProductListing(new ContextFactory(connectionString)).ListAsync(filter);

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
        int position,
        ProductStatus status = ProductStatus.Draft,
        bool withPhoto = false)
    {
        await using var context = CreateContext();

        context.Products.Add(new Product
        {
            Name = name,
            Price = 100m,
            CategoryId = categoryId,
            Position = position,
            Status = status,
            Photo = withPhoto ? Photo() : null
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
