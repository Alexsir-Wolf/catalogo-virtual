using Catalogo.Data;
using Catalogo.Features.Categories;
using Catalogo.Features.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Catalogo.Tests;

/// <summary>
/// Os pontos de invalidação que os casos de ponta a ponta **não** alcançam (T-21, ADR-008).
///
/// `StorefrontCacheTests` prova pelo HTTP o que a vitrine mostra: preço, publicação, exclusão,
/// ordem de produto, nome de categoria e contato. Sobram quatro escritas cuja invalidação nenhum
/// caso observava — trocar a foto, mover uma categoria, excluí-la e criar uma nova. As quatro
/// passavam com a linha de invalidação **removida**, que é a definição de teste que não morde.
///
/// Aqui a afirmação é direta, sobre a tag evictada, e por isso cada caso é uma linha: não há como
/// um destes quatro pontos perder a invalidação em silêncio.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class StorefrontInvalidationPointsTests(PostgresFixture postgres)
{
    /// <summary>
    /// A foto é o que a vitrine mostra em primeiro lugar — trocá-la sem invalidar deixa a listagem
    /// com a imagem antiga, que é o defeito mais visível possível e o mais fácil de não testar.
    /// </summary>
    [Fact]
    public async Task Trocar_a_foto_invalida_a_vitrine()
    {
        var (invalidation, store) = TestCache.Recording();
        var categoryId = (await CreateCategoryAsync()).Id;
        var productId = await CreateProductAsync(categoryId);

        var maintenance = new ProductMaintenance(
            Factory(),
            invalidation,
            NullLogger<ProductMaintenance>.Instance);

        await maintenance.AttachPhotoAsync(productId, PhotoFor("nova"));

        Assert.Equal([Catalogo.Features.Storefront.StorefrontCache.Tag], store.Evicted);
    }

    /// <summary>
    /// Mover categoria muda a ordem das seções da vitrine, que é a mesma ordem impressa (RN-21).
    /// </summary>
    [Fact]
    public async Task Mover_categoria_invalida_a_vitrine()
    {
        var (invalidation, store) = TestCache.Recording();
        var maintenance = new CategoryMaintenance(Factory(), invalidation);

        var minha = await CreateCategoryAsync();
        await CreateCategoryAsync();

        // A direção é escolhida pela posição real na lista, e não presumida: o banco desta coleção
        // é compartilhado, então a categoria recém-criada pode ser a última — e mover a última
        // para baixo não faz nada, o que deixaria o caso passando sem exercitar a escrita.
        var ordered = await maintenance.ListAsync();
        var index = ordered.Select(category => category.Id).ToList().IndexOf(minha.Id);
        var direction = index == ordered.Count - 1 ? MoveDirection.Up : MoveDirection.Down;

        await maintenance.MoveAsync(minha.Id, direction);

        Assert.Equal([Catalogo.Features.Storefront.StorefrontCache.Tag], store.Evicted);
    }

    /// <summary>
    /// Excluir categoria tira uma faceta do filtro da vitrine. Sem invalidar, o visitante clica
    /// numa categoria que não existe mais.
    /// </summary>
    [Fact]
    public async Task Excluir_categoria_invalida_a_vitrine()
    {
        var (invalidation, store) = TestCache.Recording();
        var maintenance = new CategoryMaintenance(Factory(), invalidation);

        var category = await CreateCategoryAsync();

        var outcome = await maintenance.DeleteAsync(category.Id);

        Assert.True(outcome.Succeeded);
        Assert.Equal([Catalogo.Features.Storefront.StorefrontCache.Tag], store.Evicted);
    }

    /// <summary>
    /// Criar categoria acrescenta uma faceta ao filtro — e a lista de facetas é parte do HTML
    /// cacheado, não uma consulta feita à parte.
    /// </summary>
    [Fact]
    public async Task Criar_categoria_invalida_a_vitrine()
    {
        var (invalidation, store) = TestCache.Recording();
        var maintenance = new CategoryMaintenance(Factory(), invalidation);

        var outcome = await maintenance.CreateAsync($"Categoria {Guid.NewGuid():N}");

        Assert.True(outcome.Succeeded);
        Assert.Equal([Catalogo.Features.Storefront.StorefrontCache.Tag], store.Evicted);
    }

    private static ProductPhoto PhotoFor(string prefix) => new()
    {
        OriginalFileName = $"{prefix}/original.webp",
        ThumbnailFileName = $"{prefix}/miniatura.webp",
        CardFileName = $"{prefix}/cartao.webp",
        LargeFileName = $"{prefix}/grande.webp",
        PrintFileName = $"{prefix}/impressao.webp"
    };

    private async Task<Category> CreateCategoryAsync()
    {
        await using var context = postgres.CreateContext();

        var category = new Category { Name = $"Categoria {Guid.NewGuid():N}", Position = 1 };

        context.Categories.Add(category);
        await context.SaveChangesAsync();

        return category;
    }

    private async Task<int> CreateProductAsync(int categoryId)
    {
        await using var context = postgres.CreateContext();

        var product = new Product
        {
            Name = $"Produto {Guid.NewGuid():N}",
            Summary = "Resumo",
            Price = 99.90m,
            CategoryId = categoryId,
            Position = 1,
            Status = ProductStatus.Draft
        };

        context.Products.Add(product);
        await context.SaveChangesAsync();

        return product.Id;
    }

    private ContextFactory Factory() => new(postgres.ConnectionString);

    private sealed class ContextFactory(string connectionString) : IDbContextFactory<CatalogDbContext>
    {
        public CatalogDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<CatalogDbContext>().UseNpgsql(connectionString).Options);
    }
}
