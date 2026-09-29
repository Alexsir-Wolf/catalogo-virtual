using Catalogo.Features.Media;

namespace Catalogo.Features.Products;

public sealed record PhotoUploadOutcome(ProductPhoto? Photo, string? Error)
{
    public bool Succeeded => Error is null;

    public static PhotoUploadOutcome Stored(ProductPhoto photo) => new(photo, null);

    /// <summary>A foto ausente é o que preserva a anterior na tela: nada a substituir.</summary>
    public static PhotoUploadOutcome Failed(string error) => new(Photo: null, error);
}

/// <summary>
/// Envio da foto pela tela de produto (UI-05). Armazenamento e banco estão do outro lado
/// da rede (ADR-018), onde indisponibilidade é evento esperado e não excepcional: aqui a
/// falha vira mensagem para a tela, porque exceção escapando de manipulador de evento do
/// painel interativo encerra o circuito e leva o formulário que o dono digitou (ADR-010).
/// </summary>
public sealed class ProductPhotoUpload(
    ProductPhotoService photoService,
    ProductMaintenance maintenance,
    ILogger<ProductPhotoUpload> logger)
{
    public const string StorageUnavailableMessage =
        "Não foi possível concluir o envio da foto agora. Tente de novo em instantes.";

    public const string TransportInterruptedMessage =
        "O envio do arquivo foi interrompido antes de terminar. Tente de novo.";

    public const string ProductUnavailableMessage =
        "Este produto não está mais disponível, então a foto não foi trocada. Ele pode ter sido excluído.";

    public async Task<PhotoUploadOutcome> StoreAsync(
        int productId,
        Stream content,
        long lengthInBytes,
        CancellationToken cancellationToken = default)
    {
        // O tamanho é conhecido antes da leitura, e o leitor do circuito estoura com
        // IOException ao passar do limite — o que faria a recusa por tamanho (RN-10)
        // chegar à tela como erro de transporte, ou o contrário.
        if (lengthInBytes > ImageValidation.MaxFileSizeInBytes)
        {
            return PhotoUploadOutcome.Failed(RejectionMessage(ImageRejection.TooLarge));
        }

        PhotoUploadResult result;

        try
        {
            result = await photoService.StoreAsync(content, lengthInBytes, cancellationToken);
        }
        catch (IOException exception)
        {
            logger.LogWarning(
                exception,
                "Leitura da foto do produto {ProductId} foi interrompida.",
                productId);

            return PhotoUploadOutcome.Failed(TransportInterruptedMessage);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(
                exception,
                "Falha ao gravar a foto do produto {ProductId} no armazenamento.",
                productId);

            return PhotoUploadOutcome.Failed(StorageUnavailableMessage);
        }

        if (!result.Succeeded)
        {
            return PhotoUploadOutcome.Failed(RejectionMessage(result.Rejection));
        }

        ProductPhoto? attached;

        try
        {
            attached = await maintenance.AttachPhotoAsync(productId, result.Photo!, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(
                exception,
                "Falha ao associar a foto ao produto {ProductId}. Derivadas gravadas sem referência, raiz {Root}.",
                productId,
                result.Photo!.OriginalFileName);

            return PhotoUploadOutcome.Failed(StorageUnavailableMessage);
        }

        if (attached is null)
        {
            logger.LogWarning(
                "Produto {ProductId} não existe mais e não recebeu a foto. Derivadas gravadas sem referência, raiz {Root}.",
                productId,
                result.Photo!.OriginalFileName);

            return PhotoUploadOutcome.Failed(ProductUnavailableMessage);
        }

        return PhotoUploadOutcome.Stored(attached);
    }

    /// <summary>
    /// Recusa e indisponibilidade têm mensagens de naturezas diferentes: a primeira pede
    /// outro arquivo, a segunda convida a repetir o mesmo envio.
    /// </summary>
    private static string RejectionMessage(ImageRejection rejection) => rejection switch
    {
        ImageRejection.NotAnImage =>
            "O arquivo não é uma imagem válida — a extensão não basta, o conteúdo é verificado.",
        ImageRejection.TooLarge =>
            $"A imagem passa de {ImageValidation.MaxFileSizeInBytes / (1024 * 1024)} MB.",
        ImageRejection.DimensionsTooLarge =>
            $"A imagem passa de {ImageValidation.MaxDimension} px de lado.",
        ImageRejection.DimensionsTooSmall =>
            $"A imagem tem menos de {ImageValidation.MinDimension} px de lado e ficaria ruim impressa.",
        _ => "Não foi possível usar este arquivo."
    };
}
