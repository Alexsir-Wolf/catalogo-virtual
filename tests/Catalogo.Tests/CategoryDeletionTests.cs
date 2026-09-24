using Catalogo.Data;
using Catalogo.Features.Categories;
using Catalogo.Features.Products;
using Microsoft.EntityFrameworkCore;

namespace Catalogo.Tests;

[Collection(PostgresCollection.Name)]
public sealed class CategoryDeletionTests(PostgresFixture postgres)
{
    [Fact]
    public async Task CA_11_categoria_com_produto_publicado_nao_e_excluida()
    {
        var maintenance = CreateMaintenance();
        var category = await CreateCategoryAsync(maintenance);
        await AddProductsAsync(category.Id, ProductStatus.Published, count: 5);

        var outcome = await maintenance.DeleteAsync(category.Id);

        Assert.Equal(CategoryFailure.HasProducts, outcome.Failure);
        Assert.Equal(5, outcome.BlockingProducts);
    }

    [Fact]
    public async Task Produto_em_rascunho_tambem_impede_a_exclusao()
    {
        var maintenance = CreateMaintenance();
        var category = await CreateCategoryAsync(maintenance);
        await AddProductsAsync(category.Id, ProductStatus.Draft, count: 1);

        var outcome = await maintenance.DeleteAsync(category.Id);

        Assert.Equal(CategoryFailure.HasProducts, outcome.Failure);
        Assert.Equal(1, outcome.BlockingProducts);
    }

    [Fact]
    public async Task Categoria_sem_produtos_e_excluida()
    {
        var maintenance = CreateMaintenance();
        var category = await CreateCategoryAsync(maintenance);

        var outcome = await maintenance.DeleteAsync(category.Id);

        Assert.True(outcome.Succeeded);
        Assert.DoesNotContain(await maintenance.ListAsync(), candidate => candidate.Id == category.Id);
    }

    private static async Task<Category> CreateCategoryAsync(CategoryMaintenance maintenance)
    {
        var name = $"Redes {Guid.NewGuid():N}";
        await maintenance.CreateAsync(name);

        return (await maintenance.ListAsync()).Single(category => category.Name == name);
    }

    private async Task AddProductsAsync(int categoryId, ProductStatus status, int count)
    {
        await using var context = postgres.CreateContext();

        for (var index = 0; index < count; index++)
        {
            context.Products.Add(new Product
            {
                Name = $"Produto {Guid.NewGuid():N}",
                Price = 10m,
                CategoryId = categoryId,
                Position = index + 1,
                Status = status
            });
        }

        await context.SaveChangesAsync();
    }

    private CategoryMaintenance CreateMaintenance() =>
        new(new ContextFactory(postgres.ConnectionString));

    private sealed class ContextFactory(string connectionString) : IDbContextFactory<CatalogDbContext>
    {
        public CatalogDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<CatalogDbContext>().UseNpgsql(connectionString).Options);
    }
}
