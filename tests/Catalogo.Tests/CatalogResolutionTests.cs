using Catalogo.Data;
using Catalogo.Features.CatalogBuilder;
using Catalogo.Features.Categories;
using Catalogo.Features.Products;
using Microsoft.EntityFrameworkCore;

namespace Catalogo.Tests;

/// <summary>
/// Resolução do critério e prévia (T-23). É onde a ADR-014 se materializa: o catálogo guarda
/// o filtro, e a lista concreta nasce aqui, no momento em que alguém pede.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CatalogResolutionTests(PostgresFixture postgres)
{
    /// <summary>
    /// CA-10 e RN-39, a regra que o plano aponta como a mais fácil de implementar errado: um
    /// recorte com a 3ª e a 6ª categorias globais as numera **01 e 02**. Usar o identificador
    /// ou a posição global produziria 03 e 06.
    /// </summary>
    [Fact]
    public async Task CA_10_a_numeracao_e_posicional_no_recorte_e_nao_a_global()
    {
        var impressoras = await CreateCategoryAsync("Impressoras", position: 1);
        var energia = await CreateCategoryAsync("Energia", position: 2);
        var tintas = await CreateCategoryAsync("Tintas", position: 3);
        var redes = await CreateCategoryAsync("Redes", position: 6);

        await PublishProductAsync(impressoras.Id, "Fora do recorte");
        await PublishProductAsync(energia.Id, "Também fora");
        await PublishProductAsync(tintas.Id, "Cartucho");
        await PublishProductAsync(redes.Id, "Cabo de rede");

        var catalogId = await SaveCatalogAsync([tintas.Id, redes.Id]);
        var resolved = await CreateResolution().ResolveAsync(catalogId);

        Assert.Equal(2, resolved!.Categories.Count);

        // A ordem é a **global** (RN-40); a numeração é a **local** (RN-39). As duas coisas ao
        // mesmo tempo, e é isso que confunde.
        Assert.Equal(tintas.Id, resolved.Categories[0].Id);
        Assert.Equal("01", resolved.Categories[0].Label);
        Assert.Equal(redes.Id, resolved.Categories[1].Id);
        Assert.Equal("02", resolved.Categories[1].Label);
    }

    /// <summary>
    /// A contraprova do caso acima: o mesmo acervo, recortado de outra forma, numera diferente.
    /// Se a numeração viesse do cadastro, os dois casos dariam o mesmo resultado.
    /// </summary>
    [Fact]
    public async Task RN_39_o_mesmo_acervo_em_outro_recorte_numera_diferente()
    {
        var primeira = await CreateCategoryAsync("Alfa", position: 1);
        var segunda = await CreateCategoryAsync("Beta", position: 2);
        var terceira = await CreateCategoryAsync("Gama", position: 3);

        await PublishProductAsync(primeira.Id, "A");
        await PublishProductAsync(segunda.Id, "B");
        await PublishProductAsync(terceira.Id, "C");

        var resolution = CreateResolution();

        var comTodas = await resolution.ResolveAsync(
            await SaveCatalogAsync([primeira.Id, segunda.Id, terceira.Id]));

        var soAUltima = await resolution.ResolveAsync(await SaveCatalogAsync([terceira.Id]));

        Assert.Equal("03", comTodas!.Categories.Single(c => c.Id == terceira.Id).Label);
        Assert.Equal("01", soAUltima!.Categories.Single(c => c.Id == terceira.Id).Label);
    }

    [Fact]
    public async Task RN_30_a_previa_resolve_apenas_produtos_no_ar()
    {
        var category = await CreateCategoryAsync();
        await PublishProductAsync(category.Id, "No ar");
        await CreateDraftAsync(category.Id, "Em rascunho");

        var resolved = await CreateResolution().ResolveAsync(await SaveCatalogAsync([category.Id]));

        Assert.Equal(1, resolved!.ProductCount);
        Assert.Equal("No ar", resolved.Categories.Single().Products.Single().Name);
    }

    /// <summary>
    /// CA-18 / RN-46: todas as categorias do critério em Rascunho resolve **vazio**, e a tela
    /// usa isso para impedir a geração com a razão.
    /// </summary>
    [Fact]
    public async Task CA_18_catalogo_com_tudo_em_rascunho_resolve_vazio()
    {
        var category = await CreateCategoryAsync();
        await CreateDraftAsync(category.Id, "Rascunho um");
        await CreateDraftAsync(category.Id, "Rascunho dois");

        var resolved = await CreateResolution().ResolveAsync(await SaveCatalogAsync([category.Id]));

        Assert.True(resolved!.IsEmpty);
        Assert.Equal(0, resolved.ProductCount);
    }

    /// <summary>
    /// CA-14: o cenário que justifica a RN-32. O catálogo foi gerado há um mês; um produto novo
    /// é publicado na categoria dele **sem ninguém tocar no catálogo**, e a prévia o destaca.
    /// Sem esse destaque, o dono descobre no cliente.
    /// </summary>
    [Fact]
    public async Task CA_14_produto_publicado_depois_da_ultima_geracao_aparece_destacado()
    {
        var category = await CreateCategoryAsync("Impressoras");
        var antigoId = await PublishProductAsync(category.Id, "Impressora antiga");
        var catalogId = await SaveCatalogAsync([category.Id]);

        // O catálogo foi gerado depois do produto antigo, e antes do novo.
        await BackdatePublicationAsync(antigoId, Days(-40));
        await CreateMaintenance().MarkGeneratedAsync(catalogId, Days(-30));

        var novoId = await PublishProductAsync(category.Id, "Impressora nova");
        await BackdatePublicationAsync(novoId, Days(-1));

        var resolved = await CreateResolution().ResolveAsync(catalogId);
        var products = resolved!.Categories.Single().Products;

        Assert.True(resolved.HasNewProducts);
        Assert.Equal(1, resolved.NewSinceLastGeneration);
        Assert.True(products.Single(product => product.Name == "Impressora nova").IsNewSinceLastGeneration);
        Assert.False(products.Single(product => product.Name == "Impressora antiga").IsNewSinceLastGeneration);
    }

    /// <summary>
    /// Catálogo nunca gerado não destaca nada: se tudo é novo, destacar tudo não informa nada —
    /// e o dono não tem expectativa anterior a ser contrariada.
    /// </summary>
    [Fact]
    public async Task RN_32_catalogo_nunca_gerado_nao_destaca_nada()
    {
        var category = await CreateCategoryAsync();
        await PublishProductAsync(category.Id, "Recente");

        var resolved = await CreateResolution().ResolveAsync(await SaveCatalogAsync([category.Id]));

        Assert.False(resolved!.HasNewProducts);
        Assert.Equal(0, resolved.NewSinceLastGeneration);
    }

    /// <summary>
    /// RN-40: produtos saem na posição definida dentro da categoria, que é a ordem curada pelo
    /// dono — não na ordem de cadastro nem alfabética.
    /// </summary>
    [Fact]
    public async Task RN_40_produtos_saem_na_posicao_curada_dentro_da_categoria()
    {
        var category = await CreateCategoryAsync();
        await PublishProductAsync(category.Id, "Terceiro", position: 3);
        await PublishProductAsync(category.Id, "Primeiro", position: 1);
        await PublishProductAsync(category.Id, "Segundo", position: 2);

        var resolved = await CreateResolution().ResolveAsync(await SaveCatalogAsync([category.Id]));

        Assert.Equal(
            ["Primeiro", "Segundo", "Terceiro"],
            resolved!.Categories.Single().Products.Select(product => product.Name));
    }

    /// <summary>
    /// Categoria do critério que não tem nenhum produto No ar **não aparece** na prévia, e por
    /// isso não consome número: numerar uma seção vazia produziria um salto no papel.
    /// </summary>
    [Fact]
    public async Task Categoria_sem_produto_no_ar_nao_consome_numero()
    {
        var comProduto = await CreateCategoryAsync("Com produto", position: 1);
        var vazia = await CreateCategoryAsync("Vazia", position: 2);
        var outra = await CreateCategoryAsync("Outra com produto", position: 3);

        await PublishProductAsync(comProduto.Id, "Um");
        await CreateDraftAsync(vazia.Id, "Só rascunho");
        await PublishProductAsync(outra.Id, "Dois");

        var resolved = await CreateResolution()
            .ResolveAsync(await SaveCatalogAsync([comProduto.Id, vazia.Id, outra.Id]));

        Assert.Equal(2, resolved!.Categories.Count);
        Assert.Equal("01", resolved.Categories[0].Label);
        Assert.Equal("02", resolved.Categories[1].Label);
        Assert.DoesNotContain(resolved.Categories, category => category.Id == vazia.Id);
    }

    [Fact]
    public async Task A_estimativa_de_paginas_acompanha_a_contagem()
    {
        var category = await CreateCategoryAsync();

        for (var index = 0; index < ResolvedCatalog.ProductsPerPage + 1; index++)
        {
            await PublishProductAsync(category.Id, $"Produto {index}", position: index + 1);
        }

        var resolved = await CreateResolution().ResolveAsync(await SaveCatalogAsync([category.Id]));

        Assert.Equal(ResolvedCatalog.ProductsPerPage + 1, resolved!.ProductCount);
        Assert.Equal(2, resolved.EstimatedPages);
    }

    [Fact]
    public async Task Catalogo_inexistente_resolve_nulo()
    {
        Assert.Null(await CreateResolution().ResolveAsync(987654));
    }

    private static DateTimeOffset Days(int offset) => DateTimeOffset.UtcNow.AddDays(offset);

    private async Task BackdatePublicationAsync(int productId, DateTimeOffset moment)
    {
        await using var context = postgres.CreateContext();

        await context.Products
            .Where(product => product.Id == productId)
            .ExecuteUpdateAsync(update =>
                update.SetProperty(product => product.PublishedAt, moment));
    }

    private async Task<int> SaveCatalogAsync(IEnumerable<int> categoryIds)
    {
        var outcome = await CreateMaintenance().SaveAsync(new CatalogDraft
        {
            Name = $"Catálogo {Guid.NewGuid():N}",
            CategoryIds = [.. categoryIds]
        });

        Assert.True(outcome.Succeeded);

        return outcome.Id!.Value;
    }

    private async Task<int> PublishProductAsync(int categoryId, string name, int position = 1)
    {
        var productId = await CreateDraftAsync(categoryId, name, position);

        await using var context = postgres.CreateContext();

        await context.Products
            .Where(product => product.Id == productId)
            .ExecuteUpdateAsync(update => update
                .SetProperty(product => product.Status, ProductStatus.Published)
                .SetProperty(product => product.PublishedAt, DateTimeOffset.UtcNow));

        return productId;
    }

    private async Task<int> CreateDraftAsync(int categoryId, string name, int position = 1)
    {
        await using var context = postgres.CreateContext();

        var product = new Product
        {
            Name = name,
            Summary = "Resumo",
            Price = 149.90m,
            CategoryId = categoryId,
            Position = position,
            Status = ProductStatus.Draft
        };

        context.Products.Add(product);
        await context.SaveChangesAsync();

        return product.Id;
    }

    private async Task<Category> CreateCategoryAsync(string? name = null, int position = 1)
    {
        await using var context = postgres.CreateContext();

        var category = new Category
        {
            Name = $"{name ?? "Categoria"} {Guid.NewGuid():N}",
            Position = position
        };

        context.Categories.Add(category);
        await context.SaveChangesAsync();

        return category;
    }

    private CatalogResolution CreateResolution() =>
        new(new ContextFactory(postgres.ConnectionString));

    private CatalogMaintenance CreateMaintenance() =>
        new(new ContextFactory(postgres.ConnectionString));

    private sealed class ContextFactory(string connectionString) : IDbContextFactory<CatalogDbContext>
    {
        public CatalogDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<CatalogDbContext>().UseNpgsql(connectionString).Options);
    }
}
