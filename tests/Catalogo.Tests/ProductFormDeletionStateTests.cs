using System.Reflection;
using Catalogo.Data;
using Catalogo.Features.Categories;
using Catalogo.Features.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Catalogo.Tests;

/// <summary>
/// O ciclo de vida da confirmação de exclusão em <c>ProductForm</c> (R-01 de
/// `REVIEW-T-16-2026-09-30`).
///
/// As duas rotas do formulário são do mesmo componente e o roteador reaproveita a instância, então
/// a confirmação aberta para um produto podia continuar desenhada depois da troca — e o clique em
/// "Excluir definitivamente" apagava outro produto, ou estourava com `Id` nulo e derrubava o
/// circuito. O projeto não tem bUnit, mas o que quebra aqui é **estado de componente**, e estado se
/// afirma sem renderizador: os casos abaixo empurram o parâmetro e chamam o ciclo de vida
/// diretamente, que é o mesmo caminho que o roteador percorre.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ProductFormDeletionStateTests(PostgresFixture postgres)
{
    /// <summary>
    /// O caminho alcançável pela navegação lateral: com a confirmação aberta, "Novo produto" leva
    /// à rota sem id, e o formulário em branco não pode herdar a confirmação do produto anterior.
    /// </summary>
    [Fact]
    public async Task Novo_produto_descarta_a_confirmacao_aberta()
    {
        var form = NewForm();
        var productId = await CreateProductAsync();

        await SetParametersAsync(form, productId);
        await AskDeletionAsync(form);

        await SetParametersAsync(form, null);

        AssertNoPendingDeletion(form);
    }

    /// <summary>
    /// O caminho por endereço digitado, favorito ou histórico: de uma edição direto para outra,
    /// <c>Reset</c> nem chega a rodar, e era este o caso em que a confirmação do produto anterior
    /// passava a apagar o atual.
    /// </summary>
    [Fact]
    public async Task Trocar_de_produto_descarta_a_confirmacao_aberta()
    {
        var form = NewForm();
        var primeiro = await CreateProductAsync();
        var segundo = await CreateProductAsync();

        await SetParametersAsync(form, primeiro);
        await AskDeletionAsync(form);

        await SetParametersAsync(form, segundo);

        AssertNoPendingDeletion(form);
    }

    /// <summary>
    /// A confirmação precisa existir antes de o caso afirmar que ela sumiu — senão a asserção
    /// passa com o defeito de volta.
    /// </summary>
    [Fact]
    public async Task Pedir_a_exclusao_abre_a_confirmacao_nomeando_o_produto()
    {
        var form = NewForm();
        var productId = await CreateProductAsync();

        await SetParametersAsync(form, productId);
        await AskDeletionAsync(form);

        Assert.True(Field<bool>(form, "confirmingDeletion"));
        Assert.Equal(ProductName, Field<string?>(form, "confirmedName"));
    }

    private static void AssertNoPendingDeletion(ProductForm form)
    {
        Assert.False(Field<bool>(form, "confirmingDeletion"));
        Assert.False(Field<bool>(form, "deleting"));
        Assert.False(Field<bool>(form, "deletionAffectsStorefront"));
        Assert.Null(Field<string?>(form, "confirmedName"));
        Assert.Null(Field<string?>(form, "deletionError"));
    }

    private ProductForm NewForm()
    {
        var form = new ProductForm();

        Inject(form, "Maintenance", new ProductMaintenance(
            Factory(),
            TestCache.Silent(),
            TimeProvider.System,
            NullLogger<ProductMaintenance>.Instance));

        Inject(form, "Publication", new ProductPublication(
            Factory(),
            TimeProvider.System,
            TestCache.Silent(),
            NullLogger<ProductPublication>.Instance));

        return form;
    }

    /// <summary>Percorre o mesmo ciclo de vida que o roteador percorre ao entrar numa rota.</summary>
    private static Task SetParametersAsync(ProductForm form, int? id)
    {
        // BL0005 avisa sobre escrever parâmetro de fora do componente, que é exatamente o que o
        // roteador faz ao entrar na rota — e é o caminho que estes casos precisam percorrer. Sem
        // renderizador não há outra forma de chegar nele.
#pragma warning disable BL0005
        form.Id = id;
#pragma warning restore BL0005

        return Invoke(form, "OnParametersSetAsync");
    }

    private static Task AskDeletionAsync(ProductForm form) => Invoke(form, "AskDeletionAsync");

    private static Task Invoke(ProductForm form, string method) =>
        (Task)typeof(ProductForm)
            .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(form, null)!;

    private static void Inject(ProductForm form, string property, object service) =>
        typeof(ProductForm)
            .GetProperty(property, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(form, service);

    private static T Field<T>(ProductForm form, string name) =>
        (T)typeof(ProductForm)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(form)!;

    private const string ProductName = "Teclado Mecânico K70";

    private async Task<int> CreateProductAsync()
    {
        await using var context = postgres.CreateContext();

        var category = new Category { Name = $"Categoria {Guid.NewGuid():N}", Position = 1 };
        context.Categories.Add(category);

        var product = new Product
        {
            Name = ProductName,
            Price = 349.90m,
            Category = category,
            Status = ProductStatus.Draft,
            Position = 1
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
