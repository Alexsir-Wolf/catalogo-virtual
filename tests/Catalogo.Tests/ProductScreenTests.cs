using System.Net;
using System.Text.RegularExpressions;
using Catalogo.Features.Account;
using Catalogo.Features.Products;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Catalogo.Tests;

[Collection(PostgresCollection.Name)]
public sealed class ProductScreenTests : IDisposable
{
    private const string OwnerUserName = "dono-produtos";
    private const string OwnerPassword = "Catalogo!2026";
    private const string NewProductRoute = "/painel/produtos/novo";

    private readonly WebApplicationFactory<Program> factory;

    public ProductScreenTests(PostgresFixture postgres) =>
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", postgres.ConnectionString);
            builder.UseSetting($"{OwnerAccountOptions.SectionName}:UserName", OwnerUserName);
            builder.UseSetting($"{OwnerAccountOptions.SectionName}:Password", OwnerPassword);
        });

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
