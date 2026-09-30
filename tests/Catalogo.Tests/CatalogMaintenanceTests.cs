using Catalogo.Data;
using Catalogo.Features.CatalogBuilder;
using Catalogo.Features.Categories;
using Catalogo.Features.Products;
using Microsoft.EntityFrameworkCore;

namespace Catalogo.Tests;

/// <summary>
/// Catálogo como filtro salvo (T-22). A decisão que estes casos protegem é uma **ausência**:
/// a lista de produtos nunca é persistida (RN-29, ADR-014). Persisti-la "para performance"
/// traria de volta exatamente a dor que o projeto veio resolver — o catálogo que envelhece
/// sozinho.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CatalogMaintenanceTests(PostgresFixture postgres)
{
    /// <summary>
    /// CA-12: salvar grava nome e categorias — e **nada além disso**.
    /// </summary>
    [Fact]
    public async Task CA_12_salvar_persiste_o_nome_e_as_categorias()
    {
        var maintenance = CreateMaintenance();
        var tintas = await CreateCategoryAsync("Tintas & Suprimentos");
        var redes = await CreateCategoryAsync("Redes & Cabeamento");

        var outcome = await maintenance.SaveAsync(new CatalogDraft
        {
            Name = NameFor("Consumíveis"),
            CategoryIds = [tintas.Id, redes.Id]
        });

        Assert.True(outcome.Succeeded);

        var saved = await maintenance.FindAsync(outcome.Id!.Value);

        Assert.Equal(2, saved!.CategoryIds.Count);
        Assert.Contains(tintas.Id, saved.CategoryIds);
        Assert.Contains(redes.Id, saved.CategoryIds);
    }

    /// <summary>
    /// RN-29, verificada **no esquema** e não no comportamento: se algum dia alguém criar uma
    /// tabela de itens de catálogo, este caso cai. É a forma de proteger uma decisão de
    /// arquitetura contra a boa intenção de quem vier depois.
    /// </summary>
    [Fact]
    public async Task RN_29_nenhuma_tabela_de_itens_de_catalogo_existe_no_esquema()
    {
        await using var context = postgres.CreateContext();

        var tables = context.Model.GetEntityTypes()
            .Select(entity => entity.GetTableName())
            .Where(name => name is not null)
            .Select(name => name!.ToLowerInvariant())
            .ToList();

        Assert.Contains("catalogs", tables);
        Assert.Contains("catalogcategory", tables);

        // O catálogo se liga a **categorias**, nunca a produtos.
        Assert.DoesNotContain(tables, name => name.Contains("catalogproduct"));
        Assert.DoesNotContain(tables, name => name.Contains("catalogitem"));

        var catalogEntity = context.Model.FindEntityType(typeof(Catalog))!;

        Assert.DoesNotContain(
            catalogEntity.GetNavigations(),
            navigation => navigation.TargetEntityType.ClrType == typeof(Product));
    }

    [Fact]
    public async Task CA_13_catalogo_sem_categoria_e_recusado()
    {
        var outcome = await CreateMaintenance().SaveAsync(new CatalogDraft
        {
            Name = NameFor("Sem critério"),
            CategoryIds = []
        });

        Assert.Equal(CatalogFailure.NoCategorySelected, outcome.Failure);
    }

    [Fact]
    public async Task RN_27_nome_vazio_e_recusado()
    {
        var category = await CreateCategoryAsync();

        var outcome = await CreateMaintenance().SaveAsync(new CatalogDraft
        {
            Name = "   ",
            CategoryIds = [category.Id]
        });

        Assert.Equal(CatalogFailure.NameRequired, outcome.Failure);
    }

    [Fact]
    public async Task RN_27_nome_duplicado_e_recusado()
    {
        var maintenance = CreateMaintenance();
        var category = await CreateCategoryAsync();
        var name = NameFor("Repetido");

        await maintenance.SaveAsync(new CatalogDraft { Name = name, CategoryIds = [category.Id] });

        var outcome = await maintenance.SaveAsync(
            new CatalogDraft { Name = name, CategoryIds = [category.Id] });

        Assert.Equal(CatalogFailure.NameAlreadyInUse, outcome.Failure);
    }

    /// <summary>
    /// Mesma razão da RN-23 nas categorias: "Consumíveis" e "consumíveis" seriam dois
    /// catálogos com o mesmo nome na tela do dono.
    /// </summary>
    [Fact]
    public async Task RN_27_nome_que_difere_apenas_na_caixa_e_recusado()
    {
        var maintenance = CreateMaintenance();
        var category = await CreateCategoryAsync();
        var name = NameFor("Caixa");

        await maintenance.SaveAsync(new CatalogDraft { Name = name, CategoryIds = [category.Id] });

        var outcome = await maintenance.SaveAsync(new CatalogDraft
        {
            Name = name.ToUpperInvariant(),
            CategoryIds = [category.Id]
        });

        Assert.Equal(CatalogFailure.NameAlreadyInUse, outcome.Failure);
    }

    /// <summary>
    /// CA-20: excluir o catálogo remove **o filtro**, e nenhum produto é afetado. O catálogo
    /// nunca foi dono de produto algum — é o que torna a exclusão barata e sem consequência.
    /// </summary>
    [Fact]
    public async Task CA_20_excluir_catalogo_nao_afeta_produto_algum()
    {
        var maintenance = CreateMaintenance();
        var category = await CreateCategoryAsync();
        var productId = await CreateProductAsync(category.Id);

        var outcome = await maintenance.SaveAsync(new CatalogDraft
        {
            Name = NameFor("Descartável"),
            CategoryIds = [category.Id]
        });

        Assert.True(await maintenance.DeleteAsync(outcome.Id!.Value));

        await using var context = postgres.CreateContext();

        Assert.Null(await maintenance.FindAsync(outcome.Id.Value));
        Assert.True(await context.Products.AnyAsync(product => product.Id == productId));
        Assert.True(await context.Categories.AnyAsync(saved => saved.Id == category.Id));
    }

    /// <summary>
    /// RN-30: a contagem é resolvida **na hora**, e só conta produto No ar — quem está em
    /// Rascunho não sai no documento.
    /// </summary>
    [Fact]
    public async Task RN_30_a_contagem_e_resolvida_agora_e_ignora_rascunho()
    {
        var maintenance = CreateMaintenance();
        var category = await CreateCategoryAsync();
        await CreateProductAsync(category.Id, ProductStatus.Published);
        await CreateProductAsync(category.Id, ProductStatus.Published);
        await CreateProductAsync(category.Id, ProductStatus.Draft);

        var name = NameFor("Contagem");
        await maintenance.SaveAsync(new CatalogDraft { Name = name, CategoryIds = [category.Id] });

        var listed = Single(await maintenance.ListAsync(), name);

        Assert.Equal(2, listed.PublishedProducts);

        // Publicar o terceiro muda a contagem **sem tocar no catálogo**: é o ponto da RN-30.
        await PublishAllAsync(category.Id);

        Assert.Equal(3, Single(await maintenance.ListAsync(), name).PublishedProducts);
    }

    [Fact]
    public async Task RN_33_catalogo_novo_nasce_marcado_como_nunca_gerado()
    {
        var maintenance = CreateMaintenance();
        var category = await CreateCategoryAsync();
        var name = NameFor("Recém-criado");

        await maintenance.SaveAsync(new CatalogDraft { Name = name, CategoryIds = [category.Id] });

        Assert.False(Single(await maintenance.ListAsync(), name).WasGenerated);
    }

    [Fact]
    public async Task RN_33_a_geracao_e_registrada_com_a_data()
    {
        var maintenance = CreateMaintenance();
        var category = await CreateCategoryAsync();
        var name = NameFor("Gerado");
        var outcome = await maintenance.SaveAsync(
            new CatalogDraft { Name = name, CategoryIds = [category.Id] });

        var moment = new DateTimeOffset(2026, 9, 29, 14, 30, 0, TimeSpan.Zero);
        await maintenance.MarkGeneratedAsync(outcome.Id!.Value, moment);

        var listed = Single(await maintenance.ListAsync(), name);

        Assert.True(listed.WasGenerated);
        Assert.Equal(moment, listed.LastGeneratedAt);
    }

    /// <summary>
    /// Editar o critério **substitui** a seleção. Mesclar faria o catálogo crescer sozinho a
    /// cada salvamento, e o dono que remove uma categoria a veria voltar.
    /// </summary>
    [Fact]
    public async Task Editar_o_criterio_substitui_as_categorias_em_vez_de_acumular()
    {
        var maintenance = CreateMaintenance();
        var first = await CreateCategoryAsync();
        var second = await CreateCategoryAsync();

        var outcome = await maintenance.SaveAsync(new CatalogDraft
        {
            Name = NameFor("Trocado"),
            CategoryIds = [first.Id, second.Id]
        });

        await maintenance.SaveAsync(new CatalogDraft
        {
            Id = outcome.Id,
            Name = NameFor("Trocado"),
            CategoryIds = [second.Id]
        });

        var saved = await maintenance.FindAsync(outcome.Id!.Value);

        Assert.Equal([second.Id], saved!.CategoryIds);
    }

    [Fact]
    public async Task Excluir_catalogo_inexistente_devolve_falso_sem_lancar()
    {
        Assert.False(await CreateMaintenance().DeleteAsync(987654));
    }

    private static CatalogSummary Single(IReadOnlyList<CatalogSummary> catalogs, string name) =>
        catalogs.Single(catalog => catalog.Name == name);

    private static string NameFor(string prefix) => $"{prefix} {Guid.NewGuid():N}";

    private async Task PublishAllAsync(int categoryId)
    {
        await using var context = postgres.CreateContext();

        await context.Products
            .Where(product => product.CategoryId == categoryId)
            .ExecuteUpdateAsync(update =>
                update.SetProperty(product => product.Status, ProductStatus.Published));
    }

    private async Task<int> CreateProductAsync(
        int categoryId,
        ProductStatus status = ProductStatus.Published)
    {
        await using var context = postgres.CreateContext();

        var product = new Product
        {
            Name = $"Produto {Guid.NewGuid():N}",
            Price = 99.90m,
            CategoryId = categoryId,
            Position = 1,
            Status = status
        };

        context.Products.Add(product);
        await context.SaveChangesAsync();

        return product.Id;
    }

    private async Task<Category> CreateCategoryAsync(string? name = null)
    {
        await using var context = postgres.CreateContext();

        var category = new Category
        {
            Name = $"{name ?? "Categoria"} {Guid.NewGuid():N}",
            Position = 1
        };

        context.Categories.Add(category);
        await context.SaveChangesAsync();

        return category;
    }

    private CatalogMaintenance CreateMaintenance() =>
        new(new ContextFactory(postgres.ConnectionString), TimeProvider.System);

    private sealed class ContextFactory(string connectionString) : IDbContextFactory<CatalogDbContext>
    {
        public CatalogDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<CatalogDbContext>().UseNpgsql(connectionString).Options);
    }
}
