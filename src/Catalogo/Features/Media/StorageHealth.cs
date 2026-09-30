namespace Catalogo.Features.Media;

/// <summary>Resultado da verificação do armazenamento para o endpoint de saúde.</summary>
public sealed record StorageStatus(bool Healthy, string Detail);

/// <summary>
/// Verificação do armazenamento de objeto para `/health` (T-28).
///
/// Existe porque uma falha aqui é **invisível no banco**: as páginas respondem, as consultas
/// funcionam, e as imagens não abrem. Um endpoint de saúde que só olhasse o banco responderia
/// `healthy` com a vitrine quebrada — que é o pior tipo de monitoramento, o que tranquiliza.
///
/// Recebe as dependências como parâmetros, e não um `IServiceProvider`: com o provedor, as duas
/// dependências reais ficavam escondidas e a verificação só era testável montando um contêiner —
/// que é a razão pela qual, por um tempo, **nenhum** ramo além do "sem credencial" tinha teste.
/// </summary>
public static class StorageHealth
{
    /// <summary>
    /// O nome do objeto que a sonda procura. Não existe de propósito: o que se verifica é a
    /// resposta do serviço, não a presença de um arquivo — depender de um nome real tornaria a
    /// saúde do sistema refém de alguém não apagar aquele arquivo.
    /// </summary>
    public const string ProbeObjectName = "sonda-de-saude-que-nao-existe";

    public static async Task<StorageStatus> CheckAsync(
        IObjectStorage storage,
        ObjectStorageOptions options,
        ILogger logger,
        CancellationToken cancellationToken = default,
        bool isDevelopment = false)
    {
        if (!options.IsConfigured)
        {
            // Sem credencial não há o que verificar, e **em desenvolvimento isso não é falha**: é o
            // estado normal de uma máquina de trabalho ou de um ambiente de teste, e dizer
            // "degraded" ali treinaria quem monitora a ignorar o sinal.
            //
            // Fora de desenvolvimento é falha de configuração, e uma das mais silenciosas que
            // existem: a chave de serviço é preenchida à mão no painel da plataforma (`sync: false`
            // no blueprint), então basta esquecê-la num serviço recriado. Antes, esse estado
            // respondia `healthy` com `not configured`, o monitor ficava verde e nenhum envio de
            // foto ou capa funcionava — que é literalmente o "healthy com a vitrine quebrada" que
            // esta verificação existe para impedir.
            return isDevelopment
                ? new StorageStatus(Healthy: true, "not configured (development)")
                : new StorageStatus(Healthy: false, "not configured");
        }

        try
        {
            await storage.DownloadAsync(options.PublicBucket, ProbeObjectName, cancellationToken);

            return new StorageStatus(Healthy: true, "reachable");
        }
        catch (HttpRequestException exception)
            when (exception.StatusCode is System.Net.HttpStatusCode.NotFound)
        {
            // "Não encontrado" é a resposta **esperada**: o serviço respondeu, autenticou e
            // procurou. É exatamente o que a verificação queria saber.
            //
            // **Limite conhecido:** `404` também é a resposta de bucket inexistente, então um erro
            // de digitação no nome do bucket sai daqui como saudável com as imagens quebradas.
            // Distinguir os dois pede listar o bucket em vez de ler um objeto, e isso está
            // registrado como pendência em vez de suposto resolvido.
            return new StorageStatus(Healthy: true, "reachable");
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // O detalhe vai para o log **aqui**, e não no chamador: quem chama recebe um resultado,
            // não uma exceção, então deixar o registro para ele significava não registrar nada —
            // e diagnosticar "o armazenamento está degradado" sem status, URL ou corpo começa do
            // zero, que é o que o critério de T-28 existe para impedir.
            logger.LogError(
                exception,
                "Armazenamento inacessível na verificação de saúde. Bucket: {Bucket}.",
                options.PublicBucket);

            // Só o tipo vai para a resposta: a mensagem pode carregar host e credencial.
            return new StorageStatus(Healthy: false, exception.GetType().Name);
        }
    }
}
