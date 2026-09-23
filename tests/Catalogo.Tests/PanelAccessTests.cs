using System.Net;
using Catalogo.Features.Account;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Catalogo.Tests;

[Collection(PostgresCollection.Name)]
public sealed class PanelAccessTests : IDisposable
{
    private const string OwnerUserName = "dono";
    private const string OwnerPassword = "Catalogo!2026";

    private readonly WebApplicationFactory<Program> factory;

    public PanelAccessTests(PostgresFixture postgres) =>
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", postgres.ConnectionString);
            builder.UseSetting($"{OwnerAccountOptions.SectionName}:UserName", OwnerUserName);
            builder.UseSetting($"{OwnerAccountOptions.SectionName}:Password", OwnerPassword);
        });

    public void Dispose() => factory.Dispose();

    [Fact]
    public async Task CA_26_acesso_anonimo_ao_painel_e_redirecionado_ao_login()
    {
        using var client = CreateClientWithoutRedirects();

        var response = await client.GetAsync("/painel");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith(PanelAuthentication.LoginPath, response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Rota_publica_continua_acessivel_sem_autenticacao()
    {
        using var client = CreateClientWithoutRedirects();

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Tela_de_acesso_e_alcancavel_sem_autenticacao()
    {
        using var client = CreateClientWithoutRedirects();

        var response = await client.GetAsync(PanelAuthentication.LoginPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Nao_existe_rota_de_registro_nem_de_recuperacao_de_senha()
    {
        using var client = CreateClientWithoutRedirects();

        string[] absentRoutes = ["/painel/registrar", "/painel/recuperar", "/painel/esqueci-minha-senha"];

        foreach (var route in absentRoutes)
        {
            var response = await client.GetAsync(route);

            // O gate do painel responde com redirecionamento ao login; o que não pode
            // acontecer é uma dessas rotas existir e responder com conteúdo próprio.
            Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task Tela_de_acesso_informa_que_a_redefinicao_exige_o_servidor()
    {
        using var client = CreateClientWithoutRedirects();

        var html = await client.GetStringAsync(PanelAuthentication.LoginPath);

        Assert.Contains("servidor", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("esqueci minha senha", html, StringComparison.OrdinalIgnoreCase);
    }

    private HttpClient CreateClientWithoutRedirects() =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
}
