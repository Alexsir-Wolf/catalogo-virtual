using Microsoft.Extensions.Options;

namespace Catalogo.Features.Media;

/// <summary>Resultado da verificação do armazenamento para o endpoint de saúde.</summary>
public sealed record StorageStatus(bool Healthy, string Detail);

/// <summary>
/// Verificação do armazenamento de objeto para `/health` (T-28).
///
/// Existe porque uma falha aqui é **invisível no banco**: as páginas respondem, as consultas
/// funcionam, e as imagens não abrem. Um endpoint de saúde que só olhasse o banco responderia
/// `healthy` com a vitrine quebrada — que é o pior tipo de monitoramento, o que tranquiliza.
/// </summary>
public static class StorageHealth
{
    public static async Task<StorageStatus> CheckAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        var options = services.GetRequiredService<IOptions<ObjectStorageOptions>>().Value;

        if (!options.IsConfigured)
        {
            // Sem credencial não há o que verificar, e isso **não** é falha: é o estado de um
            // ambiente de desenvolvimento ou de teste. Dizer "degraded" aqui treinaria quem
            // monitora a ignorar o sinal.
            return new StorageStatus(Healthy: true, "not configured");
        }

        try
        {
            using var scope = services.CreateScope();
            var storage = scope.ServiceProvider.GetRequiredService<IObjectStorage>();

            // Leitura de um objeto que **não existe**: o que se verifica é a resposta do serviço,
            // não a presença de um arquivo específico. Depender de um nome fixo tornaria a saúde
            // do sistema refém de alguém não apagar aquele arquivo.
            await storage.DownloadAsync(options.PublicBucket, ProbeObjectName, cancellationToken);

            return new StorageStatus(Healthy: true, "reachable");
        }
        catch (HttpRequestException exception)
            when (exception.StatusCode is System.Net.HttpStatusCode.NotFound)
        {
            // "Não encontrado" é a resposta **esperada**: o serviço respondeu, autenticou e
            // procurou. É exatamente o que a verificação queria saber.
            return new StorageStatus(Healthy: true, "reachable");
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // A mensagem pode carregar host e credencial, então só o tipo vai para a resposta.
            // O detalhe fica no log de quem chamou.
            return new StorageStatus(Healthy: false, exception.GetType().Name);
        }
    }

    private const string ProbeObjectName = "sonda-de-saude-que-nao-existe";
}
