using Catalogo.Data;
using Catalogo.Features.Categories;
using Catalogo.Features.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Catalogo.Tests;

/// <summary>
/// Ciclo de publicação (T-14). A pré-condição da RN-16 é verificada contra o que está
/// gravado, então todos os casos passam pelo banco — validar em memória provaria a tela,
/// não a regra.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ProductPublicationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task CA_01_publicar_produto_completo_muda_a_situacao_para_no_ar()
    {
        var publication = CreatePublication();
        var product = await CreateProductAsync(withPhoto: true);

        var outcome = await publication.PublishAsync(product.Id);

        Assert.True(outcome!.Succeeded);
        Assert.Equal(ProductStatus.Published, await StatusOfAsync(product.Id));
    }

    [Fact]
    public async Task CA_01_produto_publicado_aparece_na_consulta_publica()
    {
        var publication = CreatePublication();
        var product = await CreateProductAsync(withPhoto: true);

        await publication.PublishAsync(product.Id);

        Assert.Contains(product.Id, await PubliclyVisibleIdsAsync());
    }

    [Fact]
    public async Task CA_02_publicar_sem_foto_e_recusado_e_o_produto_fica_em_rascunho()
    {
        var publication = CreatePublication();
        var product = await CreateProductAsync(withPhoto: false);

        var outcome = await publication.PublishAsync(product.Id);

        Assert.False(outcome!.Succeeded);
        Assert.Equal([PublicationRequirement.Photo], outcome.Missing);
        Assert.Equal(ProductStatus.Draft, await StatusOfAsync(product.Id));
    }

    [Fact]
    public async Task RN_16_resumo_e_descricao_vazios_nao_impedem_a_publicacao()
    {
        var publication = CreatePublication();
        var product = await CreateProductAsync(withPhoto: true);

        Assert.Null(product.Summary);
        Assert.Null(product.Description);

        var outcome = await publication.PublishAsync(product.Id);

        Assert.True(outcome!.Succeeded);
    }

    [Fact]
    public async Task CA_03_produto_em_rascunho_nao_aparece_na_consulta_publica()
    {
        var product = await CreateProductAsync(withPhoto: true);

        Assert.Equal(ProductStatus.Draft, await StatusOfAsync(product.Id));
        Assert.DoesNotContain(product.Id, await PubliclyVisibleIdsAsync());
    }

    [Fact]
    public async Task CA_29_despublicar_retira_o_produto_de_circulacao()
    {
        var publication = CreatePublication();
        var product = await CreateProductAsync(withPhoto: true);

        await publication.PublishAsync(product.Id);
        var outcome = await publication.WithdrawAsync(product.Id);

        Assert.True(outcome!.Succeeded);
        Assert.Equal(ProductStatus.Draft, await StatusOfAsync(product.Id));
        Assert.DoesNotContain(product.Id, await PubliclyVisibleIdsAsync());
    }

    /// <summary>
    /// Nome e preço são exigências da RN-16 como a foto é. O cadastro já os cobra
    /// (T-12), então este caso grava direto no banco: a pré-condição da publicação
    /// precisa valer sobre o que está lá, não confiar na validação de quem escreveu.
    /// </summary>
    [Fact]
    public async Task RN_16_publicar_sem_nome_e_sem_preco_e_recusado()
    {
        var publication = CreatePublication();
        var product = await CreateProductAsync(withPhoto: true, name: "   ", price: 0m);

        var outcome = await publication.PublishAsync(product.Id);

        Assert.False(outcome!.Succeeded);
        Assert.Equal(
            [PublicationRequirement.Name, PublicationRequirement.Price],
            outcome.Missing);
        Assert.Equal(ProductStatus.Draft, await StatusOfAsync(product.Id));
    }

    /// <summary>
    /// A tela precisa nomear a pendência antes de o dono tentar publicar
    /// (UI-05.publicacaoBloqueada), e é esta consulta que diz qual é.
    /// </summary>
    [Fact]
    public async Task RN_17_estado_declara_que_falta_a_foto_para_publicar()
    {
        var publication = CreatePublication();
        var product = await CreateProductAsync(withPhoto: false);

        var state = await publication.FindStateAsync(product.Id);

        Assert.False(state!.CanPublish);
        Assert.Equal([PublicationRequirement.Photo], state.Missing);
    }

    [Fact]
    public async Task Produto_inexistente_nao_e_recusa_de_publicacao()
    {
        var publication = CreatePublication();

        Assert.Null(await publication.PublishAsync(-1));
        Assert.Null(await publication.WithdrawAsync(-1));
        Assert.Null(await publication.FindStateAsync(-1));
    }

    private async Task<IReadOnlyList<int>> PubliclyVisibleIdsAsync()
    {
        await using var context = postgres.CreateContext();

        return await context.Products
            .AsNoTracking()
            .Published()
            .Select(product => product.Id)
            .ToListAsync();
    }

    private async Task<ProductStatus> StatusOfAsync(int productId)
    {
        await using var context = postgres.CreateContext();

        return await context.Products
            .AsNoTracking()
            .Where(product => product.Id == productId)
            .Select(product => product.Status)
            .SingleAsync();
    }

    private async Task<Product> CreateProductAsync(
        bool withPhoto,
        string name = "Impressora Multifuncional Epson L3250",
        decimal price = 1349.90m)
    {
        await using var context = postgres.CreateContext();

        var category = new Category { Name = $"Categoria {Guid.NewGuid():N}", Position = 1 };
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var product = new Product
        {
            Name = name,
            Price = price,
            Category = category,
            Position = 1,
            Photo = withPhoto ? Photo() : null
        };

        context.Products.Add(product);
        await context.SaveChangesAsync();

        return product;
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

    private ProductPublication CreatePublication() =>
        new(
            new ContextFactory(postgres.ConnectionString),
            TimeProvider.System,
            NullLogger<ProductPublication>.Instance);

    private sealed class ContextFactory(string connectionString) : IDbContextFactory<CatalogDbContext>
    {
        public CatalogDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<CatalogDbContext>().UseNpgsql(connectionString).Options);
    }
}
