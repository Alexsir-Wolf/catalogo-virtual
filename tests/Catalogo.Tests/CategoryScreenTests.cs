using System.Net;
using System.Text.RegularExpressions;
using Catalogo.Features.Account;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Catalogo.Tests;

[Collection(PostgresCollection.Name)]
public sealed class CategoryScreenTests : IDisposable
{
    private const string OwnerUserName = "dono-categorias";
    private const string OwnerPassword = "Catalogo!2026";
    private const string Route = "/painel/categorias";

    private readonly WebApplicationFactory<Program> factory;

    public CategoryScreenTests(PostgresFixture postgres) =>
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", postgres.ConnectionString);
            builder.UseSetting($"{OwnerAccountOptions.SectionName}:UserName", OwnerUserName);
            builder.UseSetting($"{OwnerAccountOptions.SectionName}:Password", OwnerPassword);
        });

    public void Dispose() => factory.Dispose();

    [Fact]
    public async Task Tela_de_categorias_exige_autenticacao()
    {
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith(PanelAuthentication.LoginPath, response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Tela_autenticada_oferece_a_criacao_e_um_dos_dois_estados()
    {
        using var client = await SignedInClientAsync();

        var html = await client.GetStringAsync(Route);

        Assert.Contains("Nova categoria", html);

        var empty = html.Contains("data-estado=\"vazio\"", StringComparison.Ordinal);
        var populated = html.Contains("data-estado=\"default\"", StringComparison.Ordinal);

        // Nunca os dois, nunca nenhum: sem categorias a tela convida a criar a primeira,
        // e com categorias mostra a lista — não uma tabela em branco (UI-06).
        Assert.True(empty ^ populated, "A tela não renderizou exatamente um dos dois estados.");
    }

    [Fact]
    public async Task Tela_de_categorias_roda_em_modo_interativo_de_servidor()
    {
        using var client = await SignedInClientAsync();

        var html = await client.GetStringAsync(Route);

        // Sem o circuito, os botões de renomear e adicionar não reagem (ADR-010).
        Assert.Contains(ApplicationSmokeTests.InteractiveServerMarker, html);
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
