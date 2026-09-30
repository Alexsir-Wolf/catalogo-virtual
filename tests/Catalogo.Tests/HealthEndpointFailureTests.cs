using Catalogo.Features.Media;
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
/// **Por que não é teste de HTTP:** com um banco inalcançável a aplicação não sobe, porque aplica
/// migrações na partida. Foi essa impossibilidade que fez a verificação sair do `MapGet` para uma
/// peça própria — o mesmo movimento que `StorageHealth` já tinha feito, e pela mesma razão.
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
    /// O que o resultado **leva** é o tipo da falha: quem investiga precisa distinguir "não
    /// resolveu o nome" de "senha recusada" sem abrir o log, e nenhum dos dois tipos nomeia
    /// infraestrutura.
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
