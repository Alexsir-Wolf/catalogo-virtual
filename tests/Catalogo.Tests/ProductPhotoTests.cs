using Catalogo.Data;
using Catalogo.Features.Categories;
using Catalogo.Features.Media;
using Catalogo.Features.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Catalogo.Tests;

[Collection(PostgresCollection.Name)]
public sealed class ProductPhotoTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Enviar_foto_associa_as_quatro_derivadas_ao_produto()
    {
        var maintenance = CreateMaintenance();
        var productId = await CreateProductAsync(maintenance);

        var attached = await maintenance.AttachPhotoAsync(productId, PhotoNamed("primeira"));

        Assert.NotNull(attached);

        var stored = await maintenance.FindPhotoAsync(productId);

        Assert.Equal("primeira-thumbnail.webp", stored!.ThumbnailFileName);
        Assert.Equal("primeira-card.webp", stored.CardFileName);
        Assert.Equal("primeira-large.webp", stored.LargeFileName);
        Assert.Equal("primeira-print.jpg", stored.PrintFileName);
    }

    /// <summary>
    /// RN-09: uma foto por produto, não galeria. A segunda substitui a primeira em vez
    /// de somar, e é isso que o esquema também impõe — as derivadas são colunas do
    /// próprio produto.
    /// </summary>
    [Fact]
    public async Task CA_07_trocar_a_foto_gera_nomes_novos_e_atualiza_a_referencia()
    {
        var maintenance = CreateMaintenance();
        var productId = await CreateProductAsync(maintenance);

        await maintenance.AttachPhotoAsync(productId, PhotoNamed("antiga"));
        var before = await maintenance.FindPhotoAsync(productId);

        await maintenance.AttachPhotoAsync(productId, PhotoNamed("nova"));
        var after = await maintenance.FindPhotoAsync(productId);

        Assert.Equal("antiga-card.webp", before!.CardFileName);
        Assert.Equal("nova-card.webp", after!.CardFileName);

        // Nenhum nome sobrevive à troca: a vitrine deixa de apontar para a foto anterior.
        Assert.Empty(Names(before).Intersect(Names(after)));
    }

    [Fact]
    public async Task RN_09_produto_guarda_uma_unica_foto()
    {
        var maintenance = CreateMaintenance();
        var productId = await CreateProductAsync(maintenance);

        await maintenance.AttachPhotoAsync(productId, PhotoNamed("uma"));
        await maintenance.AttachPhotoAsync(productId, PhotoNamed("outra"));

        await using var context = postgres.CreateContext();
        var product = await context.Products.AsNoTracking().SingleAsync(p => p.Id == productId);

        Assert.Equal("outra-card.webp", product.Photo!.CardFileName);
    }

    [Fact]
    public async Task Produto_inexistente_nao_recebe_foto()
    {
        var maintenance = CreateMaintenance();

        var attached = await maintenance.AttachPhotoAsync(int.MaxValue, PhotoNamed("orfa"));

        Assert.Null(attached);
    }

    [Fact]
    public async Task Produto_recem_criado_nao_tem_foto()
    {
        var maintenance = CreateMaintenance();
        var productId = await CreateProductAsync(maintenance);

        var photo = await maintenance.FindPhotoAsync(productId);

        Assert.Null(photo);
    }

    /// <summary>
    /// Os nomes seguem o que o processador produz (<see cref="ImageProcessor.ObjectNameFor"/>),
    /// para o teste falhar se a convenção mudar de um lado só.
    /// </summary>
    private static ProductPhoto PhotoNamed(string root) => new()
    {
        OriginalFileName = $"{root}-original",
        ThumbnailFileName = NameFor(root, ImageDerivative.Thumbnail),
        CardFileName = NameFor(root, ImageDerivative.Card),
        LargeFileName = NameFor(root, ImageDerivative.Large),
        PrintFileName = NameFor(root, ImageDerivative.Print)
    };

    private static string NameFor(string root, ImageDerivative derivative) =>
        ImageProcessor.ObjectNameFor(
            root,
            DerivativeSpecifications.All.Single(spec => spec.Derivative == derivative));

    private static IEnumerable<string> Names(ProductPhoto photo) =>
    [
        photo.OriginalFileName,
        photo.ThumbnailFileName,
        photo.CardFileName,
        photo.LargeFileName,
        photo.PrintFileName
    ];

    private async Task<int> CreateProductAsync(ProductMaintenance maintenance)
    {
        await using var context = postgres.CreateContext();

        var category = new Category { Name = $"Categoria {Guid.NewGuid():N}", Position = 1 };
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var outcome = await maintenance.SaveAsync(new ProductDraft
        {
            Name = "Monitor VXPro 19",
            Price = 599.90m,
            CategoryId = category.Id
        });

        return outcome.Id!.Value;
    }

    private ProductMaintenance CreateMaintenance() =>
        new(new ContextFactory(postgres.ConnectionString), TestCache.Silent(), TimeProvider.System, NullLogger<ProductMaintenance>.Instance);

    private sealed class ContextFactory(string connectionString) : IDbContextFactory<CatalogDbContext>
    {
        public CatalogDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<CatalogDbContext>().UseNpgsql(connectionString).Options);
    }
}
