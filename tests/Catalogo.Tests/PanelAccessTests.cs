using System.Net;
using System.Text.RegularExpressions;
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

    /// <summary>
    /// R-09 de `REVIEW-T-31-2026-09-29`: a rota de saída estava declarada em
    /// `PanelAuthentication.LogoutPath` desde T-07 e nunca existiu, então encerrar a sessão
    /// exigia trocar a senha ou esperar o cookie expirar.
    /// </summary>
    [Fact]
    public async Task RN_58_a_saida_encerra_a_sessao_do_painel()
    {
        using var client = await SignedInClientAsync();

        using var page = await client.GetAsync(PanelAuthentication.LogoutPath);
        var token = AntiforgeryTokenIn(await page.Content.ReadAsStringAsync());

        using var response = await client.PostAsync(
            PanelAuthentication.LogoutPath,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["_handler"] = "sair",
                ["__RequestVerificationToken"] = token
            }));

        response.EnsureSuccessStatusCode();

        // Depois de sair, o painel volta a ser território fechado para este cliente.
        using var afterwards = await client.GetAsync("/painel/configuracoes");

        Assert.Contains(
            PanelAuthentication.LoginPath,
            afterwards.RequestMessage!.RequestUri!.AbsolutePath);
    }

    /// <summary>
    /// A saída é `POST` com antiforgery, e não link: um `GET` que desloga é acionável por
    /// qualquer imagem ou pré-carregador apontando para a rota.
    /// </summary>
    [Fact]
    public async Task A_saida_nao_acontece_por_requisicao_de_leitura()
    {
        using var client = await SignedInClientAsync();

        using var visit = await client.GetAsync(PanelAuthentication.LogoutPath);
        visit.EnsureSuccessStatusCode();

        using var afterwards = await client.GetAsync("/painel/configuracoes");

        Assert.Equal(HttpStatusCode.OK, afterwards.StatusCode);
        Assert.DoesNotContain(
            PanelAuthentication.LoginPath,
            afterwards.RequestMessage!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task A_saida_exige_autenticacao_como_o_resto_do_painel()
    {
        using var client = CreateClientWithoutRedirects();

        var response = await client.GetAsync(PanelAuthentication.LogoutPath);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    private async Task<HttpClient> SignedInClientAsync()
    {
        var client = factory.CreateDefaultClient(new Uri("https://localhost"), new CookieHandler());

        var token = AntiforgeryTokenIn(
            await client.GetStringAsync(PanelAuthentication.LoginPath));

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

    private static string AntiforgeryTokenIn(string html) =>
        Regex.Match(
            html,
            """name="__RequestVerificationToken"[^>]*value="([^"]+)""").Groups[1].Value;

    private HttpClient CreateClientWithoutRedirects() =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
}
