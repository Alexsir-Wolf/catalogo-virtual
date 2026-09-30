using System.Net;
using Catalogo.Data;
using Catalogo.Features.Account;
using Catalogo.Features.CatalogBuilder;
using Catalogo.Features.Categories;
using Catalogo.Features.Media;
using Catalogo.Features.Products;
using Catalogo.Features.Settings;
using Catalogo.Features.Storefront;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Catalogo.Tests;

/// <summary>
/// Cache de saída da vitrine e sua invalidação (T-21, ADR-008).
///
/// **É a parte do sistema mais propensa a defeito silencioso.** Cache que não invalida não
/// quebra nada visivelmente — só mostra dado velho, e ninguém percebe até o cliente reclamar.
/// Por isso os casos aqui cobrem **cada tipo de escrita**, um por um, e não uma amostra: o
/// esquecimento típico é a ordenação, que ninguém associa a "conteúdo da página".
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class StorefrontCacheTests : IAsyncLifetime, IDisposable
{
    private const string OwnerUserName = "dono-cache";
    private const string OwnerPassword = "Catalogo!2026";

    private readonly PostgresFixture postgres;

    private IsolatedDatabase? database;
    private string connectionString = string.Empty;
    private WebApplicationFactory<Program> factory = null!;

    public StorefrontCacheTests(PostgresFixture postgres) => this.postgres = postgres;

    public async Task InitializeAsync()
    {
        database = await IsolatedDatabase.CreateAsync(postgres, "cache");
        connectionString = database.ConnectionString;

        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", connectionString);
            builder.UseSetting($"{OwnerAccountOptions.SectionName}:UserName", OwnerUserName);
            builder.UseSetting($"{OwnerAccountOptions.SectionName}:Password", OwnerPassword);
        });

        await database.MigrateAsync();
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
    /// A segunda requisição à mesma URL é servida do cache. A verificação é indireta e é a única
    /// honesta pelo pipeline HTTP: o preço é alterado **direto no banco**, sem passar pelos
    /// serviços que invalidam, e a página continua mostrando o valor antigo. Se não houvesse
    /// cache, a segunda resposta já traria o valor novo.
    /// </summary>
    [Fact]
    public async Task A_segunda_requisicao_a_mesma_url_e_servida_do_cache()
    {
        var productId = await SeedPublishedAsync(price: 100m);
        using var client = factory.CreateClient();

        var first = await client.GetStringAsync("/");
        Assert.Contains("100,00", first);

        await SetPriceDirectlyAsync(productId, 999m);

        var second = await client.GetStringAsync("/");

        // Veio do cache: o preço novo não aparece porque ninguém invalidou.
        Assert.Contains("100,00", second);
        Assert.DoesNotContain("999,00", second);
    }

    /// <summary>
    /// URLs com filtros diferentes são **entradas distintas**: o estado de navegação vive na URL
    /// (RN-56), e uma chave que ignorasse a query serviria a listagem filtrada para quem pediu a
    /// completa.
    /// </summary>
    [Fact]
    public async Task Urls_com_filtros_diferentes_sao_entradas_distintas()
    {
        var categoryId = await SeedCategoryAsync("Impressoras");
        await SeedPublishedAsync(price: 50m, name: "Impressora cacheada", categoryId: categoryId);
        await SeedPublishedAsync(price: 70m, name: "Produto de outra categoria");

        using var client = factory.CreateClient();

        var filtered = await client.GetStringAsync($"/?categoria={categoryId}");
        var all = await client.GetStringAsync("/");

        Assert.Contains("Impressora cacheada", filtered);
        Assert.DoesNotContain("Produto de outra categoria", filtered);

        // A URL sem filtro tem os dois: se a chave ignorasse a query, esta resposta seria a
        // filtrada servida de cache.
        Assert.Contains("Produto de outra categoria", all);
    }

    /// <summary>
    /// CA-15: alterar o preço de um produto publicado reflete na vitrine **na requisição
    /// seguinte** — não em cinco minutos, não depois de reiniciar.
    /// </summary>
    [Fact]
    public async Task CA_15_alterar_preco_reflete_na_vitrine_na_requisicao_seguinte()
    {
        var productId = await SeedPublishedAsync(price: 100m);
        using var client = factory.CreateClient();

        Assert.Contains("100,00", await client.GetStringAsync("/"));

        // Pelo caminho real do painel, que é quem invalida.
        var maintenance = Maintenance();
        var draft = await maintenance.FindAsync(productId);
        await maintenance.SaveAsync(draft! with { Price = 777m });

        Assert.Contains("777,00", await client.GetStringAsync("/"));
    }

    [Fact]
    public async Task Publicar_produto_reflete_na_listagem_na_requisicao_seguinte()
    {
        var categoryId = await SeedCategoryAsync();
        var draftId = await SeedDraftAsync("Produto que vai ao ar", categoryId);

        using var client = factory.CreateClient();

        Assert.DoesNotContain("Produto que vai ao ar", await client.GetStringAsync("/"));

        await Publication().PublishAsync(draftId);

        Assert.Contains("Produto que vai ao ar", await client.GetStringAsync("/"));
    }

    [Fact]
    public async Task Despublicar_remove_o_produto_da_vitrine_na_requisicao_seguinte()
    {
        var productId = await SeedPublishedAsync(price: 40m, name: "Produto que sai do ar");

        using var client = factory.CreateClient();

        Assert.Contains("Produto que sai do ar", await client.GetStringAsync("/"));

        await Publication().WithdrawAsync(productId);

        Assert.DoesNotContain("Produto que sai do ar", await client.GetStringAsync("/"));
    }

    [Fact]
    public async Task Excluir_remove_o_produto_da_vitrine_na_requisicao_seguinte()
    {
        var productId = await SeedPublishedAsync(price: 40m, name: "Produto que sera excluido");

        using var client = factory.CreateClient();

        Assert.Contains("Produto que sera excluido", await client.GetStringAsync("/"));

        await Maintenance().DeleteAsync(productId);

        Assert.DoesNotContain("Produto que sera excluido", await client.GetStringAsync("/"));
    }

    /// <summary>
    /// **O esquecimento típico.** Reordenar não parece "mudar conteúdo", mas a ordem da vitrine é
    /// a mesma ordem impressa (RN-21, RN-22) — e sem invalidar, a página fica com a ordem antiga.
    /// </summary>
    [Fact]
    public async Task Reordenar_reflete_na_ordem_exibida_na_requisicao_seguinte()
    {
        var categoryId = await SeedCategoryAsync();
        await SeedPublishedAsync(price: 10m, name: "Aaa primeiro", categoryId: categoryId, position: 1);
        var second = await SeedPublishedAsync(price: 20m, name: "Zzz segundo", categoryId: categoryId, position: 2);

        using var client = factory.CreateClient();

        var before = await client.GetStringAsync("/");
        Assert.True(before.IndexOf("Aaa primeiro") < before.IndexOf("Zzz segundo"));

        await Ordering().MoveAsync(second, MoveDirection.Up);

        var after = await client.GetStringAsync("/");
        Assert.True(after.IndexOf("Zzz segundo") < after.IndexOf("Aaa primeiro"));
    }

    [Fact]
    public async Task Renomear_categoria_reflete_no_filtro_na_requisicao_seguinte()
    {
        var categoryId = await SeedCategoryAsync("Nome antigo da categoria");
        await SeedPublishedAsync(price: 30m, categoryId: categoryId);

        using var client = factory.CreateClient();

        Assert.Contains("Nome antigo da categoria", await client.GetStringAsync("/"));

        await Categories().RenameAsync(categoryId, "Nome novo da categoria");

        Assert.Contains("Nome novo da categoria", await client.GetStringAsync("/"));
    }

    /// <summary>
    /// CA-38: o contato alimenta o rodapé da vitrine, e a RN-68 promete reflexo imediato. Sem
    /// invalidar, "imediato" passaria a significar "depois da expiração".
    /// </summary>
    [Fact]
    public async Task CA_38_alterar_contato_reflete_na_vitrine_na_requisicao_seguinte()
    {
        await SeedPublishedAsync(price: 30m);

        using var client = factory.CreateClient();

        var before = WebUtility.HtmlDecode(await client.GetStringAsync("/"));
        Assert.DoesNotContain("vendas@exemplo.com.br", before);

        await Settings().SaveContactAsync(new ContactDraft { Email = "vendas@exemplo.com.br" });

        var after = WebUtility.HtmlDecode(await client.GetStringAsync("/"));
        Assert.Contains("vendas@exemplo.com.br", after);
    }

    /// <summary>
    /// O painel **nunca** é cacheado: ele mostra rascunho, situação e contagem que mudam a cada
    /// escrita, e servir isso de cache faria o dono editar contra um retrato do passado.
    /// </summary>
    [Fact]
    public void O_painel_nao_e_rota_cacheavel()
    {
        Assert.True(StorefrontCache.IsStorefront("/"));
        Assert.True(StorefrontCache.IsStorefront("/produto/1"));

        Assert.False(StorefrontCache.IsStorefront("/painel"));
        Assert.False(StorefrontCache.IsStorefront("/painel/produtos"));
        Assert.False(StorefrontCache.IsStorefront("/painel/catalogos"));
        Assert.False(StorefrontCache.IsStorefront("/painel/configuracoes"));
    }

    /// <summary>
    /// A invalidação evicta a tag da vitrine. É o contrato que todos os serviços de escrita
    /// consomem, e um `EvictByTag` com tag errada falharia em silêncio.
    /// </summary>
    [Fact]
    public async Task A_invalidacao_evicta_a_tag_da_vitrine()
    {
        var (invalidation, store) = TestCache.Recording();

        await invalidation.InvalidateAsync("teste");

        Assert.Equal([StorefrontCache.Tag], store.Evicted);
    }

    private async Task SetPriceDirectlyAsync(int productId, decimal price)
    {
        await using var context = CreateContext();

        await context.Products
            .Where(product => product.Id == productId)
            .ExecuteUpdateAsync(update => update.SetProperty(product => product.Price, price));
    }

    private async Task<int> SeedCategoryAsync(string? name = null)
    {
        await using var context = CreateContext();

        var category = new Category
        {
            Name = name ?? $"Categoria {Guid.NewGuid():N}",
            Position = 1
        };

        context.Categories.Add(category);
        await context.SaveChangesAsync();

        return category.Id;
    }

    private async Task<int> SeedDraftAsync(string name, int categoryId, int position = 1)
    {
        await using var context = CreateContext();

        var product = new Product
        {
            Name = name,
            Summary = "Resumo",
            Price = 10m,
            CategoryId = categoryId,
            Position = position,
            Status = ProductStatus.Draft,
            Photo = Photo()
        };

        context.Products.Add(product);
        await context.SaveChangesAsync();

        return product.Id;
    }

    private async Task<int> SeedPublishedAsync(
        decimal price,
        string? name = null,
        int? categoryId = null,
        int position = 1)
    {
        var category = categoryId ?? await SeedCategoryAsync();

        await using var context = CreateContext();

        var product = new Product
        {
            Name = name ?? $"Produto {Guid.NewGuid():N}",
            Summary = "Resumo",
            Price = price,
            CategoryId = category,
            Position = position,
            Status = ProductStatus.Published,
            PublishedAt = DateTimeOffset.UtcNow,
            Photo = Photo()
        };

        context.Products.Add(product);
        await context.SaveChangesAsync();

        return product.Id;
    }

    private static ProductPhoto Photo()
    {
        var prefix = Guid.NewGuid().ToString("N");

        return new ProductPhoto
        {
            OriginalFileName = $"{prefix}-original",
            ThumbnailFileName = $"{prefix}-miniatura.webp",
            CardFileName = $"{prefix}-cartao.webp",
            LargeFileName = $"{prefix}-ampliada.webp",
            PrintFileName = $"{prefix}-impressao.jpg"
        };
    }

    /// <summary>
    /// Os serviços vêm do **contêiner da aplicação**, e não montados à mão: é a única forma de o
    /// teste exercitar a mesma instância de cache que serve as requisições HTTP.
    /// </summary>
    private ProductMaintenance Maintenance() => Resolve<ProductMaintenance>();

    private ProductPublication Publication() => Resolve<ProductPublication>();

    private ProductOrdering Ordering() => Resolve<ProductOrdering>();

    private CategoryMaintenance Categories() => Resolve<CategoryMaintenance>();

    private PortalSettingsService Settings() => Resolve<PortalSettingsService>();

    private TService Resolve<TService>() where TService : notnull
    {
        var scope = factory.Services.CreateScope();

        return scope.ServiceProvider.GetRequiredService<TService>();
    }

    private CatalogDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<CatalogDbContext>().UseNpgsql(connectionString).Options);
}
