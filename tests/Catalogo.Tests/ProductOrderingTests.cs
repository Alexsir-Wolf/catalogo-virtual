using Catalogo.Data;
using Catalogo.Features.Categories;
using Catalogo.Features.Products;
using Microsoft.EntityFrameworkCore;

namespace Catalogo.Tests;

/// <summary>
/// Ordenação manual do produto dentro da categoria (T-15). A ordem é curadoria do dono e
/// vale ao mesmo tempo para a vitrine e para o papel (RN-21, RN-22, ADR-015), então todos
/// os casos verificam a ordem pela consulta que os dois canais vão consumir — afirmar o
/// valor da coluna provaria a escrita, não a ordem.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ProductOrderingTests(PostgresFixture postgres)
{
    [Fact]
    public async Task CA_09_mover_produto_para_a_primeira_posicao_reordena_a_categoria()
    {
        var category = await CreateCategoryAsync();
        var products = await CreateProductsAsync(category, "A", "B", "C");

        await CreateOrdering().MoveAsync(products["C"], MoveDirection.Up);
        await CreateOrdering().MoveAsync(products["C"], MoveDirection.Up);

        Assert.Equal(["C", "A", "B"], await OrderedNamesAsync(category));
    }

    [Fact]
    public async Task Mover_para_baixo_troca_com_o_vizinho_seguinte()
    {
        var category = await CreateCategoryAsync();
        var products = await CreateProductsAsync(category, "A", "B", "C");

        await CreateOrdering().MoveAsync(products["A"], MoveDirection.Down);

        Assert.Equal(["B", "A", "C"], await OrderedNamesAsync(category));
    }

    [Fact]
    public async Task O_primeiro_da_categoria_nao_sobe()
    {
        var category = await CreateCategoryAsync();
        var products = await CreateProductsAsync(category, "A", "B", "C");

        await CreateOrdering().MoveAsync(products["A"], MoveDirection.Up);

        Assert.Equal(["A", "B", "C"], await OrderedNamesAsync(category));
    }

    [Fact]
    public async Task O_ultimo_da_categoria_nao_desce()
    {
        var category = await CreateCategoryAsync();
        var products = await CreateProductsAsync(category, "A", "B", "C");

        await CreateOrdering().MoveAsync(products["C"], MoveDirection.Down);

        Assert.Equal(["A", "B", "C"], await OrderedNamesAsync(category));
    }

    /// <summary>
    /// A posição é relativa à categoria, não ao acervo: o vizinho de um produto é o
    /// produto seguinte da mesma categoria, e mover não pode atravessar a fronteira
    /// nem reposicionar quem está do outro lado (RN-21).
    /// </summary>
    [Fact]
    public async Task Mover_dentro_de_uma_categoria_nao_altera_a_ordem_da_outra()
    {
        var first = await CreateCategoryAsync();
        var second = await CreateCategoryAsync();
        var moved = await CreateProductsAsync(first, "A", "B");
        await CreateProductsAsync(second, "X", "Y");

        await CreateOrdering().MoveAsync(moved["B"], MoveDirection.Up);

        Assert.Equal(["B", "A"], await OrderedNamesAsync(first));
        Assert.Equal(["X", "Y"], await OrderedNamesAsync(second));
    }

    /// <summary>
    /// O movimento normaliza a categoria inteira para <c>1..N</c>, como T-10 fez para as
    /// categorias. Isso corrige de passagem os empates que a edição de categoria deixa
    /// atrás de si (R-02 de <c>REVIEW-T-12-2026-09-24</c>).
    /// </summary>
    [Fact]
    public async Task Mover_normaliza_as_posicoes_da_categoria_para_uma_sequencia()
    {
        var category = await CreateCategoryAsync();
        var products = await CreateProductsAsync(category, "A", "B", "C");
        await SetPositionsAsync((products["A"], 7), (products["B"], 7), (products["C"], 40));

        await CreateOrdering().MoveAsync(products["C"], MoveDirection.Up);

        Assert.Equal([1, 2, 3], await OrderedPositionsAsync(category));
    }

    /// <summary>
    /// Risco declarado em T-15: produto sem posição própria — ou com posição empatada,
    /// que é o que a troca de categoria produz hoje — precisa de desempate estável, senão
    /// a ordem oscila entre requisições e o PDF sai diferente do que a tela mostrou.
    /// </summary>
    [Fact]
    public async Task Posicoes_empatadas_tem_desempate_estavel_entre_consultas()
    {
        var category = await CreateCategoryAsync();
        var products = await CreateProductsAsync(category, "A", "B", "C");
        await SetPositionsAsync((products["A"], 0), (products["B"], 0), (products["C"], 0));

        var first = await OrderedNamesAsync(category);
        var second = await OrderedNamesAsync(category);

        Assert.Equal(first, second);
        Assert.Equal(["A", "B", "C"], first);
    }

    [Fact]
    public async Task Produto_inexistente_e_ignorado_sem_erro()
    {
        await CreateOrdering().MoveAsync(-1, MoveDirection.Up);
    }

    private async Task<int> CreateCategoryAsync()
    {
        await using var context = postgres.CreateContext();

        var category = new Category { Name = $"Categoria {Guid.NewGuid():N}", Position = 1 };
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        return category.Id;
    }

    /// <summary>Cria os produtos na ordem informada, que passa a ser a ordem curada.</summary>
    private async Task<IReadOnlyDictionary<string, int>> CreateProductsAsync(
        int categoryId,
        params string[] names)
    {
        await using var context = postgres.CreateContext();

        var created = new Dictionary<string, int>();
        var position = 1;

        foreach (var name in names)
        {
            var product = new Product
            {
                Name = name,
                Price = 100m,
                CategoryId = categoryId,
                Position = position++
            };

            context.Products.Add(product);
            await context.SaveChangesAsync();

            created[name] = product.Id;
        }

        return created;
    }

    private async Task SetPositionsAsync(params (int ProductId, int Position)[] positions)
    {
        await using var context = postgres.CreateContext();

        foreach (var (productId, position) in positions)
        {
            var product = await context.Products.SingleAsync(candidate => candidate.Id == productId);
            product.Position = position;
        }

        await context.SaveChangesAsync();
    }

    private async Task<IReadOnlyList<string>> OrderedNamesAsync(int categoryId)
    {
        await using var context = postgres.CreateContext();

        return await context.Products
            .AsNoTracking()
            .Where(product => product.CategoryId == categoryId)
            .InCuratedOrder()
            .Select(product => product.Name)
            .ToListAsync();
    }

    private async Task<IReadOnlyList<int>> OrderedPositionsAsync(int categoryId)
    {
        await using var context = postgres.CreateContext();

        return await context.Products
            .AsNoTracking()
            .Where(product => product.CategoryId == categoryId)
            .InCuratedOrder()
            .Select(product => product.Position)
            .ToListAsync();
    }

    private ProductOrdering CreateOrdering() =>
        new(new ContextFactory(postgres.ConnectionString), TestCache.Silent());

    private sealed class ContextFactory(string connectionString) : IDbContextFactory<CatalogDbContext>
    {
        public CatalogDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<CatalogDbContext>().UseNpgsql(connectionString).Options);
    }
}
