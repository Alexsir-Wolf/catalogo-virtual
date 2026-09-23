using System.Net;
using System.Text.RegularExpressions;
using Catalogo.Features.Account;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Catalogo.Tests;

[Collection(PostgresCollection.Name)]
public sealed class AuthenticationFlowTests : IDisposable
{
    private const string OwnerUserName = "dono-fluxo";
    private const string OwnerPassword = "Catalogo!2026";

    private readonly WebApplicationFactory<Program> factory;

    public AuthenticationFlowTests(PostgresFixture postgres) =>
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", postgres.ConnectionString);
            builder.UseSetting($"{OwnerAccountOptions.SectionName}:UserName", OwnerUserName);
            builder.UseSetting($"{OwnerAccountOptions.SectionName}:Password", OwnerPassword);
        });

    public void Dispose() => factory.Dispose();

    [Fact]
    public async Task Senha_correta_da_acesso_ao_painel()
    {
        using var client = CreateSessionClient();

        var response = await SignInAsync(client, OwnerPassword);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var panel = await client.GetAsync(PanelAuthentication.PanelPathPrefix);

        Assert.Equal(HttpStatusCode.OK, panel.StatusCode);
    }

    [Fact]
    public async Task Painel_autenticado_e_renderizado_em_modo_interativo_de_servidor()
    {
        using var client = CreateSessionClient();
        await SignInAsync(client, OwnerPassword);

        var html = await client.GetStringAsync(PanelAuthentication.PanelPathPrefix);

        Assert.Contains(ApplicationSmokeTests.InteractiveServerMarker, html);
    }

    [Fact]
    public async Task Senha_incorreta_nao_da_acesso_ao_painel()
    {
        using var client = CreateSessionClient();

        await SignInAsync(client, "senha-errada");

        // O gate redireciona ao login; seguindo o redirecionamento, o que volta é a
        // própria tela de acesso, e não o painel.
        var html = await client.GetStringAsync(PanelAuthentication.PanelPathPrefix);

        Assert.Contains("Não há redefinição de senha pelo sistema", html);
    }

    [Fact]
    public async Task Credencial_incorreta_nao_revela_qual_campo_falhou()
    {
        using var client = CreateSessionClient();

        var response = await SignInAsync(client, "senha-errada");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("Usuário ou senha incorretos", html);
        Assert.DoesNotContain("erroCredencial\" hidden", html);
    }

    [Fact]
    public async Task CA_27_tentativas_sucessivas_malsucedidas_bloqueiam_o_acesso()
    {
        using var client = CreateSessionClient();
        var lockedOut = false;

        for (var attempt = 1; attempt <= PanelAuthentication.MaxFailedAccessAttempts + 1; attempt++)
        {
            var response = await SignInAsync(client, "senha-errada");
            var html = await response.Content.ReadAsStringAsync();

            if (html.Contains("data-estado=\"bloqueado\"", StringComparison.Ordinal))
            {
                lockedOut = true;
                break;
            }
        }

        Assert.True(lockedOut, "O acesso não foi bloqueado após as tentativas malsucedidas.");
    }

    private async Task<HttpResponseMessage> SignInAsync(HttpClient client, string password)
    {
        var page = await client.GetStringAsync(PanelAuthentication.LoginPath);
        var token = AntiforgeryToken(page);

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "acesso",
            ["Input.UserName"] = OwnerUserName,
            ["Input.Password"] = password,
            ["__RequestVerificationToken"] = token
        });

        return await client.PostAsync(PanelAuthentication.LoginPath, form);
    }

    private static string AntiforgeryToken(string html)
    {
        var match = Regex.Match(
            html,
            """name="__RequestVerificationToken"[^>]*value="([^"]+)""");

        Assert.True(match.Success, "Token antiforgery não encontrado na tela de acesso.");

        return match.Groups[1].Value;
    }

    /// <summary>
    /// O cookie de autenticação é <c>Secure</c> (ADR-006), então a sessão de teste
    /// precisa falar HTTPS — em HTTP o cookie simplesmente não é guardado.
    /// </summary>
    private HttpClient CreateSessionClient() =>
        factory.CreateDefaultClient(new Uri("https://localhost"), new CookieHandler());
}
