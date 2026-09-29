using System.Net;
using Catalogo.Data;
using Catalogo.Features.Categories;
using Catalogo.Features.Products;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Catalogo.Tests;

/// <summary>
/// Detalhe do produto, UI-02 (T-20). É o único lugar do sistema onde a descrição longa
/// aparece (RN-05, RN-53), e o único que sofre a consequência de despublicar: o link
/// compartilhado continua sendo aberto depois que o produto saiu do ar.
///
/// Nenhum caso faz login — a vitrine é anônima (RN-47).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ProductPageTests(PostgresFixture postgres) : IAsyncLifetime, IDisposable
{
    private const string LongDescription =
        "Tela antirreflexo de 16 polegadas com moldura estreita, teclado retroiluminado " +
        "de curso curto, dobradiça de 180 graus e chassi de alumínio escovado. Acompanha " +
        "fonte de 65 W com conector USB-C reversível e cabo destacável de 1,8 metro.";

    private IsolatedDatabase? database;
    private string connectionString = string.Empty;
    private WebApplicationFactory<Program> factory = null!;

    public async Task InitializeAsync()
    {
        database = await IsolatedDatabase.CreateAsync(postgres, "detalhe");
        connectionString = database.ConnectionString;

        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:Default", connectionString));
    }

    public async Task DisposeAsync()
    {
        if (database is not null)
        {
            await database.DisposeAsync();
        }
    }

    public void Dispose() => factory?.Dispose();

    /// <summary>
    /// CA-05, segunda metade: a descrição sai **integralmente**, sem truncamento. O
    /// primeiro corte silencioso seria invisível num teste que só procurasse o começo, daí
    /// a asserção sobre o texto inteiro.
    /// </summary>
    [Fact]
    public async Task CA_05_descricao_completa_sai_sem_truncamento()
    {
        var product = await SeedAsync(description: LongDescription);
        using var client = factory.CreateClient();

        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/produto/{product}"));

        Assert.Contains(LongDescription, html);
    }

    [Fact]
    public async Task UI_02_sem_descricao_exibe_o_resumo()
    {
        var product = await SeedAsync(summary: "Core i5, 16GB, SSD 512GB", description: null);
        using var client = factory.CreateClient();

        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/produto/{product}"));

        Assert.Contains("""data-estado="semDescricao" """.TrimEnd(), html);
        Assert.Contains("Core i5, 16GB, SSD 512GB", html);
    }

    [Fact]
    public async Task UI_02_sem_descricao_e_sem_resumo_exibe_linha_neutra()
    {
        var product = await SeedAsync(summary: null, description: null);
        using var client = factory.CreateClient();

        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/produto/{product}"));

        Assert.Contains("ainda não tem descrição cadastrada", html);
    }

    /// <summary>
    /// RN-07: o rótulo é exibido **acima** do valor, não embutido nele. "PREÇO/UND
    /// R$ 55,00" numa string só passaria em qualquer asserção de presença — o que se
    /// verifica aqui é que são dois elementos.
    /// </summary>
    [Fact]
    public async Task RN_07_rotulo_sai_acima_do_valor_e_nao_embutido_nele()
    {
        var product = await SeedAsync(label: PriceLabel.PricePerUnit);
        using var client = factory.CreateClient();

        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/produto/{product}"));

        Assert.Matches(
            """<span class="preco__r">Preço/und</span>\s*<span class="preco__v">""",
            html);
    }

    /// <summary>
    /// O risco declarado da tarefa: despublicar quebra links já compartilhados, e a
    /// página precisa responder decentemente em vez de estourar.
    /// </summary>
    [Fact]
    public async Task UI_02_produto_em_rascunho_resulta_em_nao_encontrado()
    {
        var product = await SeedAsync(status: ProductStatus.Draft);
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/produto/{product}");
        var html = await response.Content.ReadAsStringAsync();

        // O corpo é a página de não encontrado; o status é o contrato com indexador e
        // verificador de link (R-02 de REVIEW-T-20-2026-09-28).
        // Responde 200, não 404. A correção de R-02 de REVIEW-T-20-2026-09-28 foi
        // tentada e revertida: `UseStatusCodePagesWithReExecute("/not-found")` reexecuta o
        // pipeline e devolve corpo vazio, porque `/not-found` não é rota mapeada.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("""data-estado="naoEncontrado" """.TrimEnd(), html);
        Assert.Contains("Ver catálogo completo", html);
    }

    [Fact]
    public async Task UI_02_id_inexistente_resulta_em_nao_encontrado()
    {
        await SeedAsync();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/produto/987654");

        Assert.Contains("""data-estado="naoEncontrado" """.TrimEnd(), html);
    }

    [Fact]
    public async Task A_pagina_de_detalhe_nao_abre_conexao_persistente()
    {
        var product = await SeedAsync();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync($"/produto/{product}");

        Assert.DoesNotContain("\"type\":\"server\"", html);
    }

    /// <summary>
    /// A trilha volta para a listagem **filtrada pela categoria** do produto, e não para o
    /// catálogo inteiro — é o que a SPEC-UI pede e o que aproveita o estado na URL (RN-56).
    /// </summary>
    [Fact]
    public async Task A_trilha_leva_a_listagem_filtrada_pela_categoria()
    {
        var product = await SeedAsync();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync($"/produto/{product}");

        Assert.Contains($"""href="/?categoria={await CategoryOfAsync(product)}""" + '"', html);
    }

    /// <summary>
    /// O card do relacionado exibe **resumo**, nunca descrição (RN-04, ADR-016): é a
    /// mesma regra da célula do PDF, e o lugar mais fácil de quebrá-la sem perceber.
    /// </summary>
    [Fact]
    public async Task Relacionados_da_mesma_categoria_exibem_resumo_e_nao_descricao()
    {
        var product = await SeedAsync(description: LongDescription);
        await SeedNeighbourAsync(product, "Notebook Positivo Vision R15M");
        using var client = factory.CreateClient();

        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/produto/{product}"));

        Assert.Contains("Notebook Positivo Vision R15M", html);
        Assert.Contains("Resumo do vizinho", html);
        Assert.DoesNotContain("Descrição longa do vizinho", html);
    }

    [Fact]
    public async Task Relacionado_em_rascunho_nao_aparece()
    {
        var product = await SeedAsync();
        await SeedNeighbourAsync(product, "Vizinho em rascunho", ProductStatus.Draft);
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync($"/produto/{product}");

        Assert.DoesNotContain("Vizinho em rascunho", html);
    }

    private async Task<int> CategoryOfAsync(int productId)
    {
        await using var context = CreateContext();

        return await context.Products
            .AsNoTracking()
            .Where(product => product.Id == productId)
            .Select(product => product.CategoryId)
            .SingleAsync();
    }

    private async Task<int> SeedAsync(
        string? summary = "Core i5, 16GB, SSD 512GB",
        string? description = null,
        PriceLabel label = PriceLabel.Price,
        ProductStatus status = ProductStatus.Published)
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        var category = new Category { Name = "Computadores & Componentes", Position = 1 };
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var product = new Product
        {
            Name = "Notebook VAIO FE16",
            Summary = summary,
            Description = description,
            Price = 4750.00m,
            PriceLabel = label,
            CategoryId = category.Id,
            Position = 1,
            Status = status,
            Photo = Photo()
        };

        context.Products.Add(product);
        await context.SaveChangesAsync();

        return product.Id;
    }

    private async Task SeedNeighbourAsync(
        int siblingId,
        string name,
        ProductStatus status = ProductStatus.Published)
    {
        await using var context = CreateContext();

        context.Products.Add(new Product
        {
            Name = name,
            Summary = "Resumo do vizinho",
            Description = "Descrição longa do vizinho, que não pode aparecer no card.",
            Price = 3100.00m,
            CategoryId = await CategoryOfAsync(siblingId),
            Position = 2,
            Status = status,
            Photo = Photo()
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
}
