using System.Net;
using System.Text.RegularExpressions;
using Catalogo.Features.Account;
using Catalogo.Features.Categories;
using Catalogo.Features.Products;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Catalogo.Tests;

[Collection(PostgresCollection.Name)]
public sealed class ProductScreenTests : IDisposable
{
    private const string OwnerUserName = "dono-produtos";
    private const string OwnerPassword = "Catalogo!2026";
    private const string NewProductRoute = "/painel/produtos/novo";

    private readonly PostgresFixture postgres;
    private readonly WebApplicationFactory<Program> factory;

    public ProductScreenTests(PostgresFixture postgres)
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
    public async Task Tela_de_produto_exige_autenticacao()
    {
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync(NewProductRoute);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task UI_05_novo_abre_em_branco_e_em_rascunho()
    {
        using var client = await SignedInClientAsync();

        var html = await client.GetStringAsync(NewProductRoute);

        Assert.Contains("data-estado=\"novo\"", html);
        Assert.Contains("Rascunho", html);
    }

    /// <summary>
    /// A SPEC-UI é explícita: sem declarar onde cada texto aparece, o dono escreve a
    /// especificação inteira no resumo e quebra a grade do PDF (ADR-016, UI-05).
    /// </summary>
    [Fact]
    public async Task Cada_campo_de_texto_declara_onde_aparece()
    {
        using var client = await SignedInClientAsync();

        var html = await client.GetStringAsync(NewProductRoute);

        Assert.Contains("Aparece na vitrine, na página de detalhe e na célula do PDF", html);
        Assert.Contains("célula do PDF</b> e no card da listagem", html);
        Assert.Contains("apenas na página de detalhe</b> da vitrine", html);
    }

    [Fact]
    public async Task Resumo_mostra_contador_com_o_limite_de_T_04()
    {
        using var client = await SignedInClientAsync();

        var html = await client.GetStringAsync(NewProductRoute);

        Assert.Contains($"/ {Product.SummaryMaxLength}", html);
    }

    [Fact]
    public async Task RN_07_rotulo_oferece_apenas_a_lista_fechada()
    {
        using var client = await SignedInClientAsync();

        var html = await client.GetStringAsync(NewProductRoute);

        Assert.Contains(">PREÇO</option>", html);
        Assert.Contains(">PREÇO/UND</option>", html);

        // O fechamento da lista é propriedade do domínio, não da marcação: a tela só
        // consegue oferecer o que o enum define. Testar contando `<option>` no HTML
        // seria frágil e provaria menos.
        Assert.Equal(2, Enum.GetValues<PriceLabel>().Length);
    }

    /// <summary>
    /// A foto precisa de um produto para se associar, e a tela de cadastro em branco não
    /// tem id — o aviso é a única coisa que explica isso ao dono (UI-05).
    /// </summary>
    [Fact]
    public async Task UI_05_novo_explica_que_a_foto_exige_o_produto_salvo()
    {
        using var client = await SignedInClientAsync();

        var html = await client.GetStringAsync(NewProductRoute);

        Assert.Contains("Salve o produto primeiro", html);
        Assert.DoesNotContain("Enviar foto", html);
    }

    [Fact]
    public async Task UI_05_edicao_oferece_o_envio_da_foto_e_declara_a_substituicao()
    {
        using var client = await SignedInClientAsync();
        var productId = await CreateProductAsync();

        var html = await client.GetStringAsync($"/painel/produtos/{productId}");

        Assert.Contains("Enviar foto", html);
        Assert.Contains("enviar outra substitui a atual", html);
    }

    /// <summary>
    /// RN-20: a exclusão exige confirmação explícita, e o botão da tela **pede** em vez de
    /// executar. A renderização estática mostra o pedido; a confirmação em si vive no
    /// circuito, como o resto de UI-05.
    /// </summary>
    [Fact]
    public async Task RN_20_a_edicao_oferece_excluir_sem_ja_excluir()
    {
        using var client = await SignedInClientAsync();
        var productId = await CreateProductAsync();

        var html = await client.GetStringAsync($"/painel/produtos/{productId}");

        Assert.Contains(">Excluir<", html);

        // O pedido não é a confirmação: o estado de confirmação só aparece depois da ação.
        Assert.DoesNotContain("confirmarExclusao", html);

        // E nada foi excluído por ter aberto a tela.
        await using var context = postgres.CreateContext();
        Assert.True(await context.Products.AnyAsync(product => product.Id == productId));
    }

    /// <summary>
    /// Não há o que excluir num produto que ainda não existe — oferecer o botão ali seria
    /// convidar a um erro sem efeito.
    /// </summary>
    [Fact]
    public async Task UI_05_novo_nao_oferece_exclusao()
    {
        using var client = await SignedInClientAsync();

        var html = await client.GetStringAsync(NewProductRoute);

        Assert.DoesNotContain(">Excluir<", html);
    }

    private async Task<int> CreateProductAsync()
    {
        await using var context = postgres.CreateContext();

        var category = new Category { Name = $"Categoria {Guid.NewGuid():N}", Position = 1 };
        context.Categories.Add(category);

        var product = new Product
        {
            Name = "Monitor VXPro 19",
            Price = 599.90m,
            Category = category,
            Status = ProductStatus.Draft,
            Position = 1
        };

        context.Products.Add(product);
        await context.SaveChangesAsync();

        return product.Id;
    }

    private async Task<HttpClient> SignedInClientAsync()
    {
        var client = factory.CreateDefaultClient(new Uri("https://localhost"), new CookieHandler());

        var page = await client.GetStringAsync(PanelAuthentication.LoginPath);
        var token = Regex.Match(
            page,
            """name="__RequestVerificationToken"[^>]*value="([^"]+)""").Groups[1].Value;

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "acesso",
            ["Input.UserName"] = OwnerUserName,
            ["Input.Password"] = OwnerPassword,
            ["__RequestVerificationToken"] = token
        });

        using var response = await client.PostAsync(PanelAuthentication.LoginPath, form);
        response.EnsureSuccessStatusCode();

        return client;
    }
}
