using System.Net;
using System.Text.Json;
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
    /// T-28: o endpoint de saúde responde e verifica **banco e armazenamento**. Verificar só o
    /// banco daria `healthy` com a vitrine sem imagens — o pior tipo de monitoramento, o que
    /// tranquiliza sem motivo.
    /// </summary>
    [Fact]
    public async Task O_endpoint_de_saude_verifica_banco_e_armazenamento()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");
        var body = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();

        Assert.Contains("\"status\":\"healthy\"", body);
        Assert.Contains("\"database\":\"reachable\"", body);

        // Sem credencial do Supabase o armazenamento reporta `not configured` e **não** derruba
        // a saúde: é o estado de um ambiente de teste, e chamar isso de degradado treinaria quem
        // monitora a ignorar o sinal.
        Assert.Contains("\"storage\":", body);
    }

    /// <summary>
    /// O endpoint de saúde é **público** — quem monitora não tem credencial do painel. Em
    /// contrapartida, a resposta não pode carregar segredo: o que ela traz é a versão do banco e
    /// um rótulo de estado, nunca host, usuário ou chave.
    /// </summary>
    [Fact]
    public async Task O_endpoint_de_saude_nao_expoe_credencial_nem_host()
    {
        using var client = factory.CreateClient();

        var body = await client.GetStringAsync("/health");

        Assert.DoesNotContain("Password", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ServiceKey", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("supabase.co", body, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// O conjunto de campos que `/health` publica é **fechado**: `status`, `database` e `storage`,
    /// e nada mais.
    ///
    /// Afirmar sobre o conjunto, e não sobre a ausência de um segredo específico, é o que resiste a
    /// mutação: qualquer campo novo — mesmo de nome inocente, mesmo sem valor sensível hoje —
    /// derruba este caso e obriga quem o acrescentou a justificá-lo. Foi assim que `failure`,
    /// `cause` e `sqlState` voltaram a existir só no log.
    /// </summary>
    [Fact]
    public async Task O_endpoint_de_saude_publica_um_conjunto_fechado_de_campos()
    {
        using var client = factory.CreateClient();

        var body = await client.GetStringAsync("/health");

        using var published = JsonDocument.Parse(body);
        var fields = published.RootElement.EnumerateObject().Select(field => field.Name).ToArray();

        Assert.Equal(new[] { "status", "database", "storage" }, fields);
    }

    /// <summary>
    /// A versão exata do banco **não** sai na resposta. Ela não serve a quem monitora — um
    /// indicador de alcançabilidade basta — e serve a quem procura falha conhecida de versão numa
    /// rota pública.
    /// </summary>
    [Fact]
    public async Task O_endpoint_de_saude_nao_publica_a_versao_do_banco()
    {
        using var client = factory.CreateClient();

        var body = await client.GetStringAsync("/health");

        Assert.DoesNotMatch(@"""database"":""\d+\.\d+", body);
    }

    /// <summary>
    /// Marcador que o Blazor emite no HTML quando um componente é entregue em modo
    /// interativo de servidor — é o que faz o cliente abrir o circuito persistente.
    /// O painel, que é o lado interativo da ADR-010, é verificado em
    /// <see cref="AuthenticationFlowTests"/>, porque exige sessão autenticada.
    /// </summary>
    public const string InteractiveServerMarker = "\"type\":\"server\"";
}
