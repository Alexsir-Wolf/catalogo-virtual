using Catalogo.Data;
using Npgsql;

namespace Catalogo.Features.Media;

/// <summary>
/// Resultado da verificação do banco para o endpoint de saúde.
///
/// O que está aqui é **diagnóstico interno**, não corpo de resposta: `/health` é público e devolve
/// apenas que o serviço está indisponível. O tipo da exceção, o tipo da causa e o código de estado
/// do Postgres existem para quem lê o log — distinguir "não resolvi o host" de "senha recusada" de
/// "banco não existe" é a topologia interna, e ela não é publicada a anônimo (R-02 de
/// REVIEW-T-28-2026-09-30).
/// </summary>
public sealed record DatabaseStatus(
    bool Healthy,
    string? Failure = null,
    string? Cause = null,
    string? SqlState = null);

/// <summary>
/// Verificação do banco para `/health` (T-28).
///
/// Existe como peça separada pela mesma razão que `StorageHealth`: enquanto a verificação vivia
/// dentro do `MapGet`, o **único** ramo capaz de vazar segredo — o `catch` — não tinha teste, porque
/// exercitá-lo pelo HTTP exigiria um banco inalcançável, e com um banco inalcançável a aplicação
/// não sobe (as migrações são aplicadas na partida). O resultado é que reinserir o vazamento mantinha
/// a suíte verde, o que foi provado por mutação no review de T-28.
/// </summary>
public static class DatabaseHealth
{
    public static async Task<DatabaseStatus> CheckAsync(
        string? configuredConnectionString,
        ILogger logger,
        CancellationToken cancellationToken = default,
        TimeSpan? probeTimeout = null)
    {
        if (string.IsNullOrWhiteSpace(configuredConnectionString))
        {
            logger.LogError("Cadeia de conexão 'Default' não configurada.");

            return new DatabaseStatus(Healthy: false, Failure: "NotConfigured");
        }

        var timeout = probeTimeout ?? ProbeTimeout;

        // O teto combina com o token do chamador: se quem pediu desistir, a sonda para junto; se o
        // teto estourar antes, o cancelamento é **nosso** e vira resultado, não exceção.
        using var probe = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        probe.CancelAfter(timeout);

        try
        {
            await using var connection = new NpgsqlConnection(
                DatabaseConnectionString.Normalize(configuredConnectionString));

            await connection.OpenAsync(probe.Token);

            await using var command = new NpgsqlCommand(ProbeQuery, connection);
            var result = await command.ExecuteScalarAsync(probe.Token);

            // A versão vai para o log, não para a resposta: versão exata é impressão digital que
            // não serve a quem monitora e serve a quem procura falha conhecida.
            logger.LogDebug(
                "Banco alcançável. Versão: {Versao}. Sonda: {Resultado}.",
                connection.PostgreSqlVersion,
                result);

            return new DatabaseStatus(Healthy: true);
        }
        // O filtro olha **qual** token cancelou, e não o tipo da exceção: o Npgsql às vezes embrulha
        // o cancelamento durante a abertura numa exceção própria, e o que define o ramo é o teto
        // ter estourado com o chamador ainda esperando.
        catch (Exception)
            when (probe.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // Estouro do teto: o chamador não desistiu, nós desistimos. Banco que não responde
            // dentro do teto **é** banco indisponível para efeito de saúde, e dizer isso é o
            // contrário de deixar a exceção subir — que viraria 500 e, pior, uma resposta tardia.
            logger.LogError(
                "Sonda do banco excedeu o teto de {TetoSegundos}s. Forma da cadeia: {Forma}.",
                timeout.TotalSeconds,
                DatabaseConnectionStringShape.Describe(configuredConnectionString));

            return new DatabaseStatus(Healthy: false, Failure: "Timeout");
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // O detalhe vai para o log **aqui**, e só para cá: com a forma da cadeia de conexão,
            // sem host nem usuário, e com o tipo da falha e o `SqlState` como campos próprios —
            // é o que a resposta pública deixou de carregar, e o log é onde ele é acionável, por
            // quem tem acesso ao painel da plataforma.
            logger.LogError(
                exception,
                "Falha ao conectar no banco. Falha: {Falha}. Causa: {Causa}. Estado SQL: {EstadoSql}. "
                + "Forma da cadeia: {Forma}.",
                exception.GetType().Name,
                exception.InnerException?.GetType().Name,
                (exception as PostgresException)?.SqlState,
                DatabaseConnectionStringShape.Describe(configuredConnectionString));

            return new DatabaseStatus(
                Healthy: false,
                Failure: exception.GetType().Name,
                Cause: exception.InnerException?.GetType().Name,
                SqlState: (exception as PostgresException)?.SqlState);
        }
    }

    /// <summary>
    /// A consulta da sonda. Existe como constante nomeada porque o que importa dela é que **chegue
    /// ao servidor** — abrir a conexão pode ser satisfeito por um pool e não provar nada.
    /// </summary>
    public const string ProbeQuery = "select 1";

    /// <summary>
    /// Teto de tempo da sonda do banco (R-03 de REVIEW-T-28-2026-09-30).
    ///
    /// **Por que existe:** `/health` é o `healthCheckPath` declarado no `render.yaml`. Uma sonda
    /// pendurada não devolve "não saudável" — não devolve nada, e a plataforma conclui que o
    /// processo travou e reinicia a instância. É exatamente o desfecho que a decisão de **não**
    /// devolver 503 por armazenamento degradado existe para evitar, chegando pela porta dos fundos.
    ///
    /// **Por que 5 segundos:** o default herdado é 15s do Npgsql, maior que qualquer janela de
    /// health check, e as duas sondas rodam em série — no pior caso a resposta sai em 10s, que
    /// ainda cabe na janela da plataforma. Do outro lado, 5s é folgado para DNS, TLS e ida e volta
    /// até o pooler na mesma região, que se mede em centenas de milissegundos: o teto não produz
    /// falso negativo em operação normal. Uma sonda que passa disso já respondeu a pergunta.
    /// </summary>
    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);
}
