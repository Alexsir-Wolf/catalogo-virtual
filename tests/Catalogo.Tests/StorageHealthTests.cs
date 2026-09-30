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
    /// O detalhe da configuração fica no log: o rótulo público é opaco. Dizer `not configured` na
    /// resposta informava a um anônimo, com precisão, que a credencial do armazenamento não foi
    /// preenchida no painel — ou seja, em que estado o deploy está quebrado.
    /// </summary>
    [Fact]
    public async Task Sem_credencial_o_rotulo_publico_nao_descreve_a_configuracao()
    {
        var status = await StorageHealth.CheckAsync(
            new ThrowingStorage(new HttpRequestException("não deveria ser chamado")),
            new ObjectStorageOptions(),
            NullLogger.Instance);

        Assert.Equal(StorageHealth.UnavailableLabel, status.Label);
        Assert.DoesNotContain("configured", status.Label);
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
    /// E o **rótulo público** não carrega nem o tipo: o tipo da exceção descreve a causa da falha
    /// a quem só tem a URL, e `/health` é anônimo. Quem investiga lê o `Detail` no log.
    /// </summary>
    [Fact]
    public async Task O_rotulo_publico_da_falha_nao_carrega_o_tipo_da_excecao()
    {
        var status = await StorageHealth.CheckAsync(
            new ThrowingStorage(new HttpRequestException("500 do serviço")),
            Configured(),
            NullLogger.Instance);

        Assert.Equal(StorageHealth.UnavailableLabel, status.Label);
        Assert.DoesNotContain(nameof(HttpRequestException), status.Label);
    }

    /// <summary>
    /// **Pendura é degradação, não exceção.**
    ///
    /// Um armazenamento que não recusa a conexão e também não responde pendurava a sonda até os
    /// 100 segundos do default do `HttpClient`, e a plataforma — que usa `/health` como
    /// `healthCheckPath` — não recebe resposta, conclui que o processo travou e reinicia a
    /// instância. O teto precisa voltar como **resultado**: deixar o cancelamento subir trocaria
    /// o 200 com `degraded` por um 500, na rota que decide se a instância fica no ar.
    /// </summary>
    [Fact]
    public async Task Sonda_pendurada_estoura_o_teto_e_volta_como_degradada()
    {
        var logger = new RecordingLogger();

        var status = await StorageHealth.CheckAsync(
            new HangingStorage(),
            Configured(),
            logger,
            probeTimeout: TimeSpan.FromMilliseconds(50));

        Assert.False(status.Healthy);
        Assert.Equal("timeout", status.Detail);
        Assert.Contains(LogLevel.Error, logger.Levels);
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

    /// <summary>
    /// O armazenamento que nunca responde — o estado que não é recusa nem erro, e que é o único
    /// capaz de pendurar a rota de saúde. Só o cancelamento o solta.
    /// </summary>
    private sealed class HangingStorage : IObjectStorage
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

        public async Task<byte[]> DownloadAsync(
            string bucket,
            string objectName,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);

            return [];
        }

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
