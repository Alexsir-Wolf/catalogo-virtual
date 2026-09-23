using System.Net;
using Catalogo.Features.Account;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Catalogo.Tests;

[Collection(PostgresCollection.Name)]
public sealed class OwnerAccountSeedingTests(PostgresFixture postgres)
{
    /// <summary>
    /// Credencial malformada tranca o painel, mas não pode derrubar a vitrine pública
    /// junto — é a área de maior carga e maior valor do sistema (ADR-010).
    /// </summary>
    [Fact]
    public async Task Senha_semeada_fora_da_politica_nao_derruba_a_vitrine()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", postgres.ConnectionString);
            builder.UseSetting($"{OwnerAccountOptions.SectionName}:UserName", "dono-invalido");
            // Sem maiúscula: a política padrão do Identity recusa.
            builder.UseSetting($"{OwnerAccountOptions.SectionName}:Password", "alex@@12343");
        });

        using var client = factory.CreateClient();

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Configuracao_ausente_nao_derruba_a_vitrine()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:Default", postgres.ConnectionString));

        using var client = factory.CreateClient();

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
