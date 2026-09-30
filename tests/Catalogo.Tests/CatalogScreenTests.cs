using System.Net;
using System.Text.RegularExpressions;
using Catalogo.Data;
using Catalogo.Features.Account;
using Catalogo.Features.CatalogBuilder;
using Catalogo.Features.Categories;
using Catalogo.Features.Products;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Catalogo.Tests;

/// <summary>
/// A tela do catálogo (UI-08). O que estes casos protegem é uma regra **de disposição**, não de
/// dado: a RN-31 exige que a pré-visualização preceda a geração, e isso é verificável na ordem
/// dos elementos da página.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CatalogScreenTests : IDisposable
{
    private const string OwnerUserName = "dono-catalogo";
    private const string OwnerPassword = "Catalogo!2026";

    private readonly PostgresFixture postgres;
    private readonly WebApplicationFactory<Program> factory;

    public CatalogScreenTests(PostgresFixture postgres)
    {
        this.postgres = postgres;

        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", postgres.ConnectionString);
            builder.UseSetting($"{OwnerAccountOptions.SectionName}:UserName", OwnerUserName);
            builder.UseSetting($"{OwnerAccountOptions.SectionName}:Password", OwnerPassword);
        });
    }

    public void Dispose() => factory.Dispose();

    [Fact]
    public async Task A_tela_de_catalogos_exige_autenticacao()
    {
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/painel/catalogos");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    /// <summary>
    /// CA-16 / RN-31: a pré-visualização **precede** a geração, e não existe caminho que a pule.
    ///
    /// A garantia é de construção — o bloco da prévia vem antes do botão no mesmo documento —, e é
    /// isso que se verifica: a posição de um no HTML contra a do outro. Um botão "gerar direto"
    /// acima da prévia, ou numa página só dele, passaria em qualquer teste de presença e anularia
    /// a proteção que justifica a ADR-014.
    /// </summary>
    [Fact]
    public async Task CA_16_a_previa_precede_a_geracao_na_pagina()
    {
        using var client = await SignedInClientAsync();
        var catalogId = await SeedCatalogAsync();

        var html = await client.GetStringAsync($"/painel/catalogos/{catalogId}");

        var previa = html.IndexOf("Prévia do que vai sair", StringComparison.Ordinal);
        var gerar = html.IndexOf("Gerar o PDF", StringComparison.Ordinal);

        Assert.True(previa >= 0, "A prévia não está na tela.");
        Assert.True(gerar >= 0, "A ação de gerar não está na tela.");
        Assert.True(previa < gerar, "A ação de gerar aparece antes da prévia.");
    }

    /// <summary>
    /// A prévia mostra o que a RN-31 exige: a lista resolvida, a contagem e a estimativa de
    /// páginas. Faltar a contagem deixaria o dono confirmando um conteúdo que ele não mediu.
    /// </summary>
    /// <summary>
    /// As asserções são **ancoradas nos selos**, e isso é a correção de um teste que não mordia:
    /// procurar "4 produtos" no HTML encontrava também a contagem por categoria, e "página"
    /// encontrava o parágrafo que explica que o número é estimativa. Apagar o total e a estimativa
    /// da tela mantinha o caso verde — ou seja, o critério de T-23 não estava verificado.
    /// </summary>
    [Fact]
    public async Task CA_16_a_previa_mostra_contagem_e_estimativa_de_paginas()
    {
        using var client = await SignedInClientAsync();
        var catalogId = await SeedCatalogAsync(products: 4);

        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/painel/catalogos/{catalogId}"));

        Assert.Equal("4 produtos", SeloIn(html, "total"));
        Assert.Equal("1 categoria", SeloIn(html, "categorias"));
        Assert.Equal("~1 página", SeloIn(html, "paginas"));

        // A lista resolvida também: contagem sem lista não é prévia.
        Assert.Contains("Produto 0", html);
    }

    /// <summary>
    /// O conteúdo do selo identificado por `data-previa`. Ler o elemento, e não o documento, é o
    /// que separa "a tela mostra este número" de "este número aparece em algum lugar da página".
    /// </summary>
    private static string SeloIn(string html, string marker)
    {
        var match = Regex.Match(
            html,
            $"""data-previa="{Regex.Escape(marker)}"[^>]*>(?<conteudo>[^<]*)<""");

        Assert.True(match.Success, $"O selo '{marker}' não está na tela.");

        return match.Groups["conteudo"].Value.Trim();
    }

    /// <summary>
    /// UI-08.previaVazia: critério que não resolve nenhum produto No ar impede a geração **com a
    /// razão** (RN-46). "Nada aqui" sem motivo faz o dono achar que o catálogo quebrou, quando o
    /// acervo é que está em Rascunho.
    /// </summary>
    [Fact]
    public async Task UI_08_previa_vazia_explica_a_razao_e_nao_oferece_geracao()
    {
        using var client = await SignedInClientAsync();
        var catalogId = await SeedCatalogAsync(products: 3, published: false);

        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/painel/catalogos/{catalogId}"));

        Assert.Contains("""data-estado="previaVazia" """.TrimEnd(), html);
        Assert.Contains("Rascunho", html);
    }

    /// <summary>
    /// UI-08: a prévia mostra a contagem **por categoria**, com a unidade.
    ///
    /// O total sozinho não serve para a decisão que o dono toma quando o critério passa do teto:
    /// ele precisa saber qual categoria tirar. E a contagem precisa dizer "produtos" — um número
    /// solto ao lado do nome da categoria é lido como código, preço ou posição.
    /// </summary>
    [Fact]
    public async Task UI_08_a_previa_conta_os_produtos_de_cada_categoria()
    {
        using var client = await SignedInClientAsync();
        var (catalogId, categoryId) = await SeedCatalogWithCategoryAsync(products: 3);

        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/painel/catalogos/{catalogId}"));

        var bloco = html[html.IndexOf($"""data-contagem="{categoryId}" """.TrimEnd(), StringComparison.Ordinal)..];

        Assert.Contains("3 produtos", bloco[..120]);
    }

    private async Task<(int CatalogId, int CategoryId)> SeedCatalogWithCategoryAsync(int products)
    {
        await using var context = postgres.CreateContext();

        var category = new Category { Name = $"Categoria {Guid.NewGuid():N}", Position = 1 };
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        for (var index = 0; index < products; index++)
        {
            context.Products.Add(new Product
            {
                Name = $"Produto contado {index}",
                Summary = "Resumo do produto",
                Price = 99.90m,
                CategoryId = category.Id,
                Position = index + 1,
                Status = ProductStatus.Published,
                PublishedAt = DateTimeOffset.UtcNow
            });
        }

        await context.SaveChangesAsync();

        var catalog = new Catalog
        {
            Name = $"Catálogo {Guid.NewGuid():N}",
            Categories = [new CatalogCategory { CategoryId = category.Id }]
        };

        context.Catalogs.Add(catalog);
        await context.SaveChangesAsync();

        return (catalog.Id, category.Id);
    }

    private async Task<int> SeedCatalogAsync(int products = 2, bool published = true)
    {
        await using var context = postgres.CreateContext();

        var category = new Category { Name = $"Categoria {Guid.NewGuid():N}", Position = 1 };
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        for (var index = 0; index < products; index++)
        {
            context.Products.Add(new Product
            {
                Name = $"Produto {index}",
                Summary = "Resumo do produto",
                Price = 99.90m,
                CategoryId = category.Id,
                Position = index + 1,
                Status = published ? ProductStatus.Published : ProductStatus.Draft,
                PublishedAt = published ? DateTimeOffset.UtcNow : null
            });
        }

        await context.SaveChangesAsync();

        var catalog = new Catalog
        {
            Name = $"Catálogo {Guid.NewGuid():N}",
            Categories = [new CatalogCategory { CategoryId = category.Id }]
        };

        context.Catalogs.Add(catalog);
        await context.SaveChangesAsync();

        return catalog.Id;
    }

    private async Task<HttpClient> SignedInClientAsync()
    {
        var client = factory.CreateDefaultClient(new Uri("https://localhost"), new CookieHandler());

        var page = await client.GetStringAsync(PanelAuthentication.LoginPath);
        var token = Regex.Match(
            page,
            """name="__RequestVerificationToken"[^>]*value="([^"]+)""").Groups[1].Value;

        using var response = await client.PostAsync(
            PanelAuthentication.LoginPath,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["_handler"] = "acesso",
                ["Input.UserName"] = OwnerUserName,
                ["Input.Password"] = OwnerPassword,
                ["__RequestVerificationToken"] = token
            }));

        response.EnsureSuccessStatusCode();

        return client;
    }
}
