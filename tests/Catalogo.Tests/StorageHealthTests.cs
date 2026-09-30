using System.Net;
using Catalogo.Features.Media;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Catalogo.Tests;

/// <summary>
/// A verificação do armazenamento para `/health` (T-28).
///
/// São três ramos e cada um significa uma coisa diferente para quem monitora, então cada um tem
/// caso próprio. Por um tempo **nenhum** deles além do "sem credencial" tinha teste, e a razão era
/// de projeto: a verificação recebia um `IServiceProvider` e só era exercitável montando um
/// contêiner. Depender das dependências reais é o que tornou isto possível.
/// </summary>
public sealed class StorageHealthTests
{
    /// <summary>
    /// Sem credencial **em desenvolvimento** é saudável: é o estado normal de uma máquina de
    /// trabalho, e chamar isso de falha treinaria quem monitora a ignorar o sinal.
    /// </summary>
    [Fact]
    public async Task Sem_credencial_em_desenvolvimento_a_verificacao_e_saudavel()
    {
        var status = await StorageHealth.CheckAsync(
            new ThrowingStorage(new HttpRequestException("não deveria ser chamado")),
            new ObjectStorageOptions(),
            NullLogger.Instance,
            isDevelopment: true);

        Assert.True(status.Healthy);
        Assert.Contains("not configured", status.Detail);
    }

    /// <summary>
    /// Sem credencial **fora** de desenvolvimento é falha de configuração — e uma das mais
    /// silenciosas que existem.
    ///
    /// A chave de serviço é preenchida à mão no painel da plataforma, então basta esquecê-la num
    /// serviço recriado. Antes, esse estado respondia `healthy`: o monitor ficava verde e nenhum
    /// envio de foto ou capa funcionava — o "healthy com a vitrine quebrada" que esta verificação
    /// existe justamente para impedir.
    /// </summary>
    [Fact]
    public async Task Sem_credencial_fora_de_desenvolvimento_e_falha_de_configuracao()
    {
        var status = await StorageHealth.CheckAsync(
            new ThrowingStorage(new HttpRequestException("não deveria ser chamado")),
            new ObjectStorageOptions(),
            NullLogger.Instance);

        Assert.False(status.Healthy);
        Assert.Equal("not configured", status.Detail);
    }

    /// <summary>
    /// O serviço responde: saudável. A sonda procura um objeto que **não existe de propósito**, e
    /// `404` é a resposta esperada — o serviço respondeu, autenticou e procurou, que é tudo o que a
    /// verificação queria saber.
    /// </summary>
    [Fact]
    public async Task Objeto_inexistente_e_saudavel_porque_o_servico_respondeu()
    {
        var status = await StorageHealth.CheckAsync(
            new ThrowingStorage(new HttpRequestException("não encontrado", null, HttpStatusCode.NotFound)),
            Configured(),
            NullLogger.Instance);

        Assert.True(status.Healthy);
        Assert.Equal("reachable", status.Detail);
    }

    /// <summary>
    /// Falha de verdade: não saudável, **e registrada**. O detalhe vai para o log dentro da
    /// verificação, porque quem chama recebe um resultado e não uma exceção — deixar o registro
    /// para o chamador significava não registrar nada, e diagnosticar "o armazenamento está
    /// degradado" sem status nem URL começa do zero.
    /// </summary>
    [Fact]
    public async Task Falha_de_acesso_e_reportada_como_degradada_e_registrada()
    {
        var logger = new RecordingLogger();

        var status = await StorageHealth.CheckAsync(
            new ThrowingStorage(new HttpRequestException("500 do serviço")),
            Configured(),
            logger);

        Assert.False(status.Healthy);
        Assert.Contains(LogLevel.Error, logger.Levels);
    }

    /// <summary>
    /// A resposta carrega **só o tipo** da exceção. A mensagem pode trazer host, usuário ou chave,
    /// e `/health` é público — quem monitora não tem credencial do painel.
    /// </summary>
    [Fact]
    public async Task O_detalhe_da_falha_nao_carrega_a_mensagem_da_excecao()
    {
        var status = await StorageHealth.CheckAsync(
            new ThrowingStorage(new HttpRequestException("https://projeto.supabase.co chave-secreta")),
            Configured(),
            NullLogger.Instance);

        Assert.Equal(nameof(HttpRequestException), status.Detail);
        Assert.DoesNotContain("supabase.co", status.Detail);
        Assert.DoesNotContain("chave-secreta", status.Detail);
    }

    /// <summary>
    /// Cancelamento **não** é falha do armazenamento: a requisição de saúde foi abortada, e
    /// reportar degradado por isso produziria alarme a cada visitante que fechasse a aba.
    /// </summary>
    [Fact]
    public async Task Cancelamento_nao_e_reportado_como_falha()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => StorageHealth.CheckAsync(
            new ThrowingStorage(new OperationCanceledException()),
            Configured(),
            NullLogger.Instance,
            cancellation.Token));
    }

    private static ObjectStorageOptions Configured() => new()
    {
        Url = "https://projeto.supabase.co",
        ServiceKey = "chave-de-teste"
    };

    private sealed class ThrowingStorage(Exception exception) : IObjectStorage
    {
        public Task UploadAsync(
            string bucket,
            string objectName,
            byte[] content,
            string contentType,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteAsync(
            string bucket,
            string objectName,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<byte[]> DownloadAsync(
            string bucket,
            string objectName,
            CancellationToken cancellationToken = default) => throw exception;

        public string PublicUrlFor(string objectName) => objectName;
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
