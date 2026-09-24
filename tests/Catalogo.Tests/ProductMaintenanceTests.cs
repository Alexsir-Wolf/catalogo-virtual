using Catalogo.Data;
using Catalogo.Features.Categories;
using Catalogo.Features.Products;
using Microsoft.EntityFrameworkCore;

namespace Catalogo.Tests;

[Collection(PostgresCollection.Name)]
public sealed class ProductMaintenanceTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Produto_com_nome_preco_e_categoria_persiste_com_todos_os_campos()
    {
        var maintenance = CreateMaintenance();
        var category = await CreateCategoryAsync();

        var outcome = await maintenance.SaveAsync(new ProductDraft
        {
            Name = "Impressora Multifuncional Epson L3250",
            Summary = "Tanque de tinta, Wi-Fi Direct, impressão e cópia",
            Description = "Especificação longa que só aparece na página de detalhe da vitrine.",
            Price = 1349.90m,
            PriceLabel = PriceLabel.PricePerUnit,
            CategoryId = category.Id
        });

        Assert.True(outcome.Succeeded);

        var saved = await maintenance.FindAsync(outcome.Id!.Value);

        Assert.Equal("Impressora Multifuncional Epson L3250", saved!.Name);
        Assert.Equal("Tanque de tinta, Wi-Fi Direct, impressão e cópia", saved.Summary);
        Assert.StartsWith("Especificação longa", saved.Description);
        Assert.Equal(1349.90m, saved.Price);
        Assert.Equal(PriceLabel.PricePerUnit, saved.PriceLabel);
        Assert.Equal(category.Id, saved.CategoryId);
    }

    [Fact]
    public async Task RN_14_produto_nasce_em_rascunho()
    {
        var maintenance = CreateMaintenance();
        var category = await CreateCategoryAsync();

        var outcome = await maintenance.SaveAsync(ValidDraft(category.Id));

        await using var context = postgres.CreateContext();
        var product = await context.Products.SingleAsync(p => p.Id == outcome.Id);

        Assert.Equal(ProductStatus.Draft, product.Status);
    }

    [Fact]
    public async Task RN_02_nome_vazio_e_recusado()
    {
        var maintenance = CreateMaintenance();
        var category = await CreateCategoryAsync();

        var outcome = await maintenance.SaveAsync(ValidDraft(category.Id) with { Name = "   " });

        Assert.Contains(ProductField.Name, outcome.Errors);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-0.01)]
    public async Task RN_06_preco_zero_ou_negativo_e_recusado(decimal price)
    {
        var maintenance = CreateMaintenance();
        var category = await CreateCategoryAsync();

        var outcome = await maintenance.SaveAsync(ValidDraft(category.Id) with { Price = price });

        Assert.Contains(ProductField.Price, outcome.Errors);
    }

    [Fact]
    public async Task RN_06_preco_ausente_e_recusado()
    {
        var maintenance = CreateMaintenance();
        var category = await CreateCategoryAsync();

        var outcome = await maintenance.SaveAsync(ValidDraft(category.Id) with { Price = null });

        Assert.Contains(ProductField.Price, outcome.Errors);
    }

    [Fact]
    public async Task RN_08_categoria_ausente_e_recusada()
    {
        var maintenance = CreateMaintenance();

        var outcome = await maintenance.SaveAsync(ValidDraft(categoryId: null));

        Assert.Contains(ProductField.Category, outcome.Errors);
    }

    [Fact]
    public async Task RN_03_resumo_acima_do_limite_e_recusado()
    {
        var maintenance = CreateMaintenance();
        var category = await CreateCategoryAsync();

        var summary = new string('a', Product.SummaryMaxLength + 1);
        var outcome = await maintenance.SaveAsync(ValidDraft(category.Id) with { Summary = summary });

        Assert.Contains(ProductField.Summary, outcome.Errors);
    }

    [Fact]
    public async Task RN_03_resumo_no_limite_exato_e_aceito()
    {
        var maintenance = CreateMaintenance();
        var category = await CreateCategoryAsync();

        var summary = new string('a', Product.SummaryMaxLength);
        var outcome = await maintenance.SaveAsync(ValidDraft(category.Id) with { Summary = summary });

        Assert.True(outcome.Succeeded);
    }

    [Fact]
    public async Task RN_05_descricao_aceita_texto_longo_sem_limite()
    {
        var maintenance = CreateMaintenance();
        var category = await CreateCategoryAsync();

        var description = string.Join(' ', Enumerable.Repeat("especificação", 400));
        var outcome = await maintenance.SaveAsync(ValidDraft(category.Id) with { Description = description });

        Assert.True(outcome.Succeeded);

        var saved = await maintenance.FindAsync(outcome.Id!.Value);

        Assert.Equal(description, saved!.Description);
    }

    /// <summary>
    /// Resumo em branco é ausência, não string vazia: a célula do PDF decide exibir só
    /// nome e preço pela ausência dele (RN-04).
    /// </summary>
    [Fact]
    public async Task RN_04_resumo_em_branco_e_gravado_como_ausente()
    {
        var maintenance = CreateMaintenance();
        var category = await CreateCategoryAsync();

        var outcome = await maintenance.SaveAsync(ValidDraft(category.Id) with { Summary = "   " });
        var saved = await maintenance.FindAsync(outcome.Id!.Value);

        Assert.Null(saved!.Summary);
    }

    [Fact]
    public async Task Edicao_altera_o_produto_existente_sem_criar_outro()
    {
        var maintenance = CreateMaintenance();
        var category = await CreateCategoryAsync();

        var created = await maintenance.SaveAsync(ValidDraft(category.Id));
        var draft = await maintenance.FindAsync(created.Id!.Value);

        var updated = await maintenance.SaveAsync(draft! with { Name = "Nome corrigido" });

        Assert.Equal(created.Id, updated.Id);

        var saved = await maintenance.FindAsync(updated.Id!.Value);

        Assert.Equal("Nome corrigido", saved!.Name);
    }

    [Fact]
    public async Task Varios_erros_sao_devolvidos_de_uma_vez()
    {
        var maintenance = CreateMaintenance();

        var outcome = await maintenance.SaveAsync(new ProductDraft());

        Assert.Contains(ProductField.Name, outcome.Errors);
        Assert.Contains(ProductField.Price, outcome.Errors);
        Assert.Contains(ProductField.Category, outcome.Errors);
    }

    private static ProductDraft ValidDraft(int? categoryId) => new()
    {
        Name = "Produto de teste",
        Price = 10.50m,
        CategoryId = categoryId
    };

    private async Task<Category> CreateCategoryAsync()
    {
        await using var context = postgres.CreateContext();

        var category = new Category { Name = $"Categoria {Guid.NewGuid():N}", Position = 1 };
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        return category;
    }

    private ProductMaintenance CreateMaintenance() =>
        new(new ContextFactory(postgres.ConnectionString));

    private sealed class ContextFactory(string connectionString) : IDbContextFactory<CatalogDbContext>
    {
        public CatalogDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<CatalogDbContext>().UseNpgsql(connectionString).Options);
    }
}
