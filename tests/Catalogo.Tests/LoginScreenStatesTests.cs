using Catalogo.Features.Account;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Catalogo.Tests;

/// <summary>
/// Os quatro estados declarados em UI-03. O estado de envio é resolvido no cliente, então
/// o que se pode afirmar aqui é que a tela chega ao navegador com o mecanismo instalado.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class LoginScreenStatesTests : IDisposable
{
    private const string OwnerUserName = "dono-estados";
    private const string OwnerPassword = "Catalogo!2026";

    private readonly WebApplicationFactory<Program> factory;

    public LoginScreenStatesTests(PostgresFixture postgres) =>
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", postgres.ConnectionString);
            builder.UseSetting($"{OwnerAccountOptions.SectionName}:UserName", OwnerUserName);
            builder.UseSetting($"{OwnerAccountOptions.SectionName}:Password", OwnerPassword);
        });

    public void Dispose() => factory.Dispose();

    [Fact]
    public async Task UI_03_enviando_a_tela_instala_a_trava_de_envio_duplicado()
    {
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(PanelAuthentication.LoginPath);

        Assert.Contains("data-estado-enviando=\"acesso\"", html);
        Assert.Contains("dataset.enviando", html);
    }

    [Fact]
    public async Task Campo_de_senha_oferece_alternar_visibilidade()
    {
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(PanelAuthentication.LoginPath);

        Assert.Contains("data-ver-senha=\"senha\"", html);
        Assert.Contains("aria-label=\"Mostrar senha\"", html);

        // Nasce oculto: revelar é ação deliberada de quem está na frente da tela.
        Assert.Contains("aria-pressed=\"false\"", html);
        Assert.Contains("type=\"password\"", html);
    }

    [Fact]
    public async Task UI_03_enviando_nao_desabilita_campos_que_precisam_ser_enviados()
    {
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(PanelAuthentication.LoginPath);

        // Campo desabilitado não vai no POST: a trava usa readOnly nos campos e
        // disabled apenas no botão. Trocar isso quebraria a autenticação em silêncio.
        Assert.Contains("field.readOnly = true", html);
        Assert.DoesNotContain("field.disabled = true", html);
    }
}
