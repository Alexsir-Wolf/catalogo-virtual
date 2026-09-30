using Catalogo.Data;
using Npgsql;

namespace Catalogo.Features.Media;

/// <summary>
/// Resultado da verificação do banco para o endpoint de saúde.
///
/// O que vai para a resposta é **só o que está aqui**, e o que está aqui é deliberadamente pobre:
/// tipos de exceção e um código de estado do Postgres. A mensagem da exceção fica fora porque num
/// erro `28P01` ela nomeia o usuário do banco, e a forma da cadeia de conexão fica fora porque
/// nomeia o host — `/health` é público, e quem monitora não tem credencial do painel.
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
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(configuredConnectionString))
        {
            logger.LogError("Cadeia de conexão 'Default' não configurada.");

            return new DatabaseStatus(Healthy: false, Failure: "NotConfigured");
        }

        try
        {
            await using var connection = new NpgsqlConnection(
                DatabaseConnectionString.Normalize(configuredConnectionString));

            await connection.OpenAsync(cancellationToken);

            await using var command = new NpgsqlCommand(ProbeQuery, connection);
            var result = await command.ExecuteScalarAsync(cancellationToken);

            // A versão vai para o log, não para a resposta: versão exata é impressão digital que
            // não serve a quem monitora e serve a quem procura falha conhecida.
            logger.LogDebug(
                "Banco alcançável. Versão: {Versao}. Sonda: {Resultado}.",
                connection.PostgreSqlVersion,
                result);

            return new DatabaseStatus(Healthy: true);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // O detalhe vai para o log **aqui**, com a forma da cadeia de conexão: sem host nem
            // usuário, diagnosticar "o banco não responde" começa do zero.
            logger.LogError(
                exception,
                "Falha ao conectar no banco. Forma da cadeia: {Forma}.",
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
}
