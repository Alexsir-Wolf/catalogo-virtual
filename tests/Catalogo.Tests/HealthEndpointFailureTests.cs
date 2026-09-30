using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Catalogo.Features.Media;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Catalogo.Tests;

/// <summary>
/// O ramo de **falha** da verificação do banco (T-28).
///
/// Existe porque os casos de fumaça rodam com o banco de pé, e o único ramo da resposta capaz de
/// vazar segredo é o de falha: a mensagem do Npgsql num erro de autenticação nomeia o usuário, e a
/// forma da cadeia de conexão nomeia o host. Enquanto nenhum caso forçava esse ramo, reinserir o
/// vazamento mantinha a suíte verde — provado por mutação no review de T-28.
///
/// **Por que a maior parte não é teste de HTTP:** com um banco inalcançável a aplicação não sobe,
/// porque aplica migrações na partida. Foi essa impossibilidade que fez a verificação sair do
/// `MapGet` para uma peça própria — o mesmo movimento que `StorageHealth` já tinha feito, e pela
/// mesma razão.
///
/// **Os dois casos do 503 são de HTTP, e precisam ser:** o que eles guardam é o corpo público, e
/// nenhum teste da peça interna prova o que o endpoint publica. A aplicação sobe com a cadeia boa e
/// a cadeia da sonda é trocada depois — ver `ProbeConnectionStringOverride`.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class HealthEndpointFailureTests(PostgresFixture postgres)
{
    /// <summary>
    /// Host e usuário inventados, e distintos o bastante para serem procurados no resultado. Um
    /// host que não resolve falha sem esperar tempo de rede.
    /// </summary>
    private const string Host = "banco-que-nao-existe.invalido";

    private const string User = "usuario-secreto-do-banco";

    private const string Password = "senha-secreta-do-banco";

    private static string UnreachableConnectionString =>
        $"Host={Host};Port=5432;Database=catalogo;Username={User};Password={Password};Timeout=2";

    [Fact]
    public async Task Banco_inalcancavel_nao_e_saudavel()
    {
        var status = await DatabaseHealth.CheckAsync(
            UnreachableConnectionString,
            NullLogger.Instance);

        Assert.False(status.Healthy);
    }

    /// <summary>
    /// **O caso que faltava, e ele precisa de um servidor de verdade.**
    ///
    /// A primeira versão deste caso apontava para um host inexistente, e não mordia: a mensagem de
    /// DNS é "este host não é conhecido" e não nomeia ninguém — trocar o tipo da exceção pela
    /// mensagem mantinha o caso verde. O vazamento real é o `28P01`, em que o **servidor** responde
    /// e a mensagem sai como `password authentication failed for user "..."`, com o nome do usuário
    /// dentro. Por isso a conexão é contra o Postgres do contêiner, com um usuário que não existe.
    ///
    /// `/health` é público: quem monitora não tem credencial do painel, e não deve receber a
    /// descrição da infraestrutura junto com o aviso de que ela caiu.
    /// </summary>
    [Fact]
    public async Task O_resultado_da_falha_nao_carrega_usuario_nem_senha()
    {
        var status = await DatabaseHealth.CheckAsync(
            RejectedCredentialConnectionString(),
            NullLogger.Instance);

        Assert.False(status.Healthy);

        var published = string.Join(" ", status.Failure, status.Cause, status.SqlState);

        Assert.DoesNotContain(User, published, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Password, published, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A cadeia de conexão rejeitada, montada sobre o servidor do contêiner: mesmo endereço e porta,
    /// usuário e senha que não existem. É o que produz o `28P01` cuja mensagem nomeia o usuário.
    /// </summary>
    private string RejectedCredentialConnectionString()
    {
        var builder = new Npgsql.NpgsqlConnectionStringBuilder(postgres.ConnectionString)
        {
            Username = User,
            Password = Password,
            Timeout = 5
        };

        return builder.ConnectionString;
    }

    /// <summary>
    /// O que o resultado **leva** é o tipo da falha — e leva para o log, não para a resposta: quem
    /// investiga precisa distinguir "não resolveu o nome" de "senha recusada", e quem só tem a URL
    /// pública não. A afirmação aqui é sobre o resultado interno; o que sai pelo HTTP está nos dois
    /// casos do 503.
    /// </summary>
    [Fact]
    public async Task O_resultado_da_falha_identifica_o_tipo_da_causa()
    {
        var status = await DatabaseHealth.CheckAsync(
            UnreachableConnectionString,
            NullLogger.Instance);

        Assert.False(string.IsNullOrWhiteSpace(status.Failure));
    }

    /// <summary>
    /// A falha é **registrada**, com a forma da cadeia de conexão. Sem esse registro, diagnosticar
    /// "o banco não responde" começa do zero — e é para o log que o detalhe foi movido quando saiu
    /// da resposta.
    /// </summary>
    [Fact]
    public async Task A_falha_e_registrada_com_severidade_de_erro()
    {
        var logger = new RecordingLogger();

        await DatabaseHealth.CheckAsync(UnreachableConnectionString, logger);

        Assert.Contains(LogLevel.Error, logger.Levels);
    }

    /// <summary>
    /// **O teto de tempo volta como resultado, não como exceção** (R-03 do review de T-28).
    ///
    /// `/health` é o `healthCheckPath` do `render.yaml`: uma sonda pendurada não responde "não
    /// saudável", não responde nada, e a plataforma trata a instância como travada e reinicia. Com
    /// o teto já estourado, a sonda precisa concluir sozinha — deixar o cancelamento subir viraria
    /// 500 em vez de um estado reportado.
    /// </summary>
    [Fact]
    public async Task Sonda_do_banco_estoura_o_teto_e_volta_como_nao_saudavel()
    {
        // Um servidor que aceita a conexão e nunca responde: é o estado que o teto existe para
        // cobrir, e o único que nem a recusa nem o erro de nome reproduzem. Sem teto, quem espera
        // é o Npgsql, com os 15 segundos do default dele.
        var silentServer = new TcpListener(IPAddress.Loopback, 0);
        silentServer.Start();

        try
        {
            var port = ((IPEndPoint)silentServer.LocalEndpoint).Port;
            var logger = new RecordingLogger();

            var status = await DatabaseHealth.CheckAsync(
                $"Host=127.0.0.1;Port={port};Database=catalogo;Username={User};Password={Password}",
                logger,
                probeTimeout: TimeSpan.FromMilliseconds(200));

            Assert.False(status.Healthy);
            Assert.Equal("Timeout", status.Failure);
            Assert.Contains(LogLevel.Error, logger.Levels);
        }
        finally
        {
            silentServer.Stop();
        }
    }

    /// <summary>
    /// **O corpo público do 503, pelo HTTP, com o banco recusando a credencial de verdade.**
    ///
    /// Este é o caso que faltava: o ramo de falha do endpoint nunca era exercitado por HTTP, então
    /// acrescentar um campo à resposta — o `sqlState`, o host, o que fosse — mantinha a suíte verde
    /// (R-05 do review de T-28). A aplicação sobe com a cadeia boa, porque aplica migrações na
    /// partida; a cadeia da **sonda** é trocada depois, já com o host de pé, porque o endpoint lê a
    /// configuração a cada requisição.
    ///
    /// A afirmação é sobre o **conjunto de chaves**: qualquer campo novo, mesmo de nome inocente,
    /// derruba o caso e obriga quem o acrescentou a justificá-lo.
    /// </summary>
    [Fact]
    public async Task A_resposta_de_503_publica_apenas_o_estado_do_servico()
    {
        var probeConnectionString = new ProbeConnectionStringOverride();

        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:Default", postgres.ConnectionString);
                builder.ConfigureAppConfiguration(configuration =>
                    configuration.Add(probeConnectionString));
            });

        using var client = factory.CreateClient();

        probeConnectionString.Use(RejectedCredentialConnectionString());

        using var response = await client.GetAsync("/health");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        using var published = JsonDocument.Parse(body);
        var fields = published.RootElement.EnumerateObject().Select(field => field.Name).ToArray();

        Assert.Equal(new[] { "status" }, fields);
        Assert.Equal("unhealthy", published.RootElement.GetProperty("status").GetString());
    }

    /// <summary>
    /// E o mesmo corpo não carrega nem a causa nem o `SqlState`: `28P01` (senha recusada),
    /// `3D000` (banco não existe) e uma falha de socket são estados distintos da infraestrutura, e
    /// distingui-los para quem só tem a URL é publicar a topologia interna. O detalhe está no log,
    /// onde quem investiga tem acesso.
    /// </summary>
    [Fact]
    public async Task A_resposta_de_503_nao_carrega_causa_sql_state_nem_usuario()
    {
        var probeConnectionString = new ProbeConnectionStringOverride();

        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:Default", postgres.ConnectionString);
                builder.ConfigureAppConfiguration(configuration =>
                    configuration.Add(probeConnectionString));
            });

        using var client = factory.CreateClient();

        probeConnectionString.Use(RejectedCredentialConnectionString());

        using var response = await client.GetAsync("/health");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        Assert.DoesNotContain("sqlState", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("28P01", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cause", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("failure", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(User, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Password, body, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Cadeia de conexão ausente é falha de configuração, não exceção: sem ela a aplicação não tem
    /// como servir nada, e o endpoint precisa dizer isso em vez de estourar.
    /// </summary>
    [Fact]
    public async Task Cadeia_de_conexao_ausente_e_reportada_como_nao_configurada()
    {
        var status = await DatabaseHealth.CheckAsync("   ", NullLogger.Instance);

        Assert.False(status.Healthy);
        Assert.Equal("NotConfigured", status.Failure);
    }

    /// <summary>
    /// A cadeia de conexão que a **sonda** enxerga, trocável depois de a aplicação estar de pé.
    ///
    /// É o que torna o 503 alcançável por HTTP: com a cadeia ruim desde o início a aplicação nem
    /// sobe, porque aplica migrações na partida — e foi essa impossibilidade que, por um tempo,
    /// deixou o ramo de falha do endpoint sem nenhum teste de verdade. As opções do `DbContext` são
    /// montadas uma vez, na partida, com a cadeia boa; o endpoint de saúde relê a configuração a
    /// cada requisição, então só ele passa a apontar para a credencial recusada.
    ///
    /// Entra por último na configuração, e vazio: enquanto não é usado, quem responde é o valor
    /// que o host de teste já tinha definido.
    /// </summary>
    private sealed class ProbeConnectionStringOverride : ConfigurationProvider, IConfigurationSource
    {
        private const string Key = "ConnectionStrings:Default";

        public IConfigurationProvider Build(IConfigurationBuilder builder) => this;

        public void Use(string connectionString)
        {
            Data[Key] = connectionString;
            OnReload();
        }
    }

    private sealed class RecordingLogger : ILogger
    {
        private readonly List<LogLevel> levels = [];

        public IReadOnlyList<LogLevel> Levels => levels;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) => levels.Add(logLevel);
    }
}
