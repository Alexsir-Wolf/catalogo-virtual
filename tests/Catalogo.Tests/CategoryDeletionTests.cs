using Catalogo.Data;
using Catalogo.Features.CatalogBuilder;
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

    /// <summary>
    /// CA-32 / RN-25.1: categoria **vazia** usada por catálogo não pode ser excluída, e a
    /// mensagem nomeia os catálogos. Sem isso, excluir uma categoria sem produtos deixaria um
    /// catálogo sem critério resolvível — o catálogo órfão da lacuna 8 da SPEC-UI.
    /// </summary>
    [Fact]
    public async Task CA_32_categoria_vazia_usada_por_catalogo_nao_e_excluida()
    {
        var category = await CreateCategoryAsync(CreateMaintenance());
        var catalogName = await SaveCatalogAsync(category.Id);

        var outcome = await CreateMaintenance().DeleteAsync(category.Id);

        Assert.Equal(CategoryFailure.UsedByCatalogs, outcome.Failure);
        Assert.Contains(catalogName, outcome.Catalogs);

        await using var context = postgres.CreateContext();
        Assert.True(await context.Categories.AnyAsync(saved => saved.Id == category.Id));
    }

    /// <summary>
    /// A recusa é **distinta** da de produtos: a causa e a saída são diferentes. Num caso o dono
    /// move ou exclui produtos, no outro edita o critério de um catálogo.
    /// </summary>
    [Fact]
    public async Task RN_25_1_a_recusa_por_catalogo_e_distinta_da_recusa_por_produtos()
    {
        var comCatalogo = await CreateCategoryAsync(CreateMaintenance());
        await SaveCatalogAsync(comCatalogo.Id);

        var comProdutos = await CreateCategoryAsync(CreateMaintenance());
        await AddProductsAsync(comProdutos.Id, ProductStatus.Published, count: 2);

        var maintenance = CreateMaintenance();

        var porCatalogo = await maintenance.DeleteAsync(comCatalogo.Id);
        var porProdutos = await maintenance.DeleteAsync(comProdutos.Id);

        Assert.Equal(CategoryFailure.UsedByCatalogs, porCatalogo.Failure);
        Assert.Equal(CategoryFailure.HasProducts, porProdutos.Failure);

        // Cada recusa carrega **o dado da sua causa**, e não o da outra.
        Assert.NotEmpty(porCatalogo.Catalogs);
        Assert.Equal(0, porCatalogo.BlockingProducts);
        Assert.Empty(porProdutos.Catalogs);
        Assert.Equal(2, porProdutos.BlockingProducts);
    }

    /// <summary>
    /// Todos os catálogos que retêm a categoria são nomeados, não apenas o primeiro: o dono
    /// precisa saber quantos critérios editar antes de conseguir excluir.
    /// </summary>
    [Fact]
    public async Task RN_25_1_todos_os_catalogos_que_retem_a_categoria_sao_nomeados()
    {
        var category = await CreateCategoryAsync(CreateMaintenance());
        var first = await SaveCatalogAsync(category.Id);
        var second = await SaveCatalogAsync(category.Id);

        var outcome = await CreateMaintenance().DeleteAsync(category.Id);

        Assert.Equal(2, outcome.Catalogs.Count);
        Assert.Contains(first, outcome.Catalogs);
        Assert.Contains(second, outcome.Catalogs);
    }

    [Fact]
    public async Task Categoria_sem_produtos_e_fora_de_catalogo_e_excluida()
    {
        var category = await CreateCategoryAsync(CreateMaintenance());

        var outcome = await CreateMaintenance().DeleteAsync(category.Id);

        Assert.True(outcome.Succeeded);

        await using var context = postgres.CreateContext();
        Assert.False(await context.Categories.AnyAsync(saved => saved.Id == category.Id));
    }

    /// <summary>
    /// Retirada do critério, a categoria volta a poder ser excluída — a retenção é sobre o
    /// estado atual, não uma marca permanente.
    /// </summary>
    [Fact]
    public async Task Retirada_do_criterio_a_categoria_volta_a_poder_ser_excluida()
    {
        var retida = await CreateCategoryAsync(CreateMaintenance());
        var outra = await CreateCategoryAsync(CreateMaintenance());

        var catalogs = new CatalogMaintenance(new ContextFactory(postgres.ConnectionString), TimeProvider.System);
        var name = $"Catálogo {Guid.NewGuid():N}";

        var saved = await catalogs.SaveAsync(new CatalogDraft
        {
            Name = name,
            CategoryIds = [retida.Id, outra.Id]
        });

        await catalogs.SaveAsync(new CatalogDraft
        {
            Id = saved.Id,
            Name = name,
            CategoryIds = [outra.Id]
        });

        Assert.True((await CreateMaintenance().DeleteAsync(retida.Id)).Succeeded);
    }

    private async Task<string> SaveCatalogAsync(int categoryId)
    {
        var name = $"Catálogo {Guid.NewGuid():N}";

        var outcome = await new CatalogMaintenance(new ContextFactory(postgres.ConnectionString), TimeProvider.System)
            .SaveAsync(new CatalogDraft { Name = name, CategoryIds = [categoryId] });

        Assert.True(outcome.Succeeded);

        return name;
    }

    private CategoryMaintenance CreateMaintenance() =>
        new(new ContextFactory(postgres.ConnectionString), TestCache.Silent());

    private sealed class ContextFactory(string connectionString) : IDbContextFactory<CatalogDbContext>
    {
        public CatalogDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<CatalogDbContext>().UseNpgsql(connectionString).Options);
    }
}
