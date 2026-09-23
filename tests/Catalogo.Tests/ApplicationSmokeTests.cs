using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Catalogo.Tests;

[Collection(PostgresCollection.Name)]
public sealed class ApplicationSmokeTests(PostgresFixture postgres) : IDisposable
{
    private readonly WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:Default", postgres.ConnectionString));

    public void Dispose() => factory.Dispose();

    [Fact]
    public async Task Storefront_responde_na_raiz()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Vitrine_publica_nao_abre_circuito_de_servidor()
    {
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.DoesNotContain(InteractiveServerMarker, html);
    }

    /// <summary>
    /// Marcador que o Blazor emite no HTML quando um componente é entregue em modo
    /// interativo de servidor — é o que faz o cliente abrir o circuito persistente.
    /// O painel, que é o lado interativo da ADR-010, é verificado em
    /// <see cref="AuthenticationFlowTests"/>, porque exige sessão autenticada.
    /// </summary>
    public const string InteractiveServerMarker = "\"type\":\"server\"";
}
