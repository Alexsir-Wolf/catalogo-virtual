namespace Catalogo.Features.Media;

/// <summary>
/// Resultado da verificação do armazenamento para o endpoint de saúde.
///
/// São **duas superfícies**, e é de propósito: `Detail` é o que vai para o log — legível,
/// específico, útil para quem tem acesso ao painel da plataforma — e `Label` é o que a resposta
/// pública publica. Para quem monitora, o `status` já entregou o sinal inteiro; dizer a um anônimo
/// que a credencial do armazenamento não foi preenchida descreve o estado da configuração do
/// deploy, que é a mesma classe de informação que o 503 deixou de carregar (R-02 e R-07 de
/// REVIEW-T-28-2026-09-30).
/// </summary>
public sealed record StorageStatus(bool Healthy, string Detail, string Label);

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

    /// <summary>
    /// O rótulo público de tudo que não está saudável. Cobre "sem credencial" e "não consegui
    /// acessar" sem distinguir os dois: a distinção é acionável para quem lê o log, e para o
    /// anônimo é só a descrição de onde o deploy está quebrado.
    /// </summary>
    public const string UnavailableLabel = "unavailable";

    /// <summary>
    /// Teto de tempo da sonda do armazenamento (R-03 de REVIEW-T-28-2026-09-30).
    ///
    /// **Por que existe:** `/health` é o `healthCheckPath` declarado no `render.yaml`, e o
    /// `HttpClient` do `SupabaseObjectStorage` é registrado sem timeout próprio — fica no default
    /// de 100 segundos. Um Supabase Storage que não recusa a conexão e também não responde, que é
    /// o comportamento típico de serviço saturado, pendura a rota por esse tempo: a plataforma não
    /// recebe resposta, conclui que o processo travou e reinicia a instância. Trocar fotos
    /// quebradas por site fora do ar é exatamente o que a decisão de responder 200 com o
    /// armazenamento degradado existe para evitar.
    ///
    /// **Por que 5 segundos:** é o mesmo teto da sonda do banco, as duas rodam em série e no pior
    /// caso a resposta sai em 10s — dentro da janela da plataforma e muito abaixo dos 100s do
    /// default. É folgado para uma ida e volta HTTPS ao Supabase, que se mede em centenas de
    /// milissegundos, então não produz falso negativo em operação normal.
    /// </summary>
    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    public static async Task<StorageStatus> CheckAsync(
        IObjectStorage storage,
        ObjectStorageOptions options,
        ILogger logger,
        CancellationToken cancellationToken = default,
        bool isDevelopment = false,
        TimeSpan? probeTimeout = null)
    {
        if (!options.IsConfigured)
        {
            // Sem credencial não há o que verificar, e **em desenvolvimento isso não é falha**: é o
            // estado normal de uma máquina de trabalho ou de um ambiente de teste, e dizer
            // "degraded" ali treinaria quem monitora a ignorar o sinal. Em desenvolvimento o rótulo
            // público pode ser o próprio detalhe: não há anônimo do outro lado.
            //
            // Fora de desenvolvimento é falha de configuração, e uma das mais silenciosas que
            // existem: a chave de serviço é preenchida à mão no painel da plataforma (`sync: false`
            // no blueprint), então basta esquecê-la num serviço recriado. Antes, esse estado
            // respondia `healthy` com `not configured`, o monitor ficava verde e nenhum envio de
            // foto ou capa funcionava — que é literalmente o "healthy com a vitrine quebrada" que
            // esta verificação existe para impedir.
            return isDevelopment
                ? new StorageStatus(
                    Healthy: true,
                    Detail: "not configured (development)",
                    Label: "not configured (development)")
                : new StorageStatus(
                    Healthy: false,
                    Detail: "not configured",
                    Label: UnavailableLabel);
        }

        var timeout = probeTimeout ?? ProbeTimeout;

        // O teto combina com o token do chamador: se quem pediu desistir, a sonda para junto; se o
        // teto estourar antes, o cancelamento é **nosso** e vira resultado, não exceção.
        using var probe = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        probe.CancelAfter(timeout);

        try
        {
            await storage.DownloadAsync(options.PublicBucket, ProbeObjectName, probe.Token);

            return new StorageStatus(Healthy: true, Detail: "reachable", Label: "reachable");
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
            return new StorageStatus(Healthy: true, Detail: "reachable", Label: "reachable");
        }
        // O filtro olha **qual** token cancelou, e não o tipo da exceção: o `HttpClient` embrulha o
        // cancelamento de formas diferentes conforme onde ele acontece, e o que define o ramo é o
        // teto ter estourado com o chamador ainda esperando.
        catch (Exception)
            when (probe.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // Estouro do teto: o chamador não desistiu, nós desistimos. Uma pendura é degradação
            // como qualquer outra, e precisa **voltar como resultado** — deixar a exceção subir
            // daqui trocaria o 200 com `degraded` por um 500 na rota que a plataforma usa para
            // decidir se mantém a instância no ar.
            logger.LogError(
                "Sonda do armazenamento excedeu o teto de {TetoSegundos}s. Bucket: {Bucket}.",
                timeout.TotalSeconds,
                options.PublicBucket);

            return new StorageStatus(Healthy: false, Detail: "timeout", Label: UnavailableLabel);
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

            // Nem a mensagem nem o tipo vão para a resposta: a mensagem pode carregar host e
            // credencial, e o tipo descreve a causa a quem só tem a URL. O tipo fica no `Detail`,
            // que é do log.
            return new StorageStatus(
                Healthy: false,
                Detail: exception.GetType().Name,
                Label: UnavailableLabel);
        }
    }
}
